using System;
using System.IO;
using System.Text;
using FlareAI.Core.Settings;

namespace FlareAI.Core
{
    /// <summary>Append-only log at %APPDATA%\FlareAI\diag.log. Never log secrets or full prompts by default.</summary>
    public static class Diagnostics
    {
        private static readonly object Gate = new object();
        public static bool Enabled = true;

        public static string LogPath { get { return Path.Combine(PluginSettings.SettingsDirectory, "diag.log"); } }

        public static void Log(string message)
        {
            if (!Enabled) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(PluginSettings.SettingsDirectory);
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception) { /* diagnostics must never throw */ }
        }

        public static void Log(string context, Exception ex)
        {
            Log(context + " :: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
        }
    }
}
