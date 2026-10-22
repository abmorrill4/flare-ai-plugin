namespace FlareAI.Core
{
    /// <summary>
    /// Severity tokens and their names in the UI. Tables and stored data keep the token "suggestion", which the UI
    /// shows as "Suggestion" (what lint finds is an "issue"). Reading also accepts "tip", the UI word of earlier versions.
    /// </summary>
    public static class Severity
    {
        public const string Error = "error", Warning = "warning", Tip = "suggestion";

        /// <summary>Lower-cases and maps "tip" to the stored token "suggestion"; other values pass through.</summary>
        public static string Normalize(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            return s == "tip" ? Tip : s;
        }

        /// <summary>"Error" / "Warning" / "Suggestion" for the UI ("off" shows as "Off").</summary>
        public static string Label(string token)
        {
            switch (Normalize(token))
            {
                case Error: return "Error";
                case Warning: return "Warning";
                case Tip: return "Suggestion";
                case "off": return "Off";
                case "on": return "On";
                default: return token ?? "";
            }
        }
    }
}
