using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FlareAI.Labeler
{
    /// <summary>One entry of <c>corpus/schema/defects.yaml</c>.</summary>
    public sealed class DefectKind
    {
        public string Id, Group, Detect, Status, Summary, Bad;
        public bool Convention;
    }

    /// <summary>
    /// Reader for the defect taxonomy. The file is a flat list of <c>- id:</c> entries with <c>key: value</c> lines, so a
    /// line reader is enough (the scoring tools read it the same way); no YAML dependency.
    /// </summary>
    public sealed class Taxonomy
    {
        private readonly Dictionary<string, DefectKind> _kinds = new Dictionary<string, DefectKind>(StringComparer.Ordinal);
        private readonly List<DefectKind> _order = new List<DefectKind>();

        public IList<DefectKind> All { get { return _order; } }
        public bool Contains(string id) { return id != null && _kinds.ContainsKey(id); }
        public DefectKind Get(string id) { DefectKind k; return id != null && _kinds.TryGetValue(id, out k) ? k : null; }
        public ISet<string> Ids { get { return new HashSet<string>(_kinds.Keys, StringComparer.Ordinal); } }

        public static Taxonomy Load(string path)
        {
            var t = new Taxonomy();
            DefectKind cur = null;
            var idRx = new Regex(@"^-\s*id:\s*(\S+)\s*$");
            var kvRx = new Regex(@"^\s+([a-z_]+):\s*(.*)$");
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var m = idRx.Match(line);
                if (m.Success)
                {
                    cur = new DefectKind { Id = m.Groups[1].Value };
                    t._kinds[cur.Id] = cur;
                    t._order.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                m = kvRx.Match(line);
                if (!m.Success) continue;
                string v = Unquote(m.Groups[2].Value.Trim());
                switch (m.Groups[1].Value)
                {
                    case "group": cur.Group = v; break;
                    case "detect": cur.Detect = v; break;
                    case "status": cur.Status = v; break;
                    case "summary": cur.Summary = v; break;
                    case "bad": cur.Bad = v; break;
                    case "convention": cur.Convention = v == "true"; break;
                }
            }
            return t;
        }

        private static string Unquote(string v)
        {
            if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') return v.Substring(1, v.Length - 2).Replace("''", "'");
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') return v.Substring(1, v.Length - 2).Replace("\\\"", "\"");
            return v;
        }
    }
}
