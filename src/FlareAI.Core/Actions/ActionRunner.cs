using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using FlareAI.Core.Editing;
using FlareAI.Core.Llm;
using FlareAI.Core.Style;

namespace FlareAI.Core.Actions
{
    /// <summary>Outcome of a Rewrite/Ask: the proposed nodes (already imported into the document) plus validation.</summary>
    public sealed class RewriteProposal
    {
        public IList<XmlNode> OriginalBlocks { get; set; }
        public IList<XmlNode> ProposedBlocks { get; set; }
        public string ProposedXml { get; set; }
        public ValidationResult Validation { get; set; }
        public LlmResponse Raw { get; set; }
        /// <summary>Lint findings still present in the proposal after the self-check round.</summary>
        public List<StyleFinding> RemainingLint { get; set; }
        /// <summary>Non-fatal problems worth showing (for example: linter unavailable).</summary>
        public List<string> Notes { get; set; }
        /// <summary>The blocks as the model saw them (pending changes resolved to what the author reads).</summary>
        public IList<XmlNode> View { get; set; }
        /// <summary>
        /// Word-level replacements equivalent to the proposal, when it kept the structure; null means the
        /// proposal restructures and applies as a block replacement.
        /// </summary>
        public List<StyleFinding> WordPlan { get; set; }
    }

    /// <summary>
    /// Orchestrates one action end to end: pick blocks → lint (docstyle) → model → parse → validate.
    /// Applying the result to the document is a separate, explicit step so the UI can show a preview.
    /// All methods are synchronous; the caller runs them off the UI thread.
    /// </summary>
    public sealed class ActionRunner
    {
        private readonly ILlmClient _llm;
        private readonly PromptBuilder _prompts;
        private readonly int _maxTokens;
        private readonly IStyleService _style;

        private readonly string _project;

        /// <param name="project">Path of the topic (any path inside its Flare project) for the project style layer; null for none.</param>
        public ActionRunner(ILlmClient llm, PromptBuilder prompts, int maxTokens, IStyleService style = null, string project = null)
        {
            _llm = llm;
            _prompts = prompts;
            _maxTokens = maxTokens;
            _style = style;
            _project = project;
        }

        public IList<XmlNode> PickBlocks(IDocumentAdapter doc)
        {
            return PickBlocks(doc, doc == null ? null : doc.SelectionNodes);
        }

        /// <summary>Flare always has a caret; a caret with no selected text means "check the whole topic".</summary>
        public IList<XmlNode> PickCheckBlocks(IDocumentAdapter doc)
        {
            return string.IsNullOrEmpty(doc == null ? null : doc.SelectionText) ? PickBlocks(doc, new List<XmlNode>()) : PickBlocks(doc);
        }

        /// <summary>Every block of the topic, regardless of selection.</summary>
        public IList<XmlNode> PickAllBlocks(IDocumentAdapter doc)
        {
            return PickBlocks(doc, new List<XmlNode>());
        }

        private static IList<XmlNode> PickBlocks(IDocumentAdapter doc, IList<XmlNode> selection)
        {
            if (doc == null || !doc.IsEditableTopic)
                throw new InvalidOperationException("Open a topic in the XML Editor first.");
            var blocks = BlockSelector.SelectBlocks(doc.Document, selection);
            if (blocks.Count == 0) throw new InvalidOperationException("Nothing to work on — the topic body is empty.");
            return blocks;
        }

        // ---- check style -----------------------------------------------------------------------

        /// <summary>Deterministic findings from docstyle, in document order. Empty when no style service is configured.</summary>
        public List<StyleFinding> Lint(IList<XmlNode> blocks)
        {
            var findings = new List<StyleFinding>();
            if (_style == null) return findings;
            // Lint whole lines (inline markup flattened, variables resolved), then map each hit back to the run it
            // falls in. Hits inside a variable's value belong to the variable set, not this topic: dropped here.
            var lines = TextLines.Of(blocks, VariableValue);
            var texts = new List<string>();
            var headings = new List<int>();
            for (var i = 0; i < lines.Count; i++)
            {
                texts.Add(lines[i].Text);
                if (lines[i].IsHeading) headings.Add(i);
            }
            var inVariables = 0;
            foreach (var h in _style.Lint(texts, headings, _project))
            {
                var f = MapToRun(lines[h.Index], h);
                if (f != null) findings.Add(f); else inVariables++;
            }
            if (inVariables > 0) Diagnostics.Log("Lint: " + inVariables + " finding(s) inside variable values skipped");
            return findings;
        }

