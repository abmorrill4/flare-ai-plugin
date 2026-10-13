using System;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Settings;

namespace FlareAI.Flare.UI
{
    /// <summary>Provider, base URL, model, key. The key is DPAPI-protected on save and never shown back.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly TextBox _layers, _feedback;
        private readonly CheckBox _projectLayer, _llmExport, _markers, _live, _onSave, _beforeBuild;
        private readonly ComboBox _provider;
        private readonly TextBox _baseUrl, _model, _fastModel, _deepModel, _key, _mcpTools, _timeout;
        private readonly CheckBox _diag;
        private readonly Label _keyStatus;
        private readonly PluginSettings _settings;

        public SettingsForm()
        {
            _settings = PluginSettings.Load();

            Text = "House Style — Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 600);   // height is set from the content below
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;

            // Layout: a label column and a field column. Everything that holds prose is AutoSize with a maximum width and
            // the next control is placed below its measured height, so nothing is cut off at 100% or higher DPI.
            _y = 12;
            _provider = AddRow("Provider", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
            _provider.Items.AddRange(new object[] { "Anthropic (direct)", "Bifrost gateway" });
            _provider.SelectedIndex = (int)_settings.Provider;
            _provider.SelectedIndexChanged += (s, e) => OnProviderChanged();

            _baseUrl = AddRow("Base URL", new TextBox { Text = _settings.BaseUrl });
            _model = AddRow("Model (standard)", new TextBox { Text = _settings.Model });
            _fastModel = AddRow("Fast model", new TextBox { Text = _settings.FastModel ?? "" });
            _deepModel = AddRow("Deep model", new TextBox { Text = _settings.DeepModel ?? "" });
            Note("Empty = claude-haiku-5-5 (fast) and claude-opus-5-5 (deep). A style guide's FlareAI\\models.json overrides these for the team.");
            _key = AddRow("API / virtual key", new TextBox { UseSystemPasswordChar = true });
            _keyStatus = new LiveLabel { Text = "No key configured.", AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, ForeColor = SystemColors.GrayText };
            Controls.Add(_keyStatus); _y += _keyStatus.PreferredHeight + 2;
            var clearKey = new LinkLabel { Text = "Clear stored key", AutoSize = true, Left = FieldLeft, Top = _y };
            clearKey.LinkClicked += (s, e) => { _settings.SetApiKey(null); _key.Text = ""; RefreshKeyStatus(); };
            Controls.Add(clearKey); _y += clearKey.PreferredHeight + 8;
            Note("The key is used only when you ask for AI: Check style's wording review, Rewrite, Ask, Topic assists, and Validate style guide's AI check. " +
                 "Checking as you type, on save, before a build, and Check project never use AI and cost nothing.");
            _mcpTools = AddRow("MCP include tools", new TextBox { Text = _settings.McpIncludeTools });
            _layers = AddRow("Style guide project", new TextBox { Text = (_settings.StyleLayers ?? "").Replace(";", Environment.NewLine), Multiline = true, Height = 40, ScrollBars = ScrollBars.Vertical });
            _y += 14;
            Note("Folder of your team's style guide (a Flare project). Empty = the guide bundled with the plugin.");
            _projectLayer = AddCheck("Also use the open project's variables, glossary and Resources\\StyleGuide", _settings.UseProjectLayer);
            _feedback = AddRow("Team feedback folder", new TextBox { Text = _settings.FeedbackFolder ?? "" });
            Note("Shared folder (synced or network) where review decisions are pooled. Empty = personal only. The guide owner sees each decision, its rule and the text; who made it only if the guide sets attribution = names (default: anonymous, counted).");
            _timeout = AddRow("Timeout (s)", new TextBox { Text = _settings.TimeoutSeconds.ToString(), Width = 60 });
            _llmExport = AddCheck("After each HTML build, add LLM-ready Markdown and llms.txt to the output", _settings.LlmExportAfterBuild);
            _markers = AddCheck("Mark paragraphs with suggestions in the editor", _settings.ShowMarkers);
            _live = AddCheck("Check as I type (style rules only, no AI)", _settings.LiveCheck);
            _onSave = AddCheck("After saving, tell me how many suggestions the topic has", _settings.CheckOnSave);
            _beforeBuild = AddCheck("When a build starts, report the project's style issues", _settings.ReportBeforeBuild);
            _diag = AddCheck("Write diagnostics log", _settings.WriteDiagnostics);
            _y += 6;

            var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(80, 26) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 26), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { save, cancel });
            ClientSize = new Size(FieldLeft + FieldWidth + 12, _y + 26 + 14);
            cancel.Location = new Point(ClientSize.Width - 12 - Math.Max(80, cancel.PreferredSize.Width), _y);
            save.Location = new Point(cancel.Left - 8 - Math.Max(80, save.PreferredSize.Width), _y);
            save.Click += (s, e) => OnSave();
            AcceptButton = save; CancelButton = cancel;
            // ponytail: scroll when taller than the screen; the grouped redesign (#13) removes the need.
            var room = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80;
            if (ClientSize.Height > room) { AutoScroll = true; ClientSize = new Size(ClientSize.Width + SystemInformation.VerticalScrollBarWidth, room); }

