using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>A fix must never land on a different occurrence than the one the finding was about.</summary>
    public class StaleFindingTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "flareai-stale-" + Guid.NewGuid().ToString("N") + ".htm");

        public void Dispose() { try { File.Delete(_path); } catch (IOException) { } }

        private FakeDocumentAdapter Open(string paragraph)
        {
            File.WriteAllText(_path, "<html><head /><body><p>" + paragraph + "</p></body></html>");
            return new FakeDocumentAdapter(_path, true);
        }

        private static ActionRunner Runner() { return new ActionRunner(new FakeLlmClient(r => "[]"), new PromptBuilder(null), 1024, new FakeStyleService()); }

        private static XmlText FirstRun(FakeDocumentAdapter doc)
        {
            return TextRuns.Of(new List<XmlNode> { BlockSelector.FindBody(doc.Document) }).First();
        }

        private static StyleFinding Finding(FakeDocumentAdapter doc, string text, int occurrence, string replacement)
        {
            var run = FirstRun(doc);
            var at = -1;
            for (var n = 0; n <= occurrence; n++) at = run.Value.IndexOf(text, at + 1, StringComparison.Ordinal);
            return FindingLocator.FromLint(new LintHit { Start = at, Length = text.Length, Match = text, Replacement = replacement, Rule = "t", Severity = "warning", Message = "m" }, run);
        }

        private static string Text(FakeDocumentAdapter doc) { return doc.Document.SelectSingleNode("//body/p").InnerText; }

        [Fact]
        public void SecondOfTwoOccurrences_WithChangedContext_IsNotApplied_AndIsFlaggedStale()
        {
            var doc = Open("Open this page now. Later, close this page too.");
            var second = Finding(doc, "this page", 1, "the page");
            // The writer edits the words before the second occurrence.
            var run = FirstRun(doc);
            run.Value = run.Value.Replace("Later, close", "Afterwards, then close");
            var before = doc.Document.OuterXml;

            Assert.Equal(0, Runner().ApplyFixes(doc, new[] { second }, "fix"));
            Assert.Equal(before, doc.Document.OuterXml);
            Assert.True(second.IsStale);
            Assert.False(second.IsFixed);

            second.IsStale = false;
            ReviewOrder.Annotate(doc.Document, new[] { second });
            Assert.True(second.IsStale);
        }

        [Fact]
        public void SingleOccurrence_WithChangedContext_StillApplies()
        {
            var doc = Open("Please utilize the tool today.");
            var f = Finding(doc, "utilize", 0, "use");
            var run = FirstRun(doc);
            run.Value = run.Value.Replace("Please", "Kindly");

            Assert.Equal(1, Runner().ApplyFixes(doc, new[] { f }, "fix"));
            Assert.Equal("Kindly use the tool today.", Text(doc));
            Assert.False(f.IsStale);
        }

        [Fact]
        public void SeveralFixesInOneParagraph_AppliedAsABatch_AllLandCorrectly()
        {
            var doc = Open("Utilize this page, then utilize that page, and utilize this page again.");
            var all = new[]
            {
                Finding(doc, "Utilize", 0, "Use"),
                Finding(doc, "utilize", 0, "use"),
                Finding(doc, "utilize", 1, "use"),
                Finding(doc, "this page", 0, "the page"),
                Finding(doc, "this page", 1, "the page")
            };

            Assert.Equal(5, Runner().ApplyFixes(doc, all, "fix"));
            Assert.Equal("Use the page, then use that page, and use the page again.", Text(doc));
            Assert.All(all, f => Assert.False(f.IsStale));
            Assert.Single(doc.TextOperations);
        }
    }
}
