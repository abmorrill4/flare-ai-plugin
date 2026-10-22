using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    internal sealed class ScaffoldOptions
    {
        public string Project, Name;
        public List<string> Conditions = new List<string> { "Print", "ScreenOnly" };
        public Dictionary<string, string> Glossary = new Dictionary<string, string>();
        public bool Force;
    }

    /// <summary>
    /// Writes the Flare project skeleton around whatever topics exist under Content: .flprj, variable sets, condition tag
    /// set, TOCs, HTML5 and PDF targets, glossary, and a stylesheet with note/warning/uicontrol classes.
    /// Existing topics and snippets are never touched; generated project files are skipped unless <c>Force</c>.
    /// </summary>
    internal static class Scaffolder
    {
        public static List<string> Run(ScaffoldOptions o)
        {
            var root = Path.GetFullPath(o.Project);
            Directory.CreateDirectory(root);
            var manifest = Manifest.Load(root) ?? new Manifest();
            var name = o.Name ?? manifest.Name ?? Path.GetFileName(root);
            var written = new List<string>();

            Action<string, XDocument> put = (rel, doc) =>
            {
                var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full) && !o.Force) return;
                Fx.Save(doc, full); written.Add(rel);
            };
            Action<string, string> putText = (rel, text) =>
            {
                var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full) && !o.Force) return;
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text.Replace("\r\n", "\n"), new UTF8Encoding(false)); written.Add(rel);
            };

            // topics, in a stable order: about first within each folder
            var topics = Fx.TopicFiles(root).Where(f => f.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .Select(f => Fx.Rel(root, f)).Where(r => !r.StartsWith("Content/Resources/", StringComparison.OrdinalIgnoreCase)).ToList();
            var titles = manifest.Topics.ToDictionary(t => t.Path, t => t.Title, StringComparer.OrdinalIgnoreCase);

            put(name + ".flprj", new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("CatapultProject", new XAttribute("Version", "1"))));

            // variables
            var vars = new SortedDictionary<string, string>(manifest.Variables, StringComparer.Ordinal);
            if (!vars.ContainsKey("General.ProductName")) vars["General.ProductName"] = "Acme Product";
            if (!vars.ContainsKey("General.CompanyName")) vars["General.CompanyName"] = "Acme Software";
            foreach (var set in vars.GroupBy(v => v.Key.Substring(0, v.Key.IndexOf('.'))))
                put("Project/VariableSets/" + set.Key + ".flvar", new XDocument(new XDeclaration("1.0", "utf-8", null),
                    new XElement("CatapultVariableSet", set.Select(v => new XElement("Variable",
                        new XAttribute("Name", v.Key.Substring(v.Key.IndexOf('.') + 1)), new XAttribute("EvaluatedDefinition", v.Value), v.Value)))));

            // conditions
            var colors = new[] { "#ffd700", "#7fffd4", "#ffa07a", "#add8e6", "#dda0dd" };
            put("Project/ConditionTagSets/Default.flcts", new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("CatapultConditionTagSet", o.Conditions.Select((c, i) => new XElement("ConditionTag", new XAttribute("Name", c), new XAttribute("BackgroundColor", colors[i % colors.Length]))))));

            // glossary
            var gloss = o.Glossary.Count > 0 ? o.Glossary : new Dictionary<string, string> { { "API", "Application programming interface." }, { "UI", "User interface." } };
            put("Project/Glossaries/Glossary.flglo", new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("CatapultGlossary", new XAttribute("Version", "1"), gloss.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
                    new XElement("GlossaryEntry", new XElement("Terms", new XElement("Term", g.Key)), new XElement("Definition", g.Value))))));

            // TOCs: Main groups by folder; Print is flat
            var groups = topics.GroupBy(t => { var d = Fx.Norm(Path.GetDirectoryName(t)); return d.Length > 8 ? d.Substring(8) : ""; }).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            Func<string, int> rank = t => Path.GetFileNameWithoutExtension(t) == "about" ? 0 : 1;
            var main = new XElement("CatapultToc", new XAttribute("Version", "1"));
            foreach (var g in groups)
            {
                var folder = new XElement("TocEntry", new XAttribute("Title", Title(g.Key)));
                foreach (var t in g.OrderBy(rank).ThenBy(t => t, StringComparer.Ordinal))
                    folder.Add(new XElement("TocEntry", new XAttribute("Title", "[%=System.LinkedTitle%]"), new XAttribute("Link", "/" + t)));
                main.Add(folder);
            }
            put("Project/TOCs/Main.fltoc", new XDocument(new XDeclaration("1.0", "utf-8", null), main));
            var print = new XElement("CatapultToc", new XAttribute("Version", "1"));
            foreach (var t in groups.SelectMany(g => g.OrderBy(rank).ThenBy(x => x, StringComparer.Ordinal)))
                print.Add(new XElement("TocEntry", new XAttribute("Title", "[%=System.LinkedTitle%]"), new XAttribute("Link", "/" + t)));
            put("Project/TOCs/Print.fltoc", new XDocument(new XDeclaration("1.0", "utf-8", null), print));

            // targets
            var start = topics.Count == 0 ? "" : "/" + topics.OrderBy(rank).ThenBy(t => t, StringComparer.Ordinal).First();
            var excludeFirst = o.Conditions.Contains("Print") ? "exclude[Default.Print]" : "";
            var excludeSecond = o.Conditions.Contains("ScreenOnly") ? "exclude[Default.ScreenOnly]" : "";
            put("Project/Targets/HTML5.fltar", new XDocument(new XDeclaration("1.0", "utf-8", null),
                Target("WebHelp2", "/Project/TOCs/Main.fltoc", start, excludeFirst)));
            put("Project/Targets/PDF.fltar", new XDocument(new XDeclaration("1.0", "utf-8", null),
                Target("PDF", "/Project/TOCs/Print.fltoc", start, excludeSecond)));

            putText("Content/Resources/Stylesheets/Default.css", Css);
            return written;
        }

        private static XElement Target(string type, string toc, string start, string expr)
        {
            var t = new XElement("CatapultTarget", new XAttribute("Version", "2"), new XAttribute("Type", type), new XAttribute("MasterToc", toc));
            if (start.Length > 0) t.SetAttributeValue("StartTopic", start);
            if (expr.Length > 0) t.SetAttributeValue("ConditionTagExpression", expr);
            return t;
        }

        private static string Title(string folder)
        {
            var last = folder.Split('/').Last().Replace('-', ' ');
            return last.Length == 0 ? "Topics" : char.ToUpperInvariant(last[0]) + last.Substring(1);
        }

        internal const string Css = @"/* Generated by FlareAI.CorpusKit scaffold. The note and warning labels come from :before, so topics hold no label text. */
