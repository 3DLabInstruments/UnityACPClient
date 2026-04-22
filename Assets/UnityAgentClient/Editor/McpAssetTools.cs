using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Find all references to an asset.
    /// </summary>
    public class FindReferencesTool : IMcpTool
    {
        public string Name => "asset_find_references";
        public string Description => "Find all assets that depend on (reference) a given asset. Useful for understanding impact of changing or deleting an asset.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""assetPath"": { ""type"": ""string"", ""description"": ""Path to the asset to find references for (e.g. 'Assets/Materials/Wood.mat')."" }
            },
            ""required"": [""assetPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var assetPath = args.GetProperty("assetPath").GetString();
            var guid = AssetDatabase.AssetPathToGUID(assetPath);

            if (string.IsNullOrEmpty(guid))
                return McpToolResult.Error($"Asset not found: {assetPath}");

            var sb = new StringBuilder();
            sb.AppendLine($"References to: {assetPath}");
            sb.AppendLine($"GUID: {guid}");
            sb.AppendLine();

            // Search all assets for references to this GUID
            var allAssets = AssetDatabase.GetAllAssetPaths();
            int count = 0;

            foreach (var path in allAssets)
            {
                if (path == assetPath) continue;
                if (!path.StartsWith("Assets/")) continue;

                var deps = AssetDatabase.GetDependencies(path, false);
                if (deps.Contains(assetPath))
                {
                    var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                    sb.AppendLine($"  {path} [{type?.Name ?? "Unknown"}]");
                    count++;
                }
            }

            if (count == 0)
                sb.AppendLine("  (no references found)");
            else
                sb.Insert(sb.ToString().IndexOf('\n') + 1, $"Found {count} referencing asset(s):\n");

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Get import settings for an asset.
    /// </summary>
    public class GetImportSettingsTool : IMcpTool
    {
        public string Name => "asset_get_import_settings";
        public string Description => "Get the import settings for an asset (texture settings, model settings, audio settings, etc.).";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""assetPath"": { ""type"": ""string"", ""description"": ""Path to the asset (e.g. 'Assets/Textures/Icon.png')."" }
            },
            ""required"": [""assetPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var assetPath = args.GetProperty("assetPath").GetString();
            var importer = AssetImporter.GetAtPath(assetPath);

            if (importer == null)
                return McpToolResult.Error($"No importer found for: {assetPath}");

            var sb = new StringBuilder();
            sb.AppendLine($"Asset: {assetPath}");
            sb.AppendLine($"Importer: {importer.GetType().Name}");
            sb.AppendLine();

            // Use SerializedObject to read all importer properties
            var so = new SerializedObject(importer);
            var prop = so.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    // Skip internal/script properties
                    if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags") continue;

                    sb.AppendLine($"  {prop.displayName}: {GetSerializedValue(prop)}");
                } while (prop.NextVisible(false));
            }

            return McpToolResult.Success(sb.ToString());
        }

        static string GetSerializedValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Boolean => prop.boolValue.ToString(),
                SerializedPropertyType.Float => prop.floatValue.ToString("F4"),
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Enum => prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString(),
                SerializedPropertyType.Vector2 => prop.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                _ => $"({prop.propertyType})"
            };
        }
    }

    /// <summary>
    /// Create a prefab from a scene GameObject.
    /// </summary>
    public class CreatePrefabTool : IMcpTool
    {
        public string Name => "asset_create_prefab";
        public string Description => "Create a prefab asset from an existing GameObject in the scene.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the source GameObject in the scene."" },
                ""prefabPath"": { ""type"": ""string"", ""description"": ""Where to save the prefab (e.g. 'Assets/Prefabs/Enemy.prefab')."" }
            },
            ""required"": [""gameObjectPath"", ""prefabPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var prefabPath = args.GetProperty("prefabPath").GetString();

            if (!prefabPath.EndsWith(".prefab"))
                prefabPath += ".prefab";

            var go = GameObject.Find(goPath);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(prefabPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out var success);
            if (success)
                return McpToolResult.Success($"Created prefab: {prefabPath}");
            else
                return McpToolResult.Error($"Failed to create prefab at: {prefabPath}");
        }
    }

    /// <summary>
    /// Instantiate a prefab into the scene.
    /// </summary>
    public class InstantiatePrefabTool : IMcpTool
    {
        public string Name => "asset_instantiate_prefab";
        public string Description => "Instantiate a prefab asset into the active scene at a given position.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""prefabPath"": { ""type"": ""string"", ""description"": ""Path to the prefab asset (e.g. 'Assets/Prefabs/Enemy.prefab')."" },
                ""position"": { ""type"": ""string"", ""description"": ""World position as 'x,y,z' (default: 0,0,0)."" },
                ""parentPath"": { ""type"": ""string"", ""description"": ""Optional parent GameObject path."" }
            },
            ""required"": [""prefabPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var prefabPath = args.GetProperty("prefabPath").GetString();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
                return McpToolResult.Error($"Prefab not found: {prefabPath}");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");

            if (args.TryGetProperty("position", out var pos))
            {
                var parts = pos.GetString().Split(',').Select(float.Parse).ToArray();
                instance.transform.position = new Vector3(parts[0], parts[1], parts[2]);
            }

            if (args.TryGetProperty("parentPath", out var pp))
            {
                var parent = GameObject.Find(pp.GetString());
                if (parent != null)
                    instance.transform.SetParent(parent.transform, true);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(instance.scene);
            return McpToolResult.Success($"Instantiated '{prefab.name}' at {instance.transform.position}");
        }
    }
}
