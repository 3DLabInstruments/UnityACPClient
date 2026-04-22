# Roadmap

This document outlines the planned features and improvements for Unity Agent Client.

> Last updated: 2026-04-21

## What's Shipped

Everything below has landed and is available in the current build.

<details>
<summary><strong>Core Platform</strong></summary>

- ✅ ACP client with stdio transport
- ✅ 80+ built-in MCP tools across 14 categories
- ✅ Standard MCP JSON-RPC protocol over HTTP
- ✅ Dual transport support (HTTP direct / stdio proxy)
- ✅ Extensible tool registry (`IMcpTool`)
- ✅ Meta-tool architecture — 62 tools grouped into 12 agent-facing tools
- ✅ Five-layer architecture design (see `docs/ARCHITECTURE.md`)
- ✅ Unity 6 compatibility — FindObjectsByType migration
- ✅ State-aware guardrails — build blocked during Play Mode, contextual hints

</details>

<details>
<summary><strong>ACP Protocol (SDK 0.3.0)</strong></summary>

- ✅ C# ACP SDK + Core engine (UnityAgentClient.Core DLL)
- ✅ `session/list`, `session/set_config_option` support
- ✅ `ConfigOptionUpdate`, `SessionInfoUpdate` session update types
- ✅ Elicitation — full form builder with Unity-native formats, URL mode, `-32042` handling
- ✅ Usage tracking — token consumption + context window + cost in toolbar

</details>

<details>
<summary><strong>Architecture</strong></summary>

- ✅ WebSocket transport with HTTP fallback
- ✅ Tool permission levels — ReadOnly / Write / Dangerous
- ✅ Session persistence — reconnect after Domain Reload
- ✅ Multi-session manager — toolbar dropdown, graceful lifecycle
- ✅ Batch operations — within-category and cross-category

</details>

<details>
<summary><strong>UI (UI Toolkit)</strong></summary>

- ✅ Full IMGUI → UI Toolkit migration (`CreateGUI` + USS)
- ✅ Message bubbles, tool call foldouts, thinking indicator, plan view
- ✅ Permission / auth / elicitation panels
- ✅ Rich input (multi-line, drag-and-drop, keyboard shortcuts, auto-scroll)
- ✅ Connection dot, typing animation, responsive layout, token display
- ✅ Markdown rendering — native UI Toolkit (headings, code blocks, lists, quotes, inline formatting)

</details>

<details>
<summary><strong>Tools</strong></summary>

- ✅ Scene — find, describe, create, transform, component add/remove, ground placement
- ✅ Spatial — raycast, frustum, occlusion, line-of-sight, NavMesh
- ✅ Lighting — get/set, bake, time-of-day presets
- ✅ Particle — 8 tools covering all major modules + preview
- ✅ Terrain — settings, height, paint, trees
- ✅ Material — assign material/color, runtime texture injection
- ✅ Editor — undo/redo, rename
- ✅ AI asset — Meshy text-to-3D / image-to-3D with task persistence
- ✅ Sentis — GPU vision (object detection, depth estimation)
- ✅ Skill system — 14 built-in + YAML extension

</details>

> Note: Creating/editing C# scripts from the agent is intentionally **not** on the roadmap. Unity triggers a Domain Reload on any script change, which tears down the agent session and its in-flight tool calls. Coding tasks belong in IDE-hosted agents (Copilot / Cursor / Claude Code); this plugin focuses on scene, asset, and runtime operations that IDE agents cannot perform.

---

## Next Up

### UI Polish

- [ ] **Context window progress bar** — Visual indicator (green → yellow → red) showing
      how full the context window is, driven by `UsageUpdateSessionUpdate`. Warn at 75%,
      recommend new session at 90%+.

### ACP Protocol — New RFDs

These are active or preview-stage RFDs in the ACP protocol that would directly benefit
Unity Agent Client users.

- [ ] **`session/close`** (ACP RFD, Preview) — Gracefully close a session and free agent
      resources. Currently sessions stay alive until the process dies. Implement in SDK +
      add "Close Session" option to session dropdown context menu.

- [ ] **`session/resume`** (ACP RFD, Draft) — Lightweight reconnect to a session without
      replaying full message history. Useful for Domain Reload recovery: instead of
      `session/load` (which replays all messages), `session/resume` just re-attaches.
      Would make post-reload reconnection faster and cheaper.

- [ ] **`session/delete`** (ACP RFD, Draft) — Remove sessions from `session/list`.
      Without this, old sessions accumulate forever. Add "Delete" action to session
      dropdown or swipe-to-delete.

- [ ] **`$/cancel_request`** (ACP RFD, Draft) — Per-request cancellation (LSP-style).
      Today we only have `session/cancel` which cancels the entire turn. This enables
      cancelling individual tool calls or file operations mid-flight.

- [ ] **Message ID** (ACP RFD, Draft) — `messageId` on message chunks for reliable
      message boundary detection and future edit/undo support. Currently we infer
      boundaries from update type transitions, which breaks on consecutive same-type
      messages.

### More Tools

- [ ] **Animation tools** — Read/write Animator parameters, trigger states, blend tree
      weights. Agents could choreograph character animations ("make the character walk
      to the door, then wave").

- [ ] **Audio tools** — AudioSource create/configure, AudioClip assignment, mixer
      snapshot control. Complete the multimedia toolkit alongside visual/spatial tools.

- [ ] **Physics tools** — Read/write Rigidbody properties, add/configure colliders and
      joints. Enable agents to set up physics simulations ("add a hinge joint between
      these two objects with 30° limits").

- [ ] **Prefab tools** — Instantiate prefabs, modify prefab instances, apply/revert
      overrides. Currently agents can only create primitive objects; prefab support
      unlocks working with real production assets.

## Mid-term

### Agent Intelligence

- [ ] **Console log streaming** — Real-time `Debug.Log` / warning / error feed to the
      agent session as context. Lets the agent react to runtime errors ("I see a
      NullReferenceException in PlayerController — let me check that component").

- [ ] **Profiler snapshot sharing** — Capture and share performance data (frame time,
      draw calls, memory) with the agent for optimization advice.

- [ ] **Scene diff awareness** — Track and report scene changes between prompts so
      the agent knows what changed since its last action.

### ACP Protocol — Advanced

- [ ] **`session/fork`** (ACP RFD, Draft) — Branch a session for speculative work
      without polluting the main conversation. Useful for "try two approaches and pick
      the best one" workflows.

- [ ] **MCP-over-ACP** (ACP RFD, Draft) — Inject Unity-side MCP tools through the ACP
      channel instead of requiring a separate stdio/HTTP process. Would let us expose
      Unity tools as first-class MCP servers visible to the agent's own MCP routing.

### Multi-Agent

- [ ] **Multiple simultaneous agent connections** — Run different agents side by side
      (e.g., Claude for scene design + GPT for shader advice). Each gets its own session
      panel, tool permissions, and MCP server config.

- [ ] **Agent-to-agent delegation** — IDE coding agent delegates to Unity scene agent
      for operations the IDE agent can't perform (scene manipulation, asset pipeline).
      Requires `session/fork` or a delegation protocol.

## Long-term

### Ecosystem

- [ ] **Tool marketplace** — Share community-built `IMcpTool` packages via UPM or a
      registry. Include discovery, versioning, and dependency management.

- [ ] **Cross-engine tool schema** — Standardized tool interfaces portable to
      Unreal / Godot. Define a common `scene.create_object` / `scene.set_transform`
      vocabulary so agents work across engines.

- [ ] **Custom LLM endpoint** (ACP RFD, Draft) — Let users configure their own
      LLM provider endpoint. Useful for self-hosted models or enterprise deployments
      with private API gateways.

---

## Contributing

To add a new tool:

1. Create a class implementing `IMcpTool` (see `McpSceneTools.cs` for examples)
2. Register it in `BuiltinMcpServer.RegisterTools()`
3. Set `RequiresMainThread = true` if your tool uses Unity Editor API

To add a new skill:

1. Place a `.yaml` file in `UserSettings/UnityAgentClient/Skills/` or add to `BuiltinSkills.cs`
2. Skills are step-by-step recipes — the agent reads them and calls tools manually

The tool registry handles MCP protocol serialization, main thread dispatch, and error handling automatically.
