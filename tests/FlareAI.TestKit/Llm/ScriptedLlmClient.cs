using System;
using System.Collections.Generic;
using System.Net;
using FlareAI.Core.Llm;

namespace FlareAI.TestKit.Llm
{
    /// <summary>
    /// <see cref="ILlmClient"/> with crafted answers and failures, for error paths and for flows where the answer's exact
    /// wording does not matter. Queued replies are used first, in order; then the first matching rule; otherwise the call
    /// fails loudly. Every answer goes through the live contract check (<see cref="AnthropicClient.Check"/>), so a
    /// <c>max_tokens</c> or <c>refusal</c> stop reason, or text that isn't the requested JSON, fails exactly as live.
    /// </summary>
    public sealed class ScriptedLlmClient : ILlmClient
    {
        private readonly object _gate = new object();
        private readonly Queue<Func<LlmRequest, LlmResponse>> _queue = new Queue<Func<LlmRequest, LlmResponse>>();
        private readonly List<KeyValuePair<Func<LlmRequest, bool>, Func<LlmRequest, LlmResponse>>> _rules = new List<KeyValuePair<Func<LlmRequest, bool>, Func<LlmRequest, LlmResponse>>>();
        private readonly List<LlmRequest> _requests = new List<LlmRequest>();

        public IList<LlmRequest> Requests { get { lock (_gate) return _requests.ToArray(); } }
        public int Calls { get { lock (_gate) return _requests.Count; } }

        /// <summary>The next call gets this reply (queued replies come before rules).</summary>
        public ScriptedLlmClient Then(Func<LlmRequest, LlmResponse> reply)
        {
            lock (_gate) _queue.Enqueue(reply);
            return this;
        }

        public ScriptedLlmClient ThenText(string text) { return Then(Reply.Text(text)); }
        public ScriptedLlmClient ThenFail(LlmFailure failure, int statusCode = 0) { return Then(Reply.Failure(failure, statusCode)); }

        /// <summary>Every matching call gets this reply (first matching rule wins).</summary>
        public ScriptedLlmClient When(Func<LlmRequest, bool> match, Func<LlmRequest, LlmResponse> reply)
        {
            lock (_gate) _rules.Add(new KeyValuePair<Func<LlmRequest, bool>, Func<LlmRequest, LlmResponse>>(match, reply));
            return this;
        }

        public ScriptedLlmClient WhenTask(string task, string text) { return When(r => r.Task == task, Reply.Text(text)); }
        public ScriptedLlmClient Always(string text) { return When(r => true, Reply.Text(text)); }
        public ScriptedLlmClient AlwaysFail(LlmFailure failure, int statusCode = 0) { return When(r => true, Reply.Failure(failure, statusCode)); }

        /// <summary>A client whose every call fails this way.</summary>
        public static ScriptedLlmClient Failing(LlmFailure failure, int statusCode = 0) { return new ScriptedLlmClient().AlwaysFail(failure, statusCode); }

        /// <summary>A review that finds nothing (the review contract with no findings).</summary>
        public static ScriptedLlmClient NoFindings() { return new ScriptedLlmClient().WhenTask("review", "{\"findings\":[]}"); }

        public LlmResponse Complete(LlmRequest request)
        {
            Func<LlmRequest, LlmResponse> reply = null;
            lock (_gate)
            {
                _requests.Add(request);
                if (_queue.Count > 0) reply = _queue.Dequeue();
                else foreach (var rule in _rules) if (rule.Key(request)) { reply = rule.Value; break; }
            }
            if (reply == null) throw new InvalidOperationException("ScriptedLlmClient has no answer for a '" + request.Task + "' request.");
            return AnthropicClient.Check(reply(request), request);
        }

        /// <summary>Ready-made replies, including one for every <see cref="LlmFailure"/>.</summary>
        public static class Reply
        {
            public static Func<LlmRequest, LlmResponse> Text(string text, string stopReason = "end_turn")
            {
                return r => new LlmResponse { Text = text, Model = r.Model ?? "scripted", StopReason = stopReason, InputTokens = 100, OutputTokens = 20 };
            }

            /// <summary>
            /// The failure as the live clients produce it: HTTP and network errors as thrown by AnthropicClient/BifrostClient;
            /// Truncated, Refusal and Contract as responses the contract check turns into the typed exception.
            /// </summary>
            public static Func<LlmRequest, LlmResponse> Failure(LlmFailure failure, int statusCode = 0)
            {
                switch (failure)
                {
                    case LlmFailure.Http:
                        var code = statusCode == 0 ? 500 : statusCode;
                        return r => { throw new LlmException("HTTP " + code + ": " + HttpDetail(code, r), code, null, LlmFailure.Http); };
                    case LlmFailure.Auth:
                        return r => { throw LlmErrors.FromHttp(401, HttpDetail(401, r), false, null); };
                    case LlmFailure.ModelDenied:
                        return r => { throw LlmErrors.FromHttp(403, HttpDetail(403, r), false, null); };
                    case LlmFailure.Budget:
                        return r => { throw LlmErrors.FromHttp(statusCode == 0 ? 429 : statusCode, HttpDetail(429, r), false, null); };
                    case LlmFailure.Network:
                        return r => { throw new LlmException("Network error: Unable to connect to the remote server", 0, new WebException("Unable to connect to the remote server", WebExceptionStatus.ConnectFailure), LlmFailure.Network); };
                    case LlmFailure.Truncated:
                        return r => new LlmResponse { Text = "{\"findings\":[{\"run_id\":\"r1\",\"exc", Model = r.Model ?? "scripted", StopReason = "max_tokens", OutputTokens = r.MaxTokens > 0 ? r.MaxTokens : 16000 };
                    case LlmFailure.Refusal:
                        return r => new LlmResponse { Text = "", Model = r.Model ?? "scripted", StopReason = "refusal" };
                    case LlmFailure.Contract:
                        return r =>
                        {
                            if (r.Schema == null) throw new LlmException("The AI's answer didn't match the expected format.", 0, null, LlmFailure.Contract);
                            return new LlmResponse { Text = "Here are my suggestions: use plain words.", Model = r.Model ?? "scripted", StopReason = "end_turn" };
                        };
                    default:
                        throw new ArgumentOutOfRangeException("failure", failure, null);
                }
            }

            public static Func<LlmRequest, LlmResponse> Auth() { return Failure(LlmFailure.Auth, 401); }
            public static Func<LlmRequest, LlmResponse> ModelDenied() { return Failure(LlmFailure.ModelDenied, 403); }
            public static Func<LlmRequest, LlmResponse> RateLimited() { return Failure(LlmFailure.Budget, 429); }
            public static Func<LlmRequest, LlmResponse> Budget() { return Failure(LlmFailure.Budget, 402); }
            public static Func<LlmRequest, LlmResponse> ServerError() { return Failure(LlmFailure.Http, 500); }

            private static string HttpDetail(int code, LlmRequest r)
            {
                switch (code)
                {
                    case 401: return "invalid x-api-key";
                    case 403: return "model " + (r.Model ?? "claude-sonnet-5-5") + " is not allowed for this key";
                    case 429: return "Number of request tokens has exceeded your per-minute rate limit";
                    case 529: return "Overloaded";
                    default: return "Internal server error";
                }
            }
        }
    }
}
