using System;
using FlareAI.Core.Actions;
using FlareAI.Core.Llm;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class ActionRunnerTests
    {
        private static ActionRunner Runner(FakeLlmClient llm) { return new ActionRunner(llm, new PromptBuilder(null), 1024); }

        [Fact]
        public void Rewrite_WithEchoModel_IsNoOpAndValid()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[1]/text()[1]");
            var llm = new FakeLlmClient(FakeLlmClient.EchoRevised);
            var proposal = Runner(llm).Rewrite(doc, null);

            Assert.True(proposal.Validation.IsValid, proposal.Validation.ToString());
            Assert.Single(proposal.ProposedBlocks);
            Assert.Equal(proposal.OriginalBlocks[0].OuterXml, proposal.ProposedBlocks[0].OuterXml);
            Assert.Contains("Fragment:", llm.Requests[0].User);
            Assert.Contains("MadCap:", llm.Requests[0].System);
        }

        [Fact]
        public void Rewrite_ThenApply_ReplacesNodeAsOneOperation()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var llm = new FakeLlmClient(r => "<revised><p MadCap:conditions=\"Default.Print\">Print only.</p></revised>");
            var runner = Runner(llm);
            var proposal = runner.Rewrite(doc, null);
            Assert.True(proposal.Validation.IsValid, proposal.Validation.ToString());

            runner.Apply(doc, proposal, "AI rewrite");
            Assert.Single(doc.Replacements);
            Assert.Equal("Print only.", doc.Document.SelectSingleNode("//body/p[2]").InnerText);
        }

        [Fact]
        public void Rewrite_ModelReturnsProse_FailsValidationInsteadOfThrowing()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var llm = new FakeLlmClient(r => "Sure! Here is a better version of your paragraph.");
            var proposal = Runner(llm).Rewrite(doc, null);
            Assert.False(proposal.Validation.IsValid);
            Assert.Throws<InvalidOperationException>(() => Runner(llm).Apply(doc, proposal, "x"));
        }

        [Fact]
        public void Rewrite_OnSnippetBlock_Refuses()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//MadCap:snippetBlock");
            var llm = new FakeLlmClient(FakeLlmClient.EchoRevised);
            Assert.Throws<InvalidOperationException>(() => Runner(llm).Rewrite(doc, null));
        }

        [Fact]
        public void Ask_PutsInstructionInPrompt()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var llm = new FakeLlmClient(FakeLlmClient.EchoRevised);
            Runner(llm).Rewrite(doc, "make it a bulleted list");
            Assert.Contains("make it a bulleted list", llm.Requests[0].User);
        }

        [Fact]
        public void CheckStyle_ParsesFindings()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var llm = new FakeLlmClient(r => "```json\n[{\"rule\":\"filler\",\"severity\":\"warning\",\"excerpt\":\"simply\",\"suggestion\":\"Remove it.\"}]\n```");
            var findings = Runner(llm).CheckStyle(doc);
            Assert.Single(findings);
            Assert.Equal("filler", findings[0].Rule);
        }

        [Fact]
        public void CheckStyle_NoFindings_ReturnsEmpty()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var llm = new FakeLlmClient(r => "[]");
            Assert.Empty(Runner(llm).CheckStyle(doc));
        }

        [Fact]
        public void AnthropicBody_HasSystemAndUser()
        {
            var body = AnthropicClient.BuildBody("m", new LlmRequest { System = "S", User = "U", MaxTokens = 10 });
            Assert.Contains("\"system\":\"S\"", body);
            Assert.Contains("\"role\":\"user\"", body);
            Assert.Contains("\"max_tokens\":10", body);
        }

        [Fact]
        public void AnthropicParse_ConcatenatesTextBlocks()
        {
            var r = AnthropicClient.Parse("{\"model\":\"m\",\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"text\",\"text\":\"b\"}],\"usage\":{\"input_tokens\":3,\"output_tokens\":4}}");
            Assert.Equal("ab", r.Text);
            Assert.Equal(3, r.InputTokens);
        }

        [Fact]
        public void CheckStyle_OmitsPendingDeletions()
        {
            // Saved by Flare 22 after an applied AI rewrite: old text is a RemoveChange, new text an AddChange.
            var doc = new FakeDocumentAdapter("tracked-changes-topic.htm");
            doc.SelectXPath("//body/p[1]");
            var llm = new FakeLlmClient(r => "");
            Runner(llm).CheckStyle(doc);
            Assert.Contains("To use SSO", llm.Requests[0].User);
            Assert.DoesNotContain("In order to be able to utilize", llm.Requests[0].User);
            Assert.Contains("In order to be able to utilize", doc.Document.OuterXml); // document untouched
        }

        [Fact]
        public void Rewrite_OverPendingChanges_Refuses()
        {
            var doc = new FakeDocumentAdapter("tracked-changes-topic.htm");
            doc.SelectXPath("//body/p[1]");
            var llm = new FakeLlmClient(FakeLlmClient.EchoRevised);
            var ex = Assert.Throws<InvalidOperationException>(() => Runner(llm).Rewrite(doc, null));
            Assert.Contains("tracked changes", ex.Message);
            Assert.Empty(llm.Requests);
        }

        [Fact]
        public void CheckStyle_CaretOnly_ChecksWholeTopic()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            doc.SelectionText = ""; // caret, nothing selected
            var llm = new FakeLlmClient(r => "");
            Runner(llm).CheckStyle(doc);
            Assert.Contains("Configure single sign-on", llm.Requests[0].User);
            Assert.Contains("Whitelist the IdP", llm.Requests[0].User);
        }

        [Fact]
        public void CheckStyle_WithSelectedText_ChecksOnlyThoseBlocks()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            doc.SelectionText = "only in print";
            var llm = new FakeLlmClient(r => "");
            Runner(llm).CheckStyle(doc);
            Assert.DoesNotContain("Whitelist the IdP", llm.Requests[0].User);
        }
    }
}
