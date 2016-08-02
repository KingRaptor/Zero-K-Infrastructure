using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using LobbyClient;
using PlasmaDownloader;
using ZeroKLobby.Controls;
using ZeroKLobby.MicroLobby;
using ZkData;

namespace ZeroKLobby.Notifications
{
    public partial class BattleBar : ZklNotifyBar
    {
        readonly TasClient client;
        bool desiredSpectatorState = false;
        string engineVersionNeeded;

        bool isVisible;
        string lastBattleFounder;
        string lastScript;

        readonly Random random = new Random();
        object speech;
        readonly Spring spring;
        bool suppressSpecChange = false;
        readonly Timer timer = new Timer();
        object voice;
        string queueLabelFormatter = "";
        DateTime queueTarget;


        /// <summary>
        /// singleton, dont use, internal for designer
        /// </summary>
        internal BattleBar()
        {
            InitializeComponent();

            Program.ToolTip.SetText(btnLeave, "Leave this battle");

            picoChat.ChatBackgroundColor = TextColor.background; //same color as Program.Conf.BgColor
            picoChat.IRCForeColor = 14; //mirc grey. Unknown use
            picoChat.DefaultTooltip = "Last lines from room chat, click to enter full screen chat";

            gameBox.BackColor = Color.Transparent;
            btnStart.Image = ZklResources.battle.GetResizedWithCache(38, 38);
            btnStart.ImageAlign = ContentAlignment.MiddleCenter;
            btnStart.TextImageRelation = TextImageRelation.ImageAboveText;
            btnStart.Text = "Play";

            btnLeave.Image = Buttons.exit.GetResizedWithCache(38, 38);
            btnLeave.ImageAlign = ContentAlignment.MiddleCenter;
            btnLeave.TextImageRelation = TextImageRelation.ImageAboveText;
            btnLeave.Text = "Leave";

            Program.ToolTip?.SetText(btnStart, "Start battle");
            Program.ToolTip?.SetText(btnLeave, "Quit battle");

            picoChat.Visible = false;


            btnStart.Click += btnStart_Click;
            btnLeave.Click += BtnLeaveClick;

            client = Program.TasClient;
            spring = new Spring(Program.SpringPaths);


            try
            {
                // silly way to create speech and voice engines on runtime - needed due to mono crash
                speech = Activator.CreateInstance(Type.GetType("ZeroKLobby.ChatToSpeech"), spring);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to init VoiceCommands:{0}", ex.Message);
            }

            spring.SpringExited += (s, e) =>
                {
                    client.ChangeMyUserStatus(isInGame: false);

                    if (e.Data)
                    {
                        Program.MainWindow.InvokeFunc(() =>
                            {
                                var defaultButton = MessageBoxDefaultButton.Button2;
                                var icon = MessageBoxIcon.None;
                                if (
                                    MessageBox.Show(this, "Do you want me to set Low details?\n(will effect: lups.cfg and springsettings.cfg)\n\nIf you wish to file a bug report, please include a copy of infolog.txt in your game data folder (accessible through Settings).\nUpload it to a text sharing site such as pastebin.com.",
                                                    "Spring engine has crashed, update your video and audio drivers please!",
                                                    MessageBoxButtons.YesNo,
                                                    icon,
                                                    defaultButton) == DialogResult.Yes)
                                {
                                    Program.Conf.UseSafeMode = true;
                                    Program.EngineConfigurator.Configure(true, 0);
                                }
                            });
                    }
                };

            spring.SpringStarted += (s, e) =>
            {
                Program.MainWindow.SwitchMusicOnOff(false);
                client.ChangeMyUserStatus(isInGame: true);
            };

            client.Rang += (s, e) =>
                {
                    if (e.User == GlobalConst.NightwatchName)
                        //Nightwatch RING is from UserController.cs (website code)
                        MainWindow.Instance.NotifyUser("chat/zkadmin", "New report arrive at zkadmin channel", true, true);
                    else
                    {
                        MainWindow.Instance.NotifyUser("chat/battle", "Someone demands your attention in battle room!", true, true);
                        AutoRespond();
                    }
                };



            client.BattleJoined += (s, e) =>
                {
                    if (!isVisible) ManualBattleStarted();
                    if (IsHostGameRunning()) btnStart.Text = "Rejoin";
                    else btnStart.Text = "Start";


                    //client.ChangeMyUserStatus(false, false);
                    var battle = client.MyBattle;
                    lastBattleFounder = battle.Founder.Name;

                    if (battle.Founder.Name.StartsWith("PlanetWars") || battle.Founder.Name.StartsWith("Zk")) ChangeDesiredSpectatorState(false); // TODO pw unpsec hack, remove later

                    if (battle.IsQueue)
                    {
                        //Title = string.Format("Joined {0} Quick Match Queue", battle.QueueName);
                        //TitleTooltip = "Please await people, game will start automatically";
                        lbQueue.Visible = true;
                        radioPlay.Visible = false;
                        radioSpec.Visible = false;
                        btnStart.Visible = false;
                    }
                    else
                    {
                        //Title = string.Format("Joined battle room hosted by {0}", battle.Founder.Name);
                        //TitleTooltip = "Use button on the left side to start a game";
                        lbQueue.Visible = false;
                        radioPlay.Visible = true;
                        radioSpec.Visible = true;
                        btnStart.Visible = true;
                    }

                    Program.Downloader.GetResource(DownloadType.MAP, battle.MapName);
                    Program.Downloader.GetResource(DownloadType.MOD, battle.ModName);
                    engineVersionNeeded = battle.EngineVersion;
                    if (engineVersionNeeded != Program.SpringPaths.SpringVersion) Program.Downloader.GetAndSwitchEngine(engineVersionNeeded);
                    else engineVersionNeeded = null;

                    if (gameBox.Image != null) gameBox.Image.Dispose();
                    CreateBattleIcon(Program.BattleIconManager.GetBattleIcon(battle.BattleID));

                    RefreshTooltip();

                    var team = battle.GetFreeTeamID(client.UserName);

                    client.ChangeMyBattleStatus(desiredSpectatorState, 0, team);
                };


            client.MyBattleMapChanged += (s, e) =>
                {
                    if (client.MyBattle != null && !Program.SpringScanner.HasResource(client.MyBattle.MapName))
                    {
                        Program.Downloader.GetResource(DownloadType.MAP, client.MyBattle.MapName);
                    }
                    RefreshTooltip();
                };

            client.MyBattleHostExited += (s, e) => { btnStart.Text = "Start"; };

            client.MyBattleStarted += (s, e) =>
                {
                    try
                    {
                        if (client.MyBattle.Users[client.UserName].ScriptPassword == null) btnStart.Text = "Watch";
                        else btnStart.Text = "Rejoin";

                        if (Utils.VerifySpringInstalled())
                        {
                            if (spring.IsRunning) spring.ExitGame();
                            lastScript = spring.ConnectGame(client.MyBattle.Ip, client.MyBattle.HostPort, client.UserName,
                                client.MyBattle.Users[client.UserName].ScriptPassword); //use MT tag when in spectator slot
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Error starting spring: " + ex.Message);
                    }
                    RefreshTooltip();
                };

            client.BattleMyUserStatusChanged += (s, e) =>
                {
                    if (client.MyBattleStatus != null)
                    {
                        btnStart.Enabled = true;

                        if (client.MyBattleStatus.IsSpectator && radioPlay.Checked) ChangeGuiSpectatorWithoutEvent(false); // i was spectated
                        if (!client.MyBattleStatus.IsSpectator && radioSpec.Checked) ChangeGuiSpectatorWithoutEvent(true); //i was unspectated
                    }
                };

            client.BattleClosed += (s, e) =>
                {
                    btnStart.Text = "Start";
                    if (gameBox.Image != null) gameBox.Image.Dispose();
                    gameBox.Image = null;
                    RefreshTooltip();
                    Stop();
                };

            client.MyBattleRemoved += (s, e) =>
                {
                    var t = new Timer();
                    var tryCount = 0;
                    t.Interval = 1000;
                    t.Tick += (s2, e2) =>
                        {
                            tryCount++;
                            if (tryCount > 15)
                            {
                                t.Stop();
                                t.Dispose();
                            }
                            else if (client.IsLoggedIn && client.MyBattle == null)
                            {
                                var bat = client.ExistingBattles.Values.FirstOrDefault(x => x.Founder.Name == lastBattleFounder && !x.IsPassworded);
                                if (bat != null)
                                {
                                    ActionHandler.JoinBattle(bat.BattleID, null);
                                    t.Stop();
                                    t.Dispose();
                                }
                            }
                        };
                    t.Start();
                };

            client.ConnectionLost += (s, e) =>
                {
                    if (gameBox.Image != null) gameBox.Image.Dispose();
                    gameBox.Image = null;
                    RefreshTooltip();
                    Stop();
                };


            // process special queue message to display in label
            client.Said += (s, e) =>
            {
                if (e.Place == SayPlace.Battle && client.MyBattle != null && client.MyBattle.Founder.Name == e.UserName && e.Text.StartsWith("Queue"))
                {
                    var t = e.Text.Substring(6);
                    queueLabelFormatter = Regex.Replace(t,
                        "([0-9]+)s",
                        m =>
                        {
                            var queueSeconds = int.Parse(m.Groups[1].Value);
                            queueTarget = DateTime.Now.AddSeconds(queueSeconds);
                            return "{0}s";
                        });
                    lbQueue.Text = string.Format(queueLabelFormatter, Math.Round(queueTarget.Subtract(DateTime.Now).TotalSeconds));
                }
            };


            timer.Tick += (s, e) =>
                {
                    if (client.IsLoggedIn)
                    {
                        if (WindowsApi.IdleTime.TotalMinutes > Program.Conf.IdleTime)
                        {
                            if (!client.MyUser.IsAway) client.ChangeMyUserStatus(isAway: true);
                        }
                        else
                        {
                            if (client.MyUser.IsAway) client.ChangeMyUserStatus(isAway: false);
                        }
                        CheckMyBattle();
                    }
                    if (client.MyBattle != null && client.MyBattle.IsQueue)
                    {
                        lbQueue.Text = string.Format(queueLabelFormatter, Math.Round(queueTarget.Subtract(DateTime.Now).TotalSeconds));
                    }
                };


            Program.MainWindow.navigationControl.PageChanged += s =>
            {
                if (s != "chat/battle")
                {
                    picoChat.Visible = true;
                }
                else picoChat.Visible = false;
            };

            timer.Interval = 1000;
            timer.Start();

            Program.BattleIconManager.BattleChanged += BattleIconManager_BattleChanged;

            //picoChat.Font = new Font(Program.Conf.ChatFont.FontFamily, Program.Conf.ChatFont.Size*0.8f);
            picoChat.ShowHistory = false;
            picoChat.ShowJoinLeave = false;
            //picoChat.HideScroll = true;

            BattleChatControl.BattleLine += (s, e) => picoChat.AddLine(e.Data);

            picoChat.MouseClick += (s, e) => NavigationControl.Instance.Path = "chat/battle";
        }

        private void BtnLeaveClick(object sender, EventArgs e)
        {
            ActionHandler.StopBattle();
        }


        /// <summary>
        /// Changes user's desired spectator state of battle
        /// </summary>
        /// <param name="state">new desired state</param>
        /// <returns>true if change allowed</returns>
        public bool ChangeDesiredSpectatorState(bool state)
        {
            desiredSpectatorState = state;
            ChangeGuiSpectatorWithoutEvent(state);
            return true;
        }

        public static bool DownloadFailed(string name)
        {
            if (!Program.SpringScanner.HasResource(name))
            {
                var down = Program.Downloader.Downloads.FirstOrDefault(x => x.Name == name);
                if (down == null || down.IsComplete == false || down.IsAborted) return true;
            }
            return false;
        }

        public void Rejoin()
        {
            if (Utils.VerifySpringInstalled())
            {
                if (spring.IsRunning) spring.ExitGame();
                if (client.MyBattle != null) spring.ConnectGame(client.MyBattle.Ip, client.MyBattle.HostPort, client.UserName, client.MyBattle.Users[client.UserName].ScriptPassword);
                else spring.RunLocalScriptGame(lastScript); //rejoining a running game from outside the battleroom???
            }
        }

        public void StartManualBattle(int battleID, string password)
        {
            Trace.TraceInformation("Joining battle {0}", battleID);
            var tas = Program.TasClient;
            if (tas.MyBattle != null)
            {
                Battle battle;
                if (tas.ExistingBattles.TryGetValue(battleID, out battle)) tas.Say(SayPlace.Battle, "", string.Format("Going to {0} zk://@join_player:{1}", battle.Title, battle.Founder.Name), true);
                tas.LeaveBattle();
            }
            if (!string.IsNullOrEmpty(password)) Program.TasClient.JoinBattle(battleID, password);
            else Program.TasClient.JoinBattle(battleID);
        }


        public static bool StillDownloading(string name)
        {
            return !Program.SpringScanner.HasResource(name) && !DownloadFailed(name);
        }

        public void Stop()
        {
            if (isVisible)
            {
                Trace.TraceInformation("Closing current battle");
                isVisible = false;
                client.LeaveBattle();

                Program.NotifySection.RemoveBar(this);
                NavigationControl.Instance.Path = "battles";
            }
        }

        void AutoRespond()
        {
            if (client.MyBattle != null && client.MyBattleStatus != null)
            {
                var moddl = Program.Downloader.Downloads.FirstOrDefault(x => x.Name == client.MyBattle.ModName);
                var mapdl = Program.Downloader.Downloads.FirstOrDefault(x => x.Name == client.MyBattle.MapName);
                if (moddl != null && moddl.IsComplete != true)
                {
                    client.Say(SayPlace.Battle,
                               "",
                               string.Format("Mod download progress: {0}%, eta: {1}", Math.Round(moddl.TotalProgress), moddl.TimeRemaining),
                               true);
                }
                if (mapdl != null && mapdl.IsComplete != true)
                {
                    client.Say(SayPlace.Battle,
                               "",
                               string.Format("Map download progress: {0}%, eta: {1}", Math.Round(mapdl.TotalProgress), mapdl.TimeRemaining),
                               true);
                }
            }
        }

        void ChangeGuiSpectatorWithoutEvent(bool newState)
        {
            suppressSpecChange = true;
            if (newState) radioPlay.Checked = true;
            else radioSpec.Checked = true;
            suppressSpecChange = false;
        }


        void CheckMyBattle()
        {
            var battle = client.MyBattle;
            var currentStatus = client.MyBattleStatus;
            if (battle == null || currentStatus == null) return;


            // fix my id
            int? team = null;

            if (battle.Users.Values.Count(x => !x.IsSpectator && x.TeamNumber == currentStatus.TeamNumber) > 1)
            {
                team = battle.GetFreeTeamID(client.UserName);
            }

            bool spec = radioSpec.Checked;
            if ((team.HasValue && team != currentStatus.TeamNumber) ||
                (currentStatus.IsSpectator != spec))
            {
                client.ChangeMyBattleStatus(spec, null, team);
            }
        }


        bool IsHostGameRunning()
        {
            if (client != null)
            {
                var bat = client.MyBattle;
                if (bat != null) return bat.IsInGame;
            }
            return false;
        }


        void ManualBattleStarted()
        {
            if (isVisible) Stop();
            isVisible = true;
            Program.NotifySection.AddBar(this);
        }


        void RefreshTooltip()
        {
            var bat = client.MyBattle;
            if (bat != null) Program.ToolTip.SetBattle(gameBox, bat.BattleID);
            else Program.ToolTip.SetText(gameBox, null);
        }



        public void btnStart_Click(object sender, EventArgs e)
        {
            NavigationControl.Instance.Path = "chat/battle";
            if (IsHostGameRunning()) Rejoin();
            else client.Say(SayPlace.Battle, "", "!start", false);
        }

        void BattleIconManager_BattleChanged(object sender, EventArgs<BattleIcon> e)
        {
            if (e.Data.Battle == Program.TasClient.MyBattle)
            {
                CreateBattleIcon(e.Data);
            }
        }

        private void CreateBattleIcon(BattleIcon e)
        {
            if (gameBox.Image == null) gameBox.Image = e.GenerateImage(true);
        }


        private void radioPlay_CheckedChanged(object sender, EventArgs e)
        {
            if (!suppressSpecChange)
            {
                desiredSpectatorState = !radioPlay.Checked;
                client.ChangeMyBattleStatus(spectate: desiredSpectatorState);
            }
        }

        private void radioSpec_CheckedChanged(object sender, EventArgs e)
        {
            //if (!suppressSpecChange) //NOTE: when "radioPlay" is checked/un-checked it already change status once.
            //{
            //    desiredSpectatorState = radioSpec.Checked;
            //    client.ChangeMyBattleStatus(spectate: desiredSpectatorState);
            //}
        }

    }


    class SideItem
    {
        public Image Image;
        public string Side;

        public SideItem(string side, byte[] image)
        {
            Side = side;
            if (image != null && image.Length > 0) Image = Image.FromStream(new MemoryStream(image));
            else Image = null;
        }

        public override string ToString()
        {
            return Side;
        }
    }
}
