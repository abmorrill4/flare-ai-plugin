using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace FlareAI.Scoring
{
    /// <summary>Supplies the plain text of a block (see CORPUS.md) so text-quote selectors can be resolved to offsets.</summary>
    public interface IBlockTextSource
    {
        /// <summary>Plain text of the element at <paramref name="xpath"/> in <paramref name="file"/>, or null when it cannot be read.</summary>
        string GetText(string file, string xpath);
    }

    /// <summary>Reads blocks straight from a corpus project folder. Variables expand to the project's variable values when given.</summary>
    public sealed class CorpusBlockTextSource : IBlockTextSource
    {
        private readonly string _root;
        private readonly Func<string, string> _variables;
        private readonly Dictionary<string, XmlDocument> _docs = new Dictionary<string, XmlDocument>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        public CorpusBlockTextSource(string projectRoot, Func<string, string> variables = null)
        {
            _root = projectRoot;
            _variables = variables;
        }

        public string GetText(string file, string xpath)
        {
            string key = file + "\n" + xpath;
            string t;
            if (_cache.TryGetValue(key, out t)) return t;
            XmlDocument doc;
            if (!_docs.TryGetValue(file, out doc))
            {
                string path = Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar));
                doc = File.Exists(path) ? Xhtml.Load(path) : null;
                _docs[file] = doc;
            }
            XmlElement el = doc == null ? null : Xhtml.Resolve(doc, xpath);
            t = el == null ? null : Xhtml.Extract(el, _variables).Text;
            _cache[key] = t;
            return t;
        }
    }

    /// <summary>In-memory source, mainly for tests: key is <c>file|xpath</c>.</summary>
    public sealed class DictionaryBlockTextSource : IBlockTextSource
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
        public DictionaryBlockTextSource Add(string file, string xpath, string text) { _map[file + "|" + xpath] = text; return this; }
        public string GetText(string file, string xpath)
        {
            string t;
            return _map.TryGetValue(file + "|" + xpath, out t) ? t : null;
        }
    }

    /// <summary>A label's selector resolved against its block's text.</summary>
    public struct ResolvedSpan
    {
        public int Start, End;
        public bool Resolved;
        /// <summary>True when the quote occurs more than once and prefix/suffix did not single out one occurrence.</summary>
        public bool Ambiguous;
    }

    public static class Anchor
    {
        /// <summary>
        /// Resolves a text-quote selector in <paramref name="blockText"/>. Every occurrence of the quote is a candidate; the score is the number of
        /// prefix characters (counted backwards) and suffix characters (counted forwards) that agree, and the best candidate wins.
        /// A tie between candidates means the selector is ambiguous: the first is returned with <c>Ambiguous</c> set.
        /// Without a quote the whole text is selected.
        /// </summary>
        public static ResolvedSpan Resolve(string blockText, string quote, string prefix, string suffix)
        {
            var r = new ResolvedSpan();
            if (blockText == null) return r;
            if (string.IsNullOrEmpty(quote)) { r.Start = 0; r.End = blockText.Length; r.Resolved = true; return r; }
            int best = -1, bestScore = -1, count = 0, tied = 0;
            int from = 0;
            while (true)
            {
                int i = blockText.IndexOf(quote, from, StringComparison.Ordinal);
                if (i < 0) break;
                count++;
                int score = 0;
                if (!string.IsNullOrEmpty(prefix))
                    for (int k = 1; k <= prefix.Length && i - k >= 0 && blockText[i - k] == prefix[prefix.Length - k]; k++) score++;
                if (!string.IsNullOrEmpty(suffix))
                    for (int k = 0; k < suffix.Length && i + quote.Length + k < blockText.Length && blockText[i + quote.Length + k] == suffix[k]; k++) score++;
                if (score > bestScore) { bestScore = score; best = i; tied = 1; }
                else if (score == bestScore) tied++;
                from = i + 1;
            }
            if (count == 0) return r;
            r.Start = best;
            r.End = best + quote.Length;
            r.Resolved = true;
            r.Ambiguous = tied > 1;
            return r;
        }

        /// <summary>Builds prefix/suffix (up to <paramref name="context"/> characters) for the selection [start, end) of a block text.</summary>
        public static void MakeSelector(string blockText, int start, int end, int context, out string quote, out string prefix, out string suffix)
        {
            quote = blockText.Substring(start, end - start);
            prefix = blockText.Substring(Math.Max(0, start - context), start - Math.Max(0, start - context));
            suffix = blockText.Substring(end, Math.Min(context, blockText.Length - end));
        }
    }
}
