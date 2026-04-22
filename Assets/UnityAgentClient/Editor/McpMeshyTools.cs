using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    // ─────────────────────────────────────────────────────────
    //  Shared Meshy API client
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Low-level Meshy API operations shared by all Meshy tools.
    /// </summary>
    internal static class MeshyApi
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
        const string BaseUrl = "https://api.meshy.ai";

        public static string ResolveApiKey()
        {
            var key = Environment.GetEnvironmentVariable("MESHY_API_KEY");
            if (!string.IsNullOrEmpty(key)) return key;

            var settingsPath = "UserSettings/UnityAgentClient/meshy_api_key.txt";
            if (File.Exists(settingsPath))
            {
                key = File.ReadAllText(settingsPath).Trim();
                if (!string.IsNullOrEmpty(key)) return key;
            }
            return null;
        }

        public static string SubmitTextTo3D(string apiKey, string prompt, string format,
            string artStyle, string negativePrompt)
        {
            var body = new JsonObject();
            body["prompt"] = prompt;
            body["output_format"] = format;
            body["art_style"] = artStyle;
            if (!string.IsNullOrEmpty(negativePrompt))
                body["negative_prompt"] = negativePrompt;

            return PostTask(apiKey, "/openapi/v2/text-to-3d", body.ToJson());
        }

        public static string SubmitImageTo3D(string apiKey, string imageBase64, string format)
        {
            var body = new JsonObject();
            body["image_url"] = $"data:image/png;base64,{imageBase64}";
            body["output_format"] = format;

            return PostTask(apiKey, "/openapi/v2/image-to-3d", body.ToJson());
        }

        /// <summary>
        /// Check a task's status. Returns (status, modelUrl).
        /// modelUrl is non-null only when status is "SUCCEEDED".
        /// Throws MeshyApiException on FAILED/EXPIRED.
        /// </summary>
        public static (string status, string modelUrl) CheckTask(
            string apiKey, string taskId, string endpoint, string preferredFormat = "glb")
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{endpoint}/{taskId}");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                throw new MeshyApiException($"Poll failed: HTTP {(int)response.StatusCode}: {responseBody}");

            var doc = JsonDocument.Parse(responseBody);
            var status = doc.RootElement.GetProperty("status").GetString();

            switch (status)
            {
                case "SUCCEEDED":
                    var modelUrls = doc.RootElement.GetProperty("model_urls");
                    string url = null;
                    if (modelUrls.TryGetProperty(preferredFormat, out var pref)) url = pref.GetString();
                    else if (modelUrls.TryGetProperty("glb", out var g)) url = g.GetString();
                    else if (modelUrls.TryGetProperty("fbx", out var f)) url = f.GetString();
                    else if (modelUrls.TryGetProperty("obj", out var o)) url = o.GetString();
                    if (url == null) throw new MeshyApiException("No model URL in response");
                    return ("SUCCEEDED", url);

                case "FAILED":
                case "EXPIRED":
                    var errorMsg = "Unknown error";
                    if (doc.RootElement.TryGetProperty("task_error", out var err))
                        errorMsg = err.TryGetProperty("message", out var msg) ? msg.GetString() : err.GetRawText();
                    throw new MeshyApiException($"Task {status}: {errorMsg}");

                default:
                    return (status, null);
            }
        }

        public static byte[] DownloadModel(string url)
        {
            var response = Http.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new MeshyApiException($"Download failed: HTTP {(int)response.StatusCode}");
            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }

        public static string SaveAndImport(byte[] modelBytes, string savePath)
        {
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(savePath, modelBytes);

            var refreshDone = new ManualResetEventSlim(false);
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                refreshDone.Set();
            };
            refreshDone.Wait(TimeSpan.FromSeconds(30));

            return savePath;
        }

        public static string ResolveSavePath(JsonElement args, string prompt, string format)
        {
            if (args.TryGetProperty("savePath", out var sp))
            {
                var path = sp.GetString();
                if (!string.IsNullOrEmpty(path))
                    return EnsureExtension(path, format);
            }

            var safeName = new string(prompt
                .ToLowerInvariant()
                .Take(40)
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray())
                .Trim('_');

            var timestamp = DateTime.Now.ToString("HHmmss");
            return $"Assets/Generated/Meshy/{safeName}_{timestamp}.{format}";
        }

        static string EnsureExtension(string path, string format)
        {
            var validExts = new[] { ".glb", ".fbx", ".obj" };
            if (validExts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                return path;
            return $"{path}.{format}";
        }

        static string PostTask(string apiKey, string endpoint, string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                throw new MeshyApiException($"HTTP {(int)response.StatusCode}: {responseBody}");

            var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.GetProperty("result").GetString();
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Task persistence — survives Domain Reload
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Persists Meshy generation tasks to Temp/ so they survive Domain Reload.
    /// After reload, pending tasks are automatically resumed via EditorApplication.update.
    /// </summary>
    [InitializeOnLoad]
    internal static class MeshyTaskManager
    {
        static readonly string PersistPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Temp", "MeshyTasks.json");

        const int PollIntervalFrames = 300; // ~5 seconds at 60fps
        static int frameCounter;

        [Serializable]
        internal class TaskEntry
        {
            public string Id { get; set; }
            public string MeshyTaskId { get; set; }
            public string Endpoint { get; set; }       // "/openapi/v2/text-to-3d"
            public string Status { get; set; }          // PENDING, IN_PROGRESS, SUCCEEDED, FAILED
            public string Prompt { get; set; }
            public string SavePath { get; set; }
            public string Format { get; set; }
            public string ModelUrl { get; set; }
            public string AssetPath { get; set; }
            public string Error { get; set; }
            public string CreatedAt { get; set; }
        }

        static List<TaskEntry> tasks = new();
        static readonly object taskLock = new();

        static MeshyTaskManager()
        {
            Load();
            if (tasks.Any(t => t.Status == "PENDING" || t.Status == "IN_PROGRESS"))
            {
                Logger.LogVerbose($"[Meshy] Resuming {tasks.Count(t => t.Status == "PENDING" || t.Status == "IN_PROGRESS")} pending task(s) after reload");
                EditorApplication.update += PollPendingTasks;
            }
        }

        public static string AddTask(string meshyTaskId, string endpoint, string prompt,
            string savePath, string format)
        {
            var entry = new TaskEntry
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                MeshyTaskId = meshyTaskId,
                Endpoint = endpoint,
                Status = "PENDING",
                Prompt = prompt,
                SavePath = savePath,
                Format = format,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };

            lock (taskLock)
            {
                tasks.Add(entry);
                Save();
            }

            // Start background polling
            EditorApplication.update -= PollPendingTasks;
            EditorApplication.update += PollPendingTasks;

            return entry.Id;
        }

        public static TaskEntry GetTask(string id)
        {
            lock (taskLock)
            {
                return tasks.FirstOrDefault(t => t.Id == id);
            }
        }

        public static IReadOnlyList<TaskEntry> GetAllTasks()
        {
            lock (taskLock) { return tasks.ToList(); }
        }

        /// <summary>
        /// Synchronously wait for a task to complete. Used by sync tools.
        /// Returns the completed task entry.
        /// </summary>
        public static TaskEntry WaitForCompletion(string id, int maxAttempts = 120, int intervalMs = 5000)
        {
            var apiKey = MeshyApi.ResolveApiKey();

            for (int i = 0; i < maxAttempts; i++)
            {
                TaskEntry entry;
                lock (taskLock) { entry = tasks.FirstOrDefault(t => t.Id == id); }
                if (entry == null) throw new MeshyApiException($"Task {id} not found");

                // Already finished (by background poller or previous attempt)
                if (entry.Status == "SUCCEEDED" || entry.Status == "FAILED")
                    return entry;

                Thread.Sleep(intervalMs);

                try
                {
                    var (status, modelUrl) = MeshyApi.CheckTask(
                        apiKey, entry.MeshyTaskId, entry.Endpoint, entry.Format);

                    lock (taskLock)
                    {
                        entry.Status = status;
                        if (status == "SUCCEEDED")
                        {
                            entry.ModelUrl = modelUrl;
                            var bytes = MeshyApi.DownloadModel(modelUrl);
                            entry.AssetPath = MeshyApi.SaveAndImport(bytes, entry.SavePath);
                        }
                        Save();
                    }

                    Logger.LogVerbose($"[Meshy] Task {id} status: {status} (attempt {i + 1}/{maxAttempts})");

                    if (status == "SUCCEEDED" || status == "FAILED")
                        return entry;
                }
                catch (MeshyApiException ex)
                {
                    lock (taskLock)
                    {
                        entry.Status = "FAILED";
                        entry.Error = ex.Message;
                        Save();
                    }
                    return entry;
                }
            }

            throw new TimeoutException();
        }

        static void PollPendingTasks()
        {
            frameCounter++;
            if (frameCounter < PollIntervalFrames) return;
            frameCounter = 0;

            var apiKey = MeshyApi.ResolveApiKey();
            if (string.IsNullOrEmpty(apiKey)) return;

            List<TaskEntry> pending;
            lock (taskLock)
            {
                pending = tasks.Where(t => t.Status == "PENDING" || t.Status == "IN_PROGRESS").ToList();
            }

            if (pending.Count == 0)
            {
                EditorApplication.update -= PollPendingTasks;
                return;
            }

            foreach (var entry in pending)
            {
                try
                {
                    var (status, modelUrl) = MeshyApi.CheckTask(
                        apiKey, entry.MeshyTaskId, entry.Endpoint, entry.Format);

                    lock (taskLock)
                    {
                        entry.Status = status;
                        if (status == "SUCCEEDED")
                        {
                            entry.ModelUrl = modelUrl;
                            // Download and import on main thread (we're already on it)
                            var bytes = MeshyApi.DownloadModel(modelUrl);
                            var dir = Path.GetDirectoryName(entry.SavePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);
                            File.WriteAllBytes(entry.SavePath, bytes);
                            AssetDatabase.Refresh();
                            entry.AssetPath = entry.SavePath;
                            Logger.LogVerbose($"[Meshy] Background task {entry.Id} completed: {entry.AssetPath}");
                        }
                        Save();
                    }
                }
                catch (MeshyApiException ex)
                {
                    lock (taskLock)
                    {
                        entry.Status = "FAILED";
                        entry.Error = ex.Message;
                        Save();
                    }
                    Logger.LogWarning($"[Meshy] Background task {entry.Id} failed: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[Meshy] Background poll error for {entry.Id}: {ex.Message}");
                }
            }
        }

        static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PersistPath, json);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[Meshy] Failed to save tasks: {e.Message}");
            }
        }

        static void Load()
        {
            try
            {
                if (!File.Exists(PersistPath)) return;
                var json = File.ReadAllText(PersistPath);
                tasks = JsonSerializer.Deserialize<List<TaskEntry>>(json) ?? new List<TaskEntry>();
                Logger.LogVerbose($"[Meshy] Loaded {tasks.Count} persisted task(s)");
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[Meshy] Failed to load tasks: {e.Message}");
                tasks = new List<TaskEntry>();
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  MCP Tools
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a 3D model from a text prompt using the Meshy API.
    /// Synchronous: blocks until the model is generated, downloaded, and imported.
    /// Tasks are persisted so they survive Domain Reload.
    /// </summary>
    public class MeshyTextTo3DTool : IMcpTool
    {
        public string Name => "meshy_text_to_3d";
        public string Description =>
            "Generate a 3D model from a text prompt using the Meshy AI API. " +
            "Blocks until the model is ready (typically 30-120 seconds). " +
            "Returns the imported asset path. " +
            "Requires MESHY_API_KEY environment variable or Unity Agent Client setting.";
        public bool RequiresMainThread => false;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""prompt"": {
                    ""type"": ""string"",
                    ""description"": ""Text description of the 3D model to generate (e.g. 'a medieval wooden chair with ornate carvings').""
                },
                ""outputFormat"": {
                    ""type"": ""string"",
                    ""description"": ""Output format: 'glb' (default), 'fbx', or 'obj'."",
                    ""enum"": [""glb"", ""fbx"", ""obj""]
                },
                ""savePath"": {
                    ""type"": ""string"",
                    ""description"": ""Asset path to save the model (e.g. 'Assets/Generated/chair.glb'). Auto-generated from prompt if omitted.""
                },
                ""artStyle"": {
                    ""type"": ""string"",
                    ""description"": ""Art style: 'realistic' (default), 'cartoon', 'low-poly', 'sculpture', 'pbr'.""
                },
                ""negativePrompt"": {
                    ""type"": ""string"",
                    ""description"": ""Things to avoid in the generation (e.g. 'low quality, blurry').""
                }
            },
            ""required"": [""prompt""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var apiKey = MeshyApi.ResolveApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return McpToolResult.Error(
                    "Meshy API key not found. Set MESHY_API_KEY environment variable, " +
                    "or add it to Unity Agent Client environment settings in " +
                    "Project Settings > Unity Agent Client > Environment Variables.");

            var prompt = args.GetProperty("prompt").GetString();
            if (string.IsNullOrWhiteSpace(prompt))
                return McpToolResult.Error("'prompt' must not be empty.");

            var format = "glb";
            if (args.TryGetProperty("outputFormat", out var fmt))
                format = fmt.GetString() ?? "glb";

            var artStyle = "realistic";
            if (args.TryGetProperty("artStyle", out var style))
                artStyle = style.GetString() ?? "realistic";

            string negativePrompt = null;
            if (args.TryGetProperty("negativePrompt", out var neg))
                negativePrompt = neg.GetString();

            var savePath = MeshyApi.ResolveSavePath(args, prompt, format);

            try
            {
                // Submit to Meshy API
                var meshyTaskId = MeshyApi.SubmitTextTo3D(apiKey, prompt, format, artStyle, negativePrompt);
                Logger.LogVerbose($"[Meshy] Task submitted: {meshyTaskId}");

                // Persist task (survives Domain Reload)
                var localId = MeshyTaskManager.AddTask(
                    meshyTaskId, "/openapi/v2/text-to-3d", prompt, savePath, format);

                // Block until done
                var entry = MeshyTaskManager.WaitForCompletion(localId);

                if (entry.Status == "FAILED")
                    return McpToolResult.Error($"Meshy generation failed: {entry.Error}");

                return McpToolResult.Success(
                    $"3D model generated and imported.\n" +
                    $"  Prompt: {prompt}\n" +
                    $"  Asset path: {entry.AssetPath}\n" +
                    $"  Format: {format}\n" +
                    $"You can now instantiate it with: unity_scene(action=\"instantiate_prefab\", prefabPath=\"{entry.AssetPath}\")");
            }
            catch (MeshyApiException ex)
            {
                return McpToolResult.Error($"Meshy API error: {ex.Message}");
            }
            catch (TimeoutException)
            {
                return McpToolResult.Error(
                    "Meshy generation timed out after 10 minutes. The model may still be processing — " +
                    "use unity_generate(action=\"list_tasks\") to check status.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Meshy] Unexpected error: {ex}");
                return McpToolResult.Error($"Meshy generation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Generate a 3D model from a reference image using the Meshy API.
    /// </summary>
    public class MeshyImageTo3DTool : IMcpTool
    {
        public string Name => "meshy_image_to_3d";
        public string Description =>
            "Generate a 3D model from a reference image using the Meshy AI API. " +
            "Blocks until the model is ready. " +
            "Requires MESHY_API_KEY environment variable or Unity Agent Client setting.";
        public bool RequiresMainThread => false;
        public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Write;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""imageBase64"": {
                    ""type"": ""string"",
                    ""description"": ""Base64-encoded reference image (PNG or JPG).""
                },
                ""imagePath"": {
                    ""type"": ""string"",
                    ""description"": ""Path to a reference image file on disk or in Assets.""
                },
                ""outputFormat"": {
                    ""type"": ""string"",
                    ""description"": ""Output format: 'glb' (default), 'fbx', or 'obj'."",
                    ""enum"": [""glb"", ""fbx"", ""obj""]
                },
                ""savePath"": {
                    ""type"": ""string"",
                    ""description"": ""Asset path to save the model (e.g. 'Assets/Generated/model.glb'). Auto-generated if omitted.""
                }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var apiKey = MeshyApi.ResolveApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return McpToolResult.Error("Meshy API key not found. Set MESHY_API_KEY environment variable.");

            string imageBase64;
            if (args.TryGetProperty("imageBase64", out var b64))
            {
                imageBase64 = b64.GetString();
            }
            else if (args.TryGetProperty("imagePath", out var ip))
            {
                var path = ip.GetString();
                if (!File.Exists(path))
                    return McpToolResult.Error($"Image file not found: {path}");
                imageBase64 = Convert.ToBase64String(File.ReadAllBytes(path));
            }
            else
            {
                return McpToolResult.Error("Provide 'imageBase64' or 'imagePath'.");
            }

            var format = "glb";
            if (args.TryGetProperty("outputFormat", out var fmt))
                format = fmt.GetString() ?? "glb";

            var savePath = $"Assets/Generated/Meshy/image_to_3d_{DateTime.Now:HHmmss}.{format}";
            if (args.TryGetProperty("savePath", out var sp) && !string.IsNullOrEmpty(sp.GetString()))
                savePath = sp.GetString();

            try
            {
                var meshyTaskId = MeshyApi.SubmitImageTo3D(apiKey, imageBase64, format);
                var localId = MeshyTaskManager.AddTask(
                    meshyTaskId, "/openapi/v2/image-to-3d", "(image)", savePath, format);

                var entry = MeshyTaskManager.WaitForCompletion(localId);

                if (entry.Status == "FAILED")
                    return McpToolResult.Error($"Meshy generation failed: {entry.Error}");

                return McpToolResult.Success(
                    $"3D model generated from image and imported.\n" +
                    $"  Asset path: {entry.AssetPath}\n" +
                    $"  Format: {format}\n" +
                    $"You can now instantiate it with: unity_scene(action=\"instantiate_prefab\", prefabPath=\"{entry.AssetPath}\")");
            }
            catch (MeshyApiException ex)
            {
                return McpToolResult.Error($"Meshy API error: {ex.Message}");
            }
            catch (TimeoutException)
            {
                return McpToolResult.Error("Meshy generation timed out. Use unity_generate(action=\"list_tasks\") to check.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Meshy] Error: {ex}");
                return McpToolResult.Error($"Meshy generation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// List Meshy generation tasks — both local (persisted) and remote (Meshy API).
    /// </summary>
    public class MeshyListTasksTool : IMcpTool
    {
        public string Name => "meshy_list_tasks";
        public string Description =>
            "List Meshy AI generation tasks. Shows local task status including any " +
            "that were interrupted by Domain Reload and automatically resumed.";
        public bool RequiresMainThread => false;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""maxCount"": {
                    ""type"": ""number"",
                    ""description"": ""Maximum number of tasks to list (default: 10).""
                }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            int maxCount = 10;
            if (args.TryGetProperty("maxCount", out var mc) && mc.TryGetInt32(out var v))
                maxCount = Math.Clamp(v, 1, 50);

            var allTasks = MeshyTaskManager.GetAllTasks();
            var sb = new StringBuilder();
            sb.AppendLine($"Meshy tasks ({allTasks.Count} total):");
            sb.AppendLine();

            var recent = allTasks.OrderByDescending(t => t.CreatedAt).Take(maxCount);
            foreach (var t in recent)
            {
                sb.AppendLine($"  [{t.Status}] {t.Prompt}");
                sb.Append($"    ID: {t.Id} | Created: {t.CreatedAt}");
                if (!string.IsNullOrEmpty(t.AssetPath))
                    sb.Append($" | Asset: {t.AssetPath}");
                if (!string.IsNullOrEmpty(t.Error))
                    sb.Append($" | Error: {t.Error}");
                sb.AppendLine();
            }

            if (!allTasks.Any())
                sb.AppendLine("  (no tasks)");

            return McpToolResult.Success(sb.ToString());
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Utilities
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Simple JSON builder to avoid pulling in extra dependencies.
    /// </summary>
    internal class JsonObject
    {
        readonly Dictionary<string, string> entries = new();

        public string this[string key]
        {
            set
            {
                if (value == null)
                    entries.Remove(key);
                else
                    entries[key] = JsonSerializer.Serialize(value);
            }
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kv in entries)
            {
                if (!first) sb.Append(',');
                sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }
    }

    internal class MeshyApiException : Exception
    {
        public MeshyApiException(string message) : base(message) { }
    }
}