        /// <summary>
        /// Lint findings inside the snippets these blocks include (and snippets those include), read from disk.
        /// Each finding carries the snippet's path; fixes are applied in the snippet (the caller opens it).
        /// </summary>
        public List<StyleFinding> LintSnippets(IList<XmlNode> blocks, string topicPath)
        {
            var findings = new List<StyleFinding>();
            if (_style == null || string.IsNullOrEmpty(topicPath)) return findings;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFullPath(topicPath) };
            LintSnippetsOf(blocks, topicPath, seen, findings, 0);
            return findings;
        }

        private void LintSnippetsOf(IList<XmlNode> blocks, string hostPath, HashSet<string> seen, List<StyleFinding> findings, int depth)
        {
            if (depth > 4) return;
            foreach (var path in SnippetPaths(blocks, hostPath))
            {
                if (!seen.Add(path) || !System.IO.File.Exists(path)) continue;
                XmlDocument snippet;
                try { snippet = new XmlDocument { PreserveWhitespace = true }; snippet.Load(path); }
                catch (Exception ex) { Diagnostics.Log("snippet " + path, ex); continue; }
                var body = BlockSelector.FindBody(snippet);
                if (body == null) continue;
                var inner = new List<XmlNode>();
                foreach (XmlNode c in body.ChildNodes) if (c.NodeType == XmlNodeType.Element) inner.Add(c);
                foreach (var f in Lint(inner))
                {
                    f.DocumentPath = path;
                    f.Block = null; // located again in the snippet as Flare opens it
                    findings.Add(f);
                }
                LintSnippetsOf(inner, path, seen, findings, depth + 1);
            }
        }

