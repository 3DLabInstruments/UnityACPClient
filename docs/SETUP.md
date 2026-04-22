# Setup Guide

This guide walks you through installing and configuring Unity Agent Client from scratch.

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| **Unity** | 2021.3 or later | LTS versions recommended |
| **Node.js** | 16+ | Optional — only needed if your agent doesn't support HTTP MCP transport |
| **An ACP-compatible agent** | — | See [Supported Agents](#step-3-install-an-acp-agent) below |

## Step 1: Install Unity Agent Client

### Option A: Clone the Repository (Recommended)

Clone the repo and open it directly in Unity:

```bash
git clone https://github.com/3DLabInstruments/UnityACPClient.git
```

Open the cloned folder in Unity Hub as a project. **That's it** — the Core DLL and all dependencies are bundled inside `Assets/UnityAgentClient/Editor/Plugins/`. No extra installation steps required.

### Option B: Add to an Existing Project

If you already have a Unity project and want to add Unity Agent Client to it:

1. Copy the `Assets/UnityAgentClient/` folder into your project's `Assets/` directory (DLLs are included inside `Editor/Plugins/`)
2. Reopen Unity — the plugin will be available immediately

## Step 2: Verify Installation

After installation, you should see:
- `Window > Unity Agent Client > AI Agent` menu item
- `Project Settings > Unity Agent Client` settings panel

## Step 3: Install an ACP Agent

You need an AI agent that supports the Agent Client Protocol. Choose one:

### GitHub Copilot CLI

If you have a GitHub Copilot subscription, this is the easiest option — no extra API keys needed.

```bash
# Install the GitHub CLI first: https://cli.github.com/
# Then install the Copilot extension
gh extension install github/gh-copilot

# Verify
copilot --version
```

**Unity Settings:**

| Field | Value |
|---|---|
| Command | `copilot` |
| Arguments | `--acp` |

### Gemini CLI (Google)

```bash
# Install
npm install -g @anthropic-ai/gemini-cli
# or
pip install gemini-cli

# Verify
gemini --version
```

**Unity Settings:**

| Field | Value |
|---|---|
| Command | `gemini` |
| Arguments | `--experimental-acp` |

If using an API key, add `GEMINI_API_KEY` to the Environment Variables section.

### opencode (Recommended — supports any LLM)

```bash
# Install (macOS/Linux)
curl -fsSL https://opencode.ai/install | bash

# Verify
opencode --version
```

**Unity Settings:**

| Field | Value |
|---|---|
| Command | `opencode` |
| Arguments | `acp` |

### Claude Code (via ACP adapter)

Claude Code requires the [claude-code-acp](https://github.com/zed-industries/claude-code-acp) adapter:

```bash
# Install the adapter (follow the repo README)
npm install -g claude-code-acp
```

**Unity Settings:**

| Field | Value |
|---|---|
| Command | `claude-code-acp` |
| Arguments | *(leave empty)* |

### Codex CLI (via ACP adapter)

Codex CLI requires the [codex-acp](https://github.com/zed-industries/codex-acp) adapter:

```bash
# Install the adapter (follow the repo README)
npm install -g codex-acp
```

**Unity Settings:**

| Field | Value |
|---|---|
| Command | `codex-acp` |
| Arguments | *(leave empty)* |

### Goose (Block)

```bash
# Install
brew install block/tap/goose
# or see https://block.github.io/goose/

# Verify
goose --version
```

**Unity Settings:**

| Field | Value |
|---|---|
| Command | `goose` |
| Arguments | `acp` |

## Step 4: Configure Unity Agent Client

1. Open **Edit > Project Settings > Unity Agent Client**
2. Fill in:
   - **Command**: The agent binary name or full path
   - **Arguments**: Agent-specific arguments (see table above)
   - **Environment Variables**: API keys or config (see examples below)
   - **Verbose Logging**: Enable for debugging connection issues

> [!NOTE]
> **macOS users**: If the agent command is not found, PATH resolution may fail in zsh.
> Use `which gemini` (or your agent command) in Terminal to get the full path, then enter the absolute path in the Command field.

> [!WARNING]
> Settings are saved in `UserSettings/UnityAgentClientSettings.json` — this folder is typically `.gitignore`d. Do NOT commit API keys to version control.

### LLM Provider Configuration

The agent handles LLM communication. Pass your API credentials via Environment Variables in Unity settings.

<details>
<summary>Azure OpenAI</summary>

| Environment Variable | Value |
|---|---|
| `AZURE_OPENAI_API_KEY` | Your Azure OpenAI API key |
| `AZURE_OPENAI_ENDPOINT` | `https://your-resource.openai.azure.com` |
| `AZURE_OPENAI_API_VERSION` | `2024-12-01-preview` |
| `AZURE_OPENAI_DEPLOYMENT` | Your deployment name (e.g., `gpt-4o`) |

> Note: Variable names may differ per agent. Check your agent's documentation for Azure-specific config. opencode and goose both support Azure OpenAI natively.

</details>

<details>
<summary>OpenAI</summary>

| Environment Variable | Value |
|---|---|
| `OPENAI_API_KEY` | `sk-...` |
| `OPENAI_BASE_URL` | (optional) Custom endpoint URL |

</details>

<details>
<summary>Anthropic (Claude)</summary>

| Environment Variable | Value |
|---|---|
| `ANTHROPIC_API_KEY` | `sk-ant-...` |

</details>

<details>
<summary>Google Gemini</summary>

| Environment Variable | Value |
|---|---|
| `GEMINI_API_KEY` | Your Gemini API key |

</details>

<details>
<summary>Custom/Local LLM (Ollama, vLLM, etc.)</summary>

| Environment Variable | Value |
|---|---|
| `OPENAI_API_KEY` | `dummy` (or your key) |
| `OPENAI_BASE_URL` | `http://localhost:11434/v1` (Ollama example) |

Most agents support OpenAI-compatible endpoints. Point `OPENAI_BASE_URL` to your local server.

</details>

## Step 5: Connect and Use

1. Open **Window > Unity Agent Client > AI Agent**
2. The window will automatically connect to the configured agent. A colored status dot in the toolbar shows the connection state (green / yellow / red). If connection fails, use the **Retry** or **Open Settings** buttons that appear.
3. Type a prompt and press **Enter** or click **Send**. **Shift+Enter** inserts a newline (the input field auto-grows). Press **Esc** to cancel a running request.
4. Drag and drop assets into the window to attach them — they appear as compact chips below the input. Click a chip to ping the asset in the Project window; click × to remove it.
5. The toolbar at the bottom shows the current **Mode** and **Model** (if the agent supports them). Mode names are shown as human-readable labels — hover to see the full mode identifier as a tooltip.

### First-time Test Prompts

Try these to verify everything works:

```
"Show me the scene hierarchy"
"What are the project settings?"
"List all scenes in the project"
"Show me the recent console logs"
```

## Troubleshooting

### "Connecting..." hangs for a long time

Connection attempts now time out after **30 seconds**. After timeout, the window shows a **Retry** button and an **Open Settings** button with the error message — so you can fix the command or credentials without closing the window. While connecting, a **Cancel** button lets you abort immediately.

Common causes:
- Check if the agent binary is installed and accessible from the terminal
- On macOS, use the full path to the binary (e.g., `/usr/local/bin/gemini`)
- Enable **Verbose Logging** in Project Settings to see detailed connection logs

### "Connection Error" with Retry button

- The agent process crashed or failed to start
- Click **Open Settings** to fix the Command and Arguments without closing the window
- Check the Unity Console for error messages
- Ensure API keys are set if required

### Tools are not working

- The MCP server starts on port 57123 (or next available port)
- Check if another application is using that port
- If Node.js is not installed and the agent doesn't support HTTP MCP, install Node.js 16+

### Domain Reload restarts the agent

This is expected. Unity's Domain Reload (triggered by C# script changes) kills all running processes. The agent will automatically reconnect and restore the most recent session from the session list. Your session history (up to 20 sessions per agent config) is preserved across reloads. Avoid using the agent to edit C# scripts inside Unity — use an IDE for coding instead.

### Agent can't find Unity tools

- Verify the MCP server is running (check Console for "MCP Server started on port XXXXX")
- Some agents may need to be restarted after first-time setup
- Try `unity_tool(name="list")` to verify tool visibility

## Directory Structure

After installation, the relevant files are:

```
YourUnityProject/
├── Assets/
│   └── (your project files)
├── Packages/
│   └── com.yetsmarch.unity-agent-client/     ← installed package
│       └── Editor/
│           ├── AgentWindow.cs             ← main UI
│           ├── BuiltinMcpServer.cs        ← MCP JSON-RPC server
│           ├── McpToolRegistry.cs         ← tool system
│           ├── McpSceneTools.cs           ← scene tools
│           ├── MetaToolRouter.cs          ← meta-tool grouping
│           ├── SkillRegistry.cs           ← skill system
│           ├── BuiltinSkills.cs           ← 14 built-in skills
│           └── server.js                  ← MCP stdio proxy (Node.js)
├── UserSettings/
│   ├── UnityAgentClientSettings.json      ← your agent config
│   └── UnityAgentClient/
│       └── Skills/                        ← your custom skill YAML files
└── ...
```

## Optional: Sentis Vision Extension

Enable AI-powered scene understanding (object detection, depth estimation) using your local GPU. This lets the agent "see" your scene.

### Prerequisites

- Unity 2023.2+ (for Sentis) or Unity 6+ (for Inference Engine)
- GPU with 4GB+ VRAM (recommended: NVIDIA RTX series)
- Python 3.8+ (only for model export, not runtime)

### Step 1: Install Sentis Package

Open **Window > Package Manager**:

**Unity 2023.2 - Unity 6.1:**
1. Click `+ > Add package by name...`
2. Enter: `com.unity.sentis`
3. Click **Add**

**Unity 6.2+:**
1. Click `+ > Add package by name...`
2. Enter: `com.unity.ai.inference`
3. Click **Add**

### Step 2: Add Scripting Define Symbols

1. Open **Edit > Project Settings > Player**
2. Expand **Other Settings > Script Compilation**
3. In **Scripting Define Symbols**, add:

```
UNITY_SENTIS
```

If using Unity 6.2+ (Inference Engine), also add:
```
UNITY_INFERENCE_ENGINE
```

4. Click **Apply**
5. Wait for recompilation

### Step 3: Prepare ONNX Models

Create the models directory:
```
Assets/
  StreamingAssets/
    AgentVision/         ← create this folder
      yolov8m.onnx       ← object detection model
      depth_anything_s.onnx  ← depth estimation model
```

#### Download YOLOv8 Model

Option A — Use the pre-exported model from Ultralytics:

```bash
pip install ultralytics
python -c "from ultralytics import YOLO; YOLO('yolov8m.pt').export(format='onnx', opset=15)"
```

This creates `yolov8m.onnx`. Copy it to `Assets/StreamingAssets/AgentVision/`.

Option B — Download directly:
- Go to [Ultralytics YOLOv8 releases](https://github.com/ultralytics/assets/releases)
- Download `yolov8m.onnx` (~50MB)

#### Download Depth Anything Model

```bash
pip install huggingface_hub
python -c "
from huggingface_hub import hf_hub_download
hf_hub_download('depth-anything/Depth-Anything-V2-Small', 'depth_anything_v2_vits.onnx', local_dir='.')
"
```

Rename to `depth_anything_s.onnx` and copy to `Assets/StreamingAssets/AgentVision/`.

> **Alternative:** Search Hugging Face for any MiDaS or Depth Anything model exported to ONNX with opset 7-15.

### Step 4: Verify Installation

1. Restart Unity
2. Check the Console for: `Sentis vision tools added to unity_spatial`
3. Open **Window > Unity Agent Client > AI Agent**
4. Test with these prompts:

```
"Detect objects in the current camera view"
"Analyze the depth in this scene"
"Describe what the camera can see"
```

### VRAM Usage Reference

| Model | VRAM | Inference Time (RTX 4060 Ti) |
|---|---|---|
| YOLOv8m (detection) | ~200MB | ~5ms |
| Depth Anything Small | ~500MB | ~15ms |
| Both loaded | ~700MB | — |

Your 16GB GPU can comfortably run both models simultaneously with plenty of headroom for Unity itself.

### Troubleshooting

**"Model not installed" error:**
- Verify the ONNX files exist in `Assets/StreamingAssets/AgentVision/`
- Check file names match exactly: `yolov8m.onnx`, `depth_anything_s.onnx`

**"Sentis vision tools" message not appearing:**
- Confirm `UNITY_SENTIS` is in Scripting Define Symbols
- Confirm the Sentis/Inference Engine package is installed in Package Manager
- Check for compilation errors in Console

**Inference is slow:**
- Ensure Unity is using GPU backend: the tools default to `BackendType.GPUCompute`
- Check **Edit > Project Settings > Player > Other Settings > Graphics APIs** — Vulkan or Direct3D12 recommended

**Model compatibility errors:**
- Sentis supports ONNX opset 7-15. Check your model's opset version
- Some operators may not be supported. Refer to [Sentis supported operators](https://docs.unity3d.com/Packages/com.unity.sentis@latest/manual/supported-operators.html)

### Available Vision Tools

After setup, these actions are available in `unity_spatial`:

| Action | Description |
|---|---|
| `detect_objects` | Capture camera view → YOLOv8 → returns object classes, positions, confidence scores |
| `estimate_depth` | Capture camera view → depth model → returns depth statistics (min/max/avg, near/mid/far distribution). Optionally saves depth map PNG |
| `describe_view` | Runs both detection + depth + camera info → comprehensive scene analysis |

Example agent interaction:
```
You:   "What's in this room?"
Agent: unity_spatial(action="detect_objects")
       → "Detected 5 objects: chair (94%), table (91%), potted plant (87%),
          tv (82%), couch (79%)"

You:   "How deep is the scene?"
Agent: unity_spatial(action="estimate_depth")
       → "Depth Analysis:
            Min: 0.12, Max: 0.95, Avg: 0.48
            Near zone: 23% of pixels
            Mid zone: 45% of pixels
            Far zone: 32% of pixels"

You:   "Give me a full analysis of this view"
Agent: unity_spatial(action="describe_view")
       → Combined: objects + depth + camera position/rotation/FOV
```

## Next Steps

- Read the [README](README.md) for the full tool reference
- Check the [ROADMAP](ROADMAP.md) for upcoming features
- Add custom tools by implementing `IMcpTool`
- Add custom skills by placing YAML files in `UserSettings/UnityAgentClient/Skills/`
