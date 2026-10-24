using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Flare.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Flare
{
    /// <summary>
    /// The end-to-end runner's hand on the plugin (tests/e2e). Off unless Flare was started with HOUSESTYLE_E2E_DIR set;
    /// then it polls that folder for cmd-N.json files, runs each on Flare's UI thread and writes res-N.json.
    /// Commands go through the same handlers and controls a writer clicks; nothing here changes what they do.
    /// Flare's own UI (trial dialog, ribbon) is driven with UI Automation by the runner; this covers the issues pane and
    /// House Style's dialogs, whose WinForms controls lose their roles inside Flare's WPF dock.
    /// </summary>
    internal sealed class E2EHook
    {
        public const string EnvVar = "HOUSESTYLE_E2E_DIR";

        private readonly string _dir;
        private readonly Func<string, Action> _command;
        private readonly Func<string, bool> _open;
        private readonly Func<Form> _main;
        private readonly Timer _timer = new Timer { Interval = 250 };
        private int _next = 1;

        private E2EHook(string dir, Func<string, Action> command, Func<string, bool> open, Func<Form> main)
        {
            _dir = dir; _command = command; _open = open; _main = main;
            _timer.Tick += (s, e) => Safe.Run("E2E", Poll);
        }

        /// <summary>A test run keeps its settings, guide and logs in its own folder (the runner seeds it), never the writer's.</summary>
        public static void IsolateIfRequested()
        {
            var dir = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(dir)) Paths.Root = Path.Combine(dir, "appdata");
        }

        /// <summary>Starts the hook when the runner asked for it; call on Flare's UI thread.</summary>
        public static void StartIfRequested(Func<string, Action> command, Func<string, bool> open, Func<Form> main)
        {
            var dir = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new E2EHook(dir, command, open, main)._timer.Start();
            var build = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(E2EHook).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            File.WriteAllText(Path.Combine(dir, "ready.json"), JsonConvert.SerializeObject(new { version = build == null ? Plugin.Version : build.InformationalVersion }));
            Diagnostics.Log("E2E hook on: " + dir);
        }

        private void Poll()
        {
            var cmd = Path.Combine(_dir, "cmd-" + _next + ".json");
            if (!File.Exists(cmd)) return;
            var id = _next++;
            JObject c;
            try { c = JObject.Parse(File.ReadAllText(cmd)); }
            catch (IOException) { _next--; return; }   // the runner is still writing it
            catch (Exception ex) { Answer(id, () => { throw new ArgumentException("Bad command file: " + ex.Message); }); return; }
            var verb = (string)c["verb"] ?? "";
            // Reads and dialog commands answer now (they run inside a dialog's message loop too); the rest are posted so a
            // dialog they open doesn't block the hook, and answer when they return.
            if (verb == "state" || verb.StartsWith("dialog", StringComparison.Ordinal)) Answer(id, () => Run(verb, c));
            else _main().BeginInvoke((Action)(() => Answer(id, () => Run(verb, c))));
        }

        private void Answer(int id, Func<object> run)
        {
            object result;
            try { result = new { ok = true, value = run(), state = State() }; }
            catch (Exception ex) { result = new { ok = false, error = ex.GetType().Name + ": " + ex.Message, state = State() }; }
            var path = Path.Combine(_dir, "res-" + id + ".json");
            File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(result, Formatting.Indented));
            File.Move(path + ".tmp", path);
        }

        private object Run(string verb, JObject c)
        {
            var arg = (string)c["arg"];
            switch (verb)
            {
                case "state": return null;
                case "command": _command(arg)(); return null;                       // a ribbon command by Commands id
                case "open": if (!_open(arg)) throw new InvalidOperationException("Flare didn't open " + arg); return null;
                case "click": Click(Pane(), arg); return null;                        // a button in the issues pane
                case "rowMenu": return Pane().E2ERowMenu((int)c["index"], arg);       // right-click a row, click an item
                case "dialogClick": Click(Dialog((string)c["title"]), arg); return null;
                case "dialogSelect":                                                  // a list item by index, or a list view row by its text
                    if (c["text"] != null) SelectRow(Dialog((string)c["title"]), (string)c["text"]); else Select(Dialog((string)c["title"]), (int)c["index"]);
                    return null;
                case "dialogType": Type(Dialog((string)c["title"]), (string)c["name"], arg); return null;   // a text box or list by its accessible name
                default: throw new ArgumentException("Unknown verb " + verb);
            }
        }

        private static FindingsPanel Pane()
        {
            var p = PanelHost.Current;
            if (p == null) throw new InvalidOperationException("The issues pane isn't open.");
            return p;
        }

        private Form Dialog(string title)
        {
            var f = Application.OpenForms.Cast<Form>().FirstOrDefault(x => IsOurs(x) && x.Text.Contains(title));
            if (f == null) throw new InvalidOperationException("No open dialog titled " + title);
            return f;
        }

        /// <summary>Clicks the visible, enabled button whose text (without "&amp;" or "…") starts with <paramref name="text"/>.</summary>
        private static void Click(Control root, string text)
        {
            var b = All(root).OfType<ButtonBase>().FirstOrDefault(x => x.Visible && x.Enabled && Label(x).StartsWith(text, StringComparison.Ordinal));
            if (b == null) throw new InvalidOperationException("No enabled button " + text);
            var button = b as Button;
            if (button != null) button.PerformClick();
            else b.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
        }

        /// <summary>A visible House Style window (Flare's own forms are left out; the floating pane counts).</summary>
        private static bool IsOurs(Form f)
        {
            return f.Visible && (f.GetType().Namespace ?? "").StartsWith("FlareAI", StringComparison.Ordinal) || f.Visible && f.Controls.OfType<FindingsPanel>().Any();
        }

        private static string Label(ButtonBase b) { return b.Text.Replace("&", "").TrimEnd('…'); }

        private static void Select(Control root, int index)
        {
            var box = All(root).OfType<ListBox>().FirstOrDefault(x => x.Visible);
            if (box == null) throw new InvalidOperationException("No list in the dialog");
            box.SelectedIndex = index;
        }

        /// <summary>Selects the first row of the dialog's list view whose text contains <paramref name="text"/>, as a click would.</summary>
        private static void SelectRow(Control root, string text)
        {
            var list = All(root).OfType<ListView>().FirstOrDefault(x => x.Visible);
            if (list == null) throw new InvalidOperationException("No list view in the dialog");
            var item = list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            if (item == null) throw new InvalidOperationException("No row with " + text);
            list.SelectedItems.Clear();
            item.Selected = true; item.Focused = true; item.EnsureVisible();
        }

        /// <summary>Types <paramref name="text"/> into the dialog's text box (or picks it in the list) named <paramref name="name"/>.</summary>
        private static void Type(Control root, string name, string text)
        {
            var box = All(root).FirstOrDefault(x => x.Visible && (x is TextBox || x is ComboBox) && x.AccessibleName == name);
            if (box == null) throw new InvalidOperationException("No box named " + name);
            var combo = box as ComboBox;
            if (combo != null && combo.DropDownStyle == ComboBoxStyle.DropDownList) combo.SelectedItem = combo.Items.Cast<object>().First(i => i.ToString() == text);
            else box.Text = text;
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var d in All(c)) yield return d;
            }
        }

        /// <summary>The pane, every House Style dialog and what it shows: enough to assert on without a screenshot.</summary>
        private object State()
        {
            var pane = PanelHost.Current;
            return new
            {
                pane = pane == null ? null : new { panel = pane.E2EState(), buttons = Buttons(pane) },
                dialogs = Application.OpenForms.Cast<Form>().Where(IsOurs).Select(f => new
                {
                    title = f.Text,
                    focus = Focus(f),
                    buttons = Buttons(f),
                    text = All(f).OfType<Label>().Where(l => l.Visible && l.Text.Length > 0).Select(l => l.Text).ToList(),
                    items = All(f).OfType<ListBox>().SelectMany(l => l.Items.Cast<object>().Select(i => i.ToString())).ToList(),
                    rows = All(f).OfType<ListView>().SelectMany(l => l.Items.Cast<ListViewItem>().Select(i => new { text = i.Text, tip = i.ToolTipText, selected = i.Selected })).ToList(),
                    boxes = All(f).Where(x => x.Visible && (x is TextBox || x is ComboBox) && !string.IsNullOrEmpty(x.AccessibleName)).Select(x => new { name = x.AccessibleName, text = x.Text }).ToList()
                }).ToList()
            };
        }

        /// <summary>The control with the keyboard focus in <paramref name="f"/>: its accessible name, else its text.</summary>
        private static string Focus(Form f)
        {
            Control c = f.ActiveControl;
            for (var inner = c as ContainerControl; inner != null && inner.ActiveControl != null; inner = inner.ActiveControl as ContainerControl) c = inner.ActiveControl;
            return c == null ? null : !string.IsNullOrEmpty(c.AccessibleName) ? c.AccessibleName : c.Text.Replace("&", "").TrimEnd('…');
        }

        private static List<object> Buttons(Control root)
        {
            return All(root).OfType<ButtonBase>().Where(b => b.Visible).Select(b => (object)new { text = Label(b), enabled = b.Enabled }).ToList();
        }
    }
}
