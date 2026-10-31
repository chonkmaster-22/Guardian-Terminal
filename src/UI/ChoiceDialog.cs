using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuardianTerminal.UI
{
    // "This download has several versions inside: which one?" (mods that ship options as separate romfs folders)
    internal sealed class ChoiceDialog : Form
    {
        private readonly ListBox list = new ListBox();

        private ChoiceDialog(string message, List<string> options)
        {
            float s = DeviceDpi / 96f;
            Text = L.T("Choose a version");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size((int)(520 * s), (int)(300 * s));
            Padding = new Padding((int)(16 * s));

            Label l = new Label { Text = message, Dock = DockStyle.Top, Height = (int)(44 * s), ForeColor = Theme.Text };
            list.Dock = DockStyle.Fill;
            list.BackColor = Theme.Surface;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.IntegralHeight = false;
            foreach (string o in options) list.Items.Add(o);
            list.SelectedIndex = 0;
            list.DoubleClick += delegate { DialogResult = DialogResult.OK; };

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = (int)(48 * s), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, (int)(10 * s), 0, 0) };
            FlatButton ok = Theme.MakeButton(L.T("Use this one"), true);
            ok.Font = Theme.BodyBold;
            ok.Size = new Size((int)(130 * s), (int)(34 * s));
            ok.DialogResult = DialogResult.OK;
            FlatButton cancel = Theme.MakeButton(L.T("Cancel"), false);
            cancel.Size = new Size((int)(100 * s), (int)(34 * s));
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(list);
            Controls.Add(buttons);
            Controls.Add(l);
            HandleCreated += delegate { NativeMethods.UseDarkTitleBar(Handle); };
        }

        public static string Ask(IWin32Window owner, string message, List<string> options)
        {
            using (ChoiceDialog d = new ChoiceDialog(message, options))
                return d.ShowDialog(owner) == DialogResult.OK && d.list.SelectedItem != null ? (string)d.list.SelectedItem : null;
        }
    }
}
