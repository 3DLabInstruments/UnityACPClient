using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Collections.Concurrent;
using AgentClientProtocol;
using UnityAgentClient.Elicitation;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System;
using System.Text;

namespace UnityAgentClient
{
    public sealed class AgentWindow : EditorWindow, IAcpClient
    {
        enum ConnectionStatus
        {
            Pending,
            Success,
            Failed,
        }

        // ── State ──
        string inputText = "";
        readonly List<SessionUpdate> messages = new();
        readonly List<UnityEngine.Object> attachedAssets = new();
        readonly ConcurrentQueue<(string SessionId, SessionUpdate Update)> pendingUpdates = new();

        ConnectionStatus connectionStatus;
        bool isConnecting;
        string lastConnectionError;
        bool isRunning;
        string sessionId;
        ClientSideConnection conn;
        Process agentProcess;

        // Tracks the config we connected with, so we can detect changes at runtime.
        string connectedCommand;
        string connectedArguments;

        // Model management
        ModelInfo[] availableModels = Array.Empty<ModelInfo>();
        int selectedModelIndex;

        // Mode management
        AgentClientProtocol.SessionMode[] availableModes = Array.Empty<AgentClientProtocol.SessionMode>();
        int selectedModeIndex;

        // Session management (multi-session, persisted via SessionStore)
        List<SessionInfo> knownSessions = new();
        bool switchingSession;
        // Cached initialize response so session-switching can build MCP config
        // without re-initializing the agent connection.
        InitializeResponse cachedInitResponse;

        // Permission management
        RequestPermissionRequest pendingPermissionRequest;
        TaskCompletionSource<RequestPermissionResponse> pendingPermissionTcs;
        bool autoApprove;

        // Auth management
        AuthMethod[] pendingAuthMethods;
        TaskCompletionSource<AuthMethod> pendingAuthTcs;

        // Elicitation management (RFD — see docs/ELICITATION.md)
        ElicitationRequest pendingElicitationRequest;
        TaskCompletionSource<ElicitationResponse> pendingElicitationTcs;

        // Usage tracking (ACP RFD — Session Usage and Context Status)
        Usage lastTurnUsage;
        long contextUsed;
        long contextSize;
        UsageCost sessionCost;

        // ── UI Toolkit references ──
        ScrollView conversationScroll;
        VisualElement scrollWrapper;
        VisualElement conversationContainer;
        TextField inputField;
        VisualElement inputArea;
        VisualElement attachmentContainer;
        VisualElement bottomComposer;
        VisualElement dragOverlay;
        VisualElement typingIndicator;
        VisualElement connectionDot;
        Label statusLabel;
        Button sendButton;
        Button stopButton;
        PopupField<string> modeField;
        PopupField<string> modelField;
        PopupField<string> sessionField;
        Button newSessionButton;
        Toggle autoApproveToggle;
        VisualElement permissionContainer;
        VisualElement authContainer;
        VisualElement elicitationContainer;
        VisualElement toolbarContainer;
        Label usageLabel;

        // ── Refresh tracking ──
        int lastRenderedMessageCount;
        string lastMessageContentHash;
        IVisualElementScheduledItem refreshSchedule;
        IVisualElementScheduledItem typingSchedule;
        bool connectInitiated;
        CancellationTokenSource connectCts;
        bool stickToBottom = true;
        bool isNarrow;
        int typingDotCount;

        [MenuItem("Window/Unity Agent Client/AI Agent")]
        static void Init()
        {
            var window = (AgentWindow)GetWindow(typeof(AgentWindow));
            window.titleContent = EditorGUIUtility.IconContent("d_Profiler.UIDetails");
            window.titleContent.text = "AI Agent";

            window.connectionStatus = ConnectionStatus.Pending;
            window.isConnecting = false;
            window.lastConnectionError = null;
            window.isRunning = false;
            window.sessionId = null;
            window.messages.Clear();
            window.attachedAssets.Clear();
            window.availableModels = Array.Empty<ModelInfo>();
            window.selectedModelIndex = 0;
            window.availableModes = Array.Empty<AgentClientProtocol.SessionMode>();
            window.selectedModeIndex = 0;
            window.pendingPermissionRequest = null;
            window.pendingPermissionTcs = null;
            window.pendingAuthMethods = null;
            window.pendingAuthTcs = null;
            window.pendingElicitationRequest = null;
            window.pendingElicitationTcs = null;
            window.connectInitiated = false;

            window.Show();
        }

        // Bottom-composer uses dynamic height (attachments + input + toolbar).
        // scrollWrapper.marginBottom is updated from composer's GeometryChangedEvent.

        // ── CreateGUI (replaces OnGUI) ──

        void CreateGUI()
        {
            try
            {
                rootVisualElement.Clear();
                lastRenderedMessageCount = 0;
                lastMessageContentHash = null;

                // Load USS
                var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(FindAssetPath("AgentWindow.uss"));
                if (uss != null) rootVisualElement.styleSheets.Add(uss);

                // Root wrapper to center the agent-root container
                var rootWrapper = new VisualElement();
                rootWrapper.style.flexGrow = 1;
                rootWrapper.style.flexDirection = FlexDirection.Row;
                rootWrapper.style.justifyContent = Justify.Center;
                rootVisualElement.Add(rootWrapper);

                // Main container
                var root = new VisualElement();
                root.AddToClassList("agent-root");
                rootWrapper.Add(root);

                // Status label
                statusLabel = new Label("Connecting...");
                statusLabel.AddToClassList("status-label");
                root.Add(statusLabel);

                // Auth container (shown when auth is needed)
                authContainer = new VisualElement();
                authContainer.style.display = DisplayStyle.None;
                root.Add(authContainer);

                // Scroll wrapper — flex grows, marginBottom reserves space for composer
                scrollWrapper = new VisualElement();
                scrollWrapper.AddToClassList("scroll-wrapper");
                scrollWrapper.style.display = DisplayStyle.None;
                root.Add(scrollWrapper);

                // Conversation scroll view
                conversationScroll = new ScrollView(ScrollViewMode.Vertical);
                conversationScroll.AddToClassList("conversation-scroll");
                scrollWrapper.Add(conversationScroll);
                // Track user scroll position to decide stickToBottom
                conversationScroll.verticalScroller.valueChanged += _ => UpdateStickToBottom();

                conversationContainer = new VisualElement();
                conversationContainer.AddToClassList("conversation-container");
                conversationScroll.Add(conversationContainer);

                // Permission container (shown inline at bottom of conversation)
                permissionContainer = new VisualElement();
                permissionContainer.style.display = DisplayStyle.None;
                conversationContainer.Add(permissionContainer);

                // Typing indicator lives outside message list (avoids index math issues)
                typingIndicator = new VisualElement();
                typingIndicator.AddToClassList("typing-indicator");
                typingIndicator.style.display = DisplayStyle.None;
                var typingLabel = new Label("Agent is thinking");
                typingLabel.name = "typing-label";
                typingIndicator.Add(typingLabel);
                conversationContainer.Add(typingIndicator);

                // Elicitation container (inline panel, below messages). Its placement
                // AFTER typingIndicator keeps `IndexOf(permissionContainer) - 1` math
                // used by UpdateLastMessageIfChanged untouched.
                elicitationContainer = new VisualElement();
                elicitationContainer.style.display = DisplayStyle.None;
                conversationContainer.Add(elicitationContainer);

                // ── Bottom composer: attachments + input + toolbar ──
                bottomComposer = new VisualElement();
                bottomComposer.AddToClassList("bottom-composer");
                bottomComposer.style.display = DisplayStyle.None;
                root.Add(bottomComposer);

                // Attachment container
                attachmentContainer = new VisualElement();
                attachmentContainer.AddToClassList("attachment-row");
                attachmentContainer.style.display = DisplayStyle.None;
                bottomComposer.Add(attachmentContainer);

                // Input area — first child of composer
                inputArea = new VisualElement();
                inputArea.AddToClassList("input-area");
                bottomComposer.Add(inputArea);

                inputField = new TextField();
                inputField.multiline = true;
                inputField.AddToClassList("input-field");
                inputField.value = inputText;
                inputField.RegisterValueChangedCallback(evt =>
                {
                    inputText = evt.newValue;
                    UpdateInputFieldHeight();
                });
                inputField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                        if (!isRunning && connectionStatus == ConnectionStatus.Success)
                            SendRequestAsync().Forget();
                    }
                });
                inputArea.Add(inputField);

                // Toolbar — last child of composer
                toolbarContainer = new VisualElement();
                toolbarContainer.AddToClassList("toolbar");
                bottomComposer.Add(toolbarContainer);

