# Architecture

This document describes the layered architecture of Unity Agent Client, the design principles behind it, and the evolution roadmap for each layer.

## Five-Layer Architecture

Unity Agent Client follows a five-layer architecture where each layer has a clear responsibility. The top two layers (Intent and Task Graph) are handled by the AI Agent (LLM), while the bottom three layers (Skill, Capability, Atomic) are implemented in the Unity Client.

```
┌─────────────────────────────────────────────────────────┐
│  [1] Intent Layer (AI Agent)                            │
│      User's high-level goal in natural language         │
│      "Build a medieval tavern scene with furniture"     │
│                                                         │
│  [2] Task Graph Layer (AI Agent)  [future]              │
│      Structured decomposition into parallel/sequential  │
│      tasks with dependencies                            │
│      ├── Generate tavern building                       │
│      ├── Generate furniture (table, 4 chairs)           │
│      ├── Place decorative props (barrels, candles)      │
│      └── Adjust lighting for warm atmosphere            │
├─────────────────────────────────────────────────────────┤
│  [3] Skill Layer (Unity Client)                         │
│      Parameterized workflow templates (Task Graph lite)  │
│      Guides the Agent on how to combine capabilities    │
│      e.g. setup_basic_scene, generate_and_place_3d_asset│
│                                                         │
│  [4] Capability API Layer (Unity Client)                │
│      Semantic, mid-granularity capabilities             │
│      Grouped by domain via MetaToolRouter               │
│      e.g. unity_scene, unity_material, unity_generate   │
│                                                         │
│  [5] Atomic API Layer (Unity Client)                    │
│      Single-purpose, idempotent Unity operations        │
│      Each tool does exactly one thing                   │
│      e.g. add_gameobject, assign_material, raycast      │
└─────────────────────────────────────────────────────────┘
```

## Layer Details

### [1] Intent Layer — AI Agent

The user expresses a goal in natural language. The AI Agent (Gemini, Claude, Copilot, etc.) is responsible for understanding the intent.

```
User: "帮我搭一个中世纪酒馆场景，里面要有桌椅和蜡烛"
       ↓
Agent understands: create a medieval tavern scene
                   with tables, chairs, and candles
```

**Why Agent-side:** Intent understanding is what LLMs excel at. Building a separate NLU engine in the Unity Client would duplicate this capability with lower quality.

### [2] Task Graph Layer — AI Agent (Future)

> **Status:** Not yet implemented. Currently the Agent handles task decomposition implicitly through its reasoning. A formal Task Graph structure is planned for the future to enable debuggable, replayable, and cacheable execution plans.

The Agent decomposes the intent into a structured set of tasks, determining:
- What can run in parallel (e.g. generating multiple props)
- What has dependencies (e.g. must create table before placing mugs on it)
- What tools/skills to use for each task

```
Intent: "Build a medieval tavern"
         ↓
Task Graph:
  ├── [parallel]
  │   ├── generate_3d("tavern building, stone walls")
  │   ├── generate_3d("wooden table, medieval style")
  │   └── generate_3d("wooden chair, medieval style") ×4
  ├── [sequential, depends on above]
  │   ├── instantiate all generated models
  │   ├── arrange furniture (table center, chairs around)
  │   └── add candle props on table
  └── [parallel]
      ├── setup warm directional light
      └── add point lights for candle glow
```

**Why Agent-side:** Task decomposition requires world knowledge and reasoning — exactly what LLMs do. The Client provides the execution engine, not the planning engine.

**Future value of Task Graph:**
- Constrains Agent's freedom — prevents chaotic tool calls
- Debuggable — inspect the plan before execution
- Replayable — re-execute the same plan on a different scene
- Cacheable — skip planning for repeated tasks
- Similar to: HTN (Hierarchical Task Network), Robotics Task Planning

### [3] Skill Layer — Unity Client

Skills are parameterized workflow templates — essentially lightweight Task Graphs for common scenarios. They serve as "best practice templates" that guide the Agent on how to combine lower-level capabilities.

