using System;
using System.Collections.Generic;
using System.Linq;

namespace FlareAI.FixtureGen
{
    /// <summary>
    /// The small YAML subset expectations.yaml uses: nested maps by indentation, scalars, and inline flow lists and maps
    /// ("[a, b]", "{ k: v, k2: [x] }"). Comments start with # at the beginning of a line or after a space.
    /// Values come back as Dictionary&lt;string, object&gt; (map), List&lt;object&gt; (list) or string.
    /// </summary>
    public static class MiniYaml
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal);
            var stack = new List<KeyValuePair<int, Dictionary<string, object>>> { new KeyValuePair<int, Dictionary<string, object>>(-1, root) };
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = StripComment(raw).TrimEnd();
                if (line.Trim().Length == 0) continue;
                var indent = line.Length - line.TrimStart().Length;
                var content = line.Trim();
                string key, value;
                var at = content.IndexOf(": ", StringComparison.Ordinal);
                if (at >= 0) { key = content.Substring(0, at).Trim(); value = content.Substring(at + 2).Trim(); }
                else if (content.EndsWith(":")) { key = content.Substring(0, content.Length - 1).Trim(); value = ""; }
                else throw new FormatException("Not 'key: value': " + content);
                while (stack.Count > 1 && stack[stack.Count - 1].Key >= indent) stack.RemoveAt(stack.Count - 1);
                var parent = stack[stack.Count - 1].Value;
                if (value.Length == 0)
                {
                    var child = new Dictionary<string, object>(StringComparer.Ordinal);
                    parent[key] = child;
                    stack.Add(new KeyValuePair<int, Dictionary<string, object>>(indent, child));
                }
                else
                {
                    var i = 0;
                    parent[key] = Flow(value, ref i);
                }
            }
            return root;
        }

        private static string StripComment(string s)
        {
            for (var i = 0; i < s.Length; i++)
                if (s[i] == '#' && (i == 0 || s[i - 1] == ' ')) return s.Substring(0, i);
            return s;
        }

        private static object Flow(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ') i++;
            if (i < s.Length && s[i] == '[')
            {
                i++;
                var list = new List<object>();
                while (true)
                {
                    while (i < s.Length && (s[i] == ' ' || s[i] == ',')) i++;
                    if (i >= s.Length) throw new FormatException("Unclosed [ in: " + s);
                    if (s[i] == ']') { i++; return list; }
                    list.Add(Flow(s, ref i));
                }
            }
            if (i < s.Length && s[i] == '{')
            {
                i++;
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                while (true)
                {
                    while (i < s.Length && (s[i] == ' ' || s[i] == ',')) i++;
                    if (i >= s.Length) throw new FormatException("Unclosed { in: " + s);
                    if (s[i] == '}') { i++; return map; }
                    var colon = s.IndexOf(':', i);
                    if (colon < 0) throw new FormatException("Missing : in: " + s);
                    var key = s.Substring(i, colon - i).Trim();
                    i = colon + 1;
                    map[key] = Flow(s, ref i);
                }
            }
            var start = i;
            while (i < s.Length && s[i] != ',' && s[i] != ']' && s[i] != '}') i++;
            return s.Substring(start, i - start).Trim();
        }

        public static Dictionary<string, object> Map(object o, string key)
        {
            var m = o as Dictionary<string, object>;
            object v;
            return m != null && m.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<string> Strings(object o)
        {
            var l = o as List<object>;
            if (l != null) return l.Select(x => (string)x).ToList();
            var s = o as string;
            return s == null ? new List<string>() : new List<string> { s };
        }
    }
}
