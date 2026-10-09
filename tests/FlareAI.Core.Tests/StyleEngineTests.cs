using System;
using System.IO;
using System.Linq;
using FlareAI.Core.Output;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>The in-process style guide and linter, against the style guide project shipped with the plugin.</summary>
    public class StyleEngineTests
    {
        private static string Repo
        {
            get
            {
                var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
                return d.FullName;
            }
        }

        private static string Guide { get { return Path.Combine(Repo, "styleguide"); } }
        private static StyleEngine Engine() { return new StyleEngine(new[] { Guide }, Guide); }

        private static string TempProject(Action<string> fill)
        {
            var root = Path.Combine(Path.GetTempPath(), "flareai-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Content"));
            File.WriteAllText(Path.Combine(root, "Demo.flprj"), "<CatapultProject />");
            fill(root);
            return root;
        }

        private static void Write(string root, string rel, string text)
        {
            var p = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, text);
        }

        [Fact]
        public void CompanyTables_LintWithExactFixes_AndCitableIds()
        {
            var hits = Engine().Lint(new[] { "Configure Single Sign-On", "In order to be able to utilize SSO, simply ask the user. Please note that we whitelist it!",
                                             "It supports SAML 2.0 and OIDC.", "Customers can contact the bank." }, new[] { 0 }, null);
            Func<int, string, LintHit> by = (i, m) => hits.FirstOrDefault(h => h.Index == i && h.Match == m);
            Assert.Equal("single sign-on", by(0, "Single Sign-On").Replacement);
            Assert.Equal("To", by(1, "In order to").Replacement);
            Assert.Equal("company", by(1, "In order to").Layer);
            Assert.Equal("use", by(1, "utilize").Replacement);
            Assert.Equal("", by(1, "simply ").Replacement);
            Assert.Equal("W", by(1, "Please note that w").Replacement);           // deleting a sentence start re-capitalizes
            Assert.Equal("allowlist", by(1, "whitelist").Replacement);
            Assert.Null(by(1, "the user").Replacement);                           // flag only
            Assert.Equal("Members", by(3, "Customers").Replacement);              // capitalized at a sentence start
            Assert.DoesNotContain(hits, h => h.Index == 2);
            Assert.StartsWith("company:terminology/", by(3, "Customers").RuleId);
            Assert.EndsWith("Terminology.htm", by(3, "Customers").Link);
            Assert.DoesNotContain(Engine().Lint(new[] { "Adds a note that appears in the history." }, null, null), h => h.Match.StartsWith("note that"));
        }

        [Fact]
        public void BundledGuide_PassesItsOwnValidation_AndServesTheLayeredGuide()
        {
            var e = Engine();
            var report = e.Validate(null);
            Assert.True(report.Errors == 0, string.Join("\n", report.Issues.Where(i => i.Level == "error").Select(i => i.File + " " + i.Location + ": " + i.Message).ToArray()));
            Assert.True(report.TableRules >= 8);
            Assert.Equal(report.TableRules, report.TestedByExample);
            var guide = e.Guide(null);
            Assert.Contains("# Layer: google (baseline)", guide);
            Assert.Contains("[company:", guide);
            Assert.Contains("overrides the layers above", guide);
            var rules = e.Rules(null);
            Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct().Count());
            Assert.Contains(rules, r => r.Id == "company:terminology/1" && r.Section == "Audience");
            Assert.NotEmpty(e.Examples(null));
        }

        [Fact]
        public void ProjectLayer_TablesExamplesOverridesVariablesGlossaryAndConditions()
        {
            var root = TempProject(r =>
            {
                Write(r, @"Project\VariableSets\General.flvar", "<CatapultVariableSet><Variable Name=\"ProductName\">Acme Online Banking</Variable><Variable Name=\"Empty\" /></CatapultVariableSet>");
                Write(r, @"Project\Glossaries\Main.flglo", "<CatapultGlossary><GlossaryEntry><Terms><Term>Zelle transfer</Term></Terms><Definition>A person-to-person payment.</Definition></GlossaryEntry></CatapultGlossary>");
                Write(r, @"Content\Resources\StyleGuide\Mobile.htm", @"<html xmlns:MadCap=""http://www.madcapsoftware.com/Schemas/MadCap.xsd""><body><h1>Mobile</h1>
                    <h2>Gestures</h2>
                    <ul><li>Say ""tap"", not ""click"", in mobile app topics.<ul><li>Bad: Click the button.</li><li>Good: Tap the button.</li></ul></li></ul>
                    <table><tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
                      <tr><td>click the button</td><td>tap the button</td><td>warning</td><td>Mobile.</td><td>Then click the button.</td></tr>
                      <tr><td>hit</td><td>(needs rewrite)</td><td>suggestion</td><td>Violent.</td><td>Hit Save.</td></tr>
                      <tr><td>swipe</td><td>swipe</td><td>bogus</td><td></td><td>Swipe left.</td></tr>
                      <tr MadCap:conditions=""Content.Headings""><td>How To</td><td>how to</td><td>warning</td><td>Sentence case.</td><td>How To Pay</td></tr>
                      <tr MadCap:conditions=""Product.Android""><td>tablet</td><td>device</td><td>warning</td><td></td><td>Use a tablet.</td></tr>
                    </table>
                    <table><tr><th>Pattern</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
                      <tr><td>\d+MB\b</td><td>(rewrite)</td><td>suggestion</td><td>Space before the unit.</td><td>Files up to 10MB.</td></tr></table>
                    <table><tr><th>Rule</th><th>Setting</th><th>Reason</th></tr><tr><td>Company.PlainLanguage1</td><td>off</td><td>Release notes.</td></tr></table>
                    </body></html>");
                Write(r, @"Content\Topic.htm", "<html/>");
            });
            try
            {
                var topic = Path.Combine(root, @"Content\Topic.htm");
                var e = Engine();
                var hits = e.Lint(new[] { "Open Acme Online Banking and click the button.", "Send a Zelle Transfer now.", "How To Pay", "Learn How To pay.", "Up to 10MB of data.", "We utilize it." }, new[] { 2 }, topic);
                Assert.Contains(hits, h => h.Match == "Acme Online Banking" && h.Variable == "General.ProductName" && h.Layer == "project");
                Assert.Contains(hits, h => h.Match == "click the button" && h.Replacement == "tap the button" && h.RuleId == "project:mobile/2");
                Assert.Contains(hits, h => h.Match == "Zelle Transfer" && h.Replacement == "Zelle transfer");
                Assert.Contains(hits, h => h.Index == 2 && h.Match == "How To");          // heading-scoped row: on the heading…
                Assert.DoesNotContain(hits, h => h.Index == 3 && h.Match == "How To");    // …not in body text
                Assert.Contains(hits, h => h.Index == 4 && h.Match == "10MB" && h.Replacement == null);   // pattern row
                Assert.DoesNotContain(hits, h => h.Index == 5 && h.Match == "utilize");   // override table turned the company rule off
                Assert.DoesNotContain(e.Lint(new[] { "Open Acme Online Banking." }, null, null), h => h.Layer == "project");

                var rules = e.Rules(topic);
                Assert.Contains(rules, r => r.Id == "project:mobile/5" && r.Applies.Contains("Headings"));
                Assert.Contains(rules, r => r.Id == "project:mobile/6" && r.Products.Contains("Android"));
                Assert.Contains(rules, r => r.Id == "project:mobile/1" && r.Bad == "Click the button." && r.Good == "Tap the button.");
                var guide = e.Guide(topic);
                Assert.Contains("[project:mobile/1] Say \"tap\"", guide);
                Assert.Contains("General.ProductName", guide);
                Assert.Contains("(applies to: Headings)", guide);

                var msgs = e.Validate(topic).Issues.Where(i => i.Layer == "project").Select(i => i.Message).ToList();
                Assert.Contains(msgs, m => m.Contains("Avoid and Use are the same"));   // swipe → swipe
                Assert.Contains(msgs, m => m.Contains("Unknown severity"));
                Assert.Equal(new[] { "google", "company", "project" }, e.Compose(topic).Layers.Select(l => l.Name).ToArray());
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void EditingTheGuide_IsPickedUpWithoutRestart()
        {
            var root = TempProject(r => Write(r, @"Content\Resources\StyleGuide\Words.htm",
                "<html><body><h1>Words</h1><table><tr><th>Avoid</th><th>Use</th><th>Example</th></tr><tr><td>foo</td><td>bar</td><td>A foo.</td></tr></table></body></html>"));
            try
            {
                var e = new StyleEngine(new[] { root }, Guide);
                Assert.Contains(e.Lint(new[] { "A foo and a baz." }, null, null), h => h.Match == "foo");
                System.Threading.Thread.Sleep(20);
                Write(root, @"Content\Resources\StyleGuide\Words.htm",
                    "<html><body><h1>Words</h1><table><tr><th>Avoid</th><th>Use</th><th>Example</th></tr><tr><td>baz</td><td>qux</td><td>A baz.</td></tr></table></body></html>");
                var hits = e.Lint(new[] { "A foo and a baz." }, null, null);
                Assert.Contains(hits, h => h.Match == "baz");
                Assert.DoesNotContain(hits, h => h.Match == "foo");
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void AGuideClonedUnderAnotherFolderName_KeepsItsRuleIds()
        {
            var clone = Path.Combine(Path.GetTempPath(), "Some-Writers-Clone-" + Guid.NewGuid().ToString("N"));
            foreach (var f in Directory.GetFiles(Guide, "*", SearchOption.AllDirectories))
            {
                var target = clone + f.Substring(Guide.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target);
            }
            try { Assert.Contains(new StyleEngine(new[] { clone }, Guide).Rules(null), r => r.Id.StartsWith("company:")); }
            finally { Directory.Delete(clone, true); }
        }

        [Fact]
        public void Mining_FindsTheProjectsOwnConventions_WithVariablesReadAsValues()
        {
            var root = TempProject(r =>
            {
                Write(r, @"Project\VariableSets\General.flvar", "<CatapultVariableSet><Variable Name=\"Bank\">Acme Financial</Variable></CatapultVariableSet>");
                Write(r, @"Content\Alerts.htm", "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body><h1>Set up alerts for your account</h1>" +
                    "<p>Turn on autopay for bills. Autopay runs monthly. Autopay stops when you cancel.</p>" +
                    "<p>At <MadCap:variable name=\"General.Bank\" /> you can also set up auto-pay here. Edit auto-pay later.</p>" +
                    "<p>To start, sign in to the app. Then sign in again and log in to the console. Sign in daily. Log in to see statements.</p>" +
                    "<p>Open the app and go to Online Banking now. From there, go to Online Banking settings and then online banking help, and online banking alerts.</p>" +
                    "<h2>Change Your Alert Settings</h2><h2>View Your Account History</h2><h2>Delete an alert from the list</h2><h2>Reset Your Online Password</h2></body></html>");
            });
            try
            {
                int files;
                var mined = Engine().Mine(Path.Combine(root, @"Content\Alerts.htm"), out files);
                Assert.Equal(1, files);
                var spelling = mined.First(c => c.Kind == "spelling" && c.Prefer.Contains("autopay"));
                Assert.Equal("autopay", spelling.Prefer);
                Assert.Equal("auto-pay", spelling.Avoid);
                Assert.StartsWith("At Acme Financial you", spelling.Example);
                var signin = mined.FirstOrDefault(c => c.Kind == "synonym" && c.Prefer.Contains("sign"));
                Assert.NotNull(signin);
                Assert.Equal("sign in", signin.Prefer);
                Assert.Equal("log in", signin.Avoid);
                Assert.Contains(mined, c => c.Kind == "capitalization");
                Assert.Contains(mined, c => c.Kind == "heading" && c.Prefer == "title case");
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void LlmExport_TurnsBuiltHtmlIntoMarkdownAndLlmsTxt()
        {
            var output = Path.Combine(Path.GetTempPath(), "flareai-out-" + Guid.NewGuid().ToString("N"), "HTML5");
            Write(output, @"Skins\nav.htm", "<html><body>skin</body></html>");
            Write(output, @"Content\pay.htm", "<html><head><title>Pay a bill</title><script>var x=1; if (a<b) {}</script><meta charset=\"utf-8\"></head><body>" +
                "<nav class=\"sidenav\"><a href=\"other.htm\">Menu item</a></nav>" +
                "<div id=\"mc-main-content\"><h1>Pay a bill</h1><p>To pay a bill, select <b>Pay</b> in Digital Banking.<br>Then confirm.</p>" +
                "<ol><li>Choose the payee.</li><li>Enter the amount. See <a href=\"limits.htm#daily\">Payment limits</a>.</li></ol>" +
                "<p><img src=\"x.png\"> <img src=\"y.png\" alt=\"The payee list\"></p>" +
                "<table><tr><th>Limit</th><th>Amount</th></tr><tr><td>Daily</td><td>$5,000</td></tr></table></div>" +
                "<footer>Copyright</footer></body></html>");
            try
            {
                var res = LlmExport.Export(output, null, "https://docs.example.com");
                var md = File.ReadAllText(Path.Combine(output, @"llm\Content\pay.md"));
                Assert.Equal(1, res.Topics);
                Assert.Contains("title: Pay a bill", md);
                Assert.Contains("url: https://docs.example.com/Content/pay.htm", md);
                Assert.Contains("# Pay a bill", md);
                Assert.Contains("select **Pay** in Digital Banking", md);
                Assert.Contains("1. Choose the payee.", md);
                Assert.Contains("2. Enter the amount. See [Payment limits](limits.md)", md);
                Assert.Contains("![The payee list](y.png)", md);
                Assert.Contains("| Limit | Amount |", md);
                Assert.Contains("| Daily | $5,000 |", md);
                Assert.DoesNotContain("Menu item", md);
                Assert.DoesNotContain("Copyright", md);
                Assert.DoesNotContain("var x", md);
                Assert.Contains("- [Pay a bill](llm/Content/pay.md)", File.ReadAllText(Path.Combine(output, "llms.txt")));
                Assert.Contains("Pay a bill", File.ReadAllText(Path.Combine(output, "llms-full.txt")));
            }
            finally { Directory.Delete(Path.GetDirectoryName(output), true); }
        }

        [Fact]
        public void Settings_GuideRoots_DefaultToTheBundledGuide()
        {
            var s = new FlareAI.Core.Settings.PluginSettings();
            Assert.Equal(new[] { @"C:\p\StyleGuide" }, s.GuideRoots(@"C:\p").ToArray());
            s.StyleLayers = "\\\\share\\style\r\n\r\n{PluginDir}\\team\\";
            Assert.Equal(new[] { @"\\share\style", @"C:\p\team" }, s.GuideRoots(@"C:\p").ToArray());
            Assert.Equal(@"C:\p\team", s.GuideRoot(@"C:\p"));
        }
    }
}
