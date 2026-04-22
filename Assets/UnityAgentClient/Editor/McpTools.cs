using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Read Unity Editor console logs.
    /// </summary>
    public class ReadUnityConsoleTool : IMcpTool
    {
        readonly List<LogEntry> collectedLogs;
        readonly object logLock;

        public string Name => "read_unity_console";
        public string Description => "Retrieve Unity Editor console logs including errors, warnings, and info messages.";
        public bool RequiresMainThread => false;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""maxCount"": { ""type"": ""number"", ""description"": ""Maximum number of logs to retrieve (default: 100)."" }
            }
        }").RootElement;

        public ReadUnityConsoleTool(List<LogEntry> logs, object logLock)
        {
            this.collectedLogs = logs;
            this.logLock = logLock;
        }

        public McpToolResult Execute(JsonElement args)
        {
            int maxCount = 100;
            if (args.TryGetProperty("maxCount", out var mc) && mc.TryGetInt32(out var v))
                maxCount = v;
            if (maxCount <= 0) maxCount = 100;

            List<LogEntry> logs;
            lock (logLock)
            {
                var startIndex = Math.Max(0, collectedLogs.Count - maxCount);
                var count = Math.Min(maxCount, collectedLogs.Count);
                logs = collectedLogs.GetRange(startIndex, count);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Console logs ({logs.Count} entries):");
            sb.AppendLine();
            foreach (var log in logs)
            {
                sb.AppendLine($"[{log.Type.ToUpper()}] {log.Condition}");
            }
            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// List all scenes in the project.
    /// </summary>
    public class ListScenesTool : IMcpTool
    {
        public string Name => "list_scenes";
        public string Description => "List all scenes in the Unity project with their build index and enabled status.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");

            sb.AppendLine("== Build Settings Scenes ==");
            var buildScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                var scene = buildScenes[i];
                sb.AppendLine($"[{i}] {scene.path} (enabled: {scene.enabled})");
            }
            sb.AppendLine();

            sb.AppendLine($"== All Scenes ({sceneGuids.Length}) ==");
            foreach (var guid in sceneGuids)
            {
                sb.AppendLine(AssetDatabase.GUIDToAssetPath(guid));
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Get Unity project settings.
    /// </summary>
    public class GetProjectSettingsTool : IMcpTool
    {
        public string Name => "get_project_settings";
        public string Description => "Get Unity project settings including product name, version, scripting backend, and target platform.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Product Name: {Application.productName}");
            sb.AppendLine($"Company Name: {Application.companyName}");
            sb.AppendLine($"Version: {Application.version}");
            sb.AppendLine($"Unity Version: {Application.unityVersion}");
            sb.AppendLine($"Platform: {Application.platform}");
            sb.AppendLine($"Data Path: {Application.dataPath}");
            sb.AppendLine($"Scripting Backend: {PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine($"API Compatibility: {PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine($"Target Platform: {EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine($"Color Space: {PlayerSettings.colorSpace}");
            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// List assets in the project.
    /// </summary>
    public class ListAssetsTool : IMcpTool
    {
        public string Name => "list_assets";
        public string Description => "List assets in the Unity project filtered by path and type. Uses Unity search syntax for filter (e.g. 't:Script', 't:Prefab').";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""Folder path to search (e.g. 'Assets/Scripts'). Defaults to 'Assets'."" },
                ""filter"": { ""type"": ""string"", ""description"": ""Search filter using Unity syntax (e.g. 't:Script', 't:Prefab')."" },
                ""maxCount"": { ""type"": ""number"", ""description"": ""Maximum results (default: 100)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var searchFolder = "Assets";
            var filter = "";
            var maxCount = 100;

            if (args.TryGetProperty("path", out var p)) searchFolder = p.GetString() ?? "Assets";
            if (args.TryGetProperty("filter", out var f)) filter = f.GetString() ?? "";
            if (args.TryGetProperty("maxCount", out var mc) && mc.TryGetInt32(out var v)) maxCount = v;
            if (maxCount <= 0) maxCount = 100;

            var guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });
            var sb = new StringBuilder();
            sb.AppendLine($"Found {guids.Length} asset(s) in '{searchFolder}':");
            sb.AppendLine();

            var count = Math.Min(maxCount, guids.Length);
            for (int i = 0; i < count; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                sb.AppendLine($"  {path} [{type?.Name ?? "Unknown"}]");
            }
            if (guids.Length > count)
                sb.AppendLine($"  ... and {guids.Length - count} more");

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Get detailed info about a specific asset.
    /// </summary>
    public class ReadAssetInfoTool : IMcpTool
    {
        public string Name => "read_asset_info";
        public string Description => "Get detailed information about a specific Unity asset including type, dependencies, labels, and importer info.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""assetPath"": { ""type"": ""string"", ""description"": ""Asset path relative to project (e.g. 'Assets/Scripts/Player.cs')."" }
            },
            ""required"": [""assetPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (!args.TryGetProperty("assetPath", out var ap))
                return McpToolResult.Error("assetPath is required");

            var assetPath = ap.GetString();
            if (string.IsNullOrEmpty(assetPath))
                return McpToolResult.Error("assetPath is required");

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return McpToolResult.Error($"Asset not found: {assetPath}");

            var sb = new StringBuilder();
            sb.AppendLine($"Path: {assetPath}");
            sb.AppendLine($"Name: {asset.name}");
            sb.AppendLine($"Type: {asset.GetType().FullName}");
            sb.AppendLine($"GUID: {AssetDatabase.AssetPathToGUID(assetPath)}");

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer != null)
                sb.AppendLine($"Importer: {importer.GetType().Name}");

            var deps = AssetDatabase.GetDependencies(assetPath, false);
            if (deps.Length > 1)
            {
                sb.AppendLine();
                sb.AppendLine($"Dependencies ({deps.Length - 1}):");
                foreach (var dep in deps)
                    if (dep != assetPath) sb.AppendLine($"  {dep}");
            }

            var labels = AssetDatabase.GetLabels(asset);
            if (labels.Length > 0)
                sb.AppendLine($"\nLabels: {string.Join(", ", labels)}");

            return McpToolResult.Success(sb.ToString());
        }
    }

    public class LogEntry
    {
        public string Condition { get; set; }
        public string StackTrace { get; set; }
        public string Type { get; set; }
    }
}
