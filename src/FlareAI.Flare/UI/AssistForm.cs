using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Topic assists": the metadata and companion content authors write by hand — a meta description, index keywords,
    /// search synonyms, micro content (question/answer pairs for search and chatbots) and glossary candidates — drafted
    /// from the topic in one call. Each section has its own Copy button for pasting into Flare's editors.
    /// </summary>
    public sealed class AssistForm : Form
    {
        private readonly Label _status;
        private readonly FlowLayoutPanel _sections;

        public AssistForm(ActionRunner runner, IDocumentAdapter doc)
        {
            Text = "House Style — Topic assists: " + System.IO.Path.GetFileName(doc.SourcePath ?? "");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(520, 360);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            _status = new LiveLabel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), Text = "Reading the topic…" };
            _sections = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
            _sections.Resize += (s, e) => { foreach (Control c in _sections.Controls) c.Width = _sections.ClientSize.Width - 24; };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(close);
            Controls.Add(_sections); Controls.Add(bar); Controls.Add(_status);
            CancelButton = close;
            A11y.Name(_sections, "Drafted topic details");
            A11y.Name(bar, "Dialog buttons");
            A11y.Apply(this);

            var blocks = runner.PickAllBlocks(doc); // DOM read on the UI thread
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = runner.Assist(blocks);
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                if (e.Error != null) { Diagnostics.Log("Topic assists", e.Error); _status.Text = "Failed: " + e.Error.Message; return; }
                var a = (TopicAssist)e.Result;
                _status.Text = "Drafted from this topic only. Review each, then copy it into Flare (Topic Properties, Index, Synonyms, Micro Content, Glossary).";
                Section("Meta description  (Topic Properties ▸ Description)", a.Description ?? "", a.Description ?? "");
                Section("Index keywords  (Insert ▸ Index Keyword; one term per line)", string.Join("\n", a.Keywords.ToArray()), string.Join(";", a.Keywords.ToArray()));
                Section("Search synonyms  (Synonyms file ▸ Groups)", string.Join("\n", a.Synonyms.Select(g => string.Join(", ", g.ToArray())).ToArray()), null);
                Section("Micro content  (question/answer pairs for search results and chatbots)", string.Join("\n\n", a.MicroContent.Select(m => "Q: " + m.Key + "\nA: " + m.Value).ToArray()), null);
                Section("Glossary candidates  (terms used but not defined)", string.Join("\n", a.Glossary.Select(g => g.Key + " — " + g.Value).ToArray()), null);
            };
            Shown += (s, e) => worker.RunWorkerAsync();
        }

        private void Section(string title, string text, string copyText)
        {
            if (string.IsNullOrEmpty(text)) return;
            var panel = new Panel { Width = _sections.ClientSize.Width - 24, Height = 40 + Math.Min(160, 18 * (text.Split('\n').Length + 1)), Margin = new Padding(0, 0, 0, 10) };
            var head = new Label { Text = title, Dock = DockStyle.Top, Height = 22, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            var box = new TextBox { Text = text.Replace("\n", "\r\n"), Multiline = true, ReadOnly = false, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            var copy = new Button { Text = "Copy", Dock = DockStyle.Right, Width = 70 };
            copy.Click += (s, e) => { try { Clipboard.SetText(copyText ?? box.Text); copy.Text = "Copied"; } catch (Exception ex) { Diagnostics.Log("copy", ex); } };
            panel.Controls.Add(box); panel.Controls.Add(copy); panel.Controls.Add(head);
            A11y.Name(panel, title); A11y.Name(box, title, "Editable draft. Select the text or use the Copy button.");
            A11y.Name(copy, "Copy " + title, "Copies this draft to the clipboard.");
            _sections.Controls.Add(panel);
        }
    }
}
