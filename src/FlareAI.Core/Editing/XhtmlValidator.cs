using System;
using System.Collections.Generic;
using System.Xml;

namespace FlareAI.Core.Editing
{
    public sealed class ValidationResult
    {
        public bool IsValid { get { return Errors.Count == 0; } }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
        public ValidationResult() { Errors = new List<string>(); Warnings = new List<string>(); }
        public override string ToString() { return string.Join("\n", Errors.ToArray()); }
    }

    /// <summary>
    /// The gate between the model and the DOM. Compares the original blocks with the proposed
    /// replacement and refuses anything that would lose Flare-specific structure.
    /// Fail closed: a validator bug should block an edit, never let a bad one through.
    /// </summary>
    public static class XhtmlValidator
    {
        private static readonly HashSet<string> AllowedHtml = new HashSet<string>(StringComparer.Ordinal)
        {
            "p","h1","h2","h3","h4","h5","h6","ul","ol","li","dl","dt","dd","table","thead","tbody","tfoot","tr","th","td",
            "caption","colgroup","col","div","span","a","img","br","hr","pre","code","em","strong","b","i","u","sup","sub",
            "blockquote","cite","q","abbr","kbd","var","samp","small","del","ins","mark","figure","figcaption","section",
            "article","aside","nav","header","footer","details","summary","object","param","video","audio","source","iframe","tt"
        };

        public static ValidationResult Validate(IList<XmlNode> original, IList<XmlNode> proposed)
        {
            var r = new ValidationResult();
            if (proposed == null || proposed.Count == 0) { r.Errors.Add("Model returned no blocks."); return r; }

            // 1. element vocabulary / namespaces
            foreach (var p in proposed) CheckVocabulary(p, r);

            // 2. Flare structure preserved: every MadCap element and every ref-bearing attribute in the
            //    original must still exist with identical attributes in the proposal.
            var before = Inventory(original);
            var after = Inventory(proposed);
            foreach (var kv in before)
            {
                int have;
                after.TryGetValue(kv.Key, out have);
                if (have < kv.Value) r.Errors.Add("Dropped or altered: " + kv.Key + " (had " + kv.Value + ", now " + have + ")");
            }
            foreach (var kv in after)
            {
                int had;
                before.TryGetValue(kv.Key, out had);
                if (had == 0 && kv.Key.StartsWith("MadCap:", StringComparison.Ordinal))
                    r.Errors.Add("Introduced a MadCap element that was not in the original: " + kv.Key);
            }

            // 3. sanity: wildly different size suggests the model answered in prose instead of editing
            var lenBefore = TextLength(original);
            var lenAfter = TextLength(proposed);
            if (lenBefore > 200 && lenAfter > lenBefore * 3)
                r.Warnings.Add("Replacement is more than 3x the original length (" + lenBefore + " → " + lenAfter + " chars).");
            if (lenBefore > 200 && lenAfter < lenBefore / 4)
                r.Warnings.Add("Replacement is less than a quarter of the original length (" + lenBefore + " → " + lenAfter + " chars).");

            return r;
        }

        private static void CheckVocabulary(XmlNode n, ValidationResult r)
        {
            if (n.NodeType == XmlNodeType.Element)
            {
                var ns = n.NamespaceURI ?? "";
                if (ns == BlockSelector.MadCapNs)
                {
                    // fine — MadCap elements pass vocabulary; inventory check guards their identity
                }
                else if (ns.Length == 0)
                {
                    if (!AllowedHtml.Contains(n.LocalName))
                        r.Errors.Add("Unknown element <" + n.LocalName + ">");
                }
                else
                {
                    r.Errors.Add("Element <" + n.Name + "> in unexpected namespace " + ns);
                }
                foreach (XmlNode c in n.ChildNodes) CheckVocabulary(c, r);
            }
        }

        /// <summary>
        /// Key = element identity we care about preserving. MadCap elements are keyed with all attributes;
        /// plain elements are keyed only when they carry a reference (href/src/name) that must not drift.
        /// </summary>
        private static Dictionary<string, int> Inventory(IList<XmlNode> nodes)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in nodes) Walk(n, d);
            return d;
        }

        private static void Walk(XmlNode n, Dictionary<string, int> d)
        {
            if (n.NodeType != XmlNodeType.Element) return;
            string key = null;
            if (n.NamespaceURI == BlockSelector.MadCapNs)
            {
                key = "MadCap:" + n.LocalName + AttrSignature(n, null);
            }
            else if (n.Attributes != null)
            {
                var sig = AttrSignature(n, new[] { "href", "src", "name", "id" });
                if (sig.Length > 0) key = n.LocalName + sig;
                // Conditions must survive, but may move to a different tag (p → ul restructure).
                var cond = AttrSignature(n, new[] { "MadCap:conditions" });
                if (cond.Length > 0) Count(d, "@" + cond);
            }
            if (key != null) Count(d, key);
            foreach (XmlNode c2 in n.ChildNodes) Walk(c2, d);
        }

        private static void Count(Dictionary<string, int> d, string key)
        {
            int c; d.TryGetValue(key, out c); d[key] = c + 1;
        }

        private static string AttrSignature(XmlNode n, string[] only)
        {
            if (n.Attributes == null || n.Attributes.Count == 0) return "";
            var parts = new List<string>();
            foreach (XmlAttribute a in n.Attributes)
            {
                if (a.Name.StartsWith("xmlns", StringComparison.Ordinal)) continue;
                if (only != null && Array.IndexOf(only, a.Name) < 0) continue;
                parts.Add(a.Name + "=\"" + a.Value + "\"");
            }
            parts.Sort(StringComparer.Ordinal);
            return parts.Count == 0 ? "" : "[" + string.Join(" ", parts.ToArray()) + "]";
        }

        private static int TextLength(IList<XmlNode> nodes)
        {
            var len = 0;
            foreach (var n in nodes) len += (n.InnerText ?? "").Length;
            return len;
        }
    }
}
