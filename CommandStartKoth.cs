using Rocket.API;
using Rocket.Unturned.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandStartKoth : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "startkoth";
        public string Help => "Start a PVP event. Optionally set the entry fee.";
        public string Syntax => "<boxname|random> [entryFee]";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (!Rocket.Core.R.Permissions.HasPermission(caller, new List<string> { "kothbox.admin", "kothbox.host" }))
            { UnturnedChat.Say(caller, "[PVP] ไม่มีสิทธิ์", Color.red); return; }
            if (command.Length < 1)
            {
                UnturnedChat.Say(caller, "Syntax: /startkoth <boxname> [entryFee]", Color.red);
                return;
            }

            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            // Optional host-set entry fee (clamped to the configured minimum inside StartEvent).
            uint fee = plugin.Configuration.Instance.EntryFee;
            if (command.Length > 1) uint.TryParse(command[1], out fee);

            plugin.StartEvent(command[0], fee);
            UnturnedChat.Say(caller, $"[PVP] Event starting at box '{command[0]}'.", Color.green);
        }
    }
}
