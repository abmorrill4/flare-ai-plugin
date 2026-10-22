using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlareAI.CorpusKit
{
    /// <summary>
    /// A deliberately small YAML subset reader for defects.yaml: block maps, block lists (including lists of maps),
    /// flow lists <c>[a, b]</c>, plain / single-quoted / double-quoted scalars, and # comments. No anchors, no multi-line
    /// scalars, no tabs. Results are Dictionary&lt;string, object&gt;, List&lt;object&gt; and string.
    /// </summary>
    internal static class Yaml
    {
        private sealed class Line { public int Indent; public string Text; public int No; }

        public static object Parse(string text)
        {
            var lines = new List<Line>();
            int no = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                no++;
                var stripped = StripComment(raw).TrimEnd();
                if (stripped.Trim().Length == 0) continue;
                if (stripped.Contains("\t")) throw new FormatException("YAML line " + no + ": tabs are not supported");
                int ind = stripped.Length - stripped.TrimStart().Length;
                lines.Add(new Line { Indent = ind, Text = stripped.TrimStart(), No = no });
            }
            if (lines.Count == 0) return new Dictionary<string, object>();
            int pos = 0;
            return Block(lines, ref pos, lines[0].Indent);
        }

        private static string StripComment(string s)
        {
            char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == q) q = '\0'; continue; }
                if (c == '\'' || c == '"') { if (i == 0 || s[i - 1] == ' ' || s[i - 1] == '[' || s[i - 1] == ',' || s[i - 1] == ':') q = c; continue; }
                if (c == '#' && (i == 0 || s[i - 1] == ' ')) return s.Substring(0, i);
            }
            return s;
        }

        private static object Block(List<Line> lines, ref int pos, int indent)
        {
            if (lines[pos].Text == "-" || lines[pos].Text.StartsWith("- ", StringComparison.Ordinal))
                return ListBlock(lines, ref pos, indent);
            return MapBlock(lines, ref pos, indent);
        }

        private static List<object> ListBlock(List<Line> lines, ref int pos, int indent)
        {
            var list = new List<object>();
            while (pos < lines.Count && lines[pos].Indent == indent && (lines[pos].Text == "-" || lines[pos].Text.StartsWith("- ", StringComparison.Ordinal)))
            {
                var ln = lines[pos];
                var rest = ln.Text.Length > 1 ? ln.Text.Substring(2).TrimStart() : "";
                if (rest.Length == 0)
                {
                    pos++;
                    if (pos < lines.Count && lines[pos].Indent > indent) list.Add(Block(lines, ref pos, lines[pos].Indent));
                    else list.Add(null);
                }
                else if (LooksLikeKey(rest))
                {
                    // treat "- key: v" as a map whose first key sits two columns in
                    int mapIndent = indent + (ln.Text.Length - rest.Length);
                    lines[pos] = new Line { Indent = mapIndent, Text = rest, No = ln.No };
                    list.Add(MapBlock(lines, ref pos, mapIndent));
                }
                else { list.Add(Scalar(rest, ln.No)); pos++; }
            }
            return list;
        }

        private static Dictionary<string, object> MapBlock(List<Line> lines, ref int pos, int indent)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            while (pos < lines.Count && lines[pos].Indent == indent && !(lines[pos].Text == "-" || lines[pos].Text.StartsWith("- ", StringComparison.Ordinal)))
            {
                var ln = lines[pos];
                int colon = KeyEnd(ln.Text);
                if (colon < 0) throw new FormatException("YAML line " + ln.No + ": expected 'key: value'");
                var key = Unquote(ln.Text.Substring(0, colon).Trim());
                var val = ln.Text.Substring(colon + 1).Trim();
                pos++;
                if (val.Length == 0)
                {
                    if (pos < lines.Count && (lines[pos].Indent > indent || (lines[pos].Indent == indent && lines[pos].Text.StartsWith("- ", StringComparison.Ordinal))))
                        map[key] = Block(lines, ref pos, lines[pos].Indent);
                    else map[key] = null;
                }
                else map[key] = Scalar(val, ln.No);
            }
            return map;
        }

        private static bool LooksLikeKey(string s) { return KeyEnd(s) >= 0 && !s.StartsWith("[", StringComparison.Ordinal) && !s.StartsWith("{", StringComparison.Ordinal); }

        /// <summary>Index of the ':' that ends a key (followed by space or end), outside quotes.</summary>
        private static int KeyEnd(string s)
        {
            char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == q) q = '\0'; continue; }
                if ((c == '\'' || c == '"') && i == 0) { q = c; continue; }
                if (c == ':' && (i + 1 == s.Length || s[i + 1] == ' ')) return i;
            }
            return -1;
        }

        private static List<string> SplitFlow(string inner)
        {
            var parts = new List<string>();
            var cur = new StringBuilder();
            char q = '\0';
            int depth = 0;
            foreach (var c in inner)
            {
                if (q != '\0') { cur.Append(c); if (c == q) q = '\0'; continue; }
                if (c == '\'' || c == '"') { q = c; cur.Append(c); continue; }
                if (c == '[' || c == '{') depth++;
                if (c == ']' || c == '}') depth--;
                if (c == ',' && depth == 0) { parts.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(c);
            }
            parts.Add(cur.ToString());
            return parts;
        }

        private static object Scalar(string s, int no)
        {
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                if (!s.EndsWith("]", StringComparison.Ordinal)) throw new FormatException("YAML line " + no + ": unterminated flow list");
                var items = new List<object>();
                foreach (var part in SplitFlow(s.Substring(1, s.Length - 2)))
                    if (part.Trim().Length > 0) items.Add(Scalar(part.Trim(), no));
                return items;
            }
            if (s.StartsWith("{", StringComparison.Ordinal))
            {
                if (!s.EndsWith("}", StringComparison.Ordinal)) throw new FormatException("YAML line " + no + ": unterminated flow map");
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var part in SplitFlow(s.Substring(1, s.Length - 2)))
                {
                    var p = part.Trim();
                    if (p.Length == 0) continue;
                    int colon = KeyEnd(p);
                    if (colon < 0) throw new FormatException("YAML line " + no + ": expected 'key: value' in flow map");
                    map[Unquote(p.Substring(0, colon).Trim())] = Scalar(p.Substring(colon + 1).Trim(), no);
                }
                return map;
            }
            return Unquote(s);
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'') return s.Substring(1, s.Length - 2).Replace("''", "'");
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                var sb = new StringBuilder();
                for (int i = 1; i < s.Length - 1; i++)
                {
                    if (s[i] == '\\' && i + 1 < s.Length - 1)
                    {
                        i++;
                        sb.Append(s[i] == 'n' ? '\n' : s[i] == 't' ? '\t' : s[i]);
                    }
                    else sb.Append(s[i]);
                }
                return sb.ToString();
            }
            return s;
        }

        // typed accessors
        public static string Str(IDictionary<string, object> m, string key) { object v; return m.TryGetValue(key, out v) ? v as string : null; }

        public static List<string> StrList(IDictionary<string, object> m, string key)
        {
            object v;
            if (!m.TryGetValue(key, out v) || v == null) return new List<string>();
            var l = v as List<object>;
            if (l != null) return l.OfType<string>().ToList();
            var s = v as string;
            return s == null ? new List<string>() : new List<string> { s };
        }
    }
}
