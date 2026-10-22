using System;
using System.Collections.Generic;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// How an issue's change reads to a writer. A rule that deletes a phrase often takes the next letter along to fix its
    /// case ("Please note that y" to "Y"): that is a deletion of "Please note that", not a replacement, and reads as one.
    /// </summary>
    public static class ChangeText
    {
        /// <summary>
        /// The words a change deletes when it only deletes, else null: the replacement is empty, or it is one capital
        /// letter carried over from the excerpt's last letter ("Please note that y" to "Y": the next word now starts the
        /// sentence). A whole-word replacement ("In order to" to "To") is a change, not a deletion.
        /// </summary>
        public static string Deleted(string excerpt, string replacement)
        {
            excerpt = excerpt ?? "";
            if (replacement == null) return null;
            if (replacement.Length == 0) return excerpt.Trim().Length == 0 ? null : excerpt.Trim();
            if (replacement.Length != 1 || !char.IsUpper(replacement[0]) || excerpt.Length < 3) return null;
            var last = excerpt[excerpt.Length - 1];
            if (!char.IsLower(last) || char.ToUpperInvariant(last) != replacement[0] || excerpt[excerpt.Length - 2] != ' ') return null;
            var cut = excerpt.Substring(0, excerpt.Length - 1).Trim();
            return cut.Length == 0 ? null : cut;
        }

        /// <summary>A row's text: Delete "x", or Change "x" to "y" (each side cut to <paramref name="max"/> characters).</summary>
        public static string Row(string excerpt, string replacement, int max = 30)
        {
            var deleted = Deleted(excerpt, replacement);
            if (deleted != null) return "Delete \"" + Cut(deleted, Math.Max(max, 40)) + "\"";
            return "Change \"" + Cut(excerpt, max) + "\" to \"" + Cut(replacement, max) + "\"";
        }

        /// <summary>The card's "what changes" for an exact change: the deleted words struck through, or the word diff.</summary>
        public static List<DiffSegment> Diff(string excerpt, string replacement)
        {
            var deleted = Deleted(excerpt, replacement);
            if (deleted != null) return new List<DiffSegment> { new DiffSegment { Kind = DiffKind.Deleted, Text = deleted } };
            return ReadableDiff.OfText(excerpt, replacement);
        }

        private static string Cut(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }
    }
}
