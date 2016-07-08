using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using JetBrains.Annotations;
using LobbyClient;
using ZkData;

namespace ZeroKLobby.MicroLobby
{
    public partial class BattleListControl: ScrollableControl
    {
        readonly Dictionary<BattleIcon, Point> battleIconPositions = new Dictionary<BattleIcon, Point>();
        Point openBattlePosition;
        readonly Regex filterOrSplit = new Regex(@"\||\bOR\b");
        string filterText;
        object lastTooltip;
        readonly IEnumerable<BattleIcon> model;
        Point previousLocation;
        readonly bool sortByPlayers;

        List<BattleIcon> view = new List<BattleIcon>();
        static Pen dividerPen = new Pen(Color.DarkCyan, 3) {DashStyle = DashStyle.Dash};
        static Font dividerFont = Config.GeneralFontBig;
        static SolidBrush dividerFontBrush = new SolidBrush(Config.TextColor);


        public string FilterText
        {
            get { return filterText; }
            set
            {
                filterText = value;
                Program.Conf.BattleFilter = value;
                Program.SaveConfig();
                Repaint();
            }
        }


        public bool HideEmpty { get; set; }

        public bool HideFull { get; set; }

        public bool HidePassworded { get; set; }

        public bool ShowOfficial { get; set; }


        public BattleListControl()
        {
            InitializeComponent();
            AutoScroll = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            BackColor = Config.BgColor;
            FilterText = Program.Conf.BattleFilter;
            Disposed += BattleListControl_Disposed;
            Program.BattleIconManager.BattleAdded += HandleBattle;
            Program.BattleIconManager.BattleChanged += HandleBattle;
            Program.BattleIconManager.RemovedBattle += HandleBattle;
            model = Program.BattleIconManager.BattleIcons;
            sortByPlayers = true;

            Repaint();
        }

        void BattleListControl_Disposed(object sender, EventArgs e)
        {
            Program.BattleIconManager.BattleChanged -= HandleBattle;
            Program.BattleIconManager.BattleAdded -= HandleBattle;
            Program.BattleIconManager.RemovedBattle -= HandleBattle;
        }

        public static bool BattleWordFilter(Battle x, string[] words)
        {
            bool hide = false;
            foreach (string wordIterated in words)
            {
                string word = wordIterated;
                bool negation = false;
                if (word.StartsWith("-"))
                {
                    word = word.Substring(1);
                    negation = true;
                }
                if (String.IsNullOrEmpty(word)) continue; // dont filter empty words

                bool isSpecialWordMatch;
                if (FilterSpecialWordCheck(x, word, out isSpecialWordMatch)) // if word is mod shortcut, handle specially
                {
                    if ((!negation && !isSpecialWordMatch) || (negation && isSpecialWordMatch))
                    {
                        hide = true;
                        break;
                    }
                }
                else
                {
                    bool playerFound = x.Users.Values.Any(u => u.Name.ToUpper().Contains(word));
                    bool titleFound = x.Title.ToUpper().Contains(word);
                    bool modFound = x.ModName.ToUpper().Contains(word);
                    bool mapFound = x.MapName.ToUpper().Contains(word);
                    if (!negation)
                    {
                        if (!(playerFound || titleFound || modFound || mapFound))
                        {
                            hide = true;
                            break;
                        }
                    }
                    else
                    {
                        if (playerFound || titleFound || modFound || mapFound) // for negation ignore players
                        {
                            hide = true;
                            break;
                        }
                    }
                }
            }
            return (!hide);
        }

        public void ShowHostDialog(GameInfo filter)
        {
            using (var dialog = new HostDialog(filter))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                ActionHandler.StopBattle();

                ActionHandler.SpawnAutohost(dialog.GameRapidTag, dialog.BattleTitle, dialog.Password, null);
            }
        }

