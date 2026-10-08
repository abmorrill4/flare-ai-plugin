using FlareAI.Core.Settings;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void ApiKey_RoundTripsThroughDpapi_AndIsNotStoredInPlaintext()
        {
            var s = new PluginSettings();
            s.SetApiKey("sk-ant-test-123");
            Assert.True(s.HasStoredKey);
            Assert.DoesNotContain("sk-ant-test-123", s.ProtectedApiKey);
            Assert.Equal("sk-ant-test-123", s.ResolveApiKey());
        }

        [Fact]
        public void Defaults_AreSane()
        {
            var s = new PluginSettings();
            Assert.Equal(LlmProvider.AnthropicDirect, s.Provider);
            Assert.Equal(PluginSettings.DefaultAnthropicBaseUrl, s.BaseUrl);
            Assert.True(s.MaxTokens >= 1024);
        }
    }
}
