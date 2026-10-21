# Using a Bifrost gateway

House Style can send its AI requests through a [Bifrost](https://docs.getbifrost.ai) gateway with a virtual key
instead of a direct Anthropic key. The style checker, linter and style guide always run inside the plugin; the gateway
only carries the AI calls.

## Writer setup
**House Style > Settings**:
- **Provider:** Bifrost gateway
- **Base URL:** the gateway root, for example `https://bifrost.example.com` (the plugin adds `/v1/chat/completions`)
- **API / virtual key:** the virtual key (sent as `x-bf-vk`, stored with Windows DPAPI)

The models are fixed for the gateway: `anthropic/claude-sonnet-5-5` (standard), `anthropic/claude-haiku-5-5` (fast)
and `anthropic/claude-opus-5-5` (deep).

## Gateway admin
Create a virtual key that allows those three models, with a budget. Writers' machines must reach the gateway URL.
Nothing else is needed: no MCP server or tool configuration.

## How it talks to the gateway
`BifrostClient`: `POST {gateway}/v1/chat/completions`, OpenAI-compatible body, `stream: false`, structured outputs via
`response_format: json_schema`, images as `image_url` data URIs. The prompts and response contract are the same as
Anthropic direct, and every edit still passes `XhtmlValidator` before it touches the topic.
