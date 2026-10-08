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
        /// <summary>Set when Match is a typed copy of this project variable's value (fix = insert the variable).</summary>
        public string Variable { get; set; }
    }

    /// <summary>The style guide and linter. Implemented over the docstyle MCP server; faked in tests.</summary>
    public interface IStyleService
    {
        /// <param name="headings">Indices of runs that are headings (heading rules apply only to them).</param>
        /// <param name="project">Path inside a Flare project to add its project layer (variables, glossary, StyleGuide), or null.</param>
        IList<LintHit> Lint(IList<string> texts, IList<int> headings, string project);
        /// <summary>The layered style guide (Google → configured layers → project) for the system prompt.</summary>
        string Guide(string project);
    }

    /// <summary>
    /// docstyle over MCP stdio. Starts the server on first use and restarts it if it died.
    /// The layered guide is cached per project until the server restarts (Settings change or crash).
    /// </summary>
    public sealed class McpStyleService : IStyleService, IDisposable
    {
        private readonly string _command, _arguments, _workingDirectory;
        private readonly int _timeoutMs;
        private readonly object _gate = new object();
        private McpStdioClient _client;
        private readonly Dictionary<string, string> _guides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public McpStyleService(string command, string arguments, string workingDirectory, int timeoutMs)
        {
            _command = command; _arguments = arguments; _workingDirectory = workingDirectory; _timeoutMs = timeoutMs;
        }

        public IList<LintHit> Lint(IList<string> texts, IList<int> headings, string project)
        {
            var result = new List<LintHit>();
            if (texts.Count == 0) return result;
            var args = new JObject { ["texts"] = new JArray(texts), ["headings"] = new JArray(headings ?? new int[0]) };
            if (!string.IsNullOrEmpty(project)) args["project"] = project;
            var json = Client().CallTool("lint", args);
            foreach (var f in json["findings"] ?? new JArray())
                result.Add(new LintHit
                {
                    Index = (int)f["index"], Start = (int)f["start"], Length = (int)f["length"],
                    Match = (string)f["match"], Replacement = (string)f["replacement"],
                    Rule = (string)f["rule"], Severity = (string)f["severity"] ?? "suggestion", Message = (string)f["message"],
                    Layer = (string)f["layer"], Link = (string)f["link"], Variable = (string)f["variable"]
                });
            return result;
        }

        public string Guide(string project)
        {
            var key = project ?? "";
            lock (_gate)
            {
                string cached;
                if (_guides.TryGetValue(key, out cached)) return cached;
            }
            var args = new JObject { ["query"] = "" };
            if (key.Length > 0) args["project"] = key;
            var guide = (string)Client().CallTool("style_lookup", args)["guide"];
            lock (_gate) _guides[key] = guide;
            return guide;
        }

        private McpStdioClient Client()
        {
            lock (_gate)
            {
                if (_client != null && _client.IsAlive) return _client;
                if (_client != null) _client.Dispose();
                _client = new McpStdioClient(_command, _arguments, _workingDirectory, _timeoutMs);
                _guides.Clear(); // a restarted server may have new layer content
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
