﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;
using LobbyClient;
using PlasmaShared;
using ServiceStack.Text;
using ZkData;
using Timer = System.Timers.Timer;

namespace ZeroKWeb
{
    /// <summary>
    ///     Handles arranging and starting of PW games
    /// </summary>
    public class PlanetWarsMatchMaker
    {
        /// <summary>
        ///     Possible attack options
        /// </summary>
        public List<AttackOption> AttackOptions { get; set; }
        public DateTime AttackerSideChangeTime { get; set; }
        public int AttackerSideCounter { get; set; }
        public AttackOption Challenge { get; set; }

        public DateTime? ChallengeTime { get; set; }

        public Dictionary<string, AttackOption> RunningBattles { get; set; }
        
        readonly List<Faction> factions;
        readonly string pwHostName;
        
        readonly TasClient tas;

        Timer timer;
        /// <summary>
        ///     Faction that should attack this turn
        /// </summary>
        public Faction AttackingFaction { get { return factions[AttackerSideCounter%factions.Count]; } }


        public PlanetWarsMatchMaker(TasClient tas)
        {
            AttackOptions = new List<AttackOption>();
            RunningBattles = new Dictionary<string, AttackOption>();

            this.tas = tas;
            tas.PreviewSaid += TasOnPreviewSaid;
            tas.LoginAccepted += TasOnLoginAccepted;
            tas.UserRemoved += TasOnUserRemoved;
            tas.ChannelUserAdded += TasOnChannelUserAdded;

            timer = new Timer(10000);
            timer.AutoReset = true;
            timer.Elapsed += TimerOnElapsed;
            timer.Start();

            var db = new ZkDataContext();
            pwHostName = db.AutohostConfigs.First(x => x.AutohostMode == AutohostMode.Planetwars).Login.TrimNumbers();

            Galaxy gal = db.Galaxies.First(x => x.IsDefault);
            factions = db.Factions.Where(x => !x.IsDeleted).ToList();

            var dbState = JsonSerializer.DeserializeFromString<PlanetWarsMatchMaker>(gal.MatchMakerState);
            if (dbState != null)
            {
                AttackerSideCounter = dbState.AttackerSideCounter;
                AttackOptions = dbState.AttackOptions;
                Challenge = dbState.Challenge;
                ChallengeTime = dbState.ChallengeTime;
                AttackerSideChangeTime = dbState.AttackerSideChangeTime;
                RunningBattles = dbState.RunningBattles;
            }
            else
            {
                AttackerSideCounter = gal.AttackerSideCounter;
                AttackerSideChangeTime = gal.AttackerSideChangeTime ?? DateTime.UtcNow;
            }
        }

        public void AcceptChallenge()
        {
            string targetHost;
            Battle emptyHost =
                tas.ExistingBattles.Values.FirstOrDefault(
                    x => !x.IsInGame && x.Founder.Name.TrimNumbers() == pwHostName && x.Users.All(y => y.IsSpectator || y.Name == x.Founder.Name));

            if (emptyHost != null)
            {
                targetHost = emptyHost.Founder.Name;
                RunningBattles[targetHost] = Challenge;
                tas.Say(TasClient.SayPlace.User, targetHost, "!map " + Challenge.Map, false);
                Thread.Sleep(500);
                foreach (User x in Challenge.Attackers) tas.ForceJoinBattle(x.Name, emptyHost.BattleID);
                foreach (User x in Challenge.Defenders) tas.ForceJoinBattle(x.Name, emptyHost.BattleID);

                Utils.StartAsync(() =>
                {
                    Thread.Sleep(8000);
                    tas.Say(TasClient.SayPlace.User, targetHost, "!forcestart", false);
                });
            }

            AttackerSideCounter++;
            ResetAttackOptions();
        }

        /// <summary>
        ///     Invoked from web page
        /// </summary>
        /// <param name="planet"></param>
        public void AddAttackOption(Planet planet)
        {
            if (!AttackOptions.Any(x => x.PlanetID == planet.PlanetID) && Challenge == null && planet.OwnerFactionID != AttackingFaction.FactionID)
            {
                AttackOptions.Add(new AttackOption
                {
                    PlanetID = planet.PlanetID,
                    Map = planet.Resource.InternalName,
                    OwnerFactionID = planet.OwnerFactionID,
                    Name = planet.Name
                });

                UpdateLobby();
            }
        }

        public PwMatchCommand GenerateLobbyCommand()
        {
            PwMatchCommand command;
            if (Challenge == null)
            {
                command = new PwMatchCommand(PwMatchCommand.ModeType.Attack)
                {
                    Options = AttackOptions.Select(x => x.ToVoteOption(PwMatchCommand.ModeType.Attack)).ToList(),
                    DeadlineSeconds = GlobalConst.PlanetWarsMinutesToAttack*60 - (int)DateTime.UtcNow.Subtract(AttackerSideChangeTime).TotalSeconds,
                    AttackerFaction = AttackingFaction.Shortcut
                };
            }
            else
            {
                command = new PwMatchCommand(PwMatchCommand.ModeType.Defend)
                {
                    Options = new List<PwMatchCommand.VoteOption> { Challenge.ToVoteOption(PwMatchCommand.ModeType.Defend) },
                    DeadlineSeconds =
                        GlobalConst.PlanetWarsMinutesToAccept*60 - (int)DateTime.UtcNow.Subtract(ChallengeTime ?? DateTime.UtcNow).TotalSeconds,
                    AttackerFaction = AttackingFaction.Shortcut,
                    DefenderFactions = GetDefendingFactions(Challenge).Select(x => x.Shortcut).ToList()
                };
            }
            return command;
        }

