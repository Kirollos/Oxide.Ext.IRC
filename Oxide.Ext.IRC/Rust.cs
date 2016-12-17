/*
    Copyright 2016 Kirollos

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

    Unless required by applicable law or agreed to in writing, software
    distributed under the License is distributed on an "AS IS" BASIS,
    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    See the License for the specific language governing permissions and
    limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Libraries;
using Oxide.Game.Rust.Libraries.Covalence;
using UnityEngine;
//using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;

using Oxide.Ext.IRC;

namespace Oxide.Plugins
{
    public class RustIRC : CSPlugin
    {
        private Oxide.Ext.IRC.Libraries.IRC irc { get { return Oxide.Ext.IRC.IRCExtension.irc; } }

        private Command cmdlib;

        private void Puts(object data, params object[] args)
        {
            Interface.Oxide.LogInfo(Convert.ToString(data), args);
        }

        class RUST : RustPlugin
        {
            public void SendToChat(string message)
            {
                PrintToChat(message);
            }

            public void SendToChat(BasePlayer p, string message)
            {
                PrintToChat(p, message);
            }
        }
        RUST rustclass;

        public RustIRC()
        {
            rustclass = new RUST();
        }

        [HookMethod("Init")]
        private void Init()
        {
            
        }

        [HookMethod("OnServerInitialized")]
        private void OnServerInitialized()
        {
            irc.lang = Interface.Oxide.GetLibrary<Core.Libraries.Lang>();
            irc.lang.RegisterMessages(irc.langs, this);
            cmdlib = Interface.Oxide.GetLibrary<Command>();
            cmdlib.AddChatCommand("ircpm", this, "CommandIRCPM");
            cmdlib.AddConsoleCommand("irc.raw", this, "ConsoleIRCRAW");
            cmdlib.AddConsoleCommand("irc.restart", this, "ConsoleIRCRestart");
            if (irc.connected && irc.registered)
            {
                //irc.Broadcast("Rust server has successfully initialized.");
                irc.Broadcast(IRCColour.Translate("RUST_OnInitMsg"));
            }
        }

        [HookMethod("OnPlayerChat")]
        void OnPlayerChat(ConsoleSystem.Arg arg)
        {
            /*foreach(var channel in irc.Channels)
            {
                irc.Send("PRIVMSG " + channel.name + " :[CHAT]: " + arg.Player().displayName + ": " + arg.ArgsStr);
            }*/
            if (Interface.Oxide.RootPluginManager.GetPlugin("BetterChat") == null)
                irc.Broadcast("[CHAT]: " + arg.Player().displayName + ": " + arg.ArgsStr);
            else
            {
                string msg = (string)Interface.Oxide.RootPluginManager.GetPlugin("BetterChat").Call("API_GetFormatedMessage", arg.Player().UserIDString, arg.ArgsStr, false);
                Regex colorregex = new Regex(@"<color=#(?<code>[a-fA-F0-9]{6})>", RegexOptions.ExplicitCapture);
                while(colorregex.IsMatch(msg) == true)
                {
                    Match regmatch = colorregex.Match(msg);
                    string hex = regmatch.Groups["code"].Value;
                    int irccode = Oxide.Ext.IRC.IRCColour.GetColorFromHex(hex) ?? 1;
                    string szirccode = String.Format("{0}{1:D2}", Convert.ToChar(3), irccode);
                    if (!msg.Substring(msg.IndexOf(regmatch.Value)).StartsWith(regmatch.Value + arg.Player().displayName))
                        msg = msg.Replace(regmatch.Value, szirccode);
                    else
                        msg = msg.Replace(regmatch.Value, "");
                }
                while(msg.Contains("</color>"))
                {
                    msg = msg.Replace("</color>", String.Format("{0}", Convert.ToChar(3)));
                }
                Regex otherregex = new Regex(@"(<.[^>]+>)", RegexOptions.ExplicitCapture);
                while (otherregex.IsMatch(msg) == true)
                {
                    Match regmatch = otherregex.Match(msg);
                    msg = msg.Replace(regmatch.Value, "");
                }
                irc.Broadcast("[CHAT] " + msg);
            }
        }

        [HookMethod("OnPlayerInit")]
        void OnPlayerInit(BasePlayer player)
        {
            /*foreach (var channel in irc.Channels)
            {
                irc.Send("PRIVMSG " + channel.name + " :[CONNECT]: " + player.displayName + " has connected!" + ( channel.adminchan ? " (IP: "+player.net.connection.ipaddress.Substring(player.net.connection.ipaddress.IndexOf(":"))+", SID: "+ player.UserIDString +")" : ""));
            }*/
            //irc.Broadcast("[CONNECT]: " + player.displayName + " has connected!");
            //irc.BroadcastAdmin("[CONNECT]: " + player.displayName + " has connected! (IP: " + player.net.connection.ipaddress.Substring(0, player.net.connection.ipaddress.IndexOf(":")) + ", SID64: " + player.UserIDString + ")");
            irc.Broadcast(IRCColour.Translate("RUST_OnPlayerInit", new Dictionary<string, string>()
            {
                { "playername", player.displayName }
            }));
            irc.BroadcastAdmin(IRCColour.Translate("RUST_OnPlayerInitAdmin", new Dictionary<string, string>()
            {
                { "playername", player.displayName },
                { "playerip",  player.net.connection.ipaddress.Substring(0, player.net.connection.ipaddress.IndexOf(":"))},
                { "playersteamid", player.UserIDString }
            }));
        }

        [HookMethod("OnPlayerDisconnected")]
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            /*foreach (var channel in irc.Channels)
            {
                irc.Send("PRIVMSG " + channel.name + " :[DISCONNECT]: " + player.displayName + " has disconnected! (" + reason + ")");
            }*/
            //irc.Broadcast("[DISCONNECT]: " + player.displayName + " has disconnected! (" + reason + ")");
            irc.Broadcast(IRCColour.Translate("RUST_OnPlayerDisconnect", new Dictionary<string, string>()
            {
                { "playername", player.displayName },
                { "reason", reason }
            }));
        }

        /*void OnEntityDeath(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null) return;

            BasePlayer v = victim.ToPlayer();
            BasePlayer a = victim.lastAttacker?.ToPlayer() ?? null;

            string reason = info?.Weapon?.GetItem()?.info?.displayName?.english ?? info?.WeaponPrefab?.name.Split('/').Last().Replace(".prefab", "").Replace(".entity", "").Replace(".weapon", "").Replace(".deployed", "").Replace("_", " ").Replace(".", "") ?? "No Weapon";

            foreach (var channel in irc.Channels)
            {
                irc.Say(channel.name, "[DEATH]: " + v.displayName + " has been killed by "+ (a?.displayName ?? "NA") +"! (" + reason + ")");
            }
            
        }*/

        [HookMethod("OnDeathNotice")] // Deathnote plugin
        private void OnDeathNotice(object obj)
        {
            JObject jobj = (JObject)obj;

            string
                weapon = jobj["weapon"].ToString(),
                victim = jobj["victim"]["name"].ToString(),
                attacker = jobj["attacker"]["name"].ToString(),
                reason = Enum.GetName(typeof(DeathReason), Convert.ToInt32(jobj["reason"].ToString())) ?? "Unknown"
                ;
            float distance = (float)Math.Round(Convert.ToSingle(jobj["distance"]));

            if (reason == "AnimalDeath") return; // On request
            
            string part1 = "";
            string part2 = "";
            string mReason = reason != "Unknown" ? "reason: " + reason : "";
            string mWeapon = weapon != "No Weapon" ? "weapon: " + weapon : "";
            string mDistance = distance > 0 ? "distance: " + distance : "";
            if (victim == attacker || attacker == "No Attacker")
            {
                part1 = "[DEATH]: " + victim + " has been killed!";
            }
            else
            {
                part1 = "[DEATH]: " + victim + " has been killed by " + attacker + "!";
            }
            part2 = "(";
            if (mReason != "")
                part2 += (mReason + ", ");
            if (mWeapon != "")
                part2 += (mWeapon + ", ");
            if (mDistance != "")
                part2 += (mDistance);
            part2 += ")";
            if (part2.EndsWith(", )"))
                part2 = part2.Remove(part2.LastIndexOf(", "), 2);
            if (part2.Length == 2)
                part2 = "";
            irc.Broadcast(part1 + " " + part2);
        }

        enum DeathReason{Turret,Helicopter,HelicopterDeath,Structure,Trap,Animal,AnimalDeath,Generic,Hunger,Thirst,Cold,Drowned,Heat,Bleeding,Poison,Suicide,Bullet,Slash,Blunt,Fall,Radiation,Stab,Explosion,Unknown};

        ////////////

        public void SendToChat(string message) => rustclass.SendToChat(message);

        public void SendToChat(BasePlayer p, string message) => rustclass.SendToChat(p, message);
        
        
        [HookMethod("CommandIRCPM")]
        private void CommandIRCPM(BasePlayer player, string command, string[] args)
        {
            if(args.Count() < 2)
            {
                SendToChat(player, "[SYNTAX]: /ircpm [nickname] [message]");
                return;
            }
            bool exists = false;
            foreach(var chan in irc.Channels)
            {
                if (chan.userlist.Select(x=>x.Key.ToLower().Trim()).Contains(args[0].ToLower().Trim())) { exists = true; break; }
                exists = false;
            }

            if (!exists)
            {
                SendToChat(player, "[ERROR]: IRC user not connected!");
            }

            if (args[0].ToLower().Trim().EndsWith("serv"))
                return;
            string msg = "";
            for(int i = 1; i < args.Length; i++)
            {
                msg += args[i];
                msg += " ";
            }
            irc.Notice(args[0].Trim(), "[RUST PM] " + player.displayName + ": " + msg.Trim());
        }

        [HookMethod("ConsoleIRCRAW")]
        private void ConsoleIRCRAW(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
            {
                SendToChat(arg.Player(), "Insufficient permission");
                return; 
            }
            if (!arg.HasArgs())
            {
                Interface.Oxide.LogWarning("Usage: ircraw <message>");
                return;
            }
            string message = "";
            foreach(var chunk in arg.Args)
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                message += chunk;
                if (arg.Args.Last() != chunk)
                    message += " ";
            }

            irc.Send(message);
        }

        [HookMethod("ConsoleIRCRestart")]
        private void ConsoleIRCRestart(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
            {
                SendToChat(arg.Player(), "Insufficient permission");
                return;
            }
            // TODO: Needs proper re-write
            irc.Disconnect("Restarting");
            irc.Destruct();
            Ext.IRC.IRCExtension.instance.Manager.GetLibrary("IRC").Shutdown();
            Ext.IRC.IRCExtension.instance.Manager.RegisterLibrary("IRC", Ext.IRC.IRCExtension.irc = new Ext.IRC.Libraries.IRC());
        }

    }
}
