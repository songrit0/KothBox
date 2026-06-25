using Rocket.API;
using Rocket.Unturned.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandDeleteKothBox : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "deletekothbox";
        public string Help => "Delete a PVP box.";
        public string Syntax => "<name>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "kothbox.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (command.Length < 1)
            {
                UnturnedChat.Say(caller, "Syntax: /deletekothbox <name>", Color.red);
                return;
            }

            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            var name = command[0];
            if (plugin.DeleteBox(name))
            {
                UnturnedChat.Say(caller, $"[PVP] Box '{name}' deleted.", Color.green);
            }
            else
            {
                UnturnedChat.Say(caller, $"[PVP] Box '{name}' not found.", Color.red);
            }
        }
    }
}
