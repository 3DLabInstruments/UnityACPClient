#!/usr/bin/env node

// Thin MCP stdio proxy.
// Connects to Unity's built-in MCP server via WebSocket (preferred) or HTTP fallback.
// No tool logic here — Unity handles everything.

const http = require('http');
const { Buffer } = require('buffer');

const UNITY_PORT = process.env.UNITY_MCP_PORT || '57123';

// ── WebSocket connection ──

let ws = null;
let wsConnected = false;
const pendingRequests = new Map();

function connectWebSocket() {
  return new Promise((resolve) => {
    try {
      // Use built-in WebSocket if available (Node 22+), otherwise fall back to HTTP
      const WebSocket = globalThis.WebSocket || (() => { throw new Error('No WebSocket'); })();
      ws = new WebSocket(`ws://localhost:${UNITY_PORT}/`);

      ws.onopen = () => {
        wsConnected = true;
        console.error(`Connected to Unity via WebSocket (port ${UNITY_PORT})`);
        resolve(true);
      };

      ws.onmessage = (event) => {
        try {
          const response = JSON.parse(event.data);
          if (response.id !== undefined && pendingRequests.has(response.id)) {
            pendingRequests.get(response.id)(response);
            pendingRequests.delete(response.id);
          } else {
            // Unsolicited message (notification from server)
            process.stdout.write(JSON.stringify(response) + '\n');
          }
        } catch (e) {
          console.error(`WebSocket parse error: ${e.message}`);
        }
      };

      ws.onclose = () => {
        wsConnected = false;
        ws = null;
        console.error('WebSocket disconnected, falling back to HTTP');
      };

      ws.onerror = () => {
        wsConnected = false;
        ws = null;
        resolve(false);
      };
    } catch {
      resolve(false);
    }
  });
}

function sendViaWebSocket(message) {
  return new Promise((resolve, reject) => {
    if (!ws || !wsConnected) {
      reject(new Error('WebSocket not connected'));
      return;
    }

    if (message.id !== undefined) {
      pendingRequests.set(message.id, resolve);
      setTimeout(() => {
        if (pendingRequests.has(message.id)) {
          pendingRequests.delete(message.id);
          reject(new Error('WebSocket request timeout'));
        }
      }, 30000);
    }

    ws.send(JSON.stringify(message));

    // Notifications have no response
    if (message.id === undefined) {
      resolve(null);
    }
  });
}

// ── HTTP fallback ──

function sendViaHttp(message) {
  return new Promise((resolve, reject) => {
    const postData = JSON.stringify(message);

    const req = http.request({
      hostname: 'localhost',
      port: parseInt(UNITY_PORT),
      path: '/',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(postData)
      }
    }, (res) => {
      let data = '';
      res.on('data', (chunk) => { data += chunk; });
      res.on('end', () => {
        try {
          resolve(JSON.parse(data));
        } catch (e) {
          reject(new Error(`Invalid response: ${e.message}`));
        }
      });
    });

    req.on('error', (e) => reject(new Error(`HTTP error: ${e.message}`)));
    req.write(postData);
    req.end();
  });
}

// ── Unified send ──

async function forwardToUnity(message) {
  if (wsConnected) {
    try {
      return await sendViaWebSocket(message);
    } catch {
      // WebSocket failed, fall back to HTTP
    }
  }
  return await sendViaHttp(message);
}

// ── stdio transport ──

let stdinBuffer = '';

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  stdinBuffer += chunk;

  const lines = stdinBuffer.split('\n');
  stdinBuffer = lines.pop() || '';

  for (const line of lines) {
    if (line.trim()) processMessage(line.trim());
  }
});

process.stdin.on('end', () => process.exit(0));

async function processMessage(line) {
  try {
    const message = JSON.parse(line);

    if (message.method === 'notifications/initialized') {
      forwardToUnity(message).catch(() => {});
      return;
    }

    const response = await forwardToUnity(message);
    if (response) {
      process.stdout.write(JSON.stringify(response) + '\n');
    }
  } catch (error) {
    console.error(`Proxy error: ${error.message}`);
    try {
      const parsed = JSON.parse(line);
      if (parsed.id !== undefined) {
        process.stdout.write(JSON.stringify({
          jsonrpc: '2.0',
          id: parsed.id,
          error: { code: -32603, message: error.message }
        }) + '\n');
      }
    } catch { }
  }
}

// ── Startup ──

async function main() {
  const connected = await connectWebSocket();
  if (!connected) {
    console.error(`WebSocket unavailable, using HTTP (port ${UNITY_PORT})`);
  }
}

main().catch(() => {});

process.on('uncaughtException', (error) => {
  console.error('Uncaught exception:', error);
  process.exit(1);
});

process.on('unhandledRejection', (reason) => {
  console.error('Unhandled rejection:', reason);
  process.exit(1);
});

