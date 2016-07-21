using System.ComponentModel.DataAnnotations;
using System.Linq;
using PlasmaShared;

namespace ZkData
{
    public class AutohostConfig
    {
        public int AutohostConfigID { get; set; }
        [Required]
        [StringLength(50)]
        public string ClusterNode { get; set; }
        [Required]
        [StringLength(50)]
        public string Login { get; set; }
        [Required]
        [StringLength(50)]
        public string Password { get; set; }
        public int MaxPlayers { get; set; }
        [StringLength(200)]
        public string Welcome { get; set; }
        public bool AutoSpawn { get; set; }
        [StringLength(50)]
        public string AutoUpdateRapidTag { get; set; }
        [StringLength(50)]
        public string SpringVersion { get; set; }
        [StringLength(500)]
        public string CommandLevels { get; set; }
        [StringLength(100)]
        public string Map { get; set; }
        [StringLength(100)]
        public string Mod { get; set; }
        [StringLength(100)]
        public string Title { get; set; }
        [StringLength(100)]
        public string JoinChannels { get; set; }
        [StringLength(50)]
        public string BattlePassword { get; set; }
        public AutohostMode AutohostMode { get; set; }
        public int? MinToStart { get; set; }
        public int? MaxToStart { get; set; }
        public int? MinToJuggle { get; set; }
        public int? MaxToJuggle { get; set; }
        public int? SplitBiggerThan { get; set; }
        public int? MergeSmallerThan { get; set; }
        public int? MaxEloDifference { get; set; }
        public bool? DontMoveManuallyJoined { get; set; }
        public int? MinLevel { get; set; }
        public int? MinElo { get; set; }
        public int? MaxLevel { get; set; }
        public int? MaxElo { get; set; }
        public bool? IsTrollHost { get; set; }

        public AhConfig ToAhConfig()
        {
            var db = this;
            return new AhConfig() {
                Login = db.Login,
                Password = db.Password,
                JoinChannels = (db.JoinChannels + "").Split('\n').Where(x => !string.IsNullOrEmpty(x)).ToArray(),
                Title = db.Title,
                Welcome = db.Welcome,
                Map = db.Map,
                Mod = db.Mod,
                MaxPlayers = db.MaxPlayers,
                AutoSpawnClones = db.AutoSpawn,
                AutoUpdateRapidTag = db.AutoUpdateRapidTag,
                SpringVersion = db.SpringVersion,
                SplitBiggerThan = db.SplitBiggerThan,
                Mode = db.AutohostMode,
                BattlePassword = db.BattlePassword,
                CommandLevels = (db.CommandLevels + "").Split('\n').Where(x => !string.IsNullOrEmpty(x)).Select(x => {
                    var parts = x.Split('=');
                    return new CommandLevel() { Command = parts[0], Level = int.Parse(parts[1]) };
                }).ToArray(),
                MaxEloDifference = db.MaxEloDifference,
                MinToJuggle = db.MinToJuggle,
                MaxToJuggle = db.MaxToJuggle
            };
        }

    }
}