        public AttackOption GetBattleInfo(string hostName)
        {
            AttackOption option;
            RunningBattles.TryGetValue(hostName, out option);
            return option;
        }

        public void UpdateLobby()
        {
            tas.Extensions.SendJsonData(GenerateLobbyCommand());
            SaveStateToDb();
        }

        public void UpdateLobby(string player)
        {
            tas.Extensions.SendJsonData(player, GenerateLobbyCommand());
        }

        List<Faction> GetDefendingFactions(AttackOption target)
        {
            if (target.OwnerFactionID != null) return new List<Faction> { factions.Find(x => x.FactionID == target.OwnerFactionID) };
            return factions.Where(x => x != AttackingFaction).ToList();
        }

        void JoinPlanetAttack(int targetPlanetId, string userName)
        {
            AttackOption attackOption = AttackOptions.Find(x => x.PlanetID == targetPlanetId);
            if (attackOption != null)
            {
                User user;
                if (tas.ExistingUsers.TryGetValue(userName, out user))
                {
                    var db = new ZkDataContext();
                    Account account = Account.AccountByLobbyID(db, user.LobbyID);
                    if (account != null && account.FactionID == AttackingFaction.FactionID && account.CanPlayerPlanetWars())
                    {
                        // remove existing user from other options
                        foreach (AttackOption aop in AttackOptions) aop.Attackers.RemoveAll(x => x.Name == userName);

                        // add user to this option
                        if (attackOption.Attackers.Count < GlobalConst.PlanetWarsMatchSize)
                        {
                            attackOption.Attackers.Add(user);

                            if (attackOption.Attackers.Count == GlobalConst.PlanetWarsMatchSize) StartChallenge(attackOption);
                            else UpdateLobby();
                        }
                    }
                }
            }
        }

        void JoinPlanetDefense(int targetPlanetID, string userName)
        {
            if (Challenge != null && Challenge.PlanetID == targetPlanetID && Challenge.Defenders.Count < GlobalConst.PlanetWarsMatchSize)
            {
                User user;
                if (tas.ExistingUsers.TryGetValue(userName, out user))
                {
                    var db = new ZkDataContext();
                    Account account = Account.AccountByLobbyID(db, user.LobbyID);
                    if (account != null && GetDefendingFactions(Challenge).Any(y => y.FactionID == account.FactionID) && account.CanPlayerPlanetWars())
                    {
                        if (!Challenge.Defenders.Any(y => y.LobbyID == user.LobbyID))
                        {
                            Challenge.Defenders.Add(user);
                            if (Challenge.Defenders.Count == GlobalConst.PlanetWarsMatchSize) AcceptChallenge();
                            else UpdateLobby();
                        }
                    }
                }
            }
        }

        void RecordPlanetwarsLoss(AttackOption option)
        {
            var db = new ZkDataContext();
            var text = new StringBuilder();
            List<int?> playerIds = option.Attackers.Select(x => (int?)x.LobbyID).Union(option.Defenders.Select(x => (int?)x.LobbyID)).ToList();
            text.AppendFormat("{0} won because nobody tried to defend", AttackingFaction.Name);
            try
            {
                PlanetWarsTurnHandler.EndTurn(option.Map, null, db, 0, db.Accounts.Where(x => playerIds.Contains(x.LobbyID)).ToList(), text, null);
            }
            catch (Exception ex)
            {
                text.Append(ex);
            }

            foreach (var fac in factions)
            {
                tas.Say(TasClient.SayPlace.Channel, fac.Shortcut, text.ToString(), true);
            }
        }

        void ResetAttackOptions()
        {
            AttackOptions.Clear();
            AttackerSideChangeTime = DateTime.UtcNow;
            Challenge = null;
            ChallengeTime = null;

            using (var db = new ZkDataContext())
            {
                var gal = db.Galaxies.First(x => x.IsDefault);
                int cnt = 3;
                var attacker = db.Factions.Single(x => x.FactionID == AttackingFaction.FactionID);
                var planets = gal.Planets.Where(x => x.OwnerFactionID != AttackingFaction.FactionID).OrderByDescending(x=>x.PlanetFactions.Where(y=>y.FactionID == AttackingFaction.FactionID).Sum(y=>y.Dropships)).ThenBy(x => x.PlanetFactions.Where(y => y.FactionID == AttackingFaction.FactionID).Select(y => y.Influence).FirstOrDefault());
                // list of planets by attacker's influence

                foreach (var p in planets)
                {
                    if (p.CanMatchMakerPlay(attacker))
                    {
                        AddAttackOption(p);                     // pick only those where you can actually attack atm
                        cnt--;
                    }
                    if (cnt == 0) break;
                }
            }
            
            UpdateLobby();

            tas.Say(TasClient.SayPlace.Channel, AttackingFaction.Shortcut, "It's your turn! Select a planet to attack", true);
        }

