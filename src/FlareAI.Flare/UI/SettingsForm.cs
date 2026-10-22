using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FlareAI.Core.Llm;
using FlareAI.Core.Settings;
using FlareAI.Core.Style;

namespace FlareAI.Flare.UI
{
    /// <summary>Provider, base URL, model, key. The key is DPAPI-protected on save and never shown back.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly ListBox _layers;
        private readonly TextBox _feedback;
        private readonly Button _addGuide, _removeGuide, _browseFeedback, _refreshModels;
        private readonly Label _guideStatus, _feedbackStatus, _modelStatus;
        private readonly CheckBox _projectLayer, _llmExport, _markers, _live, _onSave, _beforeBuild;
        private readonly ComboBox _provider;
        private readonly ComboBox _model, _fastModel, _deepModel;
        private readonly TextBox _baseUrl, _key, _timeout;
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
            _urlHint = new Label { AutoSize = false, Left = FieldLeft, Top = _y, Width = FieldWidth, ForeColor = SystemColors.GrayText, Text = GatewayHint };
            _urlHint.Height = TextRenderer.MeasureText(GatewayHint, Font, new Size(FieldWidth, 0), TextFormatFlags.WordBreak).Height + 4;
            Controls.Add(_urlHint); _y += _urlHint.Height + 4;
            _key = AddRow("API / virtual key", new TextBox { UseSystemPasswordChar = true });
            _key.Leave += (s, e) => { if (_key.Text.Trim().Length > 0 && _key.Text.Trim() != _loadedKey) LoadModels(); };
            _keyStatus = new LiveLabel { Text = "No key configured.", AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, ForeColor = SystemColors.GrayText };
            Controls.Add(_keyStatus); _y += _keyStatus.PreferredHeight + 2;
            var clearKey = new LinkLabel { Text = "Clear stored key", AutoSize = true, Left = FieldLeft, Top = _y };
            clearKey.LinkClicked += (s, e) => { _settings.SetApiKey(null); _key.Text = ""; RefreshKeyStatus(); };
            Controls.Add(clearKey); _y += clearKey.PreferredHeight + 8;
            _test = new Button { Text = "Test connection", AutoSize = true, Left = FieldLeft, Top = _y };
            _test.Click += (s, e) => TestConnection();
            _testResult = new LiveLabel { AutoSize = false, Text = "", Left = FieldLeft + _test.PreferredSize.Width + 10, Top = _y + 4, Width = FieldWidth - _test.PreferredSize.Width - 10, Height = Font.Height * 3 + 6, ForeColor = SystemColors.GrayText };
            Controls.Add(_test); Controls.Add(_testResult);
            _y += Math.Max(_test.PreferredSize.Height + 4, _testResult.Height) + 6;
            Note("The key is used only when you ask for AI: Check style's wording review, Rewrite, Ask, Topic assists, and Validate style guide's AI check. " +
                 "Checking as you type, on save, before a build, and Check project never use AI and cost nothing. Without a key, every style check still works and the AI buttons ask you to add one.");
            _modelBlockStart = Controls.Count; _modelBlockTop = _y;
            _model = AddRow("Model (standard)", NewModelCombo());
            _fastModel = AddRow("Fast model", NewModelCombo());
            _deepModel = AddRow("Deep model", NewModelCombo());
            _refreshModels = new Button { Text = "Refresh", AutoSize = true, Left = FieldLeft, Top = _y };
            _refreshModels.Click += (s, e) => LoadModels();
            _modelStatus = new LiveLabel { Text = "", AutoSize = true, MaximumSize = new Size(FieldWidth - 100, 0), Left = FieldLeft + 100, Top = _y + 4, ForeColor = SystemColors.GrayText };
            Controls.Add(_refreshModels); Controls.Add(_modelStatus);
            _y += Math.Max(_refreshModels.PreferredSize.Height, _modelStatus.PreferredHeight + 4) + 4;
            _modelsNote = Note("The lists come from your key. Default means the plugin's built-in model. A style guide's FlareAI\\models.json overrides these for the team.");
            _modelBlockEnd = Controls.Count; _modelBlockHeight = _y - _modelBlockTop;
            _models = new ModelInfo[0];
            SetModel(_model, _settings.Model); SetModel(_fastModel, _settings.FastModel); SetModel(_deepModel, _settings.DeepModel);
            _layers = AddRow("Style guide project", new ListBox { Height = 54, Width = FieldWidth - 90, IntegralHeight = false, HorizontalScrollbar = true });
            foreach (var line in (_settings.StyleLayers ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.Trim().Length > 0) _layers.Items.Add(line.Trim());
            _addGuide = new Button { Text = "Add…", Left = FieldLeft + FieldWidth - 84, Top = _layers.Top - 1, Width = 84 };
            _removeGuide = new Button { Text = "Remove", Left = _addGuide.Left, Top = _addGuide.Bottom + 4, Width = 84 };
            _addGuide.Click += (s, e) => { var f = PickFolder("Choose your team's style guide (a Flare project folder)"); if (f != null && !_layers.Items.Contains(f)) { _layers.Items.Add(f); CheckGuides(); } };
            _removeGuide.Click += (s, e) => { if (_layers.SelectedIndex >= 0) { _layers.Items.RemoveAt(_layers.SelectedIndex); CheckGuides(); } };
            Controls.Add(_addGuide); Controls.Add(_removeGuide);
            _guideStatus = new LiveLabel { AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, ForeColor = SystemColors.GrayText };
            Controls.Add(_guideStatus);
            _y += 24;
            Note("Your team's style guide: a Flare project folder. If you add several, later ones win.");
            _projectLayer = AddCheck("Also use the open project's variables, glossary and Resources\\StyleGuide", _settings.UseProjectLayer);
            _feedback = AddRow("Team feedback folder", new TextBox { Text = _settings.FeedbackFolder ?? "", Width = FieldWidth - 90 });
            _browseFeedback = new Button { Text = "Browse…", Left = FieldLeft + FieldWidth - 84, Top = _feedback.Top - 1, Width = 84 };
            _browseFeedback.Click += (s, e) => { var f = PickFolder("Choose the shared team feedback folder"); if (f != null) { _feedback.Text = f; CheckFeedback(); } };
            _feedback.Leave += (s, e) => CheckFeedback();
            Controls.Add(_browseFeedback);
            _feedbackStatus = new LiveLabel { AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, ForeColor = SystemColors.GrayText };
            Controls.Add(_feedbackStatus);
            _y += 22;
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

            A11y.Name(_model, "Standard model", "Pick from the models your key can use.");
            A11y.Name(_deepModel, "Deep model");
            A11y.Name(_fastModel, "Fast model");
            A11y.Name(_refreshModels, "Refresh models", "Reload the model lists from your key.");
            A11y.Name(_modelStatus, "Model list status");
            A11y.Name(_addGuide, "Add a style guide project folder");
            A11y.Name(_removeGuide, "Remove the selected style guide project");
            A11y.Name(_guideStatus, "Style guide project status");
            A11y.Name(_browseFeedback, "Browse for the team feedback folder");
            A11y.Name(_feedback, "Team feedback folder", "Shared folder where review decisions are pooled. Empty keeps them personal.");
            A11y.Name(_feedbackStatus, "Team feedback folder status");
            A11y.Name(_key, "API or virtual key", "Write only: a stored key is never shown back. Leave blank to keep the stored key.");
            A11y.Name(clearKey, "Clear stored key");
            A11y.Name(_test, "Test connection", "Makes one small request with the address and key as shown, and reports whether it worked.");
            A11y.Name(_testResult, "Connection test result");
            A11y.Name(_layers, "Style guide project folders", "Folders of your team's style guide. None uses the guide bundled with the plugin.");
            A11y.Name(_timeout, "Timeout in seconds");
            RefreshKeyStatus();
            OnProviderChanged();
            A11y.Apply(this);
            CheckGuides(); CheckFeedback();
            Shown += (s, e) => LoadModels();
            FormClosed += (s, e) => _closed = true;
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
            control.Tag = l;   // the row's label, so a row can be shown or hidden as a unit
            _y += Math.Max(control.Height, l.PreferredHeight + 6) + 3;
            return control;
        }

