﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web.Routing;
using LobbyClient;
using NightWatch;
using ZkData;

namespace ZeroKWeb.Controllers
{
    public class UsersController: Controller
    {
        //
        // GET: /Users/


        [Auth(Role = AuthRole.ZkAdmin)]
        public ActionResult AutoResolveDuplicates()
        {
            var db = new ZkDataContext();

            // fixes duplicate name by preserving last working lobbyID 
            foreach (var dupl in db.Accounts.Where(x => x.LobbyID != null).GroupBy(x => x.Name).Where(x => x.Count() > 1))
            {
                List<Account> dupAccounts = db.Accounts.Where(x => x.Name == dupl.Key).ToList();
                Account bestAccount = dupAccounts.OrderByDescending(x => x.LastLogin).First();
                foreach (Account ac in dupAccounts) if (ac.LobbyID != bestAccount.LobbyID) ac.LobbyID = null;
            }
            db.SubmitChanges();

            // fixes duplicate lobbyID by preserving newer account
            foreach (var dupl in db.Accounts.GroupBy(x => x.LobbyID).Where(x => x.Count() > 1 && x.Key != null))
            {
                List<Account> dupAccounts = db.Accounts.Where(x => x.LobbyID == dupl.Key).ToList();
                Account bestAccount = dupAccounts.OrderByDescending(x => x.Level).First();
                foreach (Account ac in dupAccounts) if (ac.AccountID != bestAccount.AccountID) ac.LobbyID = null;
            }
            db.SubmitChanges();

            return Redirect("Duplicates");
        }

        [Auth(Role = AuthRole.ZkAdmin)]
        public ActionResult ChangeHideCountry(int accountID, bool hideCountry)
        {
            var db = new ZkDataContext();
            Account acc = db.Accounts.Single(x => x.AccountID == accountID);

            if (hideCountry) acc.Country = "??";
            Global.Nightwatch.Tas.SetHideCountry(acc.Name, hideCountry);
            db.SubmitChanges();

            return RedirectToAction("Detail", "Users", new { id = acc.AccountID });
        }

        [Auth(Role = AuthRole.ZkAdmin)]
        public ActionResult ChangeLobbyID(int accountID, int? newLobbyID)
        {
            var db = new ZkDataContext();
            Account account = db.Accounts.Single(x => x.AccountID == accountID);
            int? oldLobbyID = account.LobbyID;
            account.LobbyID = newLobbyID;
            db.SubmitChanges();
            string response = string.Format("{0} lobby ID change from {1} -> {2}", account.Name, oldLobbyID, account.LobbyID);
            foreach (Account duplicate in db.Accounts.Where(x => x.LobbyID == newLobbyID && x.AccountID != accountID))
            {
                response += string.Format("\n Duplicate: {0} - {1} {2}",
                                          duplicate.Name,
                                          duplicate.AccountID,
                                          Url.Action("Detail", new { id = duplicate.AccountID }));
            }
            return Content(response);
        }

        [Auth(Role = AuthRole.ZkAdmin)]
        public ActionResult ChangePermissions(int accountID, int springieLevel, bool zkAdmin, bool vpnException)
        {
            var db = new ZkDataContext();
            Account acc = db.Accounts.Single(x => x.AccountID == accountID);
            acc.SpringieLevel = springieLevel;
            acc.IsZeroKAdmin = zkAdmin;
            acc.HasVpnException = vpnException;
            db.SubmitChanges();
            Global.Nightwatch.Tas.Extensions.PublishAccountData(acc);
            
            Global.Nightwatch.Tas.Say(TasClient.SayPlace.Channel, AuthService.ModeratorChannel, string.Format("Permissions changed for {0} {1}  ", acc.Name, Url.Action("Detail", "Users", new { id = acc.AccountID }, "http")), true);
            return RedirectToAction("Detail", "Users", new { id = acc.AccountID });
        }

        [Auth(Role= AuthRole.ZkAdmin)]
        public ActionResult AdminUserDetail(int id)
        {
            var db = new ZkDataContext();
            var user = Account.AccountByAccountID(db, id);
            return View("AdminUserDetail", user);
        }


        public ActionResult Detail(string id)
        {
            var db = new ZkDataContext();

            int idint;
            Account user = null;
            if (int.TryParse(id, out idint)) user = Account.AccountByAccountID(db, idint);
            if (user == null) user = Account.AccountByName(db, id);
            return View("UserDetail", user);
        }

        public ActionResult Duplicates()
        {
            IEnumerable<Account> ret;

            var db = new ZkDataContext();
            ret =
                db.ExecuteQuery<Account>(
                    "select  * from account where lobbyid in (select lobbyid from (select lobbyid, count(*)  as cnt from account group by (lobbyid)) as lc where cnt > 1) and LobbyID is not null order by lobbyid");
            ret =
                ret.Union(
                    db.ExecuteQuery<Account>(
                        "select * from account where name in (select name from (select name, count(*)  as cnt from account where lobbyid is not null group by (name)) as lc where cnt > 1) order by name"));
            return View(ret);
        }

