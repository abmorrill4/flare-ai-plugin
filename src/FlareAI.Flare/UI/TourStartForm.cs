using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>Where the guided first lint runs.</summary>
    internal enum TourChoice { Skip, Sample, OpenTopic }

    /// <summary>
    /// The first screen of the guided first lint, right after setup: practice on the sample project or on the open topic
    /// (offered only when a topic is open), or Skip.
    /// </summary>
    internal sealed class TourStartForm : Form
    {
        public TourChoice Choice { get; private set; }

        public TourStartForm(bool topicOpen)
        {
            Text = "House Style — Practice";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;
            var page = new Panel { Left = 0, Top = 0 };
            Controls.Add(page);
            var l = new StackLayout(page, DeviceDpi / 96f);
            l.Heading("Practice your first lint");
            l.Note("Follow five short steps to lint a topic and work through its issues.", true, false);
            var sample = l.Add(new Button { Text = "Practice on a sample project", AutoSize = true, Left = l.LabelLeft, Top = l.Y, MinimumSize = new Size(l.S(240), l.S(28)) });
            l.Y += sample.PreferredSize.Height + l.S(6);
            Button open = null;
            if (topicOpen)
            {
                open = l.Add(new Button { Text = "Use the topic I have open", AutoSize = true, Left = l.LabelLeft, Top = l.Y, MinimumSize = new Size(l.S(240), l.S(28)) });
                l.Y += open.PreferredSize.Height + l.S(6);
            }
            l.Gap(6);
            var skip = new Button { Text = "Skip", AutoSize = true, MinimumSize = new Size(l.S(80), l.S(26)), DialogResult = DialogResult.Cancel };
            page.Size = new Size(l.Width, l.Y + l.S(4));
            Controls.Add(skip);
            ClientSize = new Size(l.Width, page.Bottom + l.S(26) + l.S(16));
            skip.Location = new Point(ClientSize.Width - l.S(16) - Math.Max(l.S(80), skip.PreferredSize.Width), page.Bottom + l.S(4));
            CancelButton = skip;
            AcceptButton = sample;
            sample.Click += (s, e) => { Choice = TourChoice.Sample; DialogResult = DialogResult.OK; };
            if (open != null) open.Click += (s, e) => { Choice = TourChoice.OpenTopic; DialogResult = DialogResult.OK; };
            skip.Click += (s, e) => Choice = TourChoice.Skip;
            A11y.Name(skip, "Skip", "Closes this without the practice. Start it later from Help.");
            A11y.Apply(this);
        }
    }
}
