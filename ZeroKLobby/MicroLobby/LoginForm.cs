﻿using System;
using System.Drawing;
using System.Windows.Forms;
using ZeroKLobby.Controls;

namespace ZeroKLobby.MicroLobby
{
    public class LoginForm: ZklBaseForm
    {
        private readonly BitmapButton btnCancel;
        private readonly BitmapButton btnSubmit;
        private readonly Label lbInfo;

        private readonly ZklTextBox tbLogin;
        private readonly ZklTextBox tbPassword;


        public LoginForm() {
            Font = Config.GeneralFontBig;
            SuspendLayout();

            btnSubmit = new BitmapButton
            {
                ButtonStyle = FrameBorderRenderer.StyleType.DarkHive,
                DialogResult = DialogResult.OK,
                Location = new Point(70, 255),
                Size = new Size(104, 44),
                SoundType = SoundPalette.SoundType.Click,
                Text = "OK"
            };
            btnSubmit.Click += btnSubmit_Click;

            tbLogin = new ZklTextBox { Location = new Point(237, 123), Size = new Size(146, 24), TabIndex = 1 };
            tbLogin.TextBox.TextChanged += (sender, args) => { tbLogin_TextBox_TextChanged(sender, args); };

            tbPassword = new ZklTextBox
            {
                Location = new Point(237, 164),
                Size = new Size(146, 24),
                TabIndex = 2,
                TextBox = { UseSystemPasswordChar = true }
            };

            lbInfo = new Label
            {
                BackColor = Color.Transparent,
                ForeColor = Color.Red,
                Location = new Point(29, 20),
                Size = new Size(414, 51),
                Text = "Error",
                TextAlign = ContentAlignment.MiddleCenter
            };

            btnCancel = new BitmapButton
            {
                ButtonStyle = FrameBorderRenderer.StyleType.DarkHive,
                DialogResult = DialogResult.Cancel,
                Location = new Point(286, 255),
                Size = new Size(97, 44),
                SoundType = SoundPalette.SoundType.Click,
                Text = "Cancel"
            };
            btnCancel.Click += btnCancel_Click;

            var label1 = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(112, 126),
                Size = new Size(89, 18),
                Text = "Login name:"
            };

            var label2 = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(121, 167),
                Size = new Size(79, 18),
                Text = "Password:"
            };

            AcceptButton = btnSubmit;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.DimGray;
            CancelButton = btnCancel;
            ClientSize = new Size(482, 331);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(lbInfo);
            Controls.Add(tbLogin);
            Controls.Add(tbPassword);
            Controls.Add(btnCancel);
            Controls.Add(btnSubmit);
            ForeColor = Color.White;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Zero-K Login/Register";
            Load += LoginForm_Load;
            ResumeLayout(false);
            PerformLayout();

            tbLogin.Text = Program.Conf.LobbyPlayerName;
            if (string.IsNullOrEmpty(tbLogin.Text))
            {
                tbLogin.Text = Program.SteamHandler.SteamName;
                Program.SteamHandler.SteamHelper.SteamOnline += SteamApiOnSteamOnline;
            }
            tbPassword.Text = Program.Conf.LobbyPlayerPassword;

            tbLogin.TextBox.PreviewKeyDown += onKeyPress;
            tbPassword.TextBox.PreviewKeyDown += onKeyPress;
        }

        public string InfoText { set { lbInfo.Text = value; } }


        public string LoginValue { get { return tbLogin.Text; } }

        public string PasswordValue { get { return tbPassword.Text; } }

        private void onKeyPress(object sender, PreviewKeyDownEventArgs args) {
            if (args.KeyCode == Keys.Enter) btnSubmit_Click(this, args);
            if (args.KeyCode == Keys.Escape) btnCancel_Click(this, args);
        }

        protected override void OnPaintBackground(PaintEventArgs e) {
            var page = Program.MainWindow.navigationControl.CurrentNavigatable as Control;
            if (page?.BackgroundImage != null) this.RenderControlBgImage(page, e);
            else e.Graphics.Clear(Config.BgColor);
            FrameBorderRenderer.Instance.RenderToGraphics(e.Graphics, DisplayRectangle, FrameBorderRenderer.StyleType.Shraka);
        }

        private void SteamApiOnSteamOnline() {
            Program.MainWindow.InvokeFunc(
                () =>
                {
                    if (string.IsNullOrEmpty(tbLogin.Text)) tbLogin.Text = Program.SteamHandler.SteamName;
                    Program.SteamHandler.SteamHelper.SteamOnline -= SteamApiOnSteamOnline;
                });
        }

        private void btnCancel_Click(object sender, EventArgs e) {
            DialogResult = DialogResult.Cancel;
            Close();
        }


        private void btnSubmit_Click(object sender, EventArgs e) {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void LoginForm_Load(object sender, EventArgs e) {
            Icon = ZklResources.ZkIcon;
        }

        private void tbLogin_TextBox_TextChanged(object sender, EventArgs e) {
            if (sender is TextBox)
            {
                var textBox = (TextBox)sender;
                int pos = textBox.SelectionStart;
                int invalidCount = 0;
                textBox.Text = StripInvalidLobbyNameChars(textBox.Text, out invalidCount);
                pos = pos - invalidCount;
                if (pos < 0) pos = 0;
                textBox.SelectionStart = pos;   // else typing an invalid character causes caret position to return to start
            }
        }

        // adapted from the one in ZkData.Account
        static string StripInvalidLobbyNameChars(string name, out int invalidCount)
        {
            invalidCount = 0;
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (ValidLobbyNameCharacter(c)) sb.Append(c);
                else invalidCount++;
            }
            return sb.ToString();
        }

        // duplicate of the one in ZkData.Account
        static bool ValidLobbyNameCharacter(char c)
        {
            if (c >= 'a' && c <= 'z') return true;
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= '0' && c <= '9') return true;
            if (c == '_') return true;
            if (c == '[' || c == ']') return true;
            return false;
        }
    }
}