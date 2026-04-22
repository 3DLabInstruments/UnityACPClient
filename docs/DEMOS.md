# Demo Scenarios

Recording guides for promotional videos. Each demo is designed to be short (30-90s), visually impressive, and showcase a distinct capability.

---

## Demo 1: "Build a Scene from Nothing" (60s)

**Hook:** Empty scene → complete environment in one conversation.

**Starting state:** New empty Unity scene (just Main Camera + Directional Light)

**Prompt sequence:**

```
You: Please help me build a simple outdoor scene: a green ground with three trees and two rocks, and set it to evening lighting.

（Agent will call ~8 tools in sequence:
  1. create_object → Plane (ground), scale 10, green color
  2-4. create_object → 3x Cylinder (trees), brown + green spheres on top
  5-6. create_object → 2x Sphere (rocks), grey color, random positions
  7. setup_time_of_day → "sunset" preset
  8. save）
```

**What to show on screen:**
- Left: Agent window with streaming responses + tool call foldouts
- Right: Scene view updating in real time — objects appearing one by one
- Final: camera angle showing warm sunset lighting

**Key selling point:** Zero manual work. Natural language → complete scene.

---

## Demo 2: "AI 3D Asset Generation" (90s)

**Hook:** Text description → 3D model → placed in scene, all from chat.

**Starting state:** A simple room scene (floor + 4 walls)

**Prompt sequence:**

```
You: Generate a medieval wooden chair using Meshy AI, then place it in the center of the room

（Agent will:
  1. unity_generate(action="text_to_3d", prompt="medieval wooden chair, game asset")
  2. Poll task status until complete
  3. instantiate_prefab → place the generated model
  4. set_transform → position at center (0, 0, 0)）
```

**Then follow up:**

```
You: Now generate a matching wooden table, and put it next to the chair
```

**What to show:**
- Meshy task progress in the chat
- 3D model appearing in scene after generation
- Agent intelligently placing objects relative to each other

**Key selling point:** AI-generated 3D assets, integrated end-to-end in Unity workflow.

---

## Demo 3: "Scene Inspector — Ask Questions About Your Scene" (45s)

**Hook:** Agent understands your scene like a colleague looking over your shoulder.

**Starting state:** An existing game scene with multiple objects (e.g., a platformer level)

**Prompt sequence:**

```
You: What's in my scene right now?

（Agent calls: describe → structured scene summary）

You: Which objects have a Rigidbody component?

（Agent calls: find_by_component → lists all physics objects）

You: What are the Player's movement settings?

（Agent calls: get_hierarchy → finds Player,
  then get_component_data → reads PlayerMovement component,
  reports Speed=5, JumpForce=10, etc.）
```

**What to show:**
- Agent's markdown-formatted responses with structured data
- Highlighted objects in scene when agent references them

**Key selling point:** Instant project comprehension — no manual Inspector clicking.

---

## Demo 4: "Particle System from Words" (60s)

**Hook:** Describe a visual effect → agent creates and tunes a particle system live.

**Starting state:** Empty scene with dark background

**Prompt sequence:**

```
You: Create a fire particle effect in the center of the scene

（Agent will:
  1. create_object → Empty "FireEffect" with ParticleSystem
  2. set_main → duration=5, loop=true, startLifetime=1.5, startSpeed=3, startSize=0.5
  3. set_emission → rateOverTime=50
  4. set_shape → cone, angle=25, radius=0.3
  5. set_color → gradient from yellow to orange to transparent
  6. set_size → curve decreasing over lifetime
  7. preview → play）
```

**Follow up:**

```
You: Make it more intense — faster, bigger particles, and add some smoke on top

（Agent adjusts parameters + creates second particle system for smoke）
```

**What to show:**
- Scene view with particle system appearing and playing in real time
- Tool calls in chat showing each parameter adjustment
- Before/after comparison

**Key selling point:** Visual effects without touching the Inspector once.

---

## Demo 5: "Lighting in One Word" (30s)

**Hook:** Change the entire mood of a scene with one sentence.

**Starting state:** A furnished room scene with default lighting

**Prompt sequence (rapid fire, 5 mood changes):**

```
You: Set the lighting to morning
（warm sunrise, soft shadows）

You: Now switch to night
（dark blue ambient, moonlight directional）

You: Overcast
（grey flat lighting, no harsh shadows）

You: Sunset
（orange/pink sky, long shadows）

You: Back to noon
（bright white, minimal shadows）
```

**What to show:**
- Scene view transitioning between moods instantly
- Each `setup_time_of_day` call visible in the chat
- Split screen or quick cuts showing all 5 moods

**Key selling point:** Instant art direction iteration. What takes minutes in Inspector takes seconds with AI.

