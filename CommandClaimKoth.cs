using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KothBox
{
    public class CommandClaimKoth : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "claimkoth";
        public string Help => "Claim pending KOTH rewards.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
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

            plugin.ClaimRewards(player);
        }
    }
}
