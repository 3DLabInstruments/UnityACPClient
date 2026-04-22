# Regression Test Guide

This document provides a comprehensive regression test checklist for Unity Agent Client. Use it to verify that all tools and features work correctly after code changes.

## Prerequisites

- Unity 2021.3+ project with Unity Agent Client installed
- A scene with diverse objects (GameObjects, Lights, Camera, Terrain, ParticleSystem, UI Canvas)
- An ACP-compatible agent connected and showing "Connected" status

## Test Environment Setup

Before running tests, prepare the scene:

1. Create a scene with at least: a Camera, a Directional Light, a Cube, a Sphere, an empty GameObject
2. Add a `ParticleSystem` component to an empty GameObject named "TestParticle"
3. Add a `Terrain` to the scene (3D Object > Terrain)
4. Add a `Canvas` with a `Text` child for UI tests
5. Ensure at least one material exists in `Assets/Materials/`
6. Tag the Cube as "Player", set the Sphere to layer "Water"

---

## 1. unity_scene — Scene & GameObject Operations

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 1.1 | `get_hierarchy` | Call with no args | Returns scene tree with all GameObjects | ☐ |
| 1.2 | `get_component_data` | Query the Cube's Transform | Returns position, rotation, scale values | ☐ |
| 1.3 | `modify_component` | Change Cube's Transform position to (1,2,3) | Cube moves; Undo reverts it | ☐ |
| 1.4 | `create_object` | Create a Capsule named "TestCapsule" at (0,0,5) with color "1,0,0,1" | Red capsule appears at position | ☐ |
| 1.5 | `add_gameobject` | Create a Cylinder named "TestCylinder" | Cylinder appears in hierarchy | ☐ |
| 1.6 | `delete_gameobject` | Delete "TestCylinder" | Removed; requires permission (or Auto Approve) | ☐ |
| 1.7 | `save` | Save the scene | Scene saved without error | ☐ |
| 1.8 | `set_selection` | Select the Cube | Inspector shows Cube properties | ☐ |
| 1.9 | `reparent` | Reparent "TestCapsule" under the Cube | TestCapsule becomes child of Cube | ☐ |
| 1.10 | `duplicate` | Duplicate the Sphere | A copy appears with "(1)" suffix | ☐ |
| 1.11 | `find_by_component` | Find all objects with Light component | Returns Directional Light | ☐ |
| 1.12 | `set_active` | Deactivate the Sphere | Sphere becomes inactive | ☐ |
| 1.13 | `add_component` | Add Rigidbody to Cube | Rigidbody component appears | ☐ |
| 1.14 | `remove_component` | Remove Rigidbody from Cube | Component removed; requires permission | ☐ |
| 1.15 | `rename` | Rename Cube to "MainCube" | Name updated in hierarchy | ☐ |
| 1.16 | `set_transform` | Set position=(0,1,0), rotation=(0,45,0) | Cube moves and rotates | ☐ |
| 1.17 | `open_scene` | Open another scene by path | Scene loads correctly | ☐ |
| 1.18 | `set_text` | Set text on UI Text object | Text content updates | ☐ |
| 1.19 | `instantiate_prefab` | Instantiate a prefab | Prefab instance appears | ☐ |
| 1.20 | `assign_material` | Assign red color to Sphere | Sphere turns red | ☐ |
| 1.21 | `find_by_criteria` | Find by tag="Player" | Returns Cube with full hierarchy path | ☐ |
| 1.22 | `find_by_criteria` | Find by layer="Water" | Returns Sphere with full hierarchy path | ☐ |
| 1.23 | `find_by_criteria` | Find by componentType="Camera" | Returns Camera object | ☐ |
| 1.24 | `find_by_criteria` | Find by namePattern="test" | Returns all objects with "test" in name (case-insensitive) | ☐ |
| 1.25 | `find_by_criteria` | No filter provided | Returns error: must provide at least one filter | ☐ |
| 1.26 | `describe` | Call with no args | Returns scene summary: object count, cameras, lights, component stats, bounds | ☐ |
| 1.27 | `place_on_ground` | Place the Sphere on the ground | Sphere Y adjusts to terrain/ground surface; bottom touches surface | ☐ |
| 1.28 | `place_on_ground` | Place on ground with no colliders below | Returns error: no ground found | ☐ |

