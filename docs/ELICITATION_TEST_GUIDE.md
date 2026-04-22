# Elicitation — Manual Test Guide

> Scope: verify the elicitation dispatch & form renderer end-to-end (Steps 1–4),
> using the bundled mock agent. No real agent (Gemini / Copilot / etc.) is
> required — they don't implement elicitation yet.

## 0. Prerequisites

- Unity 2021.3+ project with this plugin imported
- **Node.js 18+** on `PATH` (check: `node --version`)
- Repo checked out (the mock agent script lives in `docs/samples/`)

## 1. Point the window at the mock agent

1. Open **Window ▸ Unity Agent Client ▸ AI Agent**.
2. Click the ⚙ icon (or open **Edit ▸ Project Settings ▸ Unity Agent Client**).
3. Configure the agent:
   - **Command:** `node`
   - **Args:** `["<ABSOLUTE_PATH_TO_REPO>/docs/samples/mock-elicitation-agent.js"]`
     - Windows example: `["E:/repo/UnityAgentClient/docs/samples/mock-elicitation-agent.js"]`
     - Use forward slashes, or double-escape backslashes
   - Leave **Working Directory** and **Env** empty
4. Close the settings window. The AI Agent window should show a green/yellow
   connection dot and "Connected to agent 'mock-elicitation-agent'" in the log
   (enable verbose logging in settings if you want to see it).

> **Troubleshooting:** if the dot stays red, open the Unity Console — the
> mock agent's stderr is surfaced there. Most common cause: wrong path, or
> `node` not found (restart Unity after a fresh install so `PATH` refreshes).

## 2. The happy path — Test 13.1, 13.2, 13.6, 13.7

1. Type anything into the input (e.g. "refactor the player controller") and
   press **Enter** or click **Send**.
2. You should see, in order:
   - A streaming agent message: *"Let me ask you something first…"*
   - **An elicitation panel** appears below the messages, with:
     - Header: **Agent needs input**
     - Prompt: *"How would you like me to approach this refactoring?"*
     - A required dropdown labeled **Refactoring Strategy \***
       - Items visible: `Conservative`, `Balanced (Recommended)`, `Aggressive`
       - Pre-selected: `Balanced (Recommended)` ← verifies **13.7 default**
       - Dropdown shows the **title**, not the `const` value ← verifies **13.6**
     - A non-required TextField **Additional note** with a description line
     - Buttons: **Submit**, **Decline**, **Cancel**
3. Pick `Aggressive`, type `do it` in the note, click **Submit**.
4. **Expected:** the panel disappears, and the agent streams back:

   ```
   You chose: {"strategy":"aggressive","note":"do it"}
   ```

   This verifies **13.2 accept round-trips**.

## 3. Decline — Test 13.3

1. Send any new prompt. Panel appears again.
2. Click **Decline**.
3. **Expected:** panel closes, agent replies `You declined.`

## 4. Cancel — Test 13.4

1. Send a prompt. Panel appears.
2. Click **Cancel**.
3. **Expected:** panel closes, agent replies `You canceled.`

> The `Esc` binding only fires when the panel itself has focus. If Esc
> doesn't work, click inside the panel first.

## 5. Required-field validation — Test 13.5

1. Send a prompt. Panel appears.
2. The **Refactoring Strategy** field is required (starred).
3. Because the dropdown always has a selection (the default), this specific
   schema can't easily trigger the error. To verify the validation path:
   - Temporarily edit `docs/samples/mock-elicitation-agent.js`:
     - Add a required **string** field with no default:

       ```js
       properties: {
         ticketId: { type: 'string', title: 'Ticket ID' },
         // …existing strategy/note…
       },
       required: ['ticketId', 'strategy'],
       ```

     - Reconnect the agent (close & reopen the window, or toggle command).
4. Send a prompt. Leave **Ticket ID** empty and click **Submit**.
5. **Expected:** red inline error *"'ticketId' is required"* appears below
   the fields; panel stays open; no response sent to agent.
6. Fill the field, click Submit → normal accept flow.

## 6. Domain Reload safety — Test 13.8

1. Send a prompt. Elicitation panel appears — **do not submit**.
2. In another Unity window, edit any script (even a whitespace change in
   `Assets/`) and save. This triggers a Domain Reload.
3. **Expected:**
   - The AI Agent window reloads cleanly (no hang, no exception in the
     Console that references `pendingElicitation*`).
   - The panel is gone. The mock agent has exited (`stopReason: end_turn`
     may or may not be delivered; either is fine).
   - You can send another prompt after reconnecting.

> What we're guarding against: an in-flight `TaskCompletionSource` holding
> the reader thread blocked across domain reload. The reset path in
> `ResetWindowState()` calls `TrySetResult(Cancel())` to unblock.

## 7. Standalone protocol smoke test (no Unity)

If you just want to prove the mock agent itself talks the protocol, from
the repo root:

