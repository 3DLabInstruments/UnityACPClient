#!/usr/bin/env node
/*
 * Mock ACP agent for testing UnityAgentClient's elicitation support.
 *
 * Run the Unity window with this as the agent command:
 *   command: node
 *   args:    ["<repo>/docs/samples/mock-elicitation-agent.js"]
 *
 * On any user prompt, sends an `elicitation/create` request and echoes
 * the user's answer back as an assistant message.
 *
 * Speaks ACP over stdio using line-delimited JSON-RPC. Minimal — only
 * implements what's needed to exercise elicitation.
 */

'use strict';

const readline = require('readline');

const rl = readline.createInterface({ input: process.stdin });

let nextId = 1;
const pending = new Map(); // id -> { resolve, reject }

function send(msg) {
    const line = JSON.stringify(msg) + '\n';
    // When stdout is a pipe (not TTY), Node.js buffers writes. The
    // parent process (Unity's ReadLineAsync) may hang indefinitely
    // waiting for data that sits in the buffer. Use synchronous write
    // to fd 1 to bypass Node's buffering and deliver each JSON-RPC
    // message immediately.
    require('fs').writeSync(1, line);
}

function sendRequest(method, params) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        send({ jsonrpc: '2.0', id, method, params });
    });
}

function sendNotification(method, params) {
    send({ jsonrpc: '2.0', method, params });
}

function respond(id, result) {
    send({ jsonrpc: '2.0', id, result });
}

function respondError(id, code, message) {
    send({ jsonrpc: '2.0', id, error: { code, message } });
}

async function handle(msg) {
    // Response to something we sent?
    if (msg.id !== undefined && (msg.result !== undefined || msg.error !== undefined)
        && pending.has(msg.id)) {
        const { resolve, reject } = pending.get(msg.id);
        pending.delete(msg.id);
        if (msg.error) reject(msg.error);
        else resolve(msg.result);
        return;
    }

    const { id, method, params } = msg;

    switch (method) {
        case 'initialize':
            respond(id, {
                protocolVersion: 1,
                agentCapabilities: { loadSession: false, promptCapabilities: { image: false, audio: false, embeddedContext: false } },
                authMethods: [],
                agentInfo: { name: 'mock-elicitation-agent', version: '0.1.0' },
            });
            break;

        case 'session/new':
            respond(id, { sessionId: 'mock-session-1' });
            break;

        case 'session/prompt':
            // Fire-and-forget — async elicitation flow.
            runPromptFlow(id, params).catch(err => {
                console.error('[mock] prompt flow error', err);
                respondError(id, -32000, String(err));
            });
            break;

        case 'session/cancel':
            // No-op
            break;

        default:
            // Unknown method — respond with method-not-found if it had an id.
            if (id !== undefined) respondError(id, -32601, `method not found: ${method}`);
            break;
    }
}