        void SaveStateToDb()
        {
            var db = new ZkDataContext();
            Galaxy gal = db.Galaxies.First(x => x.IsDefault);

            gal.MatchMakerState = JsonSerializer.SerializeToString(this);
            
            gal.AttackerSideCounter = AttackerSideCounter;
            gal.AttackerSideChangeTime = AttackerSideChangeTime;
            db.SubmitAndMergeChanges();
        }


        void StartChallenge(AttackOption attackOption)
        {
            Challenge = attackOption;
            ChallengeTime = DateTime.UtcNow;
            AttackOptions.Clear();
            UpdateLobby();

            foreach (var def in GetDefendingFactions(Challenge))
            {
                tas.Say(TasClient.SayPlace.Channel, def.Shortcut, "Join defense of your planet now, before it falls", true);
            }
        }


        void TasOnChannelUserAdded(object sender, TasEventArgs args)
        {
            string chan = args.ServerParams[0];
            string userName = args.ServerParams[1];
            Faction faction = factions.First(x => x.Shortcut == chan);
            if (faction != null)
            {
                var db = new ZkDataContext();
                var acc = Account.AccountByName(db, userName);
                if (acc != null && acc.CanPlayerPlanetWars()) UpdateLobby(userName);
            }
        }

        void TasOnLoginAccepted(object sender, TasEventArgs tasEventArgs)
        {
            ResetAttackOptions();
        }

        /// <summary>
        ///     Intercept channel messages for attacking/defending
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        void TasOnPreviewSaid(object sender, CancelEventArgs<TasSayEventArgs> args)
        {
            if (args.Data.Text.StartsWith("!") && args.Data.Place == TasSayEventArgs.Places.Channel &&
                args.Data.Origin == TasSayEventArgs.Origins.Player && args.Data.UserName != GlobalConst.NightwatchName)
            {
                Faction faction = factions.FirstOrDefault(x => x.Shortcut == args.Data.Channel);
                if (faction != null)
                {
                    if (faction == AttackingFaction)
                    {
                        int targetPlanetID;
                        if (int.TryParse(args.Data.Text.Substring(1), out targetPlanetID)) JoinPlanetAttack(targetPlanetID, args.Data.UserName);
                    }
                    else if (Challenge != null && GetDefendingFactions(Challenge).Contains(faction))
                    {
                        int targetPlanetID;
                        if (int.TryParse(args.Data.Text.Substring(1), out targetPlanetID)) JoinPlanetDefense(targetPlanetID, args.Data.UserName);
                    }
                }
            }
        }

        /// <summary>
        ///     Remove/reduce poll count due to lobby quits
        /// </summary>
        void TasOnUserRemoved(object sender, TasEventArgs args)
        {
            if (Challenge == null)
            {
                if (AttackOptions.Count > 0)
                {
                    string userName = args.ServerParams[0];
                    int sumRemoved = 0;
                    foreach (AttackOption aop in AttackOptions) sumRemoved += aop.Attackers.RemoveAll(x => x.Name == userName);
                    if (sumRemoved > 0) UpdateLobby();
                }
            }
            else
            {
                string userName = args.ServerParams[0];
                if (Challenge.Defenders.RemoveAll(x => x.Name == userName) > 0) UpdateLobby();
            }
        }

        void TimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            try
            {
                if (Challenge == null)
                {
                    // attack timer
                    if (DateTime.UtcNow.Subtract(AttackerSideChangeTime).TotalMinutes > GlobalConst.PlanetWarsMinutesToAttack)
                    {
                        AttackerSideCounter++;
                        ResetAttackOptions();
                    }
                }
                else
                {
                    // accept timer
                    if (DateTime.UtcNow.Subtract(ChallengeTime.Value).TotalMinutes > GlobalConst.PlanetWarsMinutesToAccept)
                    {
                        RecordPlanetwarsLoss(Challenge);

                        AttackerSideCounter++;
                        ResetAttackOptions();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }

        public class AttackOption
        {
            public List<User> Attackers = new List<User>();
            public List<User> Defenders = new List<User>();
            public string Map;
            public string Name;
            public int? OwnerFactionID;
            public int PlanetID;

            public PwMatchCommand.VoteOption ToVoteOption(PwMatchCommand.ModeType mode)
            {
                var opt = new PwMatchCommand.VoteOption
                {
                    PlanetID = PlanetID,
                    PlanetName = Name,
                    Map = Map,
                    Count = mode == PwMatchCommand.ModeType.Attack ? Attackers.Count : Defenders.Count
                };

                return opt;
            }
        }
    }
}