using System;

namespace FlareAI.Core.Llm
{
    /// <summary>A single-turn completion request. Provider-agnostic so Bifrost can replace Anthropic-direct.</summary>
    public sealed class LlmRequest
    {
        public string System { get; set; }
        public string User { get; set; }
        public int MaxTokens { get; set; }
        /// <summary>Optional hint for the provider to attach MCP tools (Bifrost: x-bf-mcp-include-tools).</summary>
        public string ToolsHint { get; set; }
    }

    public sealed class LlmResponse
    {
        public string Text { get; set; }
        public string Model { get; set; }
        public string StopReason { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    public sealed class LlmException : Exception
    {
        public int StatusCode { get; private set; }
        public LlmException(string message, int statusCode = 0, Exception inner = null) : base(message, inner)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>Synchronous by design (net40, no async). Callers run it on a background thread.</summary>
    public interface ILlmClient
    {
        LlmResponse Complete(LlmRequest request);
    }
}
