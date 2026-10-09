using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using B3.PluginAPIKit;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;
using FlareAI.Core.Settings;
using FlareAI.Core.Style;
using FlareAI.Flare.UI;

namespace FlareAI.Flare
{
    /// <summary>
    /// The plugin inside Flare's editor, not only in its pane:
    ///   - right-click items (Check style, Rewrite, Ask…) in Flare's own context menu;
    ///   - a margin bar on paragraphs with open proposals (a stylesheet on the editor view: never saved into the topic);
    ///   - live checking: a moment after typing stops, the topic is re-linted (no AI) and the pane and markers refresh;
    ///   - check on save: a quiet notice with the topic's issue count;
    ///   - report before build: when a build starts, the project is linted and a notice offers the report.
    /// Each feature has its own setting. None of them ever blocks typing, saving or building.
    /// </summary>
    internal sealed class EditorIntegration
    {
        private readonly IHost _host;
        private readonly Func<FlareDocumentAdapter, ActionRunner> _lintRunner;
        private readonly Action _checkStyle, _rewrite, _ask, _checkProject;
        private readonly Func<Form> _owner;
        // Keyed by file path: Flare hands out a new IDocument wrapper for the same document on each call.
        private readonly Dictionary<string, DocState> _docs = new Dictionary<string, DocState>(StringComparer.OrdinalIgnoreCase);
        private static readonly string MarkerDir = Path.Combine(Path.GetTempPath(), "FlareAI-markers");

        private sealed class DocState
        {
            public Timer Debounce;
            public string Css;          // the stylesheet applied now (path)
            public int Version;
            public List<StyleFinding> Lint;   // latest live lint, for markers when the pane shows another topic
            public IDocument Doc;             // the wrapper we subscribed to
        }

        public EditorIntegration(IHost host, Func<FlareDocumentAdapter, ActionRunner> lintRunner, Func<Form> owner,
                                 Action checkStyle, Action rewrite, Action ask, Action checkProject)
        {
            _host = host; _lintRunner = lintRunner; _owner = owner;
            _checkStyle = checkStyle; _rewrite = rewrite; _ask = ask; _checkProject = checkProject;
            FindingsPanel.Changed += panel => { var d = DocFor(panel.TopicPath); if (d != null) UpdateMarkers(d); };
            try { Directory.CreateDirectory(MarkerDir); foreach (var f in Directory.GetFiles(MarkerDir, "*.css")) File.Delete(f); } catch (Exception) { }
        }

        /// <summary>Hooks every open document (call on activation and whenever the active document changes).</summary>
        public void AttachAll()
        {
            try
            {
                var ctx = _host.GetEditorContext();
                foreach (var o in ctx.GetDocuments()) { var d = o as IDocument; if (d != null) Attach(d); }
                var active = ctx.GetActiveDocument();
                if (active != null) Attach(active);
            }
            catch (Exception ex) { Diagnostics.Log("editor attach", ex); }
        }

        private void Attach(IDocument doc)
        {
            var path = SafePath(doc);
            if (path == null || _docs.ContainsKey(path)) return;
            if (!(path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase))) return;
            var state = new DocState { Debounce = new Timer { Interval = 1200 }, Doc = doc };
            _docs[path] = state;

            // Right-click: Flare adds these to its own context menu for this document.
            try
            {
                var items = doc.GetUserPendingContextMenuItems();
                if (items != null && !items.Any(i => i.Text == "House Style: Check style"))
                {
                    items.Add(new ToolStripSeparator());
                    items.Add(Item("House Style: Check style", _checkStyle));
                    items.Add(Item("House Style: Rewrite selection", _rewrite));
                    items.Add(Item("House Style: Ask…", _ask));
                }
            }
            catch (Exception ex) { Diagnostics.Log("context menu", ex); }

            state.Debounce.Tick += (s, e) => { state.Debounce.Stop(); LiveCheck(doc, state); };
            doc.ContentChanged += (s, e) =>
            {
                if (!PluginSettings.Load().LiveCheck) return;
                state.Debounce.Stop(); state.Debounce.Start();   // a moment after typing stops
            };
            doc.Saved += (s, e) => OnSaved(doc, state);
            doc.Closing += (s, e) => Detach(path);
            // First markers for a topic the plugin already knows about (pane open on it).
            UpdateMarkers(doc);
        }