        protected override void OnMouseDown([NotNull] MouseEventArgs e)
        {
            if (e == null) throw new ArgumentNullException("e");
            base.OnMouseDown(e);
            Battle battle = GetBattle(e.X, e.Y);
            if (e.Button == MouseButtons.Left)
            {
                if (battle != null)
                {
                    if (battle.IsPassworded)
                    {
                        // hack dialog Program.FormMain
                        using (var form = new AskBattlePasswordForm(battle.Founder.Name)) if (form.ShowDialog() == DialogResult.OK) ActionHandler.JoinBattle(battle.BattleID, form.Password);
                    } else 
                    {
                        ActionHandler.JoinBattle(battle.BattleID, null);    

                    }
                    
                }
                else if (OpenGameButtonHitTest(e.X, e.Y)) ShowHostDialog(KnownGames.GetDefaultGame());
            }
            else if (e.Button == MouseButtons.Right)
            {
                var cm = ContextMenus.GetBattleListContextMenu(battle);
                Program.ToolTip.Visible = false;
                try
                {
                    cm.Show(Parent, e.Location);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("BattleListControl Error displaying tooltip: {0}", ex);
                }
                finally
                {
                    Program.ToolTip.Visible = true;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Battle battle = GetBattle(e.X, e.Y);
            bool openBattleButtonHit = OpenGameButtonHitTest(e.X, e.Y);
            Cursor = battle != null || openBattleButtonHit ? Cursors.Hand : Cursors.Default;
            var cursorPoint = new Point(e.X, e.Y);
            if (cursorPoint == previousLocation) return;
            previousLocation = cursorPoint;

            if (openBattleButtonHit) UpdateTooltip("Host your own battle room\nBest for private games with friends");
            else UpdateTooltip(battle);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            try
            {
                int scaledIconWidth = (int)BattleIcon.Width;
                int scaledIconHeight = (int)BattleIcon.Height;
                int scaledMapCellWidth = (int)BattleIcon.MapCellSize.Width;

                base.OnPaint(pe);
                Graphics g = pe.Graphics;
                g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
                battleIconPositions.Clear();
                int x = 0;
                int y = 0;


                if (view.Any(b => b.Battle.IsQueue && !b.IsInGame)) {
                    PaintDivider(g, ref x, ref y, "Match maker queues");
                    foreach (BattleIcon t in view.Where(b => b.Battle.IsQueue && !b.IsInGame)) {
                        if (x + scaledIconWidth > Width) {
                            x = 0;
                            y += scaledIconHeight;
                        }
                        PainBattle(t, g, ref x, ref y, scaledIconWidth, scaledIconHeight);
                    }

                    x = 0;
                    y += scaledIconHeight;
                }

                PaintDivider(g, ref x, ref y, "Open battles");
                PainOpenBattleButton(g, ref x, ref y, scaledMapCellWidth, scaledIconWidth);

                foreach (BattleIcon t in view.Where(b => !b.Battle.IsQueue && !b.IsInGame))
                {
                    if (x + scaledIconWidth > Width)
                    {
                        x = 0;
                        y += scaledIconHeight;
                    }
                    PainBattle(t, g, ref x, ref y, scaledIconWidth, scaledIconHeight);
                }
                x = 0;
                y += scaledIconHeight;

                PaintDivider(g, ref x, ref y, "Games in progress");

                foreach (BattleIcon t in view.Where(b => b.IsInGame))
                {
                    if (x + scaledIconWidth > Width)
                    {
                        x = 0;
                        y += scaledIconHeight;
                    }
                    PainBattle(t, g, ref x, ref y, scaledIconWidth, scaledIconHeight);
                }


                AutoScrollMinSize = new Size(0, y + scaledIconHeight);
            }
            catch (Exception e)
            {
                Trace.WriteLine("Error in drawing battles: " + e);
            }
        }

        void PaintDivider(Graphics g, ref int x, ref int y, string text)
        {
            y += 8;

            //g.DrawLine(dividerPen, 5, y + 2, Width - 10, y + 2);
            //y += 4;
            g.DrawString(text, dividerFont, dividerFontBrush,new RectangleF(10,y , Width-20,30),new StringFormat()
            {
                LineAlignment = StringAlignment.Center,Alignment = StringAlignment.Center
            }  );
            y += 24;
            //g.DrawLine(dividerPen, 5, y + 2, Width - 10, y + 2);
            y += 4;
        }

        void PainOpenBattleButton(Graphics g, ref int x, ref int y, int scaledMapCellWidth, int scaledIconWidth)
        {   
            g.DrawImage(ZklResources.border,
                x + (int)3,
                y + (int)3,
                (int)70,
                (int)70);
            g.DrawString("Open a new battle.", BattleIcon.TitleFont, BattleIcon.TextBrush, x + scaledMapCellWidth, y + (int)3);
            openBattlePosition = new Point(x, y);
            x += scaledIconWidth;
        }

        void PainBattle(BattleIcon t, Graphics g, ref int x, ref int y, int scaledIconWidth, int scaledIconHeight)
        {
            battleIconPositions[t] = new Point(x, y);
            if (g.VisibleClipBounds.IntersectsWith(new RectangleF(x, y, scaledIconWidth, scaledIconHeight))) g.DrawImageUnscaled(t.Image, x, y);
            x += scaledIconWidth;
        }




        void FilterBattles()
        {
            if (model == null) return;
            view.Clear();
            if (String.IsNullOrEmpty(Program.Conf.BattleFilter)) view = model.ToList();
            else
            {
                string filterText = Program.Conf.BattleFilter.ToUpper();
                string[] orParts = filterOrSplit.Split(filterText);
                view = model.Where(icon => orParts.Any(filterPart => BattleWordFilter(icon.Battle, filterPart.Split(' ')))).ToList();
            }
            IEnumerable<BattleIcon> v = view; // speedup to avoid multiple "toList"
            if (HideEmpty) v = v.Where(bi => bi.Battle.NonSpectatorCount > 0 || bi.Battle.IsQueue);
            if (HideFull) v = v.Where(bi => bi.Battle.NonSpectatorCount < bi.Battle.MaxPlayers);
            if (ShowOfficial) v = v.Where(bi => bi.Battle.IsOfficial());
            if (HidePassworded) v = v.Where(bi => !bi.Battle.IsPassworded);

            view = v.ToList();
        }

        static bool FilterSpecialWordCheck(Battle battle, string word, out bool isMatch)
        {
            // mod shortcut 
            GameInfo knownGame = KnownGames.List.SingleOrDefault(x => x.Shortcut.ToUpper() == word);
            if (knownGame != null)
            {
                isMatch = battle.ModName != null && knownGame.Regex.IsMatch(battle.ModName);
                return true;
            }
            switch (word)
            {
                case "PASSWORD":
                    isMatch = battle.IsPassworded;
                    return true;
                case "INGAME":
                    isMatch = battle.IsInGame;
                    return true;
                case "FULL":
                    isMatch = battle.NonSpectatorCount >= battle.MaxPlayers;
                    return true;
            }

            isMatch = false;
            return false;
        }

        Battle GetBattle(int x, int y)
        {
            x -= AutoScrollPosition.X;
            y -= AutoScrollPosition.Y;
            foreach (var kvp in battleIconPositions)
            {
                BattleIcon battleIcon = kvp.Key;
                Point position = kvp.Value;
                var battleIconRect = new Rectangle(position.X,
                    position.Y,
                    (int)BattleIcon.Width,
                    (int)BattleIcon.Height);
                if (battleIconRect.Contains(x, y) && battleIcon.HitTest(x - position.X, y - position.Y)) return battleIcon.Battle;
            }
            return null;
        }

        bool OpenGameButtonHitTest(int x, int y)
        {
            x -= AutoScrollPosition.X;
            y -= AutoScrollPosition.Y;
            return x > openBattlePosition.X + (int)3 && x < openBattlePosition.X + (int)71 && y > openBattlePosition.Y + (int)3 &&
                   y < openBattlePosition.Y + (int)71;
        }

        void Repaint()
        {
            FilterBattles();
            Sort();
            Invalidate();
        }


        void Sort()
        {
            IOrderedEnumerable<BattleIcon> ret = view.OrderBy(x=>x.Battle.IsInGame);
            ret = ret.OrderByDescending(x => x.Battle.IsSpringieManaged);
            if (sortByPlayers) ret = ret.ThenByDescending(bi => bi.Battle.NonSpectatorCount);
            ret = ret.ThenBy(x => x.Battle.Title);
            view = ret.ToList();
        }

        void UpdateTooltip(object tooltip)
        {
            if (lastTooltip != tooltip)
            {
                lastTooltip = tooltip;
                if (tooltip is Battle) Program.ToolTip.SetBattle(this, ((Battle)tooltip).BattleID);
                else Program.ToolTip.SetText(this, (string)tooltip);
            }
        }

        void HandleBattle(object sender, EventArgs<BattleIcon> e)
        {
            bool invalidate = view.Contains(e.Data);
            FilterBattles();
            Sort();
            Point point = PointToClient(MousePosition);
            Battle battle = GetBattle(point.X, point.Y);
            if (battle != null) UpdateTooltip(battle);
            if (view.Contains(e.Data) || invalidate) Invalidate();
        }
    }

    public interface IRenderElement
    {
        void RenderAtPosition(double x, double y);
        double DimensionX { get; }
        double DimensionY { get; }


    }
}