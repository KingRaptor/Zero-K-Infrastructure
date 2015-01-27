﻿using System.Reflection;
using PlasmaShared.LobbyMessages;
using ZkData;

namespace ZkLobbyServer
{
    public class SharedServerState
    {
        public string Version { get; private set; }
        public string Engine { get; set; }
        public string Game { get; set; }
        public int ClientCounter;
        public CommandJsonSerializer Serializer = new CommandJsonSerializer();

        public SharedServerState()
        {
            var entry = Assembly.GetEntryAssembly();
            Version = entry.GetName().Version.ToString();
            Engine = GlobalConst.DefaultEngineOverride;
            Game = "zk:stable";
        }
    }
}