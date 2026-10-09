using System.Collections.Generic;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class ReviewCacheTests
    {
        [Fact]
        public void SamePromptHits_ReturnsIndependentCopies_AndEvictsOldest()
        {
            var cache = new ReviewCache(2, null);
            cache.Put("a", new List<StyleFinding> { new StyleFinding { Rule = "r", Excerpt = "x", Replacement = "y" } });
            var first = cache.Get("a");
            first[0].Replacement = null;               // anchoring mutates findings; the cache must not see it
            Assert.Equal("y", cache.Get("a")[0].Replacement);
            Assert.Null(cache.Get("b"));
            cache.Put("b", new List<StyleFinding>());
            cache.Put("c", new List<StyleFinding>());
            Assert.Null(cache.Get("a"));               // capacity 2: oldest evicted
            Assert.NotNull(cache.Get("c"));
        }

        [Fact]
        public void SavedReviewsLoadInANewSession()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flareai-cache-" + System.Guid.NewGuid() + ".json");
            try
            {
                new ReviewCache(10, path).Put("topic", new List<StyleFinding> { new StyleFinding { Source = "ai", Rule = "r", RuleId = "company:x/1", Excerpt = "x", Replacement = null } });
                var hit = new ReviewCache(10, path).Get("topic");
                Assert.Single(hit);
                Assert.Equal("company:x/1", hit[0].RuleId);
                Assert.Null(hit[0].Replacement);
                Assert.Equal("restructure", hit[0].Kind);
            }
            finally { System.IO.File.Delete(path); }
        }

        [Fact]
        public void ReviewOrderGroupsBySectionAndSplitsLongTopics()
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml("<html><body><h1>Title</h1><p>Intro utilize.</p><h2>Steps</h2><p>Click here to utilize it.</p></body></html>");
            var body = doc.DocumentElement["body"];
            var f1 = new StyleFinding { Excerpt = "utilize it", Before = "Click here to " };
            var f2 = new StyleFinding { Excerpt = "utilize", Before = "Intro " };
            ReviewOrder.Annotate(doc, new[] { f1, f2 });
            Assert.Equal("Steps", f1.Section);
            Assert.Equal("Title", f2.Section);
            Assert.True(f2.Order < f1.Order);
            Assert.False(f1.IsStale);

            var gone = new StyleFinding { Excerpt = "no longer here" };
            ReviewOrder.Annotate(doc, new[] { gone });
            Assert.True(gone.IsStale);

            var blocks = new List<System.Xml.XmlNode>();
            foreach (System.Xml.XmlNode n in body.ChildNodes) blocks.Add(n);
            Assert.Single(ReviewOrder.Sections(blocks));                       // short topic: one review
            Assert.Equal(2, ReviewOrder.Sections(blocks, 5, 10).Count);       // long topic: split at headings
        }
    }
}
