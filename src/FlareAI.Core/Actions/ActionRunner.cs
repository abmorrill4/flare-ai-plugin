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

        public ActionRunner(ILlmClient llm, PromptBuilder prompts, int maxTokens, IStyleService style = null)
        {
            _llm = llm;
            _prompts = prompts;
            _maxTokens = maxTokens;
            _style = style;
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
            var runs = TextRuns.Of(blocks);
            var texts = new List<string>();
            var headings = new List<int>();
            for (var i = 0; i < runs.Count; i++)
            {
                texts.Add(runs[i].Value);
                if (TextRuns.IsHeading(runs[i])) headings.Add(i);
            }
            foreach (var h in _style.Lint(texts, headings)) findings.Add(FindingLocator.FromLint(h, runs[h.Index]));
            return findings;
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
            foreach (var f in raw)
            {
                if (f.Replacement != null && (f.Replacement.IndexOf('<') >= 0 || f.Replacement == f.Excerpt)) f.Replacement = null;
                if (FindingLocator.Anchor(f, runs) && !Duplicates(f, lint)) kept.Add(f);
            }
            if (kept.Count < raw.Count) Diagnostics.Log("Review: dropped " + (raw.Count - kept.Count) + " unquotable/duplicate finding(s)");
            return kept;
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
            doc.Edit(operationName, () =>
            {
                foreach (var item in todo)
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
            foreach (var b in blocks)
                if (BlockSelector.IsOpaque(b))
                    throw new InvalidOperationException("The selection includes a " + b.Name + ", which cannot be rewritten in place. Select text inside a paragraph instead.");
            if (TrackedChanges.HasPending(blocks))
                throw new InvalidOperationException("The selection has pending tracked changes. Accept or reject them (Review tab) before rewriting.");

            var notes = new List<string>();
            var lint = SafeLint(blocks, notes);
            var kind = string.IsNullOrEmpty(instruction) ? ActionKind.Rewrite : ActionKind.Ask;
            var proposal = Propose(doc, blocks, _prompts.BuildUser(kind, XhtmlFragment.Serialize(blocks), instruction, Summarize(lint)), kind.ToString());
            proposal.Notes = notes;
            proposal.RemainingLint = new List<StyleFinding>();
            if (!proposal.Validation.IsValid || _style == null || notes.Count > 0) return proposal;

            // Self-check: lint our own output and give the model one chance to fix what it left behind.
            var left = Significant(SafeLint(proposal.ProposedBlocks, notes));
            if (left.Count > 0)
            {
                var repaired = Propose(doc, blocks, _prompts.BuildRepair(proposal.ProposedXml, Summarize(left)), kind + ".repair");
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

        private RewriteProposal Propose(IDocumentAdapter doc, IList<XmlNode> blocks, string user, string label)
        {
            var resp = _llm.Complete(new LlmRequest { System = _prompts.BuildSystem(), User = user, MaxTokens = _maxTokens });
            Diagnostics.Log(label + ": blocks=" + blocks.Count + " in=" + resp.InputTokens + " out=" + resp.OutputTokens + " stop=" + resp.StopReason);

            var proposal = new RewriteProposal { OriginalBlocks = blocks, Raw = resp, Validation = new ValidationResult(), Notes = new List<string>(), RemainingLint = new List<StyleFinding>() };
            try
            {
                proposal.ProposedXml = ResponseParser.ParseRevised(resp.Text);
                proposal.ProposedBlocks = XhtmlFragment.ParseInto(proposal.ProposedXml, doc.Document);
                proposal.Validation = XhtmlValidator.Validate(blocks, proposal.ProposedBlocks);
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

        /// <summary>Applies a validated proposal. Refuses if validation failed — the UI must not bypass this.</summary>
        public void Apply(IDocumentAdapter doc, RewriteProposal proposal, string operationName)
        {
            if (proposal == null || !proposal.Validation.IsValid)
                throw new InvalidOperationException("Cannot apply: proposal failed validation.");
            doc.ReplaceBlocks(operationName, proposal.OriginalBlocks, proposal.ProposedBlocks);
            Diagnostics.Log("Applied " + operationName + ": " + proposal.OriginalBlocks.Count + " → " + proposal.ProposedBlocks.Count + " blocks");
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
