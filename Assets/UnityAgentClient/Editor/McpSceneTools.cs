using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAgentClient
{
    /// <summary>
    /// Get the full hierarchy of GameObjects in the active scene.
    /// </summary>
    public class GetHierarchyTool : IMcpTool
    {
        public string Name => "scene_get_hierarchy";
        public string Description => "Get the full GameObject hierarchy of the active scene. Shows names, active state, and component types for each object.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""maxDepth"": { ""type"": ""number"", ""description"": ""Maximum depth to traverse (default: 10)."" },
                ""includeInactive"": { ""type"": ""boolean"", ""description"": ""Include inactive GameObjects (default: true)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            int maxDepth = 10;
            bool includeInactive = true;

            if (args.ValueKind != JsonValueKind.Undefined)
            {
                if (args.TryGetProperty("maxDepth", out var md) && md.TryGetInt32(out var d)) maxDepth = d;
                if (args.TryGetProperty("includeInactive", out var ia)) includeInactive = ia.GetBoolean();
            }

            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine($"Scene: {scene.name} (path: {scene.path})");
            sb.AppendLine($"Root GameObjects: {scene.rootCount}");
            sb.AppendLine();

            foreach (var root in scene.GetRootGameObjects())
            {
                PrintGameObject(sb, root, 0, maxDepth, includeInactive);
            }

            return McpToolResult.Success(sb.ToString());
        }

        static void PrintGameObject(StringBuilder sb, GameObject go, int depth, int maxDepth, bool includeInactive)
        {
            if (!includeInactive && !go.activeInHierarchy) return;
            if (depth > maxDepth) return;

            var indent = new string(' ', depth * 2);
            var activeFlag = go.activeSelf ? "" : " [inactive]";
            var components = go.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name);
            var componentStr = string.Join(", ", components);

            sb.AppendLine($"{indent}{go.name}{activeFlag} ({componentStr})");

            for (int i = 0; i < go.transform.childCount; i++)
            {
                PrintGameObject(sb, go.transform.GetChild(i).gameObject, depth + 1, maxDepth, includeInactive);
            }
        }
    }

    /// <summary>
    /// Get detailed component data for a specific GameObject.
    /// </summary>
    public class GetComponentDataTool : IMcpTool
    {
        public string Name => "scene_get_component_data";
        public string Description => "Get all component data (serialized properties) for a GameObject found by name or path. Returns property names, types, and values.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Full path to the GameObject (e.g. 'Canvas/Panel/Button') or just the name."" },
                ""componentType"": { ""type"": ""string"", ""description"": ""Optional: only show this component type (e.g. 'Transform', 'Rigidbody')."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (!args.TryGetProperty("gameObjectPath", out var pathProp))
                return McpToolResult.Error("gameObjectPath is required");

            var path = pathProp.GetString();
            string componentFilter = null;
            if (args.TryGetProperty("componentType", out var ct))
                componentFilter = ct.GetString();

            var go = GameObject.Find(path);
            if (go == null)
            {
                // Try searching all objects by name
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            }

            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {go.name}");
            sb.AppendLine($"Path: {GetGameObjectPath(go)}");
            sb.AppendLine($"Active: {go.activeSelf} (in hierarchy: {go.activeInHierarchy})");
            sb.AppendLine($"Layer: {LayerMask.LayerToName(go.layer)} ({go.layer})");
            sb.AppendLine($"Tag: {go.tag}");
            sb.AppendLine($"Static: {go.isStatic}");
            sb.AppendLine();

            var components = go.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;
                var typeName = component.GetType().Name;
                if (componentFilter != null && !typeName.Equals(componentFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.AppendLine($"── {typeName} ──");

                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                if (prop.NextVisible(true))
                {
                    do
                    {
                        sb.AppendLine($"  {prop.displayName} ({prop.propertyType}): {GetPropertyValue(prop)}");
                    } while (prop.NextVisible(false));
                }
                sb.AppendLine();
            }

            return McpToolResult.Success(sb.ToString());
        }

        static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        static string GetPropertyValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Boolean => prop.boolValue.ToString(),
                SerializedPropertyType.Float => prop.floatValue.ToString("F4"),
                SerializedPropertyType.String => $"\"{prop.stringValue}\"",
                SerializedPropertyType.Color => prop.colorValue.ToString(),
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})"
                    : "null",
                SerializedPropertyType.Enum => prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString(),
                SerializedPropertyType.Vector2 => prop.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                SerializedPropertyType.Vector4 => prop.vector4Value.ToString(),
                SerializedPropertyType.Rect => prop.rectValue.ToString(),
                SerializedPropertyType.Bounds => prop.boundsValue.ToString(),
                SerializedPropertyType.Quaternion => prop.quaternionValue.eulerAngles.ToString(),
                SerializedPropertyType.LayerMask => prop.intValue.ToString(),
                _ => $"({prop.propertyType})"
            };
        }
    }

    /// <summary>
    /// Modify a component property on a GameObject.
    /// </summary>
    public class ModifyComponentTool : IMcpTool
    {
        public string Name => "scene_modify_component";
        public string Description => "Modify a serialized property value on a component of a GameObject. Supports int, float, bool, string, Vector3, Color values.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""componentType"": { ""type"": ""string"", ""description"": ""Component type name (e.g. 'Transform', 'Rigidbody')."" },
                ""propertyName"": { ""type"": ""string"", ""description"": ""Property name to modify (use the serialized name, e.g. 'm_LocalPosition')."" },
                ""value"": { ""type"": ""string"", ""description"": ""New value as string. For Vector3: '1,2,3'. For Color: '1,0,0,1'. For bool: 'true'/'false'."" }
            },
            ""required"": [""gameObjectPath"", ""componentType"", ""propertyName"", ""value""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var compType = args.GetProperty("componentType").GetString();
            var propName = args.GetProperty("propertyName").GetString();
            var valueStr = args.GetProperty("value").GetString();

            var go = GameObject.Find(goPath);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            var component = go.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().Name.Equals(compType, StringComparison.OrdinalIgnoreCase));
            if (component == null)
                return McpToolResult.Error($"Component '{compType}' not found on {goPath}");

            Undo.RecordObject(component, $"Modify {goPath}.{compType}.{propName}");
            var so = new SerializedObject(component);
            var prop = so.FindProperty(propName);
            if (prop == null)
                return McpToolResult.Error($"Property '{propName}' not found on {compType}");

            try
            {
                SetPropertyValue(prop, valueStr);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(component);
                EditorSceneManager.MarkSceneDirty(go.scene);

                return McpToolResult.Success($"Set {goPath}.{compType}.{propName} = {valueStr}");
            }
            catch (Exception e)
            {
                return McpToolResult.Error($"Failed to set property: {e.Message}");
            }
        }

        static void SetPropertyValue(SerializedProperty prop, string value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = int.Parse(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = bool.Parse(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = float.Parse(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value;
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = value.Split(',').Select(float.Parse).ToArray();
                    prop.vector2Value = new Vector2(v2[0], v2[1]);
                    break;
                case SerializedPropertyType.Vector3:
                    var v3 = value.Split(',').Select(float.Parse).ToArray();
                    prop.vector3Value = new Vector3(v3[0], v3[1], v3[2]);
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = value.Split(',').Select(float.Parse).ToArray();
                    prop.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]);
                    break;
                case SerializedPropertyType.Color:
                    var c = value.Split(',').Select(float.Parse).ToArray();
                    prop.colorValue = new Color(c[0], c[1], c.Length > 2 ? c[2] : 0, c.Length > 3 ? c[3] : 1);
                    break;
                case SerializedPropertyType.Enum:
                    if (int.TryParse(value, out var enumIdx))
                        prop.enumValueIndex = enumIdx;
                    else
                    {
                        var idx = Array.IndexOf(prop.enumDisplayNames, value);
                        if (idx >= 0) prop.enumValueIndex = idx;
                        else throw new ArgumentException($"Invalid enum value: {value}. Options: {string.Join(", ", prop.enumDisplayNames)}");
                    }
                    break;
                default:
                    throw new NotSupportedException($"Property type {prop.propertyType} is not supported for modification");
            }
        }
    }

    /// <summary>
    /// Add a new GameObject to the scene.
    /// </summary>
    public class AddGameObjectTool : IMcpTool
    {
        public string Name => "scene_add_gameobject";
        public string Description => "Create a new GameObject in the active scene. Optionally set parent, position, color, scale, and add components.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"", ""description"": ""Name for the new GameObject."" },
                ""parentPath"": { ""type"": ""string"", ""description"": ""Optional parent GameObject path. If omitted, created at scene root."" },
                ""primitiveType"": { ""type"": ""string"", ""description"": ""Optional primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad."" },
                ""position"": { ""type"": ""string"", ""description"": ""Optional world position as 'x,y,z' (default: 0,0,0)."" },
                ""scale"": { ""type"": ""string"", ""description"": ""Optional local scale as 'x,y,z' (default: 1,1,1)."" },
                ""color"": { ""type"": ""string"", ""description"": ""Optional color as 'r,g,b,a' (e.g. '0,0,1,1' for blue). Creates and assigns a material automatically."" },
                ""components"": { ""type"": ""string"", ""description"": ""Optional comma-separated component types to add (e.g. 'Rigidbody,BoxCollider')."" }
            },
            ""required"": [""name""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var name = args.GetProperty("name").GetString();

            GameObject go;
            if (args.TryGetProperty("primitiveType", out var pt))
            {
                if (Enum.TryParse<PrimitiveType>(pt.GetString(), true, out var pType))
                    go = GameObject.CreatePrimitive(pType);
                else
                    return McpToolResult.Error($"Invalid primitive type: {pt.GetString()}. Use: Cube, Sphere, Capsule, Cylinder, Plane, Quad");
            }
            else
            {
                go = new GameObject();
            }

            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            if (args.TryGetProperty("parentPath", out var pp))
            {
                var parent = GameObject.Find(pp.GetString());
                if (parent != null)
                    go.transform.SetParent(parent.transform, false);
            }

            if (args.TryGetProperty("position", out var pos))
            {
                var parts = pos.GetString().Split(',').Select(float.Parse).ToArray();
                go.transform.position = new Vector3(parts[0], parts[1], parts[2]);
            }

            if (args.TryGetProperty("scale", out var scl))
            {
                var s = scl.GetString().Split(',').Select(float.Parse).ToArray();
                go.transform.localScale = new Vector3(s[0], s[1], s[2]);
            }

            if (args.TryGetProperty("color", out var colorProp))
            {
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var parts = colorProp.GetString().Split(',').Select(float.Parse).ToArray();
                    var color = new Color(parts[0], parts[1],
                        parts.Length > 2 ? parts[2] : 0,
                        parts.Length > 3 ? parts[3] : 1);

                    var srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                    Shader shader = srp != null
                        ? (srp.defaultShader ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                        : Shader.Find("Standard");

                    var mat = new Material(shader) { color = color };
                    renderer.sharedMaterial = mat;
                }
            }

            if (args.TryGetProperty("components", out var comps))
            {
                var sb = new StringBuilder();
                foreach (var compName in comps.GetString().Split(',').Select(s => s.Trim()))
                {
                    var type = FindComponentType(compName);
                    if (type != null)
                    {
                        go.AddComponent(type);
                    }
                    else
                    {
                        sb.AppendLine($"Warning: Component type '{compName}' not found");
                    }
                }

                if (sb.Length > 0)
                {
                    EditorSceneManager.MarkSceneDirty(go.scene);
                    return McpToolResult.Success($"Created '{name}' with warnings:\n{sb}");
                }
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Created GameObject '{name}' at {go.transform.position}");
        }

        static Type FindComponentType(string name)
        {
            // Search Unity built-in components
            var unityType = typeof(Component).Assembly.GetTypes()
                .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && typeof(Component).IsAssignableFrom(t));
            if (unityType != null) return unityType;

            // Search all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetTypes()
                        .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && typeof(Component).IsAssignableFrom(t));
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Delete a GameObject from the scene.
    /// </summary>
    public class DeleteGameObjectTool : IMcpTool
    {
        public string Name => "scene_delete_gameobject";
        public string Description => "Delete a GameObject from the active scene by name or path. Supports undo.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject to delete."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            var scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
            return McpToolResult.Success($"Deleted GameObject '{path}'");
        }
    }

    /// <summary>
    /// Add a component to an existing GameObject.
    /// </summary>
    public class AddComponentTool : IMcpTool
    {
        public string Name => "scene_add_component";
        public string Description => "Add one or more components to an existing GameObject by name. Supports any built-in or custom component type.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the target GameObject."" },
                ""components"": { ""type"": ""string"", ""description"": ""Comma-separated component types to add (e.g. 'Rigidbody,BoxCollider,AudioSource')."" }
            },
            ""required"": [""gameObjectPath"", ""components""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var componentNames = args.GetProperty("components").GetString();

            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            var added = new List<string>();
            var failed = new List<string>();

            foreach (var compName in componentNames.Split(',').Select(s => s.Trim()))
            {
                if (string.IsNullOrEmpty(compName)) continue;

                var type = FindComponentType(compName);
                if (type == null)
                {
                    failed.Add(compName);
                    continue;
                }

                // Check if component already exists (for non-multiple components)
                if (go.GetComponent(type) != null && !typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    failed.Add($"{compName} (already exists)");
                    continue;
                }

                Undo.AddComponent(go, type);
                added.Add(compName);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            var sb = new StringBuilder();
            if (added.Count > 0)
                sb.AppendLine($"Added to '{go.name}': {string.Join(", ", added)}");
            if (failed.Count > 0)
                sb.AppendLine($"Failed: {string.Join(", ", failed)}");

            return added.Count > 0
                ? McpToolResult.Success(sb.ToString())
                : McpToolResult.Error(sb.ToString());
        }

        static Type FindComponentType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetTypes().FirstOrDefault(t =>
                        t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        typeof(Component).IsAssignableFrom(t) && !t.IsAbstract);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Remove a component from a GameObject.
    /// </summary>
    public class RemoveComponentTool : IMcpTool
    {
        public string Name => "scene_remove_component";
        public string Description => "Remove a component from a GameObject by type name. Cannot remove Transform. Supports undo.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Dangerous;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the target GameObject."" },
                ""componentType"": { ""type"": ""string"", ""description"": ""Component type name to remove (e.g. 'Rigidbody', 'BoxCollider')."" }
            },
            ""required"": [""gameObjectPath"", ""componentType""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var compType = args.GetProperty("componentType").GetString();

            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            if (compType.Equals("Transform", StringComparison.OrdinalIgnoreCase))
                return McpToolResult.Error("Cannot remove Transform component");

            var component = go.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                    c.GetType().Name.Equals(compType, StringComparison.OrdinalIgnoreCase));

            if (component == null)
                return McpToolResult.Error($"Component '{compType}' not found on '{go.name}'");

            Undo.DestroyObjectImmediate(component);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Removed '{compType}' from '{go.name}'");
        }
    }

    /// <summary>
    /// Set position, rotation, and/or scale of a GameObject's Transform in one call.
    /// Higher-level than modify_component — uses friendly parameter names instead of serialized property names.
    /// </summary>
    public class SetTransformTool : IMcpTool
    {
        public string Name => "scene_set_transform";
        public string Description => "Set the position, rotation, and/or scale of a GameObject. More convenient than modify_component for Transform changes.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""position"": { ""type"": ""string"", ""description"": ""World position as 'x,y,z' (e.g. '0,3,5')."" },
                ""rotation"": { ""type"": ""string"", ""description"": ""Euler rotation as 'x,y,z' (e.g. '0,90,0')."" },
                ""scale"": { ""type"": ""string"", ""description"": ""Local scale as 'x,y,z' (e.g. '2,2,2')."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();

            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            Undo.RecordObject(go.transform, $"Set Transform on {go.name}");

            var changes = new List<string>();

            if (args.TryGetProperty("position", out var pos))
            {
                var p = pos.GetString().Split(',').Select(float.Parse).ToArray();
                go.transform.position = new Vector3(p[0], p[1], p[2]);
                changes.Add($"position={go.transform.position}");
            }

            if (args.TryGetProperty("rotation", out var rot))
            {
                var r = rot.GetString().Split(',').Select(float.Parse).ToArray();
                go.transform.eulerAngles = new Vector3(r[0], r[1], r[2]);
                changes.Add($"rotation={go.transform.eulerAngles}");
            }

            if (args.TryGetProperty("scale", out var scl))
            {
                var s = scl.GetString().Split(',').Select(float.Parse).ToArray();
                go.transform.localScale = new Vector3(s[0], s[1], s[2]);
                changes.Add($"scale={go.transform.localScale}");
            }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one of: position, rotation, scale");

            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Transform '{go.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Shared helper methods for scene tools.
    /// </summary>
    internal static class SceneToolHelpers
    {
        /// <summary>
        /// Find a GameObject by name, including inactive objects.
        /// Uses FindObjectsByType (Unity 2023.1+) with fallback.
        /// </summary>
        public static GameObject FindGameObjectIncludeInactive(string name)
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(g => g.name == name);
#else
            return SceneToolHelpers.FindGameObjectIncludeInactive(name);
#endif
        }

        /// <summary>
        /// Find all objects of a type, including inactive.
        /// </summary>
        public static T[] FindAllIncludeInactive<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(true);
#endif
        }

        /// <summary>
        /// Find all objects of a type, active only.
        /// </summary>
        public static T[] FindAllActiveOnly<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(false);
#endif
        }
    }
}
