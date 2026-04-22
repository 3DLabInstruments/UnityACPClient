# Contributing to Unity Agent Client

Thank you for your interest in contributing! This guide covers how to add new MCP tools, create skill recipes, improve the UI, and submit your changes.

## Quick Start

```bash
git clone https://github.com/yetsmarch-sina/UnityAgentClient.git
# Open in Unity Hub → Window > Unity Agent Client > AI Agent
```

All dependencies (Core DLL, System.Text.Json) are bundled — no extra install steps.

## Project Structure

```
Assets/UnityAgentClient/Editor/
├── AgentWindow.cs              ← UI (chat window, ACP protocol handling)
├── BuiltinMcpServer.cs         ← Tool registration & MCP server
├── CoreBootstrap.cs            ← Wires Core DLL to Unity APIs
├── Mcp*Tools.cs (×15 files)    ← 80+ tool implementations ← YOUR TOOLS GO HERE
├── MarkdownVisualBuilder.cs    ← Markdown → UI Toolkit renderer
├── AgentWindow.uss             ← Styles
├── Elicitation/                ← Structured input forms
└── server.js                   ← stdio proxy for agents
```

The Core engine (`UnityAgentClient.Core.dll`) provides `IMcpTool`, `McpToolRegistry`, `MetaToolRouter`, and other infrastructure. You don't need to modify or understand it — just implement the interface.

## Adding a New MCP Tool

This is the most common contribution. Each tool gives the AI agent a new ability to inspect or modify your Unity project.

### Step 1: Implement `IMcpTool`

Create a new class or add to an existing `Mcp*Tools.cs` file:

```csharp
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    public class MyCustomTool : IMcpTool
    {
        public string Name => "my_custom_tool";
        public string Description => "One-line description of what this tool does.";
        public bool RequiresMainThread => true; // true if using Unity Editor API

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""target"": {
                    ""type"": ""string"",
                    ""description"": ""Path to the target GameObject""
                },
                ""value"": {
                    ""type"": ""number"",
                    ""description"": ""The value to set""
                }
            },
            ""required"": [""target""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            // 1. Parse arguments
            var target = args.GetProperty("target").GetString();
            var go = GameObject.Find(target);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {target}");

            // 2. Do the work (use Undo for reversibility)
            Undo.RecordObject(go.transform, "My Custom Tool");
            // ... your logic here ...

            // 3. Return result as text
            return McpToolResult.Success($"Done! Modified {target}");
        }
    }
}
```

### Step 2: Register in `BuiltinMcpServer.cs`

Find the `RegisterTools()` method and add two lines:

```csharp
// In the tool instantiation section:
var myCustomTool = new MyCustomTool();

// In the allTools array:
var allTools = new IMcpTool[]
{
    // ... existing tools ...
    myCustomTool,       // ← add here
};
```

### Step 3: Add to a Meta-Tool Category (optional but recommended)

Agents see meta-tools, not individual tools. Add your tool to an existing category:

```csharp
// In the meta-tool section, add to an existing router:
var scene = new MetaToolRouter("unity_scene", "...")
    // ... existing actions ...
    .AddAction(myCustomTool, "my_action_name");  // ← add here
```

Or create a new category if none fits:

```csharp
var myCategory = new MetaToolRouter("unity_mycategory",
    "Description of what this category does")
    .AddAction(myCustomTool, "my_action");

McpToolRegistry.Register(myCategory);
```

### Step 4: Test

1. Open Unity → `Window > Unity Agent Client > AI Agent`
2. Connect to any agent
3. Ask the agent to use your tool by describing the action
4. Or test directly: `unity_tool(name="my_custom_tool", ...)`

## Tool Implementation Guidelines

### Input Schema

- Use JSON Schema to define parameters
- Include `description` on every property — the agent reads this to decide what to pass
- Mark required parameters in the `required` array
- Keep schemas simple: `string`, `number`, `integer`, `boolean`, `array`

### Return Values

```csharp
// Success — return human-readable text the agent can parse
return McpToolResult.Success("Created 3 objects at positions (0,0,0), (1,0,0), (2,0,0)");

// Error — agent will see this and can retry or adjust
return McpToolResult.Error("Material not found at path: Assets/Missing.mat");
```

### Main Thread

- Set `RequiresMainThread => true` if your tool calls **any** Unity API (`GameObject.Find`, `AssetDatabase`, `Undo`, etc.)
- Set `RequiresMainThread => false` only for pure computation (rare)

### Permission Levels

