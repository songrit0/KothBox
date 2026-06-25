using Rocket.API;
using Rocket.Unturned.Chat;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KothBox
{
    public class CommandKothTop : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "kothtop";
        public string Help => "Show KOTH leaderboard.";
        public string Syntax => "[limit]";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            int limit = 10;
            if (command.Length > 0 && int.TryParse(command[0], out int l))
                limit = l;

            plugin.ShowLeaderboard(caller, limit);
        }
    }
}
