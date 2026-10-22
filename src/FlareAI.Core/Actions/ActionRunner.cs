using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;
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
        /// <summary>The model could only do part of the task by changing wording; the rest needs a structural edit.</summary>
        public bool NeedsRestructure { get; set; }
        public string RestructureNote { get; set; }
    }

    /// <summary>One unit of AI review: its blocks, their numbered runs, and the prompt.</summary>
    public sealed class ReviewUnit
    {
        public List<XmlNode> Blocks { get; set; }
        public RunMap Runs { get; set; }
        public string Prompt { get; set; }
        /// <summary>What the unit contains and the words it uses: selects the rules sent with it.</summary>
        public GuideScope Scope { get; set; }
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
        /// <param name="routing">Model and effort per task; null = built-in defaults.</param>
        public ActionRunner(ILlmClient llm, PromptBuilder prompts, int maxTokens, IStyleService style = null, string project = null, Routing routing = null)
        {
            _llm = llm;
            _prompts = prompts;
            _maxTokens = maxTokens;
            _style = style;
            _project = project;
            Routing = routing ?? new Routing();
        }

        public Routing Routing { get; private set; }

        /// <summary>How many model calls run at once for per-block work.</summary>
        public int Parallelism { get; set; } = 6;

        /// <summary>
        /// One model call for a task: its route (model, effort), the system prompt, and the output contract. The token
        /// ceiling is a safety net only; a cut-off answer is retried once with more room.
        /// </summary>
        /// <param name="scope">The text's content and words: the rules to send besides the always-on ones (null = none).</param>
        private LlmResponse Ask(string task, string user, JObject schema, List<LlmImage> images = null, GuideScope scope = null)
        {
            var route = Routing.For(task);
            // Every task carries the same always-on block (same prefix → one cache entry for refine, assists, review…).
            scope = scope ?? new GuideScope();
            if (scope.Always.Count == 0) scope.Always = new HashSet<string>(Routing.AlwaysPacks ?? new List<string>());
            var system = _prompts.BuildSystemParts(scope);
            var req = new LlmRequest
            {
                Task = task, System = system[0], SystemPacks = system[1], SystemTerms = system[2], User = user, Schema = schema, Model = route.Model, Effort = route.Effort,
                Images = images, MaxTokens = Math.Max(_maxTokens, AnthropicClient.DefaultMaxTokens)
            };
            var sw = Stopwatch.StartNew();
            LlmResponse resp;
            try { resp = _llm.Complete(req); }
            catch (LlmException ex) when (ex.Failure == LlmFailure.Truncated)
            {
                Diagnostics.Log(task + ": cut off at " + req.MaxTokens + " tokens; retrying with more room");
                req.MaxTokens *= 2;
                resp = _llm.Complete(req);
            }
            Usage.Record(task, route.Model, resp, sw.Elapsed.TotalSeconds);
            Diagnostics.Log(task + ": " + route.Model + "/" + route.Effort + " in=" + resp.InputTokens + " cached=" + resp.CacheReadTokens +
                            " out=" + resp.OutputTokens + " stop=" + resp.StopReason + " " + sw.ElapsedMilliseconds + "ms");
            if (schema != null && resp.Json == null)
            {
                try { resp.Json = JObject.Parse(resp.Text ?? ""); }
                catch (Newtonsoft.Json.JsonException ex) { throw new LlmException("The AI's answer didn't match the expected format.", 0, ex, LlmFailure.Contract); }
            }
            return resp;
        }

        /// <summary>Runs one job per item, a few at a time; the first failure is rethrown as itself.</summary>
        private void Each(int count, Action<int> job)
        {
            WarmThenParallel(count, Parallelism, job);
        }

        /// <summary>
        /// Runs job 0 alone, then the rest in parallel. The first call writes the prompt cache (the guide's always-on
        /// rules); the others then read it at a tenth of the price instead of each writing it at 1.25×.
        /// </summary>
        public static void WarmThenParallel(int count, int parallelism, Action<int> job)
        {
            if (count == 0) return;
            job(0);
            try { Parallel.For(1, count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism) }, job); }
            catch (AggregateException ae) { throw ae.Flatten().InnerExceptions[0]; }
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
            // Each line carries where it sits (step, cell, note, …) and its inline spans (UI labels, links), so rules fire
            // only in their Content scope and word rules leave UI labels alone.
            var lines = TextLines.Of(blocks, VariableValue);
            var inputs = lines.ConvertAll(l => l.ToLint());
            var inVariables = 0;
            foreach (var h in _style.Lint(inputs, _project))
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
                var inputs = lines.GetRange(from, count).ConvertAll(l => l.ToLint());
                foreach (var h in _style.Lint(inputs, _project))
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
        /// Model review for what a linter cannot judge, unit by unit. Only findings that quote a run of their unit
        /// verbatim survive, so every finding can be shown in place; repeats of lint findings are dropped.
        /// </summary>
        public List<StyleFinding> Review(IList<XmlNode> blocks, IList<StyleFinding> lint)
        {
            var units = PrepareReview(blocks, lint);
            var raw = new List<StyleFinding>[units.Count];
            Each(units.Count, i => raw[i] = CallReview(units[i]));
            var kept = new List<StyleFinding>();
            for (var i = 0; i < units.Count; i++) kept.AddRange(AnchorReview(raw[i], units[i], lint));
            return kept;
        }

        /// <summary>
        /// Step 1 (reads the DOM): splits the blocks into review units — one section each ("section", the default), or one block each ("block"), or one
        /// section each — with each unit's prompt: the marked runs, the topic and section it's in, and the lint already
        /// found there. Units with fewer than <paramref name="minWords"/> words (a short heading) are skipped.
        /// </summary>
        public List<ReviewUnit> PrepareReview(IList<XmlNode> blocks, IList<StyleFinding> lint, int minWords = 4)
        {
            var units = new List<ReviewUnit>();
            if (blocks.Count == 0) return units;
            var groups = Routing.ReviewUnit == "section"
                ? ReviewOrder.Sections(blocks)
                : blocks.Select(b => new List<XmlNode> { b }).ToList();
            var title = TopicTitle(blocks[0].OwnerDocument);
            foreach (var g in groups)
            {
                var map = RunMap.Of(g);
                if (map.Ids.Count == 0 || WordCount(map) < minWords) continue;
                var unitLint = (lint ?? new List<StyleFinding>()).Where(f => f.DocumentPath == null && g.Exists(b => Contains(b, f))).ToList();
                var context = (title.Length > 0 ? "Topic: " + title + "\n" : "") + (SectionOf(g[0]).Length > 0 ? "Section: " + SectionOf(g[0]) : "");
                units.Add(new ReviewUnit { Blocks = g, Runs = map, Prompt = _prompts.BuildReview(context, map.Marked, Summarize(unitLint)), Scope = ScopeOf(g) });
            }
            ShareScope(units.Select(u => u.Scope).ToList());
            return units;
        }

        /// <summary>
        /// The units of one check carry the same packs (the union of what they contain) and the team's always-on packs, so
        /// the pack block is written to the cache once and read by the rest. Each unit keeps its own text for word rules.
        /// </summary>
        private void ShareScope(List<GuideScope> scopes)
        {
            var union = new HashSet<string>(scopes.SelectMany(s => s.Content));
            foreach (var s in scopes)
            {
                s.Content = new HashSet<string>(union);
                s.Always = new HashSet<string>(Routing.AlwaysPacks ?? new List<string>());
                s.SharedPacks = scopes.Count > 1;
            }
        }

        /// <summary>Step 2 (no DOM access; runs off the UI thread, several at once): the model's findings for one unit.</summary>
        public List<StyleFinding> CallReview(ReviewUnit unit)
        {
            var resp = Ask("review", unit.Prompt, Contracts.ReviewSchema, null, unit.Scope);
            int dropped;
            var raw = Contracts.ReadReview(resp.Json, unit.Runs, out dropped);
            if (dropped > 0) Diagnostics.Log("Review: dropped " + dropped + " finding(s) that broke the contract");
            // The model may cite only the rules it was given for this unit.
            HashSet<string> allowed = null;
            if (_style != null) try { allowed = new HashSet<string>(_style.RuleIdsFor(_project, unit.Scope), StringComparer.OrdinalIgnoreCase); } catch (Exception ex) { Diagnostics.Log("rule ids", ex); }
            Contracts.KeepCitable(raw, allowed);
            return raw;
        }

        /// <summary>
        /// Step 3 (reads the DOM, on the thread that edits): anchor each finding in its live run; drop text edited since
        /// the review and repeats of lint findings.
        /// </summary>
        public List<StyleFinding> AnchorReview(List<StyleFinding> raw, ReviewUnit unit, IList<StyleFinding> lint)
        {
            var kept = new List<StyleFinding>();
            var rules = GuideRules();
            foreach (var f in raw)
            {
                if (IsTurnedOff(f.RuleId)) continue;   // turned off since the review was cached
                XmlText run;
                if (f.RunId == null || !unit.Runs.Runs.TryGetValue(f.RunId, out run) || run.ParentNode == null) continue;
                var at = run.Value.IndexOf(f.Excerpt, StringComparison.Ordinal);
                if (at < 0) continue;
                var anchor = FindingLocator.ForSpan(run, at, f.Excerpt.Length, f.Replacement, TextRuns.BlockOf(run));
                f.Before = anchor.Before;
                f.Block = anchor.Block;
                Ground(f, rules);
                if (!Duplicates(f, lint ?? new List<StyleFinding>())) kept.Add(f);
            }
            return kept;
        }

        /// <summary>The unit's content tags and its visible text (reads the DOM: call on the thread that owns it).</summary>
        private static GuideScope ScopeOf(IList<XmlNode> blocks)
        {
            return GuideScope.Of(ContentTags.Of(blocks), string.Join(" ", AcceptedView.Of(blocks).Select(b => b.InnerText).ToArray()));
        }

        private static bool Contains(XmlNode block, StyleFinding f)
        {
            return (block == f.Block || TextRuns.BlockOf(block) == f.Block) && block.InnerText.IndexOf(f.Excerpt ?? "", StringComparison.Ordinal) >= 0;
        }

        private static int WordCount(RunMap map)
        {
            var n = 0;
            foreach (var id in map.Ids) n += map.Texts[id].Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return n;
        }

        private static string TopicTitle(XmlDocument doc)
        {
            var body = BlockSelector.FindBody(doc);
            var h1 = body == null ? null : body.SelectSingleNode(".//*[local-name()='h1']");
            if (h1 != null) return System.Text.RegularExpressions.Regex.Replace(h1.InnerText, @"\s+", " ").Trim();
            var t = doc == null ? null : doc.SelectSingleNode("//*[local-name()='title']");
            return t == null ? "" : t.InnerText.Trim();
        }

        /// <summary>The heading the node falls under ("" before the first heading).</summary>
        private static string SectionOf(XmlNode node)
        {
            var block = TextRuns.BlockOf(node) ?? node;
            for (var n = block; n != null; n = n.PreviousSibling)
                if (n.NodeType == XmlNodeType.Element && ReviewOrder.IsHeading(n))
                    return System.Text.RegularExpressions.Regex.Replace(n.InnerText, @"\s+", " ").Trim();
            return "";
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

        private bool IsTurnedOff(string ruleId)
        {
            if (ruleId == null || _style == null) return false;
            try { return _style.IsOff(_project, ruleId); }
            catch (Exception ex) { Diagnostics.Log("rule off?", ex); return false; }
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
            GuideRule rule = null;
            var known = f.RuleId != null && GuideRules().TryGetValue(f.RuleId, out rule);   // the rule's own pack and words come along
            var json = Ask("refine", _prompts.BuildRefine(context, f.Excerpt, proposal, instruction), Contracts.Refine(), null,
                           GuideScope.Of(known ? rule.Applies : null, context)).Json;
            var r = json["replacement"];
            var replacement = r == null || r.Type == JTokenType.Null ? null : (string)r;
            if (replacement != null && replacement.IndexOf('<') >= 0) replacement = null;
            var why = (string)json["why"];
            return new StyleFinding
            {
                Source = "ai", Layer = "ai", Rule = f.Rule, RuleId = f.RuleId, RuleLayer = f.RuleLayer, RuleSection = f.RuleSection, Link = f.Link,
                Severity = f.Severity, Excerpt = f.Excerpt, Before = f.Before, Block = f.Block, DocumentPath = f.DocumentPath, RunId = f.RunId,
                Replacement = replacement, Suggestion = string.IsNullOrEmpty(why) ? f.Suggestion : why, Section = f.Section, Order = f.Order
            };
        }

        /// <summary>Alt text for an image (vision). Null when the model judges it decorative.</summary>
        public string SuggestAlt(LlmImageFile image, string context)
        {
            var json = Ask("alt", _prompts.BuildAlt(context), Contracts.Alt(),
                           new List<LlmImage> { new LlmImage { MediaType = image.MediaType, Base64 = image.Base64 } }).Json;
            var alt = ((string)json["alt"] ?? "").Trim();
            return (bool?)json["decorative"] == true || alt.Length == 0 ? null : alt;
        }

        /// <summary>
        /// Meta description, index keywords, search synonyms, micro content and glossary candidates for the blocks: three
        /// small calls, each with its own contract, run at once.
        /// </summary>
        public TopicAssist Assist(IList<XmlNode> blocks)
        {
            var glossary = "";
            if (_style is StyleEngine)
                try { foreach (var g in ((StyleEngine)_style).GlossaryTerms(_project)) glossary += g + "\n"; } catch (Exception ex) { Diagnostics.Log("assist: glossary", ex); }
            var fragment = XhtmlFragment.Serialize(TrackedChanges.WithoutDeletions(blocks));
            var parts = new[]
            {
                new KeyValuePair<string, JObject>("Write its meta description and its index keywords (3-8; 'Parent:Child' for a subentry).", Contracts.AssistMeta()),
                new KeyValuePair<string, JObject>("List search synonym groups: up to 5 terms the topic uses, each with the words readers might search for instead.", Contracts.AssistSynonyms()),
                new KeyValuePair<string, JObject>("Write up to 5 micro content entries (a question or phrase readers search for, and a 1-2 sentence answer from the topic) and up to 5 glossary candidates (terms the topic uses but doesn't define).", Contracts.AssistAnswers()),
            };
            var answers = new JObject[parts.Length];
            Each(parts.Length, i => answers[i] = Ask("assist", _prompts.BuildAssist(fragment, glossary.Trim(), parts[i].Key), parts[i].Value).Json);
            var t = new TopicAssist { Description = (string)answers[0]["description"] };
            foreach (var k in answers[0]["keywords"] ?? new JArray()) t.Keywords.Add((string)k);
            foreach (var g in answers[1]["groups"] ?? new JArray())
            {
                var group = new List<string> { (string)g["term"] };
                foreach (var a in g["alternatives"] ?? new JArray()) group.Add((string)a);
                if (group.Count > 1) t.Synonyms.Add(group);
            }
            foreach (var m in answers[2]["micro_content"] ?? new JArray()) t.MicroContent.Add(new KeyValuePair<string, string>((string)m["phrase"], (string)m["response"]));
            foreach (var g in answers[2]["glossary"] ?? new JArray()) t.Glossary.Add(new KeyValuePair<string, string>((string)g["term"], (string)g["definition"]));
            return t;
        }

        /// <summary>
        /// Candidate style-guide rules from an author's notes, an old guide, or another tool's prompt: the notes are split
        /// into paragraphs-sized chunks, each read on its own (small, focused jobs).
        /// </summary>
        public List<RuleCandidate> ExtractRules(string notes)
        {
            var chunks = GuideBuilder.Chunks(notes);
            var answers = new JArray[chunks.Count];
            Each(chunks.Count, i => answers[i] = (JArray)(Ask("extract", _prompts.BuildExtract(chunks[i]), Contracts.Extract()).Json["rules"] ?? new JArray()));
            var all = new JArray();
            foreach (var a in answers) foreach (var r in a) all.Add(r);
            return GuideBuilder.FromExtracted(all);
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
            var doc = new XmlDocument();
            doc.LoadXml("<html><body><p>" + System.Security.SecurityElement.Escape(text) + "</p></body></html>");
            var units = PrepareReview(new List<XmlNode> { doc.DocumentElement["body"].FirstChild }, null, 1);
            if (units.Count == 0) return false;
            GuideRule rule;
            if (rules.TryGetValue(id, out rule)) units[0].Scope.Content.UnionWith(rule.Applies);   // the rule under test must be in the prompt
            foreach (var f in CallReview(units[0]))
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
                if (!f.CanFix) continue;
                if (FindingLocator.TryLocate(doc.Document, f, out run, out start) && order.TryGetValue(run, out pos))
                    todo.Add(new KeyValuePair<StyleFinding, long>(f, (long)pos * 1000000 + start));
                else f.IsStale = true; // can't be placed safely: surface "Text changed", don't skip silently
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
                        if (!FindingLocator.TryLocate(doc.Document, f, out run, out start)) { f.IsStale = true; continue; }
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

        /// <summary>
        /// Rewrite by wording only: each block is its own job (run at once), the model returns new text for the runs it
        /// changes — never markup — so the structure can't break. Applied as word-level tracked changes. If the task needs
        /// structural edits, the proposal says so (<see cref="RewriteProposal.NeedsRestructure"/>); see <see cref="Restructure"/>.
        /// </summary>
        /// <param name="lint">False for style-guide topics: they quote the very words their rules ban.</param>
        /// <param name="task">Route: "rewrite", or "draft" for owner edits to the style guide.</param>
        public RewriteProposal Rewrite(IDocumentAdapter doc, IList<XmlNode> blocks, string instruction, bool lint, string task = "rewrite")
        {
            foreach (var b in blocks)
                if (BlockSelector.IsOpaque(b))
                    throw new InvalidOperationException("The selection includes a " + b.Name + ", which cannot be rewritten in place. Select text inside a paragraph instead.");
            var notes = new List<string>();
            var found = lint ? SafeLint(blocks, notes) : new List<StyleFinding>();
            var proposal = RewriteRuns(blocks, instruction, found, task);
            proposal.Notes = notes;
            if (!lint || _style == null || notes.Count > 0) return proposal;

            // Self-check: lint our own result and give the model one chance to fix what it left behind.
            var left = Significant(SafeLint(proposal.ProposedBlocks, notes));
            if (left.Count > 0)
            {
                var repaired = RewriteRuns(blocks, (string.IsNullOrEmpty(instruction) ? "" : instruction + "\n") + "Also fix these, which your first version left in:\n" + Summarize(left), found, task);
                var leftAfter = Significant(SafeLint(repaired.ProposedBlocks, notes));
                if (leftAfter.Count < left.Count) { repaired.Notes = notes; proposal = repaired; left = leftAfter; }
            }
            proposal.RemainingLint = left;
            return proposal;
        }

        private RewriteProposal RewriteRuns(IList<XmlNode> blocks, string instruction, List<StyleFinding> lint, string task)
        {
            var proposal = new RewriteProposal
            {
                OriginalBlocks = blocks, View = AcceptedView.Of(blocks), Validation = new ValidationResult(), Notes = new List<string>(),
                RemainingLint = new List<StyleFinding>(), WordPlan = new List<StyleFinding>()
            };
            var units = blocks.Select(b => new { Block = b, Map = RunMap.Of(new List<XmlNode> { b }), Scope = ScopeOf(new List<XmlNode> { b }) }).ToList();
            ShareScope(units.Select(u => u.Scope).ToList());
            var prompts = units.Select(u => _prompts.BuildRunRewrite(instruction, u.Map.Marked, Summarize(lint.Where(f => f.DocumentPath == null && Contains(u.Block, f)).ToList()))).ToList();
            var changes = new Dictionary<string, string>[units.Count];
            var restructure = new bool[units.Count];
            var restructureNotes = new string[units.Count];
            Each(units.Count, i =>
            {
                if (units[i].Map.Ids.Count == 0) { changes[i] = new Dictionary<string, string>(); return; }
                var schema = Contracts.RewriteSchema;
                var errors = new List<string>();
                changes[i] = Contracts.ReadRewrite(Ask(task, prompts[i], schema, null, units[i].Scope).Json, units[i].Map, errors, out restructure[i], out restructureNotes[i]);
                if (errors.Count > 0)
                {
                    // One retry with the contract breaches spelled out; then keep what was valid.
                    Diagnostics.Log(task + ": contract breaches, retrying: " + string.Join(" ", errors.ToArray()));
                    var retryErrors = new List<string>();
                    var retry = Contracts.ReadRewrite(Ask(task, prompts[i] + "\n\nYour previous answer broke the format: " + string.Join(" ", errors.ToArray()) + " Answer again.", schema, null, units[i].Scope).Json,
                                                      units[i].Map, retryErrors, out restructure[i], out restructureNotes[i]);
                    if (retryErrors.Count <= errors.Count) changes[i] = retry;
                    if (retryErrors.Count > 0) Diagnostics.Log(task + ": still " + retryErrors.Count + " breach(es); kept the valid changes");
                }
            });

            var proposed = new List<XmlNode>();
            for (var i = 0; i < units.Count; i++)
            {
                var map = units[i].Map;
                var clone = units[i].Block.CloneNode(true);
                var cloneRuns = TextRuns.Of(new List<XmlNode> { clone }).Where(r => r.Value.Trim().Length > 0).ToList();
                for (var k = 0; k < map.Ids.Count && k < cloneRuns.Count; k++)
                {
                    string text;
                    if (!changes[i].TryGetValue(map.Ids[k], out text)) continue;
                    cloneRuns[k].Value = text;
                    var run = map.Runs[map.Ids[k]];
                    var block = TextRuns.BlockOf(run) ?? units[i].Block;
                    if (!WordDiff.PlanRun(run, text, block, proposal.WordPlan))
                        proposal.WordPlan.Add(FindingLocator.ForSpan(run, 0, run.Value.Length, text, block));
                }
                proposed.Add(clone);
                if (restructure[i]) proposal.NeedsRestructure = true;
                // The note says what restructuring is needed, or what only the author can supply.
                if (!string.IsNullOrEmpty(restructureNotes[i])) proposal.RestructureNote = (proposal.RestructureNote == null ? "" : proposal.RestructureNote + " ") + restructureNotes[i];
            }
            proposal.ProposedBlocks = AcceptedView.Of(proposed);
            proposal.ProposedXml = XhtmlFragment.Serialize(proposal.ProposedBlocks);
            return proposal;
        }

        /// <summary>
        /// Structural rewrite (split a step, make a list): the model returns XHTML, which must pass the validator. Applied
        /// word-level when the structure turns out unchanged, otherwise as a block replacement.
        /// </summary>
        public RewriteProposal Restructure(IDocumentAdapter doc, IList<XmlNode> blocks, string instruction)
        {
            foreach (var b in blocks)
                if (BlockSelector.IsOpaque(b))
                    throw new InvalidOperationException("The selection includes a " + b.Name + ", which cannot be rewritten in place. Select text inside a paragraph instead.");
            var view = AcceptedView.Of(blocks);
            var notes = new List<string>();
            var lint = SafeLint(blocks, notes);
            var kind = string.IsNullOrEmpty(instruction) ? ActionKind.Rewrite : ActionKind.Ask;
            var proposal = Propose(doc, blocks, view, _prompts.BuildUser(kind, XhtmlFragment.Serialize(view), instruction, Summarize(lint)), "restructure");
            proposal.Notes = notes;
            proposal.RemainingLint = proposal.Validation.IsValid ? Significant(SafeLint(proposal.ProposedBlocks, notes)) : new List<StyleFinding>();
            return proposal;
        }

        private RewriteProposal Propose(IDocumentAdapter doc, IList<XmlNode> blocks, IList<XmlNode> view, string user, string task)
        {
            var resp = Ask(task, user, null, null, ScopeOf(blocks));
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
                throw new InvalidOperationException("Cannot apply: the suggestion failed validation.");

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
                notes.Add("The style rules couldn't be loaded: " + ex.Message);
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
