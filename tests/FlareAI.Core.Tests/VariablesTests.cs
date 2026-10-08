using System;
using System.Linq;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class VariablesTests
    {
        private static ActionRunner Runner(FakeDocumentAdapter doc)
        {
            return new ActionRunner(new FakeLlmClient(r => "[]"), new PromptBuilder(null), 1024, new FakeStyleService()) { VariableValue = doc.VariableValue };
        }

        [Fact]
        public void Lint_SeesSentencesAcrossInlineMarkup_ButOnlyFixesWithinOneRun()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var findings = Runner(doc).CheckStyle(doc);
            var across = findings.Single(f => f.Rule == "Test.Across");    // "to Settings" spans "Navigate to " + <b>Settings</b>
            Assert.Equal("to ", across.Excerpt);
            Assert.False(across.CanFix);
        }

        [Fact]
        public void HitsInsideVariableValues_AreNotTopicFindings()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");   // p[1] uses <MadCap:variable name="General.ProductName"/>
            var findings = Runner(doc).CheckStyle(doc);
            Assert.DoesNotContain(findings, f => f.Rule == "Project.Variables");
        }

        [Fact]
        public void TypedVariableValue_NamesTheVariable_ButIsGuidedNotAutoFixed()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = doc.Document.SelectSingleNode("//body/p[2]");
            p.InnerText = "Open Acme Online Banking now.";
            var runner = Runner(doc);
            var f = runner.CheckStyle(doc).Single(x => x.Rule == "Project.Variables");
            Assert.Equal("General.ProductName", f.Variable);
            Assert.False(f.CanFix);                       // Flare can't redo plugin-inserted variables
            Assert.True(runner.GoTo(doc, f));            // so: select it, author uses Insert > Variable
            Assert.Equal("Acme Online Banking", doc.LastSelected);
        }

        [Fact]
        public void UnknownVariables_AreLeftOut()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.Variables.Clear();
            Assert.NotEmpty(Runner(doc).CheckStyle(doc));   // still lints the rest
        }
    }
}