```yaml
name: create_colored_object
description: Create a primitive with a specific color
params: [objectName, shape, color, position]
steps:
  - tool: unity_scene
    action: add_gameobject
    params: { name: "{{objectName}}", primitiveType: "{{shape}}", position: "{{position}}" }
  - tool: unity_material
    action: assign
    params: { gameObjectPath: "{{objectName}}", color: "{{color}}" }
```

**Key properties:**
- Skills are suggestions, not enforced — the Agent can deviate
- Parameterized templates, not rigid macros — support variables and composition
- Defined in YAML for easy extension
- 14 built-in skills covering common scenarios
- Users can add custom skills in `UserSettings/UnityAgentClient/Skills/`

**Skill design principles:**
- Few in number, but high in expressive power
- Each Skill should be a **parameterized template**, not a fixed macro
- Composition should come from structure (steps + params), not from prompt engineering

**When to use Skills vs free composition:**

| Scenario | Approach |
|---|---|
| Common task with known best steps | Skill recipe |
| Novel or creative request | Agent freely composes Capability/Atomic tools |
| Batch of similar operations | Agent uses `unity_batch` with atomic tools |

### [4] Capability API Layer — Unity Client

Mid-granularity tools grouped by domain. This is what the Agent primarily interacts with. Implemented as `MetaToolRouter` instances.

```
unity_scene      — Object lifecycle (create, delete, rename, reparent, find, describe, place)
unity_editor     — Editor control (play mode, undo/redo, screenshots, console)
unity_asset      — Asset management (search, import, prefabs)
unity_material   — Visual styling (create, assign, modify properties)
unity_lighting   — Lighting & environment (ambient, fog, lightmap baking, time-of-day presets)
unity_animation  — Animation systems (controllers, state machines)
unity_ui         — UI operations (Canvas, RectTransform, Text)
unity_generate   — AI 3D generation (Meshy text-to-3d, image-to-3d)
unity_spatial    — Spatial intelligence (line-of-sight, visibility, NavMesh)
unity_particle   — Particle systems (read/modify modules, preview)
unity_terrain    — Terrain editing (heightmap, texture painting, trees)
unity_batch      — Cross-category batch execution
unity_skill      — Skill recipe access
unity_tool       — Direct atomic tool access (fallback)
```

**Design principles:**
- Each capability groups related atomic tools under an `action` parameter
- Cross-category aliases allow tools to appear in multiple capabilities (e.g. `instantiate_prefab` is in both `unity_scene` and `unity_asset`)
- Reduces context overhead: Agent sees 14 tools instead of 80+
- `unity_tool` provides direct access to any atomic tool as a fallback — ensures 100% coverage even when Skills/Capabilities don't match

**Evolution direction — Semantic Capability API:**

The Capability API has been evolving from purely technical parameters to **semantic parameters** that are more natural for LLM agents:

```
Technical (low-level):
  unity_scene(action="modify_component",
    gameObjectPath="Camera",
    componentType="Transform",
    propertyName="m_LocalPosition",
    value="3,2,1")

Semantic (implemented):
  unity_scene(action="place_on_ground", gameObjectPath="Chair")
  unity_scene(action="find_by_criteria", tag="Enemy", layer="Default")
  unity_scene(action="describe")
  unity_lighting(action="setup_time_of_day", timeOfDay="sunset")
  unity_particle(action="set_main", startColor="1,0.5,0,1", startSize=2)
  unity_terrain(action="modify_height", mode="raise", x=50, z=50, radius=10)
```

Semantic capabilities wrap multiple atomic operations and encode domain knowledge (e.g., time-of-day presets handle directional light, ambient, and fog together). The existing technical API remains as the foundation and fallback.

**Guardrails — Constraining Agent behavior:**

To prevent chaotic tool calls, the Capability layer provides three levels of constraint:

| Method | Description | Status |
|---|---|---|
| **Tool Routing** | Agent selects a category first, then sees only relevant actions | ✅ Implemented (MetaToolRouter) |
| **Permission Levels** | ReadOnly / Write / Dangerous classification | ✅ Implemented |
| **State-based constraints** | Only expose tools valid for current editor state (e.g. stop_playmode only available during play mode) | 🔮 Future |

