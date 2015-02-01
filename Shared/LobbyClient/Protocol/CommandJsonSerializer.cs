using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LobbyClient
{
    [Flags]
    public enum Origin
    {
        Server = 1,
        Client = 2
    }
    
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class MessageAttribute: Attribute
    {
        /// <summary>
        /// Overrides name, if unset, classname is used
        /// </summary>
        public string Name { get; set; }

        public Origin Direction { get; set; }

        public MessageAttribute(Origin direction)
        {
            Direction = direction;
        }
    }


    public class CommandJsonSerializer
    {
        readonly Dictionary<string, Type> knownTypes = new Dictionary<string, Type>();
        readonly JsonSerializerSettings settings = new JsonSerializerSettings();

        public CommandJsonSerializer()
        {
            RegisterType<Welcome>();
            RegisterType<Login>();
            RegisterType<LoginResponse>();
            RegisterType<Register>();
            RegisterType<RegisterResponse>();
            RegisterType<JoinChannel>();
            RegisterType<JoinChannelResponse>();
            RegisterType<CreateChannel>();
            RegisterType<CreateRoomResponse>();
            RegisterType<User>();
            RegisterType<ChannelUserAdded>();
            RegisterType<ChannelUserRemoved>();
            RegisterType<Say>();
            RegisterType<UserDisconnected>();
            RegisterType<OpenBattle>();
            RegisterType<BattleAdded>();
            RegisterType<UserBattleStatus>();
            RegisterType<LeftBattle>();
            RegisterType<JoinedBattle>();
            RegisterType<LeftBattle>();
            RegisterType<JoinBattle>();
            RegisterType<LeaveBattle>();
            RegisterType<BattleRemoved>();


            settings.Formatting = Formatting.None;
            settings.NullValueHandling = NullValueHandling.Ignore;
        }


        public object DeserializeLine(string line)
        {
            if (!string.IsNullOrEmpty(line)) {
                string[] parts = line.Split(new[] { " " }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) throw new Exception(string.Format("Invalid json data {0} : {1}", this, line));
                Type type;
                if (!knownTypes.TryGetValue(parts[0], out type)) throw new Exception(string.Format("Invalid json type {0} : {1}", this, parts[0]));
                return JsonConvert.DeserializeObject(parts[1], type);
            }
            return null;
        }

        public void RegisterType<T>()
        {
            Type t = typeof(T);
            knownTypes[t.Name] = t;
        }

        public void RegisterTypes(params Type[] types)
        {
            foreach (Type t in types) knownTypes[t.Name] = t;
        }

        public string SerializeToLine<T>(T value)
        {
            return string.Format("{0} {1}\n", typeof(T).Name, JsonConvert.SerializeObject(value, settings));
        }
    }

   
}