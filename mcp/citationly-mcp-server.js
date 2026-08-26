#!/usr/bin/env node

const readline = require("node:readline");

const baseUrl = process.env.CITATIONLY_API_BASE_URL || "http://localhost:8088/api/public/v1";
const apiKey = process.env.CITATIONLY_API_KEY;

const tools = [
  ["get_visibility", "Get derived visibility summary with provenance.", { days: { type: "number" } }],
  ["get_competitors", "Get graph-first competitor list with provenance.", {}],
  ["get_citations", "Get extracted citation rows with provenance.", { days: { type: "number" } }],
  ["get_recommendations", "Get evidence-linked GEO recommendations.", {}],
  ["get_alerts", "Get persisted deduplicated alerts.", { limit: { type: "number" } }],
  ["get_brand_facts", "Get AI brand claims/fact checks with provenance.", { days: { type: "number" } }],
].map(([name, description, properties]) => ({
  name,
  description,
  inputSchema: { type: "object", properties, additionalProperties: false },
}));

function endpointFor(name, args = {}) {
  const query = new URLSearchParams();
  if (args.days) query.set("days", String(args.days));
  if (args.limit) query.set("limit", String(args.limit));
  const qs = query.toString();
  const path = {
    get_visibility: "visibility",
    get_competitors: "competitors",
    get_citations: "citations",
    get_recommendations: "recommendations",
    get_alerts: "alerts",
    get_brand_facts: "brand-facts",
  }[name];
  return `${baseUrl}/${path}${qs ? `?${qs}` : ""}`;
}

async function callTool(name, args) {
  if (!apiKey) {
    throw new Error("CITATIONLY_API_KEY is required.");
  }
  const res = await fetch(endpointFor(name, args), {
    headers: { "X-API-Key": apiKey, Accept: "application/json" },
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`Citationly API ${res.status}: ${text}`);
  return text;
}

function respond(id, result) {
  process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id, result }) + "\n");
}

function fail(id, error) {
  process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id, error: { code: -32000, message: error.message } }) + "\n");
}

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
rl.on("line", async (line) => {
  if (!line.trim()) return;
  const msg = JSON.parse(line);
  try {
    if (msg.method === "initialize") {
      respond(msg.id, {
        protocolVersion: "2024-11-05",
        capabilities: { tools: {} },
        serverInfo: { name: "citationly-mcp", version: "1.0.0" },
      });
    } else if (msg.method === "tools/list") {
      respond(msg.id, { tools });
    } else if (msg.method === "tools/call") {
      const content = await callTool(msg.params.name, msg.params.arguments || {});
      respond(msg.id, { content: [{ type: "text", text: content }] });
    } else {
      respond(msg.id, {});
    }
  } catch (error) {
    fail(msg.id, error);
  }
});
