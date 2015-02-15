using System.Data.Entity.Migrations;

namespace ZkData.Migrations
{
    sealed class Configuration: DbMigrationsConfiguration<ZkDataContext>
    {
        public Configuration()
        {
            ContextKey = "PlasmaShared.Migrations.Configuration"; // if you change this, you also ahve to change content of __MigrationHistory table in DB
               
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(ZkDataContext db)
        {
            //  This method will be called after migrating to the latest version.

            if (GlobalConst.Mode == ModeType.Local) {
                // fill local DB with some basic test data

                db.MiscVars.AddOrUpdate(x => x.VarName, new MiscVar { VarName = "NightwatchPassword", VarValue = "dummy" }, new MiscVar() { VarName = "GithubHookKey", VarValue = "secret" });

                db.Accounts.AddOrUpdate(x => x.Name,
                    new Account {
                        Name = "test",
                        NewPasswordPlain = "test",
                        IsZeroKAdmin = true,
                        Kudos = 200,
                        Elo = 1700,
                        Level = 50,
                        EloWeight = 2,
                        SpringieLevel = 4,
                        Country = "cz",
                    },
                    new Account() {
                        Name = GlobalConst.NightwatchName,
                        NewPasswordPlain = "dummy",
                        IsBot = true,
                        IsZeroKAdmin = true,
                    }
                 );

                db.ForumCategories.AddOrUpdate(x => x.Title, 
                    new ForumCategory { Title = "General discussion", },
                    new ForumCategory { Title = "News", IsNews = true }, 
                    new ForumCategory { Title = "Maps", IsMaps = true },
                    new ForumCategory { Title = "Battles", IsSpringBattles = true }, 
                    new ForumCategory { Title = "Missions", IsMissions = true },
                    new ForumCategory { Title = "Clans", IsClans = true }, 
                    new ForumCategory { Title = "Planets", IsPlanets = true });

                db.AutohostConfigs.AddOrUpdate(x=>x.Login, new AutohostConfig() {
                    Login = "Springiee",
                    Title = "Local springie test",
                    Password = "dummy",
                    AutoSpawn = true,
                    AutoUpdateRapidTag = "zk:stable",
                    Mod="zk:stable",
                    ClusterNode = "alpha",
                    JoinChannels = "bots",
                    Map = "Dual Icy Run v3",
                    SpringVersion = GlobalConst.DefaultEngineOverride,
                    MaxPlayers = 10,
                }, new AutohostConfig() {
                    Login = "Fungiee",
                    Title = "Local fungicide test",
                    Password = "dummy",
                    AutoSpawn = true,
                    AutoUpdateRapidTag = "zk:stable",
                    Mod="zk:stable",
                    ClusterNode = "alpha",
                    JoinChannels = "bots",
                    Map = "Dual Icy Run v3",
                    SpringVersion = GlobalConst.DefaultEngineOverride,
                    MaxPlayers = 10,
                }, new AutohostConfig() {
                    Login = "Trifliee",
                    Title = "Local triplicator test",
                    Password = "dummy",
                    AutoSpawn = true,
                    AutoUpdateRapidTag = "zk:test",
                    Mod="zk:test",
                    ClusterNode = "alpha",
                    JoinChannels = "bots",
                    Map = "Dual Icy Run v3",
                    SpringVersion = GlobalConst.DefaultEngineOverride,
                    MaxPlayers = 10,
                });

            }
        }
    }
}