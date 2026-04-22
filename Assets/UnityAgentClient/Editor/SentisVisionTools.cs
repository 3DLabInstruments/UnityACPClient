// Unity Agent Client — Sentis Vision Extension
// This file provides AI vision capabilities using Unity Sentis/Inference Engine.
// It auto-detects whether Sentis is installed and only activates if available.
//
// To enable: Install com.unity.sentis or com.unity.ai.inference via Package Manager,
// then add UNITY_SENTIS to Project Settings > Player > Scripting Define Symbols.
//
// Required ONNX models (place in Assets/StreamingAssets/AgentVision/):
//   - yolov8m.onnx        (object detection)
//   - depth_anything_s.onnx (depth estimation)

#if UNITY_SENTIS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

// Support both old and new namespace
#if UNITY_INFERENCE_ENGINE
using Unity.InferenceEngine;
#else
using Unity.Sentis;
#endif

namespace UnityAgentClient.Vision
{
    /// <summary>
    /// Manages Sentis model lifecycle — loading, caching, and disposal.
    /// </summary>
    public static class SentisModelManager
    {
        static readonly Dictionary<string, Worker> workers = new();
        static readonly Dictionary<string, Model> models = new();

        static readonly string ModelsPath = System.IO.Path.Combine(
            Application.streamingAssetsPath, "AgentVision");

        public static bool IsAvailable => true;

        /// <summary>
        /// Load an ONNX model and create a GPU worker. Cached after first load.
        /// </summary>
        public static Worker GetWorker(string modelName)
        {
            if (workers.TryGetValue(modelName, out var cached))
                return cached;

            var modelPath = System.IO.Path.Combine(ModelsPath, modelName);
            if (!System.IO.File.Exists(modelPath))
                throw new System.IO.FileNotFoundException($"Model not found: {modelPath}");

            var modelAsset = ModelLoader.Load(modelPath);
            models[modelName] = modelAsset;

            var worker = new Worker(modelAsset, BackendType.GPUCompute);
            workers[modelName] = worker;

            Logger.LogVerbose($"Sentis model loaded: {modelName} (GPU)");
            return worker;
        }

        /// <summary>
        /// Capture the current Scene or Game view as a Texture2D.
        /// </summary>
        public static Texture2D CaptureSceneView(int width = 640, int height = 640)
        {
            var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (cam == null)
                throw new InvalidOperationException("No camera found in scene");

            var rt = RenderTexture.GetTemporary(width, height, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return tex;
        }

        public static void Dispose()
        {
            foreach (var w in workers.Values) w.Dispose();
            workers.Clear();
            models.Clear();
        }
    }

    // ── MCP Vision Tools ──

    /// <summary>
    /// Object detection using YOLOv8.
    /// </summary>
    public class DetectObjectsTool : IMcpTool
    {
        public string Name => "vision_detect_objects";
        public string Description => "Detect objects in the current camera view using YOLOv8. Returns bounding boxes, class names, and confidence scores. Requires ONNX model: yolov8m.onnx";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""cameraPath"": { ""type"": ""string"", ""description"": ""Optional camera GameObject path. Defaults to Main Camera."" },
                ""confidenceThreshold"": { ""type"": ""number"", ""description"": ""Minimum confidence (0-1, default: 0.5)."" },
                ""maxDetections"": { ""type"": ""number"", ""description"": ""Maximum detections to return (default: 20)."" }
            }
        }").RootElement;

        // COCO class names
        static readonly string[] CocoClasses = {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
            "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
            "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
            "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
            "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
            "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
            "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake",
            "chair", "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop",
            "mouse", "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
            "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
        };

