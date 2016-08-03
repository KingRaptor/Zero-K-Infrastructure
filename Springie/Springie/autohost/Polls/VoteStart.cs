using System;
using System.Threading;
using LobbyClient;
using System.Linq;

namespace Springie.autohost.Polls
{
    public class VoteStart: AbstractPoll
    {
        
        public VoteStart(TasClient tas, Spring spring, AutoHost ah): base(tas, spring, ah) {}

        protected override bool PerformInit(TasSayEventArgs e, string[] words, out string question, out int winCount)
        {
            winCount = 0;
            question = null;
            CountNoIntoWinCount = false;

            if (!spring.IsRunning) {

                var invalid = tas.MyBattle.Users.Values.Where(x => !x.IsSpectator && (x.SyncStatus != SyncStatuses.Synced || x.LobbyUser.IsAway)).ToList();
                if (invalid.Count > 0) foreach (var inv in invalid) ah.ComRing(e, new[] { inv.Name }); // ring invalids ot notify them

                // people wihtout map and spring map changed in last 2 minutes, dont allow start yet
                if (tas.MyBattle.Users.Values.Any(x=>!x.IsSpectator && x.SyncStatus != SyncStatuses.Synced) && DateTime.Now.Subtract(ah.lastMapChange).TotalSeconds < MainConfig.MapChangeDownloadWait) {
                    var waitTime = (int)(MainConfig.MapChangeDownloadWait - DateTime.Now.Subtract(ah.lastMapChange).TotalSeconds);
                    AutoHost.Respond(tas, spring, e, string.Format("Map was changed and some players don't have it yet, please wait {0} more seconds", waitTime));
                    return false;
                }

                question = "Start game? ";

                if (invalid.Count > 0) {
                    var invalids = string.Join(",", invalid);
                    ah.SayBattle(invalids + " will be forced spectators if they don't download their maps and stop being afk when vote ends");
                    question += string.Format("WARNING, SPECTATES: {0}", invalids);
                }
                if (tas.MyBattle.Users.Values.Count(x => !x.IsSpectator) >= 2)
                {
                  winCount = (tas.MyBattle.Users.Values.Count(x => !x.IsSpectator) + 1) / 2 + 1;
                }
                else
                {
                  winCount = 1;
                }
                return true;
            }
            else
            {
                AutoHost.Respond(tas, spring, e, "battle already started");
                return false;
            }
        }

        protected override bool AllowVote(TasSayEventArgs e)
        {
            if (tas.MyBattle == null) return false;
            UserBattleStatus entry;
            tas.MyBattle.Users.TryGetValue(e.UserName, out entry);
            if (entry == null || entry.IsSpectator)
            {
                ah.Respond(e, string.Format("Only players can vote"));
                return false;
            }
            else return true;
        }

        protected override void SuccessAction()
        {
            ah.ComForceSpectatorAfk(TasSayEventArgs.Default, new string[]{});
            foreach (var user in tas.MyBattle.Users.Values.Where(x => !x.IsSpectator && (x.SyncStatus != SyncStatuses.Synced || x.LobbyUser.IsAway))) {
                ah.ComForceSpectator(TasSayEventArgs.Default, new string[]{user.Name});
            }
            new Thread(()=>
                {
                    Thread.Sleep(500); // sleep to register spectating        
                    ah.ComStart(TasSayEventArgs.Default, new string[] { });
                }).Start();
        }
    }
}