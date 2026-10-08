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
            var cache = new ReviewCache(2);
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
    }
}
