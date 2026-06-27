using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using UnityEngine;

namespace KothBox
{
    /// <summary>/setkothboxbuild &lt;boxname&gt; &lt;buildname|-&gt; — link (or unlink) an AdminBarricade
    /// saved build to a KOTH box. The build auto-loads on /startkoth and clears on /stopkoth.</summary>
    public sealed class CommandSetKothBoxBuild : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "setkothboxbuild";
        public string Help => "Link an AdminBarricade saved build to a KOTH box.";
        public string Syntax => "<boxname> <buildname|->";
        public List<string> Aliases => new List<string> { "skbb" };
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (!Rocket.Core.R.Permissions.HasPermission(caller, new List<string> { "kothbox.admin" }))
            { UnturnedChat.Say(caller, "[PVP] ไม่มีสิทธิ์", Color.red); return; }
            if (command.Length < 2)
            {
                UnturnedChat.Say(caller, "ใช้: /setkothboxbuild <boxname> <buildname|-> (-=ยกเลิก)", Color.yellow);
                return;
            }

            var plugin = KothBox.Instance;
            if (plugin == null) return;

            string boxName = command[0];
            var box = plugin.GetBox(boxName);
            if (box == null)
            {
                UnturnedChat.Say(caller, $"[PVP] ไม่พบ box '{boxName}'", Color.red);
                return;
            }

            string rawBuild = command[1];
            if (rawBuild == "-")
            {
                box.BuildName = null;
                plugin.SaveBoxes();
                UnturnedChat.Say(caller, $"[PVP] ยกเลิก build ของ box '{boxName}'", Color.green);
                return;
            }

            string clean = KothBox.SanitizeBuildName(rawBuild);
            if (clean == null)
            {
                UnturnedChat.Say(caller, "[PVP] ชื่อ build ไม่ถูกต้อง (a-z 0-9 - _)", Color.red);
                return;
            }

            if (!KothBox.BuildExists(clean))
            {
                UnturnedChat.Say(caller, $"[PVP] ไม่พบ build '{clean}' ใน AdminBarricade saves", Color.red);
                return;
            }

            box.BuildName = clean;
            plugin.SaveBoxes();
            UnturnedChat.Say(caller, $"[PVP] ผูก build '{clean}' กับ box '{boxName}' แล้ว", Color.green);
        }
    }
}
