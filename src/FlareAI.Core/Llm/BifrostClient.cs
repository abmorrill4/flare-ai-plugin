using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Llm
{
    /// <summary>
    /// Phase 4 transport: POST {gateway}/v1/chat/completions (OpenAI-compatible) with x-bf-vk.
    /// Non-streaming on purpose — Bifrost Agent Mode runs the MCP tool loop server-side only for
    /// non-streaming requests. The tool loop therefore never touches this code.
    /// </summary>
    public sealed class BifrostClient : ILlmClient
    {
        private readonly string _baseUrl;
        private readonly string _virtualKey;
        private readonly string _model;
        private readonly int _timeoutMs;
        private readonly string _mcpIncludeTools;

        public BifrostClient(string gatewayBaseUrl, string virtualKey, string model, int timeoutSeconds, string mcpIncludeTools)
        {
            if (string.IsNullOrEmpty(gatewayBaseUrl)) throw new LlmException("No gateway URL configured. Open AI > Settings.");
            if (string.IsNullOrEmpty(virtualKey)) throw new LlmException("No virtual key configured. Open AI > Settings.");
            _baseUrl = gatewayBaseUrl.TrimEnd('/');
            _virtualKey = virtualKey;
            _model = model;
            _timeoutMs = Math.Max(10, timeoutSeconds) * 1000;
            _mcpIncludeTools = mcpIncludeTools;
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch (Exception) { }
        }

        public static string BuildBody(string model, LlmRequest req)
        {
            var messages = new JArray();
            if (!string.IsNullOrEmpty(req.System)) messages.Add(new JObject { ["role"] = "system", ["content"] = req.System });
            messages.Add(new JObject { ["role"] = "user", ["content"] = req.User ?? "" });
            var body = new JObject
            {
                ["model"] = model,
                ["messages"] = messages,
                ["max_tokens"] = req.MaxTokens > 0 ? req.MaxTokens : 4096,
                ["stream"] = false
            };
            return body.ToString(Formatting.None);
        }

        public LlmResponse Complete(LlmRequest request)
        {
            var payload = Encoding.UTF8.GetBytes(BuildBody(_model, request));
            var http = (HttpWebRequest)WebRequest.Create(_baseUrl + "/v1/chat/completions");
            http.Method = "POST";
            http.ContentType = "application/json";
            http.Timeout = _timeoutMs;
            http.ReadWriteTimeout = _timeoutMs;
            http.UserAgent = "FlareAI/0.1 (MadCap Flare plugin)";
            http.Headers["x-bf-vk"] = _virtualKey;
            var tools = string.IsNullOrEmpty(request.ToolsHint) ? _mcpIncludeTools : request.ToolsHint;
            if (!string.IsNullOrEmpty(tools)) http.Headers["x-bf-mcp-include-tools"] = tools;
            http.ContentLength = payload.Length;

            try
            {
                using (var s = http.GetRequestStream()) s.Write(payload, 0, payload.Length);
                using (var resp = (HttpWebResponse)http.GetResponse())
                    return Parse(ReadAll(resp));
            }
            catch (WebException wex)
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp == null) throw new LlmException("Network error: " + wex.Message, 0, wex);
                var text = ReadAll(resp);
                var detail = text;
                try { detail = JObject.Parse(text)["error"]["message"].ToString(); } catch (Exception) { }
                throw new LlmException("HTTP " + (int)resp.StatusCode + ": " + detail, (int)resp.StatusCode, wex);
            }
        }

        public static LlmResponse Parse(string json)
        {
            var o = JObject.Parse(json);
            var choice = o["choices"][0];
            var msg = choice["message"];
            var usage = o["usage"] as JObject;
            return new LlmResponse
            {
                Text = (string)msg["content"] ?? "",
                Model = (string)o["model"],
                StopReason = (string)choice["finish_reason"],
                InputTokens = usage != null ? (int?)usage["prompt_tokens"] ?? 0 : 0,
                OutputTokens = usage != null ? (int?)usage["completion_tokens"] ?? 0 : 0
            };
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }
    }
}
