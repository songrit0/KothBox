using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandJoinKoth : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "jkoth";
        public string Help => "Join the current PVP event.";
        public string Syntax => "[loadoutIndex]";
        public List<string> Aliases => new List<string> { "joinkoth" };
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var player = (UnturnedPlayer)caller;
            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            // No index -> open the loadout picker UI (or auto-join if no bundle configured).
            // Explicit index -> join with that loadout directly.
            if (command.Length > 0 && int.TryParse(command[0], out int loadoutIndex))
                plugin.JoinEvent(player, loadoutIndex);
            else
                plugin.OpenJoinUI(player);
        }
    }
}
