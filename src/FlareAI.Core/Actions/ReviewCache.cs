using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// AI review results keyed by a hash of the exact review prompt (fragment + lint summary + nothing else).
    /// Same content → same key, regardless of file name or path, so results survive renames and topic switches
    /// and an unchanged topic never pays for a second review. Holds raw (unanchored) findings; callers anchor
    /// copies against the live DOM.
    /// </summary>
    public sealed class ReviewCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, List<StyleFinding>> _items = new Dictionary<string, List<StyleFinding>>(StringComparer.Ordinal);
        private readonly LinkedList<string> _order = new LinkedList<string>();
        private readonly object _gate = new object();

        public ReviewCache(int capacity = 300) { _capacity = capacity; }

        public static string Key(string prompt)
        {
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(prompt ?? "")));
        }

        /// <summary>Copies of the cached raw findings, or null.</summary>
        public List<StyleFinding> Get(string prompt)
        {
            lock (_gate)
            {
                List<StyleFinding> raw;
                return _items.TryGetValue(Key(prompt), out raw) ? raw.ConvertAll(Copy) : null;
            }
        }

        public void Put(string prompt, List<StyleFinding> raw)
        {
            var key = Key(prompt);
            lock (_gate)
            {
                if (!_items.ContainsKey(key))
                {
                    _order.AddLast(key);
                    if (_order.Count > _capacity) { _items.Remove(_order.First.Value); _order.RemoveFirst(); }
                }
                _items[key] = raw.ConvertAll(Copy);
            }
        }

        private static StyleFinding Copy(StyleFinding f)
        {
            return new StyleFinding
            {
                Source = f.Source, Rule = f.Rule, Severity = f.Severity, Excerpt = f.Excerpt, Suggestion = f.Suggestion,
                Replacement = f.Replacement, Layer = f.Layer, Link = f.Link, Variable = f.Variable, DocumentPath = f.DocumentPath
            };
        }
    }
}