        public McpToolResult Execute(JsonElement args)
        {
            try
            {
                float threshold = 0.5f;
                int maxDet = 20;
                if (args.ValueKind != JsonValueKind.Undefined)
                {
                    if (args.TryGetProperty("confidenceThreshold", out var ct) && ct.TryGetSingle(out var t)) threshold = t;
                    if (args.TryGetProperty("maxDetections", out var md) && md.TryGetInt32(out var m)) maxDet = m;
                }

                var tex = SentisModelManager.CaptureSceneView(640, 640);
                var worker = SentisModelManager.GetWorker("yolov8m.onnx");

                // Create input tensor from texture
                using var inputTensor = TextureConverter.ToTensor(tex, 640, 640, 3);
                UnityEngine.Object.DestroyImmediate(tex);

                // Run inference
                worker.Schedule(inputTensor);
                var output = worker.PeekOutput() as Tensor<float>;
                output.ReadbackRequest();
                output.MakeReadable();

                // Parse YOLO output: [1, 84, 8400] → 8400 detections, 84 = 4 (bbox) + 80 (classes)
                var sb = new StringBuilder();
                var detections = new List<(string cls, float conf, float x, float y, float w, float h)>();

                var shape = output.shape;
                int numDetections = shape[2]; // 8400
                int numClasses = shape[1] - 4; // 80

                for (int i = 0; i < numDetections && detections.Count < maxDet; i++)
                {
                    float maxConf = 0;
                    int maxClass = 0;
                    for (int c = 0; c < numClasses; c++)
                    {
                        float conf = output[0, c + 4, i];
                        if (conf > maxConf)
                        {
                            maxConf = conf;
                            maxClass = c;
                        }
                    }

                    if (maxConf >= threshold)
                    {
                        var cx = output[0, 0, i];
                        var cy = output[0, 1, i];
                        var w = output[0, 2, i];
                        var h = output[0, 3, i];
                        var className = maxClass < CocoClasses.Length ? CocoClasses[maxClass] : $"class_{maxClass}";
                        detections.Add((className, maxConf, cx, cy, w, h));
                    }
                }

                detections.Sort((a, b) => b.conf.CompareTo(a.conf));

                sb.AppendLine($"Detected {detections.Count} object(s) (threshold: {threshold}):");
                sb.AppendLine();
                foreach (var (cls, conf, x, y, w, h) in detections)
                {
                    sb.AppendLine($"  {cls} ({conf:P0}) at center=({x:F0},{y:F0}) size=({w:F0}x{h:F0})");
                }

                if (detections.Count == 0)
                    sb.AppendLine("  (no objects detected above threshold)");

                return McpToolResult.Success(sb.ToString());
            }
            catch (System.IO.FileNotFoundException e)
            {
                return McpToolResult.Error($"Model not installed: {e.Message}\nPlace yolov8m.onnx in Assets/StreamingAssets/AgentVision/");
            }
            catch (Exception e)
            {
                return McpToolResult.Error($"Detection failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Depth estimation from camera view.
    /// </summary>
    public class EstimateDepthTool : IMcpTool
    {
        public string Name => "vision_estimate_depth";
        public string Description => "Estimate depth (distance) from the current camera view. Returns depth statistics and saves a depth map image. Requires ONNX model: depth_anything_s.onnx";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""savePath"": { ""type"": ""string"", ""description"": ""Optional: save depth map as PNG (e.g. 'Assets/DepthMaps/depth.png')."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            try
            {
                var tex = SentisModelManager.CaptureSceneView(256, 256);
                var worker = SentisModelManager.GetWorker("depth_anything_s.onnx");

                using var inputTensor = TextureConverter.ToTensor(tex, 256, 256, 3);
                UnityEngine.Object.DestroyImmediate(tex);

                worker.Schedule(inputTensor);
                var output = worker.PeekOutput() as Tensor<float>;
                output.ReadbackRequest();
                output.MakeReadable();

                // Analyze depth statistics
                float minDepth = float.MaxValue, maxDepth = float.MinValue;
                float sumDepth = 0;
                int count = output.shape.length;

                for (int i = 0; i < count; i++)
                {
                    float d = output[i];
                    if (d < minDepth) minDepth = d;
                    if (d > maxDepth) maxDepth = d;
                    sumDepth += d;
                }

                float avgDepth = sumDepth / count;
                float range = maxDepth - minDepth;

                // Analyze depth distribution (near/mid/far zones)
                int nearCount = 0, midCount = 0, farCount = 0;
                float nearThreshold = minDepth + range * 0.33f;
                float farThreshold = minDepth + range * 0.66f;

                for (int i = 0; i < count; i++)
                {
                    float d = output[i];
                    if (d < nearThreshold) nearCount++;
                    else if (d < farThreshold) midCount++;
                    else farCount++;
                }

                var sb = new StringBuilder();
                sb.AppendLine("Depth Analysis:");
                sb.AppendLine($"  Min depth: {minDepth:F3}");
                sb.AppendLine($"  Max depth: {maxDepth:F3}");
                sb.AppendLine($"  Avg depth: {avgDepth:F3}");
                sb.AppendLine($"  Range: {range:F3}");
                sb.AppendLine();
                sb.AppendLine("Depth Distribution:");
                sb.AppendLine($"  Near zone (<33%): {nearCount * 100f / count:F1}% of pixels");
                sb.AppendLine($"  Mid zone (33-66%): {midCount * 100f / count:F1}% of pixels");
                sb.AppendLine($"  Far zone (>66%): {farCount * 100f / count:F1}% of pixels");

                // Save depth map if requested
                if (args.ValueKind != JsonValueKind.Undefined && args.TryGetProperty("savePath", out var sp))
                {
                    var savePath = sp.GetString();
                    var depthTex = new Texture2D(256, 256, TextureFormat.R8, false);
                    for (int y = 0; y < 256; y++)
                    {
                        for (int x = 0; x < 256; x++)
                        {
                            float d = output[0, 0, y, x];
                            float normalized = (d - minDepth) / (range + 0.0001f);
                            depthTex.SetPixel(x, 255 - y, new Color(normalized, normalized, normalized));
                        }
                    }
                    depthTex.Apply();

                    var dir = System.IO.Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);

                    System.IO.File.WriteAllBytes(savePath, depthTex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(depthTex);
                    AssetDatabase.Refresh();
                    sb.AppendLine($"\nDepth map saved to: {savePath}");
                }

                return McpToolResult.Success(sb.ToString());
            }
            catch (System.IO.FileNotFoundException e)
            {
                return McpToolResult.Error($"Model not installed: {e.Message}\nPlace depth_anything_s.onnx in Assets/StreamingAssets/AgentVision/");
            }
            catch (Exception e)
            {
                return McpToolResult.Error($"Depth estimation failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Comprehensive scene description combining detection + depth.
    /// </summary>
    public class DescribeViewTool : IMcpTool
    {
        public string Name => "vision_describe_view";
        public string Description => "Capture the current camera view and provide a comprehensive AI description: detected objects, depth analysis, and spatial layout. Requires both yolov8m.onnx and depth_anything_s.onnx models.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""cameraPath"": { ""type"": ""string"", ""description"": ""Optional camera path. Defaults to Main Camera."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Scene View Analysis ==\n");

            // Run object detection
            var detectTool = new DetectObjectsTool();
            var detectResult = detectTool.Execute(args);
            sb.AppendLine("### Objects Detected");
            sb.AppendLine(detectResult.Text);

            // Run depth estimation
            var depthTool = new EstimateDepthTool();
            var depthResult = depthTool.Execute(args);
            sb.AppendLine("### Depth Analysis");
            sb.AppendLine(depthResult.Text);

            // Camera info
            var cam = Camera.main;
            if (cam != null)
            {
                sb.AppendLine("### Camera");
                sb.AppendLine($"  Position: {cam.transform.position}");
                sb.AppendLine($"  Rotation: {cam.transform.eulerAngles}");
                sb.AppendLine($"  FOV: {cam.fieldOfView}°");
                sb.AppendLine($"  Near/Far: {cam.nearClipPlane} / {cam.farClipPlane}");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Registers Sentis vision tools when the package is available.
    /// </summary>
    public static class SentisVisionRegistrar
    {
        public static void RegisterTools()
        {
            McpToolRegistry.Register(new DetectObjectsTool());
            McpToolRegistry.Register(new EstimateDepthTool());
            McpToolRegistry.Register(new DescribeViewTool());

            Logger.LogVerbose("Sentis vision tools registered (3 tools)");
        }

        public static void AddToSpatialMetaTool(MetaToolRouter spatial)
        {
            spatial.AddAction(new DetectObjectsTool(), "detect_objects");
            spatial.AddAction(new EstimateDepthTool(), "estimate_depth");
            spatial.AddAction(new DescribeViewTool(), "describe_view");
        }
    }
}
#endif
