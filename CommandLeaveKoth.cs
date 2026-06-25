using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandLeaveKoth : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "leavekoth";
        public string Help => "Leave the PVP event and return to where you joined.";
        public string Syntax => "";
        public List<string> Aliases => new List<string> { "lkoth" };
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }
            plugin.LeaveEventByCommand((UnturnedPlayer)caller);
        }
    }
}
