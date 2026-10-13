namespace FlareAI.Core
{
    /// <summary>"1 suggestion", "3 suggestions": counts in messages read as words, not "suggestion(s)".</summary>
    public static class Plural
    {
        public static string Of(int n, string singular)
        {
            return n + " " + (n == 1 ? singular : singular + "s");
        }
    }
}
