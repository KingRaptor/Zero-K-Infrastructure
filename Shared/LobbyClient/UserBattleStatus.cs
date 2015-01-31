#region using

using System;
using System.Net;
using Newtonsoft.Json;

#endregion

namespace LobbyClient
{
	public enum SyncStatuses
	{
		Unknown = 0,
		Synced = 1,
		Unsynced = 2
	}

	public class UserBattleStatus
	{
		public int AllyNumber;
		public bool IsSpectator;
		public DateTime JoinTime = DateTime.Now;
		public string Name;
		public string ScriptPassword;
		public int Side;
		public SyncStatuses SyncStatus = SyncStatuses.Unknown;
		public User LobbyUser;

		public int TeamNumber;
		
        public IPAddress ip = IPAddress.None;
		public int port;

		public UserBattleStatus() {}


		public UserBattleStatus(string name, User lobbyUser, string scriptPassword = null)
		{
		    Name = name;
			ScriptPassword = scriptPassword;
			LobbyUser = lobbyUser;
		}

		public virtual UserBattleStatus Clone()
		{
			return (UserBattleStatus)MemberwiseClone();
		}

		public bool Equals(UserBattleStatus other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return other.AllyNumber == AllyNumber && Equals(other.ip, ip) && other.IsSpectator.Equals(IsSpectator) &&
			       other.JoinTime.Equals(JoinTime) && Equals(other.Name, Name) && other.port == port && other.Side == Side &&
			       Equals(other.SyncStatus, SyncStatus) &&  other.TeamNumber == TeamNumber;
		}


		public void SetFrom(int status)
		{
			TeamNumber = (status >> 2) & 15;
			AllyNumber = (status >> 6) & 15;
			IsSpectator = (status & 1024) == 0;
			SyncStatus = (SyncStatuses)((status >> 22) & 3);
			Side = (status >> 24) & 15;
		}

		public int ToInt()
		{
			var status = 0;
			status += (TeamNumber & 15) << 2;
			status += (AllyNumber & 15) << 6;
			if (!IsSpectator) status |= 1024;
			status += ((int)SyncStatus & 3) << 22;
			status += (Side & 15) << 24;
			return status;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != typeof(UserBattleStatus)) return false;
			return Equals((UserBattleStatus)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				var result = AllyNumber;
				result = (result*397) ^ (ip != null ? ip.GetHashCode() : 0);
				result = (result*397) ^ IsSpectator.GetHashCode();
				result = (result*397) ^ JoinTime.GetHashCode();
				result = (result*397) ^ (Name != null ? Name.GetHashCode() : 0);
				result = (result*397) ^ port;
				result = (result*397) ^ Side;
				result = (result*397) ^ SyncStatus.GetHashCode();
				result = (result*397) ^ TeamNumber;
				return result;
			}
		}

		public override string ToString()
		{
			return Name;
		}

		public static bool operator ==(UserBattleStatus left, UserBattleStatus right)
		{
			return Equals(left, right);
		}

		public static bool operator !=(UserBattleStatus left, UserBattleStatus right)
		{
			return !Equals(left, right);
		}
	} ;
}