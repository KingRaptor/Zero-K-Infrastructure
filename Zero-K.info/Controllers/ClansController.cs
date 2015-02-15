﻿using System;
using System.Collections.Generic;
using System.Data.Entity.SqlServer;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using ZkData;

namespace ZeroKWeb.Controllers
{
    public class ClansController : Controller
    {
        //
        // GET: /Clans/

        public ActionResult Index()
        {
            var db = new ZkDataContext();

            return View(db.Clans.Where(x => !x.IsDeleted && (x.Faction != null && !x.Faction.IsDeleted)));
        }


        [Auth]
        public ActionResult Create()
        {
            if (Global.Account.Clan == null || Global.Account.HasClanRight(x => x.RightEditTexts)) return View(Global.Clan ?? new Clan() { FactionID = Global.FactionID });
            else return Content("You already have clan and you dont have rights to it");
        }

        /// <summary>
        /// Shows clan page
        /// </summary>
        /// <returns></returns>
        public ActionResult Detail(int id)
        {
            var db = new ZkDataContext();
            var clan = db.Clans.First(x => x.ClanID == id);
            if (Global.ClanID == clan.ClanID)
            {
                if (clan.ForumThread != null)
                {
                    clan.ForumThread.UpdateLastRead(Global.AccountID, false);
                    db.SubmitChanges();
                }
            }
            return View(clan);
        }



        public static Clan PerformLeaveClan(int accountID, ZkDataContext db = null)
        {
            if (db == null) db = new ZkDataContext();
            var acc = db.Accounts.Single(x => x.AccountID == accountID);
            var clan = acc.Clan;
            if (clan.Accounts.Count() > GlobalConst.ClanLeaveLimit) return null; // "This clan is too big to leave";

            RoleType leader = db.RoleTypes.FirstOrDefault(x => x.RightKickPeople && x.IsClanOnly);

            // remove role
            db.AccountRoles.DeleteAllOnSubmit(acc.AccountRolesByAccountID.Where(x => x.RoleType.IsClanOnly).ToList());

            // delete active polls
            db.Polls.DeleteAllOnSubmit(acc.PollsByRoleTargetAccountID);

            // remove planets
            acc.Planets.Clear();

            acc.ResetQuotas();

            // delete channel subscription
            //if (!acc.IsZeroKAdmin || acc.IsZeroKAdmin)
            //{
            //    var channelSub = db.LobbyChannelSubscriptions.FirstOrDefault(x => x.Account == acc && x.Channel == acc.Clan.GetClanChannel());
            //    db.LobbyChannelSubscriptions.DeleteOnSubmit(channelSub);
            //}

            acc.Clan = null;
            db.Events.InsertOnSubmit(Global.CreateEvent("{0} leaves clan {1}", acc, clan));
            db.SubmitChanges();
            if (!clan.Accounts.Any())
            {
                clan.IsDeleted = true;
                db.Events.InsertOnSubmit(Global.CreateEvent("{0} is disbanded", clan));
            }
            else if (acc.AccountRolesByAccountID.Any(x => x.RoleType == leader))  // clan leader
            {
                var otherClanMember = clan.Accounts.FirstOrDefault(x => x.AccountID != accountID);
                if (otherClanMember != null)
                {
                    db.AccountRoles.InsertOnSubmit(new AccountRole() { AccountID = otherClanMember.AccountID, Clan = clan, RoleType = leader, Inauguration = DateTime.UtcNow });
                }
            }

            db.SubmitChanges();
            return clan;
        }


        [Auth]
        public ActionResult LeaveClan()
        {
            var clan = PerformLeaveClan(Global.AccountID);
            if (clan == null) return Content("This clan is too big to leave");
            PlanetwarsController.SetPlanetOwners();

            return RedirectToAction("Index", new { id = clan.ClanID });
        }

        [Auth]
        public ActionResult JoinClan(int id, string password)
        {
            var db = new ZkDataContext();
            var clan = db.Clans.Single(x => x.ClanID == id && !x.IsDeleted);
            if (clan.CanJoin(Global.Account))
            {
                if (!string.IsNullOrEmpty(clan.Password) && clan.Password != password) return View(clan.ClanID);
                else
                {
                    var acc = db.Accounts.Single(x => x.AccountID == Global.AccountID);
                    acc.ClanID = clan.ClanID;
                    acc.FactionID = clan.FactionID;
                    db.Events.InsertOnSubmit(Global.CreateEvent("{0} joins clan {1}", acc, clan));
                    db.SubmitChanges();
                    return RedirectToAction("Detail", new { id = clan.ClanID });
                }
            }
            else return Content("You cannot join this clan - its full, or has password, or is different faction");
        }

        [Auth]
        public ActionResult KickPlayerFromClan(int clanID, int accountID)
        {
            var db = new ZkDataContext();
            var clan = db.Clans.Single(c => clanID == c.ClanID);
            if (!(Global.Account.HasClanRight(x => x.RightKickPeople) && clan.ClanID == Global.Account.ClanID)) return Content("Unauthorized");
            PerformLeaveClan(accountID);
            db.SubmitChanges();
            PlanetwarsController.SetPlanetOwners();
            return RedirectToAction("Detail", new { id = clanID });
        }

