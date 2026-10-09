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
                RefreshTeam();
                CreateRibbon();
                CreateToolStrip();
                try { _host.GetEditorContext().DocumentSwitched += OnDocumentSwitched; } catch (Exception ex) { Diagnostics.Log("subscribe", ex); }
                try { _host.GetCompilerContext().CompileComplete += OnCompileComplete; } catch (Exception ex) { Diagnostics.Log("subscribe compile", ex); }
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
            try { _host.GetCompilerContext().CompileComplete -= OnCompileComplete; } catch (Exception) { }
            _style = null;
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
            actions.AddRibbonButton(Button("Check style", "C", OnCheckStyle, "check"));
            actions.AddRibbonButton(Button("Rewrite selection", "R", OnRewrite, "rewrite"));
            actions.AddRibbonButton(Button("Ask…", "A", OnAsk, "ask"));
            actions.AddRibbonButton(Button("Check project", "P", OnCheckProject, "project"));
            actions.AddRibbonButton(Button("Topic assists", "I", OnAssist, "assist"));

            IRibbonGroup tools = tab.AddNewRibbonGroup("Plugin");
            tools.AddRibbonButton(Button("Settings", "S", OnSettings, "settings"));
            tools.AddRibbonButton(Button("Validate style guide", "V", OnValidateGuide, "validate"));
            tools.AddRibbonButton(Button("Tune style guide", "T", OnTuneGuide, "tune"));
            tools.AddRibbonButton(Button("Build style guide", "B", OnBuildGuide, "build"));

            IRibbonGroup output = tab.AddNewRibbonGroup("Output");
            output.AddRibbonButton(Button("Export for LLMs", "L", OnExportLlm, "llm"));
            tools.AddRibbonButton(Button("Diagnostics", "D", OnDiagnostics, "diag"));
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
                menu.AddMenuItem("Check style", new DelegateCommand(OnCheckStyle), null, IconPath("check-16.png"));
                menu.AddMenuItem("Rewrite selection", new DelegateCommand(OnRewrite), null, IconPath("rewrite-16.png"));
                menu.AddMenuItem("Ask…", new DelegateCommand(OnAsk), null, IconPath("ask-16.png"));
                menu.AddMenuItem("Check project", new DelegateCommand(OnCheckProject), null, IconPath("project-16.png"));
                menu.AddSeparator();
                menu.AddMenuItem("Settings", new DelegateCommand(OnSettings), null, IconPath("settings-16.png"));
                menu.AddMenuItem("Validate style guide", new DelegateCommand(OnValidateGuide), null, IconPath("validate-16.png"));
                menu.AddMenuItem("Diagnostics", new DelegateCommand(OnDiagnostics), null, IconPath("diag-16.png"));

                var bar = _nav.CreateCustomToolBar("FlareAI");
                bar.AddButton("Check style", new DelegateCommand(OnCheckStyle), null, IconPath("check-16.png"), RibbonIconSize.Small);
                bar.AddButton("Rewrite", new DelegateCommand(OnRewrite), null, IconPath("rewrite-16.png"), RibbonIconSize.Small);
                bar.AddButton("Ask…", new DelegateCommand(OnAsk), null, IconPath("ask-16.png"), RibbonIconSize.Small);
            }
            catch (Exception ex) { Diagnostics.Log("Tool Strip entries", ex); }
        }

        private RibbonControlData Button(string label, string keyTip, Action onClick, string icon)
        {
            return new RibbonControlData
            {
                Label = label, KeyTip = keyTip, Command = new DelegateCommand(onClick),
                LargeImage = Image(icon + ".png"), SmallImage = Image(icon + "-16.png")
            };
        }

        /// <summary>Full path of a bundled icon (Icons\*.png, made by scripts\make-icons.ps1), or null if missing.</summary>
        private string IconPath(string file)
        {
            var path = Path.Combine(PluginDirectory, Path.Combine("Icons", file));
            return File.Exists(path) ? path : null;
        }

        private System.Windows.Media.ImageSource Image(string file)
        {
            var path = IconPath(file);
            if (path == null) return null;
            try { return new System.Windows.Media.Imaging.BitmapImage(new Uri(path)); }
            catch (Exception ex) { Diagnostics.Log("icon " + file, ex); return null; }
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

        /// <summary>Lints every topic and snippet in the active topic's project (no AI cost) into the findings view.</summary>
        private void OnCheckProject()
        {
            try
            {
                var adapter = ActiveAdapter();
                var files = ActionRunner.ProjectFiles(adapter == null ? null : adapter.SourcePath);
                if (adapter == null || files.Count == 0)
                {
                    MessageBox.Show("Open any topic of the project first (Check project scans that project's Content folder).", "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var runner = NewRunner(adapter);
                if (runner == null) return;
                var root = Path.GetDirectoryName(files[0]);
                while (root != null && Directory.GetFiles(root, "*.flprj").Length == 0) root = Path.GetDirectoryName(root);
                var name = root == null ? "" : Path.GetFileName(root);
                PanelHost.Show(FindingsPanel.ForProject(runner, adapter, () => NewRunner(adapter), OpenAdapter, files, name), _nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Check project", ex);
                MessageBox.Show(ex.Message, "FlareAI — Check project", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSettings()
        {
            using (var f = new SettingsForm()) if (f.ShowDialog() == DialogResult.OK) RefreshTeam();
        }

        /// <summary>
        /// Who is writing, who owns the guide, where shared feedback goes; then (in the background) whether the guide
        /// checkout is behind the team's latest.
        /// </summary>
        private void RefreshTeam()
        {
            try
            {
                var settings = PluginSettings.Load();
                FeedbackLog.PersonalPath = Path.Combine(PluginSettings.SettingsDirectory, "feedback.jsonl");
                var team = Team.Load(settings.GuideRoot(PluginDirectory), settings.FeedbackFolder);
                Team.Current = team;
                PromptBuilder.DefaultOverrideFolder = team.GuideRoot == null ? null : Path.Combine(team.GuideRoot, Path.Combine("FlareAI", "prompts"));
                Diagnostics.Log("Team: writer=" + team.Writer + " guide=" + team.GuideRoot + " strict=" + team.IsStrict + " owner=" + team.IsOwner + " feedback=" + (team.FeedbackFolder ?? "(personal)"));
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { team.Status = Team.ReadStatus(team.GuideRoot, true); if (team.Status != null) Diagnostics.Log(team.Status.ToString()); }
                    catch (Exception ex) { Diagnostics.Log("guide status", ex); }
                });
            }
            catch (Exception ex) { Diagnostics.Log("Team", ex); }
        }

        /// <summary>Meta description, index keywords, synonyms, micro content and glossary candidates for the open topic.</summary>
        private void OnAssist()
        {
            try
            {
                var adapter = ActiveAdapter();
                if (adapter == null || !adapter.IsEditableTopic) { MessageBox.Show("Open a topic in the XML Editor first.", "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                var runner = NewRunner(adapter);
                if (runner == null) return;
                using (var f = new AssistForm(runner, adapter)) f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Topic assists", ex);
                MessageBox.Show(ex.Message, "FlareAI — Topic assists", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- LLM-ready output ------------------------------------------------------------------

        /// <summary>After a build: when enabled, add Markdown + llms.txt to the output that was just built (in the background).</summary>
        private void OnCompileComplete(object sender, EventArgs e)
        {
            try
            {
                var settings = PluginSettings.Load();
                if (!settings.LlmExportAfterBuild) return;
                var adapter = ActiveAdapter();
                var output = NewestOutput(adapter == null ? null : adapter.SourcePath, TimeSpan.FromMinutes(10));
                if (output == null) { Diagnostics.Log("LLM export: no fresh HTML output found after the build"); return; }
                System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { Diagnostics.Log("LLM export after build: " + RunLlmExport(settings, output)); } catch (Exception ex) { Diagnostics.Log("LLM export", ex); } });
            }
            catch (Exception ex) { Diagnostics.Log("CompileComplete", ex); }
        }

        /// <summary>Exports the newest HTML output of the open project (or a folder the author picks) as Markdown + llms.txt.</summary>
        private void OnExportLlm()
        {
            try
            {
                var settings = PluginSettings.Load();
                var adapter = ActiveAdapter();
                var output = NewestOutput(adapter == null ? null : adapter.SourcePath, TimeSpan.MaxValue);
                using (var dlg = new FolderBrowserDialog { Description = "Built HTML5 or Clean XHTML output to export (Markdown per topic + llms.txt are added to it):", SelectedPath = output ?? "" })
                {
                    if (dlg.ShowDialog(_nav.GetParentForm()) != DialogResult.OK) return;
                    output = dlg.SelectedPath;
                }
                var summary = RunLlmExport(settings, output);
                Diagnostics.Log("LLM export: " + summary);
                if (MessageBox.Show(summary + "\n\nOpen the folder?", "FlareAI — Export for LLMs", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + output + "\"");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Export for LLMs", ex);
                MessageBox.Show(ex.Message, "FlareAI — Export for LLMs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Turns the built output into Markdown per topic + llms.txt; returns a one-line summary.</summary>
        private static string RunLlmExport(PluginSettings settings, string output)
        {
            var r = FlareAI.Core.Output.LlmExport.Export(output.TrimEnd('\\'));
            return r.Topics + " topic(s) exported as Markdown to " + r.Markdown + ", with llms.txt and llms-full.txt in " + output + ".";
        }

        /// <summary>The most recently built output folder of the project (Output\user\Target or Output\Target), or null.</summary>
        private static string NewestOutput(string anyProjectPath, TimeSpan maxAge)
        {
            if (string.IsNullOrEmpty(anyProjectPath)) return null;
            var dir = Path.GetDirectoryName(anyProjectPath);
            while (dir != null && Directory.GetFiles(dir, "*.flprj").Length == 0) dir = Path.GetDirectoryName(dir);
            if (dir == null || !Directory.Exists(Path.Combine(dir, "Output"))) return null;
            DirectoryInfo best = null;
            foreach (var d in new DirectoryInfo(Path.Combine(dir, "Output")).GetDirectories("*", SearchOption.AllDirectories))
            {
                if (d.Name.Equals("Temporary", StringComparison.OrdinalIgnoreCase) || d.FullName.IndexOf("\\Temporary\\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (d.GetFiles("*.htm").Length == 0 && d.GetFiles("*.html").Length == 0) continue;     // a target root holds Default.htm / index files
                if (d.Parent != null && best != null && d.FullName.StartsWith(best.FullName + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || d.LastWriteTimeUtc > best.LastWriteTimeUtc) best = d;
            }
            if (best == null || DateTime.UtcNow - best.LastWriteTimeUtc > maxAge) return null;
            // Walk up to the target folder (the one directly under Output or Output\user).
            var root = new DirectoryInfo(Path.Combine(dir, "Output")).FullName;
            var t = best;
            while (t.Parent != null && t.Parent.Parent != null && !t.Parent.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)
                   && !t.Parent.Parent.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)) t = t.Parent;
            return t.FullName;
        }

        /// <summary>Proposes style-guide rules from the open project's content and from notes.</summary>
        private void OnBuildGuide()
        {
            try
            {
                var settings = PluginSettings.Load();
                var style = StyleService(settings);
                var adapter = ActiveAdapter();
                var project = adapter == null ? null : adapter.SourcePath;
                using (var f = new BuildGuideForm(Team.Current, style, project,
                           () => adapter == null ? NewRunnerWithoutTopic(settings, style, null) : NewRunner(adapter, false), OpenAdapter))
                    f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Build style guide", ex);
                MessageBox.Show(ex.Message, "FlareAI — Build style guide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>The owner's report: what the team's review decisions say about each rule.</summary>
        private void OnTuneGuide()
        {
            try
            {
                var settings = PluginSettings.Load();
                var style = StyleService(settings);
                var adapter = ActiveAdapter();
                var project = settings.UseProjectLayer && adapter != null ? adapter.SourcePath : null;
                using (var f = new TuneGuideForm(Team.Current, style, project, OpenAdapter, doc => NewRunner(doc as FlareDocumentAdapter, false)))
                    f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Tune style guide", ex);
                MessageBox.Show(ex.Message, "FlareAI — Tune style guide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Tests the layered style guide (tables against examples; optionally prose rules against the AI).</summary>
        private void OnValidateGuide()
        {
            try
            {
                var settings = PluginSettings.Load();
                var style = StyleService(settings);
                if (style == null) { MessageBox.Show("The style linter is turned off in Settings.", "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                var adapter = ActiveAdapter();
                var project = settings.UseProjectLayer && adapter != null ? adapter.SourcePath : null;
                using (var f = new GuideReportForm(style, () => adapter == null ? NewRunnerWithoutTopic(settings, style, project) : NewRunner(adapter), project))
                    f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Validate style guide", ex);
                MessageBox.Show(ex.Message, "FlareAI — Validate style guide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private ActionRunner NewRunnerWithoutTopic(PluginSettings settings, StyleEngine style, string project)
        {
            ILlmClient llm;
            try { llm = LlmClientFactory.Create(settings); }
            catch (LlmException lex) { MessageBox.Show(lex.Message, "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
            return new ActionRunner(llm, PromptBuilder.For(style, project), settings.MaxTokens, style, project, RoutingFor(settings))
            { Parallelism = settings.Parallelism > 0 ? settings.Parallelism : 6 };
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
                        var picked = Core.Editing.BlockSelector.SelectBlocks(adapter.Document, adapter.SelectionNodes);
                        foreach (var b in picked)
                            sb.AppendLine("  <" + b.Name + "> " + Shorten(b.InnerText, 60));
                        // How Flare holds pending tracked changes in memory (for the tracked-change readers).
                        var ns = new System.Xml.XmlNamespaceManager(adapter.Document.NameTable);
                        ns.AddNamespace("MadCap", Core.Editing.BlockSelector.MadCapNs);
                        var data = adapter.Document.SelectSingleNode("//MadCap:changeData", ns);
                        Diagnostics.Log("DIAG changeData: " + (data == null ? "(none)" : Shorten(data.OuterXml, 1500)));
                        if (picked.Count > 0) Diagnostics.Log("DIAG first block XML: " + Shorten(picked[0].OuterXml, 3000));
                    }
                }
                var settingsNow = PluginSettings.Load();
                sb.AppendLine().Append(RoutingFor(settingsNow).Describe());
                var usage = Usage.Snapshot();
                if (usage.Count > 0)
                {
                    sb.AppendLine("AI use this session:");
                    foreach (var u in usage)
                        sb.AppendLine("  " + u.Task + " · " + u.Model + ": " + u.Calls + " call(s), " + u.Input + " in (" + u.CacheRead + " cached) / " + u.Output + " out, ~$" + u.Usd.ToString("0.000") + ", " + u.Seconds.ToString("0") + " s");
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
            return NewRunner(adapter, true);
        }

        /// <param name="projectLayer">False for style-guide topics: the guide project is already a layer.</param>
        private ActionRunner NewRunner(FlareDocumentAdapter adapter, bool projectLayer)
        {
            if (adapter == null) return null;
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
            var project = settings.UseProjectLayer && projectLayer ? topicPath : null;
            return new ActionRunner(llm, PromptBuilder.For(style, project), settings.MaxTokens, style, project, RoutingFor(settings))
            {
                Parallelism = settings.Parallelism > 0 ? settings.Parallelism : 6,
                VariableValue = adapter.VariableValue
            };
        }

        private static Routing RoutingFor(PluginSettings settings)
        {
            return settings.Routing(Team.Current == null ? null : Team.Current.GuideRoot);
        }

        private StyleEngine _style;
        private string _styleKey;

        /// <summary>The in-process style guide and linter; rebuilt when the configured guide projects change.</summary>
        private StyleEngine StyleService(PluginSettings settings)
        {
            var roots = settings.GuideRoots(PluginDirectory);
            var key = string.Join("|", roots.ToArray());
            if (_style == null || _styleKey != key)
            {
                _style = new StyleEngine(roots, PluginSettings.BundledGuide(PluginDirectory));
                _styleKey = key;
            }
            return _style;
        }

        private static readonly ReviewCache Reviews = new ReviewCache(300, Path.Combine(PluginSettings.SettingsDirectory, "reviews.json"));

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
                if (current == null || current.IsProjectReport) return;
                var adapter = ActiveAdapter();
                if (adapter == null || !adapter.IsEditableTopic) return;
                var path = adapter.SourcePath ?? "";
                if (string.Equals(path, current.TopicPath, StringComparison.OrdinalIgnoreCase)) return;
                if (path.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase)) return;
                // Style-guide topics list the very words they ban; following into one (e.g. via Guide ↗) would flood the panel.
                if (path.IndexOf(@"\Resources\StyleGuide\", StringComparison.OrdinalIgnoreCase) >= 0) return;
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
                var ctx = _host.GetEditorContext();
                var wasOpen = false;
                foreach (var d in ctx.GetDocuments())
                {
                    var open = d as IDocument;
                    if (open != null && string.Equals(open.GetSourceUrl(), path, StringComparison.OrdinalIgnoreCase)) { wasOpen = true; break; }
                }
                var doc = ctx.OpenDocument(path);
                return doc == null ? null : new FlareDocumentAdapter(doc) { OpenedByPlugin = !wasOpen };
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
