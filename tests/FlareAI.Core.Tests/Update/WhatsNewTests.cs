using System.IO;
using System.Linq;
using FlareAI.Core.Update;
using Xunit;

namespace FlareAI.Core.Tests.Update
{
    public class WhatsNewTests
    {
        private const string Md = @"# What's new in House Style

Intro text that isn't shown.

## 0.3.0 — November 2026

### Faster checks
- You asked for quicker checks → now Check style answers
  in under a second.
- See the [release page](https://example.com/r) and [a bad link](javascript:alert(1)).

A closing **paragraph**.

## 0.2.0
- First entry.

## 0.1.0
- Oldest.
";

        [Fact]
        public void Parse_ReadsSectionsHeadingsBulletsAndLinks()
        {
            var w = WhatsNew.Parse(Md);
            Assert.Equal(new[] { "0.3.0", "0.2.0", "0.1.0" }, w.Sections.Select(s => s.Version).ToArray());
            var s3 = w.Sections[0];
            Assert.Equal("November 2026", s3.Title);
            Assert.Equal(WhatsNew.Kind.Heading, s3.Blocks[0].Kind);
            Assert.Equal("Faster checks", s3.Blocks[0].PlainText);
            Assert.Equal(WhatsNew.Kind.Bullet, s3.Blocks[1].Kind);
            Assert.Equal("You asked for quicker checks → now Check style answers in under a second.", s3.Blocks[1].PlainText);
            var links = s3.Blocks[2].Runs.Where(r => r.Text == "release page" || r.Text == "a bad link").ToList();
            Assert.Equal("https://example.com/r", links[0].Url);
            Assert.Null(links[1].Url);   // only web links open
            Assert.Equal(WhatsNew.Kind.Paragraph, s3.Blocks[3].Kind);
            Assert.Equal("A closing paragraph.", s3.Blocks[3].PlainText);
        }

        [Fact]
        public void Unseen_AfterAnUpdate_ShowsEveryVersionSinceTheLastOneSeen()
        {
            var w = WhatsNew.Parse(Md);
            Assert.Equal(new[] { "0.3.0", "0.2.0" }, w.Unseen("0.1.0", "0.3.0").Select(s => s.Version).ToArray());
            Assert.Empty(w.Unseen("0.3.0", "0.3.0"));
            Assert.Equal(new[] { "0.2.0" }, w.Unseen("0.1.0", "0.2.0").Select(s => s.Version).ToArray());   // never newer than running
        }

        [Fact]
        public void Unseen_OnAFirstRun_ShowsOnlyTheCurrentVersion()
        {
            var w = WhatsNew.Parse(Md);
            Assert.Equal(new[] { "0.3.0" }, w.Unseen(null, "0.3.0").Select(s => s.Version).ToArray());
            Assert.Empty(w.Unseen(null, "0.0.1"));
        }

        [Fact]
        public void ShippedFile_ParsesAndHasASectionPerVersionHeading()
        {
            var path = Path.Combine(RepoRoot(), "src", "FlareAI.Flare", WhatsNew.FileName);
            var text = File.ReadAllText(path);
            var w = WhatsNew.Parse(text);
            Assert.NotEmpty(w.Sections);
            Assert.All(w.Sections, s => Assert.NotNull(SemVer.Parse(s.Version)));
            Assert.All(w.Sections, s => Assert.NotEmpty(s.Blocks));
        }

        internal static string RepoRoot()
        {
            var d = new DirectoryInfo(System.AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            return d.FullName;
        }
    }
}
