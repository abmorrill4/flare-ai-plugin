using System;
using System.IO;
using System.Linq;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class SnippetTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flareai-snip-" + Guid.NewGuid().ToString("N"));
        private readonly string _topic, _snippet, _nested;

        public SnippetTests()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Content"));
            Directory.CreateDirectory(Path.Combine(_root, @"Resources\Snippets"));
            _topic = Path.Combine(_root, @"Content\Topic.htm");
            _snippet = Path.Combine(_root, @"Resources\Snippets\Warn.flsnp");
            _nested = Path.Combine(_root, @"Resources\Snippets\Inner.flsnp");
            const string ns = "xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"";
            File.WriteAllText(_topic, "<html " + ns + "><head /><body><p>Clean text.</p><MadCap:snippetBlock src=\"../Resources/Snippets/Warn.flsnp\" /></body></html>");
            File.WriteAllText(_snippet, "<html " + ns + "><body><p>Only admins can utilize this.</p><MadCap:snippetBlock src=\"Inner.flsnp\" /><MadCap:snippetBlock src=\"../../Content/Topic.htm\" /></body></html>");
            File.WriteAllText(_nested, "<html " + ns + "><body><p>Inner snippets utilize it too.</p></body></html>");
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

        private static ActionRunner Runner() { return new ActionRunner(new FakeLlmClient(r => "[]"), new PromptBuilder(null), 1024, new FakeStyleService()); }

        [Fact]
        public void CheckStyle_IncludesSnippetsAndNestedSnippets_WithTheirPaths()
        {
            var doc = new FakeDocumentAdapter(_topic, true);
            var findings = Runner().CheckStyle(doc).Where(f => f.Rule == "plain-words.utilize").ToList();
            Assert.Equal(new[] { _snippet, _nested }.OrderBy(x => x), findings.Select(f => f.DocumentPath).OrderBy(x => x));
            Assert.All(findings, f => Assert.True(f.CanFix));
        }

        [Fact]
        public void SnippetFinding_IsFixedInTheSnippetDocument()
        {
            var runner = Runner();
            var f = runner.CheckStyle(new FakeDocumentAdapter(_topic, true)).First(x => x.DocumentPath == _snippet);
            var snippetDoc = new FakeDocumentAdapter(_snippet, true);   // what Flare opens
            Assert.Equal(1, runner.ApplyFixes(snippetDoc, new[] { f }, "fix"));
            Assert.Contains("Only admins can use this.", snippetDoc.Document.OuterXml);
        }
    }
}
