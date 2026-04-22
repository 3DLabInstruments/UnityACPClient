# ACP Elicitation — Design & Implementation Plan

> Status: **Draft** · Owner: UnityAgentClient · Last updated: 2026-04-20
>
> Protocol status: ACP **RFD** (not yet merged into stable). SDK 0.1.5 has no
> native types. This document describes how we implement elicitation on top
> of the SDK's generic extension-method transport so Unity users benefit as
> soon as any agent ships support.

## 1. Why

Today when an agent is missing information it has to either:

1. **Guess** — and silently produce the wrong result (e.g. creates a ball at
   origin when the user said "at 1,2,3" but the tool param was dropped).
2. **Ask in prose** — forcing the user to type a free-form reply that the
   agent then has to parse again. Error-prone and wastes tokens.
3. **Bail out** — tool call fails, the conversation derails.

Elicitation fixes this by letting the agent request **structured input** with
a JSON Schema. The client renders a real form, validates inline, and sends
back typed data. This is especially valuable in Unity where answers are
often things like a `Vector3`, a scene object, a color, or one of a small
enum of assets.

## 2. Wire Protocol (summary)

Full spec: <https://agentclientprotocol.com/rfds/elicitation.md>

### Request — `elicitation/create`

```jsonc
{
  "jsonrpc": "2.0", "id": 43,
  "method": "elicitation/create",
  "params": {
    "sessionId": "sess_…",          // session-scoped
    "toolCallId": "tc_…",           // optional, ties to a tool call
    // OR:
    // "requestId": 12,             // request-scoped (pre-session)
    "mode": "form",                 // "form" | "url"
    "message": "…prompt text…",
    "requestedSchema": { /* restricted JSON Schema */ }
  }
}
```

Restricted JSON Schema (form mode only):

- Top-level **must** be `{ type: "object", properties: {…}, required: […] }`
- Property types: `string`, `number`, `integer`, `boolean`
- `enum` on primitives; labeled enums via `oneOf`/`anyOf` with `const` + `title`
- Multi-select: `type: "array", items: { type: "string", enum: [...] }`
- `string` formats: `email`, `uri`, `date`, `date-time`
- `default` values SHOULD pre-populate form fields
- **No** nested objects, no arbitrary arrays, no conditional validation

### Response

Three-action model (MCP-aligned):

```jsonc
{ "result": { "action": "accept", "content": { /* matches schema */ } } }
{ "result": { "action": "decline" } }
{ "result": { "action": "cancel"  } }   // user closed dialog / Esc
```

### URL mode (out-of-band, e.g. OAuth)

```jsonc
"params": {
  "mode": "url",
  "elicitationId": "github-oauth-001",
  "url": "https://agent.example.com/connect?elicitationId=…",
  "message": "Please authorize…"
}
```

Client returns `accept` after user consents to open URL. Interaction
completes in the browser. Agent MAY send `elicitation/complete`
notification with the same `elicitationId`. On blocking errors the agent
returns `-32042` (`URLElicitationRequiredError`) on the original request.

### Client capability advertisement

Per spec:

```jsonc
"clientCapabilities": {
  "elicitation": { "form": {}, "url": {} }
}
```

## 3. SDK Landing Point

`AgentClientProtocol` 0.3.0 added `ClientCapabilities.Elicitation` with typed
`Form` and `Url` sub-fields. The elicitation capability is now advertised at the
root level of the `initialize` request. The `ExtMethodAsync` handler for
`elicitation/create` remains unchanged — it still dispatches via the default
request handler in `ClientSideConnection`.

> **History:** Prior to SDK 0.3.0, elicitation was advertised via
> `ClientCapabilities.Meta` as a workaround (see git history).

## 4. Architecture

