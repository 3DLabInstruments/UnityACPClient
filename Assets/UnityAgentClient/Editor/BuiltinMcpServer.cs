using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Built-in MCP server that speaks standard MCP JSON-RPC protocol over HTTP.
    /// Agents can connect via McpServerHttp or through the server.js stdio proxy.
    /// </summary>
    [InitializeOnLoad]
    public static class BuiltinMcpServer
    {
        static HttpListener listener;
        static Thread listenerThread;
        const int DefaultPort = 57123;
        const int MaxPortRetries = 10;

        public static int ActivePort { get; private set; } = DefaultPort;

        static readonly List<LogEntry> collectedLogs = new();
        static readonly object logLock = new();

        static BuiltinMcpServer()
        {
            Application.logMessageReceived += OnLogMessageReceived;
            EditorApplication.update += Initialize;
        }

        static void Initialize()
        {
            EditorApplication.update -= Initialize;
            EditorApplication.update += McpToolRegistry.ProcessMainThreadQueue;

            RegisterTools();
            SkillRegistry.LoadAll();
            StartServer();

            EditorApplication.quitting += StopServer;
        }

        static void RegisterTools()
        {
            // ── Create all tool instances ──
            var readConsole = new ReadUnityConsoleTool(collectedLogs, logLock);
            var consoleErrors = new GetConsoleErrorsTool(collectedLogs, logLock);
            var listScenes = new ListScenesTool();
            var getProjectSettings = new GetProjectSettingsTool();
            var listAssets = new ListAssetsTool();
            var readAssetInfo = new ReadAssetInfoTool();

            var getHierarchy = new GetHierarchyTool();
            var getComponentData = new GetComponentDataTool();
            var modifyComponent = new ModifyComponentTool();
            var addGameObject = new AddGameObjectTool();
            var deleteGameObject = new DeleteGameObjectTool();
            var addComponent = new AddComponentTool();
            var removeComponent = new RemoveComponentTool();
            var saveScene = new SaveSceneTool();
            var setSelection = new SetSelectionTool();
            var reparent = new ReparentGameObjectTool();
            var duplicate = new DuplicateGameObjectTool();
            var getComponentsByType = new GetComponentsByTypeTool();
            var setActive = new SetActiveTool();
            var renameGameObject = new RenameGameObjectTool();
            var setTransform = new SetTransformTool();

            var enterPlay = new EnterPlayModeTool();
            var pausePlay = new PausePlayModeTool();
            var stopPlay = new StopPlayModeTool();
            var execMenu = new ExecuteMenuItemTool();
            var getEditorState = new GetEditorStateTool();
            var openScene = new OpenSceneTool();
            var screenshot = new ScreenshotTool();
            var undo = new UndoTool();
            var redo = new RedoTool();

            var findRefs = new FindReferencesTool();
            var importSettings = new GetImportSettingsTool();
            var createPrefab = new CreatePrefabTool();
            var instPrefab = new InstantiatePrefabTool();
            var assetRename = new AssetRenameTool();
            var assetMove = new AssetMoveTool();
            var assetDelete = new AssetDeleteTool();
            var assetRefresh = new AssetRefreshTool();
            var assetCreateFolder = new AssetCreateFolderTool();
            var assetCreateMat = new AssetCreateMaterialTool();

            var matGetProps = new MaterialGetPropertiesTool();
            var matSetProp = new MaterialSetPropertyTool();
            var matAssign = new MaterialAssignTool();
            var renderSettings = new RenderingGetSettingsTool();

            var lightingSettings = new LightingGetSettingsTool();
            var lightingBake = new LightingBakeTool();
            var lightingAmbient = new LightingSetAmbientTool();

            var physicsSettings = new PhysicsGetSettingsTool();
            var tagsLayers = new TagsAndLayersTool();
            var buildProject = new BuildProjectTool();
            var setLayerCollision = new PhysicsSetLayerCollisionTool();

            var navBake = new NavMeshBakeTool();
            var navSettings = new NavMeshGetSettingsTool();

            var animControllers = new AnimationGetControllersTool();
            var animStates = new AnimationGetStatesTool();
            var animParams = new AnimationGetParametersTool();

            var uiHierarchy = new UIGetCanvasHierarchyTool();
            var uiRectTransform = new UIModifyRectTransformTool();
            var uiSetText = new UISetTextTool();

            // Semantic scene tools
            var findByCriteria = new FindByCriteriaTool();
            var sceneDescribe = new SceneDescribeTool();
            var placeOnGround = new PlaceOnGroundTool();

            // Semantic lighting tools
            var lightingTimeOfDay = new LightingSetupTimeOfDayTool();

            // Spatial tools
            var raycast = new RaycastTool();
            var cameraVisibility = new CameraVisibilityTool();
            var checkLineOfSight = new CheckLineOfSightTool();
            var detectVisible = new DetectVisibleObjectsTool();
            var navPathQuery = new NavMeshPathQueryTool();
            var textureInject = new TextureInjectTool();

            // Particle system tools
            var particleGetSettings = new ParticleGetSettingsTool();
            var particleSetMain = new ParticleSetMainTool();
            var particleSetEmission = new ParticleSetEmissionTool();
            var particleSetShape = new ParticleSetShapeTool();
            var particleSetColor = new ParticleSetColorTool();
            var particleSetSize = new ParticleSetSizeTool();
            var particleSetRenderer = new ParticleSetRendererTool();
            var particlePreview = new ParticlePreviewTool();

            // Terrain tools
            var terrainGetSettings = new TerrainGetSettingsTool();
            var terrainGetHeight = new TerrainGetHeightTool();
            var terrainModifyHeight = new TerrainModifyHeightTool();
            var terrainPaintTexture = new TerrainPaintTextureTool();
            var terrainAddTrees = new TerrainAddTreesTool();

            // Meshy AI 3D generation tools
            var meshyTextTo3D = new MeshyTextTo3DTool();
            var meshyImageTo3D = new MeshyImageTo3DTool();
            var meshyListTasks = new MeshyListTasksTool();

            // ── Register all raw tools (for direct access via unity_tool) ──
            var allTools = new IMcpTool[]
            {
                readConsole, consoleErrors, listScenes, getProjectSettings, listAssets, readAssetInfo,
                getHierarchy, getComponentData, modifyComponent, addGameObject, deleteGameObject,
                saveScene, setSelection, reparent, duplicate, getComponentsByType, setActive,
                addComponent, removeComponent, renameGameObject, setTransform,
                enterPlay, pausePlay, stopPlay, execMenu, getEditorState, openScene, screenshot,
                undo, redo,
                findRefs, importSettings, createPrefab, instPrefab, assetRename, assetMove,
                assetDelete, assetRefresh, assetCreateFolder, assetCreateMat,
                matGetProps, matSetProp, matAssign, renderSettings,
                lightingSettings, lightingBake, lightingAmbient,
                physicsSettings, tagsLayers, buildProject, setLayerCollision,
                navBake, navSettings, navPathQuery,
                animControllers, animStates, animParams,
                uiHierarchy, uiRectTransform, uiSetText,
                raycast, cameraVisibility, checkLineOfSight, detectVisible, textureInject,
                findByCriteria, sceneDescribe, placeOnGround, lightingTimeOfDay,
                particleGetSettings, particleSetMain, particleSetEmission, particleSetShape,
                particleSetColor, particleSetSize, particleSetRenderer, particlePreview,
                terrainGetSettings, terrainGetHeight, terrainModifyHeight, terrainPaintTexture, terrainAddTrees,
                meshyTextTo3D, meshyImageTo3D, meshyListTasks
            };

            foreach (var tool in allTools)
                McpToolRegistry.Register(tool);

            // ── Build meta-tools (agent-facing, 6 categories + 1 fallback) ──

            var scene = new MetaToolRouter("unity_scene",
                "Scene & GameObject operations. 'create_object' creates primitives (Cube, Sphere, Cylinder, Plane, Capsule, Empty) "
                + "with optional shape, color, position, rotation, scale, and parent — no external assets needed. "
                + "Also: get_hierarchy, get_component_data, modify_component, set_transform, rename, duplicate, reparent, delete, save.")
                .AddAction(getHierarchy, "get_hierarchy")
                .AddAction(getComponentData, "get_component_data")
                .AddAction(modifyComponent, "modify_component")
                .AddAction(addGameObject, "create_object")
                .AddAction(addGameObject, "add_gameobject")
                .AddAction(deleteGameObject, "delete_gameobject")
                .AddAction(saveScene, "save")
                .AddAction(setSelection, "set_selection")
                .AddAction(reparent, "reparent")
                .AddAction(duplicate, "duplicate")
                .AddAction(getComponentsByType, "find_by_component")
                .AddAction(setActive, "set_active")
                .AddAction(addComponent, "add_component")
                .AddAction(removeComponent, "remove_component")
                .AddAction(renameGameObject, "rename")
                .AddAction(setTransform, "set_transform")
                .AddAction(openScene, "open_scene")
                // Cross-category aliases: UI operations also accessible from scene
                .AddAction(uiSetText, "set_text")
                .AddAction(instPrefab, "instantiate_prefab")
                .AddAction(matAssign, "assign_material")
                // Semantic scene tools
                .AddAction(findByCriteria, "find_by_criteria")
                .AddAction(sceneDescribe, "describe")
                .AddAction(placeOnGround, "place_on_ground");

            var editor = new MetaToolRouter("unity_editor",
                "Editor control and project information. Play mode, undo/redo, screenshots, console, project settings, build.")
                .AddAction(enterPlay, "enter_playmode")
                .AddAction(pausePlay, "pause_playmode")
                .AddAction(stopPlay, "stop_playmode")
                .AddAction(execMenu, "execute_menu_item")
                .AddAction(getEditorState, "get_state")
                .AddAction(screenshot, "screenshot")
                .AddAction(undo, "undo")
                .AddAction(redo, "redo")
                .AddAction(readConsole, "get_console_logs")
                .AddAction(consoleErrors, "get_console_errors")
                .AddAction(getProjectSettings, "get_project_settings")
                .AddAction(buildProject, "build");

            var asset = new MetaToolRouter("unity_asset",
                "Project asset file operations (list, inspect, rename, move, delete). "
                + "For creating objects IN THE SCENE, use unity_scene(action='create_object') instead. "
                + "Do NOT search for asset files when building a scene from scratch — use primitives.")
                .AddAction(listAssets, "list")
                .AddAction(readAssetInfo, "get_info")
                .AddAction(findRefs, "find_references")
                .AddAction(importSettings, "get_import_settings")
                .AddAction(listScenes, "list_scenes")
                .AddAction(createPrefab, "create_prefab")
                .AddAction(instPrefab, "instantiate_prefab")
                .AddAction(assetRename, "rename")
                .AddAction(assetMove, "move")
                .AddAction(assetDelete, "delete")
                .AddAction(assetRefresh, "refresh")
                .AddAction(assetCreateFolder, "create_folder")
                .AddAction(assetCreateMat, "create_material")
                .AddAction(textureInject, "inject_texture");

            var material = new MetaToolRouter("unity_material",
                "Material, shader, and rendering operations. Read/modify material properties, assign materials to objects, quality and render pipeline settings.")
                .AddAction(matGetProps, "get_properties")
                .AddAction(matSetProp, "set_property")
                .AddAction(matAssign, "assign")
                .AddAction(renderSettings, "get_render_settings")
                .AddAction(assetCreateMat, "create");

            var lighting = new MetaToolRouter("unity_lighting",
                "Lighting and environment. 'setup_time_of_day' instantly sets mood with presets: "
                + "morning, noon, sunset, night, overcast. Also: get_settings, set_ambient, bake.")
                .AddAction(lightingSettings, "get_settings")
                .AddAction(lightingAmbient, "set_ambient")
                .AddAction(lightingBake, "bake")
                .AddAction(lightingTimeOfDay, "setup_time_of_day");

            var animation = new MetaToolRouter("unity_animation",
                "Animation and navigation systems. Animator controllers, state machines, parameters, NavMesh.")
                .AddAction(animControllers, "get_controllers")
                .AddAction(animStates, "get_states")
                .AddAction(animParams, "get_parameters")
                .AddAction(navBake, "navmesh_bake")
                .AddAction(navSettings, "navmesh_get_settings")
                .AddAction(navPathQuery, "navmesh_query_path");

            var spatial = new MetaToolRouter("unity_spatial",
                "Spatial queries and 3D perception. Line-of-sight checks, visible object detection, raycasting, "
                + "camera frustum analysis, navigation path queries. Use for spatial reasoning about the scene.")
                .AddAction(checkLineOfSight, "check_line_of_sight")
                .AddAction(detectVisible, "detect_visible_objects")
                .AddAction(raycast, "raycast")
                .AddAction(cameraVisibility, "camera_visibility")
                .AddAction(navPathQuery, "navmesh_query_path")
                .AddAction(textureInject, "inject_texture");

#if UNITY_SENTIS
            // Add Sentis vision capabilities to the spatial meta-tool
            UnityAgentClient.Vision.SentisVisionRegistrar.AddToSpatialMetaTool(spatial);
            Logger.LogVerbose("Sentis vision tools added to unity_spatial");
#endif

            var ui = new MetaToolRouter("unity_ui",
                "UI (Canvas/UGUI) operations. Canvas hierarchy, RectTransform editing, Text/TextMeshPro. "
                + "For modifying generic component properties on UI objects, unity_scene also works.")
                .AddAction(uiHierarchy, "get_canvas_hierarchy")
                .AddAction(uiRectTransform, "modify_rect_transform")
                .AddAction(uiSetText, "set_text")
                // Cross-category: scene ops useful for UI objects too
                .AddAction(getComponentData, "get_component_data")
                .AddAction(modifyComponent, "modify_component")
                .AddAction(setActive, "set_active");

            var generate = new MetaToolRouter("unity_generate",
                "AI-powered 3D asset generation using Meshy API. Generate 3D models from text prompts or reference images. "
                + "Models are automatically downloaded and imported into the Unity project. "
                + "Requires MESHY_API_KEY environment variable.")
                .AddAction(meshyTextTo3D, "text_to_3d")
                .AddAction(meshyImageTo3D, "image_to_3d")
                .AddAction(meshyListTasks, "list_tasks")
                // Cross-category: common follow-up actions after generation
                .AddAction(instPrefab, "instantiate_prefab")
                .AddAction(assetRefresh, "refresh");

            var particle = new MetaToolRouter("unity_particle",
                "Particle system operations. Read/modify ParticleSystem modules (main, emission, shape, "
                + "color over lifetime, size over lifetime, renderer), preview control. "
                + "Use typed actions (set_main, set_emission, set_shape, etc.) for reliable module editing.")
                .AddAction(particleGetSettings, "get_settings")
                .AddAction(particleSetMain, "set_main")
                .AddAction(particleSetEmission, "set_emission")
                .AddAction(particleSetShape, "set_shape")
                .AddAction(particleSetColor, "set_color")
                .AddAction(particleSetSize, "set_size")
                .AddAction(particleSetRenderer, "set_renderer")
                .AddAction(particlePreview, "preview");

            var terrainRouter = new MetaToolRouter("unity_terrain",
                "Terrain operations. Read terrain settings, sample height, modify heightmap with brush semantics "
                + "(set/raise/lower/flatten/smooth), paint texture layers, and place trees. "
                + "All height/position values are in world units.")
                .AddAction(terrainGetSettings, "get_settings")
                .AddAction(terrainGetHeight, "get_height")
                .AddAction(terrainModifyHeight, "modify_height")
                .AddAction(terrainPaintTexture, "paint_texture")
                .AddAction(terrainAddTrees, "add_trees");

            // Register meta-tools (these are what the agent actually sees)
            McpToolRegistry.Register(scene);
            McpToolRegistry.Register(editor);
            McpToolRegistry.Register(asset);
            McpToolRegistry.Register(material);
            McpToolRegistry.Register(lighting);
            McpToolRegistry.Register(animation);
            McpToolRegistry.Register(spatial);
            McpToolRegistry.Register(ui);
            McpToolRegistry.Register(generate);
            McpToolRegistry.Register(particle);
            McpToolRegistry.Register(terrainRouter);

            // Fallback: direct access to any tool by exact name
            McpToolRegistry.Register(new DirectToolAccessTool());

            // Skill recipes
            McpToolRegistry.Register(new SkillTool());

            // Cross-category batch operations
            McpToolRegistry.Register(new BatchTool());

            // Config tools accessible via unity_editor and direct access
            McpToolRegistry.Register(physicsSettings);
            McpToolRegistry.Register(tagsLayers);
            McpToolRegistry.Register(setLayerCollision);

            Logger.LogVerbose($"Registered {allTools.Length} tools as 13 agent-facing tools (10 categories + fallback + skills + batch)");
        }

        static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (logLock)
            {
                collectedLogs.Add(new LogEntry
                {
                    Condition = condition,
                    StackTrace = stackTrace,
                    Type = type.ToString()
                });
            }
        }

        static void StartServer()
        {
            if (listener != null) return;

            for (int i = 0; i < MaxPortRetries; i++)
            {
                int port = DefaultPort + i;
                try
                {
                    var testListener = new HttpListener();
                    testListener.Prefixes.Add($"http://localhost:{port}/");
                    testListener.Start();

                    listener = testListener;
                    ActivePort = port;

                    listenerThread = new Thread(HandleRequests) { IsBackground = true };
                    listenerThread.Start();

                    Logger.LogVerbose($"MCP Server started on port {port} ({McpToolRegistry.GetAll().Count()} tools)");
                    return;
                }
                catch (HttpListenerException)
                {
                    Logger.LogVerbose($"Port {port} in use, trying next...");
                }
                catch (Exception e)
                {
                    Logger.LogError($"Failed to start MCP Server on port {port}: {e.Message}");
                }
            }

            Logger.LogError($"Failed to start MCP Server after {MaxPortRetries} attempts");
        }

        static void StopServer()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            EditorApplication.update -= McpToolRegistry.ProcessMainThreadQueue;
            if (listener != null)
            {
                listener.Stop();
                listener.Close();
                listener = null;
                Logger.LogVerbose("MCP Server stopped");
            }
        }

        static void HandleRequests()
        {
            while (listener != null && listener.IsListening)
            {
                try
                {
                    var context = listener.GetContext();

                    if (context.Request.IsWebSocketRequest)
                    {
                        ThreadPool.QueueUserWorkItem(_ => HandleWebSocketAsync(context).Wait());
                    }
                    else
                    {
                        ThreadPool.QueueUserWorkItem(_ => ProcessHttpRequest(context));
                    }
                }
                catch (HttpListenerException) { break; }
                catch (ThreadAbortException) { break; } // Expected during Domain Reload
                catch (Exception e) { Logger.LogError($"Request error: {e.Message}"); }
            }
        }

        // ── HTTP Transport ──

        static void ProcessHttpRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.HttpMethod != "POST")
                {
                    SendHttpResponse(response, 405, "{\"error\":\"Method not allowed. Use POST with JSON-RPC or upgrade to WebSocket.\"}");
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = reader.ReadToEnd();

                var result = HandleJsonRpcMessage(body);
                if (result != null)
                    SendHttpResponse(response, 200, result);
                else
                {
                    response.StatusCode = 204;
                    response.Close();
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"HTTP processing error: {e.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        static void SendHttpResponse(HttpListenerResponse response, int statusCode, string body)
        {
            var buffer = Encoding.UTF8.GetBytes(body);
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        // ── WebSocket Transport ──

        static async System.Threading.Tasks.Task HandleWebSocketAsync(HttpListenerContext context)
        {
            WebSocket ws = null;
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                ws = wsContext.WebSocket;
                Logger.LogVerbose("WebSocket client connected");

                var receiveBuffer = new byte[8192];
                var messageBuffer = new StringBuilder();

                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuffer.Append(Encoding.UTF8.GetString(receiveBuffer, 0, result.Count));

                        if (result.EndOfMessage)
                        {
                            var message = messageBuffer.ToString();
                            messageBuffer.Clear();

                            var response = HandleJsonRpcMessage(message);
                            if (response != null)
                            {
                                var responseBytes = Encoding.UTF8.GetBytes(response);
                                await ws.SendAsync(
                                    new ArraySegment<byte>(responseBytes),
                                    WebSocketMessageType.Text,
                                    true,
                                    CancellationToken.None);
                            }
                        }
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Logger.LogVerbose($"WebSocket disconnected: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"WebSocket error: {ex.Message}");
            }
            finally
            {
                ws?.Dispose();
                Logger.LogVerbose("WebSocket client disconnected");
            }
        }

        // ── MCP JSON-RPC Protocol Handler ──

        static string HandleJsonRpcMessage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("jsonrpc", out var ver) || ver.GetString() != "2.0")
                    return CreateJsonRpcError(null, -32600, "Invalid Request: jsonrpc must be '2.0'");

                var id = root.TryGetProperty("id", out var idProp) ? idProp.Clone() : (JsonElement?)null;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                var @params = root.TryGetProperty("params", out var p) ? p.Clone() : default;

                return method switch
                {
                    "initialize" => HandleInitialize(id),
                    "notifications/initialized" => null, // notification, no response
                    "tools/list" => HandleToolsList(id),
                    "tools/call" => HandleToolsCall(id, @params),
                    _ => CreateJsonRpcError(id, -32601, $"Method not found: {method}")
                };
            }
            catch (JsonException)
            {
                return CreateJsonRpcError(null, -32700, "Parse error");
            }
            catch (Exception e)
            {
                return CreateJsonRpcError(null, -32603, $"Internal error: {e.Message}");
            }
        }

        static string HandleInitialize(JsonElement? id)
        {
            return CreateJsonRpcResult(id, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { } },
                serverInfo = new
                {
                    name = "unity-agent-client-mcp",
                    version = "0.2.0"
                }
            });
        }

        static string HandleToolsList(JsonElement? id)
        {
            // Only expose meta-tools, fallback, and skill tool to the agent (8 tools instead of 48+)
            var tools = McpToolRegistry.GetAll()
                .Where(t => t is MetaToolRouter || t is DirectToolAccessTool || t is SkillTool || t is BatchTool)
                .Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    inputSchema = t.InputSchema
                }).ToArray();

            return CreateJsonRpcResult(id, new { tools });
        }

        static string HandleToolsCall(JsonElement? id, JsonElement @params)
        {
            if (!@params.TryGetProperty("name", out var nameProp))
                return CreateJsonRpcError(id, -32602, "Invalid params: 'name' is required");

            var toolName = nameProp.GetString();

            if (!McpToolRegistry.TryGet(toolName, out var tool))
                return CreateJsonRpcError(id, -32602, $"Unknown tool: {toolName}");

            var args = @params.TryGetProperty("arguments", out var a) ? a : default;

            try
            {
                var result = McpToolRegistry.Execute(tool, args);

                return CreateJsonRpcResult(id, new
                {
                    content = new[]
                    {
                        new { type = "text", text = result.Text }
                    },
                    isError = result.IsError
                });
            }
            catch (Exception e)
            {
                Logger.LogError($"Tool '{toolName}' error: {e.Message}");
                return CreateJsonRpcResult(id, new
                {
                    content = new[]
                    {
                        new { type = "text", text = $"Error: {e.Message}" }
                    },
                    isError = true
                });
            }
        }

        // ── JSON-RPC Helpers ──

        static string CreateJsonRpcResult(JsonElement? id, object result)
        {
            var response = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["result"] = result
            };
            if (id.HasValue) response["id"] = id.Value;
            return JsonSerializer.Serialize(response);
        }

        static string CreateJsonRpcError(JsonElement? id, int code, string message)
        {
            var response = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new { code, message }
            };
            if (id.HasValue) response["id"] = id.Value;
            return JsonSerializer.Serialize(response);
        }
    }
}

