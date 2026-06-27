using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    // Admin สร้างห้องบนแมพ แล้วยืนในห้อง รัน /savepreproom <radius>
    // บันทึก barricades ทุกอันในรัศมี + ตำแหน่ง spawn ผู้เล่น
    public class CommandSavePrepRoom : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "savepreproom";
        public string Help => "Admin: บันทึกห้อง prep จาก barricades ในรัศมีรอบตัว";
        public string Syntax => "<radius>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (!Rocket.Core.R.Permissions.HasPermission(caller, new List<string> { "kothbox.admin" }))
            { UnturnedChat.Say(caller, "[PVP] ไม่มีสิทธิ์", Color.red); return; }
            var plugin = KothBox.Instance;
            if (plugin == null) { UnturnedChat.Say(caller, "Plugin not loaded.", Color.red); return; }
            if (command.Length < 1 || !float.TryParse(command[0], out var radius) || radius <= 0)
            {
                UnturnedChat.Say(caller, "Syntax: /savepreproom <radius>", Color.red);
                return;
            }
            if (plugin.SavePrepRoom((UnturnedPlayer)caller, radius, out var msg))
                UnturnedChat.Say(caller, $"[PVP] {msg}", Color.green);
            else
                UnturnedChat.Say(caller, $"[PVP] {msg}", Color.red);
        }
    }
}
