using System;
using System.Collections.Generic;
using System.Linq;

namespace FlareAI.Scoring
{
    /// <summary>Checks labels against the rules in <c>corpus/schema/labels.schema.json</c> (kept in step by a test) and, optionally, against the corpus text.</summary>
    public static class LabelValidator
    {
        /// <param name="knownKinds">Defect taxonomy ids (from defects.yaml); null skips the taxonomy check.</param>
        /// <param name="textSource">When given, every quote must resolve in its block, and uniquely.</param>
        public static List<string> Validate(IEnumerable<Label> labels, ISet<string> knownKinds = null, IBlockTextSource textSource = null)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in labels)
            {
                string at = (l.Id ?? "(no id)") + ": ";
                if (string.IsNullOrEmpty(l.Id) || l.Id.IndexOf('/') <= 0) errors.Add(at + "id must be <project>/<name>");
                else if (!ids.Add(l.Id)) errors.Add(at + "duplicate id");
                if (string.IsNullOrEmpty(l.File)) errors.Add(at + "file is required");
                else if (l.File.IndexOf('\\') >= 0 || l.File.StartsWith("/", StringComparison.Ordinal)) errors.Add(at + "file must be relative with forward slashes");
                if (string.IsNullOrEmpty(l.Block) || l.Block[0] != '/') errors.Add(at + "block must be an absolute path starting with /");
                if (Array.IndexOf(LabelKind.All, l.Kind) < 0) errors.Add(at + "label must be one of " + string.Join(", ", LabelKind.All));
                if (l.Rules == null || l.Rules.Count == 0 || l.Rules.Any(string.IsNullOrWhiteSpace)) errors.Add(at + "rules must list at least one rule id, family or *");
                if (Array.IndexOf(ContextVocabulary.Origin, l.Origin) < 0) errors.Add(at + "origin must be seeded or natural");
                if (Array.IndexOf(ContextVocabulary.Status, l.Status) < 0) errors.Add(at + "status must be proposed, verified or disputed");
                if (Array.IndexOf(ContextVocabulary.Split, l.Split) < 0) errors.Add(at + "split must be dev or test");
                if (l.By == null || l.By.Count == 0) errors.Add(at + "by must name at least one source");
                if (l.Kind == LabelKind.Violation)
                {
                    if (string.IsNullOrEmpty(l.DefectKind)) errors.Add(at + "violation needs a kind");
                    else if (knownKinds != null && !knownKinds.Contains(l.DefectKind)) errors.Add(at + "kind '" + l.DefectKind + "' is not in defects.yaml");
                }
                if (l.Kind == LabelKind.BlockComplete && l.Quote != null) errors.Add(at + "block-complete labels cover a block and take no quote");
                if ((l.Kind == LabelKind.Clean || l.Kind == LabelKind.Exempt) && string.IsNullOrEmpty(l.Reason)) errors.Add(at + "clean/exempt labels need a reason");
                if ((l.Prefix != null || l.Suffix != null) && l.Quote == null) errors.Add(at + "prefix/suffix need a quote");
                if (l.Quote != null && l.Quote.Length == 0) errors.Add(at + "quote must not be empty");
                if (l.Context != null)
                {
                    foreach (var c in l.Context.Line) if (Array.IndexOf(ContextVocabulary.Line, c) < 0) errors.Add(at + "unknown context.line '" + c + "'");
                    foreach (var c in l.Context.Span) if (Array.IndexOf(ContextVocabulary.Span, c) < 0) errors.Add(at + "unknown context.span '" + c + "'");
                }
                if (textSource != null && l.Block != null && l.Quote != null)
                {
                    string text = textSource.GetText(l.File, l.Block);
                    if (text == null) errors.Add(at + "block not found: " + l.File + " " + l.Block);
                    else
                    {
                        var r = Anchor.Resolve(text, l.Quote, l.Prefix, l.Suffix);
                        if (!r.Resolved) errors.Add(at + "quote not found in block");
                        else if (r.Ambiguous) errors.Add(at + "quote is ambiguous in block: add prefix/suffix");
                    }
                }
                else if (textSource != null && l.Block != null && textSource.GetText(l.File, l.Block) == null)
                    errors.Add(at + "block not found: " + l.File + " " + l.Block);
            }
            return errors;
        }
    }
}
