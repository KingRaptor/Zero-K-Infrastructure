﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Linq;
using System.Linq;
using System.Security.Principal;

namespace ZkData
{
    partial class Account: IPrincipal, IIdentity
    {
        public static Func<ZkDataContext, int, Account> AccountByAccountID =
            CompiledQuery.Compile<ZkDataContext, int, Account>((db, accountID) => db.Accounts.SingleOrDefault(x => x.AccountID == accountID));

        public static Func<ZkDataContext, int, Account> AccountByLobbyID =
            CompiledQuery.Compile<ZkDataContext, int, Account>((db, lobbyID) => db.Accounts.FirstOrDefault(x => x.LobbyID == lobbyID));

        public static Func<ZkDataContext, string, Account> AccountByName =
            CompiledQuery.Compile<ZkDataContext, string, Account>((db, name) => db.Accounts.FirstOrDefault(x => x.Name == name && x.LobbyID != null));

        public static Func<ZkDataContext, string, string, Account> AccountVerify =
            CompiledQuery.Compile<ZkDataContext, string, string, Account>(
                (db, login, passwordHash) => db.Accounts.FirstOrDefault(x => x.Name == login && x.Password == passwordHash && x.LobbyID != null));

        Dictionary<AutohostMode, GamePreference> preferences;
        public int AvailableXP { get {
            var kudosGained = KudosGained;
            return GetXpForLevel(Level) - AccountUnlocks.Where(x=> !KudosPurchases.Any(y=>y.UnlockID == x.UnlockID)).Sum(x => (int?)(x.Unlock.XpCost*x.Count)) ?? 0;
        } }
        
        public int KudosGained { get { return ContributionsByAccountID.Sum(x=>x.KudosValue); } }
        public int KudosSpent { get { return  KudosPurchases.Sum(x=>x.KudosValue); } }



        public double EffectiveElo { get { return Elo + (GlobalConst.EloWeightMax - EloWeight)*GlobalConst.EloWeightMalusFactor; } }
        public double EloInvWeight { get { return GlobalConst.EloWeightMax + 1 - EloWeight; } }
        public double Effective1v1Elo { get { return Elo1v1Weight > 1 ? Elo1v1 + (GlobalConst.EloWeightMax - Elo1v1Weight)*GlobalConst.EloWeightMalusFactor : 0; } }



        public Dictionary<AutohostMode, GamePreference> Preferences {
            get {
                if (preferences != null) return preferences;
                else {
                    preferences = new Dictionary<AutohostMode, GamePreference>();
                    if (!string.IsNullOrEmpty(GamePreferences)) {
                        foreach (string line in GamePreferences.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                            string[] parts = line.Split('=');

                            var mode = (AutohostMode)int.Parse(parts[0]);
                            var preference = (GamePreference)int.Parse(parts[1]);
                            if (Enum.IsDefined(typeof(AutohostMode), mode) && Enum.IsDefined(typeof(GamePreference), preference)) preferences[mode] = preference;
                        }
                    }
                    foreach (AutohostMode v in Enum.GetValues(typeof(AutohostMode))) if (!preferences.ContainsKey(v)) preferences[v] = v != AutohostMode.Game1v1 ? GamePreference.Like : GamePreference.Ok;
                    if (preferences.Where(x => x.Key != AutohostMode.None).All(x => x.Value == GamePreference.Never)) foreach (var p in preferences.ToList()) preferences[p.Key] = p.Key != AutohostMode.Game1v1 ? GamePreference.Like : GamePreference.Ok; ;
                }
                return preferences;
            }
        }

        #region IIdentity Members

        public string AuthenticationType { get { return "LobbyServer"; } }
        public bool IsAuthenticated { get { return true; } }

        #endregion

        #region IPrincipal Members

        public bool IsInRole(string role) {
            if (role == "LobbyAdmin") return IsLobbyAdministrator;
            if (role == "ZkAdmin") return IsZeroKAdmin;
            else return string.IsNullOrEmpty(role);
        }

        public IIdentity Identity { get { return this; } }