## 2. unity_editor — Editor Control

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 2.1 | `get_state` | Call with no args | Returns play mode state, active scene info | ☐ |
| 2.2 | `enter_playmode` | Enter play mode | Editor enters play mode; requires permission | ☐ |
| 2.3 | `pause_playmode` | Pause while playing | Game pauses | ☐ |
| 2.4 | `stop_playmode` | Stop play mode | Editor returns to edit mode | ☐ |
| 2.5 | `screenshot` | Take a screenshot | Image file saved to specified path | ☐ |
| 2.6 | `undo` | After a scene change, undo | Previous state restored | ☐ |
| 2.7 | `redo` | After undo, redo | Change reapplied | ☐ |
| 2.8 | `get_console_logs` | Read console output | Returns recent log entries | ☐ |
| 2.9 | `get_console_errors` | Read error-only logs | Returns only error/exception entries | ☐ |
| 2.10 | `get_project_settings` | Query project settings | Returns project name, company, target platform | ☐ |
| 2.11 | `execute_menu_item` | Execute a valid menu item | Menu action executes | ☐ |
| 2.12 | `build` | Trigger build | Build starts; requires Dangerous permission | ☐ |

## 3. unity_asset — Asset Management

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 3.1 | `list` | List assets in "Assets/Materials" | Returns material file list | ☐ |
| 3.2 | `get_info` | Get info on a material asset | Returns asset type, path, dependencies | ☐ |
| 3.3 | `find_references` | Find references to a texture | Returns assets using the texture | ☐ |
| 3.4 | `create_folder` | Create "Assets/TestFolder" | Folder appears in Project | ☐ |
| 3.5 | `create_material` | Create a new material | Material asset created | ☐ |
| 3.6 | `create_prefab` | Create prefab from Cube | Prefab saved in Assets | ☐ |
| 3.7 | `rename` | Rename the test material | Asset name updated | ☐ |
| 3.8 | `move` | Move material to TestFolder | Asset moved | ☐ |
| 3.9 | `delete` | Delete TestFolder | Folder deleted; requires permission | ☐ |
| 3.10 | `refresh` | Refresh asset database | AssetDatabase refreshed | ☐ |

## 4. unity_material — Material Operations

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 4.1 | `get_properties` | Read material properties | Returns shader, color, texture properties | ☐ |
| 4.2 | `set_property` | Set color to blue | Material color updates | ☐ |
| 4.3 | `assign` | Assign material to Sphere by color | Sphere renders with new color | ☐ |
| 4.4 | `get_render_settings` | Read render pipeline settings | Returns quality, pipeline info | ☐ |
| 4.5 | `create` | Create a new material asset | Material created in project | ☐ |

## 5. unity_lighting — Lighting & Environment

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 5.1 | `get_settings` | Read lighting settings | Returns ambient, fog, skybox, lightmap, lights info | ☐ |
| 5.2 | `set_ambient` | Set ambient color to warm yellow | Ambient light updates | ☐ |
| 5.3 | `bake` | Bake lightmaps | Bake process starts | ☐ |
| 5.4 | `bake` | Bake during Play Mode | Returns error: cannot bake in play mode | ☐ |
| 5.5 | `setup_time_of_day` | Set to "morning" | Directional light angle low, warm color, fog enabled | ☐ |
| 5.6 | `setup_time_of_day` | Set to "noon" | Directional light high, bright white, no fog | ☐ |
| 5.7 | `setup_time_of_day` | Set to "sunset" | Directional light low, orange tones | ☐ |
| 5.8 | `setup_time_of_day` | Set to "night" | Very dim blue light, dark ambient | ☐ |
| 5.9 | `setup_time_of_day` | Set to "overcast" | Grey light, fog enabled | ☐ |
| 5.10 | `setup_time_of_day` | No directional light in scene | Auto-creates a Directional Light | ☐ |
| 5.11 | `setup_time_of_day` | Undo after applying | All lighting reverts to previous state | ☐ |
| 5.12 | `setup_time_of_day` | Invalid timeOfDay value | Returns descriptive error | ☐ |

