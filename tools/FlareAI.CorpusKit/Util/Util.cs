using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    /// <summary>Flare XML helpers: the MadCap namespace, load/save, and path utilities.</summary>
    internal static class Fx
    {
        public static readonly XNamespace MadCap = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";

        public static XDocument Load(string path)
        {
            return XDocument.Load(path, LoadOptions.PreserveWhitespace);
        }

        public static void Save(XDocument doc, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Indent only where children are all elements (body, lists, tables): XmlWriter's own Indent would add whitespace
            // between inline siblings such as <b>a</b><i>b</i>, which changes the text. Text blocks are written untouched.
            var settings = new XmlWriterSettings { Indent = false, Encoding = new UTF8Encoding(false), NewLineChars = "\n", NewLineHandling = NewLineHandling.None };
            using (var w = XmlWriter.Create(path, settings))
            {
                w.WriteStartDocument();
                w.WriteWhitespace("\n");
                if (doc.Root != null) { WriteEl(w, doc.Root, 0); w.WriteWhitespace("\n"); }
                w.WriteEndDocument();
            }
        }

        private static readonly HashSet<string> Pretty = new HashSet<string>(StringComparer.Ordinal)
            { "html", "head", "body", "ul", "ol", "dl", "table", "thead", "tbody", "tfoot", "tr", "MadCap:dropDown", "MadCap:dropDownBody", "MadCap:dropDownHead",
              "CatapultToc", "TocEntry", "CatapultVariableSet", "CatapultConditionTagSet", "CatapultGlossary", "GlossaryEntry", "Terms" };

        private static void WriteEl(XmlWriter w, XElement e, int depth)
        {
            bool pretty = Pretty.Contains(Prefixed(e.Name)) && e.Nodes().All(n => n is XElement || (n is XText && ((XText)n).Value.Trim().Length == 0));
            if (!pretty) { e.WriteTo(w); return; }
            var ns = e.Name.Namespace.NamespaceName;
            w.WriteStartElement(ns.Length == 0 ? null : e.GetPrefixOfNamespace(e.Name.Namespace), e.Name.LocalName, ns.Length == 0 ? null : ns);
            foreach (var a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration)
                {
                    if (a.Name.Namespace == XNamespace.Xmlns) w.WriteAttributeString("xmlns", a.Name.LocalName, null, a.Value);
                    else w.WriteAttributeString("xmlns", a.Value);
                }
                else
                {
                    var ans = a.Name.Namespace.NamespaceName;
                    w.WriteAttributeString(ans.Length == 0 ? null : e.GetPrefixOfNamespace(a.Name.Namespace), a.Name.LocalName, ans.Length == 0 ? null : ans, a.Value);
                }
            }
            var kids = e.Elements().ToList();
            foreach (var k in kids)
            {
                w.WriteWhitespace("\n" + new string(' ', 4 * (depth + 1)));
                WriteEl(w, k, depth + 1);
            }
            if (kids.Count > 0) w.WriteWhitespace("\n" + new string(' ', 4 * depth));
            w.WriteEndElement();
        }

        /// <summary>Name as written in markup: <c>MadCap:xref</c> or <c>p</c>.</summary>
        public static string Prefixed(XName n)
        {
            return n.Namespace == MadCap ? "MadCap:" + n.LocalName : n.LocalName;
        }

        public static XName Name(string prefixed)
        {
            return prefixed.StartsWith("MadCap:", StringComparison.Ordinal) ? MadCap + prefixed.Substring(7) : XName.Get(prefixed);
        }

        public static XElement Mc(string local, params object[] content)
        {
            return new XElement(MadCap + local, content);
        }

        /// <summary>A new topic document in the shape Flare writes.</summary>
        public static XDocument NewTopic(string title, string cssHref, IEnumerable<object> body)
        {
            var head = new XElement("head");
            if (title != null) head.Add(new XElement("title", title));
            if (cssHref != null) head.Add(new XElement("link", new XAttribute("href", cssHref), new XAttribute("rel", "stylesheet"), new XAttribute("type", "text/css")));
            return new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("html", new XAttribute(XNamespace.Xmlns + "MadCap", MadCap.NamespaceName), head, new XElement("body", body)));
        }

        public static string Norm(string p)
        {
            return p.Replace('\\', '/');
        }

        /// <summary>Path of <paramref name="target"/> relative to the directory <paramref name="fromDir"/>, with forward slashes.</summary>
        public static string RelativeTo(string fromDir, string target)
        {
            var a = Norm(fromDir).Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var b = Norm(target).Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            while (i < a.Length && i < b.Length - 1 && string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) i++;
            var parts = new List<string>();
            for (int k = i; k < a.Length; k++) parts.Add("..");
            for (int k = i; k < b.Length; k++) parts.Add(b[k]);
            return string.Join("/", parts);
        }

        /// <summary>Resolves a relative href against the project-relative file path; a leading '/' is project-root relative.</summary>
        public static string Resolve(string fileRel, string href)
        {
            href = Norm(href);
            var stack = new List<string>();
            if (!href.StartsWith("/", StringComparison.Ordinal))
            {
                var dir = Norm(Path.GetDirectoryName(fileRel) ?? "");
                stack.AddRange(dir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
            }
            foreach (var part in href.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                if (part == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                else stack.Add(part);
            }
            return string.Join("/", stack);
        }

        public static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        public static IEnumerable<string> TopicFiles(string projectRoot)
        {
            var content = Path.Combine(projectRoot, "Content");
            if (!Directory.Exists(content)) return new string[0];
            return Directory.EnumerateFiles(content, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Norm(f), StringComparer.Ordinal);
        }

        public static string Rel(string root, string full)
        {
            return Norm(Path.GetFullPath(full).Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length).TrimStart('\\', '/'));
        }

        public static string[] ClassTokens(XElement e)
        {
            var c = (string)e.Attribute("class");
            return c == null ? new string[0] : c.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static XElement Body(XDocument d)
        {
            return d.Root == null ? null : d.Root.Element("body");
        }
    }

    /// <summary>Small deterministic generator (xorshift), stable across runtimes, unlike System.Random.</summary>
    internal sealed class Rng
    {
        private ulong _s;

        public Rng(long seed, string salt = "")
        {
            ulong h = 14695981039346656037UL;
            foreach (var c in salt) { unchecked { h ^= c; h *= 1099511628211UL; } }
            unchecked { _s = (ulong)seed * 6364136223846793005UL + h + 1442695040888963407UL; }
            if (_s == 0) _s = 88172645463325252UL;
            for (int i = 0; i < 4; i++) NextU();
        }

        private ulong NextU()
        {
            _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
            return _s;
        }

        public double NextDouble() { return (NextU() >> 11) / (double)(1UL << 53); }
        public int Next(int n) { return n <= 1 ? 0 : (int)(NextU() % (ulong)n); }
        public T Pick<T>(IList<T> list) { return list[Next(list.Count)]; }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        /// <summary>Stable 0..99 bucket for a string and seed (used for the dev/test split).</summary>
        public static int Bucket(string s, long seed)
        {
            ulong h = 14695981039346656037UL ^ (ulong)seed;
            foreach (var c in s) { unchecked { h ^= c; h *= 1099511628211UL; } }
            return (int)(h % 100);
        }
    }

    /// <summary>Command line: positional command, <c>--key value</c> options (repeatable), and flags.</summary>
    internal sealed class Args
    {
        private readonly Dictionary<string, List<string>> _o = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public string Command;

        public Args(string[] argv)
        {
            for (int i = 0; i < argv.Length; i++)
            {
                var a = argv[i];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    var key = a.Substring(2);
                    string val = "true";
                    var eq = key.IndexOf('=');
                    if (eq >= 0) { val = key.Substring(eq + 1); key = key.Substring(0, eq); }
                    else if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal)) val = argv[++i];
                    List<string> l;
                    if (!_o.TryGetValue(key, out l)) _o[key] = l = new List<string>();
                    l.Add(val);
                }
                else if (Command == null) Command = a;
            }
        }

        public string Get(string key, string def = null)
        {
            List<string> l;
            return _o.TryGetValue(key, out l) ? l[l.Count - 1] : def;
        }

        public IList<string> All(string key)
        {
            List<string> l;
            return _o.TryGetValue(key, out l) ? (IList<string>)l : new List<string>();
        }

        public bool Flag(string key) { return _o.ContainsKey(key); }
        public long Long(string key, long def) { var v = Get(key); long r; return v != null && long.TryParse(v, out r) ? r : def; }

        public double Double(string key, double def)
        {
            var v = Get(key); double r;
            return v != null && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) ? r : def;
        }

        public string Require(string key)
        {
            var v = Get(key);
            if (v == null) throw new UsageException("Missing required option --" + key);
            return v;
        }
    }

    internal sealed class UsageException : Exception
    {
        public UsageException(string m) : base(m) { }
    }
}