        #endregion

        public override string ToString() {
            return Name;
        }

        public static double AdjustEloWeight(double currentWeight, double sumWeight, int sumCount) {
            if (currentWeight < GlobalConst.EloWeightMax) {
                currentWeight = (currentWeight + ((sumWeight - currentWeight)/(sumCount - 1))/GlobalConst.EloWeightLearnFactor);
                if (currentWeight > GlobalConst.EloWeightMax) currentWeight = GlobalConst.EloWeightMax;
            }
            return currentWeight;
        }

        public double GetWarpAvailable() {
            if (Faction != null) return Math.Min(GetWarpQuota(), Faction.Warps);
            else return 0;
        }


        public double GetMetalAvailable() {
            if (Faction != null) return Math.Min(GetMetalQuota(), Faction.Metal);
            else return 0;
        }

        public double GetDropshipsAvailable() {
            if (Faction != null) {
                double q = GetDropshipQuota();
                if (q < 1 && !Faction.PlanetFactions.Any(x => x.Dropships > 0)) q = 1;
                return Math.Min(q, Faction.Dropships);
            }
            else return 0;
        }

        public double GetBombersAvailable() {
            if (Faction != null) return Math.Min(GetBomberQuota(), Faction.Bombers);
            else return 0;
        }

        public bool CanSetPriority(PlanetStructure ps) {
            if (Faction == null || ps.Account == null) return false;
            if (ps.Account.FactionID == FactionID && HasFactionRight(x => x.RightSetEnergyPriority)) return true;
            if (ClanID != null && ps.Account.ClanID == ClanID && HasClanRight(x => x.RightSetEnergyPriority)) return true;
            return false;
        }

        public bool CanSetStructureTarget(PlanetStructure ps) {
            if (Faction == null || ps.Account == null) return false;
            if (ps.OwnerAccountID == AccountID || ps.Planet.OwnerAccountID == AccountID) return true; // owner of planet or owner of structure
            if (ps.Account.FactionID == FactionID && HasFactionRight(x => x.RightDropshipQuota > 0)) return true;
            return false;
        }


        public bool HasClanRight(Func<RoleType, bool> test) {
            return AccountRolesByAccountID.Where(x => x.RoleType.IsClanOnly).Select(x => x.RoleType).Any(test);
        }

        public bool HasFactionRight(Func<RoleType, bool> test) {
            return AccountRolesByAccountID.Where(x => !x.RoleType.IsClanOnly).Select(x => x.RoleType).Any(test);
        }


        public double GetDropshipQuota() {
            if (PwDropshipsProduced < PwDropshipsUsed) PwDropshipsUsed = PwDropshipsProduced;

            return GetQuota(x => x.PwDropshipsProduced, x => x.PwDropshipsUsed, x => x.RightDropshipQuota, x => x.Dropships);
        }

        public double GetMetalQuota() {
            if (PwMetalProduced < PwMetalUsed) PwMetalUsed = PwMetalProduced;

            return GetQuota(x => x.PwMetalProduced, x => x.PwMetalUsed, x => x.RightMetalQuota, x => x.Metal);
        }

        public double GetBomberQuota() {
            if (PwBombersProduced < PwBombersUsed) PwBombersUsed = PwBombersProduced;

            return GetQuota(x => x.PwBombersProduced, x => x.PwBombersUsed, x => x.RightBomberQuota, x => x.Bombers);
        }

        public double GetWarpQuota() {
            if (PwWarpProduced < PwWarpUsed) PwWarpUsed = PwWarpProduced;

            return GetQuota(x => x.PwWarpProduced, x => x.PwWarpUsed, x => x.RightWarpQuota, x => x.Warps);
        }


        public void SpendDropships(double count) {
            PwDropshipsUsed += count;
            Faction.Dropships -= count;

            if (PwDropshipsProduced < PwDropshipsUsed) PwDropshipsUsed = PwDropshipsProduced;
        }

        public void ProduceDropships(double count) {
            PwDropshipsProduced += count;
            Faction.Dropships += count;

            if (PwDropshipsProduced < PwDropshipsUsed) PwDropshipsUsed = PwDropshipsProduced;
        }


