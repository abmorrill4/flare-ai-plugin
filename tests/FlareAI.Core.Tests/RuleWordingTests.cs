using System;
using System.IO;
using System.Linq;
using FlareAI.Core.Editing;
using FlareAI.Core.Settings;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>Rule names for writers (no regex), remembered window state, and the plugin's own saves.</summary>
    [Collection("Core statics")]
    public class RuleWordingTests
    {
        [Fact]
        public void PatternRule_ShowsItsReason_ElseItsExample_ElseAGenericName()
        {
            var text = @"Avoid the pattern \b\d+\b(?=\s+faster) (rewrite). Say how much faster.";
            Assert.Equal("Say how much faster.", RuleWording.Display(text, "x", null));
            var bare = @"Avoid the pattern (?<![A-Za-z])\.\b (rewrite).";
            Assert.Equal("Avoid this wording pattern (for example: Wait.Now)", RuleWording.Display(bare, "Wait.Now", null));
            Assert.Equal(RuleWording.Generic, RuleWording.Display(bare, "", ""));
            Assert.Equal("Say select.", RuleWording.Display("Say select.", "e", null));
        }

        [Fact]
        public void FromName_MakesInternalNamesReadable()
        {
            Assert.Equal("Plain language 2", RuleWording.FromName("Company.PlainLanguage2"));
            Assert.Equal("Word list 221", RuleWording.FromName("google:word-list/221"));
        }

        [Fact]
        public void RequestTitle_CanShowTheReadableName()
        {
            var r = new FlareAI.Core.Actions.GuideRequest { Kind = FlareAI.Core.Actions.RequestKinds.TurnOff, Rule = "Company.PlainLanguage2" };
            Assert.Equal("Turn off: Company.PlainLanguage2", r.Title);
            Assert.Equal("Turn off: Plain language 2", r.TitleWith(q => RuleWording.FromName(q.Rule)));
        }

        [Fact]
        public void NoCatalogueName_OfTheBundledGuide_ContainsRegexSyntax()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            var guide = Path.Combine(d.FullName, "styleguide");
            var cat = RuleCatalog.Build(new StyleEngine(new[] { guide }, guide).Compose(null));
            Assert.NotEmpty(cat.Rules);
            foreach (var r in cat.Rules)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Title), r.Key);
                foreach (var bad in new[] { @"\b", "(?", "[A-Z", @"\d", @"\s" })
                    Assert.False(r.Title.Contains(bad), r.Key + ": " + r.Title);
            }
        }

        [Fact]
        public void UiState_RemembersWindowAndColumns_AndFitsTheScreen()
        {
            var old = Paths.Root;
            var temp = Path.Combine(Path.GetTempPath(), "flareai-ui-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Paths.Root = temp;
            try
            {
                Assert.Null(UiState.Load("W"));
                UiState.Save("W", new WindowState { X = 10, Y = 20, Width = 1200, Height = 800, Maximized = true, Columns = new System.Collections.Generic.List<int> { 1, 2, 3, 4 } });
                var s = UiState.Load("W");
                Assert.True(s.Maximized);
                Assert.Equal(new[] { 1, 2, 3, 4 }, s.Columns.ToArray());
                int w, h;
                UiState.Fit(null, 1600, 1000, 1920, 1040, out w, out h);
                Assert.Equal(1280, w); Assert.Equal(800, h);
                UiState.Fit(null, 800, 500, 1920, 1040, out w, out h);
                Assert.Equal(900, w); Assert.Equal(600, h);
                UiState.Fit(null, 800, 500, 1000, 560, out w, out h); Assert.Equal(900, w); UiState.Fit(null, 800, 500, 840, 560, out w, out h);   // a small screen: never larger than it
                Assert.Equal(840, w); Assert.Equal(560, h);
                Assert.False(UiState.IsVisible(s, new[] { new[] { 3000, 0, 1920, 1080 } }));   // its screen is gone
                Assert.True(UiState.IsVisible(s, new[] { new[] { 0, 0, 1920, 1080 } }));
            }
            finally { Paths.Root = old; try { Directory.Delete(temp, true); } catch (Exception) { } }
        }

        [Fact]
        public void PluginsOwnSave_IsRecognised_ForAMoment()
        {
            var path = @"C:\p\add-a-theme.htm";
            Assert.False(SelfSaves.IsOwn(path));
            SelfSaves.Mark(path);
            Assert.True(SelfSaves.IsOwn(path.ToUpperInvariant()));
            Assert.False(SelfSaves.IsOwn(@"C:\p\other.htm"));
        }
    }
}
