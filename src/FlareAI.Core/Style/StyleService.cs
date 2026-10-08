using System;
using System.Collections.Generic;
using FlareAI.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Style
{
    /// <summary>One deterministic lint hit inside one text run.</summary>
    public sealed class LintHit
    {
        public int Index { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string Match { get; set; }
        /// <summary>Exact replacement for Match; null when the fix needs judgment.</summary>
        public string Replacement { get; set; }
        public string Rule { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        /// <summary>"google" (baseline) or "company" (layered on top).</summary>
        public string Layer { get; set; }
        /// <summary>Guide page explaining the rule, when known.</summary>
        public string Link { get; set; }
    }

    /// <summary>The style guide and linter. Implemented over the docstyle MCP server; faked in tests.</summary>
    public interface IStyleService
    {
        /// <param name="headings">Indices of runs that are headings (heading rules apply only to them).</param>
        IList<LintHit> Lint(IList<string> texts, IList<int> headings);
        /// <summary>The layered style guide (Google baseline + company overrides) for the system prompt.</summary>
        string Guide();
    }

    /// <summary>
    /// docstyle over MCP stdio. Starts the server on first use and restarts it if it died.
    /// The guide is fetched once per server start.
    /// </summary>
    public sealed class McpStyleService : IStyleService, IDisposable
    {
        private readonly string _command, _arguments, _workingDirectory;
        private readonly int _timeoutMs;
        private readonly object _gate = new object();
        private McpStdioClient _client;
        private string _guide;

        public McpStyleService(string command, string arguments, string workingDirectory, int timeoutMs)
        {
            _command = command; _arguments = arguments; _workingDirectory = workingDirectory; _timeoutMs = timeoutMs;
        }

        public IList<LintHit> Lint(IList<string> texts, IList<int> headings)
        {
            var result = new List<LintHit>();
            if (texts.Count == 0) return result;
            var json = Client().CallTool("lint", new JObject { ["texts"] = new JArray(texts), ["headings"] = new JArray(headings ?? new int[0]) });
            foreach (var f in json["findings"] ?? new JArray())
                result.Add(new LintHit
                {
                    Index = (int)f["index"], Start = (int)f["start"], Length = (int)f["length"],
                    Match = (string)f["match"], Replacement = (string)f["replacement"],
                    Rule = (string)f["rule"], Severity = (string)f["severity"] ?? "suggestion", Message = (string)f["message"],
                    Layer = (string)f["layer"], Link = (string)f["link"]
                });
            return result;
        }

        public string Guide()
        {
            Client();
            return _guide;
        }

        private McpStdioClient Client()
        {
            lock (_gate)
            {
                if (_client != null && _client.IsAlive) return _client;
                if (_client != null) _client.Dispose();
                _client = new McpStdioClient(_command, _arguments, _workingDirectory, _timeoutMs);
                _guide = (string)_client.CallTool("style_lookup", new JObject { ["query"] = "" })["guide"];
                Diagnostics.Log("docstyle started: " + _command + " " + _arguments);
                return _client;
            }
        }

        public void Dispose()
        {
            lock (_gate) { if (_client != null) _client.Dispose(); _client = null; }
        }
    }
}