---

## Demo 6: "Fix My Scene — Batch Operations" (45s)

**Hook:** Agent diagnoses and fixes multiple issues in one go.

**Starting state:** A messy scene with problems (objects at wrong positions, missing materials, wrong names)

**Prompt sequence:**

```
You: Look at my scene — there are some objects floating in the air, 
     can you place them all on the ground?

（Agent will:
  1. describe → analyze scene objects
  2. find_by_criteria → objects above y=0.5
  3. Multiple place_on_ground calls → raycast each object to terrain
  Reports: "Placed 4 objects on the ground"）
```

**Follow up:**

```
You: Now rename all the "GameObject (1)", "GameObject (2)" objects 
     to something meaningful based on their shape and position

（Agent uses get_hierarchy + rename for each object:
  "GameObject (1)" → "LeftWall"
  "GameObject (2)" → "FloorTile_Center"
  etc.）
```

**What to show:**
- Hierarchy panel updating in real time
- Objects snapping to ground
- Before/after comparison of hierarchy naming

**Key selling point:** Tedious cleanup tasks automated. Agent does what an intern would do, but instantly.

---

## Demo 7: "Terrain Sculpting with Words" (60s)

**Hook:** Shape terrain by describing geography instead of painting brushes.

**Starting state:** Flat terrain with one texture layer

**Prompt sequence:**

```
You: Create a mountain in the center of the terrain, about 50 meters high

（Agent calls: modify_height with raise mode, center position, large radius）

You: Add a valley on the east side

（Agent calls: modify_height with lower mode, east offset）

You: Paint grass on the flat areas and rock texture on the mountain slopes

（Agent calls: paint_texture for grass on low areas, rock on steep areas）

You: Add a cluster of 20 trees around the base of the mountain

（Agent calls: add_trees with scattered positions）
```

**What to show:**
- Terrain deforming in real time in Scene view
- Textures being painted
- Trees populating

**Key selling point:** Level design at the speed of conversation.

---

## Demo 8: "Structured Input — Elicitation" (45s)

**Hook:** Agent asks exactly what it needs via a real form — no more guessing or free-text back-and-forth.

**Starting state:** Any scene. The key is the conversation, not the scene content.

**Prompt sequence:**

```
You: Refactor the player controller

（Agent sends elicitation/create → a structured form pops up inline:
  - Dropdown: "Refactoring Strategy" → Conservative / Balanced / Aggressive
  - Text field: "Additional notes"
  - Pre-selected default: "Balanced"
  User picks "Aggressive", types "keep backward compatibility", clicks Submit）

（Agent receives typed data {strategy: "aggressive", note: "keep backward compatibility"}
  and proceeds with the exact parameters — no ambiguity）
```

**Then show advanced Unity-native fields:**

```
You: Place a light source in my scene

（Agent sends elicitation with Unity-specific formats:
  - Vector3Field: "Position" → user picks (3, 5, -2) with native Unity input
  - ColorField: "Light Color" → user picks warm orange via color picker
  - PopupField: "Light Type" → Point / Spot / Directional
  User fills the form, clicks Submit → light created with exact parameters）
```

**What to show:**
- Elicitation panel appearing inline below the chat messages
- Native Unity controls: dropdowns, color picker, Vector3 field
- The three actions: Submit (accept), Decline, Cancel
- Agent using the structured response to proceed without further questions

**Key selling point:** No more guessing or parsing free-text. The agent asks structured questions, gets typed answers, and acts precisely.

---

## Recording Tips

- **Resolution:** 1920×1080, Unity in dark theme
- **Layout:** Agent window docked on right (1/3 width), Scene view on left (2/3)
- **Speed:** Real-time for first tool call, then 2× speed for repetitive calls
- **Audio:** Optional voiceover or text captions explaining what's happening
- **Subtitles:** Include both English and Chinese prompts for wider reach
- **Length:** Twitter/X demos ≤ 60s, YouTube demos ≤ 90s, README GIF ≤ 30s

### Recommended Recording Order

1. **Demo 5** (Lighting) — shortest, most visually dramatic, best for GIF/tweet
2. **Demo 1** (Build Scene) — the "hero" demo, shows core value proposition
3. **Demo 4** (Particles) — visually impressive, unique to this tool
4. **Demo 8** (Elicitation) — unique UX differentiator, shows structured agent↔user interaction
5. **Demo 3** (Scene Inspector) — shows AI understanding, good for devs
6. **Demo 6** (Batch Fix) — practical value, resonates with daily pain
7. **Demo 7** (Terrain) — impressive but needs terrain setup
8. **Demo 2** (3D Gen) — depends on Meshy API, longest wait time
