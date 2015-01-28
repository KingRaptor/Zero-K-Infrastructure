#region using

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using PlasmaShared.LobbyMessages;
using ZkData;

#endregion

namespace LobbyClient
{

    public class OldNewPair<T>
    {
        public T Old;
        public T New;

        public OldNewPair(T old, T @new)
        {
            Old = old;
            New = @new;
        }
    }

    public class ChannelUserInfo
    {
        public Channel Channel;
        public List<User> Users;
    }

    public class ChannelUserRemovedInfo
    {
        public Channel Channel;
        public User User;
        public string Reason;
    }

    
    public class TasClient:Connection
    {
        public const int MaxAlliances = 16;
        public const int MaxTeams = 16;

        public ProtocolExtension Extensions { get; private set; }

        public delegate void Invoker();

        public delegate void Invoker<TArg>(TArg arg);


        readonly string appName = "UnknownClient";
        Dictionary<int, Battle> existingBattles = new Dictionary<int, Battle>();
        Dictionary<string, ExistingChannel> existingChannels = new Dictionary<string, ExistingChannel>();
        Dictionary<string, User> existingUsers = new Dictionary<string, User>();
        readonly bool forcedLocalIP = false;
        readonly Invoker<Invoker> guiThreadInvoker;
        bool isLoggedIn;
        Dictionary<string, Channel> joinedChannels = new Dictionary<string, Channel>();
        int lastSpectatorCount;
        int lastUdpSourcePort;
        int lastUserBattleStatus;
        int lastUserStatus;
        readonly string localIp;
        bool lockToChangeTo;
        int mapChecksumToChangeTo;
        string mapToChangeTo;
        readonly Timer minuteTimer;

        int pingInterval = 30; // how often to ping server (in seconds)
        readonly Timer pingTimer;
        readonly Random random = new Random();
        public string serverHost { get; private set; }

        int serverPort;
        int serverUdpHolePunchingPort;
        string serverVersion;
        bool startingAfterUdpPunch;
        readonly Timer udpPunchingTimer = new Timer(400);

        public bool ConnectionFailed { get; private set; }

        public Dictionary<int, Battle> ExistingBattles { get { return existingBattles; } set { existingBattles = value; } }

        public Dictionary<string, User> ExistingUsers { get { return existingUsers; } set { existingUsers = value; } }

        
        public override async Task OnConnected()
        {
            MyBattle = null;
            MyBattleID = 0;
            ConnectionFailed = false;
            ExistingUsers = new Dictionary<string, User>();
            existingChannels = new Dictionary<string, ExistingChannel>();
            joinedChannels = new Dictionary<string, Channel>();
            existingBattles = new Dictionary<int, Battle>();
            isLoggedIn = false;
        }

        public override async Task OnConnectionClosed(bool wasRequested)
        {
            ConnectionFailed = !wasRequested;

            ExistingUsers = new Dictionary<string, User>();
            existingChannels = new Dictionary<string, ExistingChannel>();
            joinedChannels = new Dictionary<string, Channel>();
            existingBattles = new Dictionary<int, Battle>();
            MyBattle = null;
            MyBattleID = 0;
            isLoggedIn = false;
            ConnectionLost(this, new TasEventArgs(string.Format("Connection {0}", wasRequested ? "closed on user request" : "disconnected")));
        }

        public override async Task OnLineReceived(string line)
        {
            dynamic obj = CommandJsonSerializer.DeserializeLine(line);
            Input(this, line);
            await Process(obj);
        }

        public async Task SendCommand<T>(T data)
        {
            var line = CommandJsonSerializer.SerializeToLine(data);
            Output(this, line.TrimEnd('\n'));
            await SendData(Encoding.GetBytes(line));
        }

        static CommandJsonSerializer CommandJsonSerializer = new CommandJsonSerializer();

        public bool IsLoggedIn { get { return isLoggedIn; } }

        public Dictionary<string, Channel> JoinedChannels { get { return joinedChannels; } }
        public int MessageID { get; private set; }

        public Battle MyBattle { get; protected set; }
        public int MyBattleID { get; private set; }


        public UserBattleStatus MyBattleStatus
        {
            get
            {
                if (MyBattle != null) return MyBattle.Users.SingleOrDefault(x => x.Name == UserName);
                else return null;
            }
        }

        public User MyUser
        {
            get
            {
                User us;
                ExistingUsers.TryGetValue(UserName, out us);
                return us;
            }
        }


        public int PingInterval
        {
            get { return pingInterval; }
            set
            {
                pingInterval = value;
                pingTimer.Interval = pingInterval*1000;
            }
        }
        public string ServerSpringVersion { get; private set; }

        public string UserName { get; private set; }
        public string UserPassword { get; private set; }

