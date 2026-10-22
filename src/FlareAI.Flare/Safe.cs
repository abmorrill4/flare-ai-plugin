using System;
using System.Windows.Forms;
using FlareAI.Core;

namespace FlareAI.Flare
{
    /// <summary>
    /// Guards the plugin's own entry points (ribbon commands, menu items, button clicks). An error is logged and shown
    /// once with the log path, so it never reaches Flare's or another vendor's process-wide exception handler.
    /// No global handlers are installed: they would swallow other plugins' errors too.
    /// </summary>
    internal static class Safe
    {
        public static void Run(string what, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                try { Diagnostics.Log(what, ex); } catch (Exception) { }
                try
                {
                    MessageBox.Show("House Style hit an error in \"" + what + "\".\n\n" + ex.Message + "\n\nDetails are in " + Diagnostics.LogPath,
                                    "House Style", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception) { }
            }
        }

        /// <summary>The same action, guarded, for handing to Flare's command and menu APIs.</summary>
        public static Action Wrap(string what, Action action) { return () => Run(what, action); }
    }
}
