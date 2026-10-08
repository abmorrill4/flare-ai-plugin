using System;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Settings;

namespace FlareAI.Flare.UI
{
    /// <summary>Provider, base URL, model, key. The key is DPAPI-protected on save and never shown back.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly TextBox _styleCmd, _styleArgs;
        private readonly ComboBox _provider;
        private readonly TextBox _baseUrl, _model, _key, _mcpTools, _timeout;
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
            ClientSize = new Size(520, 360);
            Font = SystemFonts.MessageBoxFont;

            var y = 12;
            _provider = AddRow("Provider", y, out y, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
            _provider.Items.AddRange(new object[] { "Anthropic (direct)", "Bifrost gateway" });
            _provider.SelectedIndex = (int)_settings.Provider;
            _provider.SelectedIndexChanged += (s, e) => OnProviderChanged();

            _baseUrl = AddRow("Base URL", y, out y, new TextBox { Text = _settings.BaseUrl });
            _model = AddRow("Model", y, out y, new TextBox { Text = _settings.Model });
            _key = AddRow("API / virtual key", y, out y, new TextBox { UseSystemPasswordChar = true });
            _keyStatus = new Label { Left = 140, Top = y - 4, Width = 290, Height = 18, ForeColor = SystemColors.GrayText };
            var clearKey = new LinkLabel { Text = "Clear stored key", Left = 430, Top = y - 4, Width = 90, Height = 18 };
            clearKey.LinkClicked += (s, e) => { _settings.SetApiKey(null); _key.Text = ""; RefreshKeyStatus(); };
            Controls.Add(_keyStatus); Controls.Add(clearKey); y += 18;
            _mcpTools = AddRow("MCP include tools", y, out y, new TextBox { Text = _settings.McpIncludeTools });
            _styleCmd = AddRow("Style linter (MCP)", y, out y, new TextBox { Text = _settings.StyleServerCommand, Width = 60 });
            _styleArgs = new TextBox { Text = _settings.StyleServerArgs, Left = 206, Top = _styleCmd.Top, Width = 302 };
            Controls.Add(_styleArgs);
            Controls.Add(new Label { Text = "docstyle server: command + arguments. Clear the command to turn linting off.", Left = 140, Top = y - 4, Width = 368, Height = 18, ForeColor = SystemColors.GrayText }); y += 18;
            _timeout = AddRow("Timeout (s)", y, out y, new TextBox { Text = _settings.TimeoutSeconds.ToString(), Width = 60 });
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
            _settings.McpIncludeTools = _mcpTools.Text.Trim();
            _settings.TimeoutSeconds = timeout;
            _settings.WriteDiagnostics = _diag.Checked;
            _settings.StyleServerCommand = _styleCmd.Text.Trim();
            _settings.StyleServerArgs = _styleArgs.Text.Trim();
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
