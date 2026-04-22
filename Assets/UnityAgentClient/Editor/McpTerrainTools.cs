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
    //  Terrain Tools — read/modify Unity Terrain data
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Read terrain configuration and settings.
    /// </summary>
    public class TerrainGetSettingsTool : IMcpTool
    {
        public string Name => "terrain_get_settings";
        public string Description =>
            "Read terrain configuration: size, heightmap resolution, terrain layers (splat textures), " +
            "tree prototypes, detail prototypes, and current terrain data summary.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the Terrain GameObject. If omitted, finds the first Terrain in the scene."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var terrain = FindTerrain(args);
            if (terrain == null)
                return McpToolResult.Error("No Terrain found in the scene.");

            var data = terrain.terrainData;
            var sb = new StringBuilder();

            sb.AppendLine($"== Terrain: {terrain.gameObject.name} ==");
            sb.AppendLine($"Position: {terrain.transform.position}");
            sb.AppendLine($"Size: {data.size} (width × height × length)");
            sb.AppendLine();

            // Heightmap
            sb.AppendLine("== Heightmap ==");
            sb.AppendLine($"Resolution: {data.heightmapResolution}x{data.heightmapResolution}");
            sb.AppendLine($"Height Scale: {data.size.y}");
            sb.AppendLine();

            // Terrain layers
            sb.AppendLine($"== Terrain Layers ({data.terrainLayers.Length}) ==");
            for (int i = 0; i < data.terrainLayers.Length; i++)
            {
                var layer = data.terrainLayers[i];
                sb.AppendLine($"  [{i}] {layer.name}: texture={layer.diffuseTexture?.name ?? "none"}, tileSize={layer.tileSize}");
            }
            sb.AppendLine();

            // Alphamap
            sb.AppendLine("== Alphamap (Splatmap) ==");
            sb.AppendLine($"Resolution: {data.alphamapResolution}x{data.alphamapResolution}");
            sb.AppendLine($"Layers: {data.alphamapLayers}");
            sb.AppendLine();

            // Trees
            sb.AppendLine($"== Tree Prototypes ({data.treePrototypes.Length}) ==");
            for (int i = 0; i < data.treePrototypes.Length; i++)
            {
                var proto = data.treePrototypes[i];
                sb.AppendLine($"  [{i}] Prefab: {proto.prefab?.name ?? "none"}, BendFactor: {proto.bendFactor}");
            }
            sb.AppendLine($"Tree Instances: {data.treeInstanceCount}");
            sb.AppendLine();

            // Detail
            sb.AppendLine($"== Detail Prototypes ({data.detailPrototypes.Length}) ==");
            sb.AppendLine($"Detail Resolution: {data.detailResolution}");
            for (int i = 0; i < data.detailPrototypes.Length; i++)
            {
                var detail = data.detailPrototypes[i];
                sb.AppendLine($"  [{i}] Prototype: {detail.prototype?.name ?? "texture-based"}, " +
                    $"MinHeight: {detail.minHeight}, MaxHeight: {detail.maxHeight}");
            }

            return McpToolResult.Success(sb.ToString());
        }

        internal static Terrain FindTerrain(JsonElement args)
        {
            if (args.TryGetProperty("gameObjectPath", out var gp))
            {
                var go = GameObject.Find(gp.GetString()) ?? SceneToolHelpers.FindGameObjectIncludeInactive(gp.GetString());
                return go?.GetComponent<Terrain>();
            }
            return SceneToolHelpers.FindAllIncludeInactive<Terrain>().FirstOrDefault();
        }
    }

    /// <summary>
    /// Sample terrain height at a world position.
    /// </summary>
    public class TerrainGetHeightTool : IMcpTool
    {
        public string Name => "terrain_get_height";
        public string Description =>
            "Sample the terrain height at a world X,Z position. Returns the Y coordinate of the terrain surface.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the Terrain GameObject. If omitted, finds first Terrain."" },
                ""x"": { ""type"": ""number"", ""description"": ""World X position."" },
                ""z"": { ""type"": ""number"", ""description"": ""World Z position."" }
            },
            ""required"": [""x"", ""z""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var terrain = TerrainGetSettingsTool.FindTerrain(args);
            if (terrain == null)
                return McpToolResult.Error("No Terrain found in the scene.");

            float x = args.GetProperty("x").GetSingle();
            float z = args.GetProperty("z").GetSingle();
            float height = terrain.SampleHeight(new Vector3(x, 0, z));

            return McpToolResult.Success($"Terrain height at ({x}, {z}): Y = {height:F3}");
        }
    }

    /// <summary>
    /// Modify terrain height with brush semantics.
    /// </summary>
    public class TerrainModifyHeightTool : IMcpTool
    {
        public string Name => "terrain_modify_height";
        public string Description =>
            "Modify terrain height at a world position using brush semantics. " +
            "Modes: 'set' (absolute height), 'raise' (add height), 'lower' (subtract height), " +
            "'flatten' (flatten to target height), 'smooth' (average with neighbors). " +
            "Uses a circular brush with configurable radius and falloff.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the Terrain. If omitted, finds first Terrain."" },
                ""mode"": { ""type"": ""string"", ""description"": ""Edit mode: 'set', 'raise', 'lower', 'flatten', 'smooth'."", ""enum"": [""set"", ""raise"", ""lower"", ""flatten"", ""smooth""] },
                ""x"": { ""type"": ""number"", ""description"": ""World X position (brush center)."" },
                ""z"": { ""type"": ""number"", ""description"": ""World Z position (brush center)."" },
                ""height"": { ""type"": ""number"", ""description"": ""Target height (world units). Required for set/raise/lower/flatten."" },
                ""radius"": { ""type"": ""number"", ""description"": ""Brush radius in world units (default: 5)."" },
                ""falloff"": { ""type"": ""number"", ""description"": ""Falloff from 0 (hard edge) to 1 (smooth edge). Default: 0.5."" },
                ""strength"": { ""type"": ""number"", ""description"": ""Brush strength from 0 to 1 (default: 1). Controls how strongly the mode is applied."" }
            },
            ""required"": [""mode"", ""x"", ""z""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var terrain = TerrainGetSettingsTool.FindTerrain(args);
            if (terrain == null)
                return McpToolResult.Error("No Terrain found in the scene.");

            var data = terrain.terrainData;
            var mode = args.GetProperty("mode").GetString().ToLowerInvariant();
            float worldX = args.GetProperty("x").GetSingle();
            float worldZ = args.GetProperty("z").GetSingle();

            float height = 0f;
            if (mode != "smooth")
            {
                if (!args.TryGetProperty("height", out var hProp) || !hProp.TryGetSingle(out height))
                    return McpToolResult.Error($"'height' is required for mode '{mode}'.");
            }

            float radius = 5f;
            if (args.TryGetProperty("radius", out var rp) && rp.TryGetSingle(out var r)) radius = r;

            float falloff = 0.5f;
            if (args.TryGetProperty("falloff", out var fp) && fp.TryGetSingle(out var f)) falloff = Mathf.Clamp01(f);

            float strength = 1f;
            if (args.TryGetProperty("strength", out var sp) && sp.TryGetSingle(out var s)) strength = Mathf.Clamp01(s);

            // Convert world coords to heightmap coords
            var terrainPos = terrain.transform.position;
            var terrainSize = data.size;
            int hmRes = data.heightmapResolution;

            float normX = (worldX - terrainPos.x) / terrainSize.x;
            float normZ = (worldZ - terrainPos.z) / terrainSize.z;

            if (normX < 0 || normX > 1 || normZ < 0 || normZ > 1)
                return McpToolResult.Error($"Position ({worldX}, {worldZ}) is outside the terrain bounds.");

            float normRadius = radius / terrainSize.x;
            int pixelRadius = Mathf.CeilToInt(normRadius * hmRes);
            int centerX = Mathf.RoundToInt(normZ * (hmRes - 1)); // Note: heightmap X = world Z
            int centerY = Mathf.RoundToInt(normX * (hmRes - 1)); // heightmap Y = world X

            // Calculate the area to modify
            int startX = Mathf.Max(0, centerX - pixelRadius);
            int startY = Mathf.Max(0, centerY - pixelRadius);
            int endX = Mathf.Min(hmRes - 1, centerX + pixelRadius);
            int endY = Mathf.Min(hmRes - 1, centerY + pixelRadius);
            int sizeX = endX - startX + 1;
            int sizeY = endY - startY + 1;

            if (sizeX <= 0 || sizeY <= 0)
                return McpToolResult.Error("Brush area is empty.");

            Undo.RegisterCompleteObjectUndo(data, $"Terrain {mode} height");

            float[,] heights = data.GetHeights(startX, startY, sizeX, sizeY);
            float normalizedHeight = height / terrainSize.y;

            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    int hmX = startX + x;
                    int hmY = startY + y;

                    float distX = (hmX - centerX) / (float)pixelRadius;
                    float distY = (hmY - centerY) / (float)pixelRadius;
                    float dist = Mathf.Sqrt(distX * distX + distY * distY);

                    if (dist > 1f) continue;

                    // Calculate brush weight with falloff
                    float weight = 1f;
                    if (dist > falloff)
                        weight = 1f - (dist - falloff) / (1f - falloff);
                    weight *= strength;

                    switch (mode)
                    {
                        case "set":
                            heights[x, y] = Mathf.Lerp(heights[x, y], normalizedHeight, weight);
                            break;
                        case "raise":
                            heights[x, y] += normalizedHeight * weight;
                            break;
                        case "lower":
                            heights[x, y] -= normalizedHeight * weight;
                            break;
                        case "flatten":
                            heights[x, y] = Mathf.Lerp(heights[x, y], normalizedHeight, weight);
                            break;
                        case "smooth":
                            // Average with neighbors
                            float avg = 0f;
                            int count = 0;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    if (nx >= 0 && nx < sizeX && ny >= 0 && ny < sizeY)
                                    { avg += heights[nx, ny]; count++; }
                                }
                            }
                            avg /= count;
                            heights[x, y] = Mathf.Lerp(heights[x, y], avg, weight);
                            break;
                    }

                    heights[x, y] = Mathf.Clamp01(heights[x, y]);
                }
            }

            data.SetHeights(startX, startY, heights);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

            return McpToolResult.Success(
                $"Terrain height '{mode}' applied at ({worldX}, {worldZ}), " +
                $"radius={radius}, strength={strength}, falloff={falloff}");
        }
    }

    /// <summary>
    /// Paint terrain texture layer with brush semantics.
    /// </summary>
    public class TerrainPaintTextureTool : IMcpTool
    {
        public string Name => "terrain_paint_texture";
        public string Description =>
            "Paint a terrain texture layer at a world position. " +
            "Specify the terrain layer index and use brush radius/opacity to control painting.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the Terrain. If omitted, finds first Terrain."" },
                ""layerIndex"": { ""type"": ""integer"", ""description"": ""Index of the terrain layer to paint (0-based). Use terrain_get_settings to see available layers."" },
                ""x"": { ""type"": ""number"", ""description"": ""World X position (brush center)."" },
                ""z"": { ""type"": ""number"", ""description"": ""World Z position (brush center)."" },
                ""radius"": { ""type"": ""number"", ""description"": ""Brush radius in world units (default: 5)."" },
                ""opacity"": { ""type"": ""number"", ""description"": ""Brush opacity from 0 to 1 (default: 1)."" },
                ""falloff"": { ""type"": ""number"", ""description"": ""Falloff from 0 (hard edge) to 1 (smooth edge). Default: 0.5."" }
            },
            ""required"": [""layerIndex"", ""x"", ""z""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var terrain = TerrainGetSettingsTool.FindTerrain(args);
            if (terrain == null)
                return McpToolResult.Error("No Terrain found in the scene.");

            var data = terrain.terrainData;
            int layerIndex = args.GetProperty("layerIndex").GetInt32();

            if (layerIndex < 0 || layerIndex >= data.terrainLayers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range. Terrain has {data.terrainLayers.Length} layer(s).");

            float worldX = args.GetProperty("x").GetSingle();
            float worldZ = args.GetProperty("z").GetSingle();

            float radius = 5f;
            if (args.TryGetProperty("radius", out var rp) && rp.TryGetSingle(out var r)) radius = r;

            float opacity = 1f;
            if (args.TryGetProperty("opacity", out var op) && op.TryGetSingle(out var o)) opacity = Mathf.Clamp01(o);

            float falloff = 0.5f;
            if (args.TryGetProperty("falloff", out var fp) && fp.TryGetSingle(out var f)) falloff = Mathf.Clamp01(f);

            // Convert world coords to alphamap coords
            var terrainPos = terrain.transform.position;
            var terrainSize = data.size;
            int amRes = data.alphamapResolution;
            int numLayers = data.alphamapLayers;

            float normX = (worldX - terrainPos.x) / terrainSize.x;
            float normZ = (worldZ - terrainPos.z) / terrainSize.z;

            if (normX < 0 || normX > 1 || normZ < 0 || normZ > 1)
                return McpToolResult.Error($"Position ({worldX}, {worldZ}) is outside the terrain bounds.");

            float normRadius = radius / terrainSize.x;
            int pixelRadius = Mathf.CeilToInt(normRadius * amRes);
            int centerX = Mathf.RoundToInt(normZ * (amRes - 1));
            int centerY = Mathf.RoundToInt(normX * (amRes - 1));

            int startX = Mathf.Max(0, centerX - pixelRadius);
            int startY = Mathf.Max(0, centerY - pixelRadius);
            int endX = Mathf.Min(amRes - 1, centerX + pixelRadius);
            int endY = Mathf.Min(amRes - 1, centerY + pixelRadius);
            int sizeX = endX - startX + 1;
            int sizeY = endY - startY + 1;

            if (sizeX <= 0 || sizeY <= 0)
                return McpToolResult.Error("Brush area is empty.");

            Undo.RegisterCompleteObjectUndo(data, "Terrain Paint Texture");

            float[,,] alphamaps = data.GetAlphamaps(startX, startY, sizeX, sizeY);

            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    int amX = startX + x;
                    int amY = startY + y;

                    float distX = (amX - centerX) / (float)pixelRadius;
                    float distY = (amY - centerY) / (float)pixelRadius;
                    float dist = Mathf.Sqrt(distX * distX + distY * distY);

                    if (dist > 1f) continue;

                    float weight = 1f;
                    if (dist > falloff)
                        weight = 1f - (dist - falloff) / (1f - falloff);
                    weight *= opacity;

                    // Blend: increase target layer, proportionally decrease others
                    float currentTarget = alphamaps[x, y, layerIndex];
                    float newTarget = Mathf.Lerp(currentTarget, 1f, weight);
                    float delta = newTarget - currentTarget;

                    if (delta <= 0f) continue;

                    // Calculate the sum of other layers
                    float otherSum = 0f;
                    for (int l = 0; l < numLayers; l++)
                        if (l != layerIndex) otherSum += alphamaps[x, y, l];

                    alphamaps[x, y, layerIndex] = newTarget;

                    // Proportionally reduce other layers to maintain sum = 1
                    if (otherSum > 0f)
                    {
                        float scale = (1f - newTarget) / otherSum;
                        for (int l = 0; l < numLayers; l++)
                            if (l != layerIndex) alphamaps[x, y, l] *= scale;
                    }
                }
            }

            data.SetAlphamaps(startX, startY, alphamaps);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

            return McpToolResult.Success(
                $"Painted terrain layer [{layerIndex}] '{data.terrainLayers[layerIndex].name}' " +
                $"at ({worldX}, {worldZ}), radius={radius}, opacity={opacity}");
        }
    }

    /// <summary>
    /// Add tree instances to the terrain.
    /// </summary>
    public class TerrainAddTreesTool : IMcpTool
    {
        public string Name => "terrain_add_trees";
        public string Description =>
            "Add tree instances to the terrain at specified world positions. " +
            "Specify the tree prototype index and optional scale/variation.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the Terrain. If omitted, finds first Terrain."" },
                ""prototypeIndex"": { ""type"": ""integer"", ""description"": ""Index of the tree prototype (0-based). Use terrain_get_settings to see available prototypes."" },
                ""positions"": { ""type"": ""string"", ""description"": ""Semicolon-separated world positions: 'x1,z1;x2,z2;x3,z3'. Y is auto-sampled from terrain height."" },
                ""widthScale"": { ""type"": ""number"", ""description"": ""Width scale multiplier (default: 1)."" },
                ""heightScale"": { ""type"": ""number"", ""description"": ""Height scale multiplier (default: 1)."" },
                ""randomScaleVariation"": { ""type"": ""number"", ""description"": ""Random scale variation ± this amount (default: 0.1)."" }
            },
            ""required"": [""prototypeIndex"", ""positions""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var terrain = TerrainGetSettingsTool.FindTerrain(args);
            if (terrain == null)
                return McpToolResult.Error("No Terrain found in the scene.");

            var data = terrain.terrainData;
            int protoIndex = args.GetProperty("prototypeIndex").GetInt32();

            if (protoIndex < 0 || protoIndex >= data.treePrototypes.Length)
                return McpToolResult.Error($"Prototype index {protoIndex} out of range. Terrain has {data.treePrototypes.Length} prototype(s).");

            float widthScale = 1f, heightScale = 1f, variation = 0.1f;
            if (args.TryGetProperty("widthScale", out var ws) && ws.TryGetSingle(out var w)) widthScale = w;
            if (args.TryGetProperty("heightScale", out var hs) && hs.TryGetSingle(out var h)) heightScale = h;
            if (args.TryGetProperty("randomScaleVariation", out var rv) && rv.TryGetSingle(out var v)) variation = v;

            var positionsStr = args.GetProperty("positions").GetString();
            var posEntries = positionsStr.Split(';').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();

            if (posEntries.Length == 0)
                return McpToolResult.Error("No positions provided.");

            Undo.RegisterCompleteObjectUndo(data, "Add Trees");

            var terrainPos = terrain.transform.position;
            var terrainSize = data.size;
            int addedCount = 0;

            foreach (var posEntry in posEntries)
            {
                var parts = posEntry.Split(',').Select(float.Parse).ToArray();
                if (parts.Length < 2) continue;

                float worldX = parts[0];
                float worldZ = parts[1];

                // Normalize to terrain-local 0..1
                float normX = (worldX - terrainPos.x) / terrainSize.x;
                float normZ = (worldZ - terrainPos.z) / terrainSize.z;

                if (normX < 0 || normX > 1 || normZ < 0 || normZ > 1) continue;

                float rndW = widthScale + UnityEngine.Random.Range(-variation, variation);
                float rndH = heightScale + UnityEngine.Random.Range(-variation, variation);

                var treeInstance = new TreeInstance
                {
                    prototypeIndex = protoIndex,
                    position = new Vector3(normX, 0f, normZ), // Y is ignored, terrain uses heightmap
                    widthScale = Mathf.Max(0.1f, rndW),
                    heightScale = Mathf.Max(0.1f, rndH),
                    color = Color.white,
                    lightmapColor = Color.white,
                    rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
                };

                // Append tree — TerrainData manages the array
                var trees = data.treeInstances.ToList();
                trees.Add(treeInstance);
                data.treeInstances = trees.ToArray();
                addedCount++;
            }

            if (addedCount > 0)
            {
                terrain.Flush();
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            }

            return McpToolResult.Success(
                $"Added {addedCount} tree(s) using prototype [{protoIndex}] " +
                $"'{data.treePrototypes[protoIndex].prefab?.name ?? "unknown"}'. " +
                $"Total trees: {data.treeInstanceCount}");
        }
    }
}
