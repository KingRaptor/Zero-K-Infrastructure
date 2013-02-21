﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Transactions;
using System.Web.Mvc;
using ZeroKWeb.SpringieInterface;
using ZkData;

namespace ZeroKWeb.Controllers
{
	public class MyController: Controller
	{
		//
		// GET: /My/
		[Auth]
		public ActionResult CommanderProfile(int profileNumber, string name, int? chassis, string deleteCommander)
		{
			if (profileNumber < 1 || profileNumber > GlobalConst.CommanderProfileCount) return Content("WTF! get lost");

			var db = new ZkDataContext();
			using (var scope = new TransactionScope())
			{

				var unlocks = db.AccountUnlocks.Where(x => x.AccountID == Global.AccountID);

				Commander comm = db.Commanders.SingleOrDefault(x => x.ProfileNumber == profileNumber && x.AccountID == Global.AccountID);
				if (comm != null)
				{
					if (!string.IsNullOrEmpty(deleteCommander)) // delete commander
					{
						db.Commanders.DeleteOnSubmit(comm);
						db.SubmitChanges();
						scope.Complete();
						return GetCommanderProfileView(db, profileNumber);
					}
				}
				else
				{
					comm = new Commander() { AccountID = Global.AccountID, ProfileNumber = profileNumber };
					db.Commanders.InsertOnSubmit(comm);
				}

				if (comm.Unlock == null)
				{
					var chassisUnlock = unlocks.FirstOrDefault(x => x.UnlockID == chassis);
					if ((chassis == null || chassisUnlock == null)) return GetCommanderProfileView(db, profileNumber);
					else
					{
						comm.ChassisUnlockID = chassis.Value;
						comm.Unlock = chassisUnlock.Unlock;
					}
				}

				if (!string.IsNullOrEmpty(name)) comm.Name = name;

                // process modules
				foreach (var key in Request.Form.AllKeys.Where(x => !string.IsNullOrEmpty(x)))
				{
					var m = Regex.Match(key, "m([0-9]+)");
					if (m.Success)
					{
						var slotId = int.Parse(m.Groups[1].Value);
						int unlockId;
						int.TryParse(Request.Form[key], out unlockId);

						if (unlockId > 0)
						{
							CommanderSlot slot = db.CommanderSlots.Single(x => x.CommanderSlotID == slotId);
							Unlock unlock = db.Unlocks.Single(x => x.UnlockID == unlockId);

							if (!unlocks.Any(x => x.UnlockID == unlock.UnlockID)) return Content("WTF get lost!");
							if (slot.MorphLevel < unlock.MorphLevel) return Content(string.Format("WTF cannot use {0} in slot {1}", unlock.Name, slot.CommanderSlotID));
							if (!string.IsNullOrEmpty(unlock.LimitForChassis))
							{
								var validChassis = unlock.LimitForChassis.Split(',');
								if (!validChassis.Contains(comm.Unlock.Code)) return Content(string.Format("{0} cannot be used in commander {1}", unlock.Name, comm.Unlock.Name));
							}

							var comSlot = comm.CommanderModules.SingleOrDefault(x => x.SlotID == slotId);
							if (comSlot == null)
							{
								comSlot = new CommanderModule() { SlotID = slotId };
								comm.CommanderModules.Add(comSlot);
							}
							comSlot.ModuleUnlockID = unlockId;
						}
						else
						{
							var oldModule = comm.CommanderModules.FirstOrDefault(x => x.SlotID == slotId);
							if (oldModule != null) comm.CommanderModules.Remove(oldModule);
						}
					}
				}

                // process decorations
                foreach (var key in Request.Form.AllKeys.Where(x => !string.IsNullOrEmpty(x)))
                {
                    var d = Regex.Match(key, "d([0-9]+)");
                    if (d.Success)
                    {
                        var slotId = int.Parse(d.Groups[1].Value);
                        int unlockId;
                        int.TryParse(Request.Form[key], out unlockId);

                        if (unlockId > 0)
                        {
                            CommanderDecorationSlot decSlot = db.CommanderDecorationSlots.Single(x => x.CommanderDecorationSlotID == slotId);
                            Unlock unlock = db.Unlocks.Single(x => x.UnlockID == unlockId);

                            if (!unlocks.Any(x => x.UnlockID == unlock.UnlockID)) return Content("WTF get lost!");
                            if (!string.IsNullOrEmpty(unlock.LimitForChassis))
                            {
                                var validChassis = unlock.LimitForChassis.Split(',');
                                if (!validChassis.Contains(comm.Unlock.Code)) return Content(string.Format("{0} cannot be used in commander {1}", unlock.Name, comm.Unlock.Name));
                            }

                            var comSlot = comm.CommanderDecorations.SingleOrDefault(x => x.SlotID == slotId);
                            if (comSlot == null)
                            {
                                comSlot = new CommanderDecoration() { SlotID = slotId };
                                comm.CommanderDecorations.Add(comSlot);
                            }
                            comSlot.DecorationUnlockID = unlockId;
                        }
                        else
                        {
                            var oldDecoration = comm.CommanderDecorations.FirstOrDefault(x => x.SlotID == slotId);
                            if (oldDecoration != null) comm.CommanderDecorations.Remove(oldDecoration);
                        }
                    }
                }

                // remove a module/decoration if ordered to
                foreach (var toDel in Request.Form.AllKeys.Where(x => !string.IsNullOrEmpty(x)))
                {
					var m = Regex.Match(toDel, "deleteSlot([0-9]+)");
					if (m.Success)
					{
						var slotId = int.Parse(m.Groups[1].Value);
						comm.CommanderModules.Remove(comm.CommanderModules.SingleOrDefault(x => x.SlotID == slotId));
					}
                
                    var d = Regex.Match(toDel, "deleteDecorationSlot([0-9]+)");
                    if (d.Success)
                    {
                        var decSlotId = int.Parse(d.Groups[1].Value);
                        comm.CommanderDecorations.Remove(comm.CommanderDecorations.SingleOrDefault(x => x.SlotID == decSlotId));
                    }
                }

				db.SubmitChanges();
				foreach (var unlock in comm.CommanderModules.GroupBy(x => x.Unlock))
				{
					if (unlock.Key == null) continue;
					var owned = unlocks.Where(x => x.UnlockID == unlock.Key.UnlockID).Sum(x => (int?)x.Count) ?? 0;
					if (owned < unlock.Count())
					{
						var toRemove = unlock.Count() - owned;

						foreach (var m in unlock.OrderByDescending(x => x.SlotID))
						{
							db.CommanderModules.DeleteOnSubmit(m);
							//comm.CommanderModules.Remove(m);
							toRemove--;
							if (toRemove <= 0) break;
						}
					}
				}

				db.SubmitChanges();
				scope.Complete();
			}

			return GetCommanderProfileView(db, profileNumber);
		}

