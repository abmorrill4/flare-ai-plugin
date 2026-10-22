using System;
using System.IO;
using FlareAI.Core.Llm;

namespace FlareAI.Core.Settings
{
    /// <summary>The steps of first-run setup, in order.</summary>
    public enum SetupStep { Role = 0, Ai = 1, Guide = 2, Done = 3 }

    /// <summary>
    /// The writer's answers in first-run setup (and "Run setup again" in Settings), and how they become settings.
    /// The setup window only collects these; everything that decides what is valid or what gets saved is here.
    /// </summary>
    public sealed class SetupAnswers
    {
        /// <summary>How the writer uses House Style (setup's first question); it decides the ribbon.</summary>
        public UserRole Role { get; set; }
        /// <summary>The writer skipped the AI step: the AI settings stay as they are.</summary>
        public bool AiSkipped { get; set; }
        public AiChoice Ai { get; set; }
        /// <summary>The key as typed (never stored here beyond setup). Blank keeps a stored key for the same choice.</summary>
        public string Key { get; set; }
        /// <summary>The gateway address as typed.</summary>
        public string BaseUrl { get; set; }

        /// <summary>The writer skipped the style guide step: the guide settings stay as they are.</summary>
        public bool GuideSkipped { get; set; }
        /// <summary>True: "My team's shared style guide folder"; false: "Google's guide plus my own changes".</summary>
        public bool UseTeamGuide { get; set; }
        public string TeamGuideFolder { get; set; }

        /// <summary>The answers that describe the current settings, so running setup again starts from them.</summary>
        public static SetupAnswers From(PluginSettings s)
        {
            s = s ?? new PluginSettings();
            var guides = (s.StyleLayers ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            string last = null;
            foreach (var g in guides) if (g.Trim().Length > 0 && !g.Contains("{PluginDir}")) last = g.Trim();
            var ai = AiChoices.Of(s);
            return new SetupAnswers
            {
                Role = s.Role ?? UserRole.Writer,
                Ai = ai,
                BaseUrl = ai == AiChoice.Gateway ? s.BaseUrl : "",
                UseTeamGuide = last != null,
                TeamGuideFolder = last ?? ""
            };
        }

        /// <summary>The first thing wrong with the AI answers, in words to show; null when they can be saved.</summary>
        /// <param name="current">The settings before setup (a stored key counts only for the same choice).</param>
        public string AiProblem(PluginSettings current)
        {
            if (AiSkipped || Ai == AiChoice.None) return null;
            if (AiChoices.UsesBaseUrl(Ai))
            {
                string url, err;
                if (!LlmErrors.TryNormalizeBaseUrl(BaseUrl, out url, out err)) return err;
            }
            var typed = (Key ?? "").Trim();
            if (typed.Length == 0 && !HasKeyFor(current, Ai))
                return "Enter your " + (Ai == AiChoice.Gateway ? "gateway key" : "API key") + ", or choose Skip this step to add it later in Settings.";
            if (typed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) return "The key contains spaces or line breaks. Paste it again in full.";
            return null;
        }

        /// <summary>The first thing wrong with the style guide answers (quick checks only); null when they can be saved.</summary>
        /// <remarks>Whether the folder is a usable guide is <see cref="GuideFolderCheck"/>, which can be slow; the window runs it in the background.</remarks>
        public string GuideProblem()
        {
            if (GuideSkipped || !UseTeamGuide) return null;
            return (TeamGuideFolder ?? "").Trim().Length == 0 ? "Choose your team's style guide folder, or choose Google's guide plus my own changes." : null;
        }

        /// <summary>
        /// Writes the answers into <paramref name="s"/>. False (and nothing changed) when an answer can't be saved, with
        /// the reason in <paramref name="error"/>. Doesn't save the file: see <see cref="FirstRun.Finish"/>.
        /// </summary>
        public bool ApplyTo(PluginSettings s, out string error)
        {
            error = AiProblem(s) ?? GuideProblem();
            if (error != null) return false;
            s.Role = Role;
            if (!AiSkipped)
            {
                var sameChoice = AiChoices.Of(s) == Ai;
                AiChoices.Apply(s, Ai);
                if (AiChoices.UsesBaseUrl(Ai))
                {
                    string url, err;
                    LlmErrors.TryNormalizeBaseUrl(BaseUrl, out url, out err);
                    s.BaseUrl = url;
                }
                var typed = (Key ?? "").Trim();
                if (AiChoices.UsesKey(Ai))
                {
                    if (typed.Length > 0) s.SetApiKey(typed);
                    else if (!sameChoice) s.SetApiKey(null);   // never send one provider's key to the other
                }
            }
            if (!GuideSkipped)
            {
                if (!UseTeamGuide) s.StyleLayers = "";
                else
                {
                    var folder = TeamGuideFolder.Trim().TrimEnd('\\', '/');
                    var existing = (s.StyleLayers ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    var known = false;
                    foreach (var e in existing) if (string.Equals(e.Trim().TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase)) known = true;
                    if (!known) s.StyleLayers = folder;   // a folder already in a multi-guide list keeps the list
                }
            }
            return true;
        }

        private static bool HasKeyFor(PluginSettings s, AiChoice c)
        {
            if (s == null || AiChoices.Of(s) != c) return false;
            return !string.IsNullOrEmpty(s.ResolveApiKey());
        }
    }

    /// <summary>First-run setup: when it shows, and how it ends.</summary>
    public static class FirstRun
    {
        /// <summary>The install and tester guide on the project site, linked from the last step.</summary>
        public const string TesterGuideUrl = "https://abmorrill4.github.io/flare-ai-plugin/install.html";

        /// <summary>True until the writer has finished or skipped setup (or saved Settings): no settings file yet.</summary>
        public static bool IsNeeded() { return IsNeeded(PluginSettings.SettingsPath); }

        public static bool IsNeeded(string settingsPath) { return !File.Exists(settingsPath); }

        /// <summary>
        /// Ends setup: applies the answers (null = the writer skipped setup) and saves, which also stops setup from
        /// showing again. False with <paramref name="error"/> when an answer can't be saved; nothing is written then.
        /// </summary>
        public static bool Finish(PluginSettings s, SetupAnswers answers, out string error)
        {
            error = null;
            if (answers != null && !answers.ApplyTo(s, out error)) return false;
            s.Save();
            return true;
        }

        /// <summary>One line per answer for the last step: what was set up.</summary>
        public static string Summary(PluginSettings s)
        {
            var ai = AiChoices.Of(s);
            string aiLine;
            if (ai == AiChoice.None) aiLine = "AI: off. Lint works without it.";
            else
            {
                var hasKey = !string.IsNullOrEmpty(s.ResolveApiKey());
                aiLine = "AI: " + AiChoices.Label(ai) + (hasKey ? "." : ". No key yet: add it in Settings when you have one.");
            }
            var guide = (s.StyleLayers ?? "").Trim().Length == 0 ? "Rules: Google's guide plus your own changes." : "Rules: your team's guide in " + s.StyleLayers.Trim().Replace("\r", "").Replace("\n", ", ") + ".";
            return Roles.Summary(s.Role ?? UserRole.Writer) + "\n" + aiLine + "\n" + guide;
        }
    }
}
