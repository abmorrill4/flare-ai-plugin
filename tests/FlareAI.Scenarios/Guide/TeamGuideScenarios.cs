using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Settings;
using FlareAI.Core.Style;
using FlareAI.TestKit.Llm;
using FlareAI.TestKit.Workspace;
using Xunit;

namespace FlareAI.Scenarios.Guide
{
    /// <summary>
    /// A team guide made the way the docs describe: a copy of the starter project (styleguide/) without its Google folder,
    /// in a shared folder. The defaults bundled with House Style (its Rule/Setting rows) keep applying to it.
    /// </summary>
    public class TeamGuideScenarios
    {
        private const string ThisPage = "google:word-list/221";
        private const string Topic = "Content/help.htm";

        private sealed class Install
        {
            public string PluginDir, Bundled, TeamGuide;
            public PluginSettings Settings = new PluginSettings();
            public StyleEngine Engine;
        }

        /// <summary>The plugin installed read-only, and a team guide copied from the starter project before the shipped defaults existed.</summary>
        private static Install Installed(FakeWorkspace ws, bool emptyCopiedDefaults = true)
        {
            var i = new Install { PluginDir = Path.Combine(ws.Root, @"Plugins\FlareAI") };
            i.Bundled = PluginSettings.BundledGuide(i.PluginDir);
            CopyTree(Repo.StyleGuide, i.Bundled, null);
            ws.MakeReadOnly(i.PluginDir);
            i.TeamGuide = Path.Combine(ws.Root, @"Share\TeamGuide");
            CopyTree(Repo.StyleGuide, i.TeamGuide, @"Content\Resources\StyleGuide\Google\");
            if (emptyCopiedDefaults)
            {
                var copied = Path.Combine(i.TeamGuide, @"Content\Resources\StyleGuide\Company\Google-overrides.htm");
                var x = new XmlDocument { PreserveWhitespace = true };
                x.Load(copied);
                foreach (XmlNode row in x.SelectNodes("//*[local-name()='tr'][*[local-name()='td']]")) row.ParentNode.RemoveChild(row);
                x.Save(copied);
            }
            i.Settings.StyleLayers = i.TeamGuide;
            i.Engine = new StyleEngine(() => i.Settings.GuideRoots(i.PluginDir), i.Bundled);
            return i;
        }

        private static void CopyTree(string from, string to, string skipRel)
        {
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(from.Length + 1);
                if (skipRel != null && rel.StartsWith(skipRel, StringComparison.OrdinalIgnoreCase)) continue;
                var dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f, dest);
            }
        }

        private static List<StyleFinding> Check(FakeWorkspace ws, Install i)
        {
            ws.WriteFile(Topic, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Help</title></head>" +
                                "<body><h1>Help</h1><p>Read this page for details.</p></body></html>");
            var doc = ws.Open(Topic);
            var runner = new ActionRunner(ScriptedLlmClient.NoFindings(), PromptBuilder.For(i.Engine, doc.SourcePath), 16000, i.Engine, doc.SourcePath) { VariableValue = doc.VariableValue };
            return runner.CheckStyle(doc);
        }

        [Fact]
        public void ATeamGuideCopiedFromTheStarter_GetsTheShippedDefaults()
        {
            using (var ws = FakeWorkspace.Empty())
            {
                var i = Installed(ws);
                Assert.True(i.Settings.HasTeamGuide(i.PluginDir));

                Assert.DoesNotContain(Check(ws, i), f => f.RuleId == ThisPage);
                Assert.True(i.Engine.IsOff(null, ThisPage));
                // The catalogue says where the change comes from: House Style's defaults, not the team.
                var rule = RuleCatalog.Build(i.Engine.Compose(null)).Find(ThisPage);
                Assert.Equal(RuleState.Off, rule.State);
                Assert.True(rule.Change.IsDefault);
                // Validate doesn't charge the team's owner with the shipped rows.
                Assert.DoesNotContain(i.Engine.Validate(null).Issues, x => x.Layer == StyleEngine.DefaultsLayer);
            }
        }

        [Fact]
        public void ATeamGuideCopiedWithTheDefaults_StillWorks_AndItsOwnRowWins()
        {
            using (var ws = FakeWorkspace.Empty())
            {
                var i = Installed(ws, emptyCopiedDefaults: false);
                Assert.DoesNotContain(Check(ws, i), f => f.RuleId == ThisPage);

                // The team turns the default back on: its row is later than the defaults, so it wins.
                GuideOverrides.Set(i.TeamGuide, ThisPage, "on", "We want it.", "owner@example.com", new DateTime(2026, 10, 1));
                Assert.Contains(Check(ws, i), f => f.RuleId == ThisPage);
            }
        }