## 6. unity_animation — Animation & Navigation

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 6.1 | `get_controllers` | List AnimatorControllers in project | Returns controller list | ☐ |
| 6.2 | `get_states` | Read states for a controller | Returns state machine info | ☐ |
| 6.3 | `get_parameters` | Read parameters | Returns parameter names, types, defaults | ☐ |
| 6.4 | `navmesh_bake` | Bake NavMesh | NavMesh generated | ☐ |
| 6.5 | `navmesh_get_settings` | Read NavMesh settings | Returns agent radius, height, slope | ☐ |
| 6.6 | `navmesh_query_path` | Query path between two points | Returns path validity, distance, waypoints | ☐ |

## 7. unity_spatial — Spatial Intelligence

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 7.1 | `raycast` | Cast ray from (0,10,0) downward | Returns hit object and point | ☐ |
| 7.2 | `camera_visibility` | Check if Cube is visible to camera | Returns visibility status | ☐ |
| 7.3 | `check_line_of_sight` | Check line of sight from Camera to Cube | Returns clear/blocked status | ☐ |
| 7.4 | `detect_visible_objects` | Detect all visible objects | Returns list of visible objects | ☐ |

## 8. unity_ui — UI Operations

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 8.1 | `get_canvas_hierarchy` | Read Canvas hierarchy | Returns UI element tree | ☐ |
| 8.2 | `modify_rect_transform` | Change Text RectTransform size | UI element resized | ☐ |
| 8.3 | `set_text` | Set Text content to "Hello" | Text displays "Hello" | ☐ |

## 9. unity_generate — AI 3D Generation

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 9.1 | `text_to_3d` | Generate from text prompt | Returns task ID, starts generation | ☐ |
| 9.2 | `image_to_3d` | Generate from image | Returns task ID | ☐ |
| 9.3 | `list_tasks` | List generation tasks | Returns task status list | ☐ |

> **Note:** Requires `MESHY_API_KEY` environment variable.

## 10. unity_particle — Particle System Tools

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 10.1 | `get_settings` | Read TestParticle settings | Returns all module configurations | ☐ |
| 10.2 | `get_settings` | Query non-existent object | Returns error: GameObject not found | ☐ |
| 10.3 | `get_settings` | Query object without ParticleSystem | Returns error: no ParticleSystem | ☐ |
| 10.4 | `set_main` | Set duration=3, loop=false, startSize=2, startColor="1,0,0,1" | Main module updated; values reflected in Inspector | ☐ |
| 10.5 | `set_main` | Set simulationSpace="World" | Simulation space changes to World | ☐ |
| 10.6 | `set_main` | Set maxParticles=500 | Max particles updated | ☐ |
| 10.7 | `set_main` | No properties provided | Returns error: provide at least one property | ☐ |
| 10.8 | `set_emission` | Set rateOverTime=50 | Emission rate changes | ☐ |
| 10.9 | `set_emission` | Add burst at time=0.5, count=20 | Burst appears in emission module | ☐ |
| 10.10 | `set_emission` | Disable emission | Module disabled | ☐ |
| 10.11 | `set_shape` | Set shapeType="Cone", radius=2, angle=30 | Shape changes to cone | ☐ |
| 10.12 | `set_shape` | Set shapeType="Box", scale="2,1,3" | Box shape with custom scale | ☐ |
| 10.13 | `set_shape` | Invalid shapeType | Returns error | ☐ |
| 10.14 | `set_color` | Set gradient: startColor="1,1,0,1", endColor="1,0,0,0" | Yellow→Red fade with alpha | ☐ |
| 10.15 | `set_color` | Set single startColor only | Constant color applied | ☐ |
| 10.16 | `set_size` | Set startSize=1, endSize=0 | Particles shrink over lifetime | ☐ |
| 10.17 | `set_size` | Set startSize=0.5 only | Constant size multiplier | ☐ |
| 10.18 | `set_renderer` | Set renderMode="Stretch" | Render mode changes | ☐ |
| 10.19 | `set_renderer` | Assign material by asset path | Material applied to particle renderer | ☐ |
| 10.20 | `set_renderer` | Invalid material path | Returns error: material not found | ☐ |
| 10.21 | `preview` | command="play" | Particle system starts playing | ☐ |
| 10.22 | `preview` | command="stop" | Particle system stops | ☐ |
| 10.23 | `preview` | command="restart" | Clears and replays | ☐ |
| 10.24 | `preview` | command="simulate", simulateTime=2.0 | Simulates to t=2s | ☐ |
| 10.25 | Undo | Undo after set_main | Previous values restored | ☐ |

