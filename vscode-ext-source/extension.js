// PhoenixLAI for VS Code: puts the local PhoenixLAI studio into VS Code's own Chat panel (Ask / Edit / Agent, with
// VS Code's tools and approvals) and adds inline code completion. Zero dependencies: VS Code API + Node's http only.
// The one and only endpoint is the studio's loopback API, http://127.0.0.1:1234/v1. No settings.
"use strict";
const vscode = require("vscode");
const http = require("http");

const HOST = "127.0.0.1";
const PORT = 1234;
const VENDOR = "phoenixlai";
const MODEL_ID = "phoenixlai"; // not a file name: the studio answers with whichever chat model it has loaded

// ---------------------------------------------------------------- HTTP (loopback only)

function post(path, body, token, onEvent) {
  return new Promise((resolve, reject) => {
    const data = Buffer.from(JSON.stringify(body), "utf8");
    const req = http.request(
      { host: HOST, port: PORT, path, method: "POST", headers: { "Content-Type": "application/json", "Content-Length": data.length } },
      (res) => {
        res.setEncoding("utf8");
        if (res.statusCode !== 200) {
          let err = "";
          res.on("data", (c) => (err += c));
          res.on("end", () => {
            let msg = err;
            try { msg = JSON.parse(err).error.message; } catch (e) { /* raw text */ }
            reject(new Error(`PhoenixLAI: ${msg || "HTTP " + res.statusCode}`));
          });
          return;
        }
        let buf = "";
        res.on("data", (chunk) => {
          buf += chunk;
          let nl;
          while ((nl = buf.indexOf("\n")) >= 0) {
            const line = buf.slice(0, nl).trim();
            buf = buf.slice(nl + 1);
            if (!line.startsWith("data:")) continue;
            const payload = line.slice(5).trim();
            if (payload === "[DONE]") continue;
            try { onEvent(JSON.parse(payload)); } catch (e) { /* partial or foreign line */ }
          }
        });
        res.on("end", resolve);
        res.on("error", reject);
      }
    );
    req.on("error", (e) =>
      reject(new Error(e.code === "ECONNREFUSED" ? "PhoenixLAI is not running (start PhoenixLAI_App, API on 127.0.0.1:1234)." : "PhoenixLAI: " + e.message))
    );
    if (token) {
      if (token.isCancellationRequested) { req.destroy(); resolve(); return; }
      token.onCancellationRequested(() => { req.destroy(); resolve(); });
    }
    req.end(data);
  });
}

function getJson(path) {
  return new Promise((resolve) => {
    const req = http.get({ host: HOST, port: PORT, path, timeout: 1500 }, (res) => {
      let s = "";
      res.setEncoding("utf8");
      res.on("data", (c) => (s += c));
      res.on("end", () => { try { resolve(JSON.parse(s)); } catch (e) { resolve(null); } });
    });
    req.on("timeout", () => req.destroy());
    req.on("error", () => resolve(null));
  });
}

// ---------------------------------------------------------------- Chat panel model

let contextLength = 8192;

async function refreshContext() {
  const list = await getJson("/v1/models");
  const loaded = list && list.data && list.data.find((m) => m.state === "loaded" && m.max_context_length);
  if (loaded) contextLength = loaded.max_context_length;
}

function modelInfo() {
  const maxOutput = Math.min(4096, Math.max(512, Math.floor(contextLength / 4)));
  return {
    id: MODEL_ID,
    name: "PhoenixLAI",
    family: "phoenixlai",
    version: "1.0.0",
    detail: "Local · 127.0.0.1:1234",
    tooltip: "The model loaded in the PhoenixLAI studio",
    maxInputTokens: Math.max(2048, contextLength - maxOutput),
    maxOutputTokens: maxOutput,
    capabilities: { toolCalling: true, imageInput: false },
  };
}

function partText(p) {
  if (p instanceof vscode.LanguageModelTextPart) return p.value;
  if (vscode.LanguageModelDataPart && p instanceof vscode.LanguageModelDataPart) {
    const mime = p.mimeType || "";
    if (mime.startsWith("text/") || mime.includes("json")) return Buffer.from(p.data).toString("utf8");
    return "";
  }
  if (vscode.LanguageModelPromptTsxPart && p instanceof vscode.LanguageModelPromptTsxPart) {
    return typeof p.value === "string" ? p.value : JSON.stringify(p.value);
  }
  if (p && typeof p.value === "string") return p.value;
  return "";
}

// VS Code chat messages -> OpenAI messages (tool calls and results included).
function toOpenAi(messages) {
  const out = [];
  const R = vscode.LanguageModelChatMessageRole;
  for (const m of messages) {
    const role = m.role === R.User ? "user" : m.role === R.Assistant ? "assistant" : "system";
    let text = "";
    const calls = [];
    const results = [];
    for (const p of m.content) {
      if (p instanceof vscode.LanguageModelToolCallPart) {
        calls.push({ id: p.callId, type: "function", function: { name: p.name, arguments: JSON.stringify(p.input || {}) } });
      } else if (p instanceof vscode.LanguageModelToolResultPart) {
        results.push({ role: "tool", tool_call_id: p.callId, content: (p.content || []).map(partText).join("") });
      } else {
        text += partText(p);
      }
    }
    if (results.length) out.push(...results);
    if (text || calls.length) {
      const msg = { role, content: text };
      if (calls.length) msg.tool_calls = calls;
      out.push(msg);
    }
  }
  return out;
}