        public void SpendMetal(double count) {
            PwMetalUsed += count;
            Faction.Metal -= count;

            if (PwMetalUsed > PwMetalProduced) PwMetalUsed = PwMetalProduced;
        }

        public void ProduceMetal(double count) {
            PwMetalProduced += count;
            Faction.Metal += count;

            if (PwMetalUsed > PwMetalProduced) PwMetalUsed = PwMetalProduced;
        }

        public void SpendBombers(double count) {
            PwBombersUsed += count;
            Faction.Bombers -= count;

            if (PwBombersUsed > PwBombersProduced) PwBombersUsed = PwBombersProduced;
        }

        public void ProduceBombers(double count) {
            PwBombersProduced += count;
            Faction.Bombers += count;

            if (PwBombersUsed > PwBombersProduced) PwBombersUsed = PwBombersProduced;
        }

        public void SpendWarps(double count) {
            PwWarpUsed += count;
            Faction.Warps -= count;

            if (PwWarpUsed > PwWarpProduced) PwWarpUsed = PwWarpProduced;
        }

        public void ProduceWarps(double count) {
            PwWarpProduced += count;
            Faction.Warps += count;

            if (PwWarpUsed > PwWarpProduced) PwWarpUsed = PwWarpProduced;
        }

        public double GetQuota(Func<Account, double> producedSelector,
                               Func<Account, double> usedSelector,
                               Func<RoleType, double?> quotaSelector,
                               Func<Faction, double> factionResources) {
            double total = producedSelector(this) - usedSelector(this);
            if (total < 0) total = 0;

            if (Faction != null) {
                double clanQratio = AccountRolesByAccountID.Where(x => x.RoleType.IsClanOnly).Select(x => x.RoleType).Max(quotaSelector) ?? 0;
                double factionQratio = AccountRolesByAccountID.Where(x => !x.RoleType.IsClanOnly).Select(x => x.RoleType).Max(quotaSelector) ?? 0;

                double facRes = factionResources(Faction);

                if (factionQratio >= clanQratio) total += facRes*factionQratio;
                else {
                    if (clanQratio > 0 && Clan != null) {
                        double sumClanProd = Clan.Accounts.Sum(producedSelector);
                        double sumFacProd = Faction.Accounts.Sum(producedSelector);
                        if (sumFacProd > 0) {
                            double clanRes = facRes*sumClanProd/sumFacProd;
                            total += clanRes*clanQratio + (facRes - clanRes)*factionQratio;
                        }
                    }
                }
            }

            return Math.Floor(total);
        }


        public void SetPreferences(Dictionary<AutohostMode, GamePreference> data) {
            string str = "";
            foreach (var kvp in data) str += string.Format("{0}={1}\n", (int)kvp.Key, (int)kvp.Value);
            GamePreferences = str;
            preferences = null;
        }


        public void CheckLevelUp() {
            if (XP > GetXpForLevel(Level + 1)) Level++;
        }


        public IEnumerable<Poll> ValidPolls(ZkDataContext db = null) {
            if (db == null) db = new ZkDataContext();
            return
                db.Polls.Where(
                    x =>
                    (x.ExpireBy == null || x.ExpireBy > DateTime.UtcNow) && (x.RestrictClanID == null || x.RestrictClanID == ClanID) &&
                    (x.RestrictFactionID == null || x.RestrictFactionID == FactionID));
        }


        public int GetDropshipCapacity() {
            if (Faction == null) return 0;
            return GlobalConst.DefaultDropshipCapacity +
                   (Faction.Planets.SelectMany(x => x.PlanetStructures).Where(x => x.IsActive).Sum(x => x.StructureType.EffectDropshipCapacity) ?? 0);
        }

        public int GetBomberCapacity() {
            if (Faction == null) return 0;
            return GlobalConst.DefaultBomberCapacity +
                   (Faction.Planets.SelectMany(x => x.PlanetStructures).Where(x => x.IsActive).Sum(x => x.StructureType.EffectBomberCapacity) ?? 0);
        }


