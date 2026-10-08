using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using B3.PluginAPIKit;
using FlareAI.Core;
using FlareAI.Core.Editing;
using FlareAI.Core.Actions;
using FlareAI.Core.Llm;
using FlareAI.Core.Settings;
using FlareAI.Core.Style;
using FlareAI.Flare.UI;

namespace FlareAI.Flare
{
    /// <summary>
    /// Entry point Flare discovers in Flare.app\Plugins\FlareAI. Adds an "AI" ribbon tab.
    /// Everything interesting lives in FlareAI.Core; this class only wires Flare to it.
    /// </summary>
    public class Plugin : IPlugin
    {
        private IHost _host;
        private INavContext _nav;
        private bool _activated;

        public bool IsActivated { get { return _activated; } }
        public string GetVersion() { return "0.1.0"; }
        public string GetAuthor() { return "Alkami Enterprise Technology"; }
        public string GetDescription() { return "AI editing assistant: check style, rewrite, ask. Edits arrive as tracked changes."; }
        public string GetName() { return "FlareAI"; }

        public void Initialize(IHost host)
        {
            _host = host; // docs: do nothing else here
        }

        public void Execute()
        {
            try
            {
                Diagnostics.Enabled = PluginSettings.Load().WriteDiagnostics;
                Diagnostics.Log("FlareAI starting in " + _host.GetName() + " (" + _host.GetCompany() + "), plugin dir " + PluginDirectory);
                _nav = _host.GetNavContext();
                CreateRibbon();
                CreateToolStrip();
                try { _host.GetEditorContext().DocumentSwitched += OnDocumentSwitched; } catch (Exception ex) { Diagnostics.Log("subscribe", ex); }
                _activated = true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Execute", ex);
                MessageBox.Show("FlareAI failed to start: " + ex.Message, "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void Stop()
        {
            try { _host.GetEditorContext().DocumentSwitched -= OnDocumentSwitched; } catch (Exception) { }
            ResetStyleService();
            try { _host.Dispose(); } catch (Exception ex) { Diagnostics.Log("Stop", ex); }
            _activated = false;
        }

        // ---- ribbon ------------------------------------------------------------------

        private void CreateRibbon()
        {
            // Per the Ribbon Example: ribbon.AddNewRibbonTab(label, keyTip) → tab.AddNewRibbonGroup(label) → group.AddRibbonButton(RibbonControlData)
            IRibbon ribbon = _nav.GetRibbon();
            IRibbonTab tab = ribbon.AddNewRibbonTab("AI", "Q");
            IRibbonGroup actions = tab.AddNewRibbonGroup("Assist");
            actions.AddRibbonButton(Button("Check style", "C", OnCheckStyle, "check.png"));
            actions.AddRibbonButton(Button("Rewrite selection", "R", OnRewrite, "rewrite.png"));
            actions.AddRibbonButton(Button("Ask…", "A", OnAsk, "ask.png"));

            IRibbonGroup tools = tab.AddNewRibbonGroup("Plugin");
            tools.AddRibbonButton(Button("Settings", "S", OnSettings, "settings.png"));
            tools.AddRibbonButton(Button("Diagnostics", "D", OnDiagnostics, "diag.png"));
        }

        /// <summary>
        /// Same actions for Flare's Tool Strip UI mode, where the ribbon tab is invisible: an "AI" menu and toolbar.
        /// Failures are logged only — the ribbon is the primary surface.
        /// </summary>
        private void CreateToolStrip()
        {
            try
            {
                var menu = _nav.CreateToolStripMenu("AI", "Q");
                menu.AddMenuItem("Check style", new DelegateCommand(OnCheckStyle));
                menu.AddMenuItem("Rewrite selection", new DelegateCommand(OnRewrite));
                menu.AddMenuItem("Ask…", new DelegateCommand(OnAsk));
                menu.AddSeparator();
                menu.AddMenuItem("Settings", new DelegateCommand(OnSettings));
                menu.AddMenuItem("Diagnostics", new DelegateCommand(OnDiagnostics));

                var bar = _nav.CreateCustomToolBar("FlareAI");
                bar.AddButton("Check style", new DelegateCommand(OnCheckStyle));
                bar.AddButton("Rewrite", new DelegateCommand(OnRewrite));
                bar.AddButton("Ask…", new DelegateCommand(OnAsk));
            }
            catch (Exception ex) { Diagnostics.Log("Tool Strip entries", ex); }
        }

        private RibbonControlData Button(string label, string keyTip, Action onClick, string icon)
        {
            var data = new RibbonControlData { Label = label, KeyTip = keyTip, Command = new DelegateCommand(onClick) };
            var path = Path.Combine(PluginDirectory, Path.Combine("Icons", icon));
            if (File.Exists(path))
            {
                try { data.LargeImage = new System.Windows.Media.Imaging.BitmapImage(new Uri(path)); }
                catch (Exception ex) { Diagnostics.Log("icon " + icon, ex); }
            }
            return data;
        }

        // ---- actions -----------------------------------------------------------------

        private void OnCheckStyle() { RunAction("Check style", ActionKind.CheckStyle, null); }
        private void OnRewrite() { RunAction("Rewrite selection", ActionKind.Rewrite, null); }

        private void OnAsk()
        {
            using (var f = new AskForm())
            {
                if (f.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(f.Instruction)) return;
                RunAction("Ask", ActionKind.Ask, f.Instruction);
            }
        }

        private void OnSettings()
        {
            using (var f = new SettingsForm()) f.ShowDialog();
        }

        private void OnDiagnostics()
        {
            try
            {
                var adapter = ActiveAdapter();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Doc: " + (adapter == null ? "(none)" : adapter.SourcePath));
                if (adapter != null)
                {
                    sb.AppendLine("Editable topic: " + adapter.IsEditableTopic);
                    sb.AppendLine("Selection text: \"" + adapter.SelectionText + "\"");
                    sb.AppendLine("Selection nodes:");
                    foreach (var n in adapter.SelectionNodes) sb.AppendLine("  " + n.NodeType + " " + n.Name + " parent=" + (n.ParentNode == null ? "-" : n.ParentNode.Name));
                    if (adapter.IsEditableTopic)
                    {
                        sb.AppendLine("Blocks BlockSelector would pick:");
                        foreach (var b in Core.Editing.BlockSelector.SelectBlocks(adapter.Document, adapter.SelectionNodes))
                            sb.AppendLine("  <" + b.Name + "> " + Shorten(b.InnerText, 60));
                    }
                }
                Diagnostics.Log("DIAG\n" + sb);
                MessageBox.Show(sb + "\n(written to " + Diagnostics.LogPath + ")", "FlareAI diagnostics");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Diagnostics", ex);
                MessageBox.Show(ex.ToString(), "FlareAI diagnostics error");
            }
        }

        private void RunAction(string title, ActionKind kind, string instruction)
        {
            try
            {
                var adapter = ActiveAdapter();
                if (adapter == null || !adapter.IsEditableTopic)
                {
                    MessageBox.Show("Open a topic in the XML Editor first.", "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var runner = NewRunner(adapter);
                if (runner == null) return;
                if (kind == ActionKind.CheckStyle)
                {
                    // Docked (or floating) and modeless: the author works through the list while editing.
                    ShowFindings(adapter, runner, true);
                    return;
                }
                using (var f = new ResultForm(title, runner, adapter, kind, instruction))
                    f.ShowDialog();
            }
            catch (Exception ex)
            {
                Diagnostics.Log(title, ex);
                MessageBox.Show(ex.Message, "FlareAI — " + title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Fresh settings + LLM client per action; the docstyle server process is shared. Null if not configured.</summary>
        private ActionRunner NewRunner(FlareDocumentAdapter adapter)
        {
            var topicPath = adapter.SourcePath;
            var settings = PluginSettings.Load();
            ILlmClient llm;
            try { llm = LlmClientFactory.Create(settings); }
            catch (LlmException lex)
            {
                MessageBox.Show(lex.Message, "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                OnSettings();
                return null;
            }
            var style = StyleService(settings);
            var project = settings.UseProjectLayer ? topicPath : null;
            return new ActionRunner(llm, new PromptBuilder(style == null ? (Func<string>)null : () => style.Guide(project)), settings.MaxTokens, style, project)
            {
                VariableValue = adapter.VariableValue
            };
        }

        private McpStyleService _style;
        private string _styleKey;

        private McpStyleService StyleService(PluginSettings settings)
        {
            if (string.IsNullOrEmpty(settings.StyleServerCommand)) { ResetStyleService(); return null; }
            var args = settings.StyleServerArgsFor(PluginDirectory);
            var key = settings.StyleServerCommand + " " + args;
            if (_style == null || _styleKey != key)
            {
                ResetStyleService();
                _style = new McpStyleService(settings.StyleServerCommand, args, PluginDirectory, 60000);
                _styleKey = key;
            }
            return _style;
        }

        private void ResetStyleService()
        {
            if (_style != null) { try { _style.Dispose(); } catch (Exception ex) { Diagnostics.Log("docstyle stop", ex); } }
            _style = null;
        }

        private static readonly ReviewCache Reviews = new ReviewCache();

        private void ShowFindings(FlareDocumentAdapter adapter, ActionRunner runner, bool aiOnStart)
        {
            PanelHost.Show(new FindingsPanel(runner, adapter, () => NewRunner(adapter), OpenAdapter, Reviews, aiOnStart), _nav.GetParentForm());
        }

        /// <summary>
        /// While the findings view is open it follows the active topic: instant lint for the new topic, plus the AI
        /// review only if this exact content was reviewed before (cache). Snippet tabs opened by our own fixes don't count.
        /// </summary>
        private void OnDocumentSwitched(object sender, EventArgs e)
        {
            try
            {
                var current = PanelHost.Current;
                if (current == null) return;
                var adapter = ActiveAdapter();
                if (adapter == null || !adapter.IsEditableTopic) return;
                var path = adapter.SourcePath ?? "";
                if (string.Equals(path, current.TopicPath, StringComparison.OrdinalIgnoreCase)) return;
                if (path.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase)) return;
                var runner = NewRunner(adapter);
                if (runner != null) ShowFindings(adapter, runner, false);
            }
            catch (Exception ex) { Diagnostics.Log("DocumentSwitched", ex); }
        }

        /// <summary>Opens (or activates) a file in Flare's editor and wraps it; null if Flare can't open it.</summary>
        private IDocumentAdapter OpenAdapter(string path)
        {
            try
            {
                var doc = _host.GetEditorContext().OpenDocument(path);
                return doc == null ? null : new FlareDocumentAdapter(doc);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("open " + path, ex);
                return null;
            }
        }

        private FlareDocumentAdapter ActiveAdapter()
        {
            var ctx = _host.GetEditorContext();
            if (ctx == null) return null;
            var doc = ctx.GetActiveDocument();
            return doc == null ? null : new FlareDocumentAdapter(doc);
        }

        private static string PluginDirectory
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        private static string Shorten(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length <= n ? s : s.Substring(0, n) + "…"; }
    }
}
