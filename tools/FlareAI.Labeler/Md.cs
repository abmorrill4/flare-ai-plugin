using System;
using System.IO;
using System.Text;

namespace FlareAI.Labeler
{
    /// <summary>Generated sections of a project's LABELS.md: a block between <c>&lt;!-- labeler:tag --&gt;</c> markers, replaced in place.</summary>
    public static class Md
    {
        public static string Begin(string tag) { return "<!-- labeler:" + tag + " -->"; }
        public static string End(string tag) { return "<!-- /labeler:" + tag + " -->"; }

        /// <summary>Replaces the marked block in <paramref name="existing"/>, or appends one. Line endings of the existing text are kept.</summary>
        public static string Splice(string existing, string tag, string block)
        {
            bool crlf = existing.IndexOf("\r\n", StringComparison.Ordinal) >= 0;
            existing = existing.Replace("\r\n", "\n");
            string begin = Begin(tag), end = End(tag);
            int a = existing.IndexOf(begin, StringComparison.Ordinal);
            int b = existing.IndexOf(end, StringComparison.Ordinal);
            string result;
            if (a >= 0 && b > a)
            {
                int endLine = b + end.Length;
                if (endLine < existing.Length && existing[endLine] == '\n') endLine++;
                result = existing.Substring(0, a) + block + existing.Substring(endLine);
            }
            else result = existing.TrimEnd('\n') + "\n\n" + block;
            return crlf ? result.Replace("\n", "\r\n") : result;
        }

        public static void Update(string path, string tag, string block)
        {
            string existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            File.WriteAllText(path, Splice(existing, tag, block), new UTF8Encoding(false));
        }
    }
}
