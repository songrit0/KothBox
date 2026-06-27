using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    public class CommandSetKothSpec : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "setkothspec";
        public string Help => "Set the spectator viewing position for KOTH events.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (!Rocket.Core.R.Permissions.HasPermission(caller, new List<string> { "kothbox.admin" }))
            { UnturnedChat.Say(caller, "[PVP] ไม่มีสิทธิ์", Color.red); return; }

            var player = (UnturnedPlayer)caller;
            KothBox.Instance?.SetSpectatorSpot(player.Position);
            UnturnedChat.Say(caller, $"[PVP] บันทึกจุดผู้ชมที่ {player.Position:F1}", Color.green);
        }
    }
}