        private static IEnumerable<string> SnippetPaths(IList<XmlNode> blocks, string hostPath)
        {
            var dir = System.IO.Path.GetDirectoryName(hostPath) ?? "";
            var removed = TrackedChanges.RemoveIds(blocks.Count > 0 ? blocks[0].OwnerDocument : null);
            foreach (var b in blocks)
            {
                var ns = new XmlNamespaceManager(b.OwnerDocument.NameTable);
                ns.AddNamespace("MadCap", BlockSelector.MadCapNs);
                foreach (XmlElement s in b.SelectNodes("descendant-or-self::MadCap:snippetBlock | descendant-or-self::MadCap:snippetText", ns))
                {
                    var src = s.GetAttribute("src");
                    if (src.Length == 0 || TrackedChanges.IsDeleted(s, removed)) continue;
                    string full;
                    try { full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, src.Replace('/', System.IO.Path.DirectorySeparatorChar))); }
                    catch (Exception) { continue; }
                    yield return full;
                }
            }
        }

        /// <summary>Topics and snippets of the Flare project containing <paramref name="anyPath"/> (Content folder), or empty.</summary>
        public static List<string> ProjectFiles(string anyPath)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(anyPath)) return files;
            var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(anyPath)));
            while (dir != null && dir.GetFiles("*.flprj").Length == 0) dir = dir.Parent;
            if (dir == null) return files;
            var content = System.IO.Path.Combine(dir.FullName, "Content");
            if (!System.IO.Directory.Exists(content)) return files;
            var guide = System.IO.Path.Combine(content, @"Resources\StyleGuide") + System.IO.Path.DirectorySeparatorChar;
            foreach (var pattern in new[] { "*.htm", "*.html", "*.flsnp" })
                foreach (var f in System.IO.Directory.GetFiles(content, pattern, System.IO.SearchOption.AllDirectories))
                    if (!f.StartsWith(guide, StringComparison.OrdinalIgnoreCase) && new System.IO.FileInfo(f).Length < 2000000) files.Add(f);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>
        /// Lints whole files from disk (no AI): every topic/snippet in one linter call per batch. Findings carry the
        /// file path; fixes are applied after opening the file in the editor.
        /// </summary>
        public List<StyleFinding> LintFiles(IList<string> paths, Action<int, int> progress = null)
        {
            var findings = new List<StyleFinding>();
            if (_style == null) return findings;
            var lines = new List<TextLine>();
            var owners = new List<string>();
            var done = 0;
            foreach (var path in paths)
            {
                done++;
                try
                {
                    var xml = new XmlDocument { PreserveWhitespace = true };
                    xml.Load(path);
                    var body = BlockSelector.FindBody(xml);
                    if (body == null) continue;
                    var blocks = new List<XmlNode>();
                    foreach (XmlNode c in body.ChildNodes) if (c.NodeType == XmlNodeType.Element) blocks.Add(c);
                    foreach (var l in TextLines.Of(blocks, VariableValue)) { lines.Add(l); owners.Add(path); }
                    foreach (var s in StructureCheck.Of(blocks)) { s.DocumentPath = path; s.Block = null; findings.Add(s); }
                }
                catch (Exception ex) { Diagnostics.Log("project lint: " + path, ex); }
                if (progress != null && (done % 25 == 0 || done == paths.Count)) progress(done, paths.Count);
            }
            const int Batch = 1500;
            for (var from = 0; from < lines.Count; from += Batch)
            {
                var count = Math.Min(Batch, lines.Count - from);
                var texts = new List<string>();
                var headings = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    texts.Add(lines[from + i].Text);
                    if (lines[from + i].IsHeading) headings.Add(i);
                }
                foreach (var h in _style.Lint(texts, headings, _project))
                {
                    var f = MapToRun(lines[from + h.Index], h);
                    if (f == null) continue;
                    f.DocumentPath = owners[from + h.Index];
                    f.Block = null;
                    findings.Add(f);
                }
            }
            Diagnostics.Log("Project lint: " + paths.Count + " files, " + lines.Count + " lines, " + findings.Count + " findings");
            return findings;
        }

        /// <summary>Resolves "Set.Name" to the variable's value for linting; null leaves the variable out.</summary>
        public Func<string, string> VariableValue { get; set; }

        private static StyleFinding MapToRun(TextLine line, LintHit h)
        {
            int start = h.Start, end = h.Start + h.Length;
            var pieces = new List<Segment>();
            foreach (var s in line.Segments)
                if (s.Offset < end && start < s.Offset + s.Text.Length && s.Text.Length > 0) pieces.Add(s);
            var first = pieces.Find(s => s.Run != null && s.Run.Value.Trim().Length > 0);
            if (first == null) return null; // entirely inside variable values (or whitespace)
            var from = Math.Max(start, first.Offset) - first.Offset;
            var to = Math.Min(end, first.Offset + first.Text.Length) - first.Offset;
            // Only a hit wholly inside one run can be fixed mechanically; one that spans markup or a variable
            // is shown on its first run and needs a rewrite.
            return FindingLocator.FromLint(h, first.Run, from, to - from, pieces.Count == 1);
        }

        /// <summary>
        /// Model review for what a linter cannot judge. Only findings that quote the topic verbatim survive,
        /// so every finding can be shown in place; repeats of lint findings are dropped.
        /// </summary>
        public List<StyleFinding> Review(IList<XmlNode> blocks, IList<StyleFinding> lint)
        {
            return AnchorReview(CallReview(ReviewPrompt(blocks, lint)), blocks, lint);
        }

        /// <summary>Step 1 (reads the DOM): the review prompt for these blocks.</summary>
        public string ReviewPrompt(IList<XmlNode> blocks, IList<StyleFinding> lint)
        {
            return _prompts.BuildUser(ActionKind.CheckStyle, XhtmlFragment.Serialize(TrackedChanges.WithoutDeletions(blocks)), null, Summarize(lint));
        }

        /// <summary>Step 2 (no DOM access, safe off the UI thread while the author edits): ask the model.</summary>
        public List<StyleFinding> CallReview(string prompt)
        {
            var resp = _llm.Complete(new LlmRequest { System = _prompts.BuildSystem(), User = prompt, MaxTokens = _maxTokens });
            var raw = ResponseParser.ParseFindings(resp.Text);
            Diagnostics.Log("Review: ai=" + raw.Count + " in=" + resp.InputTokens + " out=" + resp.OutputTokens + " stop=" + resp.StopReason);
            return raw;
        }

        /// <summary>
        /// Step 3 (reads the DOM, run it on the thread that edits): keep findings that quote the topic as it is
        /// now, preferring the reviewed blocks; drop repeats of lint findings and unsafe replacements.
        /// </summary>
        public List<StyleFinding> AnchorReview(List<StyleFinding> raw, IList<XmlNode> blocks, IList<StyleFinding> lint)
        {
            var scope = new List<XmlNode>();
            foreach (var b in blocks) if (TextRuns.BlockOf(b) == b) scope.Add(b);
            var body = blocks.Count > 0 ? BlockSelector.FindBody(blocks[0].OwnerDocument) : null;
            if (body != null) foreach (XmlNode c in body.ChildNodes) if (c.NodeType == XmlNodeType.Element && !scope.Contains(c)) scope.Add(c);
            var runs = TextRuns.Of(scope);

            var kept = new List<StyleFinding>();
            var rules = GuideRules();
            foreach (var f in raw)
            {
                if (f.Replacement != null && (f.Replacement.IndexOf('<') >= 0 || f.Replacement == f.Excerpt)) f.Replacement = null;
                Ground(f, rules);
                if (FindingLocator.Anchor(f, runs) && !Duplicates(f, lint)) kept.Add(f);
            }
            if (kept.Count < raw.Count) Diagnostics.Log("Review: dropped " + (raw.Count - kept.Count) + " unquotable/duplicate finding(s)");
            return kept;
        }

        private Dictionary<string, GuideRule> _rules;
        private Dictionary<string, GuideExample> _examples;

        private Dictionary<string, GuideRule> GuideRules()
        {
            if (_rules != null) return _rules;
            var map = new Dictionary<string, GuideRule>(StringComparer.OrdinalIgnoreCase);
            if (_style == null) return map;
            try { foreach (var r in _style.Rules(_project)) map[r.Id] = r; _rules = map; }
            catch (Exception ex) { Diagnostics.Log("guide rules", ex); }
            return map;
        }

        /// <summary>The guide rule with this id (for the proposal card), or null.</summary>
        public GuideRule RuleFor(string id)
        {
            GuideRule r;
            return id != null && GuideRules().TryGetValue(id, out r) ? r : null;
        }

        /// <summary>The rule's Bad/Good example, or null. Loaded once per runner.</summary>
        public GuideExample ExampleFor(string id)
        {
            if (id == null || _style == null) return null;
            if (_examples == null)
            {
                _examples = new Dictionary<string, GuideExample>(StringComparer.OrdinalIgnoreCase);
                try { foreach (var e in _style.Examples(_project)) _examples[e.Id] = e; }
                catch (Exception ex) { Diagnostics.Log("guide examples", ex); }
            }
            GuideExample g;
            return _examples.TryGetValue(id, out g) ? g : null;
        }

        /// <summary>
        /// Re-asks the model for one proposal with the author's instruction. Returns a new finding (same rule, text
        /// and anchor; new replacement and reason). No DOM access: pass the paragraph text as context.
        /// </summary>
        public StyleFinding Refine(StyleFinding f, string context, string instruction)
        {
            var proposal = (f.Replacement == null ? "(no exact text proposed — needs a rewrite)" : "\"" + f.Replacement + "\"") + " — " + f.Suggestion;
            var resp = _llm.Complete(new LlmRequest { System = _prompts.BuildSystem(), User = _prompts.BuildRefine(context, f.Excerpt, proposal, instruction), MaxTokens = 512 });
            Diagnostics.Log("Refine: in=" + resp.InputTokens + " out=" + resp.OutputTokens);
            var r = ResponseParser.ParseRefined(resp.Text);
            var replacement = r.Key;
            if (replacement != null && replacement.IndexOf('<') >= 0) replacement = null;
            return new StyleFinding
            {
                Source = "ai", Layer = "ai", Rule = f.Rule, RuleId = f.RuleId, RuleLayer = f.RuleLayer, RuleSection = f.RuleSection, Link = f.Link,
                Severity = f.Severity, Excerpt = f.Excerpt, Before = f.Before, Block = f.Block, DocumentPath = f.DocumentPath,
                Replacement = replacement, Suggestion = string.IsNullOrEmpty(r.Value) ? f.Suggestion : r.Value, Section = f.Section, Order = f.Order
            };
        }

        /// <summary>Alt text for an image (vision). Null when the model judges it decorative.</summary>
        public string SuggestAlt(LlmImageFile image, string context)
        {
            var resp = _llm.Complete(new LlmRequest
            {
                System = _prompts.BuildSystem(), User = _prompts.BuildAlt(context), MaxTokens = 200,
                Images = new List<LlmImage> { new LlmImage { MediaType = image.MediaType, Base64 = image.Base64 } }
            });
            Diagnostics.Log("Alt text: in=" + resp.InputTokens + " out=" + resp.OutputTokens);
            var alt = (resp.Text ?? "").Trim().Trim('"');
            return alt.Equals("DECORATIVE", StringComparison.OrdinalIgnoreCase) ? null : alt;
        }

        /// <summary>Meta description, index keywords, search synonyms, micro content and glossary candidates for the blocks.</summary>
        public TopicAssist Assist(IList<XmlNode> blocks)
        {
            var glossary = "";
            if (_style is McpStyleService)
                try { foreach (var g in ((McpStyleService)_style).GlossaryTerms(_project)) glossary += g + "\n"; } catch (Exception ex) { Diagnostics.Log("assist: glossary", ex); }
            var fragment = XhtmlFragment.Serialize(TrackedChanges.WithoutDeletions(blocks));
            var resp = _llm.Complete(new LlmRequest { System = _prompts.BuildSystem(), User = _prompts.BuildAssist(fragment, glossary.Trim()), MaxTokens = _maxTokens });
            Diagnostics.Log("Topic assists: in=" + resp.InputTokens + " out=" + resp.OutputTokens);
            return TopicAssist.Parse(resp.Text);
        }

        /// <summary>Candidate style-guide rules the AI finds in an author's notes, an old guide, or another tool's prompt.</summary>
        public List<RuleCandidate> ExtractRules(string notes)
        {
            var resp = _llm.Complete(new LlmRequest { System = _prompts.BuildSystem(), User = _prompts.BuildExtract(notes), MaxTokens = _maxTokens });
            Diagnostics.Log("Extract rules: in=" + resp.InputTokens + " out=" + resp.OutputTokens + " stop=" + resp.StopReason);
            return GuideBuilder.ParseExtracted(resp.Text);
        }

        private static readonly Dictionary<string, int> SnippetUses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How many topics and snippets in the project include this snippet (by file name). Cached per session.</summary>
        public static int SnippetUsage(string snippetPath)
        {
            lock (SnippetUses)
            {
                int n;
                if (SnippetUses.TryGetValue(snippetPath, out n)) return n;
                var name = System.IO.Path.GetFileName(snippetPath);
                foreach (var file in ProjectFiles(snippetPath))
                {
                    if (string.Equals(file, snippetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    try { if (System.IO.File.ReadAllText(file).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) n++; }
                    catch (Exception) { }
                }
                SnippetUses[snippetPath] = n;
                return n;
            }
        }

        /// <summary>Keeps a cited rule only if it exists in the guide; attaches its layer, section, and link.</summary>
        private static void Ground(StyleFinding f, Dictionary<string, GuideRule> rules)
        {
            GuideRule rule;
            if (f.RuleId != null && rules.TryGetValue(f.RuleId.Trim(), out rule))
            {
                f.RuleId = rule.Id; f.RuleLayer = rule.Layer; f.RuleSection = rule.Section;
                if (string.IsNullOrEmpty(f.Link)) f.Link = rule.Link;
            }
            else f.RuleId = null; // "general", or a made-up id
        }

        /// <summary>
        /// Checks that the AI applies prose rules: each Bad example must produce a finding citing that rule, each
        /// Good example must not. Two model calls per example.
        /// </summary>
        public List<ExampleResult> VerifyExamples(IList<GuideExample> examples, Action<int, int> progress = null)
        {
            var results = new List<ExampleResult>();
            var rules = GuideRules();
            for (var i = 0; i < examples.Count; i++)
            {
                var ex = examples[i];
                var result = new ExampleResult { Example = ex };
                if (!string.IsNullOrEmpty(ex.Bad)) result.BadCaught = Cites(ex.Bad, ex.Id, rules);
                if (!string.IsNullOrEmpty(ex.Good)) result.GoodClean = !Cites(ex.Good, ex.Id, rules);
                results.Add(result);
                if (progress != null) progress(i + 1, examples.Count);
            }
            return results;
        }

        private bool Cites(string text, string id, Dictionary<string, GuideRule> rules)
        {
            var fragment = "<p>" + System.Security.SecurityElement.Escape(text) + "</p>\n";
            var prompt = _prompts.BuildUser(ActionKind.CheckStyle, fragment, null, "");
            foreach (var f in CallReview(prompt))
            {
                Ground(f, rules);
                if (string.Equals(f.RuleId, id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The block that currently holds the finding's text (blocks are replaced by edits), or null.</summary>
        public XmlNode LocateBlock(IDocumentAdapter doc, StyleFinding f)
        {
            XmlText run; int start;
            return FindingLocator.TryLocate(doc.Document, f, out run, out start) ? TextRuns.BlockOf(run) : null;
        }

        /// <summary>Lint + review in one call (the UI runs the two phases separately to show lint results first).</summary>
        public List<StyleFinding> CheckStyle(IDocumentAdapter doc)
        {
            var blocks = PickCheckBlocks(doc);
            var lint = Lint(blocks);
            lint.AddRange(LintSnippets(blocks, doc.SourcePath));
            var all = new List<StyleFinding>(lint);
            all.AddRange(Review(blocks, lint));
            return all;
        }

        /// <summary>Selects the finding's text in the editor. False when the text is no longer in the topic.</summary>
        public bool GoTo(IDocumentAdapter doc, StyleFinding f)
        {
            XmlText run; int start;
            return FindingLocator.TryLocate(doc.Document, f, out run, out start) && doc.SelectText(run, start, f.Excerpt.Length);
        }

        /// <summary>
        /// Applies the replacements of fixable findings as word-level tracked changes in one undoable operation.
        /// Every finding is re-located right before its edit (each edit changes the text nodes), right-most first.
        /// Returns how many were applied.
        /// </summary>
        public int ApplyFixes(IDocumentAdapter doc, IEnumerable<StyleFinding> findings, string operationName)
        {
            var order = new Dictionary<XmlNode, int>();
            var body = BlockSelector.FindBody(doc.Document);
            if (body == null) return 0;
            var all = TextRuns.Of(new List<XmlNode> { body });
            for (var i = 0; i < all.Count; i++) order[all[i]] = i;

            var todo = new List<KeyValuePair<StyleFinding, long>>();
            foreach (var f in findings)
            {
                XmlText run; int start; int pos;
                if (f.CanFix && FindingLocator.TryLocate(doc.Document, f, out run, out start) && order.TryGetValue(run, out pos))
                    todo.Add(new KeyValuePair<StyleFinding, long>(f, (long)pos * 1000000 + start));
            }
            todo.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (todo.Count == 0) return 0;

            var applied = 0;
            var text = todo;
            if (text.Count > 0)
                doc.Edit(operationName, () =>
                {
                    foreach (var item in text)
                    {
                        var f = item.Key;
                        XmlText run; int start;
                        if (!FindingLocator.TryLocate(doc.Document, f, out run, out start)) continue;
                        if (doc.ReplaceSpan(run, start, f.Excerpt.Length, f.Replacement)) { f.IsFixed = true; applied++; }
                    }
                });
            Diagnostics.Log("Applied " + operationName + ": " + applied + "/" + todo.Count + " text fix(es)");
            return applied;
        }

        // ---- rewrite / ask ---------------------------------------------------------------------

        public RewriteProposal Rewrite(IDocumentAdapter doc, string instruction)
        {
            return Rewrite(doc, PickBlocks(doc), instruction);
        }

        public RewriteProposal Rewrite(IDocumentAdapter doc, IList<XmlNode> blocks, string instruction)
        {
            return Rewrite(doc, blocks, instruction, true);
        }

        /// <param name="lint">False for style-guide topics: they quote the very words their rules ban, so linting them
        /// (and the self-check repair round) would "fix" the rules themselves.</param>
        public RewriteProposal Rewrite(IDocumentAdapter doc, IList<XmlNode> blocks, string instruction, bool lint)
        {
            foreach (var b in blocks)
                if (BlockSelector.IsOpaque(b))
                    throw new InvalidOperationException("The selection includes a " + b.Name + ", which cannot be rewritten in place. Select text inside a paragraph instead.");
            // Pending tracked changes stay pending: the model sees the text as the author reads it, and the
            // result is applied word-level around them (never accepted or rejected on the author's behalf).
            var view = AcceptedView.Of(blocks);
            var notes = new List<string>();
            var found = lint ? SafeLint(blocks, notes) : new List<StyleFinding>();
            var kind = string.IsNullOrEmpty(instruction) ? ActionKind.Rewrite : ActionKind.Ask;
            var proposal = Propose(doc, blocks, view, _prompts.BuildUser(kind, XhtmlFragment.Serialize(view), instruction, Summarize(found)), kind.ToString());
            proposal.Notes = notes;
            proposal.RemainingLint = new List<StyleFinding>();
            if (!lint || !proposal.Validation.IsValid || _style == null || notes.Count > 0) return proposal;

            // Self-check: lint our own output and give the model one chance to fix what it left behind.
            var left = Significant(SafeLint(proposal.ProposedBlocks, notes));
            if (left.Count > 0)
            {
                var repaired = Propose(doc, blocks, view, _prompts.BuildRepair(proposal.ProposedXml, Summarize(left)), kind + ".repair");
                if (repaired.Validation.IsValid)
                {
                    repaired.Notes = notes;
                    proposal = repaired;
                    left = Significant(SafeLint(proposal.ProposedBlocks, notes));
                }
            }
            proposal.RemainingLint = left;
            return proposal;
        }

        private RewriteProposal Propose(IDocumentAdapter doc, IList<XmlNode> blocks, IList<XmlNode> view, string user, string label)
        {
            var resp = _llm.Complete(new LlmRequest { System = _prompts.BuildSystem(), User = user, MaxTokens = _maxTokens });
            Diagnostics.Log(label + ": blocks=" + blocks.Count + " in=" + resp.InputTokens + " out=" + resp.OutputTokens + " stop=" + resp.StopReason);

            var proposal = new RewriteProposal { OriginalBlocks = blocks, View = view, Raw = resp, Validation = new ValidationResult(), Notes = new List<string>(), RemainingLint = new List<StyleFinding>() };
            try
            {
                proposal.ProposedXml = ResponseParser.ParseRevised(resp.Text);
                proposal.ProposedBlocks = XhtmlFragment.ParseInto(proposal.ProposedXml, doc.Document);
                proposal.Validation = XhtmlValidator.Validate(view, proposal.ProposedBlocks);
                if (proposal.Validation.IsValid)
                {
                    proposal.WordPlan = WordDiff.Plan(view, proposal.ProposedBlocks, blocks);
                    if (proposal.WordPlan == null && TrackedChanges.HasPending(blocks))
                        proposal.Validation.Errors.Add("This edit restructures text that has pending tracked changes. Accept or reject those changes (Review tab) first, or ask for a wording-only edit.");
                }
            }
            catch (XmlException xe)
            {
                proposal.Validation.Errors.Add("Response is not well-formed XML: " + xe.Message);
            }
            catch (FormatException fe)
            {
                proposal.Validation.Errors.Add(fe.Message);
            }
            return proposal;
        }

        /// <summary>
        /// Applies a validated proposal as one undoable tracked operation: word-level replacements when the
        /// proposal kept the structure, otherwise a block replacement. Refuses if validation failed — the UI must
        /// not bypass this. Returns a one-line description of what was applied.
        /// </summary>
        public string Apply(IDocumentAdapter doc, RewriteProposal proposal, string operationName)
        {
            if (proposal == null || !proposal.Validation.IsValid)
                throw new InvalidOperationException("Cannot apply: proposal failed validation.");

            var plan = proposal.WordPlan;
            if (plan != null)
            {
                if (plan.Count == 0) return "No wording changes to apply.";
                // All-or-nothing: only go word-level when every piece can be found right now.
                var locatable = plan.TrueForAll(f => { XmlText r; int s; return FindingLocator.TryLocate(doc.Document, f, out r, out s); });
                if (locatable)
                {
                    var n = ApplyFixes(doc, plan, operationName);
                    if (n < plan.Count) Diagnostics.Log(operationName + ": only " + n + "/" + plan.Count + " word edits applied");
                    return n == plan.Count
                        ? "Applied as " + n + " word-level tracked change(s)."
                        : "Applied " + n + " of " + plan.Count + " word-level changes — review them, or Ctrl+Z to undo all.";
                }
                Diagnostics.Log(operationName + ": word plan not locatable, falling back to block replacement");
            }
            if (TrackedChanges.HasPending(proposal.OriginalBlocks))
                throw new InvalidOperationException("The paragraph has pending tracked changes and this edit cannot be applied word by word. Accept or reject them (Review tab) first.");
            doc.ReplaceBlocks(operationName, proposal.OriginalBlocks, proposal.ProposedBlocks);
            Diagnostics.Log("Applied " + operationName + ": " + proposal.OriginalBlocks.Count + " → " + proposal.ProposedBlocks.Count + " blocks");
            return "Applied as a paragraph-level tracked change (the edit restructures the text).";
        }

        // ---- helpers -----------------------------------------------------------------------------

        private List<StyleFinding> SafeLint(IList<XmlNode> blocks, List<string> notes)
        {
            try { return Lint(blocks); }
            catch (Exception ex)
            {
                Diagnostics.Log("docstyle", ex);
                notes.Add("Style linter unavailable: " + ex.Message);
                return new List<StyleFinding>();
            }
        }

        private static List<StyleFinding> Significant(List<StyleFinding> findings)
        {
            return findings.FindAll(f => f.Severity != "suggestion");
        }

        /// <summary>Compact list the model can act on: one line per finding.</summary>
        public static string Summarize(IList<StyleFinding> findings)
        {
            if (findings == null || findings.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var f in findings)
            {
                sb.Append("- \"").Append(f.Excerpt).Append('"');
                if (f.Replacement != null) sb.Append(" → \"").Append(f.Replacement).Append('"');
                sb.Append(" (").Append(f.Rule).Append(": ").Append(f.Suggestion).AppendLine(")");
            }
            return sb.ToString().TrimEnd();
        }

        private static bool Duplicates(StyleFinding ai, IList<StyleFinding> lint)
        {
            foreach (var l in lint)
                // Same or narrower text than a lint hit = a repeat. A broader AI finding (a whole passive sentence) stays.
                if (l.Excerpt.IndexOf(ai.Excerpt, StringComparison.Ordinal) >= 0 && (l.Block == ai.Block || l.Before == ai.Before))
                    return true;
            return false;
        }
    }
}
