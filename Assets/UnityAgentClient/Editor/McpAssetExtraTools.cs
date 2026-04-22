using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Rename an asset.
    /// </summary>
    public class AssetRenameTool : IMcpTool
    {
        public string Name => "asset_rename";
        public string Description => "Rename an asset file in the project.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""assetPath"": { ""type"": ""string"", ""description"": ""Current path (e.g. 'Assets/Textures/Old.png')."" },
                ""newName"": { ""type"": ""string"", ""description"": ""New file name without path (e.g. 'New.png')."" }
            },
            ""required"": [""assetPath"", ""newName""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var assetPath = args.GetProperty("assetPath").GetString();
            var newName = args.GetProperty("newName").GetString();

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                return McpToolResult.Error($"Asset not found: {assetPath}");

            var result = AssetDatabase.RenameAsset(assetPath, System.IO.Path.GetFileNameWithoutExtension(newName));
            if (string.IsNullOrEmpty(result))
                return McpToolResult.Success($"Renamed '{assetPath}' to '{newName}'");
            else
                return McpToolResult.Error($"Rename failed: {result}");
        }
    }

    /// <summary>
    /// Move an asset to a different folder.
    /// </summary>
    public class AssetMoveTool : IMcpTool
    {
        public string Name => "asset_move";
        public string Description => "Move an asset to a different folder in the project.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""assetPath"": { ""type"": ""string"", ""description"": ""Current asset path (e.g. 'Assets/Old/Player.prefab')."" },
                ""newPath"": { ""type"": ""string"", ""description"": ""New full path (e.g. 'Assets/New/Player.prefab')."" }
            },
            ""required"": [""assetPath"", ""newPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var assetPath = args.GetProperty("assetPath").GetString();
            var newPath = args.GetProperty("newPath").GetString();

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                return McpToolResult.Error($"Asset not found: {assetPath}");

            // Ensure destination folder exists
            var dir = System.IO.Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                CreateFolderRecursive(dir);
            }

            var result = AssetDatabase.MoveAsset(assetPath, newPath);
            if (string.IsNullOrEmpty(result))
                return McpToolResult.Success($"Moved '{assetPath}' to '{newPath}'");
            else
                return McpToolResult.Error($"Move failed: {result}");
        }

        static void CreateFolderRecursive(string folderPath)
        {
            var parts = folderPath.Replace("\\", "/").Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }

    /// <summary>
    /// Delete an asset.
    /// </summary>
    public class AssetDeleteTool : IMcpTool
    {
        public string Name => "asset_delete";
        public string Description => "Delete an asset from the project. Moves to OS trash by default for safety.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""assetPath"": { ""type"": ""string"", ""description"": ""Path of the asset to delete."" }
            },
            ""required"": [""assetPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var assetPath = args.GetProperty("assetPath").GetString();

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                return McpToolResult.Error($"Asset not found: {assetPath}");

            if (AssetDatabase.MoveAssetToTrash(assetPath))
                return McpToolResult.Success($"Deleted (moved to trash): {assetPath}");
            else
                return McpToolResult.Error($"Failed to delete: {assetPath}");
        }
    }

    /// <summary>
    /// Refresh the Asset Database.
    /// </summary>
    public class AssetRefreshTool : IMcpTool
    {
        public string Name => "asset_refresh";
        public string Description => "Force a refresh of the Unity Asset Database. Useful after external file changes.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            AssetDatabase.Refresh();
            return McpToolResult.Success("Asset Database refreshed");
        }
    }
}
