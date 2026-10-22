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

## Gateway key checklist
Before handing a virtual key to a writer:
- Base URL is `https://...` with **no trailing slash** and no path (the plugin adds `/v1/chat/completions`).
- The writer pastes the **whole** key (`sk-bf-...`), with no spaces or line breaks.
- The virtual key allows all three models: `anthropic/claude-sonnet-5-5`, `anthropic/claude-haiku-5-5` and
  `anthropic/claude-opus-5-5`.
- Bifrost v1.5+ treats empty allow-lists as deny all: set `allowed_models` on the virtual key's provider config,
  `key_ids: ["*"]`, and `models` on the Anthropic provider key (see the example below).
- Set an **expiry** and a **budget** on the key.
- Check it with **Settings > Test connection** in the plugin (or the Bench `--gateway` command below).

## Gateway admin
Create a virtual key that allows those three models, with a budget. Writers' machines must reach the gateway URL.
Nothing else is needed: no MCP server or tool configuration.

Bifrost v1.5+ treats empty allow-lists as *deny all*, so list the models on the Anthropic provider key and give the
virtual key `key_ids: ["*"]`; otherwise every request fails with `no keys found for provider: anthropic`. Verified with
`maximhq/bifrost:v2.3.2`:

```json
{
  "client": { "enforce_auth_on_inference": true },
  "providers": { "anthropic": { "keys": [
    { "name": "anthropic-key-1", "value": "env.ANTHROPIC_API_KEY", "weight": 1.0,
      "models": ["claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5-5"] } ] } },
  "governance": { "virtual_keys": [
    { "id": "vk-flare", "name": "flare", "value": "sk-bf-<secret>", "is_active": true,
      "provider_configs": [ { "provider": "anthropic", "weight": 1.0, "key_ids": ["*"],
        "allowed_models": ["claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5-5"] } ] } ] }
}
```

To check a gateway without Flare: `set BIFROST_VK=<key>` then
`tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --gateway <url> --sample 4 --rewrites 2` (runs real reviews and
rewrites through it).

## How it talks to the gateway
`BifrostClient`: `POST {gateway}/v1/chat/completions`, OpenAI-compatible body, `stream: false`, structured outputs via
`response_format: json_schema`, images as `image_url` data URIs. The prompts and response contract are the same as
Anthropic direct, and every edit still passes `XhtmlValidator` before it touches the topic.
