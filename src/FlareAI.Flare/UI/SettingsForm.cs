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
        private readonly CheckBox _projectLayer, _llmExport;
        private readonly ComboBox _provider;
        private readonly TextBox _baseUrl, _model, _fastModel, _deepModel, _key, _mcpTools, _timeout;
        private readonly CheckBox _diag;
        private readonly Label _keyStatus;
        private readonly PluginSettings _settings;

        public SettingsForm()
        {
            _settings = PluginSettings.Load();

            Text = "FlareAI — Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 600);
            Font = SystemFonts.MessageBoxFont;

            var y = 12;
            _provider = AddRow("Provider", y, out y, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
            _provider.Items.AddRange(new object[] { "Anthropic (direct)", "Bifrost gateway" });
            _provider.SelectedIndex = (int)_settings.Provider;
            _provider.SelectedIndexChanged += (s, e) => OnProviderChanged();

            _baseUrl = AddRow("Base URL", y, out y, new TextBox { Text = _settings.BaseUrl });
            _model = AddRow("Model (standard)", y, out y, new TextBox { Text = _settings.Model });
            _fastModel = AddRow("Fast / deep models", y, out y, new TextBox { Text = _settings.FastModel ?? "", Width = 180 });
            _deepModel = new TextBox { Text = _settings.DeepModel ?? "", Left = 328, Top = _fastModel.Top, Width = 180 };
            Controls.Add(_deepModel);
            Controls.Add(new Label { Text = "Empty = claude-haiku-5-5 / claude-opus-5-5. A style guide's FlareAI\\models.json overrides these for the team.", Left = 140, Top = y - 4, Width = 368, Height = 30, ForeColor = SystemColors.GrayText }); y += 30;
            _key = AddRow("API / virtual key", y, out y, new TextBox { UseSystemPasswordChar = true });
            _keyStatus = new Label { Left = 140, Top = y - 4, Width = 290, Height = 18, ForeColor = SystemColors.GrayText };
            var clearKey = new LinkLabel { Text = "Clear stored key", Left = 430, Top = y - 4, Width = 90, Height = 18 };
            clearKey.LinkClicked += (s, e) => { _settings.SetApiKey(null); _key.Text = ""; RefreshKeyStatus(); };
            Controls.Add(_keyStatus); Controls.Add(clearKey); y += 18;
            _mcpTools = AddRow("MCP include tools", y, out y, new TextBox { Text = _settings.McpIncludeTools });
            _layers = AddRow("Style guide project", y, out y, new TextBox { Text = (_settings.StyleLayers ?? "").Replace(";", Environment.NewLine), Multiline = true, Height = 40, ScrollBars = ScrollBars.Vertical });
            y += 14;
            Controls.Add(new Label { Text = "Folder of your team's style guide (a Flare project). Empty = the guide bundled with the plugin.", Left = 140, Top = y - 4, Width = 368, Height = 18, ForeColor = SystemColors.GrayText }); y += 18;
            _projectLayer = new CheckBox { Text = "Also use the open project's variables, glossary and Resources\\StyleGuide", Left = 140, Top = y, Width = 368, Checked = _settings.UseProjectLayer };
            Controls.Add(_projectLayer); y += 28;
            _feedback = AddRow("Team feedback folder", y, out y, new TextBox { Text = _settings.FeedbackFolder ?? "" });
            Controls.Add(new Label { Text = "Shared folder (synced or network) where each writer's review decisions are pooled. Empty = personal only.", Left = 140, Top = y - 4, Width = 368, Height = 30, ForeColor = SystemColors.GrayText }); y += 30;
            _timeout = AddRow("Timeout (s)", y, out y, new TextBox { Text = _settings.TimeoutSeconds.ToString(), Width = 60 });
            _llmExport = new CheckBox { Text = "After each HTML build, add LLM-ready Markdown and llms.txt to the output", Left = 140, Top = y, Width = 368, Checked = _settings.LlmExportAfterBuild };
            Controls.Add(_llmExport); y += 26;
            _diag = new CheckBox { Text = "Write diagnostics log", Left = 140, Top = y, Width = 300, Checked = _settings.WriteDiagnostics };
            Controls.Add(_diag); y += 30;

            var save = new Button { Text = "Save", Left = 352, Top = ClientSize.Height - 36, Width = 75 };
            var cancel = new Button { Text = "Cancel", Left = 433, Top = ClientSize.Height - 36, Width = 75, DialogResult = DialogResult.Cancel };
            save.Click += (s, e) => OnSave();
            Controls.AddRange(new Control[] { save, cancel });
            AcceptButton = save; CancelButton = cancel;

            RefreshKeyStatus();
            OnProviderChanged();
        }

        private T AddRow<T>(string label, int top, out int next, T control) where T : Control
        {
            Controls.Add(new Label { Text = label, Left = 12, Top = top + 3, Width = 124 });
            control.Left = 140; control.Top = top;
            if (control.Width == 100 || control.Width == 0) control.Width = 368; // default width unless explicitly set
            Controls.Add(control);
            next = top + 30;
            return control;
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
            if (!int.TryParse(_timeout.Text, out timeout) || timeout < 10) { MessageBox.Show("Timeout must be a number ≥ 10."); return; }
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
            if (_key.Text.Trim().Length > 0) _settings.SetApiKey(_key.Text.Trim());
            try
            {
                _settings.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save settings: " + ex.Message, "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
