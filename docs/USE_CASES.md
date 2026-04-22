# Unity Agent Client — Use Cases & Demo Scenarios

> Showcase scenarios for demonstrating the value of Unity Agent Client.
> Focus on moments that make people say "wait, the AI actually did that?"

---

## Core Differentiator

```
GitHub Copilot:      AI helps you WRITE code
Unity AI:            Unity's locked-in AI, costs cloud tokens
Unity Agent Client:  AI directly OPERATES your Unity editor, any LLM, free
```

**One-liner: "AI doesn't just write your C# scripts — it moves objects, tweaks materials, and builds scenes."**

---

## Demo Scenarios

### Scenario 1: Voice-Level Scene Building (Most Visual)

```
You: "Create a horror game scene: a hallway with a door at the end,
      3 lights but only 1 is on, overall very dark"

AI executes automatically (< 10 seconds):
  1. scene_add_gameobject → Create hallway (stretched Cube)
  2. scene_add_gameobject → Create door (Cube)
  3. scene_add_gameobject × 3 → 3 lights
  4. scene_modify_component → Disable 2 lights
  5. get_render_settings → Dim ambient light

The scene ACTUALLY appears in the Unity editor.
```

**Wow factor**: You finish speaking, and the Scene view changes in real-time.

**Best for**: GitHub README GIF, first impression demos.

---

### Scenario 2: AI "Sees" the Game View (Sentis Vision — Killer Feature)

```
You: "Look at the current camera view and tell me what's wrong"

AI executes:
  1. vision_detect_objects → "3 persons, 2 chairs detected"
  2. vision_estimate_depth → "foreground 30%, midground 45%, background 25%"
  3. editor_get_console_errors → "2 warnings: Missing Rigidbody"

AI: "The scene has 3 characters and 2 chairs. Two characters are missing
     Rigidbody components so they won't be affected by physics.
     The background area (25%) is empty — consider adding environment
     elements to fill the depth."
```

**Wow factor**: AI isn't guessing from code — it **actually looked at the game view** through YOLOv8 + depth estimation, then gave design feedback.

**Best for**: YouTube/Bilibili demo videos, most shareable moment.

---

### Scenario 3: Batch Parameter Tuning (Most Practical)

```
You: "Change all Enemies' movement speed from 5 to 8
      and health from 100 to 150"

AI executes:
  1. scene_find_by_component("EnemyController") → Found 12 objects
  2. scene_modify_component × 12 → Update Speed
  3. scene_modify_component × 12 → Update Health

AI: "Updated 12 Enemy objects: Speed 5→8, Health 100→150"
```

**Wow factor**: Manually clicking through 12 prefabs takes 5 minutes. AI does it in 10 seconds.

**Best for**: Reddit r/Unity3D, developer forums — addresses daily pain points.

---

### Scenario 4: AI Debugger

```
You: "The player is clipping through walls during gameplay, help me check"

AI executes:
  1. scene_get_component_data("Player", "Rigidbody") → Check physics
  2. scene_get_component_data("Player", "CapsuleCollider") → Check collider
  3. editor_get_console_errors → Check errors

AI: "Found the issue: Player's Rigidbody.collisionDetectionMode is set to
     Discrete, but the movement speed is high. Changing to
     ContinuousDynamic will fix wall clipping. Want me to fix it?"

You: "Fix it"

AI: scene_modify_component → Done.
```

**Wow factor**: AI doesn't just diagnose — it **directly fixes** the problem.

**Best for**: Developer community posts, practical trust-building.

---

### Scenario 5: Non-Coders Making Games (Audience Expansion)

```
Designer/Artist: "I don't write code, but I want to try Unity"

"Add a button that makes the character jump when clicked"
  → AI creates UI Button + binds event + adds jump logic

"Make this object red"
  → AI directly changes the Material

"Make this character walk to that point"
  → AI bakes NavMesh + sets up pathfinding
```

**Wow factor**: Zero-code game prototyping.

**Best for**: Broader audience — designers, artists, educators, students.

---

### Scenario 6: Project Audit in Seconds

```
You: "Review my project — any issues I should know about?"

AI executes:
  1. editor_get_console_errors → 23 warnings, 2 errors
  2. asset_list("*.mat") → Find unused materials
  3. scene_get_hierarchy → Check for orphaned objects
  4. editor_get_project_settings → Check quality settings

AI: "Found 3 issues:
     1. 2 NullReferenceExceptions in PlayerController.cs (line 45, 78)
     2. 7 materials are imported but never referenced
     3. 12 empty GameObjects with no components (probably leftover from testing)
     Want me to clean up the empty GameObjects?"
```

**Wow factor**: AI does a full project health check that would take 20 minutes manually.

---

## Key Promotional Messages

| Audience | Message | Angle |
|----------|---------|-------|
| **Developers** | "54 tools. Any LLM. Free. No Unity Cloud tokens." | vs Unity AI |
| **Content Creators** | "Watch AI build a game scene in 30 seconds" | Visual wow |
| **Non-coders** | "Make games with natural language" | Accessibility |
| **Open Source Community** | "Agent Client Protocol for Unity — MIT licensed, extensible" | Ecosystem |

---

## Recommended Hero Demo (30-second GIF/Video)

```
0-5s:   Empty Unity scene
5-10s:  Type: "Create a pinball game with physics"
10-25s: AI calls 8 tools in sequence — scene builds step by step
        (objects appear, Rigidbody added, materials assigned)
25-30s: Press Play → ball actually bounces

Caption: "AI built this. Zero code. Any LLM."
```

This "zero to playable" demo has the highest viral potential.

---

## Comparison Table (for promotional materials)

| Feature | Unity Agent Client | Unity AI | GitHub Copilot |
|---------|-------------------|----------|----------------|
| **LLM Choice** | Any (Claude, Gemini, GPT, local) | Unity's models only | GitHub's models |
| **Cost** | Free (MIT) | Cloud token credits | Subscription |
| **Scene Operations** | ✅ 54 tools, direct editor control | Limited | ❌ Code only |
| **Vision (AI sees game)** | ✅ Sentis YOLOv8 + Depth | ❌ | ❌ |
| **Custom Tools** | ✅ Implement `IMcpTool` | ❌ | ❌ |
| **Offline / Local LLM** | ✅ | ❌ | ❌ |
| **Protocol** | ACP (open standard) | Proprietary | Proprietary |
