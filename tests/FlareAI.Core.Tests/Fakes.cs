using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using FlareAI.Core.Editing;
using FlareAI.Core.Llm;

namespace FlareAI.Core.Tests
{
    /// <summary>Loads a fixture topic and lets a test choose the "selection" by XPath.</summary>
    public sealed class FakeDocumentAdapter : IDocumentAdapter
    {
        public FakeDocumentAdapter(string fixtureName)
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", fixtureName), true) { }

        /// <summary>Any topic or snippet on disk (for multi-file tests).</summary>
        public FakeDocumentAdapter(string fullPath, bool _)
        {
            SourcePath = fullPath;
            Document = new XmlDocument { PreserveWhitespace = true };
            Document.Load(SourcePath);
            SelectionNodes = new List<XmlNode>();
            SelectionText = "";
            Replacements = new List<Tuple<string, IList<XmlNode>, IList<XmlNode>>>();
        }

        public string SourcePath { get; private set; }
        public bool IsEditableTopic { get { return BlockSelector.FindBody(Document) != null; } }
        public XmlDocument Document { get; private set; }
        public string SelectionText { get; set; }
        public IList<XmlNode> SelectionNodes { get; set; }
        public List<Tuple<string, IList<XmlNode>, IList<XmlNode>>> Replacements { get; private set; }

        public XmlNamespaceManager Ns
        {
            get
            {
                var ns = new XmlNamespaceManager(Document.NameTable);
                ns.AddNamespace("MadCap", BlockSelector.MadCapNs);
                return ns;
            }
        }

        public void SelectXPath(string xpath)
        {
            SelectionNodes = new List<XmlNode>();
            foreach (XmlNode n in Document.SelectNodes(xpath, Ns)) SelectionNodes.Add(n);
            SelectionText = SelectionNodes.Count > 0 ? SelectionNodes[0].InnerText : "";
        }

        public void ReplaceBlocks(string operationName, IList<XmlNode> oldNodes, IList<XmlNode> newNodes)
        {
            Replacements.Add(Tuple.Create(operationName, oldNodes, newNodes));
            var parent = oldNodes[0].ParentNode;
            var anchor = oldNodes[0];
            foreach (var n in newNodes) { parent.InsertBefore(n, anchor); }
            foreach (var o in oldNodes) parent.RemoveChild(o);
        }

        public List<string> TextOperations = new List<string>();
        public string LastSelected;

        public bool SelectText(XmlText run, int start, int length)
        {
            LastSelected = run.Value.Substring(start, length);
            return true;
        }

        public void Edit(string operationName, Action body)
        {
            TextOperations.Add(operationName);
            body();
        }

        public Dictionary<string, string> Variables = new Dictionary<string, string> { { "General.ProductName", "Acme Online Banking" } };

        public string VariableValue(string qualifiedName)
        {
            string v;
            return Variables.TryGetValue(qualifiedName, out v) ? v : null;
        }

        public bool ReplaceSpan(XmlText run, int start, int length, string replacement)
        {
            run.Value = run.Value.Substring(0, start) + replacement + run.Value.Substring(start + length);
            return true;
        }
    }

    /// <summary>A few real docstyle rules, in-process, so Core tests stay fast and offline.</summary>
    public sealed class FakeStyleService : FlareAI.Core.Style.IStyleService
    {
        public string GuideText = "- Refer to end users as \"members\".";

        public string LastProject;

        public IList<FlareAI.Core.Style.LintHit> Lint(IList<string> texts, IList<int> headings, string project)
        {
            LastProject = project;
            var hits = new List<FlareAI.Core.Style.LintHit>();
            for (var i = 0; i < texts.Count; i++)
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(texts[i], @"Acme Online Banking"))
                    hits.Add(new FlareAI.Core.Style.LintHit { Index = i, Start = m.Index, Length = m.Length, Match = m.Value, Rule = "Project.Variables", Severity = "warning", Layer = "project", Message = "Insert the variable.", Variable = "General.ProductName" });
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(texts[i], @"to Settings"))
                    hits.Add(new FlareAI.Core.Style.LintHit { Index = i, Start = m.Index, Length = m.Length, Match = m.Value, Replacement = "to the Settings", Rule = "Test.Across", Severity = "suggestion", Layer = "company", Message = "Spans markup." });
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(texts[i], @"\butilize\b|\bthe user\b"))
                    hits.Add(new FlareAI.Core.Style.LintHit
                    {
                        Index = i, Start = m.Index, Length = m.Length, Match = m.Value,
                        Replacement = m.Value == "utilize" ? "use" : null,
                        Rule = m.Value == "utilize" ? "plain-words.utilize" : "person.the-user",
                        Severity = "warning", Layer = "company", Message = m.Value == "utilize" ? "Use \"use\"." : "Address the reader as \"you\"."
                    });
            }
            return hits;
        }

        public string Guide(string project) { return GuideText; }
    }

    /// <summary>Returns canned text, or transforms the user prompt; records every request.</summary>
    public sealed class FakeLlmClient : ILlmClient
    {
        private readonly Func<LlmRequest, string> _answer;
        public List<LlmRequest> Requests { get; private set; }

        public FakeLlmClient(Func<LlmRequest, string> answer)
        {
            _answer = answer;
            Requests = new List<LlmRequest>();
            Diagnostics.Enabled = false; // keep test runs out of the real %APPDATA%\FlareAI\diag.log
        }

        public LlmResponse Complete(LlmRequest request)
        {
            Requests.Add(request);
            return new LlmResponse { Text = _answer(request), Model = "fake", StopReason = "end_turn" };
        }

        /// <summary>Echoes the fragment back unchanged inside &lt;revised&gt; — a rewrite must be a no-op.</summary>
        public static string EchoRevised(LlmRequest r)
        {
            var i = r.User.LastIndexOf("Fragment:", StringComparison.Ordinal);
            var frag = r.User.Substring(i + "Fragment:".Length).Trim();
            return "<revised>\n" + frag + "\n</revised>";
        }
    }
}