        public event EventHandler<EventArgs<BotBattleStatus>> BattleBotAdded = delegate { };
        public event EventHandler<EventArgs<BotBattleStatus>> BattleBotRemoved = delegate { };
        public event EventHandler<EventArgs<BotBattleStatus>> BattleBotUpdated = delegate { };
        public event EventHandler<EventArgs<Battle>> BattleClosed = delegate { };
        public event EventHandler<TasEventArgs> BattleDetailsChanged = delegate { };
        public event EventHandler<EventArgs<Battle>> BattleEnded = delegate { }; // raised just after the battle is removed from the battle list
        public event EventHandler<EventArgs<Battle>> BattleEnding = delegate { }; // raised just before the battle is removed from the battle list
        public event EventHandler BattleForceQuit = delegate { }; // i was kicked from a battle (sent after LEFTBATTLE)
        public event EventHandler<EventArgs<Battle>> BattleFound = delegate { };
        public event EventHandler<BattleInfoEventArgs> BattleInfoChanged = delegate { };
        public event EventHandler<EventArgs<Battle>> BattleJoined = delegate { };
        public event EventHandler<BattleInfoEventArgs> BattleLockChanged = delegate { };
        public event EventHandler<BattleInfoEventArgs> BattleMapChanged = delegate { };
        public event EventHandler<TasEventArgs> BattleMyUserStatusChanged = delegate { };
        public event EventHandler<TasEventArgs> BattleOpenFailed = delegate { };
        public event EventHandler<TasEventArgs> BattleOpened = delegate { };
        public event EventHandler<EventArgs<User>> BattleStarted = delegate { };
        public event EventHandler<TasEventArgs> BattleUserIpRecieved = delegate { };
        public event EventHandler<BattleUserEventArgs> BattleUserJoined = delegate { };
        public event EventHandler<BattleUserEventArgs> BattleUserLeft = delegate { };
        public event EventHandler<TasEventArgs> BattleUserStatusChanged = delegate { };
        public event EventHandler<TasEventArgs> ChannelForceLeave = delegate { }; // i was kicked from a channel
        public event EventHandler<JoinChannelResponse> ChannelJoinFailed = delegate { };
        public event EventHandler<Channel> ChannelJoined = delegate { };
        public event EventHandler<CancelEventArgs<string>> ChannelLeaving = delegate { }; // raised before attempting to leave a channel
        public event EventHandler<TasEventArgs> ChannelLeft = delegate { };
        public event EventHandler<TasEventArgs> ChannelTopicChanged = delegate { };
        public event EventHandler<ChannelUserInfo> ChannelUserAdded = delegate { };
        public event EventHandler<ChannelUserRemovedInfo> ChannelUserRemoved = delegate { };
        public event EventHandler<Welcome> Connected = delegate { };
        public event EventHandler<TasEventArgs> ConnectionLost = delegate { };
        public event EventHandler<CancelEventArgs<string>> FilterBattleByMod;
        public event EventHandler<EventArgs> HourChime = delegate { };
        public event EventHandler<string> Input = delegate { };
        public event EventHandler<TasEventArgs> JoinBattleFailed = delegate { };
        public event EventHandler<KickedFromServerEventArgs> KickedFromServer = delegate { };
        public event EventHandler<TasEventArgs> LoginAccepted = delegate { };
        public event EventHandler<LoginResponse> LoginDenied = delegate { };
        public event EventHandler<EventArgs<Battle>> MyBattleEnded = delegate { }; // raised just after the battle is removed from the battle list
        public event EventHandler<TasEventArgs> MyBattleHostExited = delegate { };
        public event EventHandler<BattleInfoEventArgs> MyBattleMapChanged = delegate { };
        public event EventHandler<TasEventArgs> MyBattleStarted = delegate { };
        public event EventHandler<string> Output = delegate { }; // outgoing command and arguments
        public event EventHandler<CancelEventArgs<Channel>> PreviewChannelJoined = delegate { };
        public event EventHandler<CancelEventArgs<TasSayEventArgs>> PreviewSaid = delegate { };
        public event EventHandler<EventArgs<string>> Rang = delegate { };
        public event EventHandler<TasEventArgs> RegistrationAccepted = delegate { };
        public event EventHandler<RegisterResponse> RegistrationDenied = delegate { };
        public event EventHandler RequestBattleStatus = delegate { };
        public event EventHandler<TasSayEventArgs> Said = delegate { }; // this is fired when any kind of say message is recieved
        public event EventHandler<SayingEventArgs> Saying = delegate { }; // this client is trying to say somethign
        public event EventHandler<TasEventArgs> StartRectAdded = delegate { };
        public event EventHandler<TasEventArgs> StartRectRemoved = delegate { };
        public event EventHandler<TasEventArgs> TestLoginAccepted = delegate { };
        public event EventHandler<TasEventArgs> TestLoginDenied = delegate { };
        public event EventHandler<User> UserAdded = delegate { };
        public event EventHandler<TasEventArgs> UserRemoved = delegate { };
        public event EventHandler<OldNewPair<User>> UserStatusChanged = delegate { };
        public event EventHandler<EventArgs<User>> UserExtensionsChanged = delegate { };
        public event EventHandler<EventArgs<User>> MyExtensionsChanged = delegate { };
        public event EventHandler<UserLobbyVersionEventArgs> UserLobbyVersionRecieved = delegate { };
        public event EventHandler<UserIPEventArgs> UserIPRecieved = delegate { };
        public event EventHandler<UserIDEventArgs> UserIDRecieved = delegate { }; 

 

        public TasClient(Invoker<Invoker> guiThreadInvoker, string appName, Login.ClientTypes? clientTypes = null, string ipOverride = null)
        {
            if (clientTypes.HasValue) this.clientType = clientTypes.Value;
            this.appName = appName;
            this.guiThreadInvoker = guiThreadInvoker;


            if (!string.IsNullOrEmpty(ipOverride))
            {
                localIp = ipOverride;
                forcedLocalIP = true;
            }
            else
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName());

                localIp = addresses[0].ToString();
                foreach (var adr in addresses)
                {
                    if (adr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localIp = adr.ToString();
                        break;
                    }
                }
            }

            Extensions = new ProtocolExtension(this, (user, data) => {
                                                                         User u;
                                                                         if (ExistingUsers.TryGetValue(user, out u))
                                                                         {
                                                                             UserExtensionsChanged(this, new EventArgs<User>(u));
                                                                         }


            });

            pingTimer = new Timer(pingInterval*1000) { AutoReset = true };
            pingTimer.Elapsed += OnPingTimer;
            pingTimer.Start();

            minuteTimer = new Timer(60000) { AutoReset = true };
            minuteTimer.Elapsed += (s, e) =>
                {
                    if (DateTime.Now.Minute == 0)
                        if (guiThreadInvoker != null) guiThreadInvoker(() => HourChime(this, new EventArgs()));
                        else HourChime(this, new EventArgs());
                };
            minuteTimer.Start();