```
Agent ──elicitation/create──▶ ClientSideConnection
                                   │
                                   ▼ (default handler)
                          AgentWindow.ExtMethodAsync(method, params)
                                   │
                                   ├─ if method == "elicitation/create":
                                   │     parse params → ElicitationRequest
                                   │     enqueue to pendingElicitation (main thread)
                                   │     await TaskCompletionSource<ElicitationResponse>
                                   │
                                   ▼
                          Unity main thread (RefreshUI tick)
                                   │
                                   ▼
                          ElicitationPanel (VisualElement, inline above input)
                                   │
                                   ▼
                          User fills form, clicks [Submit]/[Decline]/[Cancel]
                                   │
                                   ▼
                          tcs.SetResult(response) → serialized JsonElement
                                   │
                                   ▼
                          Return to agent
```

### Threading

Same pattern as `RequestPermissionAsync`:

- `ExtMethodAsync` runs on the SDK reader thread.
- Set a `TaskCompletionSource<ElicitationResponse>` and stash the parsed
  request in a field picked up by `RefreshUI` on Unity's main thread.
- Main thread renders the panel; when user submits, sets the TCS result.
- Reader thread awaits the TCS, serializes to JSON, returns.

### File layout

```
Assets/UnityAgentClient/Editor/
  Elicitation/
    ElicitationTypes.cs        POCO: ElicitationRequest / Response / SchemaNode
    ElicitationDispatcher.cs   Static: parse + dispatch elicitation/create
    ElicitationPanel.cs        UI: JSON Schema → UI Toolkit form builder
  AgentWindow.cs               Hook into ExtMethodAsync, host the panel
  AgentWindow.uss              Styles for .elicitation-panel
```

## 5. JSON → UI Toolkit mapping

| Schema                                                | Unity control                       |
|-------------------------------------------------------|-------------------------------------|
| `type: string`                                        | `TextField`                         |
| `type: string, format: email` / `uri`                 | `TextField` + format validator      |
| `type: string, format: date` / `date-time`            | `TextField` (ISO hint)              |
| `type: string, enum: [...]`                           | `PopupField<string>`                |
| `type: string, oneOf: [{const,title}…]`               | `PopupField<string>` (display title)|
| `type: number` / `integer`                            | `FloatField` / `IntegerField`       |
| `type: number, minimum+maximum`                       | `Slider` / `SliderInt`              |
| `type: boolean`                                       | `Toggle`                            |
| `type: array, items.enum`                             | stacked `Toggle`s (multi-select)    |
| (future) `format: "unity-object"`                     | `ObjectField`                       |
| (future) `format: "unity-vector3"`                    | `Vector3Field`                      |
| (future) `format: "unity-color"`                      | `ColorField`                        |
| (future) `format: "unity-scene-object"`               | `ObjectField` filtered to scene     |

`default` pre-populates every field. `required` fields missing on submit
disable the Submit button and show an inline error.

## 6. Unity-specific extensions

The restricted schema is deliberately primitive. To keep Unity power
users happy we **additionally** recognize extended `format` values listed
above. This is transparent to agents that don't use them (they still see
a valid `string` field — our MCP tools can emit a generic schema when
they don't know about the extension, and agents that do know get richer
UI). These formats live in our namespace and MUST NOT be assumed by
other ACP clients.

## 7. Roll-out steps

### Step 1 — Minimal spike ✅

- `ElicitationTypes.cs` — POCOs with `JsonPropertyName`
- `ElicitationDispatcher.cs` — switch on method, parse params, bridge to
  main thread via `TaskCompletionSource`
- `ElicitationPanel.cs` — supports **string + enum** only (covers the
  "which refactoring strategy?" example from the RFD)
- Wire into `AgentWindow.ExtMethodAsync`
- Add `elicitation` advert to `ClientCapabilities.Meta`
- **Mock-agent script** in `docs/samples/mock-elicitation-agent.js` so
  we can exercise the happy path without waiting for a real agent

Acceptance: launch mock agent, window shows a form with the prompt
message and one dropdown, `Submit` returns the chosen value, agent
echoes it back.

### Step 2 — Full form builder ✅

Native UI Toolkit controls for every ACP primitive type:

| Schema                        | Control                        |
|-------------------------------|--------------------------------|
| `boolean`                     | `Toggle`                       |
| `integer`                     | `IntegerField`                 |
| `number`                      | `FloatField`                   |
| `integer/number` + min + max  | `SliderInt` / `Slider`         |
| `string`                      | `TextField`                    |
| `string, format: multiline`   | `TextField` (multiline)        |
| `string, format: email/uri`   | `TextField` + validation       |
| `enum` / `oneOf` / `anyOf`    | `PopupField`                   |
| `array` + `items.enum`        | multi-select `Toggle` group    |

- Inline per-field error labels (validate ALL fields, not just first)
- Optional fields with no value are **omitted** from the result (not null)
- Defaults are parsed as typed values (not string coercion)
- Slider bounds validated: only used when both min/max exist and min < max
- Form-level summary error ("Please fix N highlighted fields")

### Step 3 — Unity-native formats  ✅

Custom `format` extensions for Unity-specific data types:

| Format               | Control          | Serialised value              |
|----------------------|------------------|-------------------------------|
| `unity-object`       | `ObjectField`    | `AssetDatabase.GetAssetPath`  |
| `unity-scene-object` | `ObjectField`    | Full hierarchy path `/A/B/C`  |
| `unity-vector3`      | `Vector3Field`   | `"x,y,z"` (InvariantCulture) |
| `unity-color`        | `ColorField`     | `"#RRGGBBAA"`                 |

- ObjectField constrains `objectType = typeof(GameObject)`
- Scene-object mode: `allowSceneObjects = true`, rejects persistent assets
- Color: `ColorUtility.ToHtmlStringRGBA` returns without `#` — prepended manually
- Vector3: parsed/formatted with `CultureInfo.InvariantCulture`

### Step 4 — URL mode + `-32042`  ✅

- **URL mode**: when `mode == "url"`, open browser via `Application.OpenURL`
  and return `accept` immediately. Agent handles the callback.
- **`elicitation/complete` notification**: logged via `Logger.Log`
- **Capability**: advertise `{ form: {}, url: {} }` in `ClientCapabilities.Meta`
- **`-32042` error handling**: catch `AcpException` with code -32042 on
  `PromptAsync`, show inline "Authorization required" message instead of crash.
  Generic AcpException also caught with error code + message.

### Step 5 — Capability upstreaming ✅

`AgentClientProtocol` SDK 0.3.0 added `ClientCapabilities.Elicitation` typed field.
Moved from `Meta` side-channel to the typed property. No handler changes needed.

## 8. Security

- **Never auto-accept.** Even with "auto-approve tools" on, elicitation
  always requires explicit user action (it's asking for data, not
  permission).
- **Block URL mode by default** until the user confirms per-session
  (`Application.OpenURL` can exfiltrate via query strings).
- **Sensitive data**: we don't mask inputs by default. If a schema
  property declares `"format": "password"` or title/description contains
  the word "password/secret/token", render as password field. The RFD
  says "MUST NOT use form mode for sensitive data" — we honor that on
  the client by refusing and returning `decline` with a warning logged.

## 9. Testing

Each step gets a regression test in `docs/REGRESSION_TEST.md`:

- 13.1 Mock agent sends string request → form renders → accept round-trips
- 13.2 Decline button round-trips `action: "decline"`
- 13.3 Esc / close returns `action: "cancel"`
- 13.4 Required field empty disables Submit
- 13.5 Enum with `oneOf` titles renders titles, returns `const`
- 13.6 Multi-select returns array of selected values
- 13.7 URL mode opens browser, returns `accept`
- 13.8 `-32042` error retries original request after completion

## 10. Open questions

1. **Multiple concurrent elicitations?** RFD is silent. We assume
   serial — a second request while one is pending returns an error
   response (JSON-RPC code `-32603` Internal error) until queuing is
   proven necessary.
2. **Persistence across domain reload?** Unity editor domain reload
   will kill the pending TCS. We treat this as `cancel` — on reload we
   emit `cancel` responses for any elicitation that was in flight.
3. **Agent vendor-prefix variants?** If e.g. Zed ships
   `_zed.dev/elicitation/create` before RFD merges, we add explicit
   aliases to the dispatcher switch.