		[Auth]
		public ActionResult Commanders()
		{
			var db = new ZkDataContext();

			var ret = new CommandersModel();
			ret.Unlocks =
				Global.Account.AccountUnlocks.Where(
					x => x.Unlock.UnlockType != UnlockTypes.Unit).ToList
					();
			return View(ret);
		}

		public ActionResult Index()
		{
			return RedirectToAction("Detail", "Users", new { id = Global.AccountID });
		}


		public ActionResult Reset()
		{
			var db = new ZkDataContext();
			db.AccountUnlocks.DeleteAllOnSubmit(db.AccountUnlocks.Where(x => x.AccountID == Global.AccountID));
			db.SubmitChanges();
			return RedirectToAction("UnlockList");
		}

        [Auth]
        public ActionResult Unlock(int id, bool useKudos = false)
        {
            using (var db = new ZkDataContext())
            using (var scope = new TransactionScope())
            {

                List<Unlock> unlocks;
                List<Unlock> future;

                GetUnlockLists(db, out unlocks, out future);

                if (unlocks.Any(x => x.UnlockID == id))
                {
                    Unlock unlock = db.Unlocks.FirstOrDefault(x => x.UnlockID == id);
                    if (!useKudos && unlock.IsKudosOnly == true) return Content("That unlock cannot be bought using XP");

                    if (useKudos) {
                        var acc = Account.AccountByAccountID(db, Global.AccountID);
                        if (acc.Kudos < unlock.KudosCost) return Content("Not enough kudos to unlock this");
                        acc.KudosPurchases.Add(new KudosPurchase() {Time = DateTime.UtcNow, Unlock = unlock, Account = acc, KudosValue = unlock.KudosCost??0});
                        db.SubmitAndMergeChanges();
                        acc.Kudos = acc.KudosGained - acc.KudosSpent;
                        db.SubmitAndMergeChanges();
                    }
                    
                    var au = db.AccountUnlocks.SingleOrDefault(x => x.AccountID == Global.AccountID && x.UnlockID == id);
                    if (au == null)
                    {
                        au = new AccountUnlock() { AccountID = Global.AccountID, UnlockID = id, Count = 1 };
                        db.AccountUnlocks.InsertOnSubmit(au);
                    }
                    else au.Count++;
                    db.SubmitAndMergeChanges();
                }
                scope.Complete();
            }
            return RedirectToAction("UnlockList");
        }

