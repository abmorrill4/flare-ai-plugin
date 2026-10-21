using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// The writer's own style guide: a copy of the bundled guide's own layer (its Company topics, glossary, condition tags
    /// and layer name), made on the first rule change. The generated Google folder is never copied: the Google baseline
    /// keeps coming from the plugin, so plugin updates still reach it.
    /// </summary>
    public static class GuideSeed
    {
        private static readonly string GoogleFolder = Path.Combine(Path.Combine(Path.Combine("Content", "Resources"), "StyleGuide"), "Google");

        /// <summary>True when the folder is a style guide project (it has a .flprj).</summary>
        public static bool IsSeeded(string root)
        {
            try { return !string.IsNullOrEmpty(root) && Directory.Exists(root) && Directory.GetFiles(root, "*.flprj").Length > 0; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Creates <paramref name="target"/> from <paramref name="source"/> unless it is already a guide project. Existing
        /// files are kept; the .flprj is written last, so a copy that fails part way is completed next time.
        /// Throws <see cref="GuideWriteException"/> naming the folder when it can't be written.
        /// </summary>
        public static void Ensure(string source, string target)
        {
            if (IsSeeded(target)) return;
            try
            {
                Directory.CreateDirectory(target);
                var projects = new List<KeyValuePair<string, string>>();
                if (!string.IsNullOrEmpty(source) && Directory.Exists(source))
                {
                    var root = Path.GetFullPath(source).TrimEnd('\\', '/');
                    foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        var rel = file.Substring(root.Length + 1);
                        if (rel.Equals(GoogleFolder, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(GoogleFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        var to = Path.Combine(target, rel);
                        if (rel.IndexOf(Path.DirectorySeparatorChar) < 0 && rel.EndsWith(".flprj", StringComparison.OrdinalIgnoreCase)) { projects.Add(new KeyValuePair<string, string>(file, to)); continue; }
                        if (File.Exists(to)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(file, to);
                        File.SetAttributes(to, FileAttributes.Normal);   // an install folder may hand out read-only copies
                    }
                }
                Directory.CreateDirectory(Path.Combine(target, Path.Combine(Path.Combine("Content", "Resources"), Path.Combine("StyleGuide", "Company"))));
                if (projects.Count == 0) File.WriteAllText(Path.Combine(target, "StyleGuide.flprj"), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<CatapultProject Version=\"1\" />\r\n");
                foreach (var p in projects.Where(p => !File.Exists(p.Value))) { File.Copy(p.Key, p.Value); File.SetAttributes(p.Value, FileAttributes.Normal); }
                Diagnostics.Log("Created your style guide at " + target + (string.IsNullOrEmpty(source) ? "" : " from " + source));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                throw new GuideWriteException(target, ex);
            }
        }
    }

    /// <summary>A style guide change that couldn't be written. The message names the file or folder, so the writer can fix it.</summary>
    public sealed class GuideWriteException : IOException
    {
        /// <summary>The file or folder that couldn't be written.</summary>
        public string Target { get; private set; }

        public GuideWriteException(string target, Exception inner)
            : base("House Style couldn't write to " + target + ": " + (inner == null ? "" : inner.Message), inner)
        {
            Target = target;
        }
    }
}
