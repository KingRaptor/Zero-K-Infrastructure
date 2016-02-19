﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ZkData
{
    public class Secrets
    {
        static string GetVarValue(ZkDataContext db, string key)
        {
            if (db == null) db = new ZkDataContext();
            return db.MiscVars.Where(x => x.VarName == key).Select(x => x.VarValue).FirstOrDefault();
        }

        public string GetNightwatchPassword(ZkDataContext db = null)
        {
            return GetVarValue(db, "NightwatchPassword");
        }

        public string GetSteamWebApiKey(ZkDataContext db = null)
        {
            return GetVarValue(db, "SteamWebApiKey");
        }

        public string GetGithubHookKey(ZkDataContext db = null)
        {
            return GetVarValue(db, "GithubHookKey");
        }

        public string GetSteamBuildPassword(ZkDataContext db = null)
        {
            return GetVarValue(db, "SteamBuildPassword");
        }


    }
}
