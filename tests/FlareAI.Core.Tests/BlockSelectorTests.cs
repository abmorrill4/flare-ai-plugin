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
        public void SelectionInsideListItem_ReturnsWholeList()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//ol/li[2]/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            Assert.Single(blocks);
            Assert.Equal("ol", blocks[0].LocalName);
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
