﻿using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZeroKLobby.MicroForms
{
    public partial class PromptForm : Form
    {
        const int formHeight = 248;
        const int detailHeight = 101;
        const int formWidth = 216;

        public PromptForm()
        {
            InitializeComponent();
        }

        private void detailBox_VisibleChanged(object sender, EventArgs e)
        {
            if (DesignMode) return;
            if (!detailBox.Visible)
            {
                Size = new Size((int)formWidth, (int)(formHeight - detailHeight));
            }
            else
            {
                Size = new Size((int)formWidth, (int)formHeight);
            }
        }
    }
}
