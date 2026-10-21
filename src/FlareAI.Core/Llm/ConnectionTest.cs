using System;
using FlareAI.Core.Settings;

namespace FlareAI.Core.Llm
{
    public enum ConnectionOutcome { Connected, Unreachable, KeyRejected, ModelNotAllowed, BudgetExhausted, Other }

    public sealed class ConnectionResult
    {
        public ConnectionOutcome Outcome { get; set; }
        public string Message { get; set; }
        public bool Ok { get { return Outcome == ConnectionOutcome.Connected; } }
    }

    /// <summary>One minimal request with the settings as typed (not yet saved), reported in terms an author can act on.</summary>
    public static class ConnectionTest
    {
        public static ConnectionResult Run(LlmProvider provider, string baseUrl, string key, string model, int timeoutSeconds)
        {
            return Run(provider, baseUrl, key, model, timeoutSeconds, null);
        }

        /// <param name="client">Test seam: a client to use instead of building one.</param>
        public static ConnectionResult Run(LlmProvider provider, string baseUrl, string key, string model, int timeoutSeconds, ILlmClient client)
        {
            var gateway = provider == LlmProvider.Bifrost;
            key = (key ?? "").Trim();
            if (key.Length == 0) return Fail(ConnectionOutcome.Other, "Add the " + (gateway ? "API / virtual key" : "API key") + " first, then test.");
            if (key.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) return Fail(ConnectionOutcome.KeyRejected, "The key contains spaces or line breaks. Paste it again in full.");
            try
            {
                if (client == null)
                {
                    string url, err;
                    if (!gateway && string.IsNullOrEmpty((baseUrl ?? "").Trim())) baseUrl = PluginSettings.DefaultAnthropicBaseUrl;
                    if (!LlmErrors.TryNormalizeBaseUrl(baseUrl, out url, out err)) return Fail(ConnectionOutcome.Other, err);
                    var timeout = Math.Min(Math.Max(10, timeoutSeconds), 30);
                    client = gateway ? (ILlmClient)new BifrostClient(url, key, model, timeout) : new AnthropicClient(url, key, model, timeout);
                }
                var resp = client.Complete(new LlmRequest { Task = "connection-test", User = "Reply with the single word OK.", MaxTokens = 16 });
                return new ConnectionResult { Outcome = ConnectionOutcome.Connected, Message = "Connected" + (string.IsNullOrEmpty(resp.Model) ? "" : ": " + resp.Model + " answered") + "." };
            }
            catch (LlmException ex)
            {
                if (ex.Failure == LlmFailure.Truncated) return new ConnectionResult { Outcome = ConnectionOutcome.Connected, Message = "Connected." };
                ConnectionOutcome o;
                switch (ex.Failure)
                {
                    case LlmFailure.Network: o = ConnectionOutcome.Unreachable; break;
                    case LlmFailure.Auth: o = ConnectionOutcome.KeyRejected; break;
                    case LlmFailure.ModelDenied: o = ConnectionOutcome.ModelNotAllowed; break;
                    case LlmFailure.Budget: o = ConnectionOutcome.BudgetExhausted; break;
                    default: o = ConnectionOutcome.Other; break;
                }
                return Fail(o, LlmErrors.Scrub(ex.Message, key));
            }
            catch (Exception ex)
            {
                return Fail(ConnectionOutcome.Other, "The test failed: " + LlmErrors.Scrub(ex.Message, key));
            }
        }

        private static ConnectionResult Fail(ConnectionOutcome o, string message) { return new ConnectionResult { Outcome = o, Message = message }; }
    }
}
