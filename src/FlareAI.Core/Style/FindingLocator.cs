using System;
using System.Collections.Generic;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// Ties findings to text in the live DOM. Findings remember text + context, not node references, because
    /// every tracked edit splits or replaces text nodes; we re-locate right before Go to / Fix.
    /// </summary>
    public static class FindingLocator
    {
        private const int ContextChars = 40;

        public static StyleFinding FromLint(LintHit h, XmlText run)
        {
            return new StyleFinding
            {
                Source = "lint", Rule = h.Rule, Severity = h.Severity, Suggestion = h.Message,
                Excerpt = h.Match, Replacement = h.Replacement, Layer = h.Layer, Link = h.Link,
                Before = Tail(run.Value.Substring(0, h.Start)), Block = TextRuns.BlockOf(run)
            };
        }

        /// <summary>Anchors a model finding to the runs it quotes. False when the excerpt is not verbatim in one run.</summary>
        public static bool Anchor(StyleFinding f, IList<XmlText> runs)
        {
            if (string.IsNullOrEmpty(f.Excerpt)) return false;
            foreach (var run in runs)
            {
                var i = run.Value.IndexOf(f.Excerpt, StringComparison.Ordinal);
                if (i < 0) continue;
                f.Before = Tail(run.Value.Substring(0, i));
                f.Block = TextRuns.BlockOf(run);
                return true;
            }
            return false;
        }

        /// <summary>Finds the finding's text in the current DOM, preferring its original block and context.</summary>
        public static bool TryLocate(XmlDocument doc, StyleFinding f, out XmlText run, out int start)
        {
            run = null; start = -1;
            var blocks = new List<XmlNode>();
            if (f.Block != null && TextRuns.BlockOf(f.Block) == f.Block) blocks.Add(f.Block); // still attached
            else
            {
                var body = BlockSelector.FindBody(doc);
                if (body == null) return false;
                foreach (XmlNode c in body.ChildNodes) if (c.NodeType == XmlNodeType.Element) blocks.Add(c);
            }

            XmlText fallback = null; var fallbackAt = -1;
            foreach (var r in TextRuns.Of(blocks))
            {
                for (var i = r.Value.IndexOf(f.Excerpt, StringComparison.Ordinal); i >= 0;
                     i = r.Value.IndexOf(f.Excerpt, i + 1, StringComparison.Ordinal))
                {
                    if (Tail(r.Value.Substring(0, i)) == (f.Before ?? "")) { run = r; start = i; return true; }
                    if (fallback == null) { fallback = r; fallbackAt = i; }
                }
            }
            // Earlier fixes in the same run change the context; the first occurrence in the block is the best guess.
            run = fallback; start = fallbackAt;
            return run != null;
        }

        private static string Tail(string s)
        {
            return s.Length <= ContextChars ? s : s.Substring(s.Length - ContextChars);
        }
    }
}
