using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Get physics settings.
    /// </summary>
    public class PhysicsGetSettingsTool : IMcpTool
    {
        public string Name => "physics_get_settings";
        public string Description => "Get physics settings including gravity, default solver iterations, layer collision matrix, and physics material defaults.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""includeCollisionMatrix"": { ""type"": ""boolean"", ""description"": ""Include full layer collision matrix (default: false, can be verbose)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            bool includeMatrix = false;
            if (args.ValueKind != JsonValueKind.Undefined &&
                args.TryGetProperty("includeCollisionMatrix", out var cm))
                includeMatrix = cm.GetBoolean();

            var sb = new StringBuilder();
            sb.AppendLine("== Physics Settings ==");
            sb.AppendLine($"Gravity: {Physics.gravity}");
            sb.AppendLine($"Default Solver Iterations: {Physics.defaultSolverIterations}");
            sb.AppendLine($"Default Solver Velocity Iterations: {Physics.defaultSolverVelocityIterations}");
            sb.AppendLine($"Bounce Threshold: {Physics.bounceThreshold}");
            sb.AppendLine($"Default Contact Offset: {Physics.defaultContactOffset}");
            sb.AppendLine($"Sleep Threshold: {Physics.sleepThreshold}");
            sb.AppendLine($"Queries Hit Triggers: {Physics.queriesHitTriggers}");
            sb.AppendLine($"Auto Sync Transforms: {Physics.autoSyncTransforms}");
            sb.AppendLine();

            sb.AppendLine("== Physics 2D ==");
            sb.AppendLine($"Gravity 2D: {Physics2D.gravity}");
            sb.AppendLine();

            if (includeMatrix)
            {
                sb.AppendLine("== Layer Collision Matrix ==");
                for (int i = 0; i < 32; i++)
                {
                    var layerName = LayerMask.LayerToName(i);
                    if (string.IsNullOrEmpty(layerName)) continue;

                    var collidesWith = new System.Collections.Generic.List<string>();
                    for (int j = 0; j < 32; j++)
                    {
                        var otherName = LayerMask.LayerToName(j);
                        if (string.IsNullOrEmpty(otherName)) continue;
                        if (!Physics.GetIgnoreLayerCollision(i, j))
                            collidesWith.Add(otherName);
                    }
                    sb.AppendLine($"  {layerName}: {string.Join(", ", collidesWith)}");
                }
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Get/list tags and layers.
    /// </summary>
    public class TagsAndLayersTool : IMcpTool
    {
        public string Name => "config_tags_and_layers";
        public string Description => "List all tags, layers, and sorting layers configured in the project.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();

            sb.AppendLine("== Tags ==");
            foreach (var tag in UnityEditorInternal.InternalEditorUtility.tags)
                sb.AppendLine($"  {tag}");
            sb.AppendLine();

            sb.AppendLine("== Layers ==");
            for (int i = 0; i < 32; i++)
            {
                var name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                    sb.AppendLine($"  [{i}] {name}");
            }
            sb.AppendLine();

            sb.AppendLine("== Sorting Layers ==");
            foreach (var sl in SortingLayer.layers)
                sb.AppendLine($"  [{sl.id}] {sl.name} (value: {sl.value})");

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Trigger a project build.
    /// </summary>
    public class BuildProjectTool : IMcpTool
    {
        public string Name => "build_project";
        public string Description => "Build the Unity project with current settings. Specify output path and target platform.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""outputPath"": { ""type"": ""string"", ""description"": ""Output path for the build (e.g. 'Builds/MyGame.exe')."" },
                ""target"": { ""type"": ""string"", ""description"": ""Build target: StandaloneWindows64, StandaloneOSX, StandaloneLinux64, Android, iOS, WebGL (default: current)."" }
            },
            ""required"": [""outputPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (EditorApplication.isPlaying)
                return McpToolResult.Error("Cannot build while in Play Mode. Stop Play Mode first.");

            var outputPath = args.GetProperty("outputPath").GetString();

            var target = EditorUserBuildSettings.activeBuildTarget;
            if (args.TryGetProperty("target", out var t))
            {
                if (Enum.TryParse<BuildTarget>(t.GetString(), true, out var parsed))
                    target = parsed;
                else
                    return McpToolResult.Error($"Invalid build target: {t.GetString()}");
            }

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                return McpToolResult.Error("No scenes enabled in Build Settings");

            var report = BuildPipeline.BuildPlayer(scenes, outputPath, target, BuildOptions.None);

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Build succeeded!");
                sb.AppendLine($"Output: {outputPath}");
                sb.AppendLine($"Target: {target}");
                sb.AppendLine($"Total size: {report.summary.totalSize / (1024 * 1024):F1} MB");
                sb.AppendLine($"Total time: {report.summary.totalTime.TotalSeconds:F1}s");
                sb.AppendLine($"Warnings: {report.summary.totalWarnings}");
                sb.AppendLine($"Errors: {report.summary.totalErrors}");
                return McpToolResult.Success(sb.ToString());
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Build failed: {report.summary.result}");
                sb.AppendLine($"Errors: {report.summary.totalErrors}");

                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error)
                            sb.AppendLine($"  ERROR: {msg.content}");
                    }
                }
                return McpToolResult.Error(sb.ToString());
            }
        }
    }
}
