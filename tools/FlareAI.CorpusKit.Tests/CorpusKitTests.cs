using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace FlareAI.CorpusKit.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public readonly string Path;
        public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Sub(string name) { return System.IO.Path.Combine(Path, name); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    public class CorpusKitTests
    {
        private static string Sample { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample"); } }

        /// <summary>Imports the 10-topic sample and scaffolds it.</summary>
        private static string Build(TempDir t, string name = "p", double altDrop = 0.5)
        {
            var proj = t.Sub(name);
            Assert.Equal(0, Program.Main(new[] { "import", "--src", Sample, "--out", proj, "--name", "Sample", "--var", "Zorbit=General.ProductName:Acme Orbit", "--alt-drop-rate", altDrop.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            Assert.Equal(0, Program.Main(new[] { "scaffold", "--project", proj }));
            return proj;
        }

        private static XDocument Topic(string proj, string rel) { return Fx.Load(System.IO.Path.Combine(proj, rel.Replace('/', System.IO.Path.DirectorySeparatorChar))); }

        // ---- plumbing ----------------------------------------------------------------------------------------

        [Fact]
        public void Yaml_ReadsListsOfFlowMapsWithRegexAndQuotes()
        {
            var y = Yaml.Parse("version: 1\ndefects:\n  - kind: a\n    rules: ['x:y', z]\n    ops:\n      - {op: replace, find: '\\bto(?= (?:a|b)\\b)', bad: 'in order to', variants: [plain, bold]}\n  - kind: b # comment\n    ops:\n      - {op: append, texts: ['One, two.', 'Three.'], marks: ['One', 'Three']}\n") as Dictionary<string, object>;
            var list = (List<object>)y["defects"];
            Assert.Equal(2, list.Count);
            var a = (Dictionary<string, object>)list[0];
            Assert.Equal(new[] { "x:y", "z" }, Yaml.StrList(a, "rules").ToArray());
            var op = (Dictionary<string, object>)((List<object>)a["ops"])[0];
            Assert.Equal(@"\bto(?= (?:a|b)\b)", (string)op["find"]);
            Assert.Equal(new[] { "plain", "bold" }, Yaml.StrList(op, "variants").ToArray());
            var op2 = (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)list[1])["ops"])[0];
            Assert.Equal(new[] { "One, two.", "Three." }, Yaml.StrList(op2, "texts").ToArray());
        }

        [Fact]
        public void DefaultDefectCatalog_LoadsAndCoversTheV1Taxonomy()
        {
            var kinds = DefectCatalog.Default().Select(k => k.Kind).ToList();
            foreach (var k in new[] { "utilize", "in_order_to", "please", "latin", "exclaim", "will", "we", "passive", "alt_missing", "heading_skip", "click_here", "phone" })
                Assert.Contains(k, kinds);
        }

        [Fact]
        public void Rng_IsStableForASeedAndSalt()
        {
            var a = new Rng(5, "x"); var b = new Rng(5, "x"); var c = new Rng(5, "y");
            var sa = Enumerable.Range(0, 8).Select(_ => a.Next(1000)).ToList();
            Assert.Equal(sa, Enumerable.Range(0, 8).Select(_ => b.Next(1000)).ToList());
            Assert.NotEqual(sa, Enumerable.Range(0, 8).Select(_ => c.Next(1000)).ToList());
        }

        [Fact]
        public void LenientHtml_ClosesImplicitParagraphsItemsAndCells()
        {
            var r = LenientHtml.Parse("<body><p>One<p>Two<ul><li>a<li>b</ul><table><tr><td>x<td>y<tr><td>z</table><br><img src=a.png>");
            Assert.Equal(2, r.Descendants("p").Count());
            Assert.Equal(2, r.Descendants("li").Count());
            Assert.Equal(2, r.Descendants("tr").Count());
            Assert.Equal(3, r.Descendants("td").Count());
            Assert.Equal("a.png", (string)r.Descendants("img").Single().Attribute("src"));
        }

        [Fact]
        public void Markdown_HandlesTablesNestedListsFencesAndAdmonitions()
        {
            var html = Markdown.ToHtml("# T\n\n- a\n  - b\n- c\n\n| h1 | h2 |\n| --- | --- |\n| x | y |\n\n```json\n{\"a\": 1}\n```\n\n:::warning\nCareful.\n:::\n\nUse `code` and **bold** and [link](x.md).");
            Assert.Contains("<h1>T</h1>", html);
            Assert.Contains("<li>a<ul><li>b</li></ul></li>", html.Replace("\n", ""));
            Assert.Contains("<th>h1</th>", html);
            Assert.Contains("<pre class=\"language-json\">", html);
            Assert.Contains("class=\"admonition warning\"", html);
            Assert.Contains("<code>code</code>", html);
            Assert.Contains("<strong>bold</strong>", html);
            Assert.Contains("<a href=\"x.md\">link</a>", html);
        }

        // ---- import ------------------------------------------------------------------------------------------

        [Fact]
        public void Import_MapsStructuresToFlareXhtml()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var install = Topic(proj, "Content/guide/install.htm");
                var body = install.Root.Element("body");
                // admonition div -> p.note without the "Note:" label (the stylesheet adds it)
                var note = body.Elements("p").Single(p => (string)p.Attribute("class") == "note");
                Assert.StartsWith("The installer", note.Value);
                // header row -> thead/th
                Assert.Equal(2, body.Element("table").Element("thead").Elements("tr").Single().Elements("th").Count());
                // span.guilabel -> span.uicontrol
                Assert.Equal("Workers", body.Descendants("span").Single(s => (string)s.Attribute("class") == "uicontrol").Value);
                // external image is not downloaded; local image copied
                Assert.Equal(2, body.Descendants("img").Count());
                Assert.True(File.Exists(System.IO.Path.Combine(proj, "Content", "Resources", "Images", "logo.png")));
                // unclosed li became real items
                Assert.Equal(3, body.Element("ul").Elements("li").Count());

                var quick = Topic(proj, "Content/guide/quickstart.htm").Root.Element("body");
                Assert.Contains(quick.Elements("p"), p => (string)p.Attribute("class") == "tip");
                // bold UI labels in steps stay bold; code fence -> pre
                Assert.Contains(quick.Descendants("b"), b => b.Value == "Workers");
                Assert.Contains("zorbit-agent install", quick.Descendants("pre").First().Value);
                Assert.Equal("language-bash", (string)quick.Descendants("pre").First().Attribute("class"));

                var warn = Topic(proj, "Content/guide/configure.htm").Root.Element("body");
                Assert.Contains(warn.Elements("p"), p => (string)p.Attribute("class") == "warning");
                Assert.Equal("../auth/tokens.htm", (string)warn.Descendants(Fx.MadCap + "xref").Single().Attribute("href"));

                var faq = Topic(proj, "Content/guide/faq.htm").Root.Element("body");
                Assert.Equal("https://example.com/api", (string)faq.Descendants("a").Single().Attribute("href"));

                var trouble = Topic(proj, "Content/guide/troubleshoot.htm").Root.Element("body");
                Assert.Equal(2, trouble.Descendants(Fx.MadCap + "dropDown").Count());
            }
        }

        [Fact]
        public void Import_ExtractsRepeatedBlocksToSnippetsAndNamesToVariables()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var snippets = Directory.GetFiles(System.IO.Path.Combine(proj, "Content", "Resources", "Snippets"), "*.flsnp");
                Assert.Single(snippets);
                var users = Fx.TopicFiles(proj).Where(f => File.ReadAllText(f).Contains("snippetBlock")).ToList();
                Assert.True(users.Count >= 3);
                Assert.Contains("MadCap:variable", File.ReadAllText(snippets[0]));
                var flvar = XDocument.Load(System.IO.Path.Combine(proj, "Project", "VariableSets", "General.flvar"));
                Assert.Equal("Acme Orbit", flvar.Root.Elements("Variable").Single(v => (string)v.Attribute("Name") == "ProductName").Value);
                // the brand name no longer appears as text outside the head title
                foreach (var f in Fx.TopicFiles(proj))
                {
                    var body = Fx.Load(f).Root.Element("body");
                    Assert.DoesNotContain("Zorbit", string.Concat(body.DescendantNodes().OfType<XText>().Where(x => x.Parent.Name.LocalName != "pre").Select(x => x.Value)));
                }
            }
        }

        [Fact]
        public void Import_AltPolicyIsSeedableAndRecorded()
        {
            using (var t = new TempDir())
            {
                var a = Build(t, "a", 1.0);
                var imgs = Topic(a, "Content/guide/install.htm").Descendants("img").ToList();
                Assert.All(imgs, i => Assert.Null(i.Attribute("alt")));
                var m = Manifest.Load(a);
                Assert.True(m.AltDropped.Count >= 2);
                var b = Build(t, "b", 0.0);
                Assert.Contains(Topic(b, "Content/guide/install.htm").Descendants("img"), i => i.Attribute("alt") != null);
            }
        }

        // ---- scaffold ----------------------------------------------------------------------------------------

        [Fact]
        public void Scaffold_WritesProjectSkeleton()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                foreach (var f in new[] { "Sample.flprj", "Project/VariableSets/General.flvar", "Project/ConditionTagSets/Default.flcts", "Project/Glossaries/Glossary.flglo",
                    "Project/TOCs/Main.fltoc", "Project/TOCs/Print.fltoc", "Project/Targets/HTML5.fltar", "Project/Targets/PDF.fltar", "Content/Resources/Stylesheets/Default.css" })
                    Assert.True(File.Exists(System.IO.Path.Combine(proj, f.Replace('/', System.IO.Path.DirectorySeparatorChar))), f);
                var css = File.ReadAllText(System.IO.Path.Combine(proj, "Content", "Resources", "Stylesheets", "Default.css"));
                foreach (var c in new[] { "p.note", "p.warning", "span.uicontrol" }) Assert.Contains(c, css);
                var html5 = XDocument.Load(System.IO.Path.Combine(proj, "Project", "Targets", "HTML5.fltar")).Root;
                Assert.Equal("WebHelp2", (string)html5.Attribute("Type"));
                var toc = XDocument.Load(System.IO.Path.Combine(proj, "Project", "TOCs", "Main.fltoc"));
                Assert.Equal(10, toc.Descendants("TocEntry").Count(e => e.Attribute("Link") != null));
                var tags = XDocument.Load(System.IO.Path.Combine(proj, "Project", "ConditionTagSets", "Default.flcts")).Root.Elements("ConditionTag").Select(e => (string)e.Attribute("Name")).ToList();
                Assert.Equal(new[] { "Print", "ScreenOnly" }, tags);
            }
        }

        [Fact]
        public void Scaffold_DoesNotOverwriteWithoutForce()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var toc = System.IO.Path.Combine(proj, "Project", "TOCs", "Main.fltoc");
                File.WriteAllText(toc, "<CatapultToc Version=\"1\" />");
                Program.Main(new[] { "scaffold", "--project", proj });
                Assert.Equal("<CatapultToc Version=\"1\" />", File.ReadAllText(toc));
                Program.Main(new[] { "scaffold", "--project", proj, "--force" });
                Assert.Contains("TocEntry", File.ReadAllText(toc));
            }
        }

        // ---- check -------------------------------------------------------------------------------------------

        [Fact]
        public void Check_PassesOnAnImportedProject()
        {
            using (var t = new TempDir())
            {
                var r = Checker.Run(Build(t), null);
                Assert.True(r.Ok, string.Join("\n", r.Errors));
                Assert.Equal(10, r.Topics);
            }
        }

        [Fact]
        public void Check_ReportsBrokenReferences()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var f = System.IO.Path.Combine(proj, "Content", "guide", "faq.htm");
                var text = File.ReadAllText(f).Replace("</body>",
                    "<p><MadCap:xref href=\"nope.htm\">x</MadCap:xref><MadCap:snippetBlock src=\"../Resources/Snippets/missing.flsnp\" /><img src=\"missing.png\" /><MadCap:variable name=\"General.Nope\" /></p>" +
                    "<p MadCap:conditions=\"Default.Missing\">y</p></body>");
                File.WriteAllText(f, text);
                File.Delete(System.IO.Path.Combine(proj, "Content", "guide", "install.htm"));
                var r = Checker.Run(proj, null);
                var all = string.Join("\n", r.Errors);
                Assert.Contains("xref 'nope.htm'", all);
                Assert.Contains("snippet", all);
                Assert.Contains("image 'missing.png'", all);
                Assert.Contains("General.Nope", all);
                Assert.Contains("Default.Missing", all);
                Assert.Contains("TOC link", all);   // install.htm was removed but is still in the TOC
            }
        }

        [Fact]
        public void Check_ReportsLabelsThatDoNotAnchorUniquely()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                Program.Main(new[] { "inject", "--project", proj, "--seed", "3", "--density", "0.5", "--project-id", "s" });
                var labelsPath = System.IO.Path.Combine(proj, "labels.jsonl");
                var labels = LabelIO.Read(labelsPath);
                Assert.NotEmpty(labels);
                Assert.True(Checker.Run(proj, null).Ok);

                var withQuote = labels.First(l => l.Quote != null);
                var wrong = new Label { Id = "s/L9001", File = withQuote.File, Block = withQuote.Block, Quote = "text that is not there", Verdict = "violation" };
                var badBlock = new Label { Id = "s/L9002", File = withQuote.File, Block = "/html/body/p[999]", Quote = "x", Verdict = "violation" };
                var ambiguous = new Label { Id = "s/L9003", File = "Content/guide/overview.htm", Block = "/html/body/p[2]", Quote = "a", Verdict = "violation", Prefix = "", Suffix = "" };
                labels.Add(wrong); labels.Add(badBlock); labels.Add(ambiguous);
                LabelIO.Write(labelsPath, labels);
                var all = string.Join("\n", Checker.Run(proj, null).Errors);
                Assert.Contains("L9001", all);
                Assert.Contains("L9002", all);
                Assert.Contains("L9003", all);
            }
        }

        // ---- inject ------------------------------------------------------------------------------------------

        private static string Snapshot(string proj)
        {
            var sb = new StringBuilder();
            foreach (var f in Fx.TopicFiles(proj)) sb.Append(Fx.Rel(proj, f)).Append('\n').Append(File.ReadAllText(f)).Append('\n');
            sb.Append(File.Exists(System.IO.Path.Combine(proj, "labels.jsonl")) ? File.ReadAllText(System.IO.Path.Combine(proj, "labels.jsonl")) : "");
            return sb.ToString();
        }

        [Fact]
        public void Inject_IsSeedable()
        {
            using (var t = new TempDir())
            {
                string[] snaps = new string[3];
                var seeds = new[] { "11", "11", "12" };
                for (int i = 0; i < 3; i++)
                {
                    var proj = Build(t, "p" + i);
                    Assert.Equal(0, Program.Main(new[] { "inject", "--project", proj, "--seed", seeds[i], "--density", "0.4", "--project-id", "sample" }));
                    snaps[i] = Snapshot(proj).Replace(proj, "");
                }
                Assert.Equal(snaps[0], snaps[1]);
                Assert.NotEqual(snaps[0], snaps[2]);
            }
        }

        [Fact]
        public void Inject_WritesExactLabelsThatAnchorUniquely()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var r = Injector.Run(new InjectOptions { Project = proj, Seed = 5, Density = 0.8, ProjectId = "sample" });
                Assert.True(r.Defects >= 15, "defects: " + r.Defects);
                Assert.True(r.ByKind.Count >= 6, "kinds: " + string.Join(",", r.ByKind.Keys));
                var check = Checker.Run(proj, null);
                Assert.True(check.Ok, string.Join("\n", check.Errors));
                var labels = LabelIO.Read(r.LabelsPath);
                Assert.Equal(r.Defects, labels.Count);
                Assert.Equal(labels.Count, labels.Select(l => l.Id).Distinct().Count());
                Assert.All(labels, l =>
                {
                    Assert.Equal("violation", l.Verdict);
                    Assert.Equal("seeded", l.Origin);
                    Assert.NotNull(l.Context);
                    Assert.NotEmpty(l.Context.Line);
                    Assert.Contains(l.Split, new[] { "dev", "test" });
                });
                // the quote is exactly the injected text: it appears in the block, and the label offsets are independent of the engine
                var idx = ProjectIndex.Load(proj);
                foreach (var l in labels.Where(l => l.Quote != null))
                {
                    var doc = Topic(proj, l.File);
                    var el = BlockWalker.Resolve(doc, l.Block);
                    var bi = BlockWalker.Blocks(doc, l.File, idx).Single(b => b.Element == el);
                    Assert.Equal(1, BlockWalker.CountOf(bi.Text, l.Prefix + l.Quote + l.Suffix));
                }
                Assert.Contains(labels, l => l.Quote != null);
            }
        }

        [Fact]
        public void Inject_CoversSurfaceVariantsAcrossSeeds()
        {
            var variants = new HashSet<string>(); var lines = new HashSet<string>(); var spans = new HashSet<string>(); var kinds = new HashSet<string>();
            using (var t = new TempDir())
                for (int seed = 1; seed <= 12; seed++)
                {
                    var proj = Build(t, "s" + seed);
                    var r = Injector.Run(new InjectOptions { Project = proj, Seed = seed, Density = 0.9, ProjectId = "v" });
                    foreach (var k in r.ByVariant.Keys) variants.Add(k);
                    foreach (var k in r.ByLine.Keys) lines.Add(k);
                    foreach (var k in r.BySpan.Keys) spans.Add(k);
                    foreach (var k in r.ByKind.Keys) kinds.Add(k);
                    Assert.True(Checker.Run(proj, null).Ok, "seed " + seed);
                }
            Assert.True(variants.Any(v => v.StartsWith("sentence-start+")), "variants: " + string.Join(",", variants));
            Assert.True(variants.Any(v => v.StartsWith("mid-sentence+")), "variants: " + string.Join(",", variants));
            Assert.True(variants.Any(v => v.EndsWith("+bold")), "variants: " + string.Join(",", variants));
            Assert.True(variants.Any(v => v.EndsWith("+across-inline")), "variants: " + string.Join(",", variants));
            Assert.True(variants.Any(v => v.EndsWith("+link")), "variants: " + string.Join(",", variants));
            Assert.Contains("append", variants);
            Assert.Contains("prepend", variants);
            Assert.Contains("step", lines);
            Assert.Contains("paragraph", lines);
            Assert.True(lines.Count >= 4, string.Join(",", lines));
            Assert.Contains("emphasis", spans);
            Assert.Contains("in-snippet-block", spans);
            Assert.True(kinds.Count >= 15, string.Join(",", kinds));
        }

        [Fact]
        public void Inject_PlacesDefectsNextToVariables()
        {
            using (var t = new TempDir())
            {
                string root; MiniProject(t, out root);
                File.WriteAllText(System.IO.Path.Combine(root, "Content", "v.htm"),
                    "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>V</title></head><body><h1>V</h1>" +
                    "<p>Open <MadCap:variable name=\"General.ProductName\" /> before you start.</p>" +
                    "<p>Wait for <MadCap:variable name=\"General.ProductName\" /> before you continue.</p></body></html>");
                var found = new List<Label>();
                for (int seed = 1; seed <= 8 && found.Count == 0; seed++)
                {
                    var r = Injector.Run(new InjectOptions { Project = root, Out = t.Sub("out" + seed), Seed = seed, Density = 1.0, ProjectId = "n", Kinds = new HashSet<string> { "prior_to" } });
                    found.AddRange(r.NewLabels.Where(l => l.Seed.Variant.EndsWith("+adjacent-variable")));
                }
                Assert.NotEmpty(found);
                Assert.All(found, l => Assert.Contains("variable-adjacent", l.Context.Span));
                Assert.All(found, l => Assert.Equal("prior to", l.Quote.ToLowerInvariant()));
            }
        }

        [Fact]
        public void Inject_OutLeavesTheSourceUntouched()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var before = Snapshot(proj);
                var copy = t.Sub("holdout");
                Assert.Equal(0, Program.Main(new[] { "inject", "--project", proj, "--out", copy, "--seed", "99", "--density", "0.5", "--project-id", "h" }));
                Assert.Equal(before, Snapshot(proj));
                Assert.True(File.Exists(System.IO.Path.Combine(copy, "labels.jsonl")));
                Assert.True(Checker.Run(copy, null).Ok);
            }
        }

        [Fact]
        public void Inject_SecondRunAppendsAndKeepsEarlierLabelsValid()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var r1 = Injector.Run(new InjectOptions { Project = proj, Seed = 1, Density = 0.3, ProjectId = "x" });
                var r2 = Injector.Run(new InjectOptions { Project = proj, Seed = 2, Density = 0.3, ProjectId = "x" });
                var labels = LabelIO.Read(r2.LabelsPath);
                Assert.Equal(r1.Defects + r2.Defects, labels.Count);
                Assert.Equal(labels.Count, labels.Select(l => l.Id).Distinct().Count());
                var c = Checker.Run(proj, null);
                Assert.True(c.Ok, string.Join("\n", c.Errors));
            }
        }

        [Fact]
        public void Inject_RespectsKindFilter()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                var r = Injector.Run(new InjectOptions { Project = proj, Seed = 1, Density = 1.0, ProjectId = "k", Kinds = new HashSet<string> { "please" } });
                Assert.All(r.ByKind.Keys, k => Assert.Equal("please", k));
            }
        }

        // ---- contexts ----------------------------------------------------------------------------------------

        private const string Topic1 =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>T</title></head><body>" +
            "<h2>Heading two</h2>" +
            "<ol><li>Select <b>Save</b> now.</li><li><p>Use <a href=\"https://x.test\">this link</a> and <MadCap:xref href=\"o.htm\">that xref</MadCap:xref>.</p></li></ol>" +
            "<ul><li>Plain item with <code>code</code> and <i>emphasis</i>.</li></ul>" +
            "<p class=\"note\">Remember <MadCap:variable name=\"General.ProductName\" /> first.</p>" +
            "<p MadCap:conditions=\"Default.Print\">Print only text.</p>" +
            "<table><thead><tr><th>Column</th></tr></thead><tbody><tr><td>Cell text</td></tr></tbody></table>" +
            "<p>Say <b>Hello</b> loudly.</p>" +
            "</body></html>";

        private static ProjectIndex MiniProject(TempDir t, out string root)
        {
            root = t.Sub("mini");
            Directory.CreateDirectory(System.IO.Path.Combine(root, "Content"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "Project", "VariableSets"));
            File.WriteAllText(System.IO.Path.Combine(root, "Content", "t.htm"), Topic1);
            File.WriteAllText(System.IO.Path.Combine(root, "Project", "VariableSets", "General.flvar"),
                "<CatapultVariableSet><Variable Name=\"ProductName\" EvaluatedDefinition=\"Acme\">Acme</Variable></CatapultVariableSet>");
            return ProjectIndex.Load(root);
        }

        private static BlockInfo Find(ProjectIndex idx, string root, string startsWith)
        {
            var doc = Fx.Load(System.IO.Path.Combine(root, "Content", "t.htm"));
            return BlockWalker.Blocks(doc, "Content/t.htm", idx).Single(b => b.Text.StartsWith(startsWith, StringComparison.Ordinal));
        }

        private static List<string> Span(ProjectIndex idx, BlockInfo b, string quote)
        {
            int at = b.Text.IndexOf(quote, StringComparison.Ordinal);
            Assert.True(at >= 0, quote);
            return BlockWalker.SpanContexts(b, at, at + quote.Length, idx);
        }

        [Fact]
        public void Contexts_LineContextsComeFromTheDom()
        {
            using (var t = new TempDir())
            {
                string root; var idx = MiniProject(t, out root);
                Assert.Equal(new[] { "heading:h2" }, Find(idx, root, "Heading two").Line.ToArray());
                Assert.Equal(new[] { "step" }, Find(idx, root, "Select Save").Line.ToArray());
                Assert.Equal(new[] { "step" }, Find(idx, root, "Use this link").Line.ToArray());       // p inside an ol item
                Assert.Equal(new[] { "list" }, Find(idx, root, "Plain item").Line.ToArray());
                Assert.Equal(new[] { "note" }, Find(idx, root, "Remember").Line.ToArray());
                Assert.Equal(new[] { "table-header" }, Find(idx, root, "Column").Line.ToArray());
                Assert.Equal(new[] { "table-cell" }, Find(idx, root, "Cell text").Line.ToArray());
                Assert.Equal(new[] { "paragraph" }, Find(idx, root, "Print only").Line.ToArray());
            }
        }

        [Fact]
        public void Contexts_SpanContextsCoverInlineVariableAndConditionCases()
        {
            using (var t = new TempDir())
            {
                string root; var idx = MiniProject(t, out root);
                var step = Find(idx, root, "Select Save");
                Assert.Equal(new[] { "ui-label" }, Span(idx, step, "Save").ToArray());              // bold inside a step
                Assert.Empty(Span(idx, step, "now"));
                var link = Find(idx, root, "Use this link");
                Assert.Equal(new[] { "link-text" }, Span(idx, link, "this link").ToArray());
                Assert.Equal(new[] { "xref" }, Span(idx, link, "that xref").ToArray());
                var plain = Find(idx, root, "Plain item");
                Assert.Equal(new[] { "inline-code" }, Span(idx, plain, "code").ToArray());
                Assert.Equal(new[] { "emphasis" }, Span(idx, plain, "emphasis").ToArray());
                var note = Find(idx, root, "Remember");
                Assert.Contains("Acme", note.Text);                                               // variable resolves to its value
                Assert.Equal(new[] { "variable-adjacent" }, Span(idx, note, "Acme").ToArray());
                Assert.Equal(new[] { "variable-adjacent" }, Span(idx, note, "first").ToArray());
                Assert.Equal(new[] { "conditional" }, Span(idx, Find(idx, root, "Print only"), "Print only text").ToArray());
                // bold outside a step and not after a UI verb is emphasis, not a UI label
                Assert.Equal(new[] { "emphasis" }, Span(idx, Find(idx, root, "Say Hello"), "Hello").ToArray());
            }
        }

        [Fact]
        public void Contexts_MarksSnippetsAndBoilerplate()
        {
            using (var t = new TempDir())
            {
                string root; MiniProject(t, out root);
                var snip = System.IO.Path.Combine(root, "Content", "Resources", "Snippets");
                Directory.CreateDirectory(snip);
                File.WriteAllText(System.IO.Path.Combine(snip, "s.flsnp"), "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head /><body><p>Shared words here.</p></body></html>");
                for (int i = 0; i < 5; i++)
                    File.WriteAllText(System.IO.Path.Combine(root, "Content", "r" + i + ".htm"),
                        "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head /><body><p>Repeated footer line.</p><p>See <MadCap:snippetText src=\"Resources/Snippets/s.flsnp\" />.</p></body></html>");
                var idx = ProjectIndex.Load(root);
                var footer = BlockWalker.Blocks(Fx.Load(System.IO.Path.Combine(root, "Content", "r0.htm")), "Content/r0.htm", idx).First();
                Assert.Contains("boilerplate", BlockWalker.SpanContexts(footer, 0, 8, idx));
                var s = BlockWalker.Blocks(Fx.Load(System.IO.Path.Combine(snip, "s.flsnp")), "Content/Resources/Snippets/s.flsnp", idx).Single();
                Assert.Contains("in-snippet-text", BlockWalker.SpanContexts(s, 0, 6, idx));
            }
        }

        [Fact]
        public void Contexts_UpdateRewritesLabelContexts()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                Program.Main(new[] { "inject", "--project", proj, "--seed", "4", "--density", "0.6", "--project-id", "c" });
                var path = System.IO.Path.Combine(proj, "labels.jsonl");
                var labels = LabelIO.Read(path);
                foreach (var l in labels) l.Context = new LabelContext();
                LabelIO.Write(path, labels);
                Assert.Equal(0, Program.Main(new[] { "contexts", "--project", proj, "--update" }));
                var after = LabelIO.Read(path);
                Assert.All(after, l => Assert.NotEmpty(l.Context.Line));
            }
        }

        [Fact]
        public void Save_NeverChangesInlineText()
        {
            using (var t = new TempDir())
            {
                var path = t.Sub("x.htm");
                var doc = Fx.NewTopic("T", null, new object[] { new XElement("p", new XElement("b", "a"), new XElement("i", "b")), new XElement("ul", new XElement("li", "one"), new XElement("li", "two")) });
                Fx.Save(doc, path);
                var back = Fx.Load(path);
                Assert.Equal("ab", back.Root.Element("body").Element("p").Value);
                Assert.Contains("<ul>", File.ReadAllText(path));
                Assert.Contains("\n    <body>", File.ReadAllText(path));      // block containers are indented
                Fx.Save(back, path);
                var again = File.ReadAllText(path);
                Fx.Save(Fx.Load(path), path);
                Assert.Equal(again, File.ReadAllText(path));                   // idempotent
            }
        }

        // ---- the whole thing ---------------------------------------------------------------------------------

        [Fact]
        public void RoundTrip_TenTopicSampleThroughEveryCommand()
        {
            using (var t = new TempDir())
            {
                var proj = Build(t);
                Assert.Equal(0, Program.Main(new[] { "check", "--project", proj }));
                Assert.Equal(0, Program.Main(new[] { "inject", "--project", proj, "--seed", "20261021", "--density", "0.25", "--project-id", "sample" }));
                Assert.Equal(0, Program.Main(new[] { "contexts", "--project", proj }));
                Assert.True(File.Exists(System.IO.Path.Combine(proj, "contexts.jsonl")));
                Assert.Equal(0, Program.Main(new[] { "contexts", "--project", proj, "--update" }));
                Assert.Equal(0, Program.Main(new[] { "check", "--project", proj }));
                Assert.Equal(10, Fx.TopicFiles(proj).Count(f => f.EndsWith(".htm")));
            }
        }

        [Fact]
        public void Cli_ReportsUsageErrors()
        {
            Assert.Equal(2, Program.Main(new[] { "import" }));
            Assert.Equal(2, Program.Main(new[] { "bogus" }));
        }
    }
}
