/*
    Copyright 2017 Kirollos

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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Libraries;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Oxide.Ext.IRC.Libraries
{
    public class IRC : Library
    {
        private Oxide.Plugins.RustIRC rust { get { return Oxide.Ext.IRC.IRCExtension.rust; } }
        public static IRC instance;

        public Thread thread = null;
        private TcpClient socket = null;
        private Stream stream = null;
        private bool isConnected = false; public bool connected { get { return isConnected; } }
        private bool isRegistered = false; public bool registered { get { return isRegistered; } }

        private readonly DataFileSystem dfs;
        public Core.Libraries.Lang lang;
        Regex parsingRegex = new Regex(@"^(:(?<prefix>\S+) )?(?<command>\S+)( (?!:)(?<params>.+?))?( :(?<trail>.+))?$", RegexOptions.ExplicitCapture);

        private Settings settings;
        public Dictionary<string, string> langs;

        public List<Channel> Channels { get { return settings.channels; } }

        class Settings
        {
            public string host { get; set; }
            public int port { get; set; }
            public string nick { get; set; }
            public string ident { get; set; }
            public string realname { get; set; }
            public string ns_password { get; set; }
            public string commandprefix { get; set; }
            public List<Channel> channels { get; set; }
        }

        public class Channel
        {
            public string name { get; set; }
            public string key { get; set; }
            public bool adminchan { get; set; }
            public bool lobby { get; set; }
            [JsonIgnore]
            public Dictionary<string, string> userlist = new Dictionary<string, string>();
            [JsonIgnore]
            public bool __NAMES;
        }

        public Channel GetChan(string name)
        {
            foreach (Channel chan in settings.channels)
            {
                if (chan.name.ToLower().Trim() == name.ToLower().Trim())
                    return chan;
            }
            return null;
        }

        private Settings defaultsettings()
        {
            return new Settings()
            {
                host = "irc.example.net",
                port = 6667,
                nick = "RustBot",
                ident = "Rust",
                realname = "Rust Test Bot",
                ns_password = "",
                commandprefix = "!",
                channels = new List<Channel>() { new Channel() { name = "#RustIRC", key = "", adminchan = false , lobby = false} }
            };
        }

        private bool toghost = false;

        public IRC()
        {
            instance = this;
            counterrs = 0;
            dfs = Interface.Oxide.DataFileSystem;
            langs = new Dictionary<string, string>()
            {
                {"IRC_PlayersResponse", "Connected Players [{count}/{maxplayers}]: {playerslist}"},
                {
                    "RUST_OnInitMsg",
                    "{irccolor:lred}{ircbold}Rust server has successfully initialized.{ircbold}{irccolor}"
                },
                {"RUST_OnPlayerInit", "[CONNECT]: {playername} has connected!"},
                {
                    "RUST_OnPlayerInitAdmin",
                    "[CONNECT]: {playername} has connected! (IP: {playerip}, SID64: {playersteamid})"
                },
                {
                    "RUST_OnPlayerDisconnect",
                    "[DISCONNECT]: {playername} has disconnected! ({irccolor:orange}{ircbold}{reason}{ircbold}{irccolor})"
                },
                //{ "RUST_OnBetterChat", "[CHAT] {FormattedTitle} {PlayerName}[{ID}]: {Message}" }
            };
            try
            {
                settings = dfs.ReadObject<Settings>(Path.Combine(Interface.Oxide.ConfigDirectory, "IRC"));
                //langs = dfs.ReadObject<Dictionary<string, string>>(Path.Combine(Interface.Oxide.LangDirectory, "IRC"));
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogException("Error reading config!", ex);
                return;
            }
            if (String.IsNullOrEmpty(settings.host) || settings.host == null)
            {
                settings = defaultsettings();
                dfs.WriteObject<Settings>(Path.Combine(Interface.Oxide.ConfigDirectory, "IRC"), settings, true);
            }
            /*if(langs.Count == 0)
            {
                langs = new Dictionary<string, string>()
                {
                    { "IRC_PlayersResponse", "Connected Players [{count}/{maxplayers}: {playerslist}" },
                    { "RUST_OnInitMsg", "{irccolor:lred}{ircbold}Rust server has successfully initialized.{ircbold}{irccolor}" },
                    { "RUST_OnPlayerInit", "[CONNECT]: {playername} has connected!" },
                    { "RUST_OnPlayerInitAdmin", "[CONNECT]: {playername} has connected! (IP: {playerip}, SID64: {playersteamid})" },
                    { "RUST_OnPlayerDisconnect", "[DISCONNECT]: {playername} has disconnected! ({irccolor:orange}{ircbold}{reason}{ircbold}{irccolor})" },
                    //{ "RUST_OnBetterChat", "[CHAT] {FormattedTitle} {PlayerName}[{ID}]: {Message}" }
                };
                //dfs.WriteObject<Dictionary<string, string>>(Path.Combine(Interface.Oxide.LangDirectory, "IRC"), langs, true);
            }*/
            thread = new Thread(Loop);
            thread.Start();

            Connect();
        }

        public void Connect() {
            socket = new TcpClient();
            socket.Connect(settings.host, settings.port);
            Interface.Oxide.LogWarning(settings.host + settings.port);
            socket.NoDelay = true;
            stream = socket.GetStream();
            isConnected = true;
            Send("NICK " + settings.nick);
            Send("USER " + settings.ident + " - - :" + settings.realname);
        }

        public bool Disconnect(string reason = "Bye!")
        {
            try
            {
                Send("QUIT :" + reason);
                socket.Close();
                isConnected = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Destruct(bool disconnect = false)
        {
            if (!isConnected) return;

            try
            {
                Disconnect();
                socket.Close();
            }
            catch
            {
                // ignored
            }
        }

        static int counterrs;

        private void Loop()
        {
            while (true)
            {
                if (!isConnected && counterrs == 11) break;

                try {
                    this.parse(this.Read());
                }
                catch (Exception e)
                {
                    Interface.Oxide.LogException("Error while parsing [" + ++counterrs + "]", e);
                }
                if(counterrs == 10)
                {
                    Interface.Oxide.LogError("IRC has hit 10 errors! Destructing!");
                    counterrs++;
                    this.Destruct();
                }
            }
        }

        public bool Send(string data)
        {
            if (!this.isConnected) return false;
            if (!data.Contains("\n"))
                data += "\r\n";
            byte[] _data = new UTF8Encoding().GetBytes(data);

            try
            {
                stream.Write(_data, 0, _data.Length);
                //Interface.Oxide.LogDebug("[IRC DEBUG]: {" + DateTime.Now + "} << " + data);
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogException("[IRC ERROR]: {" + DateTime.Now + "} SEND: " + data, ex);
                return false;
            }
            return true;
        }

        public string Read()
        {
            if (!isConnected) return "";
            string data = "";
            byte[] _data = new byte[1];
            while (true)
            {
                try
                {
                    int k = stream.Read(_data, 0, 1);
                    if (k == 0)
                    {
                        return "";
                    }
                    char kk = Convert.ToChar(_data[0]);
                    data += kk;
                    if (kk == '\n')
                        break;

                }
                catch (Exception ex)
                {
                    Interface.Oxide.LogException("[IRC ERROR]: Read failed", ex);
                    Interface.Oxide.LogError("[IRC FATAL ERROR]: Read has failed. Destructing now...");
                    this.Destruct();
                    return "";
                }
            }
            return data;
        }

        public void Say(string target, string text, bool formatting = true)
        {
            string[] splitted_text = text.Split(new string[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < splitted_text.Length; i++)
                this.Send("PRIVMSG " + target + " :" + (formatting ? IRCColour.Translate(splitted_text[i]) : splitted_text[i]));
            return;
        }

        public void Notice(string target, string text)
        {
            string[] splitted_text = text.Split(new string[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < splitted_text.Length; i++)
                this.Send("NOTICE " + target + " :" + splitted_text[i]);
            return;
        }

        public void parse(string raw)
        {
            //if (String.IsNullOrEmpty(raw)) return;
            if (raw == "") return;
            //Interface.Oxide.LogDebug("[IRC Debug]: {" + DateTime.Now + "} << " + raw);

            if (raw.Substring(0, 6) == "ERROR ")
            {
                Interface.Oxide.LogError("[IRC ERROR]: " + raw + ". Thus destructing!");
                this.Destruct();
                return;
            }

            // Regex taken from (http://calebdelnay.com/blog/2010/11/parsing-the-irc-message-format-as-a-client)
            string
                prefix = "",
                command = "",
                trailing = ""
                ;
            string[] parameters = new string[] { };
            // Global var instead, mono doesn't support precompiled option
            //Regex parsingRegex = new Regex(@"^(:(?<prefix>\S+) )?(?<command>\S+)( (?!:)(?<params>.+?))?( :(?<trail>.+))?$", RegexOptions.ExplicitCapture);
            Match messageMatch = parsingRegex.Match(raw);

            if (messageMatch.Success)
            {
                prefix = messageMatch.Groups["prefix"].Value;
                command = messageMatch.Groups["command"].Value;
                parameters = messageMatch.Groups["params"].Value.Split(' ');
                trailing = messageMatch.Groups["trail"].Value;

                if (!String.IsNullOrEmpty(trailing))
                    parameters = parameters.Concat(new string[] { trailing }).ToArray();
            }

            if (command == "PING")
            {
                this.Send("PONG :" + trailing);
                if (!this.isRegistered) return;
                foreach (var inschan in settings.channels)
                    this.Send("NAMES " + inschan.name);                
            }
            else
            if (command == "001")
            {
                if (!String.IsNullOrEmpty(settings.ns_password))
                {
                    if (this.toghost)
                    {
                        this.Say("NickServ", "GHOST " + settings.nick + " " + settings.ns_password, false);
                        return;
                        //this.Send("NICK " + this.settings.nick);
                        //this.toghost = false;
                    }
                    else
                    {
                        isRegistered = true;
                        this.Say("NickServ", "IDENTIFY " + settings.ns_password, false);
                    }
                }
                isRegistered = true;
                foreach (var channel in settings.channels)
                {
                    this.Send("JOIN " + channel.name + " " + channel.key);
                }
            }
            else
            {
                string chan;
                /*
                    Join, part and stuff..
                */

                if (command == "353")
                {
                    // Names list
                    chan = parameters[2];
                    Channel inschan = GetChan(chan);
                    if (!inschan.__NAMES)
                    {
                        inschan.__NAMES = true;
                        inschan.userlist.Clear();
                    }

                    string[] _userlist = trailing.Trim().Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < _userlist.Length; i++)
                    {
                        string lerank = Convert.ToString(_userlist[i][0]);
                        string lename = _userlist[i].Remove(0, 1);
                        if (lerank == "~" || lerank == "&" || lerank == "@" || lerank == "%" || lerank == "+")
                        {
                            lename = _userlist[i].Remove(0, 1);
                        }
                        else
                        {
                            lename = _userlist[i];
                            lerank = "";
                        }

                        if (!inschan.userlist.ContainsKey(lename))
                            inschan.userlist.Add(lename, lerank);
                        else
                            inschan.userlist[lename] = lerank;
                    }
                }

                if (command == "433")
                {
                    Interface.Oxide.LogError("[IRC ERROR]: Nickname already taken!");
                    Interface.Oxide.LogWarning("[IRC WARNING]: Attempting "+this.settings.nick+"2");
                    this.toghost = true;
                    this.Send("NICK " + this.settings.nick + "2");
                    //this.Destruct();
                }

                if (command == "JOIN")
                {
                    chan = trailing;
                    string user = prefix.Split('!')[0];
                    string ident = prefix.Split('!')[1].Split('@')[0];
                    string host = prefix.Split('@')[1];
                    // REMOVED ON REQUEST
                    //SendOrder(() =>
                    //    rust.SendToChat("[IRC:JOIN] " + user + " has joined " + chan)
                    //);
                    var channel = GetChan(chan);
                    if (!channel.userlist.ContainsKey(user))
                        channel.userlist.Add(user, "");
                }

                if (command == "PART")
                {
                    chan = parameters[0];
                    string user = prefix.Split('!')[0];
                    string ident = prefix.Split('!')[1].Split('@')[0];
                    string host = prefix.Split('@')[1];
                    // REMOVED ON REQUEST
                    //SendOrder(() =>
                    //    rust.SendToChat("[IRC:PART] " + user + " has parted " + chan)
                    //);
                    GetChan(chan).userlist.Remove(user);
                }
                if (command == "366")
                {
                    // End of NAMES
                    chan = parameters[1];
                    GetChan(chan).__NAMES = false;
                }

                if (command == "MODE")
                {
                    chan = parameters[0];
                    //if (String.IsNullOrEmpty(trailing)) doesn't work on some networks
                    if(chan.Contains('#'))
                    {
                        this.Send("NAMES " + chan);
                    }
                }

                if (command == "NICK")
                {
                    if (!this.isRegistered) return;
                    string user = prefix.Split('!')[0];
                    if (this.settings.nick == user)
                        this.settings.nick = trailing.Trim();
                    foreach (var inschan in settings.channels)
                    {
                        string therank = inschan.userlist.Where(x => x.Key == user).FirstOrDefault().Value;
                        if (!inschan.userlist.ContainsKey(user))
                            inschan.userlist.Add(user, therank);
                        else
                            inschan.userlist[user] = therank;
                    }
                }

                if (command == "PRIVMSG")
                {
                    chan = parameters[0];
                    string msg = "";
                    string user = prefix.Split('!')[0];
                    string ident = prefix.Split('!')[1].Split('@')[0];
                    string host = prefix.Split('@')[1];

                    if (user == "NickServ")
                    {
                        if (trailing.Contains("Ghost with your nick has been killed"))
                        {
                            this.Send("NICK " + this.settings.nick);
                            this.toghost = false;
                            this.Say("NickServ", "IDENTIFY " + settings.ns_password, false);
                            isRegistered = true;
                            foreach (var channel in settings.channels)
                            {
                                this.Send("JOIN " + channel.name + " " + channel.key);
                            }
                        }
                    }

                    if(trailing[0] == '\x01') // CTCP
                    {
                        string ctcptype = trailing.Substring(1, trailing.IndexOf(' ', 1));
                        switch (ctcptype)
                        {
                            case "VERSION":
                                this.Notice(user, "\x01VERSION RustIRC extension v" + IRCExtension.instance.Version + " by Kirollos\x01");
                                break;
                            case "PING":
                                this.Notice(user, "\x01PING " + DateTime.UtcNow.ToString() + "\x01");
                                break;
                            default:
                                this.Notice(user, "\x01" + ctcptype + " idk :(\x01");
                                break;
                        }
                    }

                    if (trailing[0] == (String.IsNullOrEmpty(settings.commandprefix) ? '!' : settings.commandprefix[0]))
                    {
                        string cmd;
                        try
                        {
                            cmd = trailing.Split(' ')[0].ToLower();
                            if (String.IsNullOrEmpty(cmd.Trim()))
                                cmd = trailing.Trim().ToLower();
                        }
                        catch
                        {
                            cmd = trailing.Trim().ToLower();
                        }
                        
                        cmd = cmd.Remove(0, 1); // remove the prefix pls

                        msg = trailing.Remove(0, 1 + cmd.Length).Trim();
                        cmd = cmd.Trim();
                        // +, %, @ and ~ are allowed except &
                        if (cmd == "say" && (IsVoice(user, chan, true) || IsHalfOp(user, chan, true) || IsOp(user, chan, true) || IsOwner(user, chan, true) ) && !IsAdmin(user, chan, true) )
                        {
                            if (String.IsNullOrEmpty(msg))
                            {
                                this.Say(chan, "[SYNTAX]: !say [message]");
                                return;
                            }
                            SendOrder(() =>
                                rust.SendToChat("[IRC] " + user + ": " + msg)
                            );
                        }

                        if (cmd == "players") // NOTE: PLAYER IDs ARE NOT UNIQUE!!
                        {
                            SendOrder(() =>
                            {
                                string listStr = "";
                                var pList = BasePlayer.activePlayerList;
                                int i = 0;
                                foreach (var player in pList)
                                {
                                    listStr += (player.displayName + "[" + i++ + "]");
                                    if(i != pList.Count)
                                        listStr += ",";
                                }
                                this.Say(chan, IRCColour.Translate("IRC_PlayersResponse", new Dictionary<string, string>()
                                {
                                    { "count", Convert.ToString(BasePlayer.activePlayerList.Count) },
                                    { "maxplayers", Convert.ToString(ConVar.Server.maxplayers) },
                                    { "playerslist", listStr }
                                }));
                            });
                        }

                        if(cmd == "pm")
                        {
                            if(String.IsNullOrEmpty(msg) || msg.Split(new char[] { ' '}).Count() < 2)
                            {
                                this.Say(chan, "[SYNTAX]: !pm [ID] [message]");
                                return;
                            }
                            SendOrder(() =>
                            {
                                var args = msg.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                                //if (Regex.IsMatch(args[0], @"^\d{1,2}$", RegexOptions.Compiled))
                                if (!string.IsNullOrEmpty(args[0]) && args[0].All(char.IsDigit))
                                {
                                    // first arg is an ID (NOT UNIQUE THOUGH)
                                    int pID = int.Parse(args[0]);
                                    if (BasePlayer.activePlayerList.Count == 0 || BasePlayer.activePlayerList.Count < pID || (BasePlayer.activePlayerList?[pID] == null))
                                    {
                                        this.Say(chan, "[ERROR]: Invalid player ID");
                                        return;
                                    }

                                    if (args.Count() < 2 || string.IsNullOrEmpty(args[1]))
                                    {
                                        this.Say(chan, "[SYNTAX]: !pm [ID] [message]");
                                        return;
                                    }
                                    rust.SendToChat(BasePlayer.activePlayerList[pID], "[IRC PM] " + user + ": " + args[1]);
                                }
                            });
                        }

                        if (cmd == "reload" && IsOp(user, chan))
                        {
                            if (String.IsNullOrEmpty(msg))
                            {
                                this.Say(chan, "[SYNTAX]: !reload [plugin name]");
                                return;
                            }
                            msg = msg.Trim();
                            if (msg == "RustIRC") return;
                            SendOrder(() =>
                            {
                                bool success = Interface.Oxide.ReloadPlugin(msg);
                                if (success)
                                    this.Say(chan, "Plugin " + msg + " successfully reloaded!");
                                else
                                    this.Say(chan, "Plugin " + msg + " failed to reload");
                            });
                        }

                        if(cmd == "ban" && IsOp(user, chan))
                        {
                            //Interface.Oxide.RootPluginManager.GetPlugin("EnhancedBanSystem").Call(func, args);
                        }

                        if(cmd == "console" && IsOp(user, chan))
                        {
                            if(msg.Length == 0 || msg == "")
                            {
                                this.Say(chan, "[SYNTAX]: !console [command] [[args]]");
                            }
                            var args = msg.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                            string consolecmd = args[0];
                            rust.RunCommand(args[0], args[1]);
                            this.Say(chan, "Successfully executed \"" + msg + "\"!");
                            return;
                        }
                    }
                }
            }
        }

        public bool IsOwner(string name, Channel chan)
        {
            return chan.userlist[name] == "~";
        }
        public bool IsAdmin(string name, Channel chan, bool actualrank = false)
        {
            return chan.userlist[name] == "&" || (!actualrank ? (IsOwner(name, chan)) : false);
        }
        public bool IsOp(string name, Channel chan, bool actualrank = false)
        {
            return chan.userlist[name] == "@" || (!actualrank ? (IsAdmin(name, chan, true) ||IsOwner(name, chan)) : false);
        }
        public bool IsHalfOp(string name, Channel chan, bool actualrank = false)
        {
            return chan.userlist[name] == "%" || (!actualrank ? (IsOp(name, chan, true) ||IsAdmin(name, chan, true) ||IsOwner(name, chan)) : false);
        }
        public bool IsVoice(string name, Channel chan, bool actualrank = false)
        {
            return chan.userlist[name] == "+" || (!actualrank ? (IsHalfOp(name, chan, true) ||IsOp(name, chan, true) ||IsAdmin(name, chan, true) ||IsOwner(name, chan)) : false);
        }

        public bool IsOwner(string name, string chan, bool actualrank = false) { return IsOwner(name, GetChan(chan)); }
        public bool IsAdmin(string name, string chan, bool actualrank = false) { return IsAdmin(name, GetChan(chan), actualrank); }
        public bool IsOp(string name, string chan, bool actualrank = false) { return IsOp(name, GetChan(chan), actualrank); }
        public bool IsHalfOp(string name, string chan, bool actualrank = false) { return IsHalfOp(name, GetChan(chan), actualrank); }
        public bool IsVoice(string name, string chan, bool actualrank = false) { return IsVoice(name, GetChan(chan), actualrank); }

        public bool IsChanAdmin(string chan)
        {
            return GetChan(chan).adminchan;
        }

        public List<Channel> Broadcast(string message)
        {
            List<Channel> returnval = new List<Channel>();
            foreach (var channel in Channels)
            {
                if (channel.adminchan) continue;
                if (channel.lobby) continue;
                this.Say(channel.name, message);
                returnval.Add(channel);
            }
            return returnval;
        }

        public List<Channel> BroadcastAdmin(string message)
        {
            List<Channel> returnval = new List<Channel>();
            foreach (var channel in Channels)
            {
                if (!channel.adminchan) continue;
                this.Say(channel.name, message);
                returnval.Add(channel);
            }
            return returnval;
        }

        public List<Channel> BroadcastLobby(string message)
        {
            List<Channel> returnval = new List<Channel>();
            foreach (var channel in Channels)
            {
                if (!channel.lobby) continue;
                this.Say(channel.name, message);
                returnval.Add(channel);
            }
            return returnval;
        }

        private void SendOrder(Action order)
        {
            IRCExtension.SendOrder(order);
        }
    }
}