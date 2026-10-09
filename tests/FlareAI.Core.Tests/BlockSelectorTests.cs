using System.Linq;
using FlareAI.Core.Editing;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class BlockSelectorTests
    {
        [Fact]
        public void NoSelection_ReturnsAllBodyChildren()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.Equal(new[] { "h1", "p", "p", "snippetBlock", "h2", "ol", "p", "dropDown" }, blocks.Select(b => b.LocalName).ToArray());
        }

        [Fact]
        public void SelectAll_BodyElementSelected_ReturnsAllBodyChildren()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body");    // what Flare reports for Ctrl+A
            Assert.Equal(8, BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes).Count);
        }

        [Fact]
        public void TextNodeInsideParagraph_ReturnsThatParagraph()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[1]/text()[1]");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.Single(blocks);
            Assert.Equal("p", blocks[0].LocalName);
            Assert.Contains("SSO functionality", blocks[0].InnerText);
        }

        [Fact]
        public void SelectionInsideListItem_ReturnsJustThatItem()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//ol/li[2]/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.Single(blocks);
            Assert.Equal("li", blocks[0].LocalName);
            Assert.Contains("SSO toggle", blocks[0].InnerText);
        }

        [Fact]
        public void SelectionAcrossTwoListItems_ReturnsBoth_SelectionWithTheListAndMore_ReturnsBlocks()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//ol/li[2]/text() | //ol/li[3]/text()");
            Assert.Equal(new[] { "li", "li" }, BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes).Select(b => b.LocalName).ToArray());
            doc.SelectXPath("//ol/li[3]/text() | //body/p[3]/text()");
            Assert.Equal(new[] { "ol", "p" }, BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes).Select(b => b.LocalName).ToArray());
        }

        [Fact]
        public void Rewrite_OfOneListItem_ReplacesOnlyThatItem()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//ol/li[2]/text()");
            var llm = new FakeLlmClient(r => "<revised><li>Click the SSO toggle to enable it.</li></revised>");
            var runner = new FlareAI.Core.Actions.ActionRunner(llm, new FlareAI.Core.Actions.PromptBuilder(null), 1024);
            var proposal = runner.Rewrite(doc, null);
            Assert.True(proposal.Validation.IsValid, proposal.Validation.ToString());
            Assert.DoesNotContain("Navigate to", llm.Requests[0].User);   // only the selected item was sent
            runner.Apply(doc, proposal, "x");
            Assert.Equal("Click the SSO toggle to enable it.", doc.Document.SelectSingleNode("//ol/li[2]").InnerText);
            Assert.Equal(3, doc.Document.SelectNodes("//ol/li").Count);
        }

        [Fact]
        public void SelectionAcrossTwoBlocks_ReturnsBothInDocumentOrder_NoDuplicates()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            // hand nodes in reverse order, plus a duplicate ancestor path
            doc.SelectXPath("//body/p[2]/text() | //body/p[1]/MadCap:variable | //body/p[1]/text()[1]");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.Equal(2, blocks.Count);
            Assert.Contains("SSO functionality", blocks[0].InnerText);
            Assert.Contains("print output", blocks[1].InnerText);
        }

        [Fact]
        public void SelectionInsideDropDown_ReturnsTheDropDown()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//MadCap:dropDownBody/p/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.Single(blocks);
            Assert.Equal("dropDown", blocks[0].LocalName);
            Assert.False(BlockSelector.IsOpaque(blocks[0]));
        }

        [Fact]
        public void SnippetBlock_IsOpaque()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//MadCap:snippetBlock");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.True(BlockSelector.IsOpaque(blocks[0]));
        }
    }
}