        /// <summary>Grey explanatory text under a field: wraps at the field width and pushes the next control down.</summary>
        private Label Note(string text)
        {
            var n = new Label { Text = text, AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Left = FieldLeft, Top = _y, ForeColor = SystemColors.GrayText };
            Controls.Add(n);
            _y += n.PreferredHeight + 8;
            return n;
        }

        /// <summary>A check box whose text wraps at the field width instead of being cut off.</summary>
        private CheckBox AddCheck(string text, bool isChecked)
        {
            // CheckBox.AutoSize never wraps; size it from the measured, wrapped text instead.
            var h = TextRenderer.MeasureText(text, Font, new Size(FieldWidth - 22, 0), TextFormatFlags.WordBreak).Height + 6;
            var c = new CheckBox { Text = text, AutoSize = false, Width = FieldWidth, Height = Math.Max(22, h), Left = FieldLeft, Top = _y, Checked = isChecked, TextAlign = ContentAlignment.TopLeft, CheckAlign = ContentAlignment.TopLeft };
            Controls.Add(c);
            _y += c.Height + 1;
            return c;
        }

        private Label _modelsNote, _urlHint, _testResult;
        private Button _test;
        private const string GatewayHint = "The address your admin gave you, e.g. https://gateway.example.com";
        private const string DirectHint = "Leave as is to use Anthropic directly.";
        private int _modelBlockStart, _modelBlockEnd, _modelBlockTop, _modelBlockHeight;
        private bool _modelBlockHidden;