async function runPromptFlow(promptId, params) {
    const sessionId = params.sessionId;
    // ACP sends `prompt` as an array of content blocks, not `messages`
    const prompt = params.prompt || [];
    const userText = prompt.map(b => b.text || '').join(' ').toLowerCase();

    sendNotification('session/update', {
        sessionId,
        update: {
            sessionUpdate: 'agent_message_chunk',
            content: { type: 'text', text: 'Let me ask you something first…\n' },
        },
    });

    // Choose schema based on user message keyword for testing different types
    let schema;
    let mode = 'form';
    if (userText.includes('unity') || userText.includes('native')) {
        // Step 3: Unity-native format extensions
        schema = {
            message: 'Configure Unity scene settings:',
            requestedSchema: {
                type: 'object',
                properties: {
                    objectRef: {
                        type: 'string',
                        title: 'Project Asset',
                        description: 'Select an asset from the project.',
                        format: 'unity-object',
                    },
                    sceneObject: {
                        type: 'string',
                        title: 'Scene Object',
                        description: 'Pick a GameObject from the scene.',
                        format: 'unity-scene-object',
                    },
                    position: {
                        type: 'string',
                        title: 'Spawn Position',
                        description: 'World-space position (x,y,z).',
                        format: 'unity-vector3',
                        default: '0,1,0',
                    },
                    tintColor: {
                        type: 'string',
                        title: 'Tint Color',
                        description: 'RGBA color for the object.',
                        format: 'unity-color',
                        default: '#FF6600FF',
                    },
                },
                required: ['position'],
            },
        };
    } else if (userText.includes('url') || userText.includes('browser')) {
        // Step 4: URL-mode elicitation (opens browser)
        mode = 'url';
        schema = {
            message: 'Please authorize in your browser.',
            url: 'https://example.com/authorize?session=' + sessionId,
            elicitationId: 'mock-auth-' + Date.now(),
        };
    } else if (userText.includes('full') || userText.includes('all')) {
        // Comprehensive schema exercising all Step 2 field types
        schema = {
            message: 'Configure your build settings:',
            requestedSchema: {
                type: 'object',
                properties: {
                    strategy: {
                        type: 'string',
                        title: 'Strategy',
                        oneOf: [
                            { const: 'conservative', title: 'Conservative' },
                            { const: 'balanced',     title: 'Balanced (Recommended)' },
                            { const: 'aggressive',   title: 'Aggressive' },
                        ],
                        default: 'balanced',
                    },
                    enableLogs: {
                        type: 'boolean',
                        title: 'Enable logging',
                        description: 'Turn on verbose build logs.',
                        default: true,
                    },
                    maxRetries: {
                        type: 'integer',
                        title: 'Max retries',
                        description: 'Number of retry attempts (0-10).',
                        minimum: 0,
                        maximum: 10,
                        default: 3,
                    },
                    quality: {
                        type: 'number',
                        title: 'Quality level',
                        description: 'Rendering quality (0.0 to 1.0).',
                        minimum: 0,
                        maximum: 1,
                        default: 0.8,
                    },
                    targetPlatforms: {
                        type: 'array',
                        title: 'Target platforms',
                        description: 'Select platforms to build for.',
                        items: {
                            type: 'string',
                            enum: ['Windows', 'macOS', 'Linux', 'Android', 'iOS', 'WebGL'],
                        },
                    },
                    description: {
                        type: 'string',
                        title: 'Build notes',
                        description: 'Optional multiline notes.',
                        format: 'multiline',
                    },
                    email: {
                        type: 'string',
                        title: 'Notification email',
                        description: 'Email to notify on completion.',
                        format: 'email',
                    },
                },
                required: ['strategy', 'maxRetries'],
            },
        };
    } else {
        // Default: simple refactoring schema (same as Step 1)
        schema = {
            message: 'How would you like me to approach this refactoring?',
            requestedSchema: {
                type: 'object',
                properties: {
                    strategy: {
                        type: 'string',
                        title: 'Refactoring Strategy',
                        oneOf: [
                            { const: 'conservative', title: 'Conservative' },
                            { const: 'balanced',     title: 'Balanced (Recommended)' },
                            { const: 'aggressive',   title: 'Aggressive' },
                        ],
                        default: 'balanced',
                    },
                    note: {
                        type: 'string',
                        title: 'Additional note',
                        description: 'Optional free-form guidance.',
                    },
                },
                required: ['strategy'],
            },
        };
    }

    let result;
    try {
        result = await sendRequest('elicitation/create', {
            sessionId,
            mode,
            ...schema,
        });
    } catch (err) {
        sendNotification('session/update', {
            sessionId,
            update: {
                sessionUpdate: 'agent_message_chunk',
                content: { type: 'text', text: `Elicitation errored: ${JSON.stringify(err)}` },
            },
        });
        respond(promptId, { stopReason: 'end_turn' });
        return;
    }

    const summary = result.action === 'accept'
        ? `You chose: ${JSON.stringify(result.content)}`
        : `You ${result.action}ed.`;

    sendNotification('session/update', {
        sessionId,
        update: {
            sessionUpdate: 'agent_message_chunk',
            content: { type: 'text', text: summary },
        },
    });

    respond(promptId, { stopReason: 'end_turn' });
}

rl.on('line', line => {
    line = line.trim();
    if (!line) return;
    let msg;
    try { msg = JSON.parse(line); }
    catch (e) { console.error('[mock] bad json:', line); return; }
    handle(msg).catch(err => console.error('[mock] handler error', err));
});

rl.on('close', () => process.exit(0));