## 11. unity_terrain — Terrain Operations

| # | Action | Test Steps | Expected Result | Pass |
|---|--------|-----------|-----------------|------|
| 11.1 | `get_settings` | Read terrain settings | Returns size, heightmap res, layers, trees, details | ☐ |
| 11.2 | `get_settings` | No terrain in scene | Returns error: no terrain found | ☐ |
| 11.3 | `get_height` | Sample height at terrain center | Returns Y coordinate | ☐ |
| 11.4 | `get_height` | Sample outside terrain bounds | Returns error: outside bounds | ☐ |
| 11.5 | `modify_height` | mode="raise", x=50, z=50, height=5, radius=10 | Terrain raised at position | ☐ |
| 11.6 | `modify_height` | mode="lower", same position, height=3 | Terrain lowered | ☐ |
| 11.7 | `modify_height` | mode="set", height=10, radius=5, falloff=0.8 | Smooth hill at absolute height | ☐ |
| 11.8 | `modify_height` | mode="flatten", height=5, radius=15 | Area flattened to height 5 | ☐ |
| 11.9 | `modify_height` | mode="smooth", radius=10, strength=0.5 | Area smoothed | ☐ |
| 11.10 | `modify_height` | mode="raise" without height param | Returns error: height required | ☐ |
| 11.11 | `modify_height` | Position outside terrain | Returns error: outside bounds | ☐ |
| 11.12 | `paint_texture` | Paint layer 0 at center, radius=10, opacity=1 | Texture painted at position | ☐ |
| 11.13 | `paint_texture` | Paint with opacity=0.5, falloff=0.3 | Partial paint with soft edges | ☐ |
| 11.14 | `paint_texture` | Invalid layerIndex=99 | Returns error: index out of range | ☐ |
| 11.15 | `add_trees` | Add 3 trees at specified positions | Trees appear on terrain | ☐ |
| 11.16 | `add_trees` | Invalid prototypeIndex | Returns error: index out of range | ☐ |
| 11.17 | `add_trees` | Position outside terrain | Silently skips out-of-bounds positions | ☐ |
| 11.18 | Undo | Undo after modify_height | Heightmap reverts | ☐ |
| 11.19 | Undo | Undo after paint_texture | Splatmap reverts | ☐ |

## 12. Cross-Cutting Concerns