            A11y.Name(_deepModel, "Deep model");
            A11y.Name(_fastModel, "Fast model");
            A11y.Name(_key, "API or virtual key", "Write only: a stored key is never shown back. Leave blank to keep the stored key.");
            A11y.Name(clearKey, "Clear stored key");
            A11y.Name(_layers, "Style guide project folder", "Folder of your team's style guide. Empty uses the guide bundled with the plugin.");
            A11y.Name(_timeout, "Timeout in seconds");
            RefreshKeyStatus();
            OnProviderChanged();
            A11y.Apply(this);
        }

        private const int LabelLeft = 12, FieldLeft = 166, FieldWidth = 420;
        private int _y;

        /// <summary>A label and its field on one row; the label wraps if it needs to and the row grows with it.</summary>
        private T AddRow<T>(string label, T control) where T : Control
        {
            var l = new Label { Text = label, AutoSize = true, MaximumSize = new Size(FieldLeft - LabelLeft - 6, 0), Left = LabelLeft, Top = _y + 3 };
            Controls.Add(l);
            control.Left = FieldLeft; control.Top = _y;
            if (control.Width == 100 || control.Width == 0) control.Width = FieldWidth;   // default width unless explicitly set
            Controls.Add(control);
            _y += Math.Max(control.Height, l.PreferredHeight + 6) + 3;
            return control;
        }

        /// <summary>Grey explanatory text under a field: wraps at the field width and pushes the next control down.</summary>
        private void Note(string text)
        {
            var n = new Label { Text = text, AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, ForeColor = SystemColors.GrayText };
            Controls.Add(n);
            _y += n.PreferredHeight + 8;
        }

        /// <summary>A check box whose text wraps at the field width instead of being cut off.</summary>
        private CheckBox AddCheck(string text, bool isChecked)
        {
            var c = new CheckBox { Text = text, AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, Checked = isChecked };
            Controls.Add(c);
            _y += c.PreferredSize.Height + 1;
            return c;
        }

        private void OnProviderChanged()
        {
            var bifrost = _provider.SelectedIndex == 1;
            _mcpTools.Enabled = bifrost;
            if (!bifrost && string.IsNullOrEmpty(_baseUrl.Text)) _baseUrl.Text = PluginSettings.DefaultAnthropicBaseUrl;
            if (!bifrost && _baseUrl.Text.Contains("/v1")) _baseUrl.Text = PluginSettings.DefaultAnthropicBaseUrl;
        }

        private void RefreshKeyStatus()
        {
            if (_settings.HasStoredKey) _keyStatus.Text = "A key is stored (protected). Blank keeps it.";
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PluginSettings.EnvKeyName))) _keyStatus.Text = "Using " + PluginSettings.EnvKeyName + " from the environment.";
            else _keyStatus.Text = "No key configured.";
        }

        private void OnSave()
        {
            int timeout;
            if (!int.TryParse(_timeout.Text, out timeout) || timeout < 10) { Notify.Warn(this, "Timeout must be a number of seconds, 10 or more.", "Settings"); _timeout.Focus(); return; }
            _settings.Provider = (LlmProvider)_provider.SelectedIndex;
            _settings.BaseUrl = _baseUrl.Text.Trim();
            _settings.Model = _model.Text.Trim();
            _settings.FastModel = _fastModel.Text.Trim();
            _settings.DeepModel = _deepModel.Text.Trim();
            _settings.McpIncludeTools = _mcpTools.Text.Trim();
            _settings.TimeoutSeconds = timeout;
            _settings.WriteDiagnostics = _diag.Checked;
            _settings.StyleLayers = _layers.Text.Trim();
            _settings.UseProjectLayer = _projectLayer.Checked;
            _settings.FeedbackFolder = _feedback.Text.Trim();
            _settings.LlmExportAfterBuild = _llmExport.Checked;
            _settings.ShowMarkers = _markers.Checked;
            _settings.LiveCheck = _live.Checked;
            _settings.CheckOnSave = _onSave.Checked;
            _settings.ReportBeforeBuild = _beforeBuild.Checked;
            if (_key.Text.Trim().Length > 0) _settings.SetApiKey(_key.Text.Trim());
            try
            {
                _settings.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Notify.Error(this, "House Style couldn't save your settings.", "Check that the settings folder isn't read-only, then try again.", ex, "Settings");
            }
        }
    }
}