```powershell
@"
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"fs":{"readTextFile":true,"writeTextFile":true},"_meta":{"elicitation":{"form":{}}}},"clientInfo":{"name":"test","version":"0.0.1"}}}
{"jsonrpc":"2.0","id":2,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}
{"jsonrpc":"2.0","id":3,"method":"session/prompt","params":{"sessionId":"mock-session-1","prompt":[{"type":"text","text":"hello"}]}}
{"jsonrpc":"2.0","id":1,"result":{"action":"accept","content":{"strategy":"aggressive","note":"do it"}}}
"@ | node docs/samples/mock-elicitation-agent.js
```

Expected stdout (one JSON per line):

1. `initialize` response with `agentInfo.name = mock-elicitation-agent`
2. `session/new` response with `sessionId = mock-session-1`
3. A `session/update` with text `Let me ask you something first…`
4. An outgoing `elicitation/create` request (id:1) with the refactoring schema
5. A `session/update` text `You chose: {"strategy":"aggressive","note":"do it"}`
6. A `session/prompt` response with `stopReason: end_turn`

## 8. Step 3 — Unity-native formats

> Keyword: type **"unity"** or **"native"** in the chat to trigger the
> Unity-native schema.

| # | Action | Expected |
|---|--------|----------|
| 8.1 | Type `unity` and send | Form appears with 4 fields: Project Asset (ObjectField), Scene Object (ObjectField), Spawn Position (Vector3Field pre-filled 0,1,0), Tint Color (ColorField pre-filled orange) |
| 8.2 | Leave all optional fields empty, Submit | Only `position` sent (it's required). Value format: `"0,1,0"` |
| 8.3 | Drag a prefab from Project to "Project Asset" | ObjectField shows the asset. Submit → value is the asset path (e.g. `"Assets/Prefabs/Cube.prefab"`) |
| 8.4 | Open a scene, drag a scene GameObject to "Scene Object" | ObjectField shows it. Submit → value is hierarchy path (e.g. `"/Environment/Tree"`) |
| 8.5 | Change Spawn Position to (1.5, 2.3, -4) | Submit → `"1.5,2.3,-4"` (invariant culture, no spaces) |
| 8.6 | Click Tint Color → change to bright green with alpha 0.5 | Submit → `"#00FF0080"` (8-digit hex, uppercase) |
| 8.7 | Clear position field (all zeros) and Submit | Still sends `"0,0,0"` (required field always included) |

## 9. Step 4 — URL mode + error handling

### 9.1 URL mode

> Keyword: type **"url"** or **"browser"** in the chat.

| # | Action | Expected |
|---|--------|----------|
| 9.1 | Type `url` and send | Default browser opens `https://example.com/authorize?session=…`. No form shown in Unity. Mock agent receives `accept` response |
| 9.2 | Check Unity Console | Log: `[UnityAgentClient] [Elicitation] Opening URL: https://example.com/…` |

### 9.2 `-32042` authorization error

> This requires a custom mock or real agent that returns `-32042`.
> The built-in mock does not produce this error. Manual verification:
> if an agent returns JSON-RPC error `-32042`, the conversation should
> show "⚠ Authorization required" instead of crashing.

## 10. Full form builder — Test 13.9–13.15

Step 2 added native controls. To test, type **"full"** or **"all"** in the prompt:

1. **Boolean toggle (13.9):** "Enable logging" shows as a Toggle (checkbox), defaults to checked.
2. **Integer slider (13.10):** "Max retries" renders as SliderInt (0–10) with input field, default=3.
3. **Float slider (13.11):** "Quality level" renders as Slider (0–1) with input field, default=0.8.
4. **Multi-select (13.12):** "Target platforms" shows Toggle checkboxes for each platform. Check some, submit → result has an array of selected values.
5. **Multiline (13.13):** "Build notes" shows a taller multiline TextField.
6. **Email validation (13.14):** Enter an invalid email in "Notification email", submit → inline error "Invalid email address".
7. **Optional omission (13.15):** Leave optional fields empty, submit → returned JSON omits those fields (not `null`).

## 11. What *isn't* covered yet (intentionally)

Don't file bugs for these — they land in later steps:

- Password-format masking
- Multiple concurrent elicitations (current behavior: second one declines)

## 12. Cheatsheet — where to look in code

| Symptom                             | File                                         |
|-------------------------------------|----------------------------------------------|
| Panel never appears                 | `AgentWindow.cs` → `ExtMethodAsync`, `UpdateElicitationUI` |
| Wrong control for a schema type     | `Elicitation/ElicitationPanel.cs` → `BuildField` |
| Wrong JSON shape sent back          | `Elicitation/ElicitationPanel.cs` → `TryCollect` |
| Agent never sees capability         | `AgentWindow.cs` → `initialize` call's `ClientCapabilities.Meta` |
| Hang on domain reload               | `AgentWindow.cs` → `ResetWindowState` |

## 13. Reporting results

When you've run through sections 2–10, mark **13.1–13.23** in
`docs/REGRESSION_TEST.md` ✅/❌ and note your Unity version + OS at the
bottom of that file.