const chatProvider = {
  async provideLanguageModelChatInformation(options, token) {
    await refreshContext();
    return [modelInfo()];
  },

  async provideLanguageModelChatResponse(model, messages, options, progress, token) {
    const body = { model: MODEL_ID, messages: toOpenAi(messages), stream: true, max_tokens: model.maxOutputTokens };
    const mo = options.modelOptions || {};
    if (typeof mo.temperature === "number") body.temperature = mo.temperature;
    if (options.tools && options.tools.length) {
      body.tools = options.tools.map((t) => ({
        type: "function",
        function: { name: t.name, description: t.description || "", parameters: t.inputSchema || { type: "object", properties: {} } },
      }));
      if (options.toolMode === vscode.LanguageModelChatToolMode.Required) body.tool_choice = "required";
    }
    const calls = [];
    await post("/v1/chat/completions", body, token, (ev) => {
      const ch = ev.choices && ev.choices[0];
      const d = ch && ch.delta;
      if (!d) return;
      if (d.content) progress.report(new vscode.LanguageModelTextPart(d.content));
      if (d.tool_calls) {
        for (const tc of d.tool_calls) {
          const i = tc.index || 0;
          calls[i] = calls[i] || { id: "", name: "", args: "" };
          if (tc.id) calls[i].id = tc.id;
          if (tc.function && tc.function.name) calls[i].name += tc.function.name;
          if (tc.function && tc.function.arguments) calls[i].args += tc.function.arguments;
        }
      }
    });
    for (const c of calls) {
      if (!c || !c.name) continue;
      let input = {};
      try { input = c.args ? JSON.parse(c.args) : {}; } catch (e) { input = {}; }
      progress.report(new vscode.LanguageModelToolCallPart(c.id || "call_" + Date.now(), c.name, input));
    }
  },

  async provideTokenCount(model, text, token) {
    const s = typeof text === "string" ? text : (text.content || []).map(partText).join("");
    return Math.ceil(s.length / 4);
  },
};

// ---------------------------------------------------------------- Inline code completion (fill in the middle)

const PREFIX_CHARS = 6000;
const SUFFIX_CHARS = 2000;
let busyChat = 0;

const inlineProvider = {
  async provideInlineCompletionItems(document, position, context, token) {
    if (busyChat > 0) return null; // the studio runs one request at a time: never hold up a chat
    await new Promise((r) => setTimeout(r, 250)); // debounce: typing cancels this token
    if (token.isCancellationRequested) return null;

    const start = document.offsetAt(position);
    const all = document.getText();
    const prefix = all.slice(Math.max(0, start - PREFIX_CHARS), start);
    const suffix = all.slice(start, start + SUFFIX_CHARS);
    if (!prefix.trim()) return null;

    const lineRest = document.lineAt(position.line).text.slice(position.character);
    const body = {
      model: MODEL_ID,
      prompt: prefix,
      suffix,
      max_tokens: 96,
      temperature: 0.1,
      stream: true,
      stop: ["\n\n\n"],
    };
    let text = "";
    try {
      await post("/v1/completions", body, token, (ev) => {
        const ch = ev.choices && ev.choices[0];
        if (ch && typeof ch.text === "string") text += ch.text;
      });
    } catch (e) {
      return null;
    }
    if (token.isCancellationRequested) return null;
    text = trimCompletion(text, suffix, lineRest);
    const lineBefore = document.lineAt(position.line).text.slice(0, position.character);
    if (text && !lineBefore.trim() && lineBefore.length && text.startsWith(lineBefore)) text = text.slice(lineBefore.length);
    if (!text) return null;
    return [new vscode.InlineCompletionItem(text, new vscode.Range(position, position))];
  },
};

// Keep a completion from repeating what already follows the cursor, and stop it at the end of the current block.
function trimCompletion(text, suffix, lineRest) {
  if (!text) return "";
  text = text.replace(/^\s*```[\w-]*\n?/, "").replace(/\n?```\s*$/, "").replace(/<CURSOR>/g, "");
  const nextLine = suffix.split("\n").find((l) => l.trim().length > 0);
  if (nextLine) {
    const at = text.indexOf("\n" + nextLine.trim());
    if (at >= 0) text = text.slice(0, at);
    const lines = text.split("\n");
    const cut = lines.findIndex((l, i) => i > 0 && l.trim() === nextLine.trim());
    if (cut > 0) text = lines.slice(0, cut).join("\n");
  }
  if (lineRest.trim() && text.endsWith(lineRest)) text = text.slice(0, text.length - lineRest.length);
  return text.replace(/\s+$/, "");
}

// ---------------------------------------------------------------- Activation

function activate(context) {
  if (!vscode.lm || typeof vscode.lm.registerLanguageModelChatProvider !== "function") {
    vscode.window.showErrorMessage("PhoenixLAI needs VS Code 1.104 or newer (Language Model Chat Provider API).");
  } else {
    // Count chats in flight so inline completion steps aside while one runs.
    const wrapped = Object.assign({}, chatProvider, {
      async provideLanguageModelChatResponse(...args) {
        busyChat++;
        try { return await chatProvider.provideLanguageModelChatResponse(...args); } finally { busyChat--; }
      },
    });
    context.subscriptions.push(vscode.lm.registerLanguageModelChatProvider(VENDOR, wrapped));
  }
  context.subscriptions.push(vscode.languages.registerInlineCompletionItemProvider({ pattern: "**" }, inlineProvider));
}

function deactivate() {}

module.exports = { activate, deactivate };