### [5] Atomic API Layer — Unity Client

The lowest level. Each tool performs exactly one Unity Editor API operation. All tools implement the `IMcpTool` interface.

**Design principles:**

| Principle | Example |
|---|---|
| **Single responsibility** | `add_gameobject` only creates; it doesn't set color or add physics |
| **Idempotent when possible** | `set_active(true)` on an already-active object is a no-op |
| **Undo-safe** | All write operations use `Undo.RecordObject` |
| **Self-contained** | Each tool validates its own inputs and returns clear errors |
| **Main-thread aware** | `RequiresMainThread` flag for Unity API calls |

**Atomicity rule:** A tool is atomic when it represents the smallest **user-meaningful** action, not the smallest Unity API call. For example:
- ✅ `assign_material` — a single user intent ("make it blue")
- ✅ `add_gameobject` — a single user intent ("create a sphere")
- ❌ `set_serialized_property` — too low-level, not a user intent

### Spatial Understanding as a Core Capability

Scene understanding is a cross-cutting concern that deserves special attention. The `unity_spatial` capability provides the foundation:

| Current | Future (semantic) |
|---|---|
| `raycast(origin, direction)` | `checkLineOfSight(from="Player", to="Door")` |
| `camera_visibility(checkObject)` | `detectVisibleObjects()` |
| `navmesh_query_path(from, to)` | `getBestViewPoint(objective="lighting")` |

Semantic spatial tools dramatically reduce the number of tool calls needed for common spatial reasoning tasks, and are a key differentiator for this project.

## Data Flow

```
User prompt
    │
    ▼
[1] Agent interprets intent
    │
    ▼
[2] Agent decomposes into tasks (implicit reasoning, formal Task Graph in future)
    │
    ▼ (for each task)
[3] Agent checks Skills for known recipes
    │ (or skips if no matching skill)
    ▼
[4] Agent calls Capability API (MetaToolRouter)
    │
    ▼
[5] MetaToolRouter dispatches to Atomic tool
    │
    ▼
    IMcpTool.Execute() → Unity Editor API → Result
    │
    ▼
    Agent receives result, continues with next task
```

## Batch Execution

For multi-step operations, the Agent can use `unity_batch` to execute multiple atomic operations in a single round-trip:

```json
{
  "tool": "unity_batch",
  "operations": [
    { "tool": "unity_scene", "action": "add_gameobject",
      "name": "Ball", "primitiveType": "Sphere", "position": "0,3,0" },
    { "tool": "unity_material", "action": "assign",
      "gameObjectPath": "Ball", "color": "0,0,1,1" }
  ]
}
```

This reduces round-trips and token usage while keeping atomic tools composable.

## Permission Model

Tools are classified into three permission levels:

| Level | Behavior | Examples |
|---|---|---|
| `ReadOnly` | Always allowed, no confirmation | get_hierarchy, get_component_data |
| `Write` | Allowed, tracked for undo | add_gameobject, modify_component |
| `Dangerous` | Requires confirmation (or Auto Approve) | delete_gameobject, build, enter_playmode |

The `Auto Approve` toggle in the Agent window bypasses all permission prompts.

## Domain Reload Resilience

Unity's Domain Reload (triggered by C# script changes) kills all background threads. The system handles this through:

- **SessionStore** — Persists a list of agent sessions per agent config to `Temp/UnityAgentClientSessions.json`. On reconnect, the most-recently-active session is restored via `session/load`; users can switch between prior sessions or create a new one from the toolbar dropdown without killing the agent process.
- **MeshyTaskManager** — Persists generation tasks to `Temp/MeshyTasks.json`, auto-resumes polling after reload
- **ThreadAbortException** handling — Gracefully catches thread termination in MCP server

## Elicitation (Structured Input)

When an agent needs typed data (rather than free-form prose), it sends an
`elicitation/create` request. The client renders a native form and returns
the user's choices as validated JSON. See `docs/ELICITATION.md` for the
full design.