        [Fact]
        public void AnUnreadableTeamGuide_ChecksWithTheBundledGuide_AndSaysWhichFolderAndWhy()
        {
            using (var ws = FakeWorkspace.Empty())
            {
                var i = Installed(ws);
                ws.MakeUnreadable(i.TeamGuide);
                ws.WriteFile(Topic, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html><head><title>Help</title></head><body><h1>Help</h1><p>Utilize this page.</p></body></html>");
                var runner = new ActionRunner(ScriptedLlmClient.NoFindings(), PromptBuilder.For(i.Engine, ws.PathOf(Topic)), 16000, i.Engine, ws.PathOf(Topic));

                // The project check runs (it used to fail with "Access to the path … is denied")…
                var found = runner.LintFiles(new[] { ws.PathOf(Topic) });

                // …with Google's guide and the bundled defaults, and says plainly which folder couldn't be read and why.
                Assert.NotEmpty(found);
                Assert.DoesNotContain(found, f => f.RuleId == ThisPage);
                var notice = runner.GuideNotice;
                Assert.NotNull(notice);
                Assert.StartsWith(StyleEngine.UnreadableNotice, notice);
                Assert.Contains(i.TeamGuide, notice);
                Assert.Contains("permission", notice);
                Assert.Contains("can't be read", File.ReadAllText(ws.DiagLog));

                // Access comes back: the next check uses the team guide again, with no notice.
                ws.ClearReadOnly();
                Assert.Null(runner.GuideNotice);
            }
        }

        [Fact]
        public void TheGuideFolderCheck_RefusesAFolderWithGooglesGuide_AndNamesAnUnreadableOne()
        {
            using (var ws = FakeWorkspace.Empty())
            {
                var i = Installed(ws);
                var bundled = FlareAI.Core.Settings.GuideFolderCheck.Check(i.Bundled, "pat@example.com");
                Assert.False(bundled.Ok);
                Assert.Contains("loaded twice", bundled.Message);
                Assert.True(FlareAI.Core.Settings.GuideFolderCheck.HasGoogleLayer(i.Bundled));
                Assert.False(FlareAI.Core.Settings.GuideFolderCheck.HasGoogleLayer(i.TeamGuide));
                Assert.True(FlareAI.Core.Settings.GuideFolderCheck.Check(i.TeamGuide, "pat@example.com").Ok);

                ws.MakeUnreadable(i.TeamGuide);
                var r = FlareAI.Core.Settings.GuideFolderCheck.Check(i.TeamGuide, "pat@example.com");
                Assert.False(r.Ok);
                Assert.Contains(i.TeamGuide, r.Message);
                Assert.Contains("permission", r.Message);
            }
        }

        [Fact]
        public void APersonalGuidesOwnOverridesTopic_DoesNotHideTheBundledDefaultRows()
        {
            using (var ws = FakeWorkspace.Empty())
            {
                var i = new Install { PluginDir = Path.Combine(ws.Root, @"Plugins\FlareAI") };
                i.Bundled = PluginSettings.BundledGuide(i.PluginDir);
                CopyTree(Repo.StyleGuide, i.Bundled, null);
                ws.MakeReadOnly(i.PluginDir);
                // The writer's own guide has a topic at the bundled overrides topic's path, with a row for another rule.
                var mine = PluginSettings.UserGuidePath;
                GuideSeed.Ensure(i.Bundled, mine);
                var topic = Path.Combine(mine, @"Content\Resources\StyleGuide\Company\Google-overrides.htm");
                Directory.CreateDirectory(Path.GetDirectoryName(topic));
                File.Copy(Path.Combine(i.Bundled, @"Content\Resources\StyleGuide\Company\Google-overrides.htm"), topic);
                File.SetAttributes(topic, FileAttributes.Normal);
                var x = new XmlDocument { PreserveWhitespace = true };
                x.Load(topic);
                foreach (XmlNode row in x.SelectNodes("//*[local-name()='tr'][*[local-name()='td']]")) row.ParentNode.RemoveChild(row);
                x.Save(topic);
                GuideOverrides.Set(mine, "google:words/2", "off", "We say utilize.", "pat@example.com", new DateTime(2026, 9, 1));
                i.Engine = new StyleEngine(() => i.Settings.GuideRoots(i.PluginDir), i.Bundled);

                Assert.DoesNotContain(Check(ws, i), f => f.RuleId == ThisPage);
                Assert.True(i.Engine.IsOff(null, "google:words/2"));
            }
        }
    }
}
