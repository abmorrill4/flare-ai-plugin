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
        /// <summary>Citable guide id when the rule comes from a guide table (for example "company:terminology/3").</summary>
        public string RuleId { get; set; }
    }

    /// <summary>One citable rule of the layered guide.</summary>
    public sealed class GuideRule
    {
        public string Id { get; set; }
        public string Layer { get; set; }
        public string Section { get; set; }
        public string Text { get; set; }
        public string Link { get; set; }
    }

    /// <summary>A prose rule with Bad/Good examples, used to check that the AI applies it.</summary>
    public sealed class GuideExample
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Bad { get; set; }
        public string Good { get; set; }
    }

    public sealed class GuideIssue
    {
        public string Level { get; set; }
        public string Layer { get; set; }
        public string File { get; set; }
        public string Location { get; set; }
        public string Message { get; set; }
    }

    public sealed class GuideReport
    {
        public List<GuideIssue> Issues = new List<GuideIssue>();
        public int TableRules, TestedByExample, ProseRules, ProseRulesWithExamples, Errors, Warnings;
        public List<string> Layers = new List<string>();
    }

    /// <summary>The style guide and linter. Implemented over the docstyle MCP server; faked in tests.</summary>
    public interface IStyleService
    {
        /// <param name="headings">Indices of runs that are headings (heading rules apply only to them).</param>
        /// <param name="project">Path inside a Flare project to add its project layer (variables, glossary, StyleGuide), or null.</param>
        IList<LintHit> Lint(IList<string> texts, IList<int> headings, string project);
        /// <summary>The layered style guide (Google → configured layers → project) for the system prompt.</summary>
        string Guide(string project);
        /// <summary>Every citable rule of the layered guide (same call as <see cref="Guide"/>, cached with it).</summary>
        IList<GuideRule> Rules(string project);
        /// <summary>Tests the guide itself (table rows against their examples, contradictions, unreadable topics).</summary>
        GuideReport Validate(string project);
        /// <summary>Prose rules that carry Bad/Good examples.</summary>
        IList<GuideExample> Examples(string project);
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
        private readonly Dictionary<string, KeyValuePair<string, IList<GuideRule>>> _guides = new Dictionary<string, KeyValuePair<string, IList<GuideRule>>>(StringComparer.OrdinalIgnoreCase);

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
                    Layer = (string)f["layer"], Link = (string)f["link"], Variable = (string)f["variable"], RuleId = (string)f["rule_id"]
                });
            return result;
        }

        public string Guide(string project)
        {
            return Lookup(project).Key;
        }

        public IList<GuideRule> Rules(string project)
        {
            return Lookup(project).Value;
        }

        private KeyValuePair<string, IList<GuideRule>> Lookup(string project)
        {
            var key = project ?? "";
            lock (_gate)
            {
                KeyValuePair<string, IList<GuideRule>> cached;
                if (_guides.TryGetValue(key, out cached)) return cached;
            }
            var args = new JObject { ["query"] = "" };
            if (key.Length > 0) args["project"] = key;
            var json = Client().CallTool("style_lookup", args);
            var rules = new List<GuideRule>();
            foreach (var r in json["rules"] ?? new JArray())
                rules.Add(new GuideRule { Id = (string)r["id"], Layer = (string)r["layer"], Section = (string)r["section"], Text = (string)r["text"], Link = (string)r["link"] });
            var entry = new KeyValuePair<string, IList<GuideRule>>((string)json["guide"], rules);
            lock (_gate) _guides[key] = entry;
            return entry;
        }

        public GuideReport Validate(string project)
        {
            var args = new JObject();
            if (!string.IsNullOrEmpty(project)) args["project"] = project;
            var json = Client().CallTool("validate_guide", args);
            var report = new GuideReport();
            foreach (var i in json["issues"] ?? new JArray())
                report.Issues.Add(new GuideIssue { Level = (string)i["level"], Layer = (string)i["layer"], File = (string)i["file"], Location = (string)i["location"], Message = (string)i["message"] });
            var sum = json["summary"] ?? new JObject();
            report.TableRules = (int?)sum["table_rules"] ?? 0;
            report.TestedByExample = (int?)sum["tested_by_example"] ?? 0;
            report.ProseRules = (int?)sum["prose_rules"] ?? 0;
            report.ProseRulesWithExamples = (int?)sum["prose_rules_with_examples"] ?? 0;
            report.Errors = (int?)sum["errors"] ?? 0;
            report.Warnings = (int?)sum["warnings"] ?? 0;
            foreach (var l in sum["layers"] ?? new JArray()) report.Layers.Add((string)l);
            lock (_gate) _guides.Clear(); // the author probably just edited the guide
            return report;
        }

        public IList<GuideExample> Examples(string project)
        {
            var args = new JObject();
            if (!string.IsNullOrEmpty(project)) args["project"] = project;
            var list = new List<GuideExample>();
            foreach (var e in Client().CallTool("guide_examples", args)["examples"] ?? new JArray())
                list.Add(new GuideExample { Id = (string)e["id"], Text = (string)e["text"], Bad = (string)e["bad"], Good = (string)e["good"] });
            return list;
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
