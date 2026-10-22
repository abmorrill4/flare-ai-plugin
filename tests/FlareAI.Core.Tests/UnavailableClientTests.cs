using FlareAI.Core.Llm;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class UnavailableClientTests
    {
        [Fact]
        public void AiCallFailsWithTheReason()
        {
            var ex = Assert.Throws<LlmException>(() => new UnavailableClient("No API key configured.").Complete(new LlmRequest()));
            Assert.Equal("No API key configured.", ex.Message);
            Assert.Equal(LlmFailure.Auth, ex.Failure);
        }
    }
}