            udpPunchingTimer.Elapsed += udpPunchingTimer_Elapsed;
            udpPunchingTimer.AutoReset = true;
        }


        public void AddBattleRectangle(int allyno, BattleRect rect)
        {
            {
                if (allyno < Spring.MaxAllies && allyno >= 0)
                {
                    RemoveBattleRectangle(allyno);
                    MyBattle.Rectangles.Add(allyno, rect);
//                    con.SendCommand("ADDSTARTRECT", allyno, rect.Left, rect.Top, rect.Right, rect.Bottom);
                }
            }
        }

        public void AddBot(string name, UserBattleStatus status, int teamColor, string aiDll)
        {
            if (name.Contains(" ")) throw new TasClientException("Bot name must not contain spaces. " + name);
            //con.SendCommand("ADDBOT", name, status.ToInt(), teamColor, aiDll);
        }

        public void ChangeLock(bool lck)
        {
            if (lck != lockToChangeTo) UpdateBattleInfo(lck, mapToChangeTo, mapChecksumToChangeTo);
        }


        public void ChangeMap(string name, int checksum)
        {
            {
                mapToChangeTo = name;
                mapChecksumToChangeTo = checksum;
                UpdateBattleInfo(lockToChangeTo, name, checksum);
            }
        }

        public void ChangeMyBattleStatus(bool? spectate = null,
                                         bool? ready = null,
                                         SyncStatuses? syncStatus = null,
                                         int? side = null,
                                         int? ally = null,
                                         int? team = null)
        {
            var ubs = MyBattleStatus;
            if (ubs != null)
            {
                var clone = (UserBattleStatus)ubs.Clone();
                clone.SetFrom(lastUserBattleStatus, ubs.TeamColor);
                if (spectate.HasValue) clone.IsSpectator = spectate.Value;
                if (ready.HasValue) clone.IsReady = ready.Value;
                if (syncStatus.HasValue) clone.SyncStatus = syncStatus.Value;
                if (side.HasValue) clone.Side = side.Value;
                if (ally.HasValue) clone.AllyNumber = ally.Value;
                if (team.HasValue) clone.TeamNumber = team.Value;
                if (clone.ToInt() != lastUserBattleStatus)
                {
                    SendMyBattleStatus(clone.ToInt(), clone.TeamColor);
                    lastUserBattleStatus = clone.ToInt();
                }
            }
        }


        public void ChangeMyUserStatus(bool? isAway = null, bool? isInGame = null)
        {
            User u;
            if (MyUser != null) u = MyUser.Clone();
            else u = new User();
            if (isAway != null) u.IsAway = isAway.Value;
            if (isInGame != null) u.IsInGame = isInGame.Value;
//            if (MyUser == null || lastUserStatus != u.ToInt())
  //          {
                //con.SendCommand("MYSTATUS", u.ToInt());
      //          lastUserStatus = u.ToInt();
    //        }
        }


        public void Connect(string host, int port)
        {
            serverHost = host;
            serverPort = port;
            Connect(host, port, forcedLocalIP ? localIp : null);
        }

        public static DateTime ConvertMilisecondTime(string arg)
        {
            return (new DateTime(1970, 1, 1, 0, 0, 0)).AddMilliseconds(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
        }


        public void RequestDisconnect()
        {
            RequestClose();
        }



        public void ForceAlly(string username, int ally)
        {
            //con.SendCommand("FORCEALLYNO", username, ally);
        }

        public void ForceColor(string username, int color)
        {
            //con.SendCommand("FORCETEAMCOLOR", username, color);
        }

        public void ForceSpectator(string username)
        {
            //con.SendCommand("FORCESPECTATORMODE", username);
        }

        public void ForceTeam(string username, int team)
        {
            //con.SendCommand("FORCETEAMNO", username, team);
        }

        public void GameSaid(string username, string text)
        {
            InvokeSaid(new TasSayEventArgs(SayPlace.Game, "", username, text, false));
        }


        public bool GetExistingUser(string name, out User u)
        {
            return ExistingUsers.TryGetValue(name, out u);
        }

        public User GetUserCaseInsensitive(string userName)
        {
            return ExistingUsers.Values.FirstOrDefault(u => String.Equals(u.Name, userName, StringComparison.InvariantCultureIgnoreCase));
        }


        public void JoinBattle(int battleID, string password = "*")
        {
            if (string.IsNullOrEmpty(password)) password = "*";
            //con.SendCommand("JOINBATTLE", battleID, password, random.Next());
        }


        public Task JoinChannel(string channelName, string key=null)
        {
            return SendCommand(new JoinChannel() { Name = channelName, Password = key });
        }

        public void Kick(string username)
        {
            //con.SendCommand("KICKFROMBATTLE", username);
        }

        public void AdminKickFromLobby(string username,string reason)
        {
            //con.SendCommand("KICKUSER", username,reason);
        }

        public void AdminSetTopic(string channel, string topic) {
            Say(SayPlace.User, "ChanServ", string.Format("!topic #{0} {1}", channel, topic.Replace("\n","\\n")),false);
        }

        public void AdminSetChannelPassword(string channel, string password) {
            if (string.IsNullOrEmpty(password) || password=="*") {
                Say(SayPlace.User, "ChanServ",string.Format("!lock #{0} {1}", channel, password),false);    
            } else {
                Say(SayPlace.User, "ChanServ", string.Format("!unlock #{0}", channel), false);
            }
        }


        public void LeaveBattle()
        {
            if (MyBattle != null)
            {
                //con.SendCommand("LEAVEBATTLE");
                var bat = MyBattle;
                bat.ScriptTags.Clear();
                MyBattle = null;
                MyBattleID = 0;
                BattleClosed(this, new EventArgs<Battle>(bat));
            }
        }


        public void LeaveChannel(string channelName)
        {
            var args = new CancelEventArgs<string>(channelName);
            ChannelLeaving(this, args);
            if (args.Cancel) return;
            //con.SendCommand("LEAVE", channelName);
            JoinedChannels.Remove(channelName);
            ChannelLeft(this, new TasEventArgs(channelName));
        }


        public static string GetMyUserID() {
            var nics = NetworkInterface.GetAllNetworkInterfaces().Where(x=> !String.IsNullOrWhiteSpace(x.GetPhysicalAddress().ToString())
                && x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

            var wantedNic = nics.FirstOrDefault();

            if (wantedNic != null)
            {
                return Crc.Crc32(wantedNic.GetPhysicalAddress().GetAddressBytes()).ToString();
            }
            return "0";
        }


        public Task Login(string userName, string password)
        {
            UserName = userName;
            UserPassword = password;
            return SendCommand(new Login() { Name = userName, PasswordHash = Utils.HashLobbyPassword(password), ClientType = clientType});
        }


        Login.ClientTypes clientType = LobbyClient.Login.ClientTypes.ZeroKLobby | (Environment.OSVersion.Platform == PlatformID.Unix ? LobbyClient.Login.ClientTypes.Linux : 0);

        public void OpenBattle(Battle nbattle)
        {
            LeaveBattle(); // leave current battle
            MyBattleID = -1;

            MyBattle = nbattle;

            var objList = new List<object>
                          {
                              0,
                              // type normal
                              (int)MyBattle.Nat,
                              MyBattle.Password,
                              MyBattle.HostPort,
                              MyBattle.MaxPlayers,
                              MyBattle.ModHash,
                              MyBattle.MapHash,
                              MyBattle.EngineName,
                              '\t' +MyBattle.EngineVersion,
                              '\t' +MyBattle.MapName,
                              '\t' + MyBattle.Title,
                              '\t' + MyBattle.ModName
                          };
            MyBattle.Founder = ExistingUsers[UserName];
            MyBattle.Ip = localIp;

            //battle.Details.AddToParamList(objList);
            mapToChangeTo = MyBattle.MapName;
            mapChecksumToChangeTo = MyBattle.MapHash ?? 0;
            lockToChangeTo = false;

            //con.SendCommand("OPENBATTLE", objList.ToArray());

            lastSpectatorCount = 0;

            // send predefined starting rectangles
            //foreach (var v in MyBattle.Rectangles) con.SendCommand("ADDSTARTRECT", v.Key, v.Value.Left, v.Value.Top, v.Value.Right, v.Value.Bottom);
        }




        public Task Register(string username, string password)
        {
            return SendCommand(new Register() { Name = username, PasswordHash = Utils.HashLobbyPassword(password) });
        }

        public void RemoveBattleRectangle(int allyno)
        {
            if (MyBattle.Rectangles.ContainsKey(allyno))
            {
                MyBattle.Rectangles.Remove(allyno);
                //con.SendCommand("REMOVESTARTRECT", allyno);
            }
        }

        public void RemoveBot(string name)
        {
            //con.SendCommand("REMOVEBOT", name);
        }


        public void RenameAccount(string newName)
        {
            //con.SendCommand("RENAMEACCOUNT", newName);
        }

        public void Ring(string username)
        {
            //con.SendCommand("RING", username);
        }


        public void ForceJoinBattle(string name, string battleHostName, string password = null) {
            var battle = ExistingBattles.Values.FirstOrDefault(x => x.Founder.Name == battleHostName);
            if (battle != null) ForceJoinBattle(name, battle.BattleID, password);
        }

        public void ForceJoinBattle(string name, int battleID, string password = null) {
            User user;
            Battle battle;
            existingUsers.TryGetValue(name, out user);
            existingBattles.TryGetValue(battleID, out battle);
            //if (user != null && battle != null) con.SendCommand("FORCEJOINBATTLE", name, battleID, password);
        }

        public void ForceJoinChannel(string user, string channel, string password= null) {
//            con.SendCommand(string.Format("FORCEJOIN {0} {1} {2}", user, channel, password));
        }

        public void ForceLeaveChannel(string user, string channel, string reason = null)
        {
//            con.SendCommand(string.Format("FORCELEAVECHANNEL {0} {1} {2}", channel, user, reason));
        }


        /// <summary>
        /// Say something through chat system
        /// </summary>
        /// <param Name="place">Pick user (private message) channel or battle</param>
        /// <param Name="channel">Channel or User Name</param>
        /// <param Name="inputtext">chat text</param>
        /// <param Name="isEmote">is message emote? (channel or battle only)</param>
        /// <param Name="linePrefix">text to be inserted in front of each line (example: "!pm xyz")</param>
        public async Task Say(SayPlace place, string channel, string inputtext, bool isEmote, string linePrefix = "")
        {
            if (String.IsNullOrEmpty(inputtext)) return;
            var lines = inputtext.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var text in lines)
            {
                if (String.IsNullOrEmpty(text)) continue;

                var sentText = linePrefix + text;

                var args = new SayingEventArgs(place, channel, sentText, isEmote);
                Saying(this, args);
                if (args.Cancel) continue;

                if (args.SayPlace == SayPlace.Channel && !JoinedChannels.ContainsKey(args.Channel)) {
                    await JoinChannel(args.Channel);
                }

                var say = new Say() { Target = args.Channel, Place = args.SayPlace, Text = args.Text, IsEmote = args.IsEmote };

                await SendCommand(say);
            }
        }


        public void SendMyBattleStatus(UserBattleStatus status)
        {
            //con.SendCommand("MYBATTLESTATUS", status.ToInt(), status.TeamColor);
        }

        public void SendMyBattleStatus(int battleStatus, int color)
        {
            //con.SendCommand("MYBATTLESTATUS", battleStatus, color);
        }

        public void SendRaw(string text)
        {
            //con.SendCommand("", text);
        }

        public void SetHideCountry(string name, bool state) {
            //con.SendCommand("HIDECOUNTRYSET", name, state ? "1" : "0");
        }

        public void SetScriptTag(string data)
        {
            //con.SendCommand("SETSCRIPTTAGS", data);
        }

        public void SetBotMode(string name, bool botMode) {
            //con.SendCommand("SETBOTMODE",name, botMode?"1":"0");
        }


        /// <summary>
        /// Starts game and automatically does hole punching if necessary
        /// </summary>
        public void StartGame()
        {
            if (MyBattle.Nat == Battle.NatMode.HolePunching)
            {
                startingAfterUdpPunch = true;
                SendUdpPacket(0, serverHost, serverUdpHolePunchingPort);
                udpPunchingTimer.Start();
            }
            else ChangeMyUserStatus(false, true);
        }

        public void UpdateBattleDetails(BattleDetails bd)
        {
            var objList = new List<object>();
            //con.SendCommand("SETSCRIPTTAGS", bd.GetParamList());
        }

        public void UpdateBot(string name, UserBattleStatus battleStatus, int teamColor)
        {
            //con.SendCommand("UPDATEBOT", name, battleStatus.ToInt(), teamColor);
        }

       
        /// <summary>
        /// Primary method - processes commands from server
        /// </summary>
        /// <param Name="command">command Name</param>
        /// <param Name="args">command arguments</param>
        async Task ProcessCommand(object obj)
        {
            string command = "";
            string[] args = new string[]{};

            // is this really needed for the whole thread? it screws with date formatting
            // Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                if (command.StartsWith("#"))
                {
                    MessageID = int.Parse(command.Substring(1));
                    command = args[0];
                    args = Utils.ShiftArray(args, -1);
                }

                
                switch (command)
                {


                    case "ADDUSER": // new user joined ta server
                        OnAddUser(args);
                        break;

                    case "FORCELEAVECHANNEL":
                        OnForceLeaveChannel(args);
                        break;

                    case "FORCEQUITBATTLE":
                        BattleForceQuit(this, EventArgs.Empty);
                        break;

                    case "KICKUSER":
                        OnKickUser(args);
                        break;

                    case "REMOVEUSER": // user left ta server
                        OnRemoveUser(args);
                        break;


                    case "SERVERMSGBOX": // server messagebox
                        InvokeSaid(new TasSayEventArgs(SayPlace.MessageBox,
                                                       "",
                                                       "",
                                                       Utils.Glue(args, 0),
                                                       false));
                        break;

                    case "CHANNELMESSAGE": // server broadcast to channel
                        InvokeSaid(new TasSayEventArgs(SayPlace.Channel,
                                                       args[0],
                                                       "",
                                                       Utils.Glue(args, 1),
                                                       false));
                        break;

                    case "SAID": // someone said something in channel
                        InvokeSaid(new TasSayEventArgs(SayPlace.Channel,
                                                       args[0],
                                                       args[1],
                                                       Utils.Glue(args, 2),
                                                       false));
                        break;

                    case "RING":
                        Rang(this, new EventArgs<string>(args[0]));
                        break;

                    case "SAIDEX": // someone said something with emote in channel
                        InvokeSaid(new TasSayEventArgs(SayPlace.Channel,
                                                       args[0],
                                                       args[1],
                                                       Utils.Glue(args, 2),
                                                       true));
                        break;

                    case "SAYPRIVATE": // sent back from sever when user sends private message
                        InvokeSaid(new TasSayEventArgs(SayPlace.User,
                                                       args[0],
                                                       UserName,
                                                       Utils.Glue(args, 1),
                                                       false));
                        break;

                    case "SAIDPRIVATE": // someone said something to me
                        InvokeSaid(new TasSayEventArgs(SayPlace.User,
                                                       args[0],
                                                       args[0],
                                                       Utils.Glue(args, 1),
                                                       false));
                        break;

                    case "SAIDBATTLE": // someone said something in battle
                        InvokeSaid(new TasSayEventArgs(SayPlace.Battle,
                                                       "",
                                                       args[0],
                                                       Utils.Glue(args, 1),
                                                       false));
                        break;

                    case "SAIDBATTLEEX": // someone said in battle with emote

                        InvokeSaid(new TasSayEventArgs(SayPlace.Battle,
                                                       "",
                                                       args[0],
                                                       Utils.Glue(args, 1),
                                                       true));
                        break;
                    case "REDIRECT": // server sends backup IP
                        OnRedirect(args);
                        break;

                    case "CLIENTSTATUS": // client's status changed
                        OnClientStatus(args);
                        break;

                    case "CLIENTS": // client list sent after channel join
                        OnClients(args);
                        break;

                    case "JOINED": // user joined one of my channels
                        OnJoined(args);
                        break;

                    case "LEFT": // user left one of my channels
                        OnLeft(args);
                        break;

                    case "CHANNELTOPIC": // channel topic update (after joining a channel)
                        OnChannelTopic(args);
                        break;

                    case "OPENBATTLEFAILED": // opening new battle has failed
                        BattleOpenFailed(this, new TasEventArgs(Utils.Glue(args)));
                        break;

                    case "OPENBATTLE": // openbattle ok
                        OnOpenBattle(args);
                        break;

                    case "REQUESTBATTLESTATUS": // ask for status at the beginning of the battle
                        RequestBattleStatus(this, new EventArgs());
                        break;

                    case "JOINBATTLE": // we joined the battle
                        OnJoinBattle(args);
                        break;

                    case "FORCEJOINBATTLE":
                        OnForceJoinBattle(args);
                        break;

                    case "JOINEDBATTLE": // user joined the battle
                        OnJoinedBattle(args);
                        break;

                    case "JOINBATTLEFAILED": // user failed to join battle 
                        JoinBattleFailed(this, new TasEventArgs(args));
                        break;

                    case "ADDBOT": // bot added to battle
                        OnAddBot(args);
                        break;

                    case "REMOVEBOT": // bot removed from battle
                        OnRemoveBot(args);
                        break;

                    case "UPDATEBOT": // bot data changed
                        OnUpdateBot(args);
                        break;

                    case "LEFTBATTLE": // user left the battle
                        OnLeftBattle(args);
                        break;

                    case "CLIENTBATTLESTATUS": // player battle status has changed
                        OnClientBattleStatus(args);
                        break;

                    case "UPDATEBATTLEINFO": // update external battle info (lock and map)
                        OnUpdateBattleInfo(args);
                        break;

                    case "BATTLEOPENED":
                        OnBattleOpened(args);
                        break;

                    case "BATTLECLOSED":
                        OnBattleClosed(args);
                        break;

                    case "CLIENTIPPORT":
                        OnClientIpPort(args);
                        break;

                    case "SETSCRIPTTAGS": // updates internal battle details
                        OnSetScriptTags(args);
                        break;

                    case "UDPSOURCEPORT":
                        OnUdpSourcePort();
                        break;

                    
                    case "ADDSTARTRECT":
                        OnAddStartRect(args);
                        break;

                    case "REMOVESTARTRECT":
                        OnRemoveStartRect(args);
                        break;

                    case "TESTLOGINACCEPT":
                        TestLoginAccepted(this, new TasEventArgs(args));
                        break;

                    case "TESTLOGINDENY":
                        TestLoginDenied(this, new TasEventArgs(args));
                        break;
                }
            }
            catch (Exception e)
            {
                //not throwing "ApplicationException" because we can explicitly say its application fault here:
                Trace.TraceError("TASC error: Error was thrown while processing chat command {0} \"{1}\" (check if chat event trigger faulty code in application): {2}", command, Utils.Glue(args), e);
            }
        }

        void OnRemoveStartRect(string[] args)
        {
            var allyNo = int.Parse(args[0]);
            MyBattle.Rectangles.Remove(allyNo);
            StartRectRemoved(this, new TasEventArgs(args));
        }

        void OnAddStartRect(string[] args)
        {
            var allyNo = int.Parse(args[0]);
            var left = int.Parse(args[1]);
            var top = int.Parse(args[2]);
            var right = int.Parse(args[3]);
            var bottom = int.Parse(args[4]);
            var rect = new BattleRect(left, top, right, bottom);
            MyBattle.Rectangles[allyNo] = rect;
            StartRectAdded(this, new TasEventArgs(args));
        }

        void OnUdpSourcePort()
        {
            udpPunchingTimer.Stop();
            if (startingAfterUdpPunch) {
                startingAfterUdpPunch = false;

                // send UDP packets to client (2x to be sure)
                foreach (var ubs in MyBattle.Users) if (ubs.ip != IPAddress.None && ubs.port != 0) SendUdpPacket(lastUdpSourcePort, ubs.ip.ToString(), ubs.port);
                foreach (var ubs in MyBattle.Users) if (ubs.ip != IPAddress.None && ubs.port != 0) SendUdpPacket(lastUdpSourcePort, ubs.ip.ToString(), ubs.port);

                MyBattle.HostPort = lastUdpSourcePort; // update source port for hosting and start it
                ChangeMyUserStatus(false, true);
            }
        }

        void OnSetScriptTags(string[] args)
        {
            var bd = new BattleDetails();
            bd.Parse(Utils.Glue(args), MyBattle.ModOptions);
            MyBattle.Details = bd;
            MyBattle.ScriptTags.AddRange(args);
            BattleDetailsChanged(this, new TasEventArgs(args));
        }

        void OnClientIpPort(string[] args)
        {
            var idx = MyBattle.GetUserIndex(args[0]);
            if (idx != -1) {
                var bs = MyBattle.Users[idx];
                bs.ip = IPAddress.Parse(args[1]);
                bs.port = int.Parse(args[2]);
                MyBattle.Users[idx] = bs;
                BattleUserIpRecieved(this, new TasEventArgs(args));
            }
        }

        void OnBattleClosed(string[] args)
        {
            var battleID = Int32.Parse(args[0]);
            Battle battle;
            if (!existingBattles.TryGetValue(battleID, out battle)) return;
            foreach (var u in battle.Users) {
                User user;
                if (ExistingUsers.TryGetValue(u.Name, out user)) user.IsInBattleRoom = false;
            }
            if (battle == MyBattle) {
                battle.ScriptTags.Clear();
                battle.Users.Clear();
                BattleClosed(this, new EventArgs<Battle>(battle));
                MyBattleEnded(this, new EventArgs<Battle>(battle));
            }
            BattleEnding(this, new EventArgs<Battle>(battle));
            existingBattles.Remove(battleID);
            BattleEnded(this, new EventArgs<Battle>(battle));
        }

        void OnBattleOpened(string[] args)
        {
            var mapHash = args[9];
            var rest = Utils.Glue(args, 10).Split('\t');
            var mapName = rest[2];
            var modName = rest[4];
            if (!IsBattleVisible(modName)) return;

            var newBattle = new Battle {
                BattleID = Int32.Parse(args[0]),
                IsReplay = args[1] != "0",
                Founder = ExistingUsers[args[3]],
                Ip = args[4],
                HostPort = Int32.Parse(args[5]),
                MaxPlayers = Int32.Parse(args[6]),
                Password = args[7] != "1" ? "*" : "apassword",
                EngineVersion = rest[1],
                EngineName = rest[0]
            };

            if (newBattle.Founder.Name == UserName) newBattle.Ip = localIp; // lobby can send wahtever, betteroverride here

            // NatType = Int32.Parse(args[2]); // todo: correctly add nattype
            newBattle.MapName = mapName;
            newBattle.MapHash = Int32.Parse(mapHash);
            newBattle.Title = rest[3];
            newBattle.ModName = modName;
            newBattle.Users.Add(new UserBattleStatus(newBattle.Founder.Name, newBattle.Founder));
            existingBattles[newBattle.BattleID] = newBattle;
            newBattle.Founder.IsInBattleRoom = true;
            BattleFound(this, new EventArgs<Battle>(newBattle));
            return;
        }

        void OnUpdateBattleInfo(string[] args)
        {
            var battleID = Int32.Parse(args[0]);
            var specCount = Int32.Parse(args[1]);
            var mapName = Utils.Glue(args, 4);
            var mapHash = Int32.Parse(args[3]);
            var isLocked = Int32.Parse(args[2]) > 0;

            Battle battle;
            if (!existingBattles.TryGetValue(battleID, out battle)) return;
            battle.SpectatorCount = specCount;

            var bi = new BattleInfoEventArgs(battleID, specCount, mapName, mapHash, isLocked);

            if (battle.MapName != mapName || battle.MapHash != mapHash) {
                battle.MapName = mapName;
                battle.MapHash = mapHash;
                if (battle == MyBattle) MyBattleMapChanged(this, bi);
                BattleMapChanged(this, bi);
            }

            if (battle.IsLocked != isLocked) {
                battle.IsLocked = isLocked;
                BattleLockChanged(this, bi);
            }
            BattleInfoChanged(this, bi);
        }

        void OnClientBattleStatus(string[] args)
        {
            var userIndex = MyBattle.GetUserIndex(args[0]);
            if (userIndex != -1) {
                var battleStatus = MyBattle.Users[userIndex];
                battleStatus.SetFrom(int.Parse(args[1]), int.Parse(args[2]));
                MyBattle.Users[userIndex] = battleStatus;
                if (MyBattle.Founder.Name == UserName) UpdateSpectators();
                if (battleStatus.Name == UserName) {
                    lastUserBattleStatus = battleStatus.ToInt();
                    BattleMyUserStatusChanged(this, new TasEventArgs(args));
                }
                BattleUserStatusChanged(this, new TasEventArgs(args));
            }
        }

        void OnLeftBattle(string[] args)
        {
            var battleID = Int32.Parse(args[0]);
            var user = args[1];
            Battle battle;
            if (!existingBattles.TryGetValue(battleID, out battle)) return;
            if (!existingUsers.ContainsKey(user)) return;
            battle.RemoveUser(user);
            battle.ScriptTags.Clear();
            var userName = args[1];
            ExistingUsers[userName].IsInBattleRoom = false;

            if (MyBattle != null && battleID == MyBattleID) {
                if (MyBattle.Founder.Name == UserName) UpdateSpectators();
                if (user == UserName) {
                    MyBattle = null;
                    MyBattleID = 0;
                    BattleClosed(this, new EventArgs<Battle>(battle));
                }
            }
            BattleUserLeft(this, new BattleUserEventArgs(userName, battleID));
        }

        void OnUpdateBot(string[] args)
        {
            if (MyBattle != null && int.Parse(args[0]) == MyBattleID) {
                var st = MyBattle.Bots.Single(bot => bot.Name == args[1]);
                st.SetFrom(int.Parse(args[2]), int.Parse(args[3]));
                BattleBotUpdated(this, new EventArgs<BotBattleStatus>(st));
            }
        }

        void OnRemoveBot(string[] args)
        {
            if (MyBattle != null && int.Parse(args[0]) == MyBattleID) {
                var toDel = MyBattle.Bots.Single(bot => bot.Name == args[1]);
                MyBattle.Bots.Remove(toDel);
                BattleBotRemoved(this, new EventArgs<BotBattleStatus>(toDel));
            }
        }

        void OnAddBot(string[] args)
        {
            if (MyBattle != null && int.Parse(args[0]) == MyBattleID) {
                var bs = new BotBattleStatus(args[1], args[2], Utils.Glue(args, 5));
                bs.SetFrom(int.Parse(args[3]), int.Parse(args[4]));
                MyBattle.Bots.Add(bs);
                BattleBotAdded(this, new EventArgs<BotBattleStatus>(bs));
            }
        }

        void OnJoinedBattle(string[] args)
        {
            var joinedBattleID = Int32.Parse(args[0]);
            Battle battle;
            if (!existingBattles.TryGetValue(joinedBattleID, out battle)) return;
            var userName = args[1];
            var scriptPassword = args.Length > 2 ? args[2] : null;
            var ubs = new UserBattleStatus(userName, existingUsers[userName], scriptPassword);
            battle.Users.Add(ubs);
            ExistingUsers[userName].IsInBattleRoom = true;
            if (userName == UserName) lastUserBattleStatus = ubs.ToInt();
            BattleUserJoined(this, new BattleUserEventArgs(userName, joinedBattleID, scriptPassword));
        }

        void OnForceJoinBattle(string[] args)
        {
            if (MyBattle != null) LeaveBattle();
            var battleid = Int32.Parse(args[0]);
            if (args.Length == 1) JoinBattle(battleid);
            else JoinBattle(battleid, args[1]);
        }

        void OnJoinBattle(string[] args)
        {
            var joinedBattleID = Int32.Parse(args[0]);
            MyBattleID = joinedBattleID;
            var battle = existingBattles[joinedBattleID];
            battle.Bots.Clear();
            MyBattle = battle;
            BattleJoined(this, new EventArgs<Battle>(MyBattle));
        }

        void OnOpenBattle(string[] args)
        {
            MyBattleID = int.Parse(args[0]);
            existingBattles[MyBattleID] = MyBattle;
            var self = new UserBattleStatus(UserName, existingUsers[UserName]);
            MyBattle.Users.Add(self); // add self
            lastUserBattleStatus = self.ToInt();
            UpdateBattleDetails(MyBattle.Details);
            // SetScriptTag(MyBattle.Mod.GetDefaultModOptionsTags()); // sends default mod options // enable if tasclient is not fixed
            BattleOpened(this, new TasEventArgs(args[0]));
        }

        void OnChannelTopic(string[] args)
        {
            var c = JoinedChannels[args[0]];
            c.TopicSetBy = args[1];
            c.TopicSetDate = ConvertMilisecondTime(args[2]);
            c.Topic = Utils.Glue(args, 3);
            ChannelTopicChanged(this, new TasEventArgs(args[0]));
        }

        void OnLeft(string[] args)
        {
            var channelName = args[0];
            var userName = args[1];
            var reason = Utils.Glue(args, 2);
            var channel = JoinedChannels[channelName];
            channel.Users.Remove(userName);
            //ChannelUserRemoved(this, new TasEventArgs(channelName, userName, reason));
        }

        void OnJoined(string[] args)
        {
            var channelName = args[0];
            var userName = args[1];
            var channel = JoinedChannels[channelName];
            channel.Users.Add(userName);
            //ChannelUserAdded(this, new TasEventArgs(channelName, userName));
        }

        void OnClients(string[] args)
        {
            var usrs = Utils.Glue(args, 1).Split(' ');
            foreach (var s in usrs) JoinedChannels[args[0]].Users.Add(s);
            //ChannelUsersAdded(this, new TasEventArgs(args));
        }

        void OnClientStatus(string[] args)
        {
            int status;
            int.TryParse(args[1], out status);

            var u = ExistingUsers[args[0]];
            var old = u.Clone();
            //u.FromInt(status);

            //if (u.Name == UserName) lastUserStatus = u.ToInt();

            if (u.IsInGame && old.IsInGame == false) BattleStarted(this, new EventArgs<User>(u));

            if (MyBattle != null && MyBattle.Founder.Name == u.Name) {
                if (u.IsInGame && old.IsInGame == false) MyBattleStarted(this, new TasEventArgs(args));
                if (!u.IsInGame && old.IsInGame == true) MyBattleHostExited(this, new TasEventArgs(args));
            }

            //UserStatusChanged(this, new TasEventArgs(args));
        }

        void OnRedirect(string[] args)
        {
            var host = args[0];
            var port = int.Parse(args[1]);
            Connect(host, port);
        }


        void OnRemoveUser(string[] args)
        {
            var userName = args[0];
            var user = ExistingUsers[userName];
            UserRemoved(this, new TasEventArgs(args));
            ExistingUsers.Remove(userName);
        }

        void OnKickUser(string[] args)
        {
            Trace.TraceInformation(String.Format("User {0} kicked (we are {1})"), args[1], MyUser.Name);
            string us = args[1];
            if (us == MyUser.Name) {
                string kicker = args[0];
                string reason = args.Length > 2 ? string.Join(" ", args, 2) : null;
                KickedFromServer(this, new KickedFromServerEventArgs(kicker, reason));
            }
        }

        void OnForceLeaveChannel(string[] args)
        {
            var channel = args[0];
            JoinedChannels.Remove(channel);
            ChannelForceLeave(this, new TasEventArgs(args));
        }

        void OnAddUser(string[] args)
        {
            try {
                //var u = User.Create(args[0]);
                //u.Country = args[1];
                //int cpu;
                //int.TryParse(args[2], out cpu);
                //u.Cpu = cpu;
                //u.LobbyID = Convert.ToInt32(args[3]);
                //ExistingUsers.Add(u.Name, u);
                //UserAdded(this, new EventArgs<User>(u));
            } catch (Exception e) {
                //TraceError ensure bright red coloured message (WriteLine looked harmless):
                Trace.TraceError("Error was thrown while processing chat command ADDUSER (check if this event trigger faulty code in application): " + e);
            }
        }


        

        async Task Process(LoginResponse loginResponse)
        {
            if (loginResponse.ResultCode == LoginResponse.Code.Ok) {
                isLoggedIn = true;
                LoginAccepted(this, new TasEventArgs());
            } else {
                isLoggedIn = false;
                LoginDenied(this, loginResponse);
            }
        }

        async Task Process(User user)
        {
            User old;
            existingUsers.TryGetValue(user.Name, out old);
            existingUsers[user.Name] = user;
            if (old == null) UserAdded(this, user);
            UserStatusChanged(this, new OldNewPair<User>(old, user));
        }


        async Task Process(RegisterResponse registerResponse)
        {
            if (registerResponse.ResultCode == RegisterResponse.Code.Ok)
            {
                RegistrationAccepted(this, new TasEventArgs());
            }
            else
            {
                RegistrationDenied(this, registerResponse);
            }
        }

        async Task Process(Say say)
        {
            InvokeSaid(new TasSayEventArgs(say.Place, say.Target,say.User, say.Text, say.IsEmote));
        }



        public Welcome ServerWelcome = new Welcome();


        async Task Process(Welcome welcome)
        {
            ServerWelcome = welcome;
            Connected(this, welcome);
        }

        async Task Process(JoinChannelResponse response)
        {
            if (response.Success) {
                JoinedChannels[response.Name] = response.Channel;
                var cancelEvent = new CancelEventArgs<Channel>(response.Channel);
                PreviewChannelJoined(this, cancelEvent);
                if (!cancelEvent.Cancel) {
                    ChannelJoined(this, response.Channel);
                    ChannelUserAdded(this, new ChannelUserInfo() {Channel = response.Channel, Users = response.Channel.Users.Select(x => existingUsers[x]).ToList()}); 
                }
            } else {
                ChannelJoinFailed(this, response);
            }
        }

        async Task Process(ChannelUserAdded arg)
        {
            Channel chan;
            if (joinedChannels.TryGetValue(arg.ChannelName, out chan)) {
                if (!chan.Users.Contains(arg.UserName)) {
                    chan.Users.Add(arg.UserName);
                    var users = new List<User>();
                    users.Add(existingUsers[arg.UserName]);
                    ChannelUserAdded(this, new ChannelUserInfo() { Channel = chan, Users = users});
                }
            }
        }




        void InvokeSaid(TasSayEventArgs sayArgs)
        {
            var previewSaidEventArgs = new CancelEventArgs<TasSayEventArgs>(sayArgs);
            PreviewSaid(this, previewSaidEventArgs);
            if (!previewSaidEventArgs.Cancel) Said(this, sayArgs);
        }

        bool IsBattleVisible(string modname)
        {
            if (FilterBattleByMod != null)
            {
                var e = new CancelEventArgs<string>(modname);
                e.Cancel = false;
                FilterBattleByMod(this, e);
                return !e.Cancel;
            }
            else return true;
        }


        void SendUdpPacket(int sourcePort, string targetIp, int targetPort)
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            var local = new IPEndPoint(IPAddress.Any, sourcePort);
            s.Bind(local);
            //s.ExclusiveAddressUse = false;
            lastUdpSourcePort = ((IPEndPoint)s.LocalEndPoint).Port;

            s.Connect(targetIp, targetPort);
            s.Send(Encoding.ASCII.GetBytes(UserName)); // todo: make async?
            s.Close();
        }


        void UpdateBattleInfo(bool lck, string mapname, int checksum)
        {
            {
                lockToChangeTo = lck;
                //con.SendCommand("UPDATEBATTLEINFO", MyBattle.Users.Count(user => user.IsSpectator), (lck ? 1 : 0), checksum, mapname);
            }
        }


        void UpdateSpectators()
        {
            {
                var n = MyBattle.Users.Count(x => x.IsSpectator);
                if (n != lastSpectatorCount)
                {
                    lastSpectatorCount = n;
                    //con.SendCommand("UPDATEBATTLEINFO", n, lockToChangeTo ? 1 : 0, mapChecksumToChangeTo,  mapToChangeTo);
                }
            }
        }


        void OnPingTimer(object sender, EventArgs args)
        {
/*            if (con != null && IsConnected) {
                //if (lastPing != DateTime.MinValue && lastPing.Subtract(lastPong).TotalSeconds > pingInterval*2) {
                    // server didnt respond to ping in 30-60s 
                   // con.RequestClose();
                //} else {
                    lastPing = DateTime.UtcNow;
                    con.SendCommand("PING");
                //}
            }
            else if (ConnectionFailed && !IsConnected)
            {
                Connect(serverHost, serverPort);
            }*/
        }

        void udpPunchingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            SendUdpPacket(lastUdpSourcePort, serverHost, serverUdpHolePunchingPort);
        }

    }
}