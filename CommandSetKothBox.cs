using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandSetKothBox : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "setkothbox";
        public string Help => "Set a new PVP box at your current position.";
        public string Syntax => "<name> <radius>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "kothbox.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (command.Length < 2)
            {
                UnturnedChat.Say(caller, "Syntax: /setkothbox <name> <radius>", Color.red);
                return;
            }

            var player = (UnturnedPlayer)caller;
            var name = command[0];
            if (!float.TryParse(command[1], out var radius) || radius <= 0)
            {
                UnturnedChat.Say(caller, "Radius must be a positive number.", Color.red);
                return;
            }

            var plugin = KothBox.Instance;
            if (plugin == null)
            {
                UnturnedChat.Say(caller, "Plugin not loaded.", Color.red);
                return;
            }

            plugin.AddOrUpdateBox(name, player.Position, radius);
            UnturnedChat.Say(caller, $"[PVP] Box '{name}' set at {player.Position:F1} with radius {radius}.", Color.green);
        }
    }
}