        [Auth]
        public ActionResult SubmitCreate(Clan clan, HttpPostedFileBase image, HttpPostedFileBase bgimage, bool noFaction = false)
        {
            //using (var scope = new TransactionScope())
            //{
            ZkDataContext db = new ZkDataContext();
            bool created = clan.ClanID == 0; // existing clan vs creation

            //return Content(noFaction ? "true":"false");
            if (noFaction) clan.FactionID = null;

            var new_faction = db.Factions.SingleOrDefault(x => x.FactionID == clan.FactionID);
            if ((new_faction != null) && new_faction.IsDeleted) return Content("Cannot create clans in deleted factions");

            Clan orgClan = null;

            if (!created)
            {
                if (!Global.Account.HasClanRight(x => x.RightEditTexts) || clan.ClanID != Global.Account.ClanID) return Content("Unauthorized");
                orgClan = db.Clans.Single(x => x.ClanID == clan.ClanID);
                orgClan.ClanName = clan.ClanName;
                orgClan.Shortcut = clan.Shortcut;
                orgClan.Description = clan.Description;
                orgClan.SecretTopic = clan.SecretTopic;
                orgClan.Password = clan.Password;
            }
            else
            {
                if (Global.Clan != null) return Content("You already have a clan");
                // should just change their faction for them?
                if (Global.FactionID != 0 && Global.FactionID != clan.FactionID) return Content("Clan must belong to same faction you are in");
            }
            if (string.IsNullOrEmpty(clan.ClanName) || string.IsNullOrEmpty(clan.Shortcut)) return Content("Name and shortcut cannot be empty!");
            if (!ZkData.Clan.IsShortcutValid(clan.Shortcut)) return Content("Shortcut must have at least 1 characters and contain only numbers and letters");

            // check if our name or shortcut conflicts with existing clans
            // if so, allow us to create a new clan over it if it's a deleted clan, else block action
            var existingClans = db.Clans.Where(x => ((SqlFunctions.PatIndex(clan.Shortcut,x.Shortcut) > 0 || SqlFunctions.PatIndex(clan.ClanName, x.ClanName) > 0) && x.ClanID != clan.ClanID));
            if (existingClans.Count() > 0)
            {
                if (existingClans.Any(x => !x.IsDeleted)) return Content("Clan with this shortcut or name already exists");
                if (created)
                {
                    Clan deadClan = existingClans.First();
                    clan = deadClan;
                    if (noFaction) clan.FactionID = null;
                }
            }
            else if (created) db.Clans.InsertOnSubmit(clan);

            if (created && (image == null || image.ContentLength == 0)) return Content("A clan image is required");
            if (image != null && image.ContentLength > 0)
            {
                var im = Image.FromStream(image.InputStream);
                if (im.Width != 64 || im.Height != 64) im = im.GetResized(64, 64, InterpolationMode.HighQualityBicubic);
                db.SubmitChanges(); // needed to get clan id for image url - stupid way really
                im.Save(Server.MapPath(clan.GetImageUrl()));
            }
            if (bgimage != null && bgimage.ContentLength > 0)
            {
                var im = Image.FromStream(bgimage.InputStream);
                db.SubmitChanges(); // needed to get clan id for image url - stupid way really
                // DW - Actually its not stupid, its required to enforce locking.
                // It would be possbile to enforce a pre-save id
                im.Save(Server.MapPath(clan.GetBGImageUrl()));
            }
            db.SubmitChanges();

            if (created) // we created a new clan, set self as founder and rights
            {
                var acc = db.Accounts.Single(x => x.AccountID == Global.AccountID);
                acc.ClanID = clan.ClanID;

                var leader = db.RoleTypes.FirstOrDefault(x => x.RightKickPeople && x.IsClanOnly);
                if (leader != null)
                    db.AccountRoles.InsertOnSubmit(new AccountRole()
                    {
                        AccountID = acc.AccountID,
                        Clan = clan,
                        RoleType = leader,
                        Inauguration = DateTime.UtcNow
                    });

                db.Events.InsertOnSubmit(Global.CreateEvent("New clan {0} formed by {1}", clan, acc));
            }
            else
            {
                if (clan.FactionID != orgClan.FactionID)   // set factions of members
                {
                    var originalID = orgClan.FactionID;
                    orgClan.FactionID = clan.FactionID;     // <- not possible to change faction // now it is!
                    if (orgClan.Faction != null && orgClan.Faction.IsDeleted)
                    {
                        orgClan.FactionID = originalID;
                        throw new ApplicationException("You cannot join deleted faction");
                    }
                    db.Events.InsertOnSubmit(Global.CreateEvent("Clan {0} moved to faction {1}", clan, orgClan.Faction));

                    db.SubmitAndMergeChanges();

                    foreach (Account member in orgClan.Accounts)
                    {
                        if (member.FactionID != clan.FactionID && member.FactionID != null)
                        {
                            FactionsController.PerformLeaveFaction(member.AccountID, true, db);
                        }
                        member.Faction = orgClan.Faction;
                    }
                }
            }

            db.SubmitChanges();
            //scope.Complete();
            Global.Nightwatch.Tas.AdminSetTopic(clan.GetClanChannel(), clan.SecretTopic);
            Global.Nightwatch.Tas.AdminSetChannelPassword(clan.GetClanChannel(), clan.Password);
            //}
            return RedirectToAction("Detail", new { id = clan.ClanID });
        }

        public ActionResult JsonGetClanList()
        {
            var db = new ZkDataContext();
            return Json(db.Clans.Where(x => !x.IsDeleted).Select(x => new { Name = x.ClanName, ID = x.ClanID, Shortcut = x.Shortcut, Description = x.Description, HasPassword = x.Password != null }).ToList(), JsonRequestBehavior.AllowGet);
        }

        /*
        public ActionResult JsonJoinClan(string login, string password, int clanID)
        {
            var db = new ZkDataContext();
            var clan = db.Clans.First(x => x.ClanID == clanID && x.Password == null);
            var acc = db.Accounts.First(x=>x.AccountID == AuthServiceClient.VerifyAccountPlain(login, password).AccountID);
            PerformLeaveClan(acc.AccountID, db);
            return Json(db.Clans.Where(x => !x.IsDeleted).ToList(), JsonRequestBehavior.AllowGet);
        }*/

    }
}