        /// <summary>The gateway uses fixed models: hide the model rows and close the gap, so setup is URL, key, Test connection.</summary>
        private void ArrangeForProvider(bool gateway)
        {
            if (gateway == _modelBlockHidden) return;
            AutoScrollPosition = new Point(0, 0);
            var delta = gateway ? -_modelBlockHeight : _modelBlockHeight;
            for (var i = 0; i < Controls.Count; i++)
            {
                var c = Controls[i];
                if (i >= _modelBlockStart && i < _modelBlockEnd) { c.Visible = !gateway; continue; }
                if (c.Top >= _modelBlockTop) c.Top += delta;
            }
            _modelBlockHidden = gateway;
            if (!AutoScroll) ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);
        }

        private int _testSeq;

        private void TestConnection()
        {
            var provider = Gateway ? LlmProvider.Bifrost : LlmProvider.AnthropicDirect;
            string url, err;
            var typed = _baseUrl.Text.Trim();
            if (!Gateway && typed.Length == 0) typed = PluginSettings.DefaultAnthropicBaseUrl;
            if (!LlmErrors.TryNormalizeBaseUrl(typed, out url, out err)) { ShowTestResult(false, err); return; }
            _baseUrl.Text = url;   // show what will be used
            var key = KeyForLoading();
            var model = GetModel(_model);
            int timeout;
            if (!int.TryParse(_timeout.Text, out timeout)) timeout = 30;
            var seq = ++_testSeq;
            _test.Enabled = false;
            _testResult.ForeColor = SystemColors.GrayText;
            _testResult.Text = "Testing…";
            var w = new BackgroundWorker();
            w.DoWork += (s, e) => e.Result = ConnectionTest.Run(provider, url, key, model, timeout);
            w.RunWorkerCompleted += (s, e) =>
            {
                if (_closed) return;
                _test.Enabled = true;
                if (seq != _testSeq) return;
                if (e.Error != null) { ShowTestResult(false, "The test couldn't run."); return; }
                var r = (ConnectionResult)e.Result;
                ShowTestResult(r.Ok, r.Message);
            };
            w.RunWorkerAsync();
        }

        private void ShowTestResult(bool ok, string message)
        {
            _testResult.ForeColor = ok ? SystemColors.ControlText : Color.FromArgb(160, 0, 0);
            _testResult.Text = (ok ? "\u2713 " : "") + message;
        }
        private int _shownProvider = -1;
        private string _otherBaseUrl;   // the other provider's URL, so switching back and forth restores it

        private void OnProviderChanged()
        {
            var bifrost = _provider.SelectedIndex == 1;
            if (_shownProvider >= 0 && _shownProvider != _provider.SelectedIndex)
            {
                var url = _baseUrl.Text;
                _baseUrl.Text = _otherBaseUrl ?? "";
                _otherBaseUrl = url;
            }
            _shownProvider = _provider.SelectedIndex;
            _modelsNote.Visible = !bifrost;
            _urlHint.Text = bifrost ? GatewayHint : DirectHint;
            _testResult.Text = "";
            ShowGatewayModels(bifrost);
            ArrangeForProvider(bifrost);
            if (bifrost && _baseUrl.Text.Trim() == PluginSettings.DefaultAnthropicBaseUrl) _baseUrl.Text = "";   // the gateway's URL goes here
            if (!bifrost && string.IsNullOrEmpty(_baseUrl.Text)) _baseUrl.Text = PluginSettings.DefaultAnthropicBaseUrl;
            if (!bifrost && _baseUrl.Text.Contains("/v1")) _baseUrl.Text = PluginSettings.DefaultAnthropicBaseUrl;
        }

        // ---- models --------------------------------------------------------------------------------------------

        private sealed class ModelItem
        {
            public string Value, Text;
            public override string ToString() { return Text; }
        }

        private IList<ModelInfo> _models;
        private string _loadedKey = "";
        private int _loadSeq;
        private bool _closed;

        private ComboBox NewModelCombo() { return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 480, Width = FieldWidth }; }

        private bool Gateway { get { return _provider.SelectedIndex == 1; } }

        private string DefaultOf(ComboBox c)
        {
            return c == _model ? PluginSettings.DefaultModel : c == _fastModel ? "claude-haiku-5-5" : "claude-opus-5-5";
        }

        private string GetModel(ComboBox c)
        {
            var item = c.SelectedItem as ModelItem;
            return item != null ? item.Value : c.Text.Trim();
        }

        /// <summary>Fills one combo from the loaded list, keeping <paramref name="value"/> selectable even if the list lacks it.</summary>
        private void SetModel(ComboBox c, string value)
        {
            value = (value ?? "").Trim();
            if (Gateway)
            {
                var g = BifrostClient.GatewayModel(DefaultOf(c));
                c.Items.Clear(); c.Items.Add(new ModelItem { Value = g, Text = g }); c.SelectedIndex = 0;
                return;
            }
            c.Items.Clear();
            var optional = c != _model;
            if (optional) c.Items.Add(new ModelItem { Value = "", Text = "Default (" + DefaultOf(c) + ")" });
            foreach (var m in _models) c.Items.Add(new ModelItem { Value = m.Id, Text = m.ToString() });
            if (value.Length == 0 && !optional) value = PluginSettings.DefaultModel;
            var pick = c.Items.Cast<ModelItem>().FirstOrDefault(i => string.Equals(i.Value, value, StringComparison.OrdinalIgnoreCase));
            if (pick == null) { pick = new ModelItem { Value = value, Text = value }; c.Items.Add(pick); }
            c.SelectedItem = pick;
        }

        /// <summary>The gateway's models are fixed (see <see cref="BifrostClient.GatewayModel"/>): shown, not chosen.</summary>
        private void ShowGatewayModels(bool gateway)
        {
            foreach (var c in new[] { _model, _fastModel, _deepModel }) c.Enabled = !gateway;
            _refreshModels.Enabled = !gateway;
            _modelStatus.Text = gateway ? "Fixed for the gateway." : "";
            SetModel(_model, _settings.Model); SetModel(_fastModel, _settings.FastModel); SetModel(_deepModel, _settings.DeepModel);
            if (!gateway && IsHandleCreated) LoadModels();
        }

        private string KeyForLoading()
        {
            var typed = _key.Text.Trim();
            return typed.Length > 0 ? typed : _settings.ResolveApiKey();
        }

        private void LoadModels()
        {
            if (Gateway || _closed) return;
            var key = KeyForLoading();
            if (string.IsNullOrEmpty(key)) { _modelStatus.Text = "Add a key to load the models it can use."; return; }
            var baseUrl = _baseUrl.Text.Trim(); var timeout = 30;
            var seq = ++_loadSeq;
            _loadedKey = _key.Text.Trim();
            _modelStatus.Text = "Loading models…";
            _refreshModels.Enabled = false;
            var w = new BackgroundWorker();
            w.DoWork += (s, e) => e.Result = AnthropicModels.List(baseUrl, key, timeout);
            w.RunWorkerCompleted += (s, e) =>
            {
                if (_closed || seq != _loadSeq) return;
                _refreshModels.Enabled = !Gateway;
                if (e.Error != null)
                {
                    _modelStatus.Text = e.Error is LlmException ? e.Error.Message : "Couldn't load models.";
                    return;
                }
                if (Gateway) return;
                var list = (IList<ModelInfo>)e.Result;
                var vals = new[] { GetModel(_model), GetModel(_fastModel), GetModel(_deepModel) };
                _models = list;
                SetModel(_model, vals[0]); SetModel(_fastModel, vals[1]); SetModel(_deepModel, vals[2]);
                _modelStatus.Text = list.Count + (list.Count == 1 ? " model" : " models") + " available to this key";
            };
            w.RunWorkerAsync();
        }

        // ---- folders -------------------------------------------------------------------------------------------

        private string PickFolder(string description)
        {
            using (var d = new FolderBrowserDialog { Description = description })
                return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
        }

        private int _guideSeq, _feedbackSeq;

        private void CheckGuides()
        {
            var paths = _layers.Items.Cast<string>().ToList();
            if (paths.Count == 0) { _guideSeq++; _guideStatus.Text = "Using the Google guide bundled with the plugin."; return; }
            var seq = ++_guideSeq;
            _guideStatus.Text = "Checking…";
            var w = new BackgroundWorker();
            w.DoWork += (s, e) => e.Result = string.Join("\n", paths.Select(DescribeGuide).ToArray());
            w.RunWorkerCompleted += (s, e) =>
            {
                if (_closed || seq != _guideSeq) return;
                _guideStatus.Text = e.Error != null ? "Couldn't check these folders." : (string)e.Result;
            };
            w.RunWorkerAsync();
        }

        private static string DescribeGuide(string path)
        {
            try
            {
                if (path.Contains("{PluginDir}")) return "Plugin folder: " + path;
                if (!Directory.Exists(path)) return "This folder doesn't exist: " + path;
                var proj = Directory.GetFiles(path, "*.flprj");
                if (proj.Length == 0) return "This folder isn't a Flare project: " + path;
                var n = GuideLayer.Load(path).Items.Count;
                return "\u2713 Flare project: " + Path.GetFileNameWithoutExtension(proj[0]) + " (" + n + " rules)";
            }
            catch (Exception) { return "Couldn't read this Flare project: " + path; }
        }

        private void CheckFeedback()
        {
            var path = _feedback.Text.Trim();
            if (path.Length == 0) { _feedbackSeq++; _feedbackStatus.Text = "Personal only"; return; }
            var seq = ++_feedbackSeq;
            _feedbackStatus.Text = "Checking…";
            var w = new BackgroundWorker();
            w.DoWork += (s, e) => e.Result = CanWrite(path);
            w.RunWorkerCompleted += (s, e) =>
            {
                if (_closed || seq != _feedbackSeq) return;
                _feedbackStatus.Text = e.Error == null && (bool)e.Result ? "\u2713 Shared with your team" : "Can't write to this folder";
            };
            w.RunWorkerAsync();
        }

        private static bool CanWrite(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return false;
                var f = Path.Combine(folder, ".house-style-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(f, "test");
                File.Delete(f);
                return true;
            }
            catch (Exception) { return false; }
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
            var typedUrl = _baseUrl.Text.Trim();
            if (!Gateway && typedUrl.Length == 0) typedUrl = PluginSettings.DefaultAnthropicBaseUrl;
            string cleanUrl = "", urlError;
            if ((typedUrl.Length > 0 || !Gateway) && !LlmErrors.TryNormalizeBaseUrl(typedUrl, out cleanUrl, out urlError))
            {
                Notify.Warn(this, urlError, "Settings"); _baseUrl.Focus(); return;
            }
            _baseUrl.Text = cleanUrl;
            _settings.BaseUrl = cleanUrl;
            if (!Gateway)   // the gateway's fixed models would overwrite the direct-key choices
            {
                _settings.Model = GetModel(_model);
                _settings.FastModel = GetModel(_fastModel);
                _settings.DeepModel = GetModel(_deepModel);
            }
            _settings.TimeoutSeconds = timeout;
            _settings.WriteDiagnostics = _diag.Checked;
            _settings.StyleLayers = string.Join("\n", _layers.Items.Cast<string>().ToArray());
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
