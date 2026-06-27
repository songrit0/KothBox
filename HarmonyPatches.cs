using HarmonyLib;
using Rocket.Core.Commands;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System;
using System.Reflection;
using UnityEngine;

namespace KothBox
{
    // While two players are both KOTH participants, treat them as NOT same group so they
    // can damage each other regardless of their in-game group membership.
    [HarmonyPatch(typeof(PlayerQuests), "isMemberOfSameGroupAs")]
    public static class KothGroupOverridePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerQuests __instance, Player player, ref bool __result)
        {
            var plugin = KothBox.Instance;
            if (plugin == null || player == null) return true;
            ulong a = __instance.player.channel.owner.playerID.steamID.m_SteamID;
            ulong b = player.channel.owner.playerID.steamID.m_SteamID;
            if (plugin.GetParticipant(a) != null && plugin.GetParticipant(b) != null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }


    // Phase 3.5: block escape commands (/tpa, /home, ...) while a player is an active
    // KOTH participant. Prefix RocketCommandManager.Execute; return false to swallow.
    [HarmonyPatch(typeof(RocketCommandManager), "Execute")]
    public static class CommandBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Rocket.API.IRocketPlayer player, string command)
        {
            var plugin = KothBox.Instance;
            if (plugin == null || string.IsNullOrEmpty(command)) return true;

            var up = player as UnturnedPlayer;
            if (up == null) return true;

            var parts = command.TrimStart('/').Split(' ');
            var cmdName = parts[0].ToLowerInvariant();

            // Block outsiders from TPA-ing to a KOTH participant.
            if (cmdName == "tpa" && parts.Length >= 2 && plugin.GetParticipant(up.CSteamID.m_SteamID) == null)
            {
                string targetArg = parts[1].ToLowerInvariant();
                foreach (var client in Provider.clients)
                {
                    if (client.playerID.characterName.ToLowerInvariant().Contains(targetArg)
                        || client.playerID.playerName.ToLowerInvariant().Contains(targetArg))
                    {
                        if (plugin.GetParticipant(client.playerID.steamID.m_SteamID) != null)
                        {
                            UnturnedChat.Say(player, "[PVP] ผู้เล่นนั้นอยู่ในสนาม PVP ไม่สามารถ TPA ได้", Color.red);
                            return false;
                        }
                    }
                }
            }

            // Only restrict while this player is in an event.
            if (plugin.GetParticipant(up.CSteamID.m_SteamID) == null) return true;

            // Whitelist: while in the arena, ONLY KOTH commands are allowed; block everything else.
            var allowed = plugin.Configuration.Instance.AllowedCommandsInEvent;
            if (allowed != null && allowed.Contains(cmdName)) return true;

            UnturnedChat.Say(player, "[PVP] ห้ามใช้คำสั่งระหว่างอยู่ในสนาม PVP — พิมพ์ /leavekoth เพื่อออก", Color.red);
            return false;
        }
    }

    // Prevent Deadbox from collecting death loot for KOTH participants.
    [HarmonyPatch]
    public static class DeadboxOptOutPatch
    {
        static MethodBase TargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Deadbox.Deadbox");
                if (t != null) return t.GetMethod("OpenWindow", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return null;
        }

        [HarmonyPrefix]
        public static bool Prefix(ulong steamId)
        {
            var koth = KothBox.Instance;
            if (koth == null) return true;
            return koth.GetParticipant(steamId) == null;
        }
    }

    // Prevent VaultBackpack from dropping a death box for KOTH participants.
    [HarmonyPatch]
    public static class VaultBackpackDeathOptOutPatch
    {
        static MethodBase TargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("VaultBackpack.VaultBackpackPlugin");
                if (t != null) return t.GetMethod("OnPlayerDeath", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return null;
        }

        [HarmonyPrefix]
        public static bool Prefix(UnturnedPlayer player)
        {
            var koth = KothBox.Instance;
            if (koth == null || player == null) return true;
            return koth.GetParticipant(player.CSteamID.m_SteamID) == null;
        }
    }
}
