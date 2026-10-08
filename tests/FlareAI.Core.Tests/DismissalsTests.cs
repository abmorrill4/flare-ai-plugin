using System;
using System.IO;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class DismissalsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flareai-dis-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

        [Fact]
        public void Dismissal_PersistsAcrossLoads_AndIsPerFileAndText()
        {
            var store = Path.Combine(_root, "dismissed.json");
            var f = new StyleFinding { Rule = "Google.Will", Excerpt = "will", Before = "It " };
            Dismissals.Load(store).Dismiss(f, @"C:\p\A.htm");
            var again = Dismissals.Load(store);
            Assert.True(again.IsDismissed(new StyleFinding { Rule = "Google.Will", Excerpt = "will", Before = "It " }, @"c:\P\a.htm"));
            Assert.False(again.IsDismissed(f, @"C:\p\B.htm"));
            Assert.False(again.IsDismissed(new StyleFinding { Rule = "Google.Will", Excerpt = "will", Before = "They " }, @"C:\p\A.htm"));
        }

        [Fact]
        public void IgnoreRule_WritesTheProjectOverrides_ForLinterAndAiRules()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Content"));
            File.WriteAllText(Path.Combine(_root, "Demo.flprj"), "<CatapultProject />");
            var topic = Path.Combine(_root, @"Content\T.htm");
            ProjectRules.Ignore(topic, new StyleFinding { Rule = "Google.Will", Layer = "google" });
            ProjectRules.Ignore(topic, new StyleFinding { Rule = "Two actions in one step", Layer = "ai" });
            ProjectRules.Ignore(topic, new StyleFinding { Rule = "Google.Will", Layer = "google" });   // idempotent
            var text = File.ReadAllText(ProjectRules.OverridesPath(topic));
            Assert.Equal(1, text.Split(new[] { "Google.Will = NO" }, StringSplitOptions.None).Length - 1);
            Assert.Contains("two actions in one step", ProjectRules.IgnoredAiRules(topic));
        }
    }
}
