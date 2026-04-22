using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAgentClient
{
    /// <summary>
    /// Get lighting and lightmap settings for the active scene.
    /// </summary>
    public class LightingGetSettingsTool : IMcpTool
    {
        public string Name => "lighting_get_settings";
        public string Description => "Get the current lighting settings including ambient light, fog, skybox, lightmap settings, and all lights in the scene.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();

            // Ambient lighting
            sb.AppendLine("== Ambient Lighting ==");
            sb.AppendLine($"Ambient Mode: {RenderSettings.ambientMode}");
            switch (RenderSettings.ambientMode)
            {
                case AmbientMode.Flat:
                    sb.AppendLine($"Ambient Color: {RenderSettings.ambientLight}");
                    break;
                case AmbientMode.Trilight:
                    sb.AppendLine($"Sky Color: {RenderSettings.ambientSkyColor}");
                    sb.AppendLine($"Equator Color: {RenderSettings.ambientEquatorColor}");
                    sb.AppendLine($"Ground Color: {RenderSettings.ambientGroundColor}");
                    break;
                case AmbientMode.Skybox:
                    sb.AppendLine($"Ambient Intensity: {RenderSettings.ambientIntensity}");
                    break;
            }
            sb.AppendLine();

            // Skybox
            sb.AppendLine("== Skybox ==");
            var skybox = RenderSettings.skybox;
            sb.AppendLine(skybox != null
                ? $"Skybox Material: {skybox.name} (shader: {skybox.shader.name})"
                : "Skybox Material: none");
            sb.AppendLine();

            // Fog
            sb.AppendLine("== Fog ==");
            sb.AppendLine($"Enabled: {RenderSettings.fog}");
            if (RenderSettings.fog)
            {
                sb.AppendLine($"Color: {RenderSettings.fogColor}");
                sb.AppendLine($"Mode: {RenderSettings.fogMode}");
                sb.AppendLine($"Density: {RenderSettings.fogDensity}");
                sb.AppendLine($"Start: {RenderSettings.fogStartDistance}");
                sb.AppendLine($"End: {RenderSettings.fogEndDistance}");
            }
            sb.AppendLine();

            // Lightmapping
            sb.AppendLine("== Lightmapping ==");
            sb.AppendLine($"GI Workflow: {Lightmapping.giWorkflowMode}");
            sb.AppendLine($"Realtime GI: {Lightmapping.realtimeGI}");
            sb.AppendLine($"Baked GI: {Lightmapping.bakedGI}");
            sb.AppendLine($"Lightmap Resolution: {LightmapEditorSettings.bakeResolution}");
            sb.AppendLine($"Lightmap Padding: {LightmapEditorSettings.padding}");
            sb.AppendLine($"Lightmap Size: {LightmapEditorSettings.maxAtlasSize}");
            sb.AppendLine();

            // Scene lights
            var lights = SceneToolHelpers.FindAllIncludeInactive<Light>();
            sb.AppendLine($"== Scene Lights ({lights.Length}) ==");
            foreach (var light in lights)
            {
                var go = light.gameObject;
                sb.AppendLine($"  {go.name} (active: {go.activeInHierarchy})");
                sb.AppendLine($"    Type: {light.type}, Color: {light.color}, Intensity: {light.intensity}");
                if (light.type == LightType.Spot)
                    sb.AppendLine($"    Spot Angle: {light.spotAngle}, Range: {light.range}");
                else if (light.type == LightType.Point)
                    sb.AppendLine($"    Range: {light.range}");
                sb.AppendLine($"    Shadows: {light.shadows}");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Bake lightmaps for the active scene.
    /// </summary>
    public class LightingBakeTool : IMcpTool
    {
        public string Name => "lighting_bake";
        public string Description => "Bake lightmaps for the active scene. This may take a while depending on scene complexity and lightmap resolution.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""clearFirst"": { ""type"": ""boolean"", ""description"": ""Clear existing lightmaps before baking (default: false)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            if (EditorApplication.isPlaying)
                return McpToolResult.Error("Cannot bake lightmaps during Play Mode.");

            bool clearFirst = false;
            if (args.TryGetProperty("clearFirst", out var cf))
                clearFirst = cf.GetBoolean();

            if (clearFirst)
                Lightmapping.Clear();

            Lightmapping.Bake();

            return McpToolResult.Success(
                "Lightmap bake started. This runs asynchronously — " +
                "check the Unity Editor progress bar for status.");
        }
    }

    /// <summary>
    /// Modify ambient lighting and fog settings.
    /// </summary>
    public class LightingSetAmbientTool : IMcpTool
    {
        public string Name => "lighting_set_ambient";
        public string Description => "Modify ambient lighting color/intensity and fog settings for the scene.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""ambientColor"": { ""type"": ""string"", ""description"": ""Ambient light color as 'r,g,b,a' (e.g. '0.2,0.2,0.3,1' for dim blue)."" },
                ""ambientIntensity"": { ""type"": ""number"", ""description"": ""Ambient intensity (0-8, default: 1)."" },
                ""fogEnabled"": { ""type"": ""boolean"", ""description"": ""Enable or disable fog."" },
                ""fogColor"": { ""type"": ""string"", ""description"": ""Fog color as 'r,g,b,a'."" },
                ""fogDensity"": { ""type"": ""number"", ""description"": ""Fog density (0-1)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("ambientColor", out var ac))
            {
                var parts = ac.GetString().Split(',').Select(float.Parse).ToArray();
                var color = new Color(parts[0], parts[1],
                    parts.Length > 2 ? parts[2] : 0,
                    parts.Length > 3 ? parts[3] : 1);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = color;
                changes.Add($"ambient color={color}");
            }

            if (args.TryGetProperty("ambientIntensity", out var ai) && ai.TryGetSingle(out var intensity))
            {
                RenderSettings.ambientIntensity = intensity;
                changes.Add($"ambient intensity={intensity}");
            }

            if (args.TryGetProperty("fogEnabled", out var fe))
            {
                RenderSettings.fog = fe.GetBoolean();
                changes.Add($"fog={RenderSettings.fog}");
            }

            if (args.TryGetProperty("fogColor", out var fc))
            {
                var parts = fc.GetString().Split(',').Select(float.Parse).ToArray();
                RenderSettings.fogColor = new Color(parts[0], parts[1],
                    parts.Length > 2 ? parts[2] : 0,
                    parts.Length > 3 ? parts[3] : 1);
                changes.Add($"fog color={RenderSettings.fogColor}");
            }

            if (args.TryGetProperty("fogDensity", out var fd) && fd.TryGetSingle(out var density))
            {
                RenderSettings.fogDensity = density;
                changes.Add($"fog density={density}");
            }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one setting to change.");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            return McpToolResult.Success($"Lighting updated: {string.Join(", ", changes)}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Semantic Lighting Tool — intent-oriented time-of-day presets
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Configure scene lighting for a time of day.
    /// Handles directional light, ambient color, fog, and intensity in one call.
    /// </summary>
    public class LightingSetupTimeOfDayTool : IMcpTool
    {
        public string Name => "lighting_setup_time_of_day";
        public string Description =>
            "Configure the entire scene lighting for a specific time of day (morning, noon, sunset, night, overcast). " +
            "Automatically adjusts the main directional light direction/color/intensity, ambient light, and optional fog. " +
            "Creates a directional light if none exists.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""timeOfDay"": { ""type"": ""string"", ""description"": ""Preset: 'morning', 'noon', 'sunset', 'night', or 'overcast'."", ""enum"": [""morning"", ""noon"", ""sunset"", ""night"", ""overcast""] },
                ""enableFog"": { ""type"": ""boolean"", ""description"": ""Enable fog for the preset (default: true for morning/overcast, false otherwise)."" },
                ""intensity"": { ""type"": ""number"", ""description"": ""Override the directional light intensity (0-8). If omitted, uses the preset default."" }
            },
            ""required"": [""timeOfDay""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var timeOfDay = args.GetProperty("timeOfDay").GetString().ToLowerInvariant();

            // Presets: (sunRotation, sunColor, sunIntensity, ambientColor, fogEnabled, fogColor, fogDensity)
            Quaternion sunRotation;
            Color sunColor, ambientColor, fogColor;
            float sunIntensity, fogDensity;
            bool defaultFog;

            switch (timeOfDay)
            {
                case "morning":
                    sunRotation = Quaternion.Euler(15f, -30f, 0f);
                    sunColor = new Color(1f, 0.85f, 0.6f);
                    sunIntensity = 0.8f;
                    ambientColor = new Color(0.4f, 0.45f, 0.55f);
                    defaultFog = true;
                    fogColor = new Color(0.8f, 0.8f, 0.85f);
                    fogDensity = 0.01f;
                    break;
                case "noon":
                    sunRotation = Quaternion.Euler(60f, 0f, 0f);
                    sunColor = new Color(1f, 0.97f, 0.92f);
                    sunIntensity = 1.2f;
                    ambientColor = new Color(0.5f, 0.55f, 0.6f);
                    defaultFog = false;
                    fogColor = new Color(0.7f, 0.8f, 0.9f);
                    fogDensity = 0.003f;
                    break;
                case "sunset":
                    sunRotation = Quaternion.Euler(5f, 40f, 0f);
                    sunColor = new Color(1f, 0.5f, 0.15f);
                    sunIntensity = 0.9f;
                    ambientColor = new Color(0.35f, 0.25f, 0.3f);
                    defaultFog = false;
                    fogColor = new Color(0.9f, 0.5f, 0.3f);
                    fogDensity = 0.005f;
                    break;
                case "night":
                    sunRotation = Quaternion.Euler(-30f, 0f, 0f);
                    sunColor = new Color(0.2f, 0.25f, 0.5f);
                    sunIntensity = 0.1f;
                    ambientColor = new Color(0.05f, 0.05f, 0.15f);
                    defaultFog = false;
                    fogColor = new Color(0.05f, 0.05f, 0.1f);
                    fogDensity = 0.02f;
                    break;
                case "overcast":
                    sunRotation = Quaternion.Euler(45f, 0f, 0f);
                    sunColor = new Color(0.7f, 0.7f, 0.7f);
                    sunIntensity = 0.6f;
                    ambientColor = new Color(0.4f, 0.4f, 0.45f);
                    defaultFog = true;
                    fogColor = new Color(0.65f, 0.65f, 0.7f);
                    fogDensity = 0.015f;
                    break;
                default:
                    return McpToolResult.Error($"Unknown timeOfDay: {timeOfDay}. Use: morning, noon, sunset, night, overcast.");
            }

            if (args.TryGetProperty("intensity", out var intProp) && intProp.TryGetSingle(out var intVal))
                sunIntensity = intVal;

            bool fogEnabled = defaultFog;
            if (args.TryGetProperty("enableFog", out var fogProp))
                fogEnabled = fogProp.GetBoolean();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            // Find or create the main directional light
            var directionalLight = SceneToolHelpers.FindAllIncludeInactive<Light>()
                .FirstOrDefault(lt => lt.type == LightType.Directional);

            if (directionalLight == null)
            {
                var lightGo = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(lightGo, "Create Directional Light");
                directionalLight = lightGo.AddComponent<Light>();
                directionalLight.type = LightType.Directional;
                directionalLight.shadows = LightShadows.Soft;
            }
            else
            {
                Undo.RecordObject(directionalLight, "Setup Time of Day");
                Undo.RecordObject(directionalLight.transform, "Setup Time of Day");
            }

            directionalLight.transform.rotation = sunRotation;
            directionalLight.color = sunColor;
            directionalLight.intensity = sunIntensity;

            // Ambient
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;

            // Fog
            RenderSettings.fog = fogEnabled;
            if (fogEnabled)
            {
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = fogDensity;
            }

            Undo.CollapseUndoOperations(undoGroup);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            return McpToolResult.Success(
                $"Lighting set to '{timeOfDay}': sun intensity={sunIntensity:F1}, " +
                $"ambient={ambientColor}, fog={fogEnabled}" +
                (fogEnabled ? $" (density={fogDensity})" : ""));
        }
    }
}
