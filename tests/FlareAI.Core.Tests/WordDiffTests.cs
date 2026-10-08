using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class WordDiffTests
    {
        private static ActionRunner Runner(FakeLlmClient llm) { return new ActionRunner(llm, new PromptBuilder(null), 1024); }

        private static string Visible(XmlNode block)
        {
            return string.Concat(TextRuns.Of(new List<XmlNode> { block }).Select(r => r.Value));
        }

        [Fact]
        public void Restructure_FallsBackToBlockReplacement()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var runner = Runner(new FakeLlmClient(r => "<revised><ul MadCap:conditions=\"Default.Print\"><li>Only in print.</li></ul></revised>"));
            var proposal = runner.Rewrite(doc, null);
            Assert.Null(proposal.WordPlan);
            Assert.Contains("paragraph-level", runner.Apply(doc, proposal, "x"));
            Assert.Single(doc.Replacements);
        }

        [Fact]
        public void Insertion_IsWidenedOverThePreviousWord()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var li = doc.Document.SelectSingleNode("//ol/li[3]");
            var view = AcceptedView.Of(new List<XmlNode> { li });
            var proposed = XhtmlFragment.ParseInto("<li>Enter your identity provider's metadata URL, and then click <b>Save</b>.</li>", doc.Document);
            var plan = WordDiff.Plan(view, proposed, new List<XmlNode> { li });
            var f = Assert.Single(plan);
            Assert.Equal("URL and ", f.Excerpt);
            Assert.Equal("URL, and then ", f.Replacement);
        }

        [Fact]
        public void WhitespaceOnlyDifferences_AreNotEdits()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var li = doc.Document.SelectSingleNode("//ol/li[1]");
            var proposed = XhtmlFragment.ParseInto("<li>Navigate  to <b>Settings</b>  &gt;  <b>Security</b>.</li>", doc.Document);
            Assert.Empty(WordDiff.Plan(AcceptedView.Of(new List<XmlNode> { li }), proposed, new List<XmlNode> { li }));
        }

        [Fact]
        public void OverPendingChanges_ModelSeesCurrentText_AndEditsLandAroundTheChanges()
        {
            // Flare-saved topic: p[1] holds a pending insertion (new text) and a pending deletion (old text).
            var doc = new FakeDocumentAdapter("tracked-changes-topic.htm");
            doc.SelectXPath("//body/p[1]");
            var llm = new FakeLlmClient(r => FakeLlmClient.EchoRevised(r).Replace("make sure your administrator has granted", "ask your administrator to grant"));
            var runner = Runner(llm);
            var proposal = runner.Rewrite(doc, null);

            Assert.DoesNotContain("In order to be able", llm.Requests[0].User); // pending deletion hidden
            Assert.DoesNotContain("MadCap:change", llm.Requests[0].User);       // insertions read as plain text
            Assert.True(proposal.Validation.IsValid, proposal.Validation.ToString());
            Assert.Contains("word-level", runner.Apply(doc, proposal, "x"));

            var p = doc.Document.SelectSingleNode("//body/p[1]");
            Assert.Contains("To use SSO, ask your administrator to grant you the required permissions", Visible(p));
            Assert.Contains("In order to be able to utilize", p.InnerText);     // the other change is still pending
            Assert.Empty(doc.Replacements);
        }

        [Fact]
        public void OverPendingChanges_RestructureIsRejected_NotApplied()
        {
            var doc = new FakeDocumentAdapter("tracked-changes-topic.htm");
            doc.SelectXPath("//body/p[1]");
            var proposal = Runner(new FakeLlmClient(r => "<revised><ul><li>To use SSO, ask your administrator. <MadCap:variable name=\"General.ProductName\" /> supports SAML 2.0 and OIDC.</li></ul></revised>")).Rewrite(doc, null);
            Assert.False(proposal.Validation.IsValid);
            Assert.Contains(proposal.Validation.Errors, e => e.Contains("pending tracked changes"));
        }

        [Theory]
        [InlineData("make sure your administrator has granted you the", 41, 3, "you ")]   // "you" also inside "your"
        [InlineData("the user and the user", 17, 4, "user")]                              // repeat at the end: grow left
        [InlineData("utilize SSO", 0, 7, "utilize")]                                     // already unique
        public void UniqueSpan_WidensUntilTheTextOccursOnce(string text, int start, int length, string expected)
        {
            int s, l;
            TextRuns.UniqueSpan(text, start, length, out s, out l);
            Assert.True(s <= start && s + l >= start + length);
            Assert.Contains(expected, text.Substring(s, l));
            Assert.Equal(text.IndexOf(text.Substring(s, l)), text.LastIndexOf(text.Substring(s, l)));
        }
    }
}
