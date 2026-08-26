# Citationly MCP server

`citationly-mcp-server.js` exposes Citationly's public API v1 as MCP tools over stdio.

## Environment

- `CITATIONLY_API_BASE_URL` defaults to `http://localhost:8088/api/public/v1`
- `CITATIONLY_API_KEY` must be a server-generated Citationly API key from Settings -> API Keys

## Tools

- `get_visibility`
- `get_competitors`
- `get_citations`
- `get_recommendations`
- `get_alerts`
- `get_brand_facts`

Each tool calls the API-key authenticated public REST endpoint and returns the JSON payload,
including provenance fields from the API response.
