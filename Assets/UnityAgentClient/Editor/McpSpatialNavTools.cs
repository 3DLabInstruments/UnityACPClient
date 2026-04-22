using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace UnityAgentClient
{
    /// <summary>
    /// Query NavMesh path between two points.
    /// </summary>
    public class NavMeshPathQueryTool : IMcpTool
    {
        public string Name => "navmesh_query_path";
        public string Description => "Calculate a navigation path between two points on the NavMesh. Returns whether the path is valid, its length, and waypoints.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""from"": { ""type"": ""string"", ""description"": ""Start position as 'x,y,z'."" },
                ""to"": { ""type"": ""string"", ""description"": ""Target position as 'x,y,z'."" },
                ""fromObject"": { ""type"": ""string"", ""description"": ""Alternative: use a GameObject's position as start. Overrides 'from'."" },
                ""toObject"": { ""type"": ""string"", ""description"": ""Alternative: use a GameObject's position as target. Overrides 'to'."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            Vector3 from, to;

            // Resolve start position
            if (args.TryGetProperty("fromObject", out var fo))
            {
                var go = FindGO(fo.GetString());
                if (go == null) return McpToolResult.Error($"Start object not found: {fo.GetString()}");
                from = go.transform.position;
            }
            else if (args.TryGetProperty("from", out var fp))
            {
                var p = fp.GetString().Split(',').Select(float.Parse).ToArray();
                from = new Vector3(p[0], p[1], p[2]);
            }
            else return McpToolResult.Error("Provide 'from' position or 'fromObject' name");

            // Resolve target position
            if (args.TryGetProperty("toObject", out var tobj))
            {
                var go = FindGO(tobj.GetString());
                if (go == null) return McpToolResult.Error($"Target object not found: {tobj.GetString()}");
                to = go.transform.position;
            }
            else if (args.TryGetProperty("to", out var tp))
            {
                var p = tp.GetString().Split(',').Select(float.Parse).ToArray();
                to = new Vector3(p[0], p[1], p[2]);
            }
            else return McpToolResult.Error("Provide 'to' position or 'toObject' name");

            // Snap to NavMesh
            if (!NavMesh.SamplePosition(from, out var fromHit, 5f, NavMesh.AllAreas))
                return McpToolResult.Error($"Start position {from} is not on or near the NavMesh");
            if (!NavMesh.SamplePosition(to, out var toHit, 5f, NavMesh.AllAreas))
                return McpToolResult.Error($"Target position {to} is not on or near the NavMesh");

            var path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path);

            var sb = new StringBuilder();
            sb.AppendLine($"From: {fromHit.position} → To: {toHit.position}");
            sb.AppendLine($"Path status: {path.status}");

            if (path.status == NavMeshPathStatus.PathComplete || path.status == NavMeshPathStatus.PathPartial)
            {
                // Calculate total distance
                float totalDist = 0;
                for (int i = 1; i < path.corners.Length; i++)
                    totalDist += Vector3.Distance(path.corners[i - 1], path.corners[i]);

                var directDist = Vector3.Distance(fromHit.position, toHit.position);

                sb.AppendLine($"Path length: {totalDist:F2}m (direct: {directDist:F2}m)");
                sb.AppendLine($"Waypoints ({path.corners.Length}):");
                for (int i = 0; i < path.corners.Length; i++)
                    sb.AppendLine($"  [{i}] {path.corners[i]}");

                if (path.status == NavMeshPathStatus.PathPartial)
                    sb.AppendLine("\nWARNING: Path is partial — target may not be fully reachable.");
            }
            else
            {
                sb.AppendLine("Path is INVALID — no navigable route exists between these points.");
            }

            return McpToolResult.Success(sb.ToString());
        }

        static GameObject FindGO(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) return go;
            return SceneToolHelpers.FindGameObjectIncludeInactive(name);
        }
    }

    /// <summary>
    /// Create a Texture2D from a URL or base64 data and apply it to a material.
    /// </summary>
    public class TextureInjectTool : IMcpTool
    {
        public string Name => "asset_inject_texture";
        public string Description => "Create a texture from a base64-encoded image or a local file path, save it as an asset, and optionally apply it to a material property.";
        public bool RequiresMainThread => true;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""base64"": { ""type"": ""string"", ""description"": ""Base64-encoded image data (PNG or JPG)."" },
                ""filePath"": { ""type"": ""string"", ""description"": ""Alternative: path to an image file on disk."" },
                ""savePath"": { ""type"": ""string"", ""description"": ""Where to save the texture asset (e.g. 'Assets/Textures/Generated.png')."" },
                ""applyToMaterial"": { ""type"": ""string"", ""description"": ""Optional: material asset path to apply the texture to."" },
                ""materialProperty"": { ""type"": ""string"", ""description"": ""Material property name (default: '_MainTex')."" }
            },
            ""required"": [""savePath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var savePath = args.GetProperty("savePath").GetString();
            if (!savePath.EndsWith(".png") && !savePath.EndsWith(".jpg"))
                savePath += ".png";

            byte[] imageBytes = null;

            // Source: base64
            if (args.TryGetProperty("base64", out var b64))
            {
                try
                {
                    imageBytes = Convert.FromBase64String(b64.GetString());
                }
                catch (Exception e)
                {
                    return McpToolResult.Error($"Invalid base64 data: {e.Message}");
                }
            }
            // Source: file path
            else if (args.TryGetProperty("filePath", out var fp))
            {
                var path = fp.GetString();
                if (!System.IO.File.Exists(path))
                    return McpToolResult.Error($"File not found: {path}");
                imageBytes = System.IO.File.ReadAllBytes(path);
            }
            else
            {
                return McpToolResult.Error("Provide 'base64' image data or 'filePath'");
            }

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            // Write the raw image bytes to disk
            System.IO.File.WriteAllBytes(savePath, imageBytes);
            AssetDatabase.Refresh();

            // Verify it imported
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
            if (texture == null)
                return McpToolResult.Error($"Texture saved but failed to import at: {savePath}");

            var sb = new StringBuilder();
            sb.AppendLine($"Texture created: {savePath}");
            sb.AppendLine($"  Size: {texture.width}x{texture.height}");
            sb.AppendLine($"  Format: {texture.format}");

            // Optionally apply to material
            if (args.TryGetProperty("applyToMaterial", out var matPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath.GetString());
                if (mat == null)
                    return McpToolResult.Error($"Material not found: {matPath.GetString()}");

                var propName = "_MainTex";
                if (args.TryGetProperty("materialProperty", out var mp))
                    propName = mp.GetString();

                if (!mat.HasProperty(propName))
                    return McpToolResult.Error($"Material does not have property: {propName}");

                Undo.RecordObject(mat, "Apply texture");
                mat.SetTexture(propName, texture);
                EditorUtility.SetDirty(mat);

                sb.AppendLine($"  Applied to: {matPath.GetString()} [{propName}]");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }
}