                // Dynamic reserve space below scrollWrapper based on composer size
                bottomComposer.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var h = bottomComposer.resolvedStyle.height;
                    if (float.IsNaN(h) || h <= 0) return;
                    scrollWrapper.style.marginBottom = h;
                });

                RebuildToolbar();

                // Drag overlay (pickingMode=Ignore so it doesn't eat drop events)
                dragOverlay = new VisualElement();
                dragOverlay.AddToClassList("drag-overlay");
                dragOverlay.pickingMode = PickingMode.Ignore;
                dragOverlay.style.display = DisplayStyle.None;
                var dragHint = new Label("Drop assets here to attach");
                dragOverlay.Add(dragHint);
                rootVisualElement.Add(dragOverlay);

                // Drag-and-drop
                rootVisualElement.RegisterCallback<DragEnterEvent>(_ =>
                {
                    if (connectionStatus == ConnectionStatus.Success)
                        dragOverlay.style.display = DisplayStyle.Flex;
                });
                rootVisualElement.RegisterCallback<DragLeaveEvent>(_ => dragOverlay.style.display = DisplayStyle.None);
                rootVisualElement.RegisterCallback<DragExitedEvent>(_ => dragOverlay.style.display = DisplayStyle.None);
                rootVisualElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
                rootVisualElement.RegisterCallback<DragPerformEvent>(OnDragPerform);

                // Global keyboard shortcuts: Escape → cancel active request
                rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Escape && isRunning)
                    {
                        try { CancelSessionAsync().Forget(); }
                        catch (Exception ex) { Logger.LogWarning($"Failed to cancel: {ex.Message}"); }
                        evt.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);

                // Responsive layout — toggle .narrow class on width change
                rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var w = rootVisualElement.resolvedStyle.width;
                    if (float.IsNaN(w)) return;
                    var narrow = w < 500;
                    if (narrow == isNarrow) return;
                    isNarrow = narrow;
                    if (narrow) root.AddToClassList("narrow");
                    else root.RemoveFromClassList("narrow");
                });

                // Schedule periodic refresh
                refreshSchedule = rootVisualElement.schedule.Execute(RefreshUI).Every(100);

                // Typing indicator animation
                typingSchedule = rootVisualElement.schedule.Execute(() =>
                {
                    if (typingIndicator.style.display == DisplayStyle.None) return;
                    typingDotCount = (typingDotCount + 1) % 4;
                    var lbl = typingIndicator.Q<Label>("typing-label");
                    if (lbl != null)
                        lbl.text = "Agent is thinking" + new string('.', typingDotCount);
                }).Every(350);

                // Start connection
                TryConnect();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[AgentWindow] CreateGUI FAILED: {ex}");
            }
        }

        void UpdateInputFieldHeight()
        {
            if (inputField == null) return;
            // Let UI Toolkit auto-size within min/max-height USS constraints.
            // Using style.height = auto keeps the field growing with content lines.
            inputField.style.height = StyleKeyword.Auto;
        }

        void UpdateStickToBottom()
        {
            if (conversationScroll == null) return;
            var contentH = conversationScroll.contentContainer.layout.height;
            var viewH = conversationScroll.layout.height;
            var offset = conversationScroll.scrollOffset.y;
            if (float.IsNaN(contentH) || float.IsNaN(viewH)) return;
            // If content fits entirely, treat as pinned.
            if (contentH <= viewH)
            {
                stickToBottom = true;
                return;
            }
            stickToBottom = offset >= contentH - viewH - 50;
        }

        void RebuildToolbar()
        {
            toolbarContainer.Clear();

            // Connection status dot (always first)
            connectionDot = new VisualElement();
            connectionDot.AddToClassList("connection-dot");
            UpdateConnectionDot();
            toolbarContainer.Add(connectionDot);

            // Session dropdown + "New" button (only meaningful once connected)
            if (knownSessions != null && knownSessions.Count > 0 && !string.IsNullOrEmpty(sessionId))
            {
                var orderedIds = knownSessions.Select(s => s.SessionId).ToList();
                var labels = BuildSessionLabels(knownSessions);
                var currentIdx = orderedIds.IndexOf(sessionId);
                if (currentIdx < 0) currentIdx = 0;

                sessionField = new PopupField<string>("", labels, currentIdx);
                sessionField.AddToClassList("session-dropdown");
                sessionField.style.minWidth = 140;
                sessionField.tooltip = $"Session {sessionId}";
                sessionField.RegisterValueChangedCallback(evt =>
                {
                    // Match by index (stable) — labels can collide on duplicate titles.
                    var idx = sessionField.index;
                    if (idx < 0 || idx >= orderedIds.Count) return;
                    var newId = orderedIds[idx];
                    if (newId == sessionId) return;
                    SwitchSessionAsync(newId).Forget();
                });
                toolbarContainer.Add(sessionField);
            }
            else
            {
                sessionField = null;
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                newSessionButton = new Button(() => CreateNewSessionAsync().Forget())
                {
                    text = "＋",
                    tooltip = "Start a new session with this agent",
                };
                newSessionButton.AddToClassList("session-new-button");
                toolbarContainer.Add(newSessionButton);
            }
            else
            {
                newSessionButton = null;
            }

            // Mode popup
            if (availableModes.Length > 0)
            {
                var modeLabels = availableModes
                    .Select(m => !string.IsNullOrEmpty(m.Name) ? m.Name : m.Id)
                    .ToList();
                var modeIdx = Mathf.Clamp(selectedModeIndex, 0, modeLabels.Count - 1);
                modeField = new PopupField<string>("", modeLabels, modeIdx);
                modeField.style.minWidth = 80;
                modeField.tooltip = availableModes[modeIdx].Description ?? availableModes[modeIdx].Id;
                modeField.RegisterValueChangedCallback(evt =>
                {
                    var idx = modeLabels.IndexOf(evt.newValue);
                    if (idx >= 0 && idx != selectedModeIndex)
                    {
                        selectedModeIndex = idx;
                        modeField.tooltip = availableModes[idx].Description ?? availableModes[idx].Id;
                        SetSessionModeAsync(availableModes[idx].Id).Forget();
                    }
                });
                toolbarContainer.Add(modeField);
            }
            else
            {
                modeField = null;
            }

            // Model popup
            if (availableModels.Length > 0)
            {
                var modelChoices = new List<string>(availableModels.Select(x => x.Name));
                var modelIdx = Mathf.Clamp(selectedModelIndex, 0, modelChoices.Count - 1);
                modelField = new PopupField<string>("", modelChoices, modelIdx);
                modelField.RegisterValueChangedCallback(evt =>
                {
                    var idx = new List<string>(availableModels.Select(x => x.Name)).IndexOf(evt.newValue);
                    if (idx >= 0 && idx != selectedModelIndex)
                    {
                        selectedModelIndex = idx;
                        SetSessionModelAsync(availableModels[idx].ModelId).Forget();
                    }
                });
                toolbarContainer.Add(modelField);
            }
            else
            {
                modelField = null;
            }

            // Send button
            sendButton = new Button(() => SendRequestAsync().Forget()) { text = "Send" };
            sendButton.AddToClassList("send-button");
            toolbarContainer.Add(sendButton);

            // Stop button
            stopButton = new Button(() =>
            {
                try { CancelSessionAsync().Forget(); }
                catch (Exception ex) { Logger.LogWarning($"Failed to cancel: {ex.Message}"); }
            })
            { text = "Stop" };
            stopButton.AddToClassList("send-button");
            toolbarContainer.Add(stopButton);

            // Auto-approve toggle
            autoApproveToggle = new Toggle("Auto Approve");
            autoApproveToggle.AddToClassList("autoapprove-toggle");
            autoApproveToggle.value = autoApprove;
            autoApproveToggle.RegisterValueChangedCallback(evt => autoApprove = evt.newValue);
            autoApproveToggle.tooltip = "Auto Approve — skip permission prompts";
            toolbarContainer.Add(autoApproveToggle);

            // Usage label (token + context info)
            usageLabel = new Label();
            usageLabel.AddToClassList("usage-label");
            usageLabel.style.display = DisplayStyle.None;
            toolbarContainer.Add(usageLabel);
            UpdateUsageLabel();

            UpdateToolbarState();
        }

        void UpdateConnectionDot()
        {
            if (connectionDot == null) return;
            connectionDot.RemoveFromClassList("connection-dot--connected");
            connectionDot.RemoveFromClassList("connection-dot--pending");
            connectionDot.RemoveFromClassList("connection-dot--failed");
            switch (connectionStatus)
            {
                case ConnectionStatus.Success:
                    connectionDot.AddToClassList("connection-dot--connected");
                    connectionDot.tooltip = "Connected";
                    break;
                case ConnectionStatus.Pending:
                    connectionDot.AddToClassList("connection-dot--pending");
                    connectionDot.tooltip = isConnecting ? "Connecting..." : "Waiting";
                    break;
                case ConnectionStatus.Failed:
                    connectionDot.AddToClassList("connection-dot--failed");
                    connectionDot.tooltip = string.IsNullOrEmpty(lastConnectionError)
                        ? "Connection failed"
                        : $"Connection failed: {lastConnectionError}";
                    break;
            }
        }

        void UpdateUsageLabel()
        {
            if (usageLabel == null) return;

            var parts = new List<string>();

            // Token usage from last prompt turn
            if (lastTurnUsage != null)
            {
                parts.Add($"⬆{FormatTokenCount(lastTurnUsage.InputTokens)} ⬇{FormatTokenCount(lastTurnUsage.OutputTokens)}");
            }

            // Context window from usage_update
            if (contextSize > 0)
            {
                var pct = (int)((double)contextUsed / contextSize * 100);
                parts.Add($"ctx {pct}%");
            }

            // Cost
            if (sessionCost != null)
            {
                parts.Add($"{sessionCost.Currency} {sessionCost.Amount:F4}");
            }

            if (parts.Count > 0)
            {
                usageLabel.text = string.Join("  ·  ", parts);
                usageLabel.tooltip = BuildUsageTooltip();
                usageLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                usageLabel.style.display = DisplayStyle.None;
            }
        }

        string BuildUsageTooltip()
        {
            var sb = new StringBuilder();
            if (lastTurnUsage != null)
            {
                sb.AppendLine($"Input tokens: {lastTurnUsage.InputTokens:N0}");
                sb.AppendLine($"Output tokens: {lastTurnUsage.OutputTokens:N0}");
                sb.AppendLine($"Total tokens: {lastTurnUsage.TotalTokens:N0}");
                if (lastTurnUsage.ThoughtTokens.HasValue)
                    sb.AppendLine($"Thought tokens: {lastTurnUsage.ThoughtTokens.Value:N0}");
                if (lastTurnUsage.CachedReadTokens.HasValue)
                    sb.AppendLine($"Cache read: {lastTurnUsage.CachedReadTokens.Value:N0}");
                if (lastTurnUsage.CachedWriteTokens.HasValue)
                    sb.AppendLine($"Cache write: {lastTurnUsage.CachedWriteTokens.Value:N0}");
            }
            if (contextSize > 0)
                sb.AppendLine($"Context: {contextUsed:N0} / {contextSize:N0}");
            if (sessionCost != null)
                sb.AppendLine($"Cost: {sessionCost.Currency} {sessionCost.Amount:F4}");
            return sb.ToString().TrimEnd();
        }

        static string FormatTokenCount(long tokens)
        {
            if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:F1}M";
            if (tokens >= 1_000) return $"{tokens / 1_000.0:F1}K";
            return tokens.ToString();
        }

        // ── Connection Management ──

        void TryConnect()
        {
            if (conn != null || connectInitiated) return;
            var settings = AgentSettingsProvider.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.Command))
            {
                ShowSettingsPrompt();
                return;
            }
            connectInitiated = true;
            ConnectWithRetryAsync(settings).Forget();
        }

        void ShowSettingsPrompt()
        {
            statusLabel.text = "No agent has been configured.";
            statusLabel.style.display = DisplayStyle.Flex;
            scrollWrapper.style.display = DisplayStyle.None;
            if (bottomComposer != null) bottomComposer.style.display = DisplayStyle.None;

            var existingBtn = rootVisualElement.Q<Button>("settings-prompt-btn");
            if (existingBtn == null)
            {
                var settingsBtn = new Button(() =>
                {
                    SettingsService.OpenProjectSettings("Project/Unity Agent Client");
                })
                { text = "Open Project Settings...", name = "settings-prompt-btn" };
                statusLabel.parent.Add(settingsBtn);
            }
        }

        // ── Periodic Refresh (replaces OnGUI + OnInspectorUpdate) ──

        void RefreshUI()
        {
            // 0. Detect config changes while connected — auto-reconnect.
            if (conn != null && !string.IsNullOrEmpty(connectedCommand))
            {
                var currentSettings = AgentSettingsProvider.Load();
                if (currentSettings != null &&
                    (currentSettings.Command != connectedCommand ||
                     currentSettings.Arguments != connectedArguments))
                {
                    Logger.LogVerbose("Agent config changed — reconnecting…");
                    ResetWindowState();
                    TryConnect();
                    return;
                }
            }

            // 1. Drain pending updates from background thread
            DrainPendingUpdates();

            // 2. Check connection status
            if (conn == null && !connectInitiated)
            {
                TryConnect();
                return;
            }

            // 3. Check for process death
            if (agentProcess != null && agentProcess.HasExited && connectionStatus == ConnectionStatus.Success)
            {
                Logger.LogWarning("Agent process exited unexpectedly, attempting to reconnect...");
                conn = null;
                connectionStatus = ConnectionStatus.Pending;
                connectInitiated = false;
                TryConnect();
                return;
            }

            // 4. Update status label
            UpdateStatusUI();

            // 5. Append new messages
            bool contentChanged = false;
            if (messages.Count > lastRenderedMessageCount)
            {
                for (int i = lastRenderedMessageCount; i < messages.Count; i++)
                {
                    var element = CreateMessageElement(messages[i]);
                    if (element != null)
                    {
                        // Insert before the permission container
                        int insertIdx = conversationContainer.IndexOf(permissionContainer);
                        if (insertIdx >= 0)
                            conversationContainer.Insert(insertIdx, element);
                        else
                            conversationContainer.Add(element);
                    }
                }
                lastRenderedMessageCount = messages.Count;
                contentChanged = true;
            }

            // 6. Update last message if it changed (streaming)
            if (UpdateLastMessageIfChanged()) contentChanged = true;

            // 7. Show/hide permission UI
            UpdatePermissionUI();

            // 8. Show/hide auth UI
            UpdateAuthUI();

            // 8b. Show/hide elicitation UI
            UpdateElicitationUI();

            // 9. Update toolbar state
            UpdateToolbarState();

            // 9b. Update usage display
            UpdateUsageLabel();

            // 10. Typing indicator (dedicated footer)
            if (typingIndicator != null)
            {
                typingIndicator.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // 11. Auto-scroll: only if user was pinned to bottom before content changed
            if (contentChanged && stickToBottom)
            {
                AutoScroll();
            }
        }

        void UpdateStatusUI()
        {
            UpdateConnectionDot();
            switch (connectionStatus)
            {
                case ConnectionStatus.Pending:
                    statusLabel.style.display = DisplayStyle.Flex;
                    bottomComposer.style.display = DisplayStyle.None;
                    if (pendingAuthMethods != null)
                    {
                        statusLabel.text = "";
                        authContainer.style.display = DisplayStyle.Flex;
                        scrollWrapper.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        statusLabel.text = isConnecting ? "Connecting..." : "Waiting...";
                        scrollWrapper.style.display = DisplayStyle.None;
                    }

                    // Always show cancel + settings buttons during pending state
                    if (rootVisualElement.Q<Button>("cancel-connect-btn") == null)
                    {
                        var cancelBtn = new Button(() =>
                        {
                            connectCts?.Cancel();
                            // Force fail state immediately so user isn't stuck
                            connectionStatus = ConnectionStatus.Failed;
                            lastConnectionError = "Connection cancelled.";
                            isConnecting = false;
                        })
                        { text = "Cancel", name = "cancel-connect-btn" };
                        statusLabel.parent.Add(cancelBtn);

                        var settingsBtn = new Button(() =>
                        {
                            connectCts?.Cancel();
                            connectionStatus = ConnectionStatus.Failed;
                            lastConnectionError = "Connection cancelled.";
                            isConnecting = false;
                            SettingsService.OpenProjectSettings("Project/Unity Agent Client");
                        })
                        { text = "Open Settings...", name = "pending-settings-btn" };
                        statusLabel.parent.Add(settingsBtn);
                    }
                    return;

                case ConnectionStatus.Failed:
                    // Remove pending-state buttons
                    rootVisualElement.Q<Button>("cancel-connect-btn")?.RemoveFromHierarchy();
                    rootVisualElement.Q<Button>("pending-settings-btn")?.RemoveFromHierarchy();

                    statusLabel.style.display = DisplayStyle.Flex;
                    bottomComposer.style.display = DisplayStyle.None;
                    statusLabel.text = string.IsNullOrEmpty(lastConnectionError)
                        ? "Connection Error"
                        : $"Connection Error: {lastConnectionError}";
                    scrollWrapper.style.display = DisplayStyle.None;

                    // Show retry + settings buttons if not already visible
                    if (rootVisualElement.Q<Button>("retry-btn") == null)
                    {
                        var retryBtn = new Button(() =>
                        {
                            conn = null;
                            connectionStatus = ConnectionStatus.Pending;
                            connectInitiated = false;
                            rootVisualElement.Q<Button>("retry-btn")?.RemoveFromHierarchy();
                            rootVisualElement.Q<Button>("failed-settings-btn")?.RemoveFromHierarchy();
                            TryConnect();
                        })
                        { text = "Retry", name = "retry-btn" };
                        statusLabel.parent.Add(retryBtn);

                        var settingsBtn = new Button(() =>
                        {
                            SettingsService.OpenProjectSettings("Project/Unity Agent Client");
                        })
                        { text = "Open Settings...", name = "failed-settings-btn" };
                        statusLabel.parent.Add(settingsBtn);
                    }
                    return;

                case ConnectionStatus.Success:
                    statusLabel.style.display = DisplayStyle.None;
                    authContainer.style.display = DisplayStyle.None;
                    scrollWrapper.style.display = DisplayStyle.Flex;
                    bottomComposer.style.display = DisplayStyle.Flex;

                    // Show empty state placeholder when no messages
                    var placeholder = conversationContainer.Q<VisualElement>("empty-state");
                    if (messages.Count == 0 && placeholder == null)
                    {
                        var emptyState = new VisualElement();
                        emptyState.name = "empty-state";
                        emptyState.style.flexGrow = 1;
                        emptyState.style.alignItems = Align.Center;
                        emptyState.style.justifyContent = Justify.Center;

                        var hint = new Label("Type a message below to start a conversation.");
                        hint.style.color = new Color(0.55f, 0.55f, 0.55f, 1f);
                        hint.style.fontSize = 13;
                        hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                        emptyState.Add(hint);

                        conversationContainer.Insert(0, emptyState);
                    }
                    else if (messages.Count > 0 && placeholder != null)
                    {
                        placeholder.RemoveFromHierarchy();
                    }

                    // Remove all connection-state buttons
                    rootVisualElement.Q<Button>("retry-btn")?.RemoveFromHierarchy();
                    rootVisualElement.Q<Button>("failed-settings-btn")?.RemoveFromHierarchy();
                    rootVisualElement.Q<Button>("cancel-connect-btn")?.RemoveFromHierarchy();
                    rootVisualElement.Q<Button>("pending-settings-btn")?.RemoveFromHierarchy();
                    rootVisualElement.Q<Button>("settings-prompt-btn")?.RemoveFromHierarchy();
                    break;
            }
        }

        bool UpdateLastMessageIfChanged()
        {
            if (messages.Count == 0) return false;
            var lastMsg = messages[^1];
            var hash = ComputeMessageHash(lastMsg);
            if (hash == lastMessageContentHash) return false;
            lastMessageContentHash = hash;

            // Remove and re-add the last message element.
            // Message elements are inserted before `permissionContainer`, so the last
            // rendered message is at index (IndexOf(permissionContainer) - 1).
            if (lastRenderedMessageCount == messages.Count)
            {
                int permIdx = conversationContainer.IndexOf(permissionContainer);
                int lastMsgIdx = permIdx >= 0 ? permIdx - 1 : -1;
                if (lastMsgIdx >= 0)
                {
                    conversationContainer.RemoveAt(lastMsgIdx);
                    var newElement = CreateMessageElement(lastMsg);
                    if (newElement != null)
                    {
                        int insertIdx = conversationContainer.IndexOf(permissionContainer);
                        if (insertIdx >= 0)
                            conversationContainer.Insert(insertIdx, newElement);
                        else
                            conversationContainer.Add(newElement);
                    }
                    return true;
                }
            }
            return false;
        }

        static string ComputeMessageHash(SessionUpdate update)
        {
            if (update is AgentMessageChunkSessionUpdate agentMsg)
            {
                var content = agentMsg.Content;
                if (content is TextContentBlock text) return $"agent:{text.Text?.Length}:{text.Text?.GetHashCode()}";
            }
            else if (update is AgentThoughtChunkSessionUpdate thoughtMsg)
            {
                var content = thoughtMsg.Content;
                if (content is TextContentBlock text) return $"thought:{text.Text?.Length}:{text.Text?.GetHashCode()}";
            }
            else if (update is ToolCallSessionUpdate toolCall)
            {
                return $"tool:{toolCall.Title}:{toolCall.Content?.Length}:{toolCall.RawOutput?.ToString().Length}";
            }
            return update.GetHashCode().ToString();
        }

        void UpdatePermissionUI()
        {
            if (pendingPermissionRequest != null)
            {
                if (permissionContainer.childCount == 0)
                {
                    BuildPermissionUI();
                }
                permissionContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                permissionContainer.style.display = DisplayStyle.None;
                permissionContainer.Clear();
            }
        }

        void BuildPermissionUI()
        {
            permissionContainer.Clear();

            var panel = new VisualElement();
            panel.AddToClassList("message-permission");

            var header = new Label("Permission Request");
            header.AddToClassList("bold-label");
            panel.Add(header);

            try
            {
                var toolCall = JsonSerializer.Deserialize<ToolCallSessionUpdate>(
                    (JsonElement)pendingPermissionRequest.ToolCall,
                    AcpJsonSerializerContext.Default.Options);

                var codeLabel = new Label(toolCall.Title);
                codeLabel.AddToClassList("code-block");
                panel.Add(codeLabel);
            }
            catch { }

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("permission-buttons");

            foreach (var option in pendingPermissionRequest.Options)
            {
                var buttonLabel = option.Kind switch
                {
                    PermissionOptionKind.AllowOnce => "Allow",
                    PermissionOptionKind.AllowAlways => "Allow Always",
                    PermissionOptionKind.RejectOnce => "Reject",
                    PermissionOptionKind.RejectAlways => "Reject Always",
                    _ => option.OptionId,
                };

                var optId = option.OptionId;
                var btn = new Button(() => HandlePermissionResponse(optId)) { text = buttonLabel };
                buttonRow.Add(btn);
            }

            panel.Add(buttonRow);
            permissionContainer.Add(panel);
        }

        void UpdateElicitationUI()
        {
            if (pendingElicitationRequest != null)
            {
                if (elicitationContainer.childCount == 0)
                {
                    var req = pendingElicitationRequest;
                    var panel = ElicitationPanel.Build(req, HandleElicitationAction);
                    elicitationContainer.Clear();
                    elicitationContainer.Add(panel);
                    panel.Focus();
                }
                elicitationContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                elicitationContainer.style.display = DisplayStyle.None;
                elicitationContainer.Clear();
            }
        }

        void HandleElicitationAction(ElicitationResponse response)
        {
            var tcs = pendingElicitationTcs;
            pendingElicitationRequest = null;
            pendingElicitationTcs = null;
            elicitationContainer.Clear();
            elicitationContainer.style.display = DisplayStyle.None;
            tcs?.TrySetResult(response ?? ElicitationResponse.Cancel());
        }

        void UpdateAuthUI()
        {
            if (pendingAuthMethods == null)
            {
                authContainer.style.display = DisplayStyle.None;
                authContainer.Clear();
                return;
            }

            if (authContainer.childCount > 0) return; // already built

            authContainer.style.display = DisplayStyle.Flex;
            authContainer.Clear();

            var panel = new VisualElement();
            panel.AddToClassList("message-auth");

            var header = new Label("Authentication Required");
            header.AddToClassList("bold-label");
            panel.Add(header);

            foreach (var authMethod in pendingAuthMethods)
            {
                var methodPanel = new VisualElement();
                methodPanel.AddToClassList("message-auth");

                var nameLabel = new Label(authMethod.Name);
                nameLabel.AddToClassList("bold-label");
                methodPanel.Add(nameLabel);

                if (!string.IsNullOrEmpty(authMethod.Description))
                {
                    var descLabel = new Label(authMethod.Description);
                    descLabel.style.whiteSpace = WhiteSpace.Normal;
                    methodPanel.Add(descLabel);
                }

                var method = authMethod;
                var selectBtn = new Button(() => HandleAuthResponse(method)) { text = "Select" };
                selectBtn.style.width = 80;
                methodPanel.Add(selectBtn);

                panel.Add(methodPanel);
            }

            authContainer.Add(panel);
        }

        void UpdateToolbarState()
        {
            if (sendButton == null) return;
            bool busy = isRunning || switchingSession;
            sendButton.SetEnabled(!busy && connectionStatus == ConnectionStatus.Success);
            stopButton.SetEnabled(isRunning);
            modeField?.SetEnabled(!busy);
            modelField?.SetEnabled(!busy);
            sessionField?.SetEnabled(!busy);
            newSessionButton?.SetEnabled(!busy);
            sendButton.style.display = isRunning ? DisplayStyle.None : DisplayStyle.Flex;
            stopButton.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void AutoScroll()
        {
            var sv = conversationScroll;
            if (sv == null) return;
            // Defer one frame so layout settles before scrolling to the new bottom.
            rootVisualElement.schedule.Execute(() =>
            {
                sv.scrollOffset = new Vector2(0, float.MaxValue);
                stickToBottom = true;
            });
        }

        // ── Thread-safe update draining ──

        void DrainPendingUpdates()
        {
            while (pendingUpdates.TryDequeue(out var item))
            {
                // Drop updates for sessions other than the currently-active one.
                // This protects us from stale chunks landing in a session the
                // user has just switched away from.
                if (!string.IsNullOrEmpty(item.SessionId) &&
                    !string.IsNullOrEmpty(sessionId) &&
                    item.SessionId != sessionId)
                {
                    continue;
                }
                ApplyUpdate(item.Update);
            }
        }

        void ApplyUpdate(SessionUpdate update)
        {
            if (update is AgentMessageChunkSessionUpdate agentMessageChunk)
            {
                if (messages.Count > 0 && messages[^1] is AgentMessageChunkSessionUpdate lastAgentMessage)
                {
                    var lastText = GetContentText(lastAgentMessage.Content);
                    var currentText = GetContentText(agentMessageChunk.Content);
                    messages[^1] = new AgentMessageChunkSessionUpdate
                    {
                        Content = new TextContentBlock
                        {
                            Text = lastText + currentText,
                        }
                    };
                    return;
                }
            }
            else if (update is AgentThoughtChunkSessionUpdate agentThoughtChunk)
            {
                if (messages.Count > 0 && messages[^1] is AgentThoughtChunkSessionUpdate lastAgentThought)
                {
                    var lastText = GetContentText(lastAgentThought.Content);
                    var currentText = GetContentText(agentThoughtChunk.Content);
                    messages[^1] = new AgentThoughtChunkSessionUpdate
                    {
                        Content = new TextContentBlock
                        {
                            Text = lastText + currentText,
                        }
                    };
                    return;
                }
            }
            else if (update is ToolCallUpdateSessionUpdate toolCallUpdate)
            {
                if (messages.Count > 0 && messages[^1] is ToolCallSessionUpdate lastToolCall)
                {
                    var combinedContent = lastToolCall.Content != null
                        ? new List<ToolCallContent>(lastToolCall.Content)
                        : new List<ToolCallContent>();
                    if (toolCallUpdate.Content != null)
                        combinedContent.AddRange(toolCallUpdate.Content);

                    messages[^1] = lastToolCall with
                    {
                        Status = toolCallUpdate.Status ?? lastToolCall.Status,
                        Title = toolCallUpdate.Title ?? lastToolCall.Title,
                        Content = combinedContent.ToArray(),
                        RawInput = toolCallUpdate.RawInput ?? lastToolCall.RawInput,
                        RawOutput = toolCallUpdate.RawOutput ?? lastToolCall.RawOutput,
                    };

                    return;
                }
            }
            else if (update is UsageUpdateSessionUpdate usageUpdate)
            {
                contextUsed = usageUpdate.Used;
                contextSize = usageUpdate.Size;
                sessionCost = usageUpdate.Cost;
                return;
            }

            messages.Add(update);
        }

        // ── Message Element Creation ──

        VisualElement CreateMessageElement(SessionUpdate update)
        {
            switch (update)
            {
                case UserMessageChunkSessionUpdate userMessage:
                    return CreateUserMessageElement(userMessage);
                case AgentMessageChunkSessionUpdate agentMessage:
                    return CreateAgentMessageElement(agentMessage);
                case AgentThoughtChunkSessionUpdate agentThought:
                    return CreateAgentThoughtElement(agentThought);
                case ToolCallSessionUpdate toolCall:
                    return CreateToolCallElement(toolCall);
                case ToolCallUpdateSessionUpdate:
                    return null; // merged into ToolCallSessionUpdate
                case PlanSessionUpdate plan:
                    return CreatePlanElement(plan);
                case AvailableCommandsUpdateSessionUpdate availableCommands:
                    return CreateAvailableCommandsElement(availableCommands);
                case CurrentModeUpdateSessionUpdate currentMode:
                    return CreateCurrentModeElement(currentMode);
                case ConfigOptionUpdateSessionUpdate:
                case SessionInfoUpdateSessionUpdate:
                case UsageUpdateSessionUpdate:
                    return null; // no UI needed
                default:
                    var unknown = new Label($"Unknown update type: {update.GetType().Name}");
                    return unknown;
            }
        }

        VisualElement CreateUserMessageElement(UserMessageChunkSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-user");

            if (update.Content is ResourceLinkContentBlock resourceLink)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                var clip = new Label("\ud83d\udcce");
                clip.style.width = 18;
                row.Add(clip);

                if (resourceLink.Uri.StartsWith("file://"))
                {
                    var path = Path.GetRelativePath(Path.Combine(Application.dataPath, ".."), resourceLink.Uri[7..]);
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (asset != null)
                    {
                        var objField = new ObjectField();
                        objField.objectType = asset.GetType();
                        objField.value = asset;
                        objField.SetEnabled(false);
                        row.Add(objField);
                    }
                    else
                    {
                        var lbl = new Label(resourceLink.Name ?? resourceLink.Uri);
                        lbl.style.whiteSpace = WhiteSpace.Normal;
                        row.Add(lbl);
                    }
                }
                else
                {
                    var lbl = new Label(resourceLink.Name ?? resourceLink.Uri);
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(lbl);
                }

                container.Add(row);
            }
            else
            {
                var text = GetContentText(update.Content);
                container.Add(MarkdownVisualBuilder.Build(text));
            }

            return container;
        }

        VisualElement CreateAgentMessageElement(AgentMessageChunkSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-agent");
            var text = GetContentText(update.Content);
            container.Add(MarkdownVisualBuilder.Build(text));
            return container;
        }

        VisualElement CreateAgentThoughtElement(AgentThoughtChunkSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-thought");

            var foldout = new Foldout { text = "Thinking..." };
            foldout.value = false;
            foldout.AddToClassList("foldout-header");

            var text = GetContentText(update.Content);
            foldout.Add(MarkdownVisualBuilder.Build(text));
            container.Add(foldout);
            return container;
        }

        VisualElement CreateToolCallElement(ToolCallSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-tool-call");

            var foldout = new Foldout { text = update.Title ?? "Tool Call" };
            foldout.value = false;
            foldout.AddToClassList("foldout-header");

            var inputLabel = new Label("Input");
            inputLabel.AddToClassList("bold-label");
            foldout.Add(inputLabel);

            var inputCode = new Label(update.RawInput?.ToString() ?? "");
            inputCode.AddToClassList("code-block");
            inputCode.style.whiteSpace = WhiteSpace.Normal;
            foldout.Add(inputCode);

            if (update.RawOutput != null)
            {
                var outputLabel = new Label("Output");
                outputLabel.AddToClassList("bold-label");
                foldout.Add(outputLabel);

                var outputCode = new Label(update.RawOutput.ToString());
                outputCode.AddToClassList("code-block");
                outputCode.style.whiteSpace = WhiteSpace.Normal;
                foldout.Add(outputCode);
            }

            if (update.Content != null)
            {
                var contentLabel = new Label("Content");
                contentLabel.AddToClassList("bold-label");
                foldout.Add(contentLabel);

                foreach (var content in update.Content)
                {
                    var contentText = content switch
                    {
                        ContentToolCallContent c => GetContentText(c.Content),
                        DiffToolCallContent diff => diff.Path,
                        _ => content.ToString(),
                    };
                    foldout.Add(MarkdownVisualBuilder.Build(contentText));
                }
            }

            container.Add(foldout);
            return container;
        }

        VisualElement CreatePlanElement(PlanSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-plan");

            var foldout = new Foldout { text = "Plan" };
            foldout.value = false;
            foldout.AddToClassList("foldout-header");

            foreach (var entry in update.Entries)
            {
                var entryLabel = new Label($"- {entry}");
                entryLabel.style.whiteSpace = WhiteSpace.Normal;
                foldout.Add(entryLabel);
            }

            container.Add(foldout);
            return container;
        }

        VisualElement CreateAvailableCommandsElement(AvailableCommandsUpdateSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-tool-call");

            var foldout = new Foldout { text = "Available Commands" };
            foldout.value = false;
            foldout.AddToClassList("foldout-header");

            foreach (var cmd in update.AvailableCommands)
            {
                var cmdLabel = new Label($"{cmd.Name} - {cmd.Description}");
                cmdLabel.style.whiteSpace = WhiteSpace.Normal;
                foldout.Add(cmdLabel);
            }

            container.Add(foldout);
            return container;
        }

        VisualElement CreateCurrentModeElement(CurrentModeUpdateSessionUpdate update)
        {
            var container = new VisualElement();
            container.AddToClassList("message-mode");

            var mode = availableModes.FirstOrDefault(m => m.Id == update.CurrentModeId);
            var displayName = mode != null && !string.IsNullOrEmpty(mode.Name) ? mode.Name : update.CurrentModeId;

            var label = new Label($"Current Mode: {displayName}");
            label.AddToClassList("bold-label");
            container.Add(label);

            return container;
        }

        // ── Drag and Drop ──

        void OnDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        }

        void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj != null && !attachedAssets.Contains(obj))
                    attachedAssets.Add(obj);
            }
            RefreshAttachments();
            if (dragOverlay != null) dragOverlay.style.display = DisplayStyle.None;
            evt.StopPropagation();
        }

        void RefreshAttachments()
        {
            attachmentContainer.Clear();
            if (attachedAssets.Count == 0)
            {
                attachmentContainer.style.display = DisplayStyle.None;
                return;
            }
            attachmentContainer.style.display = DisplayStyle.Flex;

            for (int i = 0; i < attachedAssets.Count; i++)
            {
                var asset = attachedAssets[i];
                if (asset == null) continue;

                int idx = i;
                var chip = new VisualElement();
                chip.AddToClassList("attachment-chip");

                // Icon (asset preview or default icon)
                var iconImg = new Image();
                iconImg.AddToClassList("attachment-chip-icon");
                var content = EditorGUIUtility.ObjectContent(asset, asset.GetType());
                if (content?.image != null)
                {
                    iconImg.image = content.image;
                    iconImg.scaleMode = ScaleMode.ScaleToFit;
                }
                chip.Add(iconImg);

                var label = new Label(asset.name);
                label.AddToClassList("attachment-chip-label");
                label.tooltip = AssetDatabase.GetAssetPath(asset) is string p && !string.IsNullOrEmpty(p) ? p : asset.name;
                chip.Add(label);

                // Click on chip → ping asset in Project window
                chip.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button == 0)
                        EditorGUIUtility.PingObject(asset);
                });

                var removeBtn = new Button(() =>
                {
                    if (idx < attachedAssets.Count)
                        attachedAssets.RemoveAt(idx);
                    RefreshAttachments();
                })
                { text = "\u00d7" };
                removeBtn.AddToClassList("attachment-remove-btn");
                removeBtn.tooltip = "Remove attachment";
                chip.Add(removeBtn);

                attachmentContainer.Add(chip);
            }
        }

        // ── Permission / Auth handlers ──

        void HandlePermissionResponse(string optionId)
        {
            var selectedOption = pendingPermissionRequest.Options
                .FirstOrDefault(o => o.OptionId == optionId);
            if (selectedOption?.Kind == PermissionOptionKind.AllowAlways)
            {
                try
                {
                    var toolCall = JsonSerializer.Deserialize<ToolCallSessionUpdate>(
                        (JsonElement)pendingPermissionRequest.ToolCall,
                        AcpJsonSerializerContext.Default.Options);
                    if (toolCall != null)
                    {
                        McpToolRegistry.GrantPermission(toolCall.Title);
                        Logger.LogVerbose($"Granted permanent permission for: {toolCall.Title}");
                    }
                }
                catch { }
            }

            pendingPermissionTcs.TrySetResult(new RequestPermissionResponse
            {
                Outcome = new SelectedRequestPermissionOutcome
                {
                    OptionId = optionId,
                }
            });

            pendingPermissionRequest = null;
            pendingPermissionTcs = null;
        }

        void HandleAuthResponse(AuthMethod authMethod)
        {
            pendingAuthTcs.TrySetResult(authMethod);
            pendingAuthMethods = null;
            pendingAuthTcs = null;
        }

        // ── USS asset path helper ──

        static string FindAssetPath(string filename)
        {
            var packagePath = $"Packages/com.yetsmarch.unity-agent-client/Editor/{filename}";
            if (File.Exists(Path.GetFullPath(packagePath))) return packagePath;
            var assetsPath = $"Assets/UnityAgentClient/Editor/{filename}";
            if (File.Exists(Path.GetFullPath(assetsPath))) return assetsPath;
            var guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(filename) + " t:StyleSheet");
            if (guids.Length > 0) return AssetDatabase.GUIDToAssetPath(guids[0]);
            return assetsPath;
        }

        // ── Lifecycle ──

        void Disconnect()
        {
            // Save the active session to the store so it can be resumed later.
            if (!string.IsNullOrEmpty(sessionId))
            {
                var settings = AgentSettingsProvider.Load();
                if (settings != null)
                    SessionStore.Touch(settings.Command, settings.Arguments, sessionId);
            }

            connectCts?.Cancel();
            connectCts = null;

            if (agentProcess != null && !agentProcess.HasExited)
            {
                // Try a graceful shutdown first: close stdin so the agent sees
                // EOF and exits cleanly. Fall back to Kill() after a short wait.
                try { agentProcess.StandardInput?.Close(); }
                catch (Exception ex) { Logger.LogVerbose($"stdin close: {ex.Message}"); }

                bool exited = false;
                try { exited = agentProcess.WaitForExit(1500); }
                catch { }

                if (!exited)
                {
                    try { agentProcess.Kill(); }
                    catch (Exception ex) { Logger.LogVerbose($"Kill failed: {ex.Message}"); }
                    Logger.LogVerbose("Disconnected (forced)");
                }
                else
                {
                    Logger.LogVerbose("Disconnected (graceful)");
                }
            }

            conn = null;
            agentProcess = null;
            sessionId = null;
            isRunning = false;
            pendingAuthMethods = null;
            pendingAuthTcs = null;
            cachedInitResponse = null;
            // Unblock any in-flight elicitation waiting on the reader thread.
            pendingElicitationTcs?.TrySetResult(ElicitationResponse.Cancel());
            pendingElicitationRequest = null;
            pendingElicitationTcs = null;

            AssemblyReloadEvents.beforeAssemblyReload -= Disconnect;
        }

        void ResetWindowState()
        {
            Disconnect();
            connectionStatus = ConnectionStatus.Pending;
            isConnecting = false;
            lastConnectionError = null;
            messages.Clear();
            attachedAssets.Clear();
            lastRenderedMessageCount = 0;
            lastMessageContentHash = null;
            connectInitiated = false;
            availableModels = Array.Empty<ModelInfo>();
            selectedModelIndex = 0;
            availableModes = Array.Empty<AgentClientProtocol.SessionMode>();
            selectedModeIndex = 0;
            pendingPermissionRequest = null;
            pendingPermissionTcs = null;
            // Any in-flight elicitation is treated as cancelled on reset.
            pendingElicitationTcs?.TrySetResult(ElicitationResponse.Cancel());
            pendingElicitationRequest = null;
            pendingElicitationTcs = null;
            lastTurnUsage = null;
            contextUsed = 0;
            contextSize = 0;
            sessionCost = null;
            autoApprove = false;
            inputText = "";
            knownSessions = new List<SessionInfo>();
            connectedCommand = null;
            connectedArguments = null;
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }

        void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Disconnect;
        }

        void OnDisable()
        {
            Disconnect();
        }

        // ── Connection ──

        async Task ConnectAsync(AgentSettings config, CancellationToken cancellationToken = default)
        {
            Disconnect();

            var startInfo = new ProcessStartInfo
            {
#if UNITY_EDITOR_OSX
                FileName = "/bin/zsh",
                Arguments = $"-cl '{config.Command} {config.Arguments}'",
#else
                FileName = config.Command,
                Arguments = config.Arguments,
#endif
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var kv in config.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[kv.Key] = kv.Value;
            }

            Logger.Log($"Starting agent process: {startInfo.FileName} {startInfo.Arguments}");
            agentProcess = Process.Start(startInfo);

            if (agentProcess == null || agentProcess.HasExited)
            {
                throw new InvalidOperationException(
                    $"Failed to start agent process: {startInfo.FileName} {startInfo.Arguments}");
            }

            Logger.Log($"Agent process started (PID {agentProcess.Id}). Sending initialize…");

            Task.Run(() =>
            {
                var line = agentProcess.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(line))
                {
                    UnityEngine.Debug.LogError($"[Agent stderr] {line}");
                    connectionStatus = ConnectionStatus.Failed;
                }
            }, cancellationToken).Forget();

            conn = new ClientSideConnection(_ => this, agentProcess.StandardOutput, agentProcess.StandardInput);
            conn.Open();

            var packageVersion = ReadPackageVersion();

            var initRes = await conn.InitializeAsync(new()
            {
                ProtocolVersion = 1,
                ClientCapabilities = new()
                {
                    Fs = new()
                    {
                        ReadTextFile = true,
                        WriteTextFile = true,
                    },
                    Elicitation = new()
                    {
                        Form = JsonSerializer.SerializeToElement(new { }),
                        Url = JsonSerializer.SerializeToElement(new { }),
                    }
                },
                ClientInfo = new()
                {
                    Name = "UnityAgentClient",
                    Version = packageVersion,
                }
            }, cancellationToken);

            cachedInitResponse = initRes;
            Logger.Log($"Connected to agent '{initRes.AgentInfo?.Name}'");

            if (initRes.AuthMethods != null && initRes.AuthMethods.Length >= 1)
            {
                if (initRes.AuthMethods.Length == 1)
                {
                    await conn.AuthenticateAsync(new()
                    {
                        MethodId = initRes.AuthMethods[0].Id,
                    }, cancellationToken);

                    Logger.Log($"Authenticated with method: {initRes.AuthMethods[0].Id}");
                }
                else
                {
                    pendingAuthMethods = initRes.AuthMethods;
                    pendingAuthTcs = new TaskCompletionSource<AuthMethod>(TaskCreationOptions.RunContinuationsAsynchronously);

                    var selectedAuthMethod = await pendingAuthTcs.Task;

                    await conn.AuthenticateAsync(new()
                    {
                        MethodId = selectedAuthMethod.Id,
                    }, cancellationToken);

                    Logger.Log($"Authenticated with method: {selectedAuthMethod.Id}");
                }
            }

            var mcpServers = BuildMcpServerConfig(initRes);

            // Try to resume the most-recently-active session for this config.
            // If none exists, or session/load fails, fall through to session/new.
            var mostRecent = SessionStore.GetMostRecent(config.Command, config.Arguments);
            bool sessionRestored = false;

            if (mostRecent != null && initRes.AgentCapabilities?.LoadSession != false)
            {
                try
                {
                    var loadResponse = await conn.LoadSessionAsync(new()
                    {
                        SessionId = mostRecent.SessionId,
                        Cwd = Application.dataPath,
                        McpServers = mcpServers,
                    }, cancellationToken);

                    sessionId = mostRecent.SessionId;
                    sessionRestored = true;

                    if (loadResponse.Models?.AvailableModels != null && loadResponse.Models.AvailableModels.Length > 0)
                    {
                        availableModels = loadResponse.Models.AvailableModels;
                        selectedModelIndex = availableModels
                            .Select((model, index) => (model, index))
                            .Where(x => x.model.ModelId == loadResponse.Models.CurrentModelId)
                            .FirstOrDefault()
                            .index;
                    }

                    if (loadResponse.Modes?.AvailableModes != null && loadResponse.Modes.AvailableModes.Length > 0)
                    {
                        availableModes = loadResponse.Modes.AvailableModes;
                        selectedModeIndex = availableModes
                            .Select((mode, index) => (mode, index))
                            .Where(x => x.mode.Id == loadResponse.Modes.CurrentModeId)
                            .FirstOrDefault()
                            .index;
                    }

                    SessionStore.Touch(config.Command, config.Arguments, sessionId);
                    Logger.Log($"Session restored ({sessionId}).");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Could not restore session: {ex.Message}. Starting new session.");
                    SessionStore.Remove(config.Command, config.Arguments, mostRecent.SessionId);
                }
            }

            if (!sessionRestored)
            {
                var newSession = await conn.NewSessionAsync(new()
                {
                    Cwd = Application.dataPath,
                    McpServers = mcpServers,
                }, cancellationToken);

                sessionId = newSession.SessionId;

                if (newSession.Models?.AvailableModels != null && newSession.Models.AvailableModels.Length > 0)
                {
                    availableModels = newSession.Models.AvailableModels;
                    selectedModelIndex = availableModels
                        .Select((model, index) => (model, index))
                        .Where(x => x.model.ModelId == newSession.Models.CurrentModelId)
                        .First()
                        .index;
                }

                if (newSession.Modes?.AvailableModes != null && newSession.Modes.AvailableModes.Length > 0)
                {
                    availableModes = newSession.Modes.AvailableModes;
                    selectedModeIndex = availableModes
                        .Select((mode, index) => (mode, index))
                        .Where(x => x.mode.Id == newSession.Modes.CurrentModeId)
                        .FirstOrDefault()
                        .index;
                }

                SessionStore.Upsert(config.Command, config.Arguments, sessionId, title: null);
                Logger.Log($"Session created ({sessionId}).");
            }

            // Sync the known-sessions cache for the UI.
            knownSessions = SessionStore.LoadAll(config.Command, config.Arguments);

            connectionStatus = ConnectionStatus.Success;
            connectedCommand = config.Command;
            connectedArguments = config.Arguments;

            // Rebuild toolbar now that modes/models/sessions are available
            RebuildToolbar();
        }

        static string ResolveServerJsPath()
        {
            var candidates = new[]
            {
                Path.Combine("Packages", "com.yetsmarch.unity-agent-client", "Editor", "server.js"),
                Path.Combine(Application.dataPath, "UnityAgentClient", "Editor", "server.js"),
            };

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;

            foreach (var candidate in candidates)
            {
                var fullPath = Path.IsPathRooted(candidate) ? candidate : Path.Combine(projectRoot, candidate);
                if (File.Exists(fullPath))
                    return Path.GetFullPath(fullPath);
            }

            var packagePath = Path.GetFullPath("Packages/com.yetsmarch.unity-agent-client");
            if (Directory.Exists(packagePath))
            {
                var serverJs = Path.Combine(packagePath, "Editor", "server.js");
                if (File.Exists(serverJs))
                    return serverJs;
            }

            Logger.LogWarning("Could not resolve server.js path, using fallback search");
            var found = Directory.GetFiles(Application.dataPath, "server.js", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains("UnityAgentClient"));
            if (found != null) return Path.GetFullPath(found);

            throw new FileNotFoundException("Cannot find server.js for the built-in MCP server.");
        }

        static string ReadPackageVersion()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine("Packages", "com.yetsmarch.unity-agent-client", "package.json"),
                    Path.Combine(Application.dataPath, "UnityAgentClient", "package.json"),
                };

                var projectRoot = Directory.GetParent(Application.dataPath).FullName;

                foreach (var candidate in candidates)
                {
                    var fullPath = Path.IsPathRooted(candidate) ? candidate : Path.Combine(projectRoot, candidate);
                    if (File.Exists(fullPath))
                    {
                        var json = File.ReadAllText(fullPath);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("version", out var versionProp))
                            return versionProp.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to read package version: {ex.Message}");
            }

            return "0.1.0";
        }

        async Task ConnectWithRetryAsync(AgentSettings config, int maxRetries = 3)
        {
            if (isConnecting)
                return;

            isConnecting = true;
            lastConnectionError = null;
            connectCts?.Cancel();
            connectCts = new CancellationTokenSource();
            var token = connectCts.Token;

            try
            {
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                        await ConnectAsync(config, timeoutCts.Token);
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // User cancelled — don't retry
                        Disconnect();
                        lastConnectionError = "Connection cancelled by user.";
                        connectionStatus = ConnectionStatus.Failed;
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout — treat as a failed attempt
                        Disconnect();
                        lastConnectionError = "Connection timed out (30s). The agent process may not be responding.";
                        Logger.LogWarning($"Connection attempt {attempt + 1}/{maxRetries + 1} timed out.");
                        connectionStatus = ConnectionStatus.Failed;

                        if (attempt < maxRetries)
                        {
                            connectionStatus = ConnectionStatus.Pending;
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), token);
                        }
                    }
                    catch (Exception ex)
                    {
                        Disconnect();
                        lastConnectionError = ex.Message;
                        Logger.LogWarning($"Connection attempt {attempt + 1}/{maxRetries + 1} failed: {ex.Message}");
                        connectionStatus = ConnectionStatus.Failed;

                        if (attempt < maxRetries)
                        {
                            connectionStatus = ConnectionStatus.Pending;
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), token);
                        }
                    }
                }

                connectionStatus = ConnectionStatus.Failed;
            }
            catch (OperationCanceledException)
            {
                Disconnect();
                lastConnectionError = "Connection cancelled by user.";
                connectionStatus = ConnectionStatus.Failed;
            }
            finally
            {
                isConnecting = false;
            }
        }

        static McpServer[] BuildMcpServerConfig(InitializeResponse initRes)
        {
            var port = BuiltinMcpServer.ActivePort;

            if (initRes?.AgentCapabilities?.McpCapabilities?.Http == true)
            {
                Logger.LogVerbose("Agent supports HTTP MCP — connecting directly (no Node.js)");
                return new McpServer[]
                {
                    new HttpMcpServer
                    {
                        Name = "unity-agent-client-mcp",
                        Url = $"http://localhost:{port}/",
                        Headers = Array.Empty<HttpHeader>()
                    }
                };
            }

            var serverJsPath = ResolveServerJsPath();
            Logger.LogVerbose("Using stdio MCP proxy via server.js");
            return new McpServer[]
            {
                new StdioMcpServer
                {
                    Command = "node",
                    Args = new string[] { serverJsPath },
                    Env = new EnvVariable[]
                    {
                        new() { Name = "UNITY_MCP_PORT", Value = port.ToString() }
                    },
                    Name = "unity-agent-client-mcp"
                }
            };
        }

        string GetContentText(ContentBlock content)
        {
            if (content == null) return "";

            var result = content switch
            {
                TextContentBlock text => text.Text,
                ResourceLinkContentBlock resourceLink => $"[{resourceLink.Name ?? "Resource"}]({resourceLink.Uri})",
                _ => content.ToString()
            };

            return result;
        }

        // ── Send / Model / Mode / Cancel ──

        async Task SendRequestAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(inputText)) return;

            // Derive a human-readable title the first time the user sends a
            // message in this session (before we mutate inputText). The store
            // ignores empty/null titles, so we only set it when missing.
            if (!string.IsNullOrEmpty(sessionId))
            {
                var existing = knownSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (existing == null || string.IsNullOrWhiteSpace(existing.Title))
                {
                    var settings = AgentSettingsProvider.Load();
                    if (settings != null)
                    {
                        var title = SessionStore.TitleFromMessage(inputText);
                        SessionStore.SetTitle(settings.Command, settings.Arguments, sessionId, title);
                        knownSessions = SessionStore.LoadAll(settings.Command, settings.Arguments);
                        // Refresh the dropdown label.
                        RebuildToolbar();
                    }
                }
            }

            isRunning = true;
            try
            {
                messages.Add(new UserMessageChunkSessionUpdate
                {
                    Content = new TextContentBlock
                    {
                        Text = inputText,
                    }
                });

                var promptBlocks = new List<ContentBlock>
                {
                    new TextContentBlock { Text = inputText }
                };

                foreach (var asset in attachedAssets)
                {
                    if (asset == null) continue;

                    var assetPath = AssetDatabase.GetAssetPath(asset);
                    string uri;

                    if (string.IsNullOrEmpty(assetPath))
                    {
                        var gameObject = asset as GameObject;
                        if (gameObject != null && gameObject.scene.IsValid())
                        {
                            var scenePath = gameObject.scene.path;
                            var instanceId = gameObject.GetInstanceID();
                            uri = new Uri($"file://{Path.GetFullPath(scenePath)}?instanceID={instanceId}").AbsoluteUri;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        var fullPath = Path.GetFullPath(assetPath);
                        uri = new Uri(fullPath).AbsoluteUri;
                    }

                    var resourceLink = new ResourceLinkContentBlock
                    {
                        Name = asset.name,
                        Uri = uri
                    };

                    promptBlocks.Add(resourceLink);

                    messages.Add(new UserMessageChunkSessionUpdate
                    {
                        Content = resourceLink
                    });
                }

                var request = new PromptRequest
                {
                    SessionId = sessionId,
                    Prompt = promptBlocks.ToArray(),
                };

                inputText = "";
                if (inputField != null) inputField.value = "";
                attachedAssets.Clear();
                RefreshAttachments();

                var promptResponse = await conn.PromptAsync(request, cancellationToken);
                if (promptResponse.Usage != null)
                    lastTurnUsage = promptResponse.Usage;
            }
            catch (AgentClientProtocol.AcpException ex) when (ex.Code == -32042)
            {
                Logger.LogWarning("Agent requires authorization (-32042). Check agent configuration.");
                messages.Add(new AgentMessageChunkSessionUpdate
                {
                    Content = new TextContentBlock
                    {
                        Text = "⚠ Authorization required — the agent returned error -32042. Please check your agent configuration and credentials."
                    }
                });
            }
            catch (AgentClientProtocol.AcpException ex)
            {
                Logger.LogError($"Agent error ({ex.Code}): {ex.Message}");
                messages.Add(new AgentMessageChunkSessionUpdate
                {
                    Content = new TextContentBlock
                    {
                        Text = $"⚠ Agent error ({ex.Code}): {ex.Message}"
                    }
                });
            }
            finally
            {
                isRunning = false;
            }
        }

        async Task SetSessionModelAsync(string modelId, CancellationToken cancellationToken = default)
        {
            isRunning = true;
            try
            {
                await conn.SetSessionModelAsync(new()
                {
                    SessionId = sessionId,
                    ModelId = modelId,
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to change model: {ex.Message}");
            }
            finally
            {
                isRunning = false;
            }
        }

        async Task SetSessionModeAsync(string modeId, CancellationToken cancellationToken = default)
        {
            isRunning = true;
            try
            {
                await conn.SetSessionModeAsync(new()
                {
                    SessionId = sessionId,
                    ModeId = modeId,
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to change mode: {ex.Message}");
            }
            finally
            {
                isRunning = false;
            }
        }

        async Task CancelSessionAsync(CancellationToken cancellationToken = default)
        {
            if (!isRunning) return;
            try
            {
                await conn.CancelAsync(new()
                {
                    SessionId = sessionId,
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to cancel session: {ex.Message}");
            }
            finally
            {
                isRunning = false;
            }
        }

        // ── Session manager ──

        /// <summary>
        /// Build display labels for the session dropdown. Uses each session's
        /// title as-is (so ordering changes don't shuffle the visible label),
        /// appending a short id suffix only when multiple sessions share the
        /// same title.
        /// </summary>
        static List<string> BuildSessionLabels(List<SessionInfo> sessions)
        {
            var baseTitles = sessions
                .Select(s => string.IsNullOrWhiteSpace(s.Title) ? "New Session" : s.Title)
                .ToList();

            var titleCounts = baseTitles
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count());

            var result = new List<string>(sessions.Count);
            for (int i = 0; i < sessions.Count; i++)
            {
                var title = baseTitles[i];
                if (titleCounts[title] > 1)
                {
                    var suffix = sessions[i].SessionId;
                    if (suffix != null && suffix.Length > 6)
                        suffix = suffix.Substring(0, 6);
                    result.Add($"{title} ({suffix})");
                }
                else
                {
                    result.Add(title);
                }
            }
            return result;
        }

        /// <summary>
        /// Detects the "session already loaded" error some agents return
        /// (e.g. opencode) when session/load targets a session the agent
        /// already holds in memory. Treated as a benign no-op rather than a
        /// real failure.
        /// </summary>
        static bool IsAlreadyLoadedError(Exception ex)
        {
            if (ex == null) return false;
            var msg = ex.Message ?? "";
            return msg.IndexOf("already loaded", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("already active", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void ClearConversationUI()
        {
            // Drop message elements from the conversation DOM. Messages are
            // always Inserted before permissionContainer (see RefreshUI), so
            // everything in [0, permIdx) is a rendered message element and
            // safe to remove. The persistent children (permissionContainer,
            // typingIndicator, elicitationContainer) stay in place.
            if (conversationContainer != null && permissionContainer != null)
            {
                var permIdx = conversationContainer.IndexOf(permissionContainer);
                while (permIdx > 0)
                {
                    conversationContainer.RemoveAt(0);
                    permIdx--;
                }
                // Also remove any empty-state placeholder.
                var emptyState = conversationContainer.Q<VisualElement>("empty-state");
                emptyState?.RemoveFromHierarchy();
            }

            messages.Clear();
            attachedAssets.Clear();
            lastRenderedMessageCount = 0;
            lastMessageContentHash = null;
            pendingPermissionRequest = null;
            pendingPermissionTcs = null;
            pendingElicitationTcs?.TrySetResult(ElicitationResponse.Cancel());
            pendingElicitationRequest = null;
            pendingElicitationTcs = null;
            lastTurnUsage = null;
            contextUsed = 0;
            contextSize = 0;
            sessionCost = null;
            UpdatePermissionUI();
            UpdateElicitationUI();
        }

        async Task SwitchSessionAsync(string targetSessionId)
        {
            if (switchingSession) return;
            if (conn == null || string.IsNullOrEmpty(targetSessionId)) return;
            if (targetSessionId == sessionId) return;

            switchingSession = true;
            UpdateToolbarState();
            var previousSessionId = sessionId;
            try
            {
                if (isRunning)
                {
                    try { await CancelSessionAsync(); } catch { }
                }

                var settings = AgentSettingsProvider.Load();
                var mcpServers = BuildMcpServerConfig(cachedInitResponse);

                // Flush any pending updates from the previous session before
                // mutating sessionId. Updates for the previous session that
                // arrive AFTER this swap are dropped by DrainPendingUpdates'
                // sessionId filter.
                while (pendingUpdates.TryDequeue(out _)) { }

                // Swap sessionId upfront so replay notifications emitted during
                // `session/load` pass the filter and land in pendingUpdates.
                sessionId = targetSessionId;

                LoadSessionResponse loadResponse = null;
                bool alreadyLoaded = false;
                try
                {
                    loadResponse = await conn.LoadSessionAsync(new()
                    {
                        SessionId = targetSessionId,
                        Cwd = Application.dataPath,
                        McpServers = mcpServers,
                    });
                }
                catch (Exception ex) when (IsAlreadyLoadedError(ex))
                {
                    // Some agents (e.g. opencode) refuse session/load for a
                    // session they already have live in memory. That's fine —
                    // we just can't get a replay. Keep the swap, show an
                    // empty conversation, and let new prompts flow through.
                    alreadyLoaded = true;
                    Logger.LogWarning(
                        $"Agent already has session {targetSessionId} loaded. " +
                        "Switching without replay — existing history won't be re-shown, " +
                        "but new messages in this session will work normally.");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to switch session: {ex.Message}");
                    // Roll back — the old conversation should remain visible.
                    sessionId = previousSessionId;
                    // Drop any replay updates that may have been queued before
                    // the failure completed.
                    while (pendingUpdates.TryDequeue(out _)) { }
                    // Drop the broken entry so the user isn't stuck on it.
                    if (settings != null)
                        SessionStore.Remove(settings.Command, settings.Arguments, targetSessionId);
                    knownSessions = settings != null
                        ? SessionStore.LoadAll(settings.Command, settings.Arguments)
                        : new List<SessionInfo>();
                    RebuildToolbar();
                    return;
                }

                // Load succeeded (or was unnecessary). Clear the old UI.
                // DrainPendingUpdates will apply the replay notifications
                // (already queued for targetSessionId) on the next RefreshUI
                // tick and repopulate the conversation. For already-loaded
                // sessions there are no replay events, so the view stays empty.
                ClearConversationUI();
                if (alreadyLoaded)
                {
                    // Drop anything the agent may still be streaming from the
                    // previous session that hasn't been filtered yet.
                    while (pendingUpdates.TryDequeue(out _)) { }
                }

                if (loadResponse?.Models?.AvailableModels != null && loadResponse.Models.AvailableModels.Length > 0)
                {
                    availableModels = loadResponse.Models.AvailableModels;
                    selectedModelIndex = availableModels
                        .Select((model, index) => (model, index))
                        .Where(x => x.model.ModelId == loadResponse.Models.CurrentModelId)
                        .FirstOrDefault()
                        .index;
                }

                if (loadResponse?.Modes?.AvailableModes != null && loadResponse.Modes.AvailableModes.Length > 0)
                {
                    availableModes = loadResponse.Modes.AvailableModes;
                    selectedModeIndex = availableModes
                        .Select((mode, index) => (mode, index))
                        .Where(x => x.mode.Id == loadResponse.Modes.CurrentModeId)
                        .FirstOrDefault()
                        .index;
                }

                if (settings != null)
                {
                    SessionStore.Touch(settings.Command, settings.Arguments, sessionId);
                    knownSessions = SessionStore.LoadAll(settings.Command, settings.Arguments);
                }
                RebuildToolbar();
                Logger.LogVerbose($"Switched to session {sessionId}.");
            }
            finally
            {
                switchingSession = false;
                UpdateToolbarState();
            }
        }

        async Task CreateNewSessionAsync()
        {
            if (switchingSession) return;
            if (conn == null) return;

            switchingSession = true;
            UpdateToolbarState();
            try
            {
                if (isRunning)
                {
                    try { await CancelSessionAsync(); } catch { }
                }

                var settings = AgentSettingsProvider.Load();
                var mcpServers = BuildMcpServerConfig(cachedInitResponse);

                // Create FIRST so that a failure doesn't wipe the user's visible conversation.
                NewSessionResponse newSession;
                try
                {
                    newSession = await conn.NewSessionAsync(new()
                    {
                        Cwd = Application.dataPath,
                        McpServers = mcpServers,
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to create new session: {ex.Message}");
                    return;
                }

                ClearConversationUI();
                while (pendingUpdates.TryDequeue(out _)) { }

                sessionId = newSession.SessionId;

                if (newSession.Models?.AvailableModels != null && newSession.Models.AvailableModels.Length > 0)
                {
                    availableModels = newSession.Models.AvailableModels;
                    selectedModelIndex = availableModels
                        .Select((model, index) => (model, index))
                        .Where(x => x.model.ModelId == newSession.Models.CurrentModelId)
                        .FirstOrDefault()
                        .index;
                }

                if (newSession.Modes?.AvailableModes != null && newSession.Modes.AvailableModes.Length > 0)
                {
                    availableModes = newSession.Modes.AvailableModes;
                    selectedModeIndex = availableModes
                        .Select((mode, index) => (mode, index))
                        .Where(x => x.mode.Id == newSession.Modes.CurrentModeId)
                        .FirstOrDefault()
                        .index;
                }

                if (settings != null)
                {
                    SessionStore.Upsert(settings.Command, settings.Arguments, sessionId, title: null);
                    knownSessions = SessionStore.LoadAll(settings.Command, settings.Arguments);
                }
                RebuildToolbar();
                Logger.LogVerbose($"New session {sessionId} created.");
            }
            finally
            {
                switchingSession = false;
                UpdateToolbarState();
            }
        }

        // ── IAcpClient interface implementation ──

        public async ValueTask<RequestPermissionResponse> RequestPermissionAsync(RequestPermissionRequest request, CancellationToken cancellationToken = default)
        {
            if (autoApprove)
            {
                var allowOption = request.Options
                    .FirstOrDefault(o => o.Kind == PermissionOptionKind.AllowAlways)
                    ?? request.Options.FirstOrDefault(o => o.Kind == PermissionOptionKind.AllowOnce);

                if (allowOption != null)
                {
                    Logger.LogVerbose($"[Auto-approve] Granted: {allowOption.OptionId}");
                    return new RequestPermissionResponse
                    {
                        Outcome = new SelectedRequestPermissionOutcome
                        {
                            OptionId = allowOption.OptionId,
                        }
                    };
                }
            }

            pendingPermissionRequest = request;
            pendingPermissionTcs = new TaskCompletionSource<RequestPermissionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var response = await pendingPermissionTcs.Task;
            return response;
        }

        public ValueTask SessionNotificationAsync(SessionNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pendingUpdates.Enqueue((notification.SessionId, notification.Update));
            return default;
        }

        public async ValueTask<WriteTextFileResponse> WriteTextFileAsync(WriteTextFileRequest request, CancellationToken cancellationToken = default)
        {
            var directory = Path.GetDirectoryName(request.Path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(request.Path, request.Content);

            return new WriteTextFileResponse();
        }

        public async ValueTask<ReadTextFileResponse> ReadTextFileAsync(ReadTextFileRequest request, CancellationToken cancellationToken = default)
        {
            using var stream = new FileStream(
                request.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true
            );

            using var reader = new StreamReader(
                stream,
                encoding: System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096
            );

            string content;

            if (request.Line.HasValue || request.Limit.HasValue)
            {
                content = await ReadLinesAsync(reader, request.Line ?? 1, request.Limit, cancellationToken);
            }
            else
            {
                content = await reader.ReadToEndAsync();
            }

            return new ReadTextFileResponse
            {
                Content = content
            };
        }

        async Task<string> ReadLinesAsync(StreamReader reader, uint startLine, uint? limit, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            uint currentLine = 1;
            uint linesRead = 0;

            while (currentLine < startLine && !reader.EndOfStream)
            {
                await reader.ReadLineAsync();
                currentLine++;
                cancellationToken.ThrowIfCancellationRequested();
            }

            while (!reader.EndOfStream)
            {
                if (limit.HasValue && linesRead >= limit.Value)
                {
                    break;
                }

                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.Append(line);

                linesRead++;
                cancellationToken.ThrowIfCancellationRequested();
            }

            return sb.ToString();
        }

        // ── Terminal stubs (protocol compliance only) ──

        public ValueTask<CreateTerminalResponse> CreateTerminalAsync(CreateTerminalRequest request, CancellationToken cancellationToken = default)
        {
            Logger.LogVerbose($"Terminal create requested (stub): {request.Command}");
            return new ValueTask<CreateTerminalResponse>(new CreateTerminalResponse
            {
                TerminalId = $"stub-{Guid.NewGuid():N}"
            });
        }

        public ValueTask<TerminalOutputResponse> TerminalOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<TerminalOutputResponse>(new TerminalOutputResponse
            {
                Output = "",
                Truncated = false,
                ExitStatus = new TerminalExitStatus { ExitCode = 0 }
            });
        }

        public ValueTask<ReleaseTerminalResponse> ReleaseTerminalAsync(ReleaseTerminalRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<ReleaseTerminalResponse>(new ReleaseTerminalResponse());
        }

        public ValueTask<WaitForTerminalExitResponse> WaitForTerminalExitAsync(WaitForTerminalExitRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<WaitForTerminalExitResponse>(new WaitForTerminalExitResponse
            {
                ExitCode = 0
            });
        }

        public ValueTask<KillTerminalCommandResponse> KillTerminalCommandAsync(KillTerminalCommandRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<KillTerminalCommandResponse>(new KillTerminalCommandResponse());
        }

        // ── Extension methods (forward compatibility) ──

        public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement request, CancellationToken cancellationToken = default)
        {
            Logger.LogVerbose($"Extension method: {method}");
            if (method == ClientMethods.ElicitationCreate)
            {
                return HandleElicitationCreateAsync(request, cancellationToken);
            }
            return new ValueTask<JsonElement>(JsonSerializer.SerializeToElement(new { }));
        }

        async ValueTask<JsonElement> HandleElicitationCreateAsync(JsonElement request, CancellationToken cancellationToken)
        {
            ElicitationRequest parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ElicitationRequest>(request.GetRawText());
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"[Elicitation] Failed to parse request: {ex.Message}");
                return JsonSerializer.SerializeToElement(ElicitationResponse.Cancel());
            }

            if (parsed == null)
            {
                return JsonSerializer.SerializeToElement(ElicitationResponse.Cancel());
            }

            // URL mode — open browser and immediately return accept
            if (parsed.Mode == "url" && !string.IsNullOrEmpty(parsed.Url))
            {
                Logger.Log($"[Elicitation] Opening URL: {parsed.Url}");
                Application.OpenURL(parsed.Url);
                return JsonSerializer.SerializeToElement(
                    ElicitationResponse.Accept(JsonSerializer.SerializeToElement(new { })));
            }

            // One at a time — decline any overlapping request.
            if (pendingElicitationRequest != null)
            {
                Logger.LogVerbose("[Elicitation] Another elicitation is already in flight; declining.");
                return JsonSerializer.SerializeToElement(ElicitationResponse.Decline());
            }

            var tcs = new TaskCompletionSource<ElicitationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingElicitationTcs = tcs;
            // Set the request last: UpdateElicitationUI gates on it and we want
            // the TCS visible before the UI can fire a response.
            pendingElicitationRequest = parsed;

            using (cancellationToken.Register(() =>
                tcs.TrySetResult(ElicitationResponse.Cancel())))
            {
                var response = await tcs.Task;
                return JsonSerializer.SerializeToElement(response);
            }
        }

        public ValueTask ExtNotificationAsync(string method, JsonElement notification, CancellationToken cancellationToken = default)
        {
            Logger.LogVerbose($"Extension notification: {method}");
            if (method == ClientMethods.ElicitationComplete)
            {
                Logger.Log("Elicitation completed (agent confirmed browser callback).");
            }
            return default;
        }
    }
}
