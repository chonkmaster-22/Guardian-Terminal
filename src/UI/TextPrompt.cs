using System;
using System.Drawing;
using System.Windows.Forms;

namespace GuardianTerminal.UI
{
    // A one-line question ("Name of the new profile:"); returns the text, or null when cancelled.
    internal sealed class TextPrompt : Form
    {
        private readonly TextBox box = new TextBox();

        private TextPrompt(string title, string question, string initial)
        {
            float s = DeviceDpi / 96f;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size((int)(420 * s), (int)(140 * s));
            Label q = new Label { Text = question, Location = new Point((int)(16 * s), (int)(14 * s)), Size = new Size((int)(388 * s), (int)(22 * s)) };
            box.SetBounds((int)(16 * s), (int)(42 * s), (int)(388 * s), (int)(26 * s));
            box.BackColor = Theme.Surface;
            box.ForeColor = Theme.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Text = initial ?? "";
            FlatButton ok = Theme.MakeButton("OK", true);
            ok.Font = Theme.BodyBold;
            ok.SetBounds((int)(304 * s), (int)(92 * s), (int)(100 * s), (int)(34 * s));
            ok.DialogResult = DialogResult.OK;
            FlatButton cancel = Theme.MakeButton(L.T("Cancel"), false);
            cancel.SetBounds((int)(194 * s), (int)(92 * s), (int)(100 * s), (int)(34 * s));
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { q, box, ok, cancel });
            HandleCreated += delegate { NativeMethods.UseDarkTitleBar(Handle); };
            Shown += delegate { box.SelectAll(); box.Focus(); };
        }

        public static string Ask(IWin32Window owner, string title, string question, string initial)
        {
            using (TextPrompt p = new TextPrompt(title, question, initial))
                return p.ShowDialog(owner) == DialogResult.OK && p.box.Text.Trim().Length > 0 ? p.box.Text.Trim() : null;
        }
    }
}
