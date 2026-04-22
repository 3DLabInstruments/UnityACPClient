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
    /// Enter Play Mode in the editor.
    /// </summary>
    public class EnterPlayModeTool : IMcpTool
    {
        public string Name => "editor_enter_playmode";
        public string Description => "Enter Play Mode in the Unity editor. The scene must be saved first.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""saveScene"": { ""type"": ""boolean"", ""description"": ""Save the current scene before entering Play Mode (default: true)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (EditorApplication.isPlaying)
                return McpToolResult.Error("Already in Play Mode");

            bool saveScene = true;
            if (args.ValueKind != JsonValueKind.Undefined &&
                args.TryGetProperty("saveScene", out var ss))
                saveScene = ss.GetBoolean();

            if (saveScene)
                EditorSceneManager.SaveOpenScenes();

            EditorApplication.isPlaying = true;
            return McpToolResult.Success("Entering Play Mode");
        }
    }

    /// <summary>
    /// Pause/unpause Play Mode.
    /// </summary>
    public class PausePlayModeTool : IMcpTool
    {
        public string Name => "editor_pause_playmode";
        public string Description => "Toggle pause state during Play Mode.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (!EditorApplication.isPlaying)
                return McpToolResult.Error("Not in Play Mode");

            EditorApplication.isPaused = !EditorApplication.isPaused;
            return McpToolResult.Success(EditorApplication.isPaused ? "Paused" : "Resumed");
        }
    }

    /// <summary>
    /// Stop Play Mode.
    /// </summary>
    public class StopPlayModeTool : IMcpTool
    {
        public string Name => "editor_stop_playmode";
        public string Description => "Stop Play Mode and return to Edit Mode.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (!EditorApplication.isPlaying)
                return McpToolResult.Error("Not in Play Mode");

            EditorApplication.isPlaying = false;
            return McpToolResult.Success("Stopped Play Mode");
        }
    }

    /// <summary>
    /// Execute a Unity menu item command.
    /// </summary>
    public class ExecuteMenuItemTool : IMcpTool
    {
        public string Name => "editor_execute_menu_item";
        public string Description => "Execute a Unity editor menu item by its full path (e.g. 'Edit/Preferences', 'GameObject/3D Object/Cube', 'Assets/Refresh').";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""menuPath"": { ""type"": ""string"", ""description"": ""Full menu item path (e.g. 'GameObject/3D Object/Cube')."" }
            },
            ""required"": [""menuPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var menuPath = args.GetProperty("menuPath").GetString();

            if (EditorApplication.ExecuteMenuItem(menuPath))
                return McpToolResult.Success($"Executed menu item: {menuPath}");
            else
                return McpToolResult.Error($"Menu item not found or failed: {menuPath}");
        }
    }

    /// <summary>
    /// Get the current editor state (play mode, scene, selection, etc.)
    /// </summary>
    public class GetEditorStateTool : IMcpTool
    {
        public string Name => "editor_get_state";
        public string Description => "Get current Unity editor state including play mode status, active scene, selected objects, and editor version.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();

            sb.AppendLine($"== Editor State ==");
            sb.AppendLine($"Play Mode: {(EditorApplication.isPlaying ? "Playing" : "Editing")}");
            sb.AppendLine($"Paused: {EditorApplication.isPaused}");
            sb.AppendLine($"Compiling: {EditorApplication.isCompiling}");
            sb.AppendLine();

            sb.AppendLine($"== Active Scene ==");
            sb.AppendLine($"Name: {scene.name}");
            sb.AppendLine($"Path: {scene.path}");
            sb.AppendLine($"Dirty: {scene.isDirty}");
            sb.AppendLine($"Root Objects: {scene.rootCount}");
            sb.AppendLine();

            sb.AppendLine($"== Loaded Scenes ==");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine($"  [{i}] {s.name} (loaded: {s.isLoaded})");
            }
            sb.AppendLine();

            sb.AppendLine($"== Selection ==");
            var selection = Selection.gameObjects;
            if (selection.Length == 0)
            {
                sb.AppendLine("  (nothing selected)");
            }
            else
            {
                foreach (var sel in selection)
                    sb.AppendLine($"  {sel.name}");
            }

            sb.AppendLine();
            sb.AppendLine($"== Available Actions ==");
            if (EditorApplication.isPlaying)
            {
                sb.AppendLine("  In Play Mode: use stop_playmode or pause_playmode");
                sb.AppendLine("  Scene modifications will be lost when exiting Play Mode");
                sb.AppendLine("  Build is not available during Play Mode");
            }
            else
            {
                sb.AppendLine("  In Edit Mode: all tools available");
                if (scene.isDirty) sb.AppendLine("  Scene has unsaved changes — consider saving");
                if (EditorApplication.isCompiling) sb.AppendLine("  ⚠️ Scripts are compiling — wait before making changes");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Open a scene by path.
    /// </summary>
    public class OpenSceneTool : IMcpTool
    {
        public string Name => "editor_open_scene";
        public string Description => "Open a scene in the editor by its asset path (e.g. 'Assets/Scenes/MainMenu.unity').";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""scenePath"": { ""type"": ""string"", ""description"": ""Path to the scene asset (e.g. 'Assets/Scenes/Main.unity')."" },
                ""additive"": { ""type"": ""boolean"", ""description"": ""Open additively instead of replacing current scene (default: false)."" }
            },
            ""required"": [""scenePath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var scenePath = args.GetProperty("scenePath").GetString();
            bool additive = false;
            if (args.TryGetProperty("additive", out var a)) additive = a.GetBoolean();

            if (!System.IO.File.Exists(scenePath))
                return McpToolResult.Error($"Scene file not found: {scenePath}");

            var mode = additive ? OpenSceneMode.Additive : OpenSceneMode.Single;
            EditorSceneManager.OpenScene(scenePath, mode);
            return McpToolResult.Success($"Opened scene: {scenePath}");
        }
    }

    /// <summary>
    /// Trigger Undo in the editor.
    /// </summary>
    public class UndoTool : IMcpTool
    {
        public string Name => "editor_undo";
        public string Description => "Undo the last operation in the Unity editor. Can undo multiple steps by specifying count.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""count"": { ""type"": ""number"", ""description"": ""Number of undo steps (default: 1)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            int count = 1;
            if (args.TryGetProperty("count", out var c) && c.TryGetInt32(out var v))
                count = Math.Clamp(v, 1, 50);

            for (int i = 0; i < count; i++)
                Undo.PerformUndo();

            return McpToolResult.Success($"Undone {count} operation(s)");
        }
    }

    /// <summary>
    /// Trigger Redo in the editor.
    /// </summary>
    public class RedoTool : IMcpTool
    {
        public string Name => "editor_redo";
        public string Description => "Redo a previously undone operation in the Unity editor. Can redo multiple steps by specifying count.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""count"": { ""type"": ""number"", ""description"": ""Number of redo steps (default: 1)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            int count = 1;
            if (args.TryGetProperty("count", out var c) && c.TryGetInt32(out var v))
                count = Math.Clamp(v, 1, 50);

            for (int i = 0; i < count; i++)
                Undo.PerformRedo();

            return McpToolResult.Success($"Redone {count} operation(s)");
        }
    }
}
