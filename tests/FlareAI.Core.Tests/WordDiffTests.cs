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
        public void RunRewrite_AfterBoldLabel_KeepsEveryUnchangedWord()
        {
            // Bench: "from the <b>Transfers</b> page. Each change …" came back as "from the Transfers Each change …".
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = XhtmlFragment.ParseInto("<p>Members can remove a transfer from the <b>Transfers</b> page. Each change appears in the account history. Your new transfer is ready to go! Our best-in-class transfer experience delights members.</p>", doc.Document)[0];
            doc.Document.SelectSingleNode("//body").AppendChild(p);
            doc.SelectXPath("//body/p[last()]/text()[2]");
            var llm = new FakeLlmClient(r => FakeLlmClient.EditRuns(r, t => t.Replace("ready to go!", "ready to go.").Replace("Our best-in-class transfer experience delights members.", "The transfer experience is designed for members.")));
            var runner = Runner(llm);
            var proposal = runner.Rewrite(doc, null);
            Assert.True(proposal.Validation.IsValid, proposal.Validation.ToString());
            runner.Apply(doc, proposal, "x");
            Assert.Equal("Members can remove a transfer from the Transfers page. Each change appears in the account history. Your new transfer is ready to go. The transfer experience is designed for members.", Visible(p));
        }

        [Fact]
        public void RunRewrite_DroppingTheWordsAfterABoldLabel_PutsThemBack_WithoutARetry()
        {
            // Haiku in the bench: " page. Each change …" came back as "Each change …", losing "page.".
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = XhtmlFragment.ParseInto("<p>Remove a transfer from the <b>Transfers</b> page. Your transfer is ready to go!</p>", doc.Document)[0];
            doc.Document.SelectSingleNode("//body").AppendChild(p);
            doc.SelectXPath("//body/p[last()]/text()[2]");
            var llm = new FakeLlmClient(r => FakeLlmClient.EditRuns(r, t => t.Replace(" page. Your transfer is ready to go!", "Your transfer is ready.")));
            var runner = Runner(llm);
            var proposal = runner.Rewrite(doc, null);
            Assert.Single(llm.Requests);                                          // no retry
            Assert.Contains("r3: \"page.\"", llm.Requests[0].User);               // the model was told up front
            runner.Apply(doc, proposal, "x");
            Assert.Equal("Remove a transfer from the Transfers page. Your transfer is ready.", Visible(p));
        }

        [Fact]
        public void RunRewrite_ChangingTheWordsThatFinishTheSentence_IsRetried_ThenLeftOut()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = XhtmlFragment.ParseInto("<p>Remove a transfer from the <b>Transfers</b> page. Your transfer is ready to go!</p>", doc.Document)[0];
            doc.Document.SelectSingleNode("//body").AppendChild(p);
            doc.SelectXPath("//body/p[last()]/text()[2]");
            var llm = new FakeLlmClient(r => FakeLlmClient.EditRuns(r, t => t.Replace(" page. Your transfer is ready to go!", "screen. Your transfer is ready.")));
            var runner = Runner(llm);
            var proposal = runner.Rewrite(doc, null);
            Assert.Equal(2, llm.Requests.Count);                                  // the breach is retried once
            Assert.Contains("must still start with \"page.\"", llm.Requests[1].User);
            runner.Apply(doc, proposal, "x");
            Assert.Contains("Transfers page.", Visible(p));                        // the bad change never lands
        }

        [Fact]
        public void Restructure_FallsBackToBlockReplacement()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var runner = Runner(new FakeLlmClient(r => "<revised><p MadCap:conditions=\"Default.Print\">Only in print:</p><ul MadCap:conditions=\"Default.Print\"><li>output.</li></ul></revised>"));
            var proposal = runner.Restructure(doc, runner.PickBlocks(doc), null);
            Assert.Null(proposal.WordPlan);
            Assert.Null(proposal.ApplyBlocked);
            Assert.Contains("paragraph-level", runner.Apply(doc, proposal, "x"));
            Assert.Single(doc.Replacements);
        }

        [Fact]
        public void Restructure_ThatChangesTheElement_IsRefused()
        {
            // ReplaceDocumentNode keeps the target's element name (flare-api-notes), so p → ul would leave a <p> around the list (#111).
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var runner = Runner(new FakeLlmClient(r => "<revised><ul MadCap:conditions=\"Default.Print\"><li>Only in print.</li></ul></revised>"));
            var proposal = runner.Restructure(doc, runner.PickBlocks(doc), null);
            Assert.True(proposal.Validation.IsValid);
            Assert.Contains("turns a paragraph into a bulleted list", proposal.ApplyBlocked);
            Assert.Throws<System.InvalidOperationException>(() => runner.Apply(doc, proposal, "x"));
            Assert.Empty(doc.Replacements);
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
            var llm = new FakeLlmClient(r => FakeLlmClient.EditRuns(r, t => t.Replace("make sure your administrator has granted", "ask your administrator to grant")));
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
            var runner = Runner(new FakeLlmClient(r => "<revised><ul><li>To use SSO, ask your administrator. <MadCap:variable name=\"General.ProductName\" /> supports SAML 2.0 and OIDC.</li></ul></revised>"));
            var proposal = runner.Restructure(doc, runner.PickBlocks(doc), null);
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
