using Rocket.API;
using Rocket.Unturned.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandReloadKothBoxes : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "reloadkothboxes";
        public string Help => "Reload all PVP box data.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "kothbox.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            plugin.ReloadBoxes();
            UnturnedChat.Say(caller, "[PVP] Boxes reloaded.", Color.green);
        }
    }
}