        public static int GetXpForLevel(int level) {
            if (level < 0) return 0;
            return level*80 + 20*level*level;
        }


        partial void OnCreated() {
            FirstLogin = DateTime.UtcNow;
            Elo = 1500;
            Elo1v1 = 1500;
            EloWeight = 1;
            Elo1v1Weight = 1;
            SpringieLevel = 1;
        }

        public bool CanAppoint(Account targetAccount, RoleType roleType) {
            if (targetAccount.AccountID != AccountID && targetAccount.FactionID == FactionID &&
                (!roleType.IsClanOnly || targetAccount.ClanID == ClanID) &&
                (roleType.RestrictFactionID == null || roleType.RestrictFactionID == FactionID)) {
                return
                    AccountRolesByAccountID.Any(
                        x => x.RoleType.RoleTypeHierarchiesByMasterRoleTypeID.Any(y => y.CanAppoint && y.SlaveRoleTypeID == roleType.RoleTypeID));
            }
            else return false;
        }

        public bool CanRecall(Account targetAccount, RoleType roleType) {
            if (targetAccount.AccountID != AccountID && targetAccount.FactionID == FactionID &&
                (!roleType.IsClanOnly || targetAccount.ClanID == ClanID)) {
                return
                    AccountRolesByAccountID.Any(
                        x => x.RoleType.RoleTypeHierarchiesByMasterRoleTypeID.Any(y => y.CanRecall && y.SlaveRoleTypeID == roleType.RoleTypeID));
            }
            else return false;
        }

        public bool CanVoteRecall(Account targetAccount, RoleType roleType) {
            if (roleType.IsVoteable && targetAccount.FactionID == FactionID && (!roleType.IsClanOnly || targetAccount.ClanID == ClanID)) return true;
            else return false;
        }


        partial void OnGamePreferencesChanged() {
            preferences = null;
        }

        partial void OnNameChanging(string value) {
            if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(value)) {
                List<string> aliases = null;
                if (!string.IsNullOrEmpty(Aliases)) aliases = new List<string>(Aliases.Split(','));
                else aliases = new List<string>();

                if (!aliases.Contains(Name)) aliases.Add(Name);
                Aliases = string.Join(",", aliases.ToArray());
            }
        }


        partial void OnValidate(ChangeAction action) {
            if (action == ChangeAction.Update || action == ChangeAction.Insert) {
                if (string.IsNullOrEmpty(Avatar)) {
                    var rand = new Random();
                    List<Avatar> avatars = ZkData.Avatar.GetCachedList();
                    if (avatars.Any()) Avatar = avatars[rand.Next(avatars.Count)].AvatarName;
                }
            }
        }

        partial void OnXPChanged() {
            CheckLevelUp();
        }


        /// <summary>
        /// Todo distribute among clan and faction members
        /// </summary>
        public void ResetQuotas() {
            PwBombersProduced = 0;
            PwBombersUsed = 0;
            PwDropshipsProduced = 0;
            PwDropshipsUsed = 0;
            PwMetalProduced = 0;
            PwMetalUsed = 0;
        }
    }

    public enum GamePreference
    {
        [Description("Never")]
        Never = -2,
        [Description("Ok")]
        Ok = -1,
        [Description("Like")]
        Like = 0,
        [Description("Best")]
        Best = 1
    }

    public class UserLanguageNoteAttribute: Attribute
    {
        protected String note;

        public UserLanguageNoteAttribute(string note) {
            this.note = note;
        }

        public String Note { get { return note; } }
    }

    public enum UserLanguage
    {
        [Description("Auto, let the system decide")]
        auto,
        [Description("English")]
        en,
        [UserLanguageNote(
            "В текущий момент ведется локализация материалов сайта на русский язык, с целью помочь начинающим игрокам и сделать игру доступнее!<br /><a href='/Wiki/Localization'>Присоединяйся</a>"
            )]
        [Description("Йа креведко!")]
        ru
    }
}