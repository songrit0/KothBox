using Rocket.API;
using Rocket.Unturned.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandKothBoxes : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "kothboxes";
        public string Help => "List all PVP boxes.";
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

            var boxes = plugin.GetAllBoxes();
            if (boxes.Count == 0)
            {
                UnturnedChat.Say(caller, "No PVP boxes configured.", Color.yellow);
                return;
            }

            UnturnedChat.Say(caller, $"[PVP] {boxes.Count} box(es):", Color.cyan);
            foreach (var box in boxes)
            {
                UnturnedChat.Say(caller, $"  • {box.Name} (ID {box.Id}): {box.GetCenter():F1}, radius {box.Radius}", Color.white);
            }
        }
    }
}