        private static ToolStripMenuItem Item(string text, Action action)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += (s, e) => { try { action(); } catch (Exception ex) { Diagnostics.Log(text, ex); } };
            return mi;
        }

        private void Detach(string path)
        {
            DocState state;
            if (!_docs.TryGetValue(path, out state)) return;
            state.Debounce.Stop(); state.Debounce.Dispose();
            if (state.Css != null) try { File.Delete(state.Css); } catch (Exception) { }
            _docs.Remove(path);
        }

        // ---- live check and save ------------------------------------------------------------------------------

        /// <summary>Linter + structure check of the whole topic, from the editor's current DOM. Free and fast (no AI).</summary>
        private List<StyleFinding> Lint(IDocument doc, out FlareDocumentAdapter adapter)
        {
            adapter = new FlareDocumentAdapter(doc);
            if (!adapter.IsEditableTopic) return null;
            var runner = _lintRunner(adapter);
            if (runner == null) return null;
            var blocks = runner.PickAllBlocks(adapter);
            var lint = runner.Lint(blocks);
            lint.AddRange(StructureCheck.Of(blocks));
            return lint;
        }

        private void LiveCheck(IDocument doc, DocState state)
        {
            try
            {
                FlareDocumentAdapter adapter;
                var lint = Lint(doc, out adapter);
                if (lint == null) return;
                state.Lint = lint;
                var panel = PanelHost.Current;
                if (panel != null && !panel.IsProjectReport && SamePath(panel.TopicPath, adapter.SourcePath)) panel.RefreshLint(lint);   // raises Changed → markers
                else UpdateMarkers(doc);
            }
            catch (Exception ex) { Diagnostics.Log("live check", ex); }
        }

        private void OnSaved(IDocument doc, DocState state)
        {
            try
            {
                var settings = PluginSettings.Load();
                if (!settings.CheckOnSave) return;
                FlareDocumentAdapter adapter;
                var lint = Lint(doc, out adapter);
                if (lint == null) return;
                state.Lint = lint;
                if (lint.Count == 0) return;   // nothing to say
                var errors = lint.Count(f => f.Severity == "error");
                Toast.Show(_owner(), lint.Count + " style issue(s) in " + Path.GetFileName(adapter.SourcePath) + (errors > 0 ? " (" + errors + " error(s))" : "") + ". Saved anyway.",
                           "Show them", _checkStyle);
                UpdateMarkers(doc);
            }
            catch (Exception ex) { Diagnostics.Log("check on save", ex); }
        }

        // ---- markers ------------------------------------------------------------------------------------------

        private void UpdateMarkers(IDocument doc)
        {
            DocState state;
            var path = doc == null ? null : SafePath(doc);
            if (path == null || !_docs.TryGetValue(path, out state)) return;
            try
            {
                var adapter = new FlareDocumentAdapter(doc);
                var show = PluginSettings.Load().ShowMarkers;
                List<StyleFinding> findings = null;
                var panel = PanelHost.Current;
                if (show && panel != null && !panel.IsProjectReport && SamePath(panel.TopicPath, adapter.SourcePath)) findings = panel.OpenTopicFindings();   // lint + AI
                else if (show) findings = state.Lint;
                var marks = new List<KeyValuePair<XmlNode, string>>();
                foreach (var f in findings ?? new List<StyleFinding>())
                {
                    XmlText run; int start;
                    var block = FindingLocator.TryLocate(adapter.Document, f, out run, out start) ? TextRuns.BlockOf(run) : f.Block;
                    if (block != null && block.OwnerDocument == adapter.Document) marks.Add(new KeyValuePair<XmlNode, string>(block, f.Severity));
                }
                var css = marks.Count == 0 ? null : MarkerCss.Css(marks);
                // Flare caches a stylesheet by path: write each version under a new name, then swap.
                var previous = state.Css;
                state.Css = null;
                if (css != null)
                {
                    state.Css = Path.Combine(MarkerDir, "m" + doc.GetHashCode().ToString("x") + "-" + (++state.Version) + ".css");
                    File.WriteAllText(state.Css, css);
                    doc.ApplyStylesheet(state.Css);
                }
                if (previous != null) { try { doc.RemoveStylesheet(previous); } catch (Exception) { } try { File.Delete(previous); } catch (Exception) { } }
                if (css != null || previous != null) doc.UpdateView();
            }
            catch (Exception ex) { Diagnostics.Log("markers", ex); }
        }

        private IDocument DocFor(string path)
        {
            var key = _docs.Keys.FirstOrDefault(k => SamePath(k, path));
            return key == null ? null : _docs[key].Doc;
        }

        // ---- report before build -------------------------------------------------------------------------------

        /// <summary>When a build starts: lint the project in the background and offer the report. The build isn't held up.</summary>
        public void OnCompileStart(string anyProjectPath)
        {
            try
            {
                if (!PluginSettings.Load().ReportBeforeBuild || string.IsNullOrEmpty(anyProjectPath)) return;
                var files = ActionRunner.ProjectFiles(anyProjectPath);
                if (files.Count == 0) return;
                var activeDoc = _host.GetEditorContext().GetActiveDocument();
                var runner = activeDoc == null ? null : _lintRunner(new FlareDocumentAdapter(activeDoc));
                if (runner == null) return;
                var owner = _owner();
                var worker = new System.ComponentModel.BackgroundWorker();
                worker.DoWork += (s, e) => e.Result = runner.LintFiles(files);
                worker.RunWorkerCompleted += (s, e) =>
                {
                    if (e.Error != null) { Diagnostics.Log("report before build", e.Error); return; }
                    var found = (List<StyleFinding>)e.Result;
                    if (found.Count == 0) { Toast.Show(owner, "Building. No style issues in the project's " + files.Count + " topics and snippets."); return; }
                    var inFiles = found.Select(f => f.DocumentPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    var errors = found.Count(f => f.Severity == "error");
                    Toast.Show(owner, "Building. " + found.Count + " style issue(s) in " + inFiles + " file(s)" + (errors > 0 ? ", " + errors + " error(s)" : "") + ".",
                               "Open the report", _checkProject);
                };
                worker.RunWorkerAsync();
            }
            catch (Exception ex) { Diagnostics.Log("report before build", ex); }
        }

        private static string SafePath(IDocument d) { try { return d.GetSourceUrl(); } catch (Exception) { return null; } }

        private static bool SamePath(string a, string b)
        {
            return a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
