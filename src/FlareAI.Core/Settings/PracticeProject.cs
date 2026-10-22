using System;
using System.IO;

namespace FlareAI.Core.Settings
{
    /// <summary>
    /// The practice project for the guided first lint: a tiny Flare project shipped in the plugin folder
    /// (Samples\HouseStylePractice) with planted issues, copied to the writer's Documents folder on first use.
    /// </summary>
    public static class PracticeProject
    {
        /// <summary>The topic the tour opens.</summary>
        public const string Topic = @"Content\Send money.htm";

        /// <summary>Documents\House Style Practice.</summary>
        public static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "House Style Practice"); }
        }

        /// <summary>The shipped copy inside the plugin folder.</summary>
        public static string Source(string pluginDir) { return Path.Combine(pluginDir, Path.Combine("Samples", "HouseStylePractice")); }

        /// <summary>
        /// Copies the practice project to <paramref name="destination"/> (files already there are kept, so the writer's
        /// own edits survive a second tour) and returns the full path of the practice topic.
        /// </summary>
        public static string Prepare(string pluginDir, string destination)
        {
            var source = Source(pluginDir);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The practice project is missing from " + source + ".");
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, file.Substring(source.Length).TrimStart('\\', '/'));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
                File.SetAttributes(target, FileAttributes.Normal);   // an install under Program Files may be read-only
            }
            return Path.Combine(destination, Topic);
        }
    }
}
