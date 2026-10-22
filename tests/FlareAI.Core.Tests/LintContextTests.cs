using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>
    /// Context-aware lint (#53): lines and inline spans carry where they sit, rules fire only in their Content scope, and word
    /// rules stay out of UI labels. Same-span findings (#54): one owner, chosen the same way every time, listing every rule.
    /// </summary>
    public class LintContextTests : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "flareai-lintctx-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { try { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); } catch (IOException) { } }

        private static string Guide
        {
            get
            {
                var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
                return Path.Combine(d.FullName, "styleguide");
            }
        }

        private static StyleEngine Bundled() { return new StyleEngine(new[] { Guide }, Guide); }

        private static List<XmlNode> Blocks(string body)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml("<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body>" + body + "</body></html>");
            return doc.SelectSingleNode("//body").ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();
        }

        private static List<TextLine> Lines(string body) { return TextLines.Of(Blocks(body), null); }

        /// <summary>Every rule id flagging the words <paramref name="match"/> (the shown rule and the ones it lists).</summary>
        private static List<string> RulesOn(StyleEngine e, string body, string match)
        {
            var lines = Lines(body);
            return e.Lint(lines.ConvertAll(l => l.ToLint()), null).Where(h =>
            {
                var at = lines[h.Index].Text.IndexOf(match, StringComparison.OrdinalIgnoreCase);
                return at >= 0 && h.Start < at + match.Length && at < h.Start + h.Length;
            }).SelectMany(h => h.RuleIds).ToList();
        }

        // ---- extraction ----------------------------------------------------------------------------------------------

        [Fact]
        public void Lines_KnowWhereTheySit()
        {
            Func<string, TextContext> ctx = body => Assert.Single(Lines(body)).ToLint().Context;
            Assert.True(ctx("<ol><li>Open the file.</li></ol>").HasFlag(TextContext.Steps));
            Assert.True(ctx("<ol><li><p>Open the file.</p></li></ol>").HasFlag(TextContext.Steps));
            Assert.True(ctx("<ul><li>A bullet.</li></ul>").HasFlag(TextContext.Lists));
            Assert.False(ctx("<ul><li>A bullet.</li></ul>").HasFlag(TextContext.Steps));
            Assert.False(ctx("<p>Plain.</p>").HasFlag(TextContext.Steps));
            Assert.True(ctx("<table><tr><td>Cell.</td></tr></table>").HasFlag(TextContext.TableCell));
            Assert.True(ctx("<table><tr><th>Head</th></tr></table>").HasFlag(TextContext.TableHeader));
            Assert.True(ctx("<p class=\"note\">Careful.</p>").HasFlag(TextContext.Notes));
            Assert.True(ctx("<div class=\"warning\"><p>Careful.</p></div>").HasFlag(TextContext.Notes));
            Assert.True(ctx("<p>Note: careful.</p>").HasFlag(TextContext.Notes));
            Assert.True(ctx("<h2>Title</h2>").HasFlag(TextContext.Heading));
            Assert.True(ctx("<MadCap:dropDown><MadCap:dropDownHead><MadCap:dropDownHotspot>More</MadCap:dropDownHotspot></MadCap:dropDownHead></MadCap:dropDown>").HasFlag(TextContext.Hotspot));
            // A bullet nested in a step is a list item, not a step.
            var nested = Lines("<ol><li>Step.<ul><li>Bullet.</li></ul></li></ol>").Single(l => l.Text == "Bullet.").ToLint().Context;
            Assert.False(nested.HasFlag(TextContext.Steps));
            Assert.True(nested.HasFlag(TextContext.Lists));
        }

        [Fact]
        public void Spans_MarkUiLabelsAndLinkText_AtTheirOffsets()
        {
            var l = Assert.Single(Lines("<p>Click <b>Sign-On Settings</b>, open <span class=\"uicontrol\">Tools</span>, see <a href=\"x.htm\">the topic</a> and <MadCap:xref href=\"y.htm\">this</MadCap:xref>.</p>")).ToLint();
            Func<string, TextContext> at = word => l.Overlapping(l.Text.IndexOf(word, StringComparison.Ordinal), l.Text.IndexOf(word, StringComparison.Ordinal) + word.Length);
            Assert.Equal(TextContext.UI, at("Sign-On Settings"));
            Assert.Equal(TextContext.UI, at("Tools"));
            Assert.Equal(TextContext.Link, at("the topic"));
            Assert.Equal(TextContext.Link, at("this"));
            Assert.Equal(TextContext.None, at("Click"));
        }

        // ---- scope (bundled guide) ----------------------------------------------------------------------------------

        [Fact]
        public void StepsRule_FiresInAStep_NotInAParagraph()
        {
            var e = Bundled();
            const string id = "google:procedures/20";   // "(Optional)" (Content.Steps)
            Assert.Contains(id, RulesOn(e, "<ol><li>(Optional) Select a file.</li></ol>", "(Optional)"));
            Assert.Contains(id, RulesOn(e, "<ol><li><p>(Optional) Select a file.</p></li></ol>", "(Optional)"));
            Assert.DoesNotContain(id, RulesOn(e, "<p>(Optional) Select a file.</p>", "(Optional)"));
            Assert.DoesNotContain(id, RulesOn(e, "<ul><li>(Optional) Select a file.</li></ul>", "(Optional)"));
        }

        [Fact]
        public void ListsRule_FiresInListItems_AndTablesRule_InTables()
        {
            var e = Bundled();
            Func<string, string, bool> fires = (body, id) => e.Lint(Lines(body).ConvertAll(l => l.ToLint()), null).Any(h => h.RuleIds.Contains(id));
            const string lists = "google:lists/36";      // ", etc." (Content.Lists)
            Assert.True(fires("<ul><li>Apples, pears, etc.</li></ul>", lists));
            Assert.True(fires("<ol><li>Apples, pears, etc.</li></ol>", lists));
            Assert.False(fires("<p>Apples, pears, etc.</p>", lists));
            // A Tables rule fires in table text only. (The bundled guide has no Tables lint row since #55/#60: its markup rows
            // became rules for the reviewer, and "in Table 2" is about running text.)
            var tablesScope = new LintScope(new[] { "Tables" }, false);
            Func<string, bool> inTables = body => { var l = Lines(body)[0].ToLint(); return tablesScope.Allows(l, 0, l.Text.Length); };
            Assert.True(inTables("<table><tr><td>The results are in Table 2.</td></tr></table>"));
            Assert.False(inTables("<h2>The results are in Table 2.</h2>"));
            Assert.False(inTables("<p>The results are in Table 2.</p>"));
            const string table2 = "google:tables/22";     // "in Table 2": a reference in running text, wherever it sits (#60)
            Assert.True(fires("<p>The results are in Table 2.</p>", table2));
            Assert.True(fires("<table><tr><td>The results are in Table 2.</td></tr></table>", table2));
        }

        [Fact]
        public void WordRules_SkipUiLabels_ButFireInProse()
        {
            var e = Bundled();
            const string signOn = "google:word-list/640";
            Assert.DoesNotContain(signOn, RulesOn(e, "<ol><li>Click <b>Sign-On Settings</b>.</li></ol>", "sign-on"));
            Assert.DoesNotContain(signOn, RulesOn(e, "<p>Open <span class=\"uicontrol\">Sign-On Settings</span>.</p>", "sign-on"));
            Assert.Contains(signOn, RulesOn(e, "<p>Configure sign-on for your team.</p>", "sign-on"));
            // Link text is prose: rules still apply there.
            Assert.Contains(signOn, RulesOn(e, "<p>See <a href=\"x.htm\">configure sign-on</a>.</p>", "sign-on"));
        }

        [Fact]
        public void UnknownContext_LintsAsBefore()
        {
            // MCP and callers that pass plain text: only Headings scoping applies, so a Steps row fires in any text.
            var hits = Bundled().Lint(new[] { "(Optional) Select a file.", "Click Sign-On Settings." }, null, null);
            Assert.Contains(hits, h => h.Index == 0 && h.RuleIds.Contains("google:procedures/20"));
            Assert.Contains(hits, h => h.Index == 1 && h.RuleIds.Contains("google:word-list/640"));
        }

        [Fact]
        public void Runner_MapsContextHitsBackToTheirRun()
        {
            var runner = new ActionRunner(new FakeLlmClient(r => "[]"), new PromptBuilder(null), 1024, Bundled());
            var blocks = Blocks("<ol><li>(Optional) Click <b>Sign-On Settings</b> to configure sign-on.</li></ol>");
            var findings = runner.Lint(blocks);
            // The step's "(Optional)" is found (Steps scope) on the first run; "sign-on" in the prose run after the label; nothing in the label.
            Assert.Contains(findings, f => f.Excerpt.StartsWith("(Optional)") && f.Before == "" && (f.RuleId == "google:procedures/20" || f.AlsoRules.Any(r => r.Id == "google:procedures/20")));
            Assert.Contains(findings, f => f.Excerpt == "sign-on" && f.Before.EndsWith(" to configure "));
            Assert.DoesNotContain(findings, f => f.Excerpt.Contains("Sign-On"));
        }

        // ---- synthetic scopes ---------------------------------------------------------------------------------------

        private string ScopedGuide(string rows, string owners = null)
        {
            var dir = Path.Combine(_temp, "scoped");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Scoped.htm"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Scoped</title></head><body><h1>Scoped</h1>" +
                "<table><tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>" + rows + "</table></body></html>");
            if (owners != null) File.WriteAllText(Path.Combine(dir, "rule-owners.txt"), owners);
            return dir;
        }

        [Fact]
        public void NotesAndUiScopes_FromConditionTags()
        {
            var g = ScopedGuide(
                "<tr MadCap:conditions=\"Content.Notes\"><td>zorble</td><td>zarble</td><td>warning</td><td></td><td>A zorble.</td></tr>" +
                "<tr MadCap:conditions=\"Content.UI\"><td>Klonk</td><td>Klank</td><td>warning</td><td></td><td>Press Klonk.</td></tr>");
            var e = new StyleEngine(new[] { g }, Guide);
            Func<string, string, bool> fires = (body, word) => e.Lint(Lines(body).ConvertAll(l => l.ToLint()), null).Any(h => h.Match == word);
            Assert.True(fires("<p class=\"note\">A zorble here.</p>", "zorble"));
            Assert.True(fires("<p>Note: a zorble here.</p>", "zorble"));
            Assert.False(fires("<p>A zorble here.</p>", "zorble"));
            // A rule about UI wording applies in UI labels too.
            Assert.True(fires("<p>Click <b>Klonk</b>.</p>", "Klonk"));
            Assert.True(fires("<p>Press Klonk.</p>", "Klonk"));
        }

        // ---- same-span ownership (#54) ---------------------------------------------------------------------------------

        [Fact]
        public void SameSpan_OwnerIsTheCanonicalRule_AndTheSameEveryRun()
        {
            const string rows = "<tr><td>frobnicate</td><td>tweak</td><td>warning</td><td></td><td>Frobnicate it.</td></tr>" +
                                "<tr><td>frobnicate</td><td>adjust</td><td>warning</td><td></td><td>Frobnicate it.</td></tr>";
            var plain = new StyleEngine(new[] { ScopedGuide(rows) }, Guide);
            var ids = plain.Rules(null).Where(r => r.Text.Contains("frobnicate")).Select(r => r.Id).ToList();
            Assert.Equal(2, ids.Count);
            var first = plain.Lint(new[] { "Frobnicate it." }, null, null).Single(h => h.Match == "Frobnicate");
            Assert.Equal(ids[0], first.RuleId);                       // no owner defined: guide order
            Assert.Equal(new[] { ids[0], ids[1] }, first.RuleIds.ToArray());
            for (var i = 0; i < 3; i++) Assert.Equal(ids[0], plain.Lint(new[] { "Frobnicate it." }, null, null).Single(h => h.Match == "Frobnicate").RuleId);

            Directory.Delete(Path.Combine(_temp, "scoped"), true);
            var owned = new StyleEngine(new[] { ScopedGuide(rows, "# canonical owners\n" + ids[1] + "\n") }, Guide);
            var hit = owned.Lint(new[] { "Frobnicate it." }, null, null).Single(h => h.Match == "Frobnicate");
            Assert.Equal(ids[1], hit.RuleId);
            Assert.Equal("Adjust", hit.Replacement);
            Assert.Equal(new[] { ids[1], ids[0] }, hit.RuleIds.ToArray());
        }

        [Fact]
        public void TurningOffEveryRuleOnASpan_LeavesNoHitThere()
        {
            foreach (var text in new[] { "Use a fruit, e.g. a pear.", "Please note that the file is open.", "Pages 10–20 apply." })
            {
                var layers = new List<GuideLayer> { GuideLayer.Load(Guide, "google", "Google"), GuideLayer.Load(Guide, null, null, "Google") };
                var before = new Composition(layers).Lint(new[] { text }, null);
                Assert.NotEmpty(before);
                foreach (var h in before)
                {
                    var off = new List<GuideLayer> { GuideLayer.Load(Guide, "google", "Google"), GuideLayer.Load(Guide, null, null, "Google") };
                    foreach (var id in h.RuleIds) off[1].Overrides.Add(new KeyValuePair<string, string>(id, "off"));
                    var after = new Composition(off).Lint(new[] { text }, null);
                    Assert.DoesNotContain(after, a => a.Start < h.Start + h.Length && h.Start < a.Start + a.Length && a.RuleId != null && !before.Any(b => b != h && b.RuleIds.Contains(a.RuleId)));
                }
            }
        }

        [Fact]
        public void AnInnerHit_PassesItsListedRulesToTheOuterOne()
        {
            // "Please" alone is flagged by more than one rule; inside "Please note that" the longer hit is shown and must list them all.
            var e = Bundled();
            var all = e.Compose(null).Lint(new[] { "Please note that the file is open." }, null, true).Where(h => h.Start == 0).Select(h => h.RuleId).Where(x => x != null).Distinct().ToList();
            var shown = e.Lint(new[] { "Please note that the file is open." }, null, null).Where(h => h.Start == 0).ToList();
            Assert.Single(shown);
            Assert.Empty(all.Except(shown[0].RuleIds));
        }
    }
}
