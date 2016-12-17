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
using System.IO;
using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Libraries;
using Oxide.Game.Rust;

namespace Oxide.Ext.IRC
{
    public class IRCExtension : Extension
    {
        public override string Name => "IRC";
        public override VersionNumber Version => new VersionNumber(1, 0, 0);
        public override string Author => "Kirollos";

        public static Libraries.IRC irc;
        public static IRCExtension instance;
        public static Oxide.Plugins.RustIRC rust;
        public static Queue<Action> orders = new Queue<Action>();

        public IRCExtension(ExtensionManager manager) : base(manager)
        {
            instance = this;
        }

        public override void Load()
        {
            Manager.RegisterLibrary("IRC", irc = new Libraries.IRC());
            Interface.Oxide.OnFrame(OnFrame);
        }

        public override void LoadPluginWatchers(string pluginDirectory)
        {
            
        }

        public override void OnModLoad()
        {
            Interface.Oxide.RootPluginManager.AddPlugin(rust = new Oxide.Plugins.RustIRC());
        }

        private void OnFrame(float delta)
        {
            lock(orders)
            {
                while(orders.Count != 0)
                {
                    orders.Dequeue()();
                }
            }
        }
        
        public static void SendOrder(Action order)
        {
            lock(orders)
                orders.Enqueue(order);
        }
    }
}
