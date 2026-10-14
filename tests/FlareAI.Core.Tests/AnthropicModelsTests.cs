using System;
using System.Collections.Generic;
using FlareAI.Core.Llm;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class AnthropicModelsTests
    {
        [Fact]
        public void FollowsPagesAndParsesNames()
        {
            var urls = new List<string>();
            AnthropicModels.Fetcher fetch = (url, key, t) =>
            {
                urls.Add(url);
                return url.Contains("after_id=m2")
                    ? "{\"data\":[{\"id\":\"m3\",\"display_name\":\"Three\"}],\"has_more\":false,\"last_id\":\"m3\"}"
                    : "{\"data\":[{\"id\":\"m1\",\"display_name\":\"One\"},{\"id\":\"m2\"}],\"has_more\":true,\"last_id\":\"m2\"}";
            };
            var list = AnthropicModels.List("https://x.test/", "k", 30, fetch);
            Assert.Equal(3, list.Count);
            Assert.Equal("https://x.test/v1/models?limit=1000", urls[0]);
            Assert.EndsWith("&after_id=m2", urls[1]);
            Assert.Equal("One — m1", list[0].ToString());
            Assert.Equal("m2", list[1].ToString());
        }

        [Fact]
        public void BadJsonAndTransportErrorsAreReadableAndKeyFree()
        {
            Assert.Throws<LlmException>(() => AnthropicModels.List(null, "k", 30, (u, k, t) => "<html>"));
            var ex = Assert.Throws<LlmException>(() => AnthropicModels.List(null, "sk-secret", 30, (u, k, t) => { throw new InvalidOperationException("boom sk-secret"); }));
            Assert.DoesNotContain("sk-secret", ex.Message);
            Assert.StartsWith("Couldn't reach Anthropic", ex.Message);
        }
    }
}
