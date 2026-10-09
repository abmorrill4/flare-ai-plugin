using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Llm
{
    /// <summary>
    /// Phase 1 transport: POST {baseUrl}/v1/messages with x-api-key.
    /// Built on HttpWebRequest because the plugin must target .NET Framework 4.0.
    /// </summary>
    public sealed class AnthropicClient : ILlmClient
    {
        public const string ApiVersion = "2023-06-01";

        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _timeoutMs;

        public AnthropicClient(string baseUrl, string apiKey, string model, int timeoutSeconds)
        {
            if (string.IsNullOrEmpty(apiKey)) throw new LlmException("No API key configured. Open AI > Settings.");
            _baseUrl = (baseUrl ?? "https://api.anthropic.com").TrimEnd('/');
            _apiKey = apiKey;
            _model = model;
            _timeoutMs = Math.Max(10, timeoutSeconds) * 1000;
            EnsureTls12();
        }

        /// <summary>Exposed for tests: the exact JSON body we send.</summary>
        public static string BuildBody(string model, LlmRequest req)
        {
            var body = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = req.MaxTokens > 0 ? req.MaxTokens : 4096,
                ["messages"] = new JArray(new JObject { ["role"] = "user", ["content"] = req.User ?? "" })
            };
            // The system prompt (instructions + the whole layered style guide) is identical across calls: cache it,
            // so section-by-section reviews and repeat checks pay for it once.
            if (!string.IsNullOrEmpty(req.System))
                body["system"] = new JArray(new JObject { ["type"] = "text", ["text"] = req.System, ["cache_control"] = new JObject { ["type"] = "ephemeral" } });
            return body.ToString(Formatting.None);
        }

        public LlmResponse Complete(LlmRequest request)
        {
            var url = _baseUrl + "/v1/messages";
            var payload = Encoding.UTF8.GetBytes(BuildBody(_model, request));

            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "POST";
            http.ContentType = "application/json";
            http.Accept = "application/json";
            http.Timeout = _timeoutMs;
            http.ReadWriteTimeout = _timeoutMs;
            http.UserAgent = "FlareAI/0.1 (MadCap Flare plugin)";
            http.Headers["x-api-key"] = _apiKey;
            http.Headers["anthropic-version"] = ApiVersion;
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
                if (resp == null)
                    throw new LlmException("Network error: " + wex.Message, 0, wex);
                var text = ReadAll(resp);
                var detail = text;
                try { detail = JObject.Parse(text)["error"]["message"].ToString(); } catch (Exception) { }
                throw new LlmException("HTTP " + (int)resp.StatusCode + ": " + detail, (int)resp.StatusCode, wex);
            }
        }

        /// <summary>Exposed for tests.</summary>
        public static LlmResponse Parse(string json)
        {
            var o = JObject.Parse(json);
            var sb = new StringBuilder();
            var content = o["content"] as JArray;
            if (content != null)
                foreach (var block in content)
                    if ((string)block["type"] == "text") sb.Append((string)block["text"]);

            var usage = o["usage"] as JObject;
            return new LlmResponse
            {
                Text = sb.ToString(),
                Model = (string)o["model"],
                StopReason = (string)o["stop_reason"],
                InputTokens = usage != null ? (int?)usage["input_tokens"] ?? 0 : 0,
                OutputTokens = usage != null ? (int?)usage["output_tokens"] ?? 0 : 0
            };
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static void EnsureTls12()
        {
            // net40 has no SecurityProtocolType.Tls12 constant; the runtime (4.5+) honours the raw value.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch (Exception) { }
        }
    }
}