        public ActionResult Index(string name, string alias, string ip, int? userID = null)
        {
            var db = new ZkDataContext();
            IQueryable<Account> ret = db.Accounts.AsQueryable();

            if (!string.IsNullOrEmpty(name)) ret = ret.Where(x => x.Name.Contains(name));
            if (!string.IsNullOrEmpty(alias)) ret = ret.Where(x => x.Aliases.Contains(alias));
            if (!string.IsNullOrEmpty(ip)) ret = ret.Where(x => x.AccountIPS.Any(y => y.IP == ip));
            if (userID != null && userID != 0) ret = ret.Where(x => x.AccountUserIDS.Any(y => y.UserID == userID));

            return View("UserList", ret.Take(100));
        }

        [Auth(Role = AuthRole.ZkAdmin)]
        public ActionResult NewUsers(string name, string ip, int? userID = null)
        {
            var db = new ZkDataContext();
            IQueryable<Account> ret = db.Accounts.AsQueryable();

            if (!string.IsNullOrEmpty(name)) ret = ret.Where(x => x.Name.Contains(name));
            if (!string.IsNullOrEmpty(ip)) ret = ret.Where(x => x.AccountIPS.Any(y => y.IP == ip));
            if (userID != null && userID != 0) ret = ret.Where(x => x.AccountUserIDS.Any(y => y.UserID == userID));

            return View("NewUsers", ret.OrderByDescending(x=> x.FirstLogin).Take(100));
        }

        public ActionResult LobbyDetail(string id)
        {
            var db = new ZkDataContext();
            int idint;
            Account user = null;
            if (int.TryParse(id, out idint)) user = Account.AccountByLobbyID(db, idint);
            if (user == null) user = Account.AccountByName(db, id);

            return View("UserDetail", user);
        }

        const int MaxBanHours = 24 * 36525;   // 100 years

        [Auth(Role = AuthRole.ZkAdmin | AuthRole.LobbyAdmin)]
        public ActionResult Punish(int accountID,
                                   string reason,
                                   bool deleteXP,
                                   bool deleteInfluence,
                                   bool banMute,
                                   bool banCommanders,
                                   bool banSite,
                                   bool banLobby,
                                   bool banUnlocks,
                                   bool banForum,
                                   bool segregateHost,
                                   string banIP,
                                   int? banUserID,
                                   double banHours)
        {
            ZkDataContext db = new ZkDataContext();
            Account acc = db.Accounts.Single(x => x.AccountID == accountID);

            if (banHours > MaxBanHours) banHours = MaxBanHours; // todo show some notification 

            Punishment punishment = new Punishment
                             {
                                 Time = DateTime.UtcNow,
                                 Reason = reason,
                                 BanMute = banMute,
                                 BanCommanders = banCommanders,
                                 BanSite = banSite,
                                 BanLobby = banLobby,
                                 BanExpires = DateTime.UtcNow.AddHours(banHours),
                                 BanUnlocks = banUnlocks,
                                 BanIP = banIP,
                                 BanForum = banForum,
                                 SegregateHost = segregateHost,
                                 DeleteXP = deleteXP,
                                 DeleteInfluence = deleteInfluence,
                                 CreatedAccountID = Global.AccountID,
                                 UserID = banUserID
                             };
            acc.PunishmentsByAccountID.Add(punishment);
            db.SubmitChanges();

            Global.Nightwatch.Tas.Extensions.PublishAccountData(acc);

            Global.Nightwatch.Tas.Say(TasClient.SayPlace.Channel, AuthService.ModeratorChannel, string.Format("New penalty for {0} {1}  ", acc.Name, Url.Action("Detail", "Users", new { id = acc.AccountID }, "http")), true);

            return RedirectToAction("Detail", new { id = accountID });
        }

        [Auth]
        public ActionResult ReportToAdmin(int id)
        {
            var db = new ZkDataContext();
            var acc = Account.AccountByAccountID(db, id);
            return View("ReportToAdmin", acc);
        }

        [Auth]
        public ActionResult ReportToAdminSubmit(int accountID, string text)
        {
            var db = new ZkDataContext();
            var acc = Account.AccountByAccountID(db, accountID);
            
            db.AbuseReports.InsertOnSubmit(new AbuseReport()
                                           {
                                               
                                               AccountID = acc.AccountID,
                                               ReporterAccountID = Global.AccountID,
                                               Time = DateTime.UtcNow,
                                               Text = text
                                           });
            db.SubmitAndMergeChanges();

            var str = string.Format("{0} {1} reports abuse by {2} {3} : {4}", Global.Account.Name, Url.Action("Detail", "Users", new { id = Global.AccountID }, "http"), acc.Name, Url.Action("Detail", "Users", new { id = acc.AccountID }, "http"), text);

            Global.Nightwatch.Tas.Say(TasClient.SayPlace.Channel, AuthService.ModeratorChannel, str, true);
            foreach (var u in Global.Nightwatch.Tas.JoinedChannels[AuthService.ModeratorChannel].ChannelUsers)
            {
                Global.Nightwatch.Tas.Ring(u);
            }

            return Content("Thank you. Your issue was reported. Moderators will now look into it.");
        }

        [Auth(Role = AuthRole.LobbyAdmin|AuthRole.ZkAdmin)]
        public ActionResult RemovePunishment(int punishmentID) {
            var db = new ZkDataContext();
            var todel = db.Punishments.First(x => x.PunishmentID == punishmentID);
            db.Punishments.DeleteOnSubmit(todel);
            db.SubmitAndMergeChanges();
            return RedirectToAction("Detail", "Users", new { id = todel.UserID });
        }
    }
}