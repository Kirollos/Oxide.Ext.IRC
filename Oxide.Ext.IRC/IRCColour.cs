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

namespace Oxide.Ext.IRC
{
    class IRCColour
    {
        public enum mIRC_Colours
        {
            INVALID = -1, NONE = -2,
            white = 0, black, blue, green, lred, brown, purple,
            orange, yellow, lgreen, cyan, lcyan, lblue, pink, grey, lgrey
        }

        public static List<byte[]> mirc = new List<byte[]>()
        {
            new byte[] { 255, 255, 255 },
            new byte[] { 000, 000, 000 },
            new byte[] { 000, 000, 127 },
            new byte[] { 000, 147, 000 },
            new byte[] { 255, 000, 000 },
            new byte[] { 127, 000, 000 },
            new byte[] { 156, 000, 156 },
            new byte[] { 252, 127, 000 },
            new byte[] { 255, 255, 000 },
            new byte[] { 000, 252, 000 },
            new byte[] { 000, 147, 147 },
            new byte[] { 000, 255, 255 },
            new byte[] { 000, 000, 252 },
            new byte[] { 255, 000, 255 },
            new byte[] { 127, 127, 127 },
            new byte[] { 210, 210, 210 }
        };

        public static int? GetColorFromHex(string hex)
        {
            hex = hex.Trim();
            byte r, g, b;
            if (hex.StartsWith("#"))
                hex = hex.Remove(0, 1);
            r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            int i=0, c=0, d = 200000;
            int? ret = null;
            while(i < 16)
            {

                byte[] m = mirc[i];
                c = ((r - m[0]) * (r - m[0])) + ((g - m[1]) * (g - m[1])) + ((b - m[2]) * (b - m[2]));
                if ( c < d )
                {
                    d = c;
                    ret = i;
                }
                i++;
            }
            return ret;
        }

        public static string Translate(string msg, Dictionary<string, string> parameters = null)
        {
            try
            {
                if (String.IsNullOrEmpty(msg))
                    return "";
            }
            catch
            {
                return "";
            }

            /*if(IRCExtension.irc.langs.ContainsKey(msg))
            {
                msg = IRCExtension.irc.langs[msg];
            }*/
            string zzz = IRCExtension.irc.lang.GetMessage(msg, IRCExtension.rust);
            if (zzz != null)
            {
                msg = zzz;
            }

            if (parameters != null)
            {
                foreach (var lekey in parameters)
                {
                    if (msg.Contains("{" + lekey.Key + "}"))
                        msg = msg.Replace("{" + lekey.Key + "}", lekey.Value);
                }
            }

            int idx;
            while ((idx = msg.IndexOf("{irccolor:", 0)) > -1)
            {
                int idxc = msg.IndexOf(':', idx);
                int idxend = msg.IndexOf('}', idxc);
                string colourname = msg.Substring(idxc + 1, idxend - idxc - 1).ToLower();

                mIRC_Colours colourid;

                try
                {
                    colourid = (mIRC_Colours)Enum.Parse(typeof(mIRC_Colours), colourname);
                    if (!Enum.IsDefined(typeof(mIRC_Colours), colourid) || colourid.ToString() == "None")
                    {
                        Interface.Oxide.LogWarning("IRC Warning: IRC colour (" + colourname + ") is invalid.");
                        colourid = mIRC_Colours.INVALID;
                    }
                }
                catch
                {
                    Interface.Oxide.LogWarning("IRC Warning: IRC colour (" + colourname + ") is invalid.");
                    colourid = mIRC_Colours.INVALID;
                }

                if (colourid >= mIRC_Colours.white)
                    msg = msg.Replace("{irccolor:" + colourname + "}", String.Format("{0}{1:D2}", Convert.ToChar(3), (int)colourid));
                else if (colourid == mIRC_Colours.NONE)
                    msg = msg.Replace("{irccolor:" + colourname + "}", Convert.ToChar(15).ToString());
                else
                    msg = msg.Replace("{irccolor:" + colourname + "}", "");

            }

            msg = msg.Replace("{ircbold}", Convert.ToChar(2).ToString());
            msg = msg.Replace("{irccolor}", Convert.ToChar(3).ToString());

            return msg;
            //return IRCExtension.irc.langs.ContainsKey(msg) == true ? IRCExtension.irc.langs[msg] : msg;
        }
    }
}
