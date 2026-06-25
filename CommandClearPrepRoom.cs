using Rocket.API;
using Rocket.Unturned.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandClearPrepRoom : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "clearpreproom";
        public string Help => "Admin: ลบ prep room template ออก";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "kothbox.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = KothBox.Instance;
            if (plugin == null) { UnturnedChat.Say(caller, "Plugin not loaded.", Color.red); return; }
            plugin.ClearPrepRoom(out var msg);
            UnturnedChat.Say(caller, $"[PVP] {msg}", Color.green);
        }
    }
}
