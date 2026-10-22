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
        public void AutoUpdate_IsOnByDefault_TheSettingDecides_UnlessThePolicyFileDoes()
        {
            Assert.True(new PluginSettings().AutoUpdate);
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flareai-policy-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                // No policy file: the Settings check box decides, and it can be changed.
                var off = FlareAI.Core.Update.UpdatePolicy.Resolve(false, dir);
                Assert.False(off.Enabled);
                Assert.False(off.FromPolicyFile);
                Assert.True(FlareAI.Core.Update.UpdatePolicy.Resolve(true, dir).Enabled);
                // A policy file: it decides, and the check box is read-only ("Set by your IT department").
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, FlareAI.Core.Update.UpdateLayout.PolicyFileName), "{\"autoUpdate\": false}");
                var policy = FlareAI.Core.Update.UpdatePolicy.Resolve(true, dir);
                Assert.False(policy.Enabled);
                Assert.True(policy.FromPolicyFile);
            }
            finally { System.IO.Directory.Delete(dir, true); }
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
