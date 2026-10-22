using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    internal sealed class CheckResult
    {
        public List<string> Errors = new List<string>();
        public int Files, Topics, Labels;
        public bool Ok { get { return Errors.Count == 0; } }
    }

    /// <summary>
    /// Verifies a project hangs together: every file parses, every xref, snippet, image, link, TOC entry and variable resolves,
    /// condition tags exist, and every label's block resolves and its quote (with prefix and suffix) anchors exactly once.
    /// </summary>
    internal static class Checker
    {
        private static readonly Regex External = new Regex(@"^([a-z][a-z0-9+.-]*:|//)", RegexOptions.IgnoreCase);
        private static readonly string[] XmlExt = { ".htm", ".html", ".flsnp", ".fltoc", ".flvar", ".flcts", ".fltar", ".flglo", ".flprj" };

        public static CheckResult Run(string projectRoot, string labelsPath)
        {
            var root = Path.GetFullPath(projectRoot);
            var res = new CheckResult();
            var docs = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Fx.Rel(root, f);
                if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith(".git/")) continue;
                if (!XmlExt.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                res.Files++;
                try { docs[rel] = Fx.Load(f); }
                catch (Exception e) { res.Errors.Add(rel + ": not well-formed: " + e.Message); }
            }

            var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in docs)
            {
                var set = Path.GetFileNameWithoutExtension(kv.Key);
                if (kv.Key.EndsWith(".flvar", StringComparison.OrdinalIgnoreCase))
                    foreach (var v in kv.Value.Root.Elements("Variable")) vars.Add(set + "." + (string)v.Attribute("Name"));
                if (kv.Key.EndsWith(".flcts", StringComparison.OrdinalIgnoreCase))
                    foreach (var t in kv.Value.Root.Elements("ConditionTag")) tags.Add(set + "." + (string)t.Attribute("Name"));
            }

            Func<string, bool> exists = rel => File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            Action<string, string, string> needFile = (from, what, href) =>
            {
                if (string.IsNullOrWhiteSpace(href)) { res.Errors.Add(from + ": empty " + what); return; }
                var path = Uri.UnescapeDataString(Regex.Replace(href, "[#?].*$", ""));
                if (path.Length == 0) return;
                var target = Fx.Resolve(from, path);
                if (!exists(target)) res.Errors.Add(from + ": " + what + " '" + href + "' does not resolve (" + target + ")");
            };

            foreach (var kv in docs)
            {
                var rel = kv.Key; var doc = kv.Value;
                var ext = Path.GetExtension(rel).ToLowerInvariant();
                if (ext == ".htm" || ext == ".html" || ext == ".flsnp")
                {
                    if (ext != ".flsnp") res.Topics++;
                    foreach (var e in doc.Descendants())
                    {
                        var n = Fx.Prefixed(e.Name);
                        switch (n)
                        {
                            case "MadCap:xref": needFile(rel, "xref", (string)e.Attribute("href")); break;
                            case "MadCap:snippetBlock":
                            case "MadCap:snippetText": needFile(rel, "snippet", (string)e.Attribute("src")); break;
                            case "img": needFile(rel, "image", (string)e.Attribute("src")); break;
                            case "a":
                                {
                                    var href = (string)e.Attribute("href");
                                    if (href != null && href.Length > 0 && !href.StartsWith("#") && !External.IsMatch(href)) needFile(rel, "link", href);
                                    break;
                                }
                            case "link":
                                {
                                    var href = (string)e.Attribute("href");
                                    if (href != null && !External.IsMatch(href)) needFile(rel, "stylesheet", href);
                                    break;
                                }
                            case "MadCap:variable":
                                {
                                    var name = (string)e.Attribute("name");
                                    if (name == null || !vars.Contains(name)) res.Errors.Add(rel + ": variable '" + name + "' is not defined");
                                    break;
                                }
                        }
                        var cond = e.Attribute(Fx.MadCap + "conditions");
                        if (cond != null)
                            foreach (var tok in Regex.Split(cond.Value, @"[,\s]+").Where(t => t.Length > 0))
                                if (!tags.Contains(tok)) res.Errors.Add(rel + ": condition tag '" + tok + "' is not defined");
                    }
                }
                else if (ext == ".fltoc")
                {
                    foreach (var te in doc.Descendants("TocEntry"))
                    {
                        var link = (string)te.Attribute("Link");
                        if (link != null) needFile(rel, "TOC link", link);
                    }
                }
                else if (ext == ".fltar")
                {
                    foreach (var attr in new[] { "MasterToc", "StartTopic" })
                    {
                        var v = (string)doc.Root.Attribute(attr);
                        if (v != null) needFile(rel, attr, v);
                    }
                }
            }

            CheckLabels(root, labelsPath ?? Path.Combine(root, "labels.jsonl"), docs, res);
            return res;
        }

        private static void CheckLabels(string root, string labelsPath, Dictionary<string, XDocument> docs, CheckResult res)
        {
            if (!File.Exists(labelsPath)) return;
            List<Label> labels;
            try { labels = LabelIO.Read(labelsPath); }
            catch (Exception e) { res.Errors.Add(Path.GetFileName(labelsPath) + ": cannot read labels: " + e.Message); return; }
            var idx = ProjectIndex.Load(root, false);
            var blocksByFile = new Dictionary<string, Dictionary<XElement, BlockInfo>>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var verdicts = new HashSet<string> { "violation", "clean", "exempt", "ambiguous", "block-complete" };
            foreach (var l in labels)
            {
                res.Labels++;
                var who = "label " + l.Id;
                if (string.IsNullOrEmpty(l.Id) || !ids.Add(l.Id)) { res.Errors.Add(who + ": id missing or duplicated"); continue; }
                if (l.Verdict == null || !verdicts.Contains(l.Verdict)) res.Errors.Add(who + ": unknown label '" + l.Verdict + "'");
                XDocument doc;
                if (l.File == null || !docs.TryGetValue(l.File, out doc)) { res.Errors.Add(who + ": file '" + l.File + "' not found"); continue; }
                var block = BlockWalker.Resolve(doc, l.Block ?? "");
                if (block == null) { res.Errors.Add(who + ": block '" + l.Block + "' does not resolve in " + l.File); continue; }
                if (l.Quote == null) continue;
                Dictionary<XElement, BlockInfo> map;
                if (!blocksByFile.TryGetValue(l.File, out map))
                    blocksByFile[l.File] = map = BlockWalker.Blocks(doc, l.File, idx).ToDictionary(b => b.Element, b => b);
                BlockInfo bi;
                if (!map.TryGetValue(block, out bi)) { res.Errors.Add(who + ": block '" + l.Block + "' has no text"); continue; }
                var probe = (l.Prefix ?? "") + l.Quote + (l.Suffix ?? "");
                int n = BlockWalker.CountOf(bi.Text, probe);
                if (n == 0) res.Errors.Add(who + ": quote '" + l.Quote + "' (with its prefix and suffix) is not in " + l.Block + " of " + l.File);
                else if (n > 1) res.Errors.Add(who + ": quote '" + l.Quote + "' is ambiguous: it matches " + n + " times in " + l.Block + " of " + l.File);
            }
        }
    }
}
