using Rocket.API;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandPreviewKothBox : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "previewkothbox";
        public string Help => "Preview a PVP box with a ring effect.";
        public string Syntax => "<name>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "kothbox.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (command.Length < 1)
            {
                UnturnedChat.Say(caller, "Syntax: /previewkothbox <name>", Color.red);
                return;
            }

            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            var name = command[0];
            var box = plugin.GetBox(name);
            if (box == null)
            {
                UnturnedChat.Say(caller, $"[PVP] Box '{name}' not found.", Color.red);
                return;
            }

            plugin.PreviewBox(box);
            UnturnedChat.Say(caller, $"[PVP] Previewing box '{name}' with ring effect.", Color.green);
        }
    }
}
