using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace FlareAI.Core.Settings
{
    /// <summary>Which backend the plugin talks to.</summary>
    public enum LlmProvider
    {
        AnthropicDirect = 0,
        Bifrost = 1
    }

    /// <summary>
    /// Persisted at %APPDATA%\FlareAI\settings.json. The API key is DPAPI-protected (CurrentUser scope),
    /// so the file is useless on another machine or account. Dev fallback: ANTHROPIC_API_KEY env var.
    /// </summary>
    public sealed class PluginSettings
    {
        public const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
        public const string DefaultModel = "claude-sonnet-5-5";
        public const string EnvKeyName = "ANTHROPIC_API_KEY";

        public LlmProvider Provider { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public int MaxTokens { get; set; }
        public int TimeoutSeconds { get; set; }
        /// <summary>DPAPI-protected, base64. Never the plaintext key.</summary>
        public string ProtectedApiKey { get; set; }
        /// <summary>Bifrost only: comma-separated patterns for x-bf-mcp-include-tools.</summary>
        public string McpIncludeTools { get; set; }
        public bool WriteDiagnostics { get; set; }
        /// <summary>docstyle MCP server launch command; empty disables linting. {PluginDir} expands to the plugin folder.</summary>
        public string StyleServerCommand { get; set; }
        public string StyleServerArgs { get; set; }
        /// <summary>
        /// Style layer folders applied after the Google baseline, in order (later wins), one per line.
        /// Empty = the bundled company layer. Each folder: guide.md, rules/*.yml, accept.txt, glossary.json, overrides.ini.
        /// </summary>
        public string StyleLayers { get; set; }
        /// <summary>Add the open Flare project's variables, glossary and Resources/StyleGuide folder as the last layer.</summary>
        public bool UseProjectLayer { get; set; }
        /// <summary>After each build of an HTML target, add Markdown per topic + llms.txt to the output (docstyle --llm-export).</summary>
        public bool LlmExportAfterBuild { get; set; }
        /// <summary>Fast-tier model (refine, topic assists); empty = built-in default. <see cref="Model"/> is the standard tier.</summary>
        public string FastModel { get; set; }
        /// <summary>Deep-tier model (owner edits to the style guide); empty = built-in default.</summary>
        public string DeepModel { get; set; }
        /// <summary>Model calls run at once for per-block work.</summary>
        public int Parallelism { get; set; }

        /// <summary>The routing in effect: these settings, then the team's FlareAI\models.json in the style guide.</summary>
        public FlareAI.Core.Llm.Routing Routing(string guideRoot)
        {
            return new FlareAI.Core.Llm.Routing(string.IsNullOrEmpty(FastModel) ? null : FastModel, string.IsNullOrEmpty(Model) ? null : Model,
                                                string.IsNullOrEmpty(DeepModel) ? null : DeepModel).WithTeamFile(guideRoot);
        }
        /// <summary>
        /// Shared team feedback folder (a synced OneDrive/SharePoint folder, network share, or repo writers can push to);
        /// each writer appends to their own file there. Empty = decisions stay in the personal log only.
        /// </summary>
        public string FeedbackFolder { get; set; }

        /// <summary>The company style guide: the last configured layer, or the bundled company layer.</summary>
        public string GuideRoot(string pluginDir)
        {
            string last = null;
            foreach (var line in (StyleLayers ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.Trim().Length > 0) last = line.Trim().Replace("{PluginDir}", pluginDir);
            return last ?? System.IO.Path.Combine(pluginDir, @"docstyle\layers\company");
        }

        /// <summary>Server arguments with {PluginDir} expanded and one --layer per configured folder.</summary>
        public string StyleServerArgsFor(string pluginDir)
        {
            var args = (StyleServerArgs ?? "").Replace("{PluginDir}", pluginDir);
            foreach (var line in (StyleLayers ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = line.Trim().Replace("{PluginDir}", pluginDir);
                if (dir.Length > 0) args += " --layer \"" + dir.TrimEnd('\\') + "\"";
            }
            return args;
        }

        public PluginSettings()
        {
            Provider = LlmProvider.AnthropicDirect;
            BaseUrl = DefaultAnthropicBaseUrl;
            Model = DefaultModel;
            MaxTokens = 4096;
            TimeoutSeconds = 90;
            WriteDiagnostics = true;
            StyleServerCommand = DefaultStyleServerCommand;
            StyleServerArgs = DefaultStyleServerArgs;
            StyleLayers = "";
            UseProjectLayer = true;
        }

        public const string DefaultStyleServerCommand = "uv";
        public const string DefaultStyleServerArgs = "run --quiet --script \"{PluginDir}\\docstyle\\server.py\"";

        // ---- key handling -------------------------------------------------------------

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FlareAI.v1");

        public void SetApiKey(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) { ProtectedApiKey = null; return; }
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            ProtectedApiKey = Convert.ToBase64String(enc);
        }

        /// <summary>Stored key first, then the environment variable. Null if neither.</summary>
        public string ResolveApiKey()
        {
            if (!string.IsNullOrEmpty(ProtectedApiKey))
            {
                try
                {
                    var dec = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedApiKey), Entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(dec);
                }
                catch (CryptographicException) { /* different user/machine — fall through */ }
            }
            var env = Environment.GetEnvironmentVariable(EnvKeyName);
            return string.IsNullOrEmpty(env) ? null : env;
        }

        public bool HasStoredKey { get { return !string.IsNullOrEmpty(ProtectedApiKey); } }

        // ---- persistence --------------------------------------------------------------

        public static string SettingsDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlareAI"); }
        }

        public static string SettingsPath { get { return Path.Combine(SettingsDirectory, "settings.json"); } }

        public static PluginSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonConvert.DeserializeObject<PluginSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                    if (s != null) return s;
                }
            }
            catch (Exception) { /* corrupt file → defaults */ }
            return new PluginSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
        }
    }
}
