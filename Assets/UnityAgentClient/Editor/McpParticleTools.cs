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
    // ═══════════════════════════════════════════════════════════════
    //  Particle System Tools — read/modify ParticleSystem modules
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Read all module settings for a ParticleSystem.
    /// </summary>
    public class ParticleGetSettingsTool : IMcpTool
    {
        public string Name => "particle_get_settings";
        public string Description =>
            "Read all particle system module settings for a GameObject. " +
            "Returns main, emission, shape, color over lifetime, size over lifetime, " +
            "velocity over lifetime, and renderer module configurations.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject with a ParticleSystem."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(goPath) ?? SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
                return McpToolResult.Error($"No ParticleSystem on '{go.name}'.");

            var sb = new StringBuilder();
            sb.AppendLine($"== ParticleSystem: {go.name} ==");
            sb.AppendLine($"Is Playing: {ps.isPlaying}, Is Emitting: {ps.isEmitting}");
            sb.AppendLine($"Particle Count: {ps.particleCount}");

            // Main module
            var main = ps.main;
            sb.AppendLine();
            sb.AppendLine("== Main Module ==");
            sb.AppendLine($"Duration: {main.duration}");
            sb.AppendLine($"Looping: {main.loop}");
            sb.AppendLine($"Prewarm: {main.prewarm}");
            sb.AppendLine($"Start Delay: {FormatMinMaxCurve(main.startDelay)}");
            sb.AppendLine($"Start Lifetime: {FormatMinMaxCurve(main.startLifetime)}");
            sb.AppendLine($"Start Speed: {FormatMinMaxCurve(main.startSpeed)}");
            sb.AppendLine($"Start Size: {FormatMinMaxCurve(main.startSize)}");
            sb.AppendLine($"Start Color: {FormatMinMaxGradient(main.startColor)}");
            sb.AppendLine($"Gravity Modifier: {FormatMinMaxCurve(main.gravityModifier)}");
            sb.AppendLine($"Simulation Space: {main.simulationSpace}");
            sb.AppendLine($"Max Particles: {main.maxParticles}");

            // Emission module
            var emission = ps.emission;
            sb.AppendLine();
            sb.AppendLine("== Emission Module ==");
            sb.AppendLine($"Enabled: {emission.enabled}");
            if (emission.enabled)
            {
                sb.AppendLine($"Rate Over Time: {FormatMinMaxCurve(emission.rateOverTime)}");
                sb.AppendLine($"Rate Over Distance: {FormatMinMaxCurve(emission.rateOverDistance)}");
                sb.AppendLine($"Bursts: {emission.burstCount}");
                for (int i = 0; i < emission.burstCount; i++)
                {
                    var burst = emission.GetBurst(i);
                    sb.AppendLine($"  Burst {i}: time={burst.time:F2}, count={FormatMinMaxCurve(burst.count)}, cycles={burst.cycleCount}, interval={burst.repeatInterval:F2}");
                }
            }

            // Shape module
            var shape = ps.shape;
            sb.AppendLine();
            sb.AppendLine("== Shape Module ==");
            sb.AppendLine($"Enabled: {shape.enabled}");
            if (shape.enabled)
            {
                sb.AppendLine($"Shape Type: {shape.shapeType}");
                sb.AppendLine($"Radius: {shape.radius}");
                sb.AppendLine($"Angle: {shape.angle}");
                sb.AppendLine($"Arc: {shape.arc}");
                sb.AppendLine($"Position: {shape.position}");
                sb.AppendLine($"Rotation: {shape.rotation}");
                sb.AppendLine($"Scale: {shape.scale}");
            }

            // Color over Lifetime
            var colorOverLifetime = ps.colorOverLifetime;
            sb.AppendLine();
            sb.AppendLine("== Color Over Lifetime ==");
            sb.AppendLine($"Enabled: {colorOverLifetime.enabled}");
            if (colorOverLifetime.enabled)
                sb.AppendLine($"Color: {FormatMinMaxGradient(colorOverLifetime.color)}");

            // Size over Lifetime
            var sizeOverLifetime = ps.sizeOverLifetime;
            sb.AppendLine();
            sb.AppendLine("== Size Over Lifetime ==");
            sb.AppendLine($"Enabled: {sizeOverLifetime.enabled}");
            if (sizeOverLifetime.enabled)
                sb.AppendLine($"Size: {FormatMinMaxCurve(sizeOverLifetime.size)}");

            // Velocity over Lifetime
            var velocityOverLifetime = ps.velocityOverLifetime;
            sb.AppendLine();
            sb.AppendLine("== Velocity Over Lifetime ==");
            sb.AppendLine($"Enabled: {velocityOverLifetime.enabled}");
            if (velocityOverLifetime.enabled)
            {
                sb.AppendLine($"X: {FormatMinMaxCurve(velocityOverLifetime.x)}");
                sb.AppendLine($"Y: {FormatMinMaxCurve(velocityOverLifetime.y)}");
                sb.AppendLine($"Z: {FormatMinMaxCurve(velocityOverLifetime.z)}");
                sb.AppendLine($"Space: {velocityOverLifetime.space}");
            }

            // Renderer
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                sb.AppendLine();
                sb.AppendLine("== Renderer ==");
                sb.AppendLine($"Render Mode: {renderer.renderMode}");
                sb.AppendLine($"Material: {(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none")}");
                sb.AppendLine($"Sort Mode: {renderer.sortMode}");
                sb.AppendLine($"Min/Max Particle Size: {renderer.minParticleSize} / {renderer.maxParticleSize}");
            }

            return McpToolResult.Success(sb.ToString());
        }

        static string FormatMinMaxCurve(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return $"{curve.constant:F3}";
                case ParticleSystemCurveMode.TwoConstants:
                    return $"[{curve.constantMin:F3} .. {curve.constantMax:F3}]";
                default:
                    return $"({curve.mode})";
            }
        }

        static string FormatMinMaxGradient(ParticleSystem.MinMaxGradient grad)
        {
            switch (grad.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return $"{grad.color}";
                case ParticleSystemGradientMode.TwoColors:
                    return $"[{grad.colorMin} .. {grad.colorMax}]";
                default:
                    return $"({grad.mode})";
            }
        }
    }

    /// <summary>
    /// Modify the MainModule of a ParticleSystem.
    /// </summary>
    public class ParticleSetMainTool : IMcpTool
    {
        public string Name => "particle_set_main";
        public string Description =>
            "Modify the Main module of a ParticleSystem: duration, looping, start lifetime, " +
            "start speed, start size, start color, gravity, simulation space, max particles.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""duration"": { ""type"": ""number"", ""description"": ""Duration in seconds."" },
                ""loop"": { ""type"": ""boolean"", ""description"": ""Enable looping."" },
                ""prewarm"": { ""type"": ""boolean"", ""description"": ""Enable prewarm."" },
                ""startLifetime"": { ""type"": ""number"", ""description"": ""Start lifetime in seconds."" },
                ""startSpeed"": { ""type"": ""number"", ""description"": ""Start speed."" },
                ""startSize"": { ""type"": ""number"", ""description"": ""Start size."" },
                ""startColor"": { ""type"": ""string"", ""description"": ""Start color as 'r,g,b,a' (0-1 range)."" },
                ""gravityModifier"": { ""type"": ""number"", ""description"": ""Gravity multiplier."" },
                ""simulationSpace"": { ""type"": ""string"", ""description"": ""Simulation space: 'Local' or 'World'."", ""enum"": [""Local"", ""World""] },
                ""maxParticles"": { ""type"": ""integer"", ""description"": ""Max particle count."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var result = ParticleToolHelpers.GetParticleSystem(args);
            if (result.error != null) return result.error;
            var ps = result.ps;

            Undo.RecordObject(ps, "Set Particle Main Module");
            var main = ps.main;
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("duration", out var d) && d.TryGetSingle(out var dur))
            { main.duration = dur; changes.Add($"duration={dur}"); }

            if (args.TryGetProperty("loop", out var lp))
            { main.loop = lp.GetBoolean(); changes.Add($"loop={main.loop}"); }

            if (args.TryGetProperty("prewarm", out var pw))
            { main.prewarm = pw.GetBoolean(); changes.Add($"prewarm={main.prewarm}"); }

            if (args.TryGetProperty("startLifetime", out var sl) && sl.TryGetSingle(out var lt))
            { main.startLifetime = lt; changes.Add($"startLifetime={lt}"); }

            if (args.TryGetProperty("startSpeed", out var ss) && ss.TryGetSingle(out var spd))
            { main.startSpeed = spd; changes.Add($"startSpeed={spd}"); }

            if (args.TryGetProperty("startSize", out var sz) && sz.TryGetSingle(out var size))
            { main.startSize = size; changes.Add($"startSize={size}"); }

            if (args.TryGetProperty("startColor", out var sc))
            {
                var c = ParticleToolHelpers.ParseColor(sc.GetString());
                main.startColor = c;
                changes.Add($"startColor={c}");
            }

            if (args.TryGetProperty("gravityModifier", out var gm) && gm.TryGetSingle(out var grav))
            { main.gravityModifier = grav; changes.Add($"gravity={grav}"); }

            if (args.TryGetProperty("simulationSpace", out var ssp))
            {
                if (Enum.TryParse<ParticleSystemSimulationSpace>(ssp.GetString(), true, out var space))
                { main.simulationSpace = space; changes.Add($"simulationSpace={space}"); }
            }

            if (args.TryGetProperty("maxParticles", out var mp))
            { main.maxParticles = mp.GetInt32(); changes.Add($"maxParticles={main.maxParticles}"); }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one main module property to change.");

            EditorSceneManager.MarkSceneDirty(ps.gameObject.scene);
            return McpToolResult.Success($"Main module updated on '{ps.gameObject.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Modify the Emission module of a ParticleSystem.
    /// </summary>
    public class ParticleSetEmissionTool : IMcpTool
    {
        public string Name => "particle_set_emission";
        public string Description =>
            "Modify the Emission module of a ParticleSystem: enable/disable, rate over time, " +
            "rate over distance, and burst configuration.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""enabled"": { ""type"": ""boolean"", ""description"": ""Enable or disable the emission module."" },
                ""rateOverTime"": { ""type"": ""number"", ""description"": ""Particles emitted per second."" },
                ""rateOverDistance"": { ""type"": ""number"", ""description"": ""Particles emitted per unit distance."" },
                ""addBurst"": { ""type"": ""string"", ""description"": ""Add a burst: 'time,count' (e.g. '0.5,20')."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var result = ParticleToolHelpers.GetParticleSystem(args);
            if (result.error != null) return result.error;
            var ps = result.ps;

            Undo.RecordObject(ps, "Set Particle Emission");
            var emission = ps.emission;
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("enabled", out var en))
            { emission.enabled = en.GetBoolean(); changes.Add($"enabled={emission.enabled}"); }

            if (args.TryGetProperty("rateOverTime", out var rot) && rot.TryGetSingle(out var rate))
            { emission.rateOverTime = rate; changes.Add($"rateOverTime={rate}"); }

            if (args.TryGetProperty("rateOverDistance", out var rod) && rod.TryGetSingle(out var rateDist))
            { emission.rateOverDistance = rateDist; changes.Add($"rateOverDistance={rateDist}"); }

            if (args.TryGetProperty("addBurst", out var burst))
            {
                var parts = burst.GetString().Split(',').Select(float.Parse).ToArray();
                if (parts.Length >= 2)
                {
                    emission.SetBurst(emission.burstCount, new ParticleSystem.Burst(parts[0], (short)parts[1]));
                    changes.Add($"addBurst(time={parts[0]}, count={parts[1]})");
                }
            }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one emission property to change.");

            EditorSceneManager.MarkSceneDirty(ps.gameObject.scene);
            return McpToolResult.Success($"Emission updated on '{ps.gameObject.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Modify the Shape module of a ParticleSystem.
    /// </summary>
    public class ParticleSetShapeTool : IMcpTool
    {
        public string Name => "particle_set_shape";
        public string Description =>
            "Modify the Shape module of a ParticleSystem: shape type (Sphere, Hemisphere, Cone, Box, Circle, Edge), " +
            "radius, angle, arc, position, rotation, scale.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""enabled"": { ""type"": ""boolean"", ""description"": ""Enable or disable the shape module."" },
                ""shapeType"": { ""type"": ""string"", ""description"": ""Shape type: 'Sphere', 'Hemisphere', 'Cone', 'Box', 'Circle', 'Edge'."" },
                ""radius"": { ""type"": ""number"", ""description"": ""Shape radius."" },
                ""angle"": { ""type"": ""number"", ""description"": ""Cone angle (for Cone shape)."" },
                ""arc"": { ""type"": ""number"", ""description"": ""Arc angle in degrees (0-360)."" },
                ""position"": { ""type"": ""string"", ""description"": ""Shape position offset as 'x,y,z'."" },
                ""rotation"": { ""type"": ""string"", ""description"": ""Shape rotation as 'x,y,z' euler angles."" },
                ""scale"": { ""type"": ""string"", ""description"": ""Shape scale as 'x,y,z'."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var result = ParticleToolHelpers.GetParticleSystem(args);
            if (result.error != null) return result.error;
            var ps = result.ps;

            Undo.RecordObject(ps, "Set Particle Shape");
            var shape = ps.shape;
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("enabled", out var en))
            { shape.enabled = en.GetBoolean(); changes.Add($"enabled={shape.enabled}"); }

            if (args.TryGetProperty("shapeType", out var st))
            {
                if (Enum.TryParse<ParticleSystemShapeType>(st.GetString(), true, out var shapeType))
                { shape.shapeType = shapeType; changes.Add($"shapeType={shapeType}"); }
                else
                    return McpToolResult.Error($"Unknown shape type: {st.GetString()}");
            }

            if (args.TryGetProperty("radius", out var r) && r.TryGetSingle(out var rad))
            { shape.radius = rad; changes.Add($"radius={rad}"); }

            if (args.TryGetProperty("angle", out var a) && a.TryGetSingle(out var angle))
            { shape.angle = angle; changes.Add($"angle={angle}"); }

            if (args.TryGetProperty("arc", out var arc) && arc.TryGetSingle(out var arcVal))
            { shape.arc = arcVal; changes.Add($"arc={arcVal}"); }

            if (args.TryGetProperty("position", out var pos))
            {
                var p = pos.GetString().Split(',').Select(float.Parse).ToArray();
                shape.position = new Vector3(p[0], p[1], p[2]);
                changes.Add($"position={shape.position}");
            }

            if (args.TryGetProperty("rotation", out var rot))
            {
                var rv = rot.GetString().Split(',').Select(float.Parse).ToArray();
                shape.rotation = new Vector3(rv[0], rv[1], rv[2]);
                changes.Add($"rotation={shape.rotation}");
            }

            if (args.TryGetProperty("scale", out var scl))
            {
                var sv = scl.GetString().Split(',').Select(float.Parse).ToArray();
                shape.scale = new Vector3(sv[0], sv[1], sv[2]);
                changes.Add($"scale={shape.scale}");
            }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one shape property to change.");

            EditorSceneManager.MarkSceneDirty(ps.gameObject.scene);
            return McpToolResult.Success($"Shape updated on '{ps.gameObject.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Modify the Color Over Lifetime module.
    /// </summary>
    public class ParticleSetColorTool : IMcpTool
    {
        public string Name => "particle_set_color";
        public string Description =>
            "Modify the Color Over Lifetime module of a ParticleSystem. " +
            "Set a gradient from startColor to endColor over the particle's lifetime.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""enabled"": { ""type"": ""boolean"", ""description"": ""Enable or disable color over lifetime."" },
                ""startColor"": { ""type"": ""string"", ""description"": ""Start color as 'r,g,b,a' (0-1)."" },
                ""endColor"": { ""type"": ""string"", ""description"": ""End color as 'r,g,b,a' (0-1). Creates a gradient from start to end."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var result = ParticleToolHelpers.GetParticleSystem(args);
            if (result.error != null) return result.error;
            var ps = result.ps;

            Undo.RecordObject(ps, "Set Particle Color Over Lifetime");
            var colorOverLifetime = ps.colorOverLifetime;
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("enabled", out var en))
            { colorOverLifetime.enabled = en.GetBoolean(); changes.Add($"enabled={colorOverLifetime.enabled}"); }

            if (args.TryGetProperty("startColor", out var sc) && args.TryGetProperty("endColor", out var ec))
            {
                var start = ParticleToolHelpers.ParseColor(sc.GetString());
                var end = ParticleToolHelpers.ParseColor(ec.GetString());
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                    new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
                colorOverLifetime.enabled = true;
                changes.Add($"gradient: {start} → {end}");
            }
            else if (args.TryGetProperty("startColor", out var sc2))
            {
                var color = ParticleToolHelpers.ParseColor(sc2.GetString());
                colorOverLifetime.color = color;
                colorOverLifetime.enabled = true;
                changes.Add($"color={color}");
            }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one color property to change.");

            EditorSceneManager.MarkSceneDirty(ps.gameObject.scene);
            return McpToolResult.Success($"Color Over Lifetime updated on '{ps.gameObject.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Modify the Size Over Lifetime module.
    /// </summary>
    public class ParticleSetSizeTool : IMcpTool
    {
        public string Name => "particle_set_size";
        public string Description =>
            "Modify the Size Over Lifetime module. Set a constant size multiplier, " +
            "or a start-to-end size curve over the particle's lifetime.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""enabled"": { ""type"": ""boolean"", ""description"": ""Enable or disable size over lifetime."" },
                ""startSize"": { ""type"": ""number"", ""description"": ""Size multiplier at birth (0-1 normalized)."" },
                ""endSize"": { ""type"": ""number"", ""description"": ""Size multiplier at death. If provided with startSize, creates a linear curve."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var result = ParticleToolHelpers.GetParticleSystem(args);
            if (result.error != null) return result.error;
            var ps = result.ps;

            Undo.RecordObject(ps, "Set Particle Size Over Lifetime");
            var sizeOverLifetime = ps.sizeOverLifetime;
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("enabled", out var en))
            { sizeOverLifetime.enabled = en.GetBoolean(); changes.Add($"enabled={sizeOverLifetime.enabled}"); }

            if (args.TryGetProperty("startSize", out var ss) && ss.TryGetSingle(out var startSize))
            {
                if (args.TryGetProperty("endSize", out var es) && es.TryGetSingle(out var endSize))
                {
                    var curve = AnimationCurve.Linear(0f, startSize, 1f, endSize);
                    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);
                    sizeOverLifetime.enabled = true;
                    changes.Add($"size: {startSize} → {endSize}");
                }
                else
                {
                    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(startSize);
                    sizeOverLifetime.enabled = true;
                    changes.Add($"size={startSize}");
                }
            }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one size property to change.");

            EditorSceneManager.MarkSceneDirty(ps.gameObject.scene);
            return McpToolResult.Success($"Size Over Lifetime updated on '{ps.gameObject.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Modify the ParticleSystemRenderer.
    /// </summary>
    public class ParticleSetRendererTool : IMcpTool
    {
        public string Name => "particle_set_renderer";
        public string Description =>
            "Modify the ParticleSystemRenderer: render mode (Billboard, Stretch, Mesh, etc.), " +
            "material assignment, sort mode, min/max particle size.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""renderMode"": { ""type"": ""string"", ""description"": ""Render mode: 'Billboard', 'Stretch', 'HorizontalBillboard', 'VerticalBillboard', 'Mesh'."" },
                ""materialPath"": { ""type"": ""string"", ""description"": ""Asset path of the material to assign (e.g. 'Assets/Materials/Fire.mat')."" },
                ""sortMode"": { ""type"": ""string"", ""description"": ""Sort mode: 'None', 'Distance', 'OldestInFront', 'YoungestInFront'."" },
                ""minParticleSize"": { ""type"": ""number"", ""description"": ""Minimum particle size (0-1, screen fraction)."" },
                ""maxParticleSize"": { ""type"": ""number"", ""description"": ""Maximum particle size (0-1, screen fraction)."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(goPath) ?? SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null) return McpToolResult.Error($"GameObject not found: {goPath}");

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return McpToolResult.Error($"No ParticleSystemRenderer on '{go.name}'.");

            Undo.RecordObject(renderer, "Set Particle Renderer");
            var changes = new System.Collections.Generic.List<string>();

            if (args.TryGetProperty("renderMode", out var rm))
            {
                if (Enum.TryParse<ParticleSystemRenderMode>(rm.GetString(), true, out var mode))
                { renderer.renderMode = mode; changes.Add($"renderMode={mode}"); }
                else return McpToolResult.Error($"Unknown render mode: {rm.GetString()}");
            }

            if (args.TryGetProperty("materialPath", out var mp))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(mp.GetString());
                if (mat == null) return McpToolResult.Error($"Material not found: {mp.GetString()}");
                renderer.sharedMaterial = mat;
                changes.Add($"material={mat.name}");
            }

            if (args.TryGetProperty("sortMode", out var sm))
            {
                if (Enum.TryParse<ParticleSystemSortMode>(sm.GetString(), true, out var sort))
                { renderer.sortMode = sort; changes.Add($"sortMode={sort}"); }
            }

            if (args.TryGetProperty("minParticleSize", out var mins) && mins.TryGetSingle(out var minSize))
            { renderer.minParticleSize = minSize; changes.Add($"minSize={minSize}"); }

            if (args.TryGetProperty("maxParticleSize", out var maxs) && maxs.TryGetSingle(out var maxSize))
            { renderer.maxParticleSize = maxSize; changes.Add($"maxSize={maxSize}"); }

            if (changes.Count == 0)
                return McpToolResult.Error("Provide at least one renderer property to change.");

            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Renderer updated on '{go.name}': {string.Join(", ", changes)}");
        }
    }

    /// <summary>
    /// Preview control: play/stop/restart a ParticleSystem in the editor.
    /// </summary>
    public class ParticlePreviewTool : IMcpTool
    {
        public string Name => "particle_preview";
        public string Description =>
            "Control ParticleSystem preview in the editor: play, stop, restart, or simulate to a specific time.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the GameObject."" },
                ""command"": { ""type"": ""string"", ""description"": ""Preview command: 'play', 'stop', 'restart', or 'simulate'."", ""enum"": [""play"", ""stop"", ""restart"", ""simulate""] },
                ""simulateTime"": { ""type"": ""number"", ""description"": ""Time in seconds to simulate to (only for 'simulate' command)."" }
            },
            ""required"": [""gameObjectPath"", ""command""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var result = ParticleToolHelpers.GetParticleSystem(args);
            if (result.error != null) return result.error;
            var ps = result.ps;

            var command = args.GetProperty("command").GetString().ToLowerInvariant();

            switch (command)
            {
                case "play":
                    ps.Play(true);
                    return McpToolResult.Success($"Playing particle system on '{ps.gameObject.name}'");
                case "stop":
                    ps.Stop(true);
                    return McpToolResult.Success($"Stopped particle system on '{ps.gameObject.name}'");
                case "restart":
                    ps.Stop(true);
                    ps.Clear(true);
                    ps.Play(true);
                    return McpToolResult.Success($"Restarted particle system on '{ps.gameObject.name}'");
                case "simulate":
                    float time = 1f;
                    if (args.TryGetProperty("simulateTime", out var st) && st.TryGetSingle(out var t)) time = t;
                    ps.Simulate(time, true, true);
                    return McpToolResult.Success($"Simulated to t={time}s on '{ps.gameObject.name}'");
                default:
                    return McpToolResult.Error($"Unknown command: {command}. Use: play, stop, restart, simulate.");
            }
        }
    }

    /// <summary>
    /// Shared helpers for particle tools.
    /// </summary>
    internal static class ParticleToolHelpers
    {
        public struct ParticleResult
        {
            public ParticleSystem ps;
            public McpToolResult error;
        }

        public static ParticleResult GetParticleSystem(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(goPath) ?? SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return new ParticleResult { error = McpToolResult.Error($"GameObject not found: {goPath}") };

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
                return new ParticleResult { error = McpToolResult.Error($"No ParticleSystem on '{go.name}'.") };

            return new ParticleResult { ps = ps };
        }

        public static Color ParseColor(string colorStr)
        {
            var parts = colorStr.Split(',').Select(float.Parse).ToArray();
            return new Color(
                parts[0],
                parts.Length > 1 ? parts[1] : 0f,
                parts.Length > 2 ? parts[2] : 0f,
                parts.Length > 3 ? parts[3] : 1f);
        }
    }
}