		[Auth]
		public ActionResult UnlockList()
		{
			List<Unlock> unlocks;
			List<Unlock> future;
			var db = new ZkDataContext();
			GetUnlockLists(db, out unlocks, out future);

			return View("UnlockList", new UnlockListResult() { Account = Global.Account, Unlocks = unlocks, FutureUnlocks = future });
		}

		PartialViewResult GetCommanderProfileView(ZkDataContext db, int profile)
		{
			var com = db.Commanders.SingleOrDefault(x => x.AccountID == Global.AccountID && x.ProfileNumber == profile);

			return PartialView("CommanderProfile",
			                   new CommanderProfileModel
			                   {
			                   	ProfileID = profile,
			                   	Commander = com,
			                   	Slots = db.CommanderSlots.ToList(),
                                DecorationSlots = db.CommanderDecorationSlots.ToList(),
			                   	Unlocks =
			                   		db.AccountUnlocks.Where(
			                   			x =>
			                   			x.AccountID == Global.AccountID &&
			                   			(x.Unlock.UnlockType != UnlockTypes.Unit)).ToList().Where(
			                   			 	x =>
			                   			 	(com == null || x.Unlock.LimitForChassis == null || x.Unlock.LimitForChassis.Contains(com.Unlock.Code)) &&
			                   			 	(com == null || x.Count > com.CommanderModules.Count(y => y.ModuleUnlockID == x.UnlockID)) &&
                                            (com == null || x.Count > com.CommanderDecorations.Count(y => y.DecorationUnlockID == x.UnlockID))
                                            ).Select(x => x.Unlock).ToList()
			                   });
		}

		void GetUnlockLists(ZkDataContext db, out List<Unlock> unlocks, out List<Unlock> future)
		{
			var maxedUnlockList =
				db.AccountUnlocks.Where(x => x.AccountID == Global.AccountID && x.Count >= x.Unlock.MaxModuleCount).Select(x => x.UnlockID).ToList();
			var anyUnlockList = db.AccountUnlocks.Where(x => x.AccountID == Global.AccountID && x.Count > 0).Select(x => x.UnlockID).ToList();

			var temp =
				db.Unlocks.Where(
					x =>
					x.NeededLevel <= Global.Account.Level && (x.XpCost <= Global.Account.AvailableXP) && !maxedUnlockList.Contains(x.UnlockID) &&
					(x.RequiredUnlockID == null || anyUnlockList.Contains(x.RequiredUnlockID ?? 0))
                    ).OrderBy(x => x.NeededLevel).ThenBy(x => x.XpCost).ThenBy(x => x.UnlockType).ToList();
			unlocks = temp;

			future =
				db.Unlocks.Where(x => !maxedUnlockList.Contains(x.UnlockID) && !temp.Select(y => y.UnlockID).Contains(x.UnlockID)).OrderBy(x => x.NeededLevel).
					ThenBy(x => x.XpCost).ToList();
		}


		public class CommanderProfileModel
		{
			public Commander Commander;
			public int ProfileID;
			public List<CommanderSlot> Slots;
            public List<CommanderDecorationSlot> DecorationSlots;
			public List<Unlock> Unlocks;
		}

		public class CommandersModel
		{
			public List<AccountUnlock> Unlocks;
		}


		public class UnlockListResult
		{
			public Account Account;
			public IEnumerable<Unlock> FutureUnlocks;
			public IEnumerable<Unlock> Unlocks;
		}

        [Auth]
	    public ActionResult GamePreferences(List<GamePreference> preference, string active)
	    {

	        var db = new ZkDataContext();
            var acc = db.Accounts.Single(x => x.AccountID == Global.AccountID);
            
            var keys= acc.Preferences.Where(x=>x.Key != AutohostMode.None).OrderBy(x=>x.Key).Select(x=>x.Key).ToList();
            for (int i = 0; i < keys.Count(); i++) {
                acc.Preferences[keys[i]] = preference[i];
            }
            acc.MatchMakingActive = !string.IsNullOrEmpty(active);
            acc.SetPreferences(acc.Preferences);
            db.SubmitChanges();
            PlayerJuggler.SendAccountConfig(acc);
            return RedirectToAction("Detail", "Users", new { id = acc.AccountID });

	    }
				
		[Auth]
	    public ActionResult LanguageChange(string language)
		{
			var db = new ZkDataContext();
            var acc = db.Accounts.Single(x => x.AccountID == Global.AccountID);
            
            language = language.ToLower();
			acc.Language = language == "auto" ? "" : language;
			db.SubmitChanges();
			
            return RedirectToAction("Detail", "Users", new { id = acc.AccountID });
		}
	}
}