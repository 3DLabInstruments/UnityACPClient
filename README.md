# Unity Agent Client

[![GitHub license](https://img.shields.io/github/license/3DLabInstruments/UnityACPClient)](./LICENSE)
![Unity 2021.3+](https://img.shields.io/badge/unity-2021.3+-000.svg)

English | [中文](README_CN.md)

Provides integration of any AI agent (Gemini CLI, Claude Code, Codex CLI, etc.) with the Unity editor using Agent Client Protocol (ACP). AI agents can directly inspect, query, and modify your Unity project — scenes, components, assets, and more.

![demo](/docs/images/UnityDemo.gif)

> **Note:** Unity Agent Client now includes the ACP icon in the window title bar and native Markdown rendering for agent responses.

## Demo Recordings

These recordings show the project in action:

- [Install and use walkthrough](/docs/demos/nuityinstall.gif) — install the tool and get connected in the editor
- [Generate a 3D scene with an agent](/docs/demos/generate3Dscene.mp4) — use the agent to create a 3D model and place it in the scene
- [Switch scene lighting from noon to another look](/docs/demos/backtonoon.mp4) — adjust the scene lighting atmosphere live

## Overview

Unity Agent Client is an editor extension that uses the [Agent Client Protocol (ACP)](https://agentclientprotocol.com) to connect AI agents to the Unity editor. Instead of just chatting, agents can **operate** on your project through a comprehensive set of built-in MCP tools.

### What Can the Agent Do?

```
You:   "Set the Player's movement speed to 10"
Agent: 1. scene_get_hierarchy       → finds Player object
       2. scene_get_component_data  → reads PlayerMovement component
       3. scene_modify_component    → sets Speed = 10
       4. "Done. Player speed is now 10."
```

### Features

- **Any AI agent** — supports all ACP-compatible agents (Gemini CLI, Claude Code, Codex CLI, opencode, Goose, etc.)
- **60+ built-in MCP tools** — grouped into meta-tools for efficient agent interaction
- **Meta-tool architecture** — agent sees ~13 tools instead of 60+, reducing context overhead by ~80%
- **Multi-session support** — session dropdown in toolbar lets you switch between conversations; `＋` button creates new sessions
- **Elicitation forms** — agents can request structured input via native UI controls (toggles, sliders, dropdowns, multi-select, color picker, Vector3, object picker); URL-mode opens the browser for OAuth flows
- **Skill recipes** — pre-defined step-by-step guides for common tasks (14 built-in)
- **AI 3D asset generation** — generate 3D models from text/image via Meshy AI
- **Extensible tool system** — add custom tools with a single class implementing `IMcpTool`
- **Dual MCP transport** — auto-selects HTTP (no Node.js) or stdio proxy based on agent capabilities
- **Drag & drop context** — attach assets to prompts for richer context
- **Auto-reconnect** — recovers from agent crashes with exponential backoff; auto-reconnects on config change

The demo recordings above highlight the install flow, agent-driven 3D generation, and lighting changes in a real Unity scene.

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Unity Editor                                               │
│                                                             │
│  ┌─ Open Source (GitHub) ─────────────────────────────────┐ │
│  │  AgentWindow ──ACP(stdio)──> Agent Process             │ │
│  │      │                        (Gemini/Claude)          │ │
│  │      │                             │                   │ │
│  │      ├── Elicitation Panel        │ MCP               │ │
│  │      └── Permission UI            ▼                   │ │
│  │  BuiltinMcpServer  <──── HTTP or stdio proxy          │ │
│  │      │                                                 │ │
│  │  80+ Tool Implementations (Mcp*Tools.cs)               │ │
│  └────────────────────────────────────────────────────────┘ │
│              │ references                                   │
│  ┌─ UnityAgentClient.Core.dll (Closed Source) ────────────┐ │
│  │  AgentClientProtocol  — ACP protocol types & transport │ │
│  │  McpToolRegistry      — IMcpTool, permissions, dispatch│ │
│  │  MetaToolRouter       — category routing, batch exec   │ │
│  │  SkillRegistry        — YAML skill recipes             │ │
│  │  SessionStore         — session persistence            │ │
│  └────────────────────────────────────────────────────────┘ │
│              │                                              │
│          Unity Editor API                                   │
└─────────────────────────────────────────────────────────────┘
```

### Project Structure

This project uses a **hybrid open/closed source model**:

- **Open source** (this repository) — UI layer, tool implementations, settings, MCP server bootstrap
- **Closed source** ([`UnityAgentClient.Core`](https://www.nuget.org/packages/UnityAgentClient.Core)) — Core engine distributed as a NuGet DLL, containing ACP protocol implementation, tool registry, meta-tool routing, and skill system

To add custom tools, implement the `IMcpTool` interface (exported by the Core DLL) and register in `BuiltinMcpServer.cs`.


## Built-in MCP Tools

The agent sees **13 tools** instead of 62, organized by category with a fallback for direct access:

### Meta-Tool Architecture

```
Agent sees 13 tools:
┌──────────────────────────────────────────────────────────────┐
│ unity_scene     — Scene & GameObject operations (22 actions) │
│ unity_editor    — Editor control & project info (12 actions) │
│ unity_asset     — Asset management (14 actions)              │
│ unity_material  — Material & rendering (5 actions)           │
│ unity_lighting  — Lighting & environment (4 actions)         │
│ unity_animation — Animation & navigation (6 actions)         │
│ unity_ui        — UI Canvas operations (6 actions)           │
│ unity_generate  — AI 3D asset generation (5 actions)         │
│ unity_spatial   — Spatial queries & 3D perception (6 actions)│
│ unity_particle  — Particle system editing (8 actions)        │
│ unity_terrain   — Terrain sculpting & painting (5 actions)   │
│ unity_skill     — Skill recipes for common tasks (14 skills) │
│ unity_tool      — Direct access fallback (any tool by name)  │
│ unity_batch     — Cross-category batch operations            │
└──────────────────────────────────────────────────────────────┘

Example:
  unity_scene(action="modify_component", gameObjectPath="Player", ...)
  unity_asset(action="find_references", assetPath="Assets/Materials/Wood.mat")
  unity_generate(action="text_to_3d", prompt="a medieval wooden chair")
  unity_tool(name="list")  → shows all 62 available tool names
```

### unity_scene — Scene & GameObjects

| Action | Description |
|---|---|
| `get_hierarchy` | Get full GameObject tree with component info |
| `get_component_data` | Read all serialized properties of any component |
| `modify_component` | Modify component properties (int, float, bool, string, Vector3, Color, Enum) |
| `add_gameobject` | Create GameObjects with primitives, components, color, scale, and parenting |
| `delete_gameobject` | Delete GameObjects (with Undo support) |
| `save` | Save active scene or all open scenes |
| `set_selection` | Select GameObjects in the editor |
| `reparent` | Move a GameObject under a different parent |
| `duplicate` | Duplicate a GameObject with all children |
| `find_by_component` | Find all GameObjects with a specific component type |
| `set_active` | Set a GameObject active or inactive |
| `rename` | Rename a GameObject |
| `set_transform` | Set position, rotation, and/or scale in one call |
| `open_scene` | Open a scene by path |
| `set_text` | Set Text/TextMeshPro content (cross-ref from UI) |
| `instantiate_prefab` | Instantiate a prefab (cross-ref from Asset) |
| `assign_material` | Assign a material or color to a Renderer (cross-ref from Material) |

### unity_editor — Editor Control

| Action | Description |
|---|---|
| `enter_playmode` | Enter Play Mode |
| `pause_playmode` | Pause / resume Play Mode |
| `stop_playmode` | Stop Play Mode |
| `execute_menu_item` | Execute any editor menu command |
| `get_state` | Get current editor state (play mode, scene, selection, available actions) |
| `screenshot` | Capture Game view screenshot |
| `undo` | Undo last N operations |
| `redo` | Redo last N operations |
| `get_console_logs` | Read all console log entries |
| `get_console_errors` | Get only errors and warnings |
| `get_project_settings` | Get project configuration |
| `build` | Build the project |

### unity_lighting — Lighting & Environment

| Action | Description |
|---|---|
| `get_settings` | Get ambient light, skybox, fog, lightmap settings, and all scene lights |
| `set_ambient` | Modify ambient color, intensity, fog enable/color/density |
| `bake` | Bake lightmaps for the active scene |

See the [lighting demo recording](/docs/demos/backtonoon.mp4) for a live example of scene lighting changes.

### unity_asset — Asset Management

| Action | Description |
|---|---|
| `list` | Search assets by path and type |
| `get_info` | Get asset details, dependencies, labels |
| `find_references` | Find all assets that reference a given asset |
| `get_import_settings` | Read asset importer settings |
| `list_scenes` | List all scenes and build settings |
| `create_prefab` | Create a prefab from a scene object |
| `instantiate_prefab` | Instantiate a prefab into the scene |
| `rename` | Rename an asset |
| `move` | Move an asset to a different folder |
| `delete` | Delete an asset (moves to trash) |
| `refresh` | Force Asset Database refresh |
| `create_folder` | Create project folders |
| `create_material` | Create a new material |

### unity_material — Materials & Rendering

| Action | Description |
|---|---|
| `get_properties` | Read all shader properties of a material |
| `set_property` | Modify material properties (color, float, texture, vector) |
| `assign` | Assign a material to a GameObject's Renderer (by path or by color) |
| `get_render_settings` | Get quality, shadow, and render pipeline settings |
| `create` | Create a new material with specified shader |

### unity_animation — Animation & Navigation

| Action | Description |
|---|---|
| `get_controllers` | List all Animator Controllers |
| `get_states` | Get full state machine structure |
| `get_parameters` | Read/write Animator Controller parameters |
| `navmesh_bake` | Bake NavMesh for the active scene |
| `navmesh_get_settings` | Get NavMesh agent types and area settings |

### unity_ui — UI (Canvas/UGUI)

| Action | Description |
|---|---|
| `get_canvas_hierarchy` | Get specialized UI hierarchy with RectTransform details |
| `modify_rect_transform` | Modify anchors, pivot, size, and position |
| `set_text` | Set text content on Text or TextMeshPro |
| `get_component_data` | Read component properties (cross-ref from Scene) |
| `modify_component` | Modify component properties (cross-ref from Scene) |
| `set_active` | Set active/inactive (cross-ref from Scene) |

### unity_generate — AI 3D Asset Generation

Generate game-ready 3D models directly inside Unity using [Meshy AI](https://www.meshy.ai). The agent handles the entire pipeline: prompt → API call → poll → download → import → place in scene.

| Action | Description |
|---|---|
| `text_to_3d` | Generate a 3D model from a text description (supports glb/fbx/obj, art styles, negative prompts) |
| `image_to_3d` | Generate a 3D model from a reference image (base64 or file path) |
| `list_tasks` | List recent Meshy generation tasks and their statuses |
| `instantiate_prefab` | Place the generated model into the scene (cross-ref from Asset) |
| `refresh` | Refresh Asset Database after generation (cross-ref from Asset) |

See the [3D generation demo recording](/docs/demos/generate3Dscene.mp4) for a complete end-to-end example.

**Setup:** Set the `MESHY_API_KEY` environment variable in `Project Settings > Unity Agent Client > Environment Variables`, or save it to `UserSettings/UnityAgentClient/meshy_api_key.txt`.

**Example — end-to-end generation flow:**

```
You:   "Generate a low-poly medieval chair and place it at position 3,0,5"
Agent: 1. unity_generate(action="text_to_3d",
          prompt="a medieval wooden chair with ornate carvings",
          artStyle="low-poly", outputFormat="glb")
       2. (polls Meshy API for ~30-120 seconds until model is ready)
       3. unity_generate(action="instantiate_prefab",
          prefabPath="Assets/Generated/Meshy/a_medieval_wooden_chair_143022.glb",
          position="3,0,5")
       4. "Done. Medieval chair generated and placed at (3, 0, 5)."
```

**Use Cases:**

| Scenario | Example Prompt |
|---|---|
| **Rapid prototyping** | "Fill this room with furniture — a table, 4 chairs, and a bookshelf" |
| **Concept iteration** | "Generate 3 different styles of a sci-fi weapon: realistic, cartoon, and low-poly" |
| **Scene dressing** | "Add some decorative props to the medieval tavern scene — barrels, mugs, candles" |
| **Reference-based modeling** | (drag image into window) "Generate a 3D model from this concept art" |
| **Asset pipeline automation** | "Generate low-poly trees and save them as prefabs in Assets/Prefabs/Environment" |
| **Quick placeholder assets** | "I need a temporary robot character for testing — generate one and add a Rigidbody" |

> [!TIP]
> Use `artStyle="low-poly"` for game-ready assets with optimized polygon counts. Generated models are saved to `Assets/Generated/Meshy/` by default.

### unity_spatial — Spatial Queries & Perception

| Action | Description |
|---|---|
| `check_line_of_sight` | Check if two objects (or positions) have clear line of sight — reports what blocks the path |
| `detect_visible_objects` | List all objects visible from a camera, grouped by distance (near/mid/far) |
| `raycast` | Cast a ray and report hits (low-level, use `check_line_of_sight` for simpler queries) |
| `camera_visibility` | Check if a specific object is visible from a camera (frustum + occlusion) |
| `navmesh_query_path` | Calculate navigation path between two points with distance and waypoints |
| `inject_texture` | Create a texture from base64/file and optionally apply to a material |

### unity_tool — Direct Access Fallback

For when the agent knows the exact tool name or can't find the right category:

```
unity_tool(name="list")                           → list all 51 tools
unity_tool(name="physics_get_settings")           → call any tool directly
unity_tool(name="config_tags_and_layers")          → bypasses meta-tool routing
```

### unity_skill — Skill Recipes

Skills are pre-defined step-by-step guides for common Unity tasks. The agent reads a skill recipe, then executes the steps using the other meta-tools.

```
unity_skill(action="list")                        → list all available skills
unity_skill(action="search", query="button")      → search by keyword
unity_skill(action="get", name="create_ui_button") → get full recipe
```

**Built-in Skills:**

| Skill | Category | Description |
|---|---|---|
| `setup_basic_scene` | scene | Ground + light + camera setup |
| `create_ui_button` | ui | Canvas + Button + Text with label |
| `create_character_controller` | scene | Capsule + Rigidbody + constraints |
| `setup_physics_layers` | physics | Review layers and collision matrix |
| `create_animation_setup` | animation | Review Animator Controllers and states |
| `create_ui_health_bar` | ui | Slider-based health bar |
| `setup_main_menu` | ui | Title + button group layout |
| `create_spawn_system` | scene | Spawn point container pattern |
| `generate_and_place_3d_asset` | generation | AI-generate a 3D model and place it in scene |
| `create_colored_object` | scene | Shape + color + position + physics in one workflow |
| `setup_lighting` | scene | Mood-based lighting (bright/warm/cool/dark/dramatic) |
| `create_physics_object` | physics | Rigidbody + mass + constraints setup |
| `populate_scene` | scene | Batch create objects in grid/circle/line/random pattern |
| `analyze_scene` | analysis | Comprehensive scene inspection (hierarchy + visibility + lights) |

**Adding Custom Skills:** Place `.yaml` files in `UserSettings/UnityAgentClient/Skills/` in your project. See `BuiltinSkills.cs` for the format.

### Adding Custom Tools

Implement `IMcpTool` and register in `BuiltinMcpServer.RegisterTools()`:

```csharp
public class MyTool : IMcpTool
{
    public string Name => "my_custom_tool";
    public string Description => "Does something useful";
    public bool RequiresMainThread => true; // true if using Unity API
    public JsonElement InputSchema => JsonDocument.Parse(@"{ ""type"": ""object"", ""properties"": {} }").RootElement;

    public McpToolResult Execute(JsonElement args)
    {
        // Your Unity API calls here
        return McpToolResult.Success("Result text");
    }
}
```

## Setup

For the full step-by-step setup guide with troubleshooting, see **[docs/SETUP.md](docs/SETUP.md)**.

**Quick start:**

Unity Agent Client requires Unity 2021.3 or later. Node.js is optional (only needed for agents that don't support HTTP MCP transport).

### 1. Clone the Repository

```bash
git clone https://github.com/3DLabInstruments/UnityACPClient.git
```

Open the cloned folder in Unity Hub. All DLLs are bundled — no extra steps needed.

To add to an existing project, copy `Assets/UnityAgentClient/` into your project's `Assets/` folder (the DLLs are bundled inside `Editor/Plugins/`).

### 2. Set Up the Agent to Use

Open `Project Settings > Unity Agent Client` and fill in the settings according to the AI agent you want to use.

> [!NOTE]
> On macOS, PATH resolution may fail in zsh. If an error occurs, use the `which` command to check the full path of the binary and enter it in Command.

> [!WARNING] 
> The settings are saved in the project's UserSettings folder. While the UserSettings folder is usually excluded by `.gitignore`, be careful not to accidentally upload API keys, etc.

<details>

<summary>GitHub Copilot CLI</summary>

Requires GitHub Copilot CLI (`gh extension install github/gh-copilot` or the standalone `copilot` binary).

| Command   | Arguments |
| --------- | --------- |
| `copilot` | `--acp`   |

</details>

<details>

<summary>Gemini CLI</summary>

Gemini CLI currently provides experimental ACP support. This can be executed by specifying the `--experimental-acp` option.

| Command  | Arguments            |
| -------- | -------------------- |
| `gemini` | `--experimental-acp` |

If using an API key for login, add `GEMINI_API_KEY` to Environment Variables.

</details>

<details>

<summary>Claude Code</summary>

Claude Code itself does not support ACP, so use the adapter provided by Zed. Follow the README in the following repository to set up claude-code-acp:

https://github.com/zed-industries/claude-code-acp

| Command           | Arguments |
| ----------------- | --------- |
| `claude-code-acp` | -         |

</details>

<details>

<summary>Codex CLI</summary>

Codex CLI itself does not support ACP, so use the adapter provided by Zed. Follow the README in the following repository to set up codex-acp:

https://github.com/zed-industries/codex-acp

| Command     | Arguments |
| ----------- | --------- |
| `codex-acp` | -         |

</details>

<details>

<summary>opencode (Recommended)</summary>

opencode is an open-source AI agent that runs on the CLI and supports any LLM provider, as well as MCP and ACP by default.

https://opencode.ai/

| Command    | Arguments |
| ---------- | --------- |
| `opencode` | `acp`     |

</details>

<details>

<summary>Goose</summary>

Goose is an open-source AI agent that runs on the CLI/desktop and supports any LLM provider, as well as MCP and ACP by default.

https://block.github.io/goose/

| Command    | Arguments |
| ---------- | --------- |
| `goose` | `acp`     |

</details>

## Usage

Open `Window > Unity Agent Client > AI Agent` to automatically connect to the session.

![](/docs/images/ACP-allow.png)

- Enter a prompt in the field and press **Send** (or Enter) to submit. Shift+Enter inserts a newline; the input field auto-grows as you type. Press **Esc** to cancel a running request.
- Drag and drop assets into the window to attach them as context. Attached items appear as compact chips with an icon; click a chip to ping the asset in the Project window, or press × to remove it.
- A colored dot in the toolbar reflects connection status (green = connected, yellow = connecting, red = failed); hover it for details.
- A "thinking…" indicator is shown while the agent is responding.
- **Session management:** A dropdown in the toolbar shows all sessions for the current agent. Click `＋` to create a new session. Sessions persist across domain reloads (up to 20 per agent config). There is no "Disconnect" button — sessions coexist and the agent process stays alive.
- **Elicitation:** When an agent needs structured input (e.g. choosing a strategy, configuring build settings), an inline form appears with native controls — dropdowns, toggles, sliders, multi-select checkboxes, and text fields with validation. Submit, Decline, or Cancel (Esc) to respond.
- If the connection fails, a **Retry** button and an **Open Settings** button appear with the error message. If it's still connecting, a **Cancel** button lets you abort and reconfigure.
- When executing tools, the agent may request permission (whether permission is requested depends on the agent's settings).
- If supported by the agent in use, you can switch modes or models in the toolbar. Mode names are shown as human-readable labels (e.g. "Agent", "Plan") rather than raw identifiers.
- Changing agent settings (command/arguments) while connected automatically triggers a reconnect — no need to close and reopen the window.

## Best Practices

Unity Agent Client **DOES NOT recommend** using AI agents for coding within the editor. Due to Unity's constraints, editing C# scripts triggers a Domain Reload which restarts the agent process. The session list and last active session are preserved and automatically restored after reconnection.

Unity Agent Client is best suited for:

- **Scene setup & iteration** — "Add a directional light and set it to warm color"
- **AI 3D asset generation** — "Generate a low-poly medieval chair and place it at position 3,0,5"
- **Project analysis** — "Which assets reference the Wood material?"
- **Configuration review** — "Show me the import settings for all textures in Assets/UI"
- **Quick prototyping** — "Create a Cube named Enemy at position 5,0,3 with a Rigidbody"
- **Debugging assistance** — "Show me the recent console errors"

## Roadmap

See [ROADMAP.md](ROADMAP.md) for planned features and future direction.

## License

This library is provided under the [MIT LICENSE](LICENSE).