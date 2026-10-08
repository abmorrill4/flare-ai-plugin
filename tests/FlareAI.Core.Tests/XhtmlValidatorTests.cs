using System.Linq;
using FlareAI.Core.Editing;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class XhtmlValidatorTests
    {
        private static FakeDocumentAdapter Doc() { return new FakeDocumentAdapter("sample-topic.htm"); }

        [Fact]
        public void UnchangedFragment_IsValid()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[1]/text()[1]");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto(XhtmlFragment.Serialize(blocks), doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.True(r.IsValid, r.ToString());
        }

        [Fact]
        public void DroppedVariable_IsRejected()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[1]/text()[1]");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<p>To use SSO, make sure your administrator has granted you permission. SAML 2.0 and OIDC are supported.</p>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("MadCap:variable"));
        }

        [Fact]
        public void ChangedXrefHref_IsRejected()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[3]/text()[1]");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<p>See <MadCap:xref href=\"Other.htm\">Troubleshoot SSO</MadCap:xref>.</p>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.False(r.IsValid);
        }

        [Fact]
        public void RewordedXrefText_IsAllowed()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[3]/text()[1]");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<p>See <MadCap:xref href=\"Troubleshooting-SSO.htm\">Troubleshoot SSO</MadCap:xref> for details.</p>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.True(r.IsValid, r.ToString());
        }

        [Fact]
        public void DroppedConditionAttribute_IsRejected()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[2]/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<p>This paragraph is only in print output.</p>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.False(r.IsValid);
        }

        [Fact]
        public void UnknownElement_IsRejected()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[2]/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<p MadCap:conditions=\"Default.Print\">Only in <blink>print</blink>.</p>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.Contains(r.Errors, e => e.Contains("<blink>"));
        }

        [Fact]
        public void InventedMadCapElement_IsRejected()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[2]/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<p MadCap:conditions=\"Default.Print\">Only in print <MadCap:variable name=\"General.Made.Up\" />.</p>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.Contains(r.Errors, e => e.Contains("Introduced"));
        }

        [Fact]
        public void MalformedXml_Throws()
        {
            var doc = Doc();
            Assert.ThrowsAny<System.Xml.XmlException>(() => XhtmlFragment.ParseInto("<p>unclosed", doc.Document));
        }

        [Fact]
        public void ParagraphToList_Restructure_IsValid()
        {
            var doc = Doc();
            doc.SelectXPath("//body/p[2]/text()");
            var blocks = BlockSelector.SelectBlocks(doc.Document, doc.SelectionNodes);
            var proposed = XhtmlFragment.ParseInto("<ul MadCap:conditions=\"Default.Print\"><li>Only in print output.</li></ul>", doc.Document);
            var r = XhtmlValidator.Validate(blocks, proposed);
            Assert.True(r.IsValid, r.ToString());
            Assert.Equal("ul", proposed.First().LocalName);
        }
    }
}
