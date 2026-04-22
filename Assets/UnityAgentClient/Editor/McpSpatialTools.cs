using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Raycast from a point in a direction to detect what objects are hit.
    /// </summary>
    public class RaycastTool : IMcpTool
    {
        public string Name => "spatial_raycast";
        public string Description => "Cast a ray from a position in a direction and report what it hits. Useful for visibility checks, line-of-sight, and spatial queries.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""origin"": { ""type"": ""string"", ""description"": ""Ray origin as 'x,y,z'."" },
                ""direction"": { ""type"": ""string"", ""description"": ""Ray direction as 'x,y,z' (e.g. '0,0,1' for forward)."" },
                ""maxDistance"": { ""type"": ""number"", ""description"": ""Max ray distance (default: 100)."" },
                ""fromCamera"": { ""type"": ""boolean"", ""description"": ""If true, cast from the main camera's position in its forward direction. Ignores origin/direction."" },
                ""hitAll"": { ""type"": ""boolean"", ""description"": ""If true, return all hits along the ray (default: false, first hit only)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            Vector3 origin;
            Vector3 direction;
            float maxDistance = 100f;
            bool hitAll = false;

            if (args.TryGetProperty("maxDistance", out var md) && md.TryGetSingle(out var d)) maxDistance = d;
            if (args.TryGetProperty("hitAll", out var ha)) hitAll = ha.GetBoolean();

            if (args.TryGetProperty("fromCamera", out var fc) && fc.GetBoolean())
            {
                var cam = Camera.main;
                if (cam == null)
                    return McpToolResult.Error("No Main Camera found in scene");
                origin = cam.transform.position;
                direction = cam.transform.forward;
            }
            else
            {
                if (!args.TryGetProperty("origin", out var o) || !args.TryGetProperty("direction", out var dir))
                    return McpToolResult.Error("Provide 'origin' and 'direction', or set 'fromCamera' to true");

                var op = o.GetString().Split(',').Select(float.Parse).ToArray();
                var dp = dir.GetString().Split(',').Select(float.Parse).ToArray();
                origin = new Vector3(op[0], op[1], op[2]);
                direction = new Vector3(dp[0], dp[1], dp[2]).normalized;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Ray: origin={origin}, direction={direction}, maxDistance={maxDistance}");
            sb.AppendLine();

            if (hitAll)
            {
                var hits = Physics.RaycastAll(origin, direction, maxDistance);
                if (hits.Length == 0)
                {
                    sb.AppendLine("No hits.");
                    return McpToolResult.Success(sb.ToString());
                }

                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                sb.AppendLine($"Hits ({hits.Length}):");
                foreach (var hit in hits)
                {
                    sb.AppendLine($"  {hit.collider.gameObject.name} at distance {hit.distance:F2}, point {hit.point}, normal {hit.normal}");
                }
            }
            else
            {
                if (Physics.Raycast(origin, direction, out var hit, maxDistance))
                {
                    sb.AppendLine($"Hit: {hit.collider.gameObject.name}");
                    sb.AppendLine($"  Distance: {hit.distance:F2}");
                    sb.AppendLine($"  Point: {hit.point}");
                    sb.AppendLine($"  Normal: {hit.normal}");
                    sb.AppendLine($"  Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
                }
                else
                {
                    sb.AppendLine("No hit — ray did not intersect any collider.");
                }
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Query what a camera can see (frustum visibility check).
    /// </summary>
    public class CameraVisibilityTool : IMcpTool
    {
        public string Name => "spatial_camera_visibility";
        public string Description => "Check which GameObjects are visible from a camera's viewpoint (frustum culling check). Can also check if a specific object is visible.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""cameraPath"": { ""type"": ""string"", ""description"": ""Camera GameObject path. Defaults to Main Camera."" },
                ""checkObject"": { ""type"": ""string"", ""description"": ""Optional: check if this specific GameObject is visible from the camera."" },
                ""maxResults"": { ""type"": ""number"", ""description"": ""Max visible objects to return (default: 50)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            Camera cam;
            if (args.TryGetProperty("cameraPath", out var cp))
            {
                var go = GameObject.Find(cp.GetString());
                if (go == null)
                    return McpToolResult.Error($"Camera not found: {cp.GetString()}");
                cam = go.GetComponent<Camera>();
                if (cam == null)
                    return McpToolResult.Error($"No Camera component on: {cp.GetString()}");
            }
            else
            {
                cam = Camera.main;
                if (cam == null)
                    return McpToolResult.Error("No Main Camera found");
            }

            // Check specific object visibility
            if (args.TryGetProperty("checkObject", out var co))
            {
                var targetName = co.GetString();
                var target = GameObject.Find(targetName);
                if (target == null)
                    target = SceneToolHelpers.FindGameObjectIncludeInactive(targetName);
                if (target == null)
                    return McpToolResult.Error($"GameObject not found: {targetName}");

                var renderer = target.GetComponent<Renderer>();
                if (renderer == null)
                {
                    // No renderer — check if transform position is in frustum
                    var vp = cam.WorldToViewportPoint(target.transform.position);
                    var inFrustum = vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1 && vp.z > 0;
                    return McpToolResult.Success(
                        $"'{targetName}' position is {(inFrustum ? "VISIBLE" : "NOT VISIBLE")} from {cam.name}\n" +
                        $"  Viewport position: {vp}");
                }

                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var visible = GeometryUtility.TestPlanesAABB(planes, renderer.bounds);

                // Also check occlusion via raycast
                var dirToTarget = (renderer.bounds.center - cam.transform.position);
                var occluded = false;
                RaycastHit hit = default;
                if (visible && Physics.Raycast(cam.transform.position, dirToTarget.normalized, out hit, dirToTarget.magnitude))
                {
                    if (hit.collider.gameObject != target)
                        occluded = true;
                }

                var status = visible ? (occluded ? "IN FRUSTUM but OCCLUDED" : "VISIBLE") : "NOT VISIBLE";
                return McpToolResult.Success(
                    $"'{targetName}' is {status} from {cam.name}\n" +
                    $"  Distance: {dirToTarget.magnitude:F2}m" +
                    (occluded ? $"\n  Occluded by: {hit.collider.gameObject.name}" : ""));
            }

            // List all visible renderers
            int maxResults = 50;
            if (args.TryGetProperty("maxResults", out var mr) && mr.TryGetInt32(out var m)) maxResults = m;

            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            var allRenderers = SceneToolHelpers.FindAllActiveOnly<Renderer>();

            var sb = new StringBuilder();
            sb.AppendLine($"Camera: {cam.name} (fov: {cam.fieldOfView}, near: {cam.nearClipPlane}, far: {cam.farClipPlane})");
            sb.AppendLine();

            int count = 0;
            foreach (var r in allRenderers)
            {
                if (count >= maxResults) break;
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, r.bounds))
                {
                    var dist = Vector3.Distance(cam.transform.position, r.bounds.center);
                    sb.AppendLine($"  {r.gameObject.name} (distance: {dist:F1}m, bounds: {r.bounds.size})");
                    count++;
                }
            }

            sb.Insert(sb.ToString().IndexOf('\n') + 1, $"Visible objects: {count}\n");

            if (count == 0)
                sb.AppendLine("  (no renderers visible from this camera)");

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Check line-of-sight between two GameObjects or positions.
    /// Semantic wrapper over raycast — uses object names instead of coordinates.
    /// </summary>
    public class CheckLineOfSightTool : IMcpTool
    {
        public string Name => "spatial_check_line_of_sight";
        public string Description => "Check if there is a clear line of sight between two GameObjects (or a GameObject and a position). Returns whether the path is clear or what is blocking it.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""from"": { ""type"": ""string"", ""description"": ""Source GameObject name/path, or position as 'x,y,z'."" },
                ""to"": { ""type"": ""string"", ""description"": ""Target GameObject name/path, or position as 'x,y,z'."" }
            },
            ""required"": [""from"", ""to""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var fromStr = args.GetProperty("from").GetString();
            var toStr = args.GetProperty("to").GetString();

            var fromPos = ResolvePosition(fromStr);
            var toPos = ResolvePosition(toStr);

            if (fromPos == null)
                return McpToolResult.Error($"Cannot resolve 'from': {fromStr}");
            if (toPos == null)
                return McpToolResult.Error($"Cannot resolve 'to': {toStr}");

            var direction = toPos.Value - fromPos.Value;
            var distance = direction.magnitude;

            var sb = new StringBuilder();
            sb.AppendLine($"Line of sight: {fromStr} → {toStr}");
            sb.AppendLine($"  Distance: {distance:F2}m");

            if (Physics.Raycast(fromPos.Value, direction.normalized, out var hit, distance))
            {
                if (IsTargetObject(hit.collider.gameObject, toStr))
                {
                    sb.AppendLine($"  Result: CLEAR — direct line of sight");
                }
                else
                {
                    sb.AppendLine($"  Result: BLOCKED by '{hit.collider.gameObject.name}'");
                    sb.AppendLine($"  Blocked at distance: {hit.distance:F2}m");
                    sb.AppendLine($"  Block point: {hit.point}");
                }
            }
            else
            {
                sb.AppendLine($"  Result: CLEAR — no obstacles detected");
            }

            return McpToolResult.Success(sb.ToString());
        }

        static Vector3? ResolvePosition(string input)
        {
            // Try as coordinates first
            var parts = input.Split(',');
            if (parts.Length == 3 && float.TryParse(parts[0], out var x)
                && float.TryParse(parts[1], out var y) && float.TryParse(parts[2], out var z))
                return new Vector3(x, y, z);

            // Try as GameObject name
            var go = GameObject.Find(input);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(input);
            return go?.transform.position;
        }

        static bool IsTargetObject(GameObject hit, string targetName)
        {
            var go = GameObject.Find(targetName);
            if (go == null) go = SceneToolHelpers.FindGameObjectIncludeInactive(targetName);
            return go != null && hit == go;
        }
    }

    /// <summary>
    /// Detect all visible objects from the main camera or a specified camera.
    /// Semantic wrapper — returns a clean summary instead of raw frustum data.
    /// </summary>
    public class DetectVisibleObjectsTool : IMcpTool
    {
        public string Name => "spatial_detect_visible_objects";
        public string Description => "List all objects currently visible from a camera, grouped by distance (near/mid/far). Useful for scene analysis and spatial reasoning.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""cameraPath"": { ""type"": ""string"", ""description"": ""Camera GameObject path. Defaults to Main Camera."" },
                ""groupByDistance"": { ""type"": ""boolean"", ""description"": ""Group results by near/mid/far distance (default: true)."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            Camera cam;
            if (args.TryGetProperty("cameraPath", out var cp))
            {
                var go = GameObject.Find(cp.GetString());
                if (go == null) return McpToolResult.Error($"Camera not found: {cp.GetString()}");
                cam = go.GetComponent<Camera>();
                if (cam == null) return McpToolResult.Error($"No Camera on: {cp.GetString()}");
            }
            else
            {
                cam = Camera.main;
                if (cam == null) return McpToolResult.Error("No Main Camera found");
            }

            bool groupByDistance = true;
            if (args.TryGetProperty("groupByDistance", out var gbd))
                groupByDistance = gbd.GetBoolean();

            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            var allRenderers = SceneToolHelpers.FindAllActiveOnly<Renderer>();

            var visible = allRenderers
                .Where(r => GeometryUtility.TestPlanesAABB(frustumPlanes, r.bounds))
                .Select(r => new
                {
                    Name = r.gameObject.name,
                    Distance = Vector3.Distance(cam.transform.position, r.bounds.center),
                    Position = r.transform.position,
                    Layer = LayerMask.LayerToName(r.gameObject.layer)
                })
                .OrderBy(v => v.Distance)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Camera: {cam.name} at {cam.transform.position}");
            sb.AppendLine($"Visible objects: {visible.Count}");
            sb.AppendLine();

            if (visible.Count == 0)
            {
                sb.AppendLine("  (nothing visible)");
                return McpToolResult.Success(sb.ToString());
            }

            if (groupByDistance)
            {
                var farPlane = cam.farClipPlane;
                var nearThreshold = farPlane * 0.2f;
                var farThreshold = farPlane * 0.6f;

                var near = visible.Where(v => v.Distance < nearThreshold).ToList();
                var mid = visible.Where(v => v.Distance >= nearThreshold && v.Distance < farThreshold).ToList();
                var far = visible.Where(v => v.Distance >= farThreshold).ToList();

                if (near.Any())
                {
                    sb.AppendLine($"Near (< {nearThreshold:F0}m): {near.Count}");
                    foreach (var v in near)
                        sb.AppendLine($"  {v.Name} ({v.Distance:F1}m) [{v.Layer}]");
                }
                if (mid.Any())
                {
                    sb.AppendLine($"Mid ({nearThreshold:F0}-{farThreshold:F0}m): {mid.Count}");
                    foreach (var v in mid)
                        sb.AppendLine($"  {v.Name} ({v.Distance:F1}m) [{v.Layer}]");
                }
                if (far.Any())
                {
                    sb.AppendLine($"Far (> {farThreshold:F0}m): {far.Count}");
                    foreach (var v in far)
                        sb.AppendLine($"  {v.Name} ({v.Distance:F1}m) [{v.Layer}]");
                }
            }
            else
            {
                foreach (var v in visible)
                    sb.AppendLine($"  {v.Name} ({v.Distance:F1}m) [{v.Layer}]");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }
}
