using System;
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
    /// Save the active scene(s).
    /// </summary>
    public class SaveSceneTool : IMcpTool
    {
        public string Name => "scene_save";
        public string Description => "Save the current active scene or all open scenes.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""all"": { ""type"": ""boolean"", ""description"": ""Save all open scenes instead of just the active one (default: false)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            bool all = false;
            if (args.ValueKind != JsonValueKind.Undefined &&
                args.TryGetProperty("all", out var a))
                all = a.GetBoolean();

            if (all)
            {
                EditorSceneManager.SaveOpenScenes();
                return McpToolResult.Success("Saved all open scenes");
            }
            else
            {
                var scene = SceneManager.GetActiveScene();
                EditorSceneManager.SaveScene(scene);
                return McpToolResult.Success($"Saved scene: {scene.name} ({scene.path})");
            }
        }
    }

    /// <summary>
    /// Set the editor selection to specific GameObjects.
    /// </summary>
    public class SetSelectionTool : IMcpTool
    {
        public string Name => "scene_set_selection";
        public string Description => "Set the editor selection to one or more GameObjects by name or path. The Inspector will show the selected object(s).";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPaths"": { ""type"": ""string"", ""description"": ""Comma-separated list of GameObject names or paths to select."" }
            },
            ""required"": [""gameObjectPaths""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var paths = args.GetProperty("gameObjectPaths").GetString()
                .Split(',').Select(s => s.Trim()).ToArray();

            var selected = new System.Collections.Generic.List<UnityEngine.Object>();
            var notFound = new System.Collections.Generic.List<string>();

            foreach (var path in paths)
            {
                var go = GameObject.Find(path);
                if (go == null)
                    go = SceneToolHelpers.FindGameObjectIncludeInactive(path);

                if (go != null)
                    selected.Add(go);
                else
                    notFound.Add(path);
            }

            Selection.objects = selected.ToArray();

            if (notFound.Count > 0)
                return McpToolResult.Success($"Selected {selected.Count} object(s). Not found: {string.Join(", ", notFound)}");

            return McpToolResult.Success($"Selected {selected.Count} object(s): {string.Join(", ", selected.Select(o => o.name))}");
        }
    }

    /// <summary>
    /// Reparent a GameObject under a different parent.
    /// </summary>
    public class ReparentGameObjectTool : IMcpTool
    {
        public string Name => "scene_reparent";
        public string Description => "Move a GameObject under a different parent, or to the scene root.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject to move."" },
                ""newParentPath"": { ""type"": ""string"", ""description"": ""Path or name of the new parent. Leave empty or 'root' to move to scene root."" },
                ""worldPositionStays"": { ""type"": ""boolean"", ""description"": ""Keep world position when reparenting (default: true)."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var go = FindGameObject(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            bool worldPositionStays = true;
            if (args.TryGetProperty("worldPositionStays", out var wps))
                worldPositionStays = wps.GetBoolean();

            Undo.SetTransformParent(go.transform, null, $"Reparent {go.name}");

            if (args.TryGetProperty("newParentPath", out var pp))
            {
                var parentPath = pp.GetString();
                if (!string.IsNullOrEmpty(parentPath) && parentPath != "root")
                {
                    var newParent = FindGameObject(parentPath);
                    if (newParent == null)
                        return McpToolResult.Error($"Parent not found: {parentPath}");

                    Undo.SetTransformParent(go.transform, newParent.transform, $"Reparent {go.name}");
                    go.transform.SetParent(newParent.transform, worldPositionStays);
                    EditorSceneManager.MarkSceneDirty(go.scene);
                    return McpToolResult.Success($"Moved '{go.name}' under '{newParent.name}'");
                }
            }

            go.transform.SetParent(null, worldPositionStays);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Moved '{go.name}' to scene root");
        }

        static GameObject FindGameObject(string path)
        {
            var go = GameObject.Find(path);
            if (go != null) return go;
            return SceneToolHelpers.FindGameObjectIncludeInactive(path);
        }
    }

    /// <summary>
    /// Duplicate a GameObject.
    /// </summary>
    public class DuplicateGameObjectTool : IMcpTool
    {
        public string Name => "scene_duplicate";
        public string Description => "Duplicate a GameObject in the scene (including all children and components). Supports Undo.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject to duplicate."" },
                ""newName"": { ""type"": ""string"", ""description"": ""Optional name for the duplicate. Defaults to 'OriginalName (1)'."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(goPath);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            var duplicate = UnityEngine.Object.Instantiate(go, go.transform.parent);
            Undo.RegisterCreatedObjectUndo(duplicate, $"Duplicate {go.name}");

            if (args.TryGetProperty("newName", out var nn))
                duplicate.name = nn.GetString();

            EditorSceneManager.MarkSceneDirty(duplicate.scene);
            return McpToolResult.Success($"Duplicated '{go.name}' as '{duplicate.name}'");
        }
    }

    /// <summary>
    /// Rename a GameObject in the scene.
    /// </summary>
    public class RenameGameObjectTool : IMcpTool
    {
        public string Name => "scene_rename_gameobject";
        public string Description => "Rename a GameObject in the active scene.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Current path or name of the GameObject."" },
                ""newName"": { ""type"": ""string"", ""description"": ""New name for the GameObject."" }
            },
            ""required"": [""gameObjectPath"", ""newName""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var newName = args.GetProperty("newName").GetString();

            if (string.IsNullOrWhiteSpace(newName))
                return McpToolResult.Error("'newName' must not be empty.");

            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            var oldName = go.name;
            Undo.RecordObject(go, $"Rename {oldName} to {newName}");
            go.name = newName;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Renamed '{oldName}' to '{newName}'");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Semantic Scene Tools — high-level, intent-oriented actions
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Find GameObjects by tag, layer, component type, or name pattern.
    /// Returns full hierarchy paths for unambiguous identification.
    /// </summary>
    public class FindByCriteriaTool : IMcpTool
    {
        public string Name => "scene_find_by_criteria";
        public string Description =>
            "Find GameObjects matching criteria: tag, layer, component type, or name pattern. " +
            "Returns full hierarchy paths for unambiguous follow-up operations.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""tag"": { ""type"": ""string"", ""description"": ""Filter by tag (e.g. 'Player', 'Enemy')."" },
                ""layer"": { ""type"": ""string"", ""description"": ""Filter by layer name (e.g. 'UI', 'Water')."" },
                ""componentType"": { ""type"": ""string"", ""description"": ""Filter by component type name (e.g. 'Camera', 'Light', 'Rigidbody', 'ParticleSystem')."" },
                ""namePattern"": { ""type"": ""string"", ""description"": ""Filter by name containing this substring (case-insensitive)."" },
                ""includeInactive"": { ""type"": ""boolean"", ""description"": ""Include inactive GameObjects (default: true)."" },
                ""maxResults"": { ""type"": ""integer"", ""description"": ""Maximum number of results (default: 50)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            bool includeInactive = true;
            if (args.TryGetProperty("includeInactive", out var ia)) includeInactive = ia.GetBoolean();

            int maxResults = 50;
            if (args.TryGetProperty("maxResults", out var mr)) maxResults = mr.GetInt32();

            string tag = null, layer = null, componentType = null, namePattern = null;
            if (args.TryGetProperty("tag", out var t)) tag = t.GetString();
            if (args.TryGetProperty("layer", out var l)) layer = l.GetString();
            if (args.TryGetProperty("componentType", out var ct)) componentType = ct.GetString();
            if (args.TryGetProperty("namePattern", out var np)) namePattern = np.GetString()?.ToLowerInvariant();

            if (tag == null && layer == null && componentType == null && namePattern == null)
                return McpToolResult.Error("Provide at least one filter: tag, layer, componentType, or namePattern.");

            var allObjects = includeInactive
                ? SceneToolHelpers.FindAllIncludeInactive<GameObject>()
                : SceneToolHelpers.FindAllActiveOnly<GameObject>();

            int layerIndex = -1;
            if (layer != null)
            {
                layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex < 0)
                    return McpToolResult.Error($"Layer not found: {layer}");
            }

            Type resolvedComponentType = null;
            if (componentType != null)
            {
                resolvedComponentType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(tp => tp.Name.Equals(componentType, StringComparison.OrdinalIgnoreCase)
                        && typeof(Component).IsAssignableFrom(tp));
                if (resolvedComponentType == null)
                    return McpToolResult.Error($"Component type not found: {componentType}");
            }

            var results = new System.Collections.Generic.List<string>();
            foreach (var go in allObjects)
            {
                if (tag != null && !go.CompareTag(tag)) continue;
                if (layerIndex >= 0 && go.layer != layerIndex) continue;
                if (resolvedComponentType != null && go.GetComponent(resolvedComponentType) == null) continue;
                if (namePattern != null && !go.name.ToLowerInvariant().Contains(namePattern)) continue;

                results.Add(GetHierarchyPath(go));
                if (results.Count >= maxResults) break;
            }

            if (results.Count == 0)
                return McpToolResult.Success("No matching GameObjects found.");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} matching GameObject(s):");
            foreach (var path in results)
                sb.AppendLine($"  {path}");
            return McpToolResult.Success(sb.ToString());
        }

        static string GetHierarchyPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return "/" + path;
        }
    }

    /// <summary>
    /// Get a structured summary of the active scene.
    /// </summary>
    public class SceneDescribeTool : IMcpTool
    {
        public string Name => "scene_describe";
        public string Description =>
            "Get a high-level summary of the active scene: object count, cameras, lights, " +
            "key component types, bounding volume, and root hierarchy.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var scene = SceneManager.GetActiveScene();
            var allObjects = SceneToolHelpers.FindAllIncludeInactive<GameObject>();

            var sb = new StringBuilder();
            sb.AppendLine($"== Scene: {scene.name} ({scene.path}) ==");
            sb.AppendLine($"Total GameObjects: {allObjects.Length}");

            // Root objects
            var rootObjects = scene.GetRootGameObjects();
            sb.AppendLine($"Root objects ({rootObjects.Length}): {string.Join(", ", rootObjects.Select(r => r.name).Take(20))}");
            if (rootObjects.Length > 20) sb.Append($"  ... and {rootObjects.Length - 20} more");
            sb.AppendLine();

            // Cameras
            var cameras = SceneToolHelpers.FindAllIncludeInactive<Camera>();
            sb.AppendLine($"Cameras ({cameras.Length}):");
            foreach (var cam in cameras)
                sb.AppendLine($"  {cam.gameObject.name} (active: {cam.gameObject.activeInHierarchy}, fov: {cam.fieldOfView:F0})");

            // Lights
            var lights = SceneToolHelpers.FindAllIncludeInactive<Light>();
            sb.AppendLine($"Lights ({lights.Length}):");
            foreach (var light in lights)
                sb.AppendLine($"  {light.gameObject.name}: {light.type}, intensity={light.intensity:F1}, color={light.color}");

            // Component type summary
            var typeCounts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var go in allObjects)
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;
                    if (typeName == "Transform") continue;
                    typeCounts.TryGetValue(typeName, out var count);
                    typeCounts[typeName] = count + 1;
                }
            }
            sb.AppendLine($"Component types ({typeCounts.Count}):");
            foreach (var kv in typeCounts.OrderByDescending(kv => kv.Value).Take(15))
                sb.AppendLine($"  {kv.Key}: {kv.Value}");

            // Bounding box
            var renderers = SceneToolHelpers.FindAllActiveOnly<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                sb.AppendLine($"Scene bounds: center={bounds.center}, size={bounds.size}");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Place a GameObject on the ground using raycast.
    /// Semantic wrapper over raycast + set_transform.
    /// </summary>
    public class PlaceOnGroundTool : IMcpTool
    {
        public string Name => "scene_place_on_ground";
        public string Description =>
            "Place a GameObject on the ground surface below it (or at a specified position). " +
            "Uses downward raycast to find ground (terrain or mesh collider), then adjusts " +
            "the object's Y position so its bottom sits on the surface.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject to place."" },
                ""position"": { ""type"": ""string"", ""description"": ""Optional world position 'x,y,z' to raycast from. If omitted, uses the object's current position."" },
                ""offsetY"": { ""type"": ""number"", ""description"": ""Extra Y offset above ground (default: 0)."" },
                ""maxDistance"": { ""type"": ""number"", ""description"": ""Maximum raycast distance downward (default: 1000)."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(goPath) ?? SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            float offsetY = 0f;
            if (args.TryGetProperty("offsetY", out var oy) && oy.TryGetSingle(out var off)) offsetY = off;

            float maxDistance = 1000f;
            if (args.TryGetProperty("maxDistance", out var md) && md.TryGetSingle(out var dist)) maxDistance = dist;

            // Determine raycast origin
            Vector3 origin = go.transform.position + Vector3.up * 50f;
            if (args.TryGetProperty("position", out var posElem))
            {
                var parts = posElem.GetString().Split(',').Select(float.Parse).ToArray();
                origin = new Vector3(parts[0], parts.Length > 1 ? parts[1] + 50f : 50f, parts.Length > 2 ? parts[2] : 0f);
            }

            // Temporarily disable colliders on the object to avoid self-hit
            var ownColliders = go.GetComponentsInChildren<Collider>();
            var wasEnabled = new bool[ownColliders.Length];
            for (int i = 0; i < ownColliders.Length; i++)
            {
                wasEnabled[i] = ownColliders[i].enabled;
                ownColliders[i].enabled = false;
            }

            bool hit = Physics.Raycast(origin, Vector3.down, out RaycastHit hitInfo, maxDistance);

            // Restore colliders
            for (int i = 0; i < ownColliders.Length; i++)
                ownColliders[i].enabled = wasEnabled[i];

            if (!hit)
                return McpToolResult.Error("No ground surface found below the specified position. Ensure the scene has colliders on ground objects.");

            // Calculate bottom offset from pivot
            float bottomOffset = 0f;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                bottomOffset = go.transform.position.y - renderer.bounds.min.y;

            var newPos = new Vector3(
                args.TryGetProperty("position", out _) ? hitInfo.point.x : go.transform.position.x,
                hitInfo.point.y + bottomOffset + offsetY,
                args.TryGetProperty("position", out _) ? hitInfo.point.z : go.transform.position.z);

            Undo.RecordObject(go.transform, $"Place {go.name} on ground");
            go.transform.position = newPos;
            EditorSceneManager.MarkSceneDirty(go.scene);

            return McpToolResult.Success($"Placed '{go.name}' on ground at {newPos} (surface: {hitInfo.collider.gameObject.name}, normal: {hitInfo.normal})");
        }
    }
}
