using System;
using System.IO;
using System.Linq;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class ProjectCheckTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flareai-proj-" + Guid.NewGuid().ToString("N"));

        public ProjectCheckTests()
        {
            Directory.CreateDirectory(Path.Combine(_root, @"Content\Sub"));
            Directory.CreateDirectory(Path.Combine(_root, @"Content\Resources\StyleGuide"));
            File.WriteAllText(Path.Combine(_root, "Demo.flprj"), "<CatapultProject />");
            const string ns = "xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"";
            File.WriteAllText(Path.Combine(_root, @"Content\A.htm"), "<html " + ns + "><body><p>We utilize it.</p></body></html>");
            File.WriteAllText(Path.Combine(_root, @"Content\Sub\B.htm"), "<html " + ns + "><body><ul><li>Fine.</li><li>Ask the user.</li></ul></body></html>");
            File.WriteAllText(Path.Combine(_root, @"Content\Resources\StyleGuide\Guide.htm"), "<html><body><p>Never utilize jargon.</p></body></html>");
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

        [Fact]
        public void ProjectFiles_FindsTopicsAndSnippets_ButNotTheStyleGuideFolder()
        {
            var files = ActionRunner.ProjectFiles(Path.Combine(_root, @"Content\Sub\B.htm"));
            Assert.Equal(new[] { "A.htm", "B.htm" }, files.Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void LintFiles_OneBatch_FindingsCarryTheirFile_AndFixInThatFile()
        {
            var runner = new ActionRunner(new FakeLlmClient(r => "[]"), new PromptBuilder(null), 1024, new FakeStyleService());
            var files = ActionRunner.ProjectFiles(Path.Combine(_root, @"Content\A.htm"));
            var findings = runner.LintFiles(files);
            var utilize = findings.Single(f => f.Rule == "plain-words.utilize");
            Assert.EndsWith(@"Content\A.htm", utilize.DocumentPath);
            Assert.EndsWith(@"Sub\B.htm", findings.Single(f => f.Rule == "person.the-user").DocumentPath);

            var a = new FakeDocumentAdapter(utilize.DocumentPath, true);   // what Flare opens
            Assert.Equal(1, runner.ApplyFixes(a, new[] { utilize }, "fix"));
            Assert.Contains("We use it.", a.Document.OuterXml);
        }
    }
}
