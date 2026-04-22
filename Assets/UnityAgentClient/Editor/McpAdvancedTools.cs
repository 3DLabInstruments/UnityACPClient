using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Find all GameObjects with a specific component type.
    /// </summary>
    public class GetComponentsByTypeTool : IMcpTool
    {
        public string Name => "scene_get_components_by_type";
        public string Description => "Find all GameObjects in the active scene that have a specific component type (e.g. 'Rigidbody', 'AudioSource', 'Light', or custom scripts).";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""componentType"": { ""type"": ""string"", ""description"": ""Component type name (e.g. 'Rigidbody', 'Light', 'Camera', 'MyScript')."" },
                ""includeInactive"": { ""type"": ""boolean"", ""description"": ""Include inactive GameObjects (default: true)."" }
            },
            ""required"": [""componentType""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var typeName = args.GetProperty("componentType").GetString();
            bool includeInactive = true;
            if (args.TryGetProperty("includeInactive", out var ia)) includeInactive = ia.GetBoolean();

            var type = FindType(typeName);
            if (type == null)
                return McpToolResult.Error($"Component type not found: {typeName}");

#if UNITY_2023_1_OR_NEWER
            var components = UnityEngine.Object.FindObjectsByType(type, includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude, UnityEngine.FindObjectsSortMode.None) as Component[];
#else
            var components = UnityEngine.Object.FindObjectsOfType(type, includeInactive) as Component[];
#endif
            if (components == null || components.Length == 0)
                return McpToolResult.Success($"No GameObjects found with component '{typeName}'");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {components.Length} GameObject(s) with '{typeName}':");
            sb.AppendLine();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                var go = comp.gameObject;
                var path = GetPath(go);
                sb.AppendLine($"  {path} (active: {go.activeInHierarchy})");
            }

            return McpToolResult.Success(sb.ToString());
        }

        static string GetPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null) { path = parent.name + "/" + path; parent = parent.parent; }
            return path;
        }

        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetTypes().FirstOrDefault(t =>
                        t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        typeof(Component).IsAssignableFrom(t));
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Set a GameObject active or inactive.
    /// </summary>
    public class SetActiveTool : IMcpTool
    {
        public string Name => "scene_set_active";
        public string Description => "Set a GameObject active or inactive in the scene.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""active"": { ""type"": ""boolean"", ""description"": ""true to activate, false to deactivate."" }
            },
            ""required"": [""gameObjectPath"", ""active""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var active = args.GetProperty("active").GetBoolean();

            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            Undo.RecordObject(go, $"Set {go.name} active={active}");
            go.SetActive(active);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Set '{go.name}' active = {active}");
        }
    }

    /// <summary>
    /// Capture a screenshot of the Scene or Game view.
    /// </summary>
    public class ScreenshotTool : IMcpTool
    {
        public string Name => "editor_screenshot";
        public string Description => "Capture a screenshot of the Game view and save to a file. Returns the file path.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""outputPath"": { ""type"": ""string"", ""description"": ""Output file path (e.g. 'Screenshots/capture.png'). Defaults to 'Temp/screenshot.png'."" },
                ""superSize"": { ""type"": ""number"", ""description"": ""Resolution multiplier (1-4, default: 1)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var outputPath = "Temp/screenshot.png";
            int superSize = 1;

            if (args.ValueKind != JsonValueKind.Undefined)
            {
                if (args.TryGetProperty("outputPath", out var op)) outputPath = op.GetString();
                if (args.TryGetProperty("superSize", out var ss) && ss.TryGetInt32(out var s)) superSize = Math.Clamp(s, 1, 4);
            }

            var dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            ScreenCapture.CaptureScreenshot(outputPath, superSize);
            var fullPath = System.IO.Path.GetFullPath(outputPath);
            return McpToolResult.Success($"Screenshot saved to: {fullPath}\n(Note: screenshot is captured on the next frame render)");
        }
    }

    /// <summary>
    /// Get filtered console log entries (errors/warnings only).
    /// </summary>
    public class GetConsoleErrorsTool : IMcpTool
    {
        readonly System.Collections.Generic.List<LogEntry> collectedLogs;
        readonly object logLock;

        public string Name => "editor_get_console_errors_only";
        public string Description => "Get only error and warning entries from the Unity console. Useful for quickly identifying issues.";
        public bool RequiresMainThread => false;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""maxCount"": { ""type"": ""number"", ""description"": ""Maximum entries to return (default: 50)."" },
                ""includeWarnings"": { ""type"": ""boolean"", ""description"": ""Include warnings in addition to errors (default: true)."" }
            }
        }").RootElement;

        public GetConsoleErrorsTool(System.Collections.Generic.List<LogEntry> logs, object logLock)
        {
            this.collectedLogs = logs;
            this.logLock = logLock;
        }

        public McpToolResult Execute(JsonElement args)
        {
            int maxCount = 50;
            bool includeWarnings = true;

            if (args.ValueKind != JsonValueKind.Undefined)
            {
                if (args.TryGetProperty("maxCount", out var mc) && mc.TryGetInt32(out var v)) maxCount = v;
                if (args.TryGetProperty("includeWarnings", out var iw)) includeWarnings = iw.GetBoolean();
            }

            System.Collections.Generic.List<LogEntry> filtered;
            lock (logLock)
            {
                filtered = collectedLogs
                    .Where(l => l.Type == "Error" || l.Type == "Exception" || l.Type == "Assert" ||
                               (includeWarnings && l.Type == "Warning"))
                    .TakeLast(maxCount)
                    .ToList();
            }

            if (filtered.Count == 0)
                return McpToolResult.Success("No errors or warnings in console.");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {filtered.Count} error/warning entries:");
            sb.AppendLine();
            foreach (var log in filtered)
            {
                sb.AppendLine($"[{log.Type.ToUpper()}] {log.Condition}");
                if (!string.IsNullOrEmpty(log.StackTrace))
                    sb.AppendLine($"  {log.StackTrace.Split('\n').FirstOrDefault()?.Trim()}");
            }
            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Create a folder in the project.
    /// </summary>
    public class AssetCreateFolderTool : IMcpTool
    {
        public string Name => "asset_create_folder";
        public string Description => "Create a new folder in the Unity project. Creates parent folders if needed.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""Folder path to create (e.g. 'Assets/NewFolder/SubFolder')."" }
            },
            ""required"": [""path""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var folderPath = args.GetProperty("path").GetString();

            if (AssetDatabase.IsValidFolder(folderPath))
                return McpToolResult.Success($"Folder already exists: {folderPath}");

            var parts = folderPath.Replace("\\", "/").Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }

            return McpToolResult.Success($"Created folder: {folderPath}");
        }
    }

    /// <summary>
    /// Create a new material asset.
    /// </summary>
    public class AssetCreateMaterialTool : IMcpTool
    {
        public string Name => "asset_create_material";
        public string Description => "Create a new material asset with a specified shader.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""Output path (e.g. 'Assets/Materials/NewMat.mat')."" },
                ""shader"": { ""type"": ""string"", ""description"": ""Shader name (e.g. 'Standard', 'Universal Render Pipeline/Lit'). Defaults to 'Standard'."" },
                ""color"": { ""type"": ""string"", ""description"": ""Optional main color as 'r,g,b,a' (e.g. '1,0,0,1' for red)."" }
            },
            ""required"": [""path""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("path").GetString();
            if (!path.EndsWith(".mat")) path += ".mat";

            var shaderName = "Standard";
            if (args.TryGetProperty("shader", out var s)) shaderName = s.GetString();

            var shader = Shader.Find(shaderName);
            if (shader == null)
                return McpToolResult.Error($"Shader not found: {shaderName}");

            var mat = new Material(shader);

            if (args.TryGetProperty("color", out var c))
            {
                var parts = c.GetString().Split(',').Select(float.Parse).ToArray();
                mat.color = new Color(parts[0], parts[1], parts.Length > 2 ? parts[2] : 0, parts.Length > 3 ? parts[3] : 1);
            }

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var dirParts = dir.Replace("\\", "/").Split('/');
                var current = dirParts[0];
                for (int i = 1; i < dirParts.Length; i++)
                {
                    var next = current + "/" + dirParts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, dirParts[i]);
                    current = next;
                }
            }

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.Refresh();
            return McpToolResult.Success($"Created material: {path} (shader: {shaderName})");
        }
    }
}
