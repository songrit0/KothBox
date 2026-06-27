using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    // Admin: save your current inventory as a named default kit (given to players with no MY SET).
    public class CommandSaveDefaultKit : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "savedefaultkit";
        public string Help => "Admin: save current inventory as a named default PVP kit.";
        public string Syntax => "<name>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (!Rocket.Core.R.Permissions.HasPermission(caller, new List<string> { "kothbox.admin" }))
            { UnturnedChat.Say(caller, "[PVP] ไม่มีสิทธิ์", Color.red); return; }
            var plugin = KothBox.Instance;
            if (plugin == null) { UnturnedChat.Say(caller, "Plugin not loaded.", Color.red); return; }
            if (command.Length < 1) { UnturnedChat.Say(caller, "Usage: /savedefaultkit <name>", Color.red); return; }
            int n = plugin.SaveDefaultKit((UnturnedPlayer)caller, command[0]);
            UnturnedChat.Say(caller, $"[PVP] Saved default kit '{command[0]}' ({n} items).", Color.green);
        }
    }
}
