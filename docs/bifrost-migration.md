# Phase 4 — Bifrost migration notes

Verified against https://docs.getbifrost.ai (MCP overview, tool-execution, agent-mode).

## Transport change only
`ILlmClient` gets a second implementation, `BifrostClient`:

- `POST {gatewayBaseUrl}/v1/chat/completions` (OpenAI-compatible body)
- Auth header: `x-bf-vk: <virtual key>` (also accepts `Authorization: Bearer` / `X-Api-Key`)
- Non-streaming (`stream: false`) — Agent Mode does not support streaming
- Optional `x-bf-mcp-include-tools: <pattern>` to opt in to specific MCP tools per request
  (required if the gateway has *Disable Auto Tool Injection* on — recommended)
- Model id uses Bifrost's `provider/model` form, e.g. `anthropic/claude-sonnet-5-5`

The prompt/response contract (`<revised>…</revised>`, findings JSON) is unchanged.

## Gateway-side setup (one time, owned by the gateway admin)
1. Register the style-guide + linter MCP server as a **STDIO client** on the gateway host:
   ```json
   {
     "name": "docstyle",
     "connection_type": "stdio",
     "stdio_config": { "command": "uv", "args": ["run", "--quiet", "--script", "/opt/docstyle/server.py"] },
     "tools_to_execute": ["*"],
     "tools_to_auto_execute": ["style_lookup", "lint", "glossary_lookup"]
   }
   ```
   `tools_to_auto_execute` makes Agent Mode run the tool loop server-side; the plugin just
   gets the final answer. All three tools are read-only, so auto-execute is safe.
2. Create a **virtual key** for the tester scoped to: the doc model(s) and
   `mcp_configs: [{ mcp_client_name: "docstyle", tools_to_execute: ["*"] }]`, with a budget.
3. Tool names arrive at the model prefixed with the client name (`docstyle_style_lookup`).

   Deploy the whole `mcp/docstyle` folder (server, guides, Vale styles, `.vale.ini`, glossary) to
   `/opt/docstyle`; `uv` installs the MCP SDK and Vale on first run. `--selftest` verifies it.

## What the plugin stops doing
- Spawning docstyle locally (`McpStyleService`) — the gateway runs it; the plugin's system prompt gets
  the layered guide from `docstyle_style_lookup` instead.
- Local lint calls — `docstyle_lint` via the gateway. Keep the client-side lint pass for the findings
  window either way: it needs exact spans to offer one-click fixes.

## What stays
- `XhtmlValidator` gate before any DOM write. The gateway does not know Flare XHTML.
- DPAPI-protected secret storage (now holding the virtual key).

## Fallback if agent mode is unavailable
Run the loop client-side: chat → inspect `tool_calls` → `POST /v1/mcp/tool/execute` with the
tool-call object → append `{role:"tool", tool_call_id, content}` → chat again. Still no
sidecar needed; it is ~60 lines in `BifrostClient`.
