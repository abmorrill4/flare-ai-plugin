using FlareAI.Core.Settings;

namespace FlareAI.Core.Llm
{
    public static class LlmClientFactory
    {
        public static ILlmClient Create(PluginSettings s)
        {
            var key = s.ResolveApiKey();
            switch (s.Provider)
            {
                case LlmProvider.Bifrost:
                    return new BifrostClient(s.BaseUrl, key, s.Model, s.TimeoutSeconds);
                default:
                    return new AnthropicClient(s.BaseUrl, key, s.Model, s.TimeoutSeconds);
            }
        }
    }
}
