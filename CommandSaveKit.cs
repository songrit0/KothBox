using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    // Save your current inventory (gun + attachments + items, arranged) as your KOTH kit (MY SET).
    public class CommandSaveKit : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "savekit";
        public string Help => "Save your current inventory as your PVP loadout (MY SET).";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = KothBox.Instance;
            if (plugin == null) { UnturnedChat.Say(caller, "Plugin not loaded.", Color.red); return; }
            int n = plugin.SaveMyKit((UnturnedPlayer)caller);
            UnturnedChat.Say(caller, $"[PVP] Saved MY SET ({n} items). It will be used when you join PVP.", Color.green);
        }
    }
}
