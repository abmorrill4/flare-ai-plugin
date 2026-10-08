using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FlareAI.Core;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Shows a findings panel in Flare's own docking system (a pane on the right, like Flare's built-in panes);
    /// falls back to a floating window if that is unavailable. Everything that touches Flare's internal pane API
    /// (B3.AppKit, not part of the public plugin API) sits in <see cref="FlarePane"/> and runs under try/catch,
    /// so a Flare update can only downgrade the sidebar to a window, never break the plugin.
    /// </summary>
    public static class PanelHost
    {
        private static bool _paneBroken;

        public static void Show(FindingsPanel panel, Form flareMainForm)
        {
            if (!_paneBroken)
            {
                try
                {
                    if (FlarePane.Show(panel, flareMainForm)) { panel.Begin(); return; }
                }
                catch (Exception ex) // TypeLoadException, MissingMethodException, FileNotFoundException, InvalidCastException, …
                {
                    _paneBroken = true;
                    Diagnostics.Log("Docked pane unavailable, using a floating window", ex);
                }
            }
            ShowFloating(panel, flareMainForm);
        }

        private static void ShowFloating(FindingsPanel panel, Form owner)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            var form = new Form
            {
                Text = panel.Title, StartPosition = FormStartPosition.Manual, Size = new Size(720, 460),
                MinimumSize = new Size(320, 260), ShowInTaskbar = false, KeyPreview = true, Font = SystemFonts.MessageBoxFont
            };
            form.Location = new Point(area.Right - form.Width - 24, area.Bottom - form.Height - 24);
            panel.Dock = DockStyle.Fill;
            form.Controls.Add(panel);
            panel.CloseRequested += (s, e) => form.Close();
            form.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
            form.Show(owner);
            panel.Begin();
        }
    }

    /// <summary>The only code that depends on Flare internals. Keep it small.</summary>
    internal static class FlarePane
    {
        private static B3.AppKit.Pane _pane;
        private static SidebarView _view;

        /// <summary>Shows the panel in a pane docked right; false if Flare's main form has no pane dock.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // B3.AppKit is resolved when this method is JIT-compiled, i.e. inside the caller's try
        public static bool Show(FindingsPanel panel, Form flareMainForm)
        {
            var editor = flareMainForm as B3.AppKit.Editor;
            var dock = editor == null ? null : editor.PaneDock;
            if (dock == null)
            {
                Diagnostics.Log("Docked pane: main form is " + (flareMainForm == null ? "null" : flareMainForm.GetType().FullName) + ", no PaneDock");
                return false;
            }

            if (_pane == null || _pane.IsDisposed || _view == null || _view.IsDisposed)
            {
                _view = new SidebarView();
                _pane = new B3.AppKit.Pane { Name = "FlareAI.Findings", Title = panel.Title, View = _view };
                // Flare picks the group (the left panes by default); the author can drag it anywhere and Flare's
                // layout remembers it by pane name. (Installing under another pane replaces that pane's view.)
                dock.InstallPane(_pane);
                dock.PinPane(_pane, DockStyle.Right);
            }
            _view.SetContent(panel);
            _pane.Title = panel.Title;
            panel.CloseRequested += (s, e) => Hide(dock);
            _pane.BringPaneToFront(true);
            return true;
        }

        private static void Hide(B3.AppKit.IPaneDock dock)
        {
            try
            {
                if (_pane != null && !_pane.IsDisposed) dock.UninstallPane(_pane);
            }
            catch (Exception ex) { Diagnostics.Log("Docked pane close", ex); }
            _pane = null;
            _view = null;
        }

        /// <summary>A Flare pane view that hosts one panel at a time (a new check replaces the previous one).</summary>
        private sealed class SidebarView : B3.AppKit.PaneView
        {
            public void SetContent(Control content)
            {
                foreach (Control old in Controls) old.Dispose();
                Controls.Clear();
                content.Dock = DockStyle.Fill;
                Controls.Add(content);
            }
        }
    }
}