| # | Feature | Test Steps | Expected Result | Pass |
|---|---------|-----------|-----------------|------|
| 12.1 | **Permission: ReadOnly** | Call get_hierarchy | Executes without prompt | ☐ |
| 12.2 | **Permission: Write** | Call create_object | Executes (tracked for undo) | ☐ |
| 12.3 | **Permission: Dangerous** | Call delete_gameobject with Auto Approve OFF | Prompts for confirmation | ☐ |
| 12.4 | **Permission: Auto Approve** | Enable Auto Approve, call delete_gameobject | Executes without prompt | ☐ |
| 12.5 | **Undo integration** | Make 3 changes, undo 3 times | All changes reverted in order | ☐ |
| 12.6 | **unity_batch** | Batch: create object + assign material | Both operations succeed in one call | ☐ |
| 12.7 | **unity_tool** | Direct access: unity_tool(name="scene_get_hierarchy") | Returns hierarchy (bypasses router) | ☐ |
| 12.8 | **unity_tool** | unity_tool(name="list") | Returns all registered tool names | ☐ |
| 12.9 | **unity_skill** | List available skills | Returns 14+ built-in skills | ☐ |
| 12.10 | **Domain Reload** | Trigger script compilation | Agent reconnects after reload | ☐ |
| 12.11 | **Play Mode guard** | Enter play mode, try bake lightmap | Returns error: cannot during play mode | ☐ |
| 12.12 | **Main thread dispatch** | Call any RequiresMainThread tool | Executes on main thread without freeze | ☐ |
| 12.13 | **Error handling** | Call with missing required param | Returns clear error message | ☐ |
| 12.14 | **Scene dirty** | Make any write change | Scene marked as modified (asterisk in title) | ☐ |
| 12.15 | **Connection timeout** | Set Command to a non-existent binary, open window | Times out after ~30s; shows error + Retry + Open Settings buttons | ☐ |
| 12.16 | **Cancel connection** | While connecting (Pending), click Cancel | Connection aborted; shows Open Settings button | ☐ |
| 12.17 | **Retry connection** | After failure, fix Command in Settings, click Retry | Successfully connects | ☐ |
| 12.18 | **Mode display** | Connect to an agent with multiple modes | Toolbar shows friendly mode names (e.g. "Agent") not raw URLs | ☐ |
| 12.19 | **Connection status dot** | Open window with valid config | Toolbar dot transitions yellow → green on connect; red on failure; tooltip reflects state | ☐ |
| 12.20 | **Typing indicator** | Send any prompt | "Agent is thinking…" with animated dots appears while running, disappears when done | ☐ |
| 12.21 | **Enter-to-send / Shift+Enter** | Type text, press Enter | Sends. Shift+Enter inserts newline, input auto-grows up to max height | ☐ |
| 12.22 | **Escape cancels** | Send a long-running prompt, press Esc | Request cancelled, Stop button disappears | ☐ |
| 12.23 | **Drag overlay** | Drag an asset over the window | Green-tinted overlay with "Drop assets here" shows; hides on drop or leave | ☐ |
| 12.24 | **Attachment chip** | Drop an asset; click the chip; click × | Chip shows icon + name; click pings asset in Project; × removes attachment | ☐ |
| 12.25 | **Auto-scroll stickiness** | Scroll up during streaming response | Auto-scroll pauses; resumes only when scrolled back near bottom | ☐ |
| 12.26 | **Narrow layout** | Resize window width < 500px | Toolbar compacts (Auto Approve label hidden); widen restores | ☐ |

## 13. Elicitation (ACP RFD — Steps 1–4)

> Run the mock agent: configure command=`node`, args=`["<repo>/docs/samples/mock-elicitation-agent.js"]`.
> Keywords: "full"/"all" → Step 2 form, "unity"/"native" → Step 3 Unity fields, "url"/"browser" → Step 4 URL mode.

| # | Feature | Test Steps | Expected Result | Pass |
|---|---------|------------|-----------------|------|
| 13.1 | **Form appears** | Send any prompt; mock agent issues `elicitation/create` | Panel appears below messages with the prompt message and a strategy dropdown | ☐ |
| 13.2 | **Accept round-trips** | Pick a strategy, click Submit | Agent prints `You chose: {"strategy":"<value>", …}` | ☐ |
| 13.3 | **Decline** | Click Decline | Agent prints `You declined.` | ☐ |
| 13.4 | **Cancel via button** | Click Cancel | Agent prints `You canceled.` | ☐ |
| 13.5 | **Required validation** | Submit full form without setting required fields | Inline per-field errors appear; summary says "Please fix N highlighted fields" | ☐ |
| 13.6 | **oneOf labels** | Dropdown shows human titles ("Balanced (Recommended)") but submitted value is the `const` | ☐ |
| 13.7 | **Default applied** | Dropdown pre-selects `balanced` (the schema default) | ☐ |
| 13.8 | **Domain reload mid-elicitation** | Open the form, edit any script to trigger reload | Panel disappears; no hang; agent receives `cancel` | ☐ |
| 13.9 | **Boolean toggle** | Send "full"; form shows "Enable logging" as a Toggle (checkbox) | Toggle defaults to checked (true) | ☐ |
| 13.10 | **Integer slider** | Send "full"; "Max retries" renders as SliderInt (0-10) | Slider shows with input field, default=3 | ☐ |
| 13.11 | **Float slider** | Send "full"; "Quality level" renders as Slider (0-1) | Slider shows with input field, default=0.8 | ☐ |
| 13.12 | **Multi-select array** | Send "full"; "Target platforms" shows Toggle checkboxes | Check some platforms, submit; result has array of selected values | ☐ |
| 13.13 | **Multiline string** | Send "full"; "Build notes" shows a taller multiline TextField | ☐ |
| 13.14 | **Email format validation** | Send "full"; enter invalid email in "Notification email", submit | Inline error "Invalid email address" | ☐ |
| 13.15 | **Optional fields omitted** | Send "full"; leave optional fields empty, submit | Returned JSON omits empty optional fields (not null) | ☐ |
| 13.16 | **ObjectField (asset)** | Send "unity"; drag a prefab to "Project Asset" | ObjectField shows asset; submit → asset path string | ☐ |
| 13.17 | **ObjectField (scene)** | Send "unity"; drag scene GO to "Scene Object" | Shows GO; submit → full hierarchy path `/Parent/Child` | ☐ |
| 13.18 | **Vector3Field** | Send "unity"; "Spawn Position" shows Vector3Field with default (0,1,0) | Edit to (1.5,-2,3.7); submit → `"1.5,-2,3.7"` | ☐ |
| 13.19 | **ColorField** | Send "unity"; "Tint Color" shows ColorField with orange | Change to green 50% alpha; submit → `"#00FF0080"` | ☐ |
| 13.20 | **URL mode opens browser** | Send "url" | Browser opens example.com URL; no form in Unity | ☐ |
| 13.21 | **URL mode returns accept** | Check agent stdout | Agent receives `accept` response for url-mode elicitation | ☐ |
| 13.22 | **AcpException -32042** | Use agent that returns -32042 | Conversation shows "⚠ Authorization required" inline | ☐ |
| 13.23 | **Generic AcpException** | Use agent that returns other error code | Conversation shows "⚠ Agent error (CODE): message" | ☐ |

