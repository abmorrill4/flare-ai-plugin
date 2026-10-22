using System;
using System.IO;

namespace FlareAI.Core
{
    /// <summary>
    /// The writer's own data folder (settings, logs, dismissals, review cache, writer id). One place, so tests and
    /// harnesses can redirect it to a temp folder instead of the real <c>%APPDATA%\FlareAI</c>.
    /// </summary>
    public static class Paths
    {
        private static string _root;

        /// <summary>The default root: <c>%APPDATA%\FlareAI</c>.</summary>
        public static string DefaultRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlareAI"); }
        }

        /// <summary>The root in effect. Setting null (or empty) restores <see cref="DefaultRoot"/>.</summary>
        public static string Root
        {
            get { return string.IsNullOrEmpty(_root) ? DefaultRoot : _root; }
            set { _root = value; }
        }

        /// <summary>True when <see cref="Root"/> has been redirected away from the default.</summary>
        public static bool IsRedirected { get { return !string.IsNullOrEmpty(_root); } }

        /// <summary>A file or folder under <see cref="Root"/>.</summary>
        public static string In(string name) { return Path.Combine(Root, name); }

        public static string Settings { get { return In("settings.json"); } }
        public static string DiagLog { get { return In("diag.log"); } }
        public static string Feedback { get { return In("feedback.jsonl"); } }
        public static string Dismissed { get { return In("dismissed.json"); } }
        public static string Reviews { get { return In("reviews.json"); } }
        public static string WriterId { get { return In("writer-id.txt"); } }
    }
}
