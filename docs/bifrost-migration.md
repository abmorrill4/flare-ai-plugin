# Using a Bifrost gateway

House Style can send its AI requests through a [Bifrost](https://docs.getbifrost.ai) gateway instead of a direct
Anthropic key. Style checks always run inside the plugin; the gateway carries only the AI calls. Writers call the key a
**gateway key**; Bifrost calls it a virtual key.

## Writer setup
In first-run setup, or **House Style > Settings** on the **AI** tab:
- Choose **My company's AI service (gateway)**.
- **Base URL:** the gateway address, for example `https://bifrost.example.com`. House Style removes spaces, a trailing
  slash and a trailing `/v1`, and changes `http://` to `https://` (except for a gateway on the same computer).
- **Gateway key:** the virtual key. It's sent as `x-bf-vk` and stored with Windows DPAPI.
- Select **Test connection**.

The models are fixed for the gateway: `anthropic/claude-sonnet-5-5` (standard), `anthropic/claude-haiku-5-5` (fast)
and `anthropic/claude-opus-5-5` (deep).

## Gateway key checklist
Before you give a writer a gateway key:
- The writer pastes the **whole** key (`sk-bf-...`), with no spaces or line breaks.
- The virtual key allows all three models: `anthropic/claude-sonnet-5-5`, `anthropic/claude-haiku-5-5` and
  `anthropic/claude-opus-5-5`.
- Bifrost v1.5 and later treat empty allow-lists as deny all: set `allowed_models` on the virtual key's provider config,
  `key_ids: ["*"]`, and `models` on the Anthropic provider key (see the example below).
- Set an **expiry** and a **budget** on the key. For budget sizes, see `docs/testing/ai-cost.md`.
- Check it with **Test connection** in the plugin, or with the Bench `--gateway` command below.

## Gateway admin
Create a virtual key that allows those three models, with a budget. Writers' computers must reach the gateway address.
Nothing else is needed: no MCP server or tool configuration.

Bifrost v1.5 and later treat empty allow-lists as *deny all*, so list the models on the Anthropic provider key and give
the virtual key `key_ids: ["*"]`. Otherwise every request fails with `no keys found for provider: anthropic`. Verified
with `maximhq/bifrost:v2.3.2`:

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