## 14. Session Manager (multi-session)

> These tests verify that sessions are first-class, persisted across domain reloads, and that the agent process is reused when switching between them. No "Disconnect" UI is expected — sessions simply coexist.

| # | Feature | Test Steps | Expected Result | Pass |
|---|---------|------------|-----------------|------|
| 14.1 | **Single session (baseline)** | Fresh project; connect; send "hello" | Session dropdown appears in toolbar with "1. hello…" and a `＋` button next to it | ☐ |
| 14.2 | **Create new session** | Click `＋` | Conversation clears; dropdown shows two entries; agent process is NOT restarted (observe logs / PID unchanged) | ☐ |
| 14.3 | **Switch back** | Open dropdown, select the first session | Original messages re-appear (replayed via `session/load`); no process restart | ☐ |
| 14.4 | **Title derivation** | In a fresh session send "Analyze Assets/Scenes/Main.unity"; check dropdown | Title truncated to ~48 chars with ellipsis; session is the topmost entry | ☐ |
| 14.5 | **Persistence across domain reload** | Create 2 sessions → trigger domain reload (edit a script) | After reload, dropdown restores both entries; most-recently-active one loads automatically | ☐ |
| 14.6 | **Config change invalidates store** | Change agent `Command` in Settings | On next connect, dropdown is empty (old sessions dropped — they belong to the previous config) | ☐ |
| 14.7 | **Graceful shutdown** | Close the Agent window with an active session | Agent process exits within ~1.5s via stdin EOF; no orphaned processes in Task Manager | ☐ |
| 14.8 | **Process-died recovery** | With a session open, kill the agent process externally | Window auto-reconnects (creates new session); old sessions still listed for later `session/load` attempt | ☐ |
| 14.9 | **Switch while running** | Send a long prompt, then switch sessions mid-turn | Current turn is cancelled (`session/cancel`) before load fires; no crashes | ☐ |

## Test Summary

| Category | Total Tests | Passed | Failed | Notes |
|----------|-----------|--------|--------|-------|
| unity_scene | 28 | | | |
| unity_editor | 12 | | | |
| unity_asset | 10 | | | |
| unity_material | 5 | | | |
| unity_lighting | 12 | | | |
| unity_animation | 6 | | | |
| unity_spatial | 4 | | | |
| unity_ui | 3 | | | |
| unity_generate | 3 | | | |
| unity_particle | 25 | | | |
| unity_terrain | 19 | | | |
| Cross-Cutting | 26 | | | |
| Elicitation | 23 | | | |
| Session Manager | 9 | | | |
| **Total** | **185** | | | |

**Tested by:** _______________  
**Date:** _______________  
**Unity Version:** _______________  
**Agent:** _______________  
