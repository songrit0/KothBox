using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    // Debug: trigger an effect id at your position so we can see if it renders at all,
    // independent of the event/RenderDome logic. /testdome 30062  (white) / 30063 (green)
    public class CommandTestDome : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "testdome";
        public string Help => "Spawn an effect at your position to test rendering.";
        public string Syntax => "<effectId>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "kothbox.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var player = (UnturnedPlayer)caller;
            ushort id = 30062;
            if (command.Length > 0) ushort.TryParse(command[0], out id);

            var asset = Assets.find(EAssetType.EFFECT, id) as EffectAsset;
            if (asset == null)
            {
                UnturnedChat.Say(caller, $"[PVP] Effect {id} NOT FOUND (bundle not loaded / wrong id).", Color.red);
                return;
            }

            EffectManager.triggerEffect(new TriggerEffectParameters(asset)
            {
                position = player.Position,
                relevantDistance = 256f,
                reliable = true
            });
            UnturnedChat.Say(caller, $"[PVP] Triggered effect {id} at your position (asset found OK).", Color.green);
        }
    }
}
