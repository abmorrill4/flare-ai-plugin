namespace FlareAI.Core.Settings
{
    /// <summary>How the writer uses House Style; it decides which commands the ribbon shows.</summary>
    public enum UserRole
    {
        /// <summary>"To check my own writing": lint, AI, rules, settings and help.</summary>
        Writer = 0,
        /// <summary>"To set up the style guide for my team": everything a writer has plus the Style Guide group.</summary>
        Owner = 1
    }

    /// <summary>The role in effect and its words in the UI.</summary>
    public static class Roles
    {
        /// <summary>The setup and Settings question.</summary>
        public const string Question = "How will you use House Style?";

        /// <summary>The answers, in <see cref="UserRole"/> order.</summary>
        public static readonly string[] Answers = { "To check my own writing", "To set up the style guide for my team" };

        /// <summary>
        /// Owner for anyone the team guide's owners.txt lists, whatever they answered in setup (they approve and decline
        /// requests); otherwise the role the writer chose, or Writer with no choice yet.
        /// </summary>
        public static UserRole Effective(PluginSettings settings, bool listedOwner)
        {
            if (listedOwner) return UserRole.Owner;
            return settings != null && settings.Role.HasValue ? settings.Role.Value : UserRole.Writer;
        }

        /// <summary>One line for the setup summary.</summary>
        public static string Summary(UserRole role)
        {
            return role == UserRole.Owner ? "Role: style guide owner." : "Role: writer.";
        }
    }
}
