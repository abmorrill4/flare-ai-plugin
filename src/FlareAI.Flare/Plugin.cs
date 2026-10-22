using System;
using System.Collections.Generic;
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
    /// Entry point Flare discovers in Flare.app\Plugins\FlareAI. Adds a "House Style" ribbon tab.
    /// Everything interesting lives in FlareAI.Core; this class only wires Flare to it.
    /// </summary>
    public class Plugin : IPlugin
    {
        private IHost _host;
        private INavContext _nav;
        private bool _activated;
        private UpdateFlow _updates;

        public bool IsActivated { get { return _activated; } }
        public string GetVersion() { return Version; }

        /// <summary>The release version stamped at build time (package.ps1 -Version), without the "+commit" suffix.</summary>
        internal static string Version
        {
            get
            {
                var a = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(Plugin).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                var v = a != null ? a.InformationalVersion : typeof(Plugin).Assembly.GetName().Version.ToString();
                var plus = v.IndexOf('+');
                return plus > 0 ? v.Substring(0, plus) : v;
            }
        }
        public string GetAuthor() { return "Arthur Morrill"; }
        public string GetDescription() { return "AI editing assistant: check style, rewrite, ask. Edits arrive as tracked changes."; }
        public string GetName() { return "House Style"; }

        public void Initialize(IHost host)
        {
            _host = host; // docs: do nothing else here
        }

        public void Execute()
        {
            try
            {
                Diagnostics.Enabled = PluginSettings.Load().WriteDiagnostics;
                Diagnostics.Log("House Style starting in " + _host.GetName() + " (" + _host.GetCompany() + "), plugin dir " + PluginDirectory);
                _nav = _host.GetNavContext();
                RefreshTeam();
                CreateRibbon();
                CreateToolStrip();
                try { _host.GetEditorContext().DocumentSwitched += OnDocumentSwitched; } catch (Exception ex) { Diagnostics.Log("subscribe", ex); }
                try { _host.GetCompilerContext().CompileComplete += OnCompileComplete; } catch (Exception ex) { Diagnostics.Log("subscribe compile", ex); }
                _editor = new EditorIntegration(_host, LintRunner, () => _nav.GetParentForm(), OnCheckStyle, OnRewrite, OnAsk, OnCheckProject);
                try { _host.GetCompilerContext().CompileStart += OnCompileStart; } catch (Exception ex) { Diagnostics.Log("subscribe compile start", ex); }
                _editor.AttachAll();
                _activated = true;
                // Last update's result / What's new, then the daily update check (background; per-user install only).
                _updates = new UpdateFlow(() => _nav.GetParentForm(), PluginDirectory, Version);
                try { _updates.Start(); } catch (Exception ex) { Diagnostics.Log("updates", ex); }
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Execute", ex);
                Notify.Error(Owner, "House Style didn't start.", "Restart Flare. If it happens again, choose Copy details and send them to support.", ex);
            }
        }

        public void Stop()
        {
            try { _host.GetEditorContext().DocumentSwitched -= OnDocumentSwitched; } catch (Exception) { }
            try { _host.GetCompilerContext().CompileComplete -= OnCompileComplete; } catch (Exception) { }
            try { _host.GetCompilerContext().CompileStart -= OnCompileStart; } catch (Exception) { }
            _style = null;
            try { _host.Dispose(); } catch (Exception ex) { Diagnostics.Log("Stop", ex); }
            _activated = false;
        }

        // ---- ribbon ------------------------------------------------------------------

        private void CreateRibbon()
        {
            // Per the Ribbon Example: ribbon.AddNewRibbonTab(label, keyTip) → tab.AddNewRibbonGroup(label) → group.AddRibbonButton(RibbonControlData)
            IRibbon ribbon = _nav.GetRibbon();
            IRibbonTab tab = ribbon.AddNewRibbonTab(Notify.Product, "Q");
            var groups = new Dictionary<string, IRibbonGroup>();
            foreach (var c in Commands.All)
            {
                IRibbonGroup group;
                if (!groups.TryGetValue(c.Group, out group)) groups[c.Group] = group = tab.AddNewRibbonGroup(c.Group);
                var button = group.AddRibbonButton(Button(c));
                try { if (button != null) button.ToolTip = c.Tooltip; } catch (Exception ex) { Diagnostics.Log("ribbon tooltip " + c.Id, ex); }
            }
        }

        /// <summary>
        /// Same actions for Flare's Tool Strip UI mode, where the ribbon tab is invisible: a "House Style" menu and toolbar.
        /// Failures are logged only — the ribbon is the primary surface.
        /// </summary>
        private void CreateToolStrip()
        {
            try
            {
                var menu = _nav.CreateToolStripMenu(Notify.Product, "Q");
                foreach (var c in Commands.Menu)
                {
                    if (c == null) { menu.AddSeparator(); continue; }
                    menu.AddMenuItem(c.Label, new DelegateCommand(HandlerFor(c)), null, IconPath(c.Icon + "-16.png"), c.Label, c.Tooltip, c.KeyTip);
                }

                var bar = _nav.CreateCustomToolBar(Notify.Product);
                foreach (var c in Commands.Toolbar)
                    bar.AddButton(c.Label, new DelegateCommand(HandlerFor(c)), null, IconPath(c.Icon + "-16.png"), RibbonIconSize.Small, c.Label, c.Tooltip);
            }
            catch (Exception ex) { Diagnostics.Log("Tool Strip entries", ex); }
        }

        private RibbonControlData Button(CommandInfo c)
        {
            return new RibbonControlData
            {
                Label = c.Label, KeyTip = c.KeyTip, Command = new DelegateCommand(HandlerFor(c)),
                LargeImage = Image(c.Icon + ".png"), SmallImage = Image(c.Icon + "-16.png")
            };
        }

        /// <summary>The handler for a command in the table (ribbon, Tool Strip and right-click all call the same one).</summary>
        private Action HandlerFor(CommandInfo c)
        {
            return Safe.Wrap(c.Label, RawHandlerFor(c));
        }

        private Action RawHandlerFor(CommandInfo c)
        {
            if (c == Commands.CheckStyle) return OnCheckStyle;
            if (c == Commands.Rewrite) return OnRewrite;
            if (c == Commands.Ask) return OnAsk;
            if (c == Commands.CheckProject) return OnCheckProject;
            if (c == Commands.TopicAssists) return OnAssist;
            if (c == Commands.ValidateGuide) return OnValidateGuide;
            if (c == Commands.TuneGuide) return OnTuneGuide;
            if (c == Commands.BuildGuide) return OnBuildGuide;
            if (c == Commands.ExportLlm) return OnExportLlm;
            if (c == Commands.Settings) return OnSettings;
            if (c == Commands.Diagnostics) return OnDiagnostics;
            if (c == Commands.WhatsNew) return () => UpdateFlow.ShowAll(Owner, PluginDirectory, Version);
            throw new ArgumentException("No handler for command " + c.Id);
        }

        /// <summary>Owner for dialogs: Flare's main window (null if it isn't available).</summary>
        private IWin32Window Owner
        {
            get { try { return _nav == null ? null : _nav.GetParentForm(); } catch (Exception) { return null; } }
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

        private void OnCheckStyle() { RunAction(Commands.CheckStyle.Label, ActionKind.CheckStyle, null); }
        private void OnRewrite() { RunAction(Commands.Rewrite.Label, ActionKind.Rewrite, null); }

        private void OnAsk()
        {
            using (var f = new AskForm())
            {
                if (f.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(f.Instruction)) return;
                RunAction("Ask", ActionKind.Ask, f.Instruction);   // title of the result window
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
                    Notify.Info(Owner, "Open any topic of the project first. Check project looks at every topic and snippet in that project.", Commands.CheckProject.Label);
                    return;
                }
                var runner = NewRunner(adapter, true, true);
                if (runner == null) return;
                var root = Path.GetDirectoryName(files[0]);
                while (root != null && Directory.GetFiles(root, "*.flprj").Length == 0) root = Path.GetDirectoryName(root);
                var name = root == null ? "" : Path.GetFileName(root);
                PanelHost.Show(FindingsPanel.ForProject(runner, adapter, () => NewRunner(adapter, true, true), OpenAdapter, files, name), _nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Check project", ex);
                Notify.Failed(Owner, "check the project", ex, Commands.CheckProject.Label);
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
                // With no team guide, rule changes go to the writer's own guide in %APPDATA% (created from the bundled one on
                // first need), never to the plugin folder: standard users can't write under Program Files.
                var team = Team.Load(settings.GuideRoot(PluginDirectory), settings.FeedbackFolder);
                team.SeedFrom = settings.ReadOnlyGuide(PluginDirectory);
                settings.GuideRoots(PluginDirectory);   // upgrades a writer's guide in the old layout (a full copy) before first use
                Team.Current = team;
                PromptBuilder.DefaultOverrideFolder = team.GuideRoot == null ? null : Path.Combine(team.GuideRoot, Path.Combine("FlareAI", "prompts"));
                Diagnostics.Log("Team: writer=" + team.Writer + " guide=" + team.GuideRoot + " strict=" + team.IsStrict + " owner=" + team.IsOwner + " feedback=" + (team.FeedbackFolder ?? "(personal)"));
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    // Read-only detection on load: a create-and-delete probe (shares report attributes unreliably).
                    try { if (team.IsStrict && !team.IsPersonal) Diagnostics.Log("Guide access: " + (team.ProbeGuide() ?? "writable") + (team.AccessNotice == null ? "" : " — " + team.AccessNotice)); }
                    catch (Exception ex) { Diagnostics.Log("guide access", ex); }
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
                if (adapter == null || !adapter.IsEditableTopic) { Notify.Info(Owner, "Open a topic in the XML Editor first.", Commands.TopicAssists.Label); return; }
                var runner = NewRunner(adapter);
                if (runner == null) return;
                using (var f = new AssistForm(runner, adapter)) f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Topic assists", ex);
                Notify.Failed(Owner, "draft topic assists", ex, Commands.TopicAssists.Label);
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
                using (var dlg = new FolderBrowserDialog { Description = "Built HTML5 or Clean HTML output to export. Markdown for each topic, plus llms.txt, is added to it.", SelectedPath = output ?? "" })
                {
                    if (dlg.ShowDialog(_nav.GetParentForm()) != DialogResult.OK) return;
                    output = dlg.SelectedPath;
                }
                var summary = RunLlmExport(settings, output);
                Diagnostics.Log("LLM export: " + summary);
                if (Notify.AskYesNo(Owner, summary + "\n\nOpen the folder?", Commands.ExportLlm.Label))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + output + "\"");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Export for LLMs", ex);
                Notify.Failed(Owner, "export for LLMs", ex, Commands.ExportLlm.Label);
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
                Notify.Failed(Owner, "build the style guide", ex, Commands.BuildGuide.Label);
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
                // Turned-off rules: the guides in effect (the read-only one in the plugin folder lists its defaults, which the
                // writer turns back on with a row in their own guide) and the open project.
                var pluginDir = PluginDirectory;
                Func<IList<string>> roots = () => settings.GuideRoots(pluginDir);
                Func<string, bool> isDefault = r => PluginSettings.IsUnder(r, pluginDir);
                var openProject = adapter != null ? adapter.SourcePath : null;
                using (var f = new TuneGuideForm(Team.Current, style, project, OpenAdapter, doc => NewRunner(doc as FlareDocumentAdapter, false), roots, openProject, isDefault))
                    f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Tune style guide", ex);
                Notify.Failed(Owner, "open the guide report", ex, Commands.TuneGuide.Label);
            }
        }

        /// <summary>Tests the layered style guide (tables against examples; optionally prose rules against the AI).</summary>
        private void OnValidateGuide()
        {
            try
            {
                var settings = PluginSettings.Load();
                var style = StyleService(settings);
                var adapter = ActiveAdapter();
                var project = settings.UseProjectLayer && adapter != null ? adapter.SourcePath : null;
                using (var f = new GuideReportForm(style, () => adapter == null ? NewRunnerWithoutTopic(settings, style, project) : NewRunner(adapter), project))
                    f.ShowDialog(_nav.GetParentForm());
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Validate style guide", ex);
                Notify.Failed(Owner, "validate the style guide", ex, Commands.ValidateGuide.Label);
            }
        }

        private ActionRunner NewRunnerWithoutTopic(PluginSettings settings, StyleEngine style, string project)
        {
            ILlmClient llm;
            try { llm = LlmClientFactory.Create(settings); }
            catch (LlmException lex) { Notify.Warn(Owner, lex.Message); return null; }
            return new ActionRunner(llm, PromptBuilder.For(style, project), settings.MaxTokens, style, project, RoutingFor(settings))
            { Parallelism = settings.Parallelism > 0 ? settings.Parallelism : 6 };
        }

        private void OnDiagnostics()
        {
            try
            {
                var adapter = ActiveAdapter();
                var sb = new System.Text.StringBuilder();
                // About: version, automatic updates, last check (issue #101).
                sb.AppendLine(_updates != null ? _updates.Describe() : "House Style " + Version);
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
                Notify.Report(Owner, Commands.Diagnostics.Label, sb + "\n(The same text is in the log: " + Diagnostics.LogPath + ")");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Diagnostics", ex);
                Notify.Failed(Owner, "collect diagnostics", ex, Commands.Diagnostics.Label);
            }
        }

        private void RunAction(string title, ActionKind kind, string instruction)
        {
            try
            {
                var adapter = ActiveAdapter();
                if (adapter == null || !adapter.IsEditableTopic)
                {
                    Notify.Info(Owner, "Open a topic in the XML Editor first.", title);
                    return;
                }
                var runner = NewRunner(adapter, true, kind == ActionKind.CheckStyle);
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
                Notify.Failed(Owner, "finish \"" + title + "\"", ex, title);
            }
        }

        /// <summary>Fresh settings + LLM client per action; the docstyle server process is shared. Null if not configured.</summary>
        private ActionRunner NewRunner(FlareDocumentAdapter adapter)
        {
            return NewRunner(adapter, true);
        }

        /// <param name="projectLayer">False for style-guide topics: the guide project is already a layer.</param>
        /// <param name="lintOnly">True for work that needs no AI (Check project): runs without a key; AI buttons then report why.</param>
        private ActionRunner NewRunner(FlareDocumentAdapter adapter, bool projectLayer, bool lintOnly = false)
        {
            if (adapter == null) return null;
            var topicPath = adapter.SourcePath;
            var settings = PluginSettings.Load();
            ILlmClient llm;
            try { llm = LlmClientFactory.Create(settings); }
            catch (LlmException lex)
            {
                if (lintOnly) llm = new UnavailableClient(lex.Message);
                else
                {
                    Notify.Warn(Owner, lex.Message);
                    OnSettings();
                    return null;
                }
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

        /// <summary>
        /// The in-process style guide and linter; rebuilt when the configured guide projects change. It asks for the guide
        /// roots before each use (the writer's own guide joins once created) and reloads when any guide file changes.
        /// </summary>
        private StyleEngine StyleService(PluginSettings settings)
        {
            var key = settings.StyleLayers ?? "";
            if (_style == null || _styleKey != key)
            {
                var pluginDir = PluginDirectory;
                _style = new StyleEngine(() => settings.GuideRoots(pluginDir), PluginSettings.BundledGuide(pluginDir));
                _styleKey = key;
            }
            return _style;
        }

        private static readonly ReviewCache Reviews = new ReviewCache(300, Path.Combine(PluginSettings.SettingsDirectory, "reviews.json"));

        private void ShowFindings(FlareDocumentAdapter adapter, ActionRunner runner, bool aiOnStart)
        {
            PanelHost.Show(new FindingsPanel(runner, adapter, () => NewRunner(adapter, true, true), OpenAdapter, Reviews, aiOnStart), _nav.GetParentForm());
        }

        /// <summary>
        /// While the findings view is open it follows the active topic: instant lint for the new topic, plus the AI
        /// review only if this exact content was reviewed before (cache). Snippet tabs opened by our own fixes don't count.
        /// </summary>
        private EditorIntegration _editor;
        private string _lastTopic;

        private void OnCompileStart(object sender, EventArgs e)
        {
            try
            {
                var active = ActiveAdapter();
                _editor.OnCompileStart(active != null ? active.SourcePath : _lastTopic);
            }
            catch (Exception ex) { Diagnostics.Log("CompileStart", ex); }
        }

        /// <summary>A runner for the linter only (live check, check on save, report before build): no API key needed.</summary>
        private ActionRunner LintRunner(FlareDocumentAdapter adapter)
        {
            var settings = PluginSettings.Load();
            var style = StyleService(settings);
            if (style == null) return null;
            var project = settings.UseProjectLayer ? adapter.SourcePath : null;
            return new ActionRunner(null, PromptBuilder.For(style, project), 0, style, project, RoutingFor(settings)) { VariableValue = adapter.VariableValue };
        }

        private void OnDocumentSwitched(object sender, EventArgs e)
        {
            try { if (_editor != null) _editor.AttachAll(); var a = ActiveAdapter(); if (a != null) _lastTopic = a.SourcePath; } catch (Exception ex) { Diagnostics.Log("editor", ex); }
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
                var runner = NewRunner(adapter, true, true);
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
