using System.IO;
using System.IO.Compression;
using System.Text;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class NoteTextTests
    {
        private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private static byte[] Docx(string bodyXml)
        {
            var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var e = z.CreateEntry("[Content_Types].xml");
                using (var w = new StreamWriter(e.Open())) w.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
                e = z.CreateEntry("word/document.xml");
                using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                    w.Write("<w:document xmlns:w=\"" + Word + "\"><w:body>" + bodyXml + "</w:body></w:document>");
            }
            return ms.ToArray();
        }

        [Fact]
        public void Docx_paragraphs_runs_tabs_and_table_cells_become_lines()
        {
            var text = NoteText.FromDocx(Docx(
                "<w:p><w:r><w:t>Never say </w:t></w:r><w:r><w:t>utilize</w:t></w:r></w:p>" +
                "<w:p><w:r><w:t>Use</w:t><w:tab/><w:t>use</w:t></w:r></w:p>" +
                "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>A &amp; B</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>C</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"));
            Assert.Equal("Never say utilize\nUse\tuse\nA & B\nC", text);
        }

        [Fact]
        public void Docx_without_document_xml_is_a_friendly_error()
        {
            var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true)) { using (var w = new StreamWriter(z.CreateEntry("other.txt").Open())) w.Write("x"); }
            Assert.Throws<InvalidDataException>(() => NoteText.FromDocx(ms.ToArray()));
            Assert.Throws<InvalidDataException>(() => NoteText.FromDocx(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public void ReadFile_reads_a_docx_that_another_program_holds_open()
        {
            var path = Path.Combine(Path.GetTempPath(), "notes-" + System.Guid.NewGuid().ToString("N") + ".docx");
            File.WriteAllBytes(path, Docx("<w:p><w:r><w:t>Hello</w:t></w:r></w:p>"));
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) // like Word
                    Assert.Equal("Hello", NoteText.ReadFile(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Html_drops_style_script_conditional_comments_and_op_tags()
        {
            var html = "<html><head><title>T</title><style>p {color:red}</style><script>var a=1;</script>" +
                       "<!--[if gte mso 9]><xml><o:x>junk</o:x></xml><![endif]--></head><body>" +
                       "<p class=MsoNormal>Use &ldquo;sign in&rdquo;,&nbsp;not <b>log\r\n in</b>.<o:p></o:p></p>" +
                       "<!--[if !supportLists]--><span>1.</span><!--[endif]-->" +
                       "<ul><li>One &amp; two</li><li>Three</li></ul>" +
                       "<table><tr><td>Cell A</td><td>Cell B</td></tr></table></body></html>";
            var text = NoteText.CleanHtml(html);
            Assert.DoesNotContain("color:red", text);
            Assert.DoesNotContain("var a", text);
            Assert.DoesNotContain("junk", text);
            Assert.DoesNotContain("<", text);
            Assert.StartsWith("Use", text);
            Assert.Contains("Use “sign in”, not log in.", text);
            var lines = text.Split('\n');
            Assert.Contains("One & two", lines);
            Assert.Contains("Three", lines);
            Assert.Contains("Cell A", lines);
            Assert.Contains("Cell B", lines);
        }

        [Fact]
        public void Html_empty_input_is_empty() { Assert.Equal("", NoteText.CleanHtml(null)); }
    }
}