```csharp
// Override for dangerous operations (delete, build, play mode)
public ToolPermissionLevel PermissionLevel => ToolPermissionLevel.Dangerous;

// Default is ReadOnly — no override needed for read-only tools
// Write is inferred from name patterns (set_, create_, modify_, etc.)
```

### Undo Support

Always use `Undo.RecordObject()` or `Undo.RegisterCreatedObjectUndo()` for scene modifications. This lets users undo agent actions.

### File Organization

| Category | File | What goes here |
|----------|------|----------------|
| Scene & GameObjects | `McpSceneTools.cs` | Hierarchy, components, transform |
| Editor operations | `McpEditorTools.cs` | Play mode, undo, screenshots |
| Assets | `McpAssetTools.cs` | AssetDatabase operations |
| Materials | `McpMaterialTools.cs` | Shader properties, material assignment |
| Lighting | `McpLightingTools.cs` | Lights, baking, ambient |
| Animation | `McpAnimationTools.cs` | Animator, states, parameters |
| UI | `McpUITools.cs` | Canvas, RectTransform, Text |
| Spatial | `McpSpatialTools.cs` | Raycast, visibility, line-of-sight |
| Particles | `McpParticleTools.cs` | Particle system modules |
| Terrain | `McpTerrainTools.cs` | Heightmap, painting, trees |
| New category | `McpYourTools.cs` | Create a new file for unrelated tools |

## Adding a Skill Recipe

Skills are step-by-step YAML guides that teach the agent how to combine tools for complex tasks.

### Create a YAML file

Place it in `UserSettings/UnityAgentClient/Skills/` (project-level, not tracked by git):

```yaml
name: setup_outdoor_scene
description: Create a basic outdoor scene with terrain, trees, and lighting
parameters:
  - name: style
    description: Visual style (realistic, stylized, low-poly)
    default: realistic
steps:
  - description: Create a Terrain
    tool: unity_scene
    action: create_object
    notes: Use 'Terrain' type with default settings

  - description: Add trees to the terrain
    tool: unity_scene
    action: create_object
    notes: Add 5-10 tree objects as children of the terrain

  - description: Set up outdoor lighting
    tool: unity_lighting
    action: setup_time_of_day
    notes: Use 'noon' preset for bright outdoor lighting
```

Skills are discovered automatically — no code changes required.

## Improving the UI

The chat UI is built with UI Toolkit (USS + VisualElements). Key files:

- `AgentWindow.cs` — Main `EditorWindow`, conversation flow, ACP protocol
- `AgentWindow.uss` — All styles (dark theme)
- `MarkdownVisualBuilder.cs` — Markdown → VisualElement tree
- `ElicitationPanel.cs` — JSON Schema → native form controls

### USS Class Reference

| Class | Element |
|-------|---------|
| `.message-user` | User message bubble (green) |
| `.message-agent` | Agent message container |
| `.message-thought` | Thinking foldout |
| `.message-tool-call` | Tool call foldout |
| `.md-*` | Markdown elements (h1-h6, code-block, paragraph, etc.) |
| `.elicitation-*` | Elicitation form elements |
| `.toolbar` | Bottom toolbar |

## Submitting Changes

### Pull Request Checklist

- [ ] Tool has a clear, descriptive `Name` (e.g., `editor_undo`, not `undo`)
- [ ] `Description` is a single sentence the agent can understand
- [ ] `InputSchema` has `description` on every property
- [ ] `RequiresMainThread` is set correctly
- [ ] Undo support for any scene/asset modifications
- [ ] Error cases return `McpToolResult.Error()` with actionable messages
- [ ] No `using AgentClientProtocol` — tool code should only reference `IMcpTool`, `McpToolResult`, `ToolPermissionLevel`
- [ ] Tool is registered in `BuiltinMcpServer.RegisterTools()`
- [ ] Tested with at least one agent (Gemini CLI, Claude Code, or Copilot)

### Naming Conventions

- Tool name: `category_action` (e.g., `scene_get_hierarchy`, `editor_enter_playmode`)
- Meta-tool action: short verb (e.g., `get_hierarchy`, `create_object`, `bake`)
- Class name: `PascalCase` + `Tool` suffix (e.g., `GetHierarchyTool`)

## Questions?

- **Architecture**: See `docs/ARCHITECTURE.md`
- **Setup**: See `docs/SETUP.md`
- **Tool list**: See `README.md` for the full tool reference
- **Issues**: Open a GitHub issue for bugs or feature proposals
