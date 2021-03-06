// Contact: Jan Lichovník  licho@licho.eu, tel: +420 604 935 349,  www.itl.cz
// Last change by: licho  29.06.2016

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ZeroKLobby.Controls;

namespace ZeroKLobby.MicroLobby
{
    internal class MyToolTabItemRenderer: ToolStripProfessionalRenderer
    {
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var rectangle = new Rectangle(0, 0, e.Item.Size.Width - 1, e.Item.Size.Height - 1);

            var but = e.Item as ToolStripButton;
            if (but?.Checked == true) FrameBorderRenderer.Instance.RenderToGraphics(e.Graphics, rectangle, FrameBorderRenderer.StyleType.DarkHiveGlow);
            else if (e.Item.Selected)
            {
                var glow = new SolidBrush(Color.FromArgb(89, 23, 252, 255));
                e.Graphics.FillRectangle(glow, rectangle);
            }
            else base.OnRenderButtonBackground(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            //base.OnRenderToolStripBackground(e);
        }

        protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
        {
            //base.OnRenderToolStripPanelBackground(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            //base.OnRenderToolStripBorder(e);
        }

        protected override void OnRenderToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e)
        {
            //base.OnRenderToolStripContentPanelBackground(e);
        }
    }

    public class ToolTabs: ZklBaseControl
    {
        /// <summary>
        ///     This dictionary is a map of button Name (toolStrip.Items) to Control (panel.Controls)
        /// </summary>
        private readonly Dictionary<string, Control> controls = new Dictionary<string, Control>();
        private readonly Panel panel = new Panel { Dock = DockStyle.Fill };
        private readonly ToolStrip toolStrip = new ToolStrip
        {
            Dock = DockStyle.Left,
            Stretch = false,
            GripStyle = ToolStripGripStyle.Hidden,
            ShowItemToolTips = false,
            Font = Config.GeneralFont,
            Tag = HiliteLevel.None,
            RenderMode = ToolStripRenderMode.Professional,
            AutoSize = true, //auto reduce space usage
            MaximumSize = new Size(155, 4000),
            MinimumSize = new Size(100, 0),
            Padding = new Padding(3,0,10,0),
            Renderer = new MyToolTabItemRenderer()
        };
        private ToolStripButton activeButton;
        private ToolStripItem lastHoverItem;


        public ToolTabs()
        {
            Controls.Add(panel);
            Controls.Add(toolStrip);

            BackColor = Config.BgColor; //for any child control to inherit it
            ForeColor = Config.TextColor;
            Init(toolStrip);

            //set colour for overflow button:
            var ovrflwBtn = toolStrip.OverflowButton;
            ovrflwBtn.BackColor = Color.DimGray; //note: the colour of arrow on OverFlow button can't be set, that's why we couldn't use User's theme


            var timer = new Timer { Interval = 1000 };

            timer.Tick += (s, e) =>
            {
                foreach (var button in toolStrip.Items.OfType<ToolStripButton>())
                {
                    if (button.Tag is HiliteLevel && (HiliteLevel)button.Tag == HiliteLevel.Flash) button.BackColor = button.BackColor == Color.SkyBlue ? Color.Empty : Color.SkyBlue;
                }
            };

            timer.Start();
        }

        public ToolStripButton ActiveButton
        {
            get { return activeButton; }
            set
            {
                if (activeButton == value) return;
                if (activeButton != null) activeButton.Checked = false;
                value.Checked = true;
                activeButton = value;
                foreach (Control control in panel.Controls) control.Visible = control == controls[activeButton.Name];
            }
        }

        public IEnumerable<Control> Tabs { get { return controls.Values; } }


        public void DisposeAllTabs()
        {
            foreach (var control in controls.Values) control.Dispose();
            ClearTabs();
        }

        public void AddTab(string name, string title, Control control, Image icon, string tooltip, int sortImportance)
        {
            var isPrivateTab = control is PrivateMessageControl;
            name = isPrivateTab ? name + "_pm" : name + "_chan";
            var button = new ToolStripButton(name, icon)
            {
                Name = name,
                Alignment = ToolStripItemAlignment.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = ContentAlignment.MiddleLeft,
                AutoToolTip = false,
                ToolTipText = tooltip,
                Tag = sortImportance,
                Text = title,
            };
            if (control is BattleChatControl) button.Height = button.Height*2;
            button.MouseEnter += button_MouseEnter;
            button.MouseLeave += button_MouseLeave;
            button.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var point = new Point(button.Bounds.Location.X + e.X, button.Bounds.Location.Y + e.Y);
                    try
                    {
                        Program.ToolTip.Visible = false;
                        if (control is ChatControl) ContextMenus.GetChannelContextMenu((ChatControl)control).Show(toolStrip, point);
                        else if (control is PrivateMessageControl) ContextMenus.GetPrivateMessageContextMenu((PrivateMessageControl)control).Show(toolStrip, point);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("Error displaying tooltip:{0}", ex);
                    }
                    finally
                    {
                        Program.ToolTip.Visible = true;
                    }
                }
                else if (e.Button == MouseButtons.Middle)
                {
                    if (control is ChatControl)
                    {
                        var chatControl = (ChatControl)control;
                        if (chatControl.CanLeave) Program.TasClient.LeaveChannel(chatControl.ChannelName);
                    }
                    else if (control is PrivateMessageControl)
                    {
                        var chatControl = (PrivateMessageControl)control;
                        ActionHandler.ClosePrivateChat(chatControl.UserName);
                    }
                }
            };

            var added = false;
            var insertItemText = sortImportance + Name;
            for (var i = 0; i < toolStrip.Items.Count; i++)
            {
                var existingItemText = (int)toolStrip.Items[i].Tag + toolStrip.Items[i].Text;
                if (string.Compare(existingItemText, insertItemText) < 0)
                {
                    toolStrip.Items.Insert(i, button);
                    added = true;
                    break;
                }
            }
            if (!added) toolStrip.Items.Add(button);

            button.Click += (s, e) =>
            {
                try
                {
                    if (control is BattleChatControl)
                    {
                        NavigationControl.Instance.Path = "chat/battle";
                    }
                    else if (control is PrivateMessageControl)
                    {
                        var pmControl = (PrivateMessageControl)control;
                        var userName = pmControl.UserName;
                        NavigationControl.Instance.Path = "chat/user/" + userName;
                    }
                    else if (control is ChatControl)
                    {
                        var chatControl = (ChatControl)control;
                        var channelName = chatControl.ChannelName;
                        NavigationControl.Instance.Path = "chat/channel/" + channelName;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.ToString());
                }
            };
            control.Dock = DockStyle.Fill;
            control.Visible = false;
            controls.Add(name, control);
            panel.Controls.Add(control);
        }

        public void ClearTabs()
        {
            controls.Clear();
            toolStrip.Items.Clear();
            panel.Controls.Clear();
        }


        public bool SetHilite(string tabName, HiliteLevel level, bool isPrivateTab)
        {
            //Some complicated sequence of call (for reference):
            //ChatControl.client_said           -> MainWindow.NotifyUser -> NavigationControl.HilitePath -> ChatTab.Hilite -> this.SetHiLite
            //BattleChatControl.TasClient_Said  -> MainWindow.NotifyUser -> NavigationControl.HilitePath -> ChatTab.Hilite -> this.SetHiLite
            //ChatTab.client_said               -> MainWindow.NotifyUser -> NavigationControl.HilitePath -> ChatTab.Hilite -> this.SetHiLite

            tabName = isPrivateTab ? tabName + "_pm" : tabName + "_chan";
            var button = GetItemByName(toolStrip.Items, tabName);
            if (button == null) return false;
            var current = button.Tag as HiliteLevel?;
            if (current != null && level == HiliteLevel.Bold && current.Value == HiliteLevel.Flash) return false; // dont change from flash to bold
            button.Tag = level;
            var oldFont = button.Font;
            switch (level)
            {
                case HiliteLevel.None:
                    button.BackColor = Color.Empty;
                    button.Font = new Font(oldFont, FontStyle.Regular);
                    //oldFont.Dispose();
                    break;
                case HiliteLevel.Bold:
                    button.BackColor = Color.Empty;
                    button.Font = new Font(oldFont, FontStyle.Bold | FontStyle.Italic);
                    //oldFont.Dispose();
                    break;
                case HiliteLevel.Flash:
                    button.Font = new Font(oldFont, FontStyle.Bold);
                    //oldFont.Dispose();

                    break;
            }
            return true;
        }

        public ChatControl GetChannelTab(string name)
        {
            Control control;
            controls.TryGetValue(name + "_chan", out control);
            return control as ChatControl;
        }

        public PrivateMessageControl GetPrivateTab(string name)
        {
            Control control;
            controls.TryGetValue(name + "_pm", out control);
            return control as PrivateMessageControl;
        }

        public void RemoveChannelTab(string key)
        {
            key = key + "_chan";
            RemoveTab(key);
        }

        public void RemovePrivateTab(string key)
        {
            key = key + "_pm";
            RemoveTab(key);
        }

        private void RemoveTab(string key)
        {
            if (!controls.ContainsKey(key)) return;

            panel.Controls.Remove(controls[key]);
            controls.Remove(key);
            if (Program.ToolTip != null) Program.ToolTip.Clear(GetItemByName(toolStrip.Items, key));
            toolStrip.Items.RemoveAt(FindItemsByExactName(toolStrip.Items, key));
        }

        public void SelectChannelTab(string name)
        {
            ActiveButton = (ToolStripButton)GetItemByName(toolStrip.Items, name + "_chan");
        }

        public void SelectPrivateTab(string name)
        {
            ActiveButton = (ToolStripButton)GetItemByName(toolStrip.Items, name + "_pm");
        }

        /// <summary>
        ///     Get index of ToolStripItem in ToolStripItemCollection using case-sensitive search.
        /// </summary>
        private int FindItemsByExactName(ToolStripItemCollection items, string name)
        {
            for (var i = 0; i < items.Count; i++) if (items[i].Name == name) return i;
            return -1;
        }

        /// <summary>
        ///     Get ToolStripItem in ToolStripItemCollection using case-sensitive search.
        /// </summary>
        private ToolStripItem GetItemByName(ToolStripItemCollection collectionItem, string name)
        {
            var index = FindItemsByExactName(collectionItem, name);
            if (index == -1) return null;
            return collectionItem[index];
        }

        public string GetNextTabPath()
        {
            return GetAdjTabPath(true);
        }

        public string GetPrevTabPath()
        {
            return GetAdjTabPath(false);
        }

        private string GetAdjTabPath(bool next)
        {
            var path = "chat/";

            var nextControl = GetNextControl(controls[activeButton.Name], next);
            var nextButtonName = nextControl.Name;
            if (nextButtonName != "")
            {
                if (nextControl is BattleChatControl)
                {
                    path += "battle";
                }
                else if (nextControl is PrivateMessageControl)
                {
                    path += "user/";
                    path += nextButtonName;
                }
                else if (nextControl is ChatControl)
                {
                    path += "channel/";
                    path += nextButtonName;
                }
            }
            return path;
        }

        public void SetIcon(string tabName, Image icon, bool isPrivateTab)
        {
            tabName = isPrivateTab ? tabName + "_pm" : tabName + "_chan";
            var button = (ToolStripButton)GetItemByName(toolStrip.Items, tabName);
            button.Image = icon;
        }

        private void button_MouseEnter(object sender, EventArgs e)
        {
            var item = (ToolStripItem)sender;
            if (item != lastHoverItem)
            {
                lastHoverItem = item;
                Program.ToolTip.SetText(toolStrip, item.ToolTipText);
            }
        }

        private void button_MouseLeave(object sender, EventArgs e)
        {
            var item = (ToolStripItem)sender;
            if (item == lastHoverItem)
            {
                lastHoverItem = null;
                Program.ToolTip.Clear(toolStrip);
            }
        }
    }
}