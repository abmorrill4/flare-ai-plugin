using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// AI review results keyed by a hash of the exact review prompt (fragment + lint summary + nothing else).
    /// Same content → same key, regardless of file name or path, so results survive renames and topic switches
    /// and an unchanged topic never pays for a second review. Holds raw (unanchored) findings; callers anchor
    /// copies against the live DOM. With a path, the cache is saved to disk, so reviews also survive closing Flare.
    /// </summary>
    public sealed class ReviewCache
    {
        private readonly int _capacity;
        private readonly string _path;
        private readonly Dictionary<string, List<StyleFinding>> _items = new Dictionary<string, List<StyleFinding>>(StringComparer.Ordinal);
        private readonly LinkedList<string> _order = new LinkedList<string>();
        private readonly object _gate = new object();

        /// <param name="path">JSON file to load from and save to (the host uses <see cref="Paths.Reviews"/>); null keeps the cache in memory only.</param>
        public ReviewCache(int capacity = 300, string path = null)
        {
            _capacity = capacity;
            _path = path;
            if (path == null || !File.Exists(path)) return;
            try
            {
                var saved = JsonConvert.DeserializeObject<List<KeyValuePair<string, List<StyleFinding>>>>(File.ReadAllText(path, Encoding.UTF8));
                if (saved != null) foreach (var kv in saved) if (kv.Value != null) { _items[kv.Key] = kv.Value; _order.AddLast(kv.Key); }
            }
            catch (Exception ex) { Diagnostics.Log("review cache " + path, ex); }
        }

        public static string Key(string prompt)
        {
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(prompt ?? "")));
        }

        /// <summary>The note the review pane shows when AI is off and earlier AI reviews of the text were hidden.</summary>
        public const string AiOffNote = "AI is off; earlier AI suggestions are hidden.";

        /// <summary>
        /// The cached review to show for <paramref name="prompt"/>: none while AI is off (the writer chose no AI, so earlier AI
        /// suggestions aren't shown), and then <paramref name="hidden"/> says whether there was one.
        /// </summary>
        public List<StyleFinding> Shown(string prompt, bool aiOff, out bool hidden)
        {
            var raw = Get(prompt);
            hidden = aiOff && raw != null;
            return aiOff ? null : raw;
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
                Save();
            }
        }

        private void Save()
        {
            if (_path == null) return;
            try
            {
                var list = new List<KeyValuePair<string, List<StyleFinding>>>();
                foreach (var k in _order) list.Add(new KeyValuePair<string, List<StyleFinding>>(k, _items[k]));
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonConvert.SerializeObject(list), ProjectRules.NoBom);
            }
            catch (Exception ex) { Diagnostics.Log("review cache " + _path, ex); }
        }

        private static StyleFinding Copy(StyleFinding f)
        {
            return new StyleFinding
            {
                Source = f.Source, Rule = f.Rule, Severity = f.Severity, Excerpt = f.Excerpt, Suggestion = f.Suggestion,
                Replacement = f.Replacement, Layer = f.Layer, Link = f.Link, Variable = f.Variable, DocumentPath = f.DocumentPath, RuleId = f.RuleId, RunId = f.RunId
            };
        }
    }
}
