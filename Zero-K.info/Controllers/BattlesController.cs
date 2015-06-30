 using System;
using System.Collections.Generic;
 using System.Data.Entity;
 using System.Linq;
using System.Web;
using System.Web.Mvc;
using ZkData;
using ZeroKWeb.Models;

namespace ZeroKWeb.Controllers
{
    public class BattlesController : Controller
    {
        //
        // GET: /Battles/

        /// <summary>
        /// Returns the page of the <see cref="SpringBattle"/> with the specified ID
        /// </summary>
        public ActionResult Detail(int id)
        {
          var db = new ZkDataContext();
          var bat = db.SpringBattles.FirstOrDefault(x => x.SpringBattleID == id);
            if (bat == null) return Content("No such battle exists");
          if (bat.ForumThread != null)
          {
            bat.ForumThread.UpdateLastRead(Global.AccountID, false);
            db.SubmitChanges();
          }
          return View(bat);
        }

        /// <summary>
        /// Returns the main battle replay list; params filter
        /// </summary>
        public ActionResult Index(string battleTitle,
                                  string map,
                                  string mod,
                                  string user,
                                  int? players,
                                  int? age,
                                  int? duration,
                                  bool? mission,
                                  bool? bots,
                                  int? offset) {
            var db = new ZkDataContext();

            IQueryable<SpringBattle> q = db.SpringBattles.Include(x=>x.SpringBattlePlayers);
            
            if (!string.IsNullOrEmpty(battleTitle))
                q = q.Where(b => b.Title.Contains(battleTitle));

            if (!string.IsNullOrEmpty(map))
                q = q.Where(b => b.ResourceByMapResourceID.InternalName.Contains(map));

            if (mod == null) mod = "Zero-K";
            if (!string.IsNullOrEmpty(mod))
                q = q.Where(b => b.ResourceByModResourceID.InternalName.Contains( mod));

            //if (user == null && Global.IsAccountAuthorized) user = Global.Account.Name;
            if (!string.IsNullOrEmpty(user)) {
                var aid = (from account in db.Accounts
                          where account.Name == user
                           select account.AccountID).FirstOrDefault();
                if(aid != 0)
                    q = q.Where(b => b.SpringBattlePlayers.Any(p => !p.IsSpectator && p.AccountID == aid));
            }

            if (players.HasValue)
                q = q.Where(b => b.SpringBattlePlayers.Where(p => !p.IsSpectator).Count() == players.Value);

            if (age.HasValue) {
                DateTime limit = DateTime.UtcNow;
                switch (age) {
                    case 1:
                        limit = DateTime.Now.AddHours(-1);
                        break;
                    case 2:
                        limit = DateTime.UtcNow.AddDays(-7);
                        break;
                    case 3:
                        limit = DateTime.UtcNow.AddDays(-31);
                        break;
                }
                q = q.Where(b => b.StartTime >= limit);
            }

            if (duration.HasValue)
                q = q.Where(b => Math.Abs(b.Duration - duration.Value * 60) < 300);

            if (mission.HasValue)
                q = q.Where(b => b.IsMission == mission.Value);

            if (bots.HasValue)
                q = q.Where(b => b.HasBots == bots.Value);

            q = q.OrderByDescending(b => b.StartTime);
                

            if (offset.HasValue) q = q.Skip(offset.Value);
            q = q.Take(Global.AjaxScrollCount);

            var result = q.ToList().Select(b =>
                    new BattleQuickInfo() {
                        Battle = b,
                        Players = b.SpringBattlePlayers,
                        Map = b.ResourceByMapResourceID,
                        Mod = b.ResourceByModResourceID
                    }).ToList();


            //if(result.Count == 0)
            //    return Content("");
            if (offset.HasValue)
                return View("BattleTileList", result);
            else
                return View(result);
        }

        /// <summary>
        /// Returns a page with the <see cref="SpringBattle"/> infolog
        /// </summary>
        [Auth(Role = AuthRole.ZkAdmin)]
        public ActionResult Logs(int id)
        {
            using (var db = new ZkDataContext()) {
                var bat = db.SpringBattles.Single(x => x.SpringBattleID == id);
                return Content(System.IO.File.ReadAllText(string.Format(GlobalConst.InfologPathFormat, bat.EngineGameID)), "text/plain");//,string.Format("infolog_{0}.txt", bat.SpringBattleID)
            }
            
        }
    }

    public struct BattleQuickInfo {
        public SpringBattle Battle { get; set; }
        public IEnumerable<SpringBattlePlayer> Players { get; set; }
        public Resource Map { get; set; }
        public Resource Mod { get; set; }
    }
}
