using System;
using System.Collections.Generic;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// Files the plugin itself is saving (a project-wide Apply opens, writes, saves and closes them). The check-on-save
    /// notice is for the writer's own saves; it must stay quiet for these. A save is remembered for a short window
    /// because Flare may raise its Saved event after Save() returns.
    /// </summary>
    public static class SelfSaves
    {
        private static readonly Dictionary<string, DateTime> Marked = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        /// <summary>How long after <see cref="Mark"/> a Saved event still counts as the plugin's own.</summary>
        public static TimeSpan Window = TimeSpan.FromSeconds(10);

        /// <summary>Call just before the plugin saves <paramref name="path"/>.</summary>
        public static void Mark(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (Gate) Marked[path] = DateTime.UtcNow;
        }

        /// <summary>True when <paramref name="path"/> was saved by the plugin a moment ago.</summary>
        public static bool IsOwn(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (Gate)
            {
                DateTime at;
                if (!Marked.TryGetValue(path, out at)) return false;
                if (DateTime.UtcNow - at <= Window) return true;
                Marked.Remove(path);
                return false;
            }
        }
    }
}