body { font-family: Arial, Helvetica, sans-serif; font-size: 11pt; line-height: 1.4; }
h1 { font-size: 20pt; } h2 { font-size: 16pt; } h3 { font-size: 13pt; }
pre { background: #f5f5f5; padding: 6px; font-family: Consolas, monospace; white-space: pre-wrap; }
code, kbd { font-family: Consolas, monospace; }
table { border-collapse: collapse; }
th, td { border: 1px solid #bbb; padding: 3px 6px; vertical-align: top; }
th { background: #eee; text-align: left; }
span.uicontrol { font-weight: bold; }
p.note, p.tip, p.warning, p.caution, p.important { margin: 8px 0; padding: 6px 8px 6px 10px; border-left: 4px solid #888; background: #f7f7f7; }
p.note { border-left-color: #2a7ab8; }
p.tip { border-left-color: #3a9a4d; }
p.warning { border-left-color: #d98e00; }
p.caution { border-left-color: #c0392b; }
p.important { border-left-color: #8e44ad; }
p.note:before { content: 'Note: '; font-weight: bold; }
p.tip:before { content: 'Tip: '; font-weight: bold; }
p.warning:before { content: 'Warning: '; font-weight: bold; }
p.caution:before { content: 'Caution: '; font-weight: bold; }
p.important:before { content: 'Important: '; font-weight: bold; }
";
    }
}