### Capability advertisement

```jsonc
// Sent in initialize → ClientCapabilities.Meta
{ "elicitation": { "form": {}, "url": {} } }
```

### Standard field types (Steps 1–2)

| JSON Schema              | UI Toolkit control         |
|--------------------------|----------------------------|
| `boolean`                | `Toggle`                   |
| `integer`                | `IntegerField` / `SliderInt` (when min+max) |
| `number`                 | `FloatField` / `Slider` (when min+max) |
| `string`                 | `TextField`                |
| `enum` / `oneOf`         | `PopupField<string>`       |
| `array` + `items.enum`   | multi-select `Toggle` group |
| `format: "multiline"`    | multiline `TextField`      |
| `format: "email"/"uri"`  | `TextField` with validation |

### Unity-native formats (Step 3)

Custom `format` extensions for Unity-specific data. MCP tool authors
can reference these in their elicitation schemas:

| Format string         | Control        | Serialized value                       |
|-----------------------|----------------|----------------------------------------|
| `unity-object`        | `ObjectField`  | Asset path (`"Assets/Prefabs/X.prefab"`) |
| `unity-scene-object`  | `ObjectField`  | Hierarchy path (`"/Parent/Child"`)     |
| `unity-vector3`       | `Vector3Field` | `"x,y,z"` (InvariantCulture)          |
| `unity-color`         | `ColorField`   | `"#RRGGBBAA"` (8-digit hex)           |

Example schema property:

```jsonc
"spawnPosition": {
  "type": "string",
  "format": "unity-vector3",
  "title": "Spawn Position",
  "default": "0,1,0"
}
```

### URL mode (Step 4)

When `mode == "url"`, the client opens the URL in the default browser via
`Application.OpenURL` and returns `accept`. The agent handles the OAuth /
authorization callback and sends `elicitation/complete` when done.

### Error handling

- **`-32042`**: Shown inline as "⚠ Authorization required"
- **Other `AcpException`**: Shown inline with error code and message

## Extensibility

### Adding a new Atomic Tool

```csharp
public class MyTool : IMcpTool
{
    public string Name => "my_custom_tool";
    public string Description => "What it does";
    public bool RequiresMainThread => true;
    public JsonElement InputSchema => /* ... */;

    public McpToolResult Execute(JsonElement args)
    {
        // Unity API calls
        return McpToolResult.Success("Done");
    }
}
```

Register in `BuiltinMcpServer.RegisterTools()` and add to the appropriate `MetaToolRouter`.

### Adding a new Skill

Place a `.yaml` file in `UserSettings/UnityAgentClient/Skills/`:

```yaml
name: my_custom_skill
description: What this skill achieves
category: scene
parameters:
  - name: objectName
    description: Name of the object to create
steps:
  - tool: unity_scene
    action: add_gameobject
    name: "{{objectName}}"
tips: |
  Additional guidance for the Agent.
```

### Adding a new Capability group

Create a new `MetaToolRouter` in `BuiltinMcpServer.RegisterTools()`:

```csharp
var myCategory = new MetaToolRouter("unity_mycategory",
    "Description of what this category covers")
    .AddAction(toolA, "action_a")
    .AddAction(toolB, "action_b");
McpToolRegistry.Register(myCategory);
```

## Design Principles Summary

| # | Principle | Rationale |
|---|---|---|
| 1 | **Don't expose raw Unity API to the Agent** | LLMs will misuse low-level APIs; wrap them in semantic capabilities |
| 2 | **Semantic first, not technical first** | `moveCameraTo("door")` > `set_transform(position="3,2,1")` |
| 3 | **Composition through structure, not prompts** | Use Skills and Batch, don't rely on LLM to remember multi-step sequences |
| 4 | **Few Skills, high expressiveness** | Parameterized templates with variables beat a catalog of rigid macros |
| 5 | **Always provide a fallback** | `unity_tool` direct access ensures 100% coverage when abstractions don't fit |
| 6 | **Intent and planning stay Agent-side** | Don't rebuild an LLM in the Client; provide the best execution layer instead |
