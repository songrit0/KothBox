using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.NetTransport;
using SDG.Unturned;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KothBox
{
    // Phase 8: Workshop loadout-pick UI + HUD.
    //
    // Bundle element names the plugin drives (build them in unity/Editor/BuildKothUI.cs):
    //   Loadout panel (LoadoutUIEffectId):
    //     buttons  Loadout_0 .. Loadout_5  (clickable)        -> OnUIClick
    //     button   KothClose                                  -> close
    //     texts    Loadout_0_Name .. Loadout_5_Name           -> rewritten with loadout names
    //     visibility Loadout_0 .. Loadout_5                    -> hide unused slots
    //   HUD (HudEffectId):
    //     texts    Koth_Rank  Koth_MyTime  Koth_Countdown
    public partial class KothBox
    {
        private const short LoadoutUIKey = 27531;
        private const short HudKey = 27532;
        private const short GameMenuKey = 27533;
        private const short GachaKey = 27534;
        private const short StreakKey = 27536;
        private const int MaxLoadoutButtons = 6;
        private const int MaxStreakSlots = 6;
        private const int StreakBarSegments = 10;

        // Players currently shown the streak panel.
        private readonly HashSet<ulong> _streakShown = new HashSet<ulong>();
        // Per-player AutoEquip preference (true = equip streak items to hand on award).
        private readonly Dictionary<ulong, bool> _autoEquipPref = new Dictionary<ulong, bool>();
        // Timestamp of last dome kill per killer (used for 2.5s kill-flash).
        private readonly Dictionary<ulong, float> _lastKillAt = new Dictionary<ulong, float>();

        // Players whose next loadout pick should respawn them (vs join).
        private readonly HashSet<ulong> _awaitingRespawn = new HashSet<ulong>();
        // Players currently shown a HUD (so we clear it on event end).
        private readonly HashSet<ulong> _hudShown = new HashSet<ulong>();
        // Per-player streak banner: expiry time + text (while active, overrides progress text).
        private readonly Dictionary<ulong, (float until, string text)> _streakBanner = new Dictionary<ulong, (float, string)>();
        // Host-menu: the entry fee each player has currently selected.
        private readonly Dictionary<ulong, uint> _hostFee = new Dictionary<ulong, uint>();
        // Loadout index -> gun name from sv_items (populated on a background thread at load).
        private readonly Dictionary<int, string> _loadoutGunNames = new Dictionary<int, string>();
        // When to auto-hide the gacha result panel (Time.realtimeSinceStartup); 0 = hidden.
        private float _gachaHideAt;

        // --- Game menu / lobby (/koth) ---
        public void ShowGameMenu(UnturnedPlayer player)
        {
            ushort id = Configuration.Instance.GameMenuEffectId;
            var tc = Conn(player.Player);
            if (id == 0 || tc == null)
            {
                // No bundle: fall back to chat + direct /jkoth.
                UnturnedChat.Say(player, $"[PVP] Event: {_currentState}. Fee {_eventEntryFee}, pool {_prizePool}. Use /jkoth to join, /startkoth to host.", Color.cyan);
                return;
            }

            EffectManager.sendUIEffect(id, GameMenuKey, tc, true);
            bool running = _currentState == EventState.Warmup || _currentState == EventState.Active;
            string status = running
                ? $"{_currentState} — fee {_eventEntryFee}, pool {_prizePool}, {_eventState.Participants.Count} players"
                : "No event running";
            EffectManager.sendUIEffectText(GameMenuKey, tc, true, "Koth_Status", status);

            // Show host controls only to admins; default the selected fee.
            bool canHost = player.IsAdmin
                || Rocket.Core.R.Permissions.HasPermission(player, new List<string> { "kothbox.admin" })
                || Rocket.Core.R.Permissions.HasPermission(player, new List<string> { "kothbox.host" });
            EffectManager.sendUIEffectVisibility(GameMenuKey, tc, true, "Koth_HostRow", canHost);
            uint fee = _hostFee.TryGetValue(player.CSteamID.m_SteamID, out var f) ? f : Configuration.Instance.EntryFee;
            _hostFee[player.CSteamID.m_SteamID] = fee;
            EffectManager.sendUIEffectText(GameMenuKey, tc, true, "Koth_SelFee", fee.ToString());
        }

        private void ClearGameMenu(Player player)
        {
            var tc = Conn(player);
            if (tc != null && Configuration.Instance.GameMenuEffectId != 0)
                EffectManager.askEffectClearByID(Configuration.Instance.GameMenuEffectId, tc);
        }

        private const short KothButtonKey = 27535;

        // Persistent floating button that opens the menu (so players don't have to type /koth).
        public void ShowKothButton(Player player)
        {
            ushort id = Configuration.Instance.KothButtonEffectId;
            var tc = Conn(player);
            if (id != 0 && tc != null) EffectManager.sendUIEffect(id, KothButtonKey, tc, true);
        }

        public void ShowKothButtonToAll()
        {
            if (Configuration.Instance.KothButtonEffectId == 0) return;
            foreach (var client in Provider.clients)
                if (client?.player != null) ShowKothButton(client.player);
        }

        // --- Gacha result (winner's prize, shown to everyone) ---
        private void ShowGachaResult(string winnerName, string prizeLabel)
        {
            ushort id = Configuration.Instance.GachaResultEffectId;
            if (id == 0) return;
            foreach (var client in Provider.clients)
            {
                var tc = client?.player?.channel?.owner?.transportConnection;
                if (tc == null) continue;
                EffectManager.sendUIEffect(id, GachaKey, tc, true);
                EffectManager.sendUIEffectText(GachaKey, tc, true, "Koth_GachaWinner", winnerName);
                EffectManager.sendUIEffectText(GachaKey, tc, true, "Koth_GachaPrize", prizeLabel);
            }
            _gachaHideAt = Time.realtimeSinceStartup + 8f; // auto-hide after 8s
        }

        private void HideGachaResult()
        {
            ushort id = Configuration.Instance.GachaResultEffectId;
            _gachaHideAt = 0f;
            if (id == 0) return;
            foreach (var client in Provider.clients)
            {
                var tc = client?.player?.channel?.owner?.transportConnection;
                if (tc != null) EffectManager.askEffectClearByID(id, tc);
            }
        }

        private static ITransportConnection Conn(Player p) => p?.channel?.owner?.transportConnection;

        // Entry point for /jkoth with no index: show the picker if a bundle is set, else auto-join.
        public void OpenJoinUI(UnturnedPlayer player)
        {
            // No event running -> open the KOTH menu instead (so they can host/start one).
            if (_currentState != EventState.Warmup && _currentState != EventState.Active)
            {
                ShowGameMenu(player);
                return;
            }
            if (GetParticipant(player.CSteamID.m_SteamID) != null)
            {
                UnturnedChat.Say(player, "[PVP] คุณอยู่ใน event แล้ว | You are already in the event.", Color.red);
                return;
            }

            bool isHost = _eventHostId != 0 && player.CSteamID.m_SteamID == _eventHostId;
            if (_eventEntryFee > 0 && !isHost && player.Experience < _eventEntryFee)
            {
                UnturnedChat.Say(player, $"[PVP] XP ไม่พอ (ต้องการ {_eventEntryFee} XP)", Color.red);
                return;
            }

            if (Configuration.Instance.LoadoutUIEffectId != 0)
                ShowLoadoutUI(player.Player, false);
            else
                JoinEvent(player, 0); // no bundle -> first loadout
        }

        private void ShowLoadoutUI(Player player, bool respawn)
        {
            ushort id = Configuration.Instance.LoadoutUIEffectId;
            var tc = Conn(player);
            if (id == 0 || tc == null) return;

            var options = GetKitOptions(player.channel.owner.playerID.steamID.m_SteamID);
            if (options.Count == 0)
            {
                var cfgLoadouts = Configuration.Instance.Loadouts;
                if (cfgLoadouts != null)
                    foreach (var l in cfgLoadouts)
                        options.Add(new KeyValuePair<string, string>(l.Name, ""));
            }
            // No loadout options at all: skip the picker and join/respawn immediately
            if (options.Count == 0)
            {
                if (respawn)
                    ApplyRespawnLoadout(player, 0);
                else
                {
                    var up = UnturnedPlayer.FromPlayer(player);
                    if (up != null) JoinEvent(up, 0);
                }
                return;
            }

            EffectManager.sendUIEffect(id, LoadoutUIKey, tc, true);
            for (int i = 0; i < MaxLoadoutButtons; i++)
            {
                bool has = i < options.Count;
                EffectManager.sendUIEffectVisibility(LoadoutUIKey, tc, true, "Loadout_" + i, has);
                if (!has) continue;
                EffectManager.sendUIEffectText(LoadoutUIKey, tc, true, "Loadout_" + i + "_Name", options[i].Key);

                // Push icon URL from first non-clothing item in this kit.
                var snaps = string.IsNullOrEmpty(options[i].Value)
                    ? new System.Collections.Generic.List<ItemSnapshot>()
                    : InventoryStash.Deserialize(options[i].Value);
                var firstItem = snaps.Find(s => s.Page < ClothingPageBase);
                if (firstItem != null && firstItem.Id != 0)
                {
                    string iconUrl = null;
                    lock (Instance._itemImageUrls)
                        Instance._itemImageUrls.TryGetValue(firstItem.Id, out iconUrl);
                    if (!string.IsNullOrEmpty(iconUrl))
                        EffectManager.sendUIEffectImageURL(LoadoutUIKey, tc, true, "Loadout_" + i + "_Icon", iconUrl);
                }
            }
            EffectManager.sendUIEffectText(LoadoutUIKey, tc, true, "Koth_PickTitle",
                respawn ? "RESPAWN - pick a loadout" : "PICK YOUR LOADOUT");
        }

        private void ClearLoadoutUI(Player player)
        {
            var tc = Conn(player);
            if (tc != null && Configuration.Instance.LoadoutUIEffectId != 0)
                EffectManager.askEffectClearByID(Configuration.Instance.LoadoutUIEffectId, tc);
        }

        // Public event from EffectManager.onEffectButtonClicked (no Harmony needed).
        private void OnUIClick(Player player, string button)
        {
            if (player == null || string.IsNullOrEmpty(button)) return;
            ulong steamId = player.channel.owner.playerID.steamID.m_SteamID;
            var up = UnturnedPlayer.FromPlayer(player);

            if (button == "Koth_PrepBtn")
            {
                TryTeleportToPrepRoom(player);
                return;
            }


            if (button == "KothClose")
            {
                // Respawn pickers can't be dismissed empty - give the default instead.
                if (_awaitingRespawn.Remove(steamId))
                    ApplyRespawnLoadout(player, 0);
                ClearLoadoutUI(player);
                return;
            }

            // --- Game menu / lobby buttons ---
            // The GameMenu phone's KOTH tile fires this (KothBox receives all UI clicks).
            if (button == "Tab_Koth" && up != null) { OpenJoinUI(up); return; }
            if (button == "Koth_OpenMenu" && up != null) { ShowGameMenu(up); return; }
            if (button == "KothMenuClose") { ClearGameMenu(player); return; }
            if (button == "Koth_Join" && up != null) { ClearGameMenu(player); OpenJoinUI(up); return; }
            if (button == "Koth_Start" && up != null)
            {
                uint fee = _hostFee.TryGetValue(steamId, out var hf) ? hf : Configuration.Instance.EntryFee;
                ClearGameMenu(player);
                StartEvent(Configuration.Instance.DefaultBoxName, fee, steamId);
                OpenJoinUI(up); // host gets the loadout pick to join their own event
                return;
            }
            if (button.StartsWith("Koth_Fee_"))
            {
                uint cur = _hostFee.TryGetValue(steamId, out var hf2) ? hf2 : Configuration.Instance.EntryFee;
                int delta = 0;
                switch (button)
                {
                    case "Koth_Fee_P10":  delta =  10;  break;
                    case "Koth_Fee_P100": delta =  100; break;
                    case "Koth_Fee_M10":  delta = -10;  break;
                    case "Koth_Fee_M100": delta = -100; break;
                }
                uint newFee = (uint)UnityEngine.Mathf.Max(0, (int)cur + delta);
                _hostFee[steamId] = newFee;
                var tc = Conn(player);
                if (tc != null) EffectManager.sendUIEffectText(GameMenuKey, tc, true, "Koth_SelFee", newFee.ToString());
                return;
            }

            if (!button.StartsWith("Loadout_")) return;
            if (!int.TryParse(button.Substring("Loadout_".Length), out int index)) return;

            if (_awaitingRespawn.Remove(steamId))
            {
                ApplyRespawnLoadout(player, index);
                ClearLoadoutUI(player);
            }
            else if (up != null)
            {
                JoinEvent(up, index);
                ClearLoadoutUI(player);
            }
        }

        private void ApplyRespawnLoadout(Player victim, int index)
        {
            ClearGrid(victim);
            var loadout = PickLoadout(index);
            GiveLoadout(victim, loadout);
            var participant = GetParticipant(victim.channel.owner.playerID.steamID.m_SteamID);
            if (participant != null && loadout != null) participant.LoadoutName = loadout.Name;
            var up = UnturnedPlayer.FromPlayer(victim);
            if (up != null) UnturnedChat.Say(up, "[PVP] ฟื้นคืนชีพในสนาม! | Respawned in the dome!", Color.cyan);
        }

        // --- HUD: rank / personal time / stage countdown, pushed each tick ---
        private void UpdateHuds(List<ParticipantData> rankedByTime)
        {
            ushort hudId = Configuration.Instance.HudEffectId;
            if (hudId == 0) return;

            float remain = _currentState == EventState.Warmup
                ? Mathf.Max(0, Configuration.Instance.WarmupDuration - _stateTimer)
                : Mathf.Max(0, _activeDuration - _stateTimer);
            string stage = _currentState == EventState.Warmup ? "WARMUP" : "FIGHT";
            int remSec = Mathf.CeilToInt(remain);
            string remStr = $"{remSec / 60:D2}:{remSec % 60:D2}";

            // Scoreboard (same for everyone): player name + kills, ranked by kills then time.
            var board = _eventState.Participants
                .OrderByDescending(p => p.Kills).ThenByDescending(p => p.CumulativeSeconds).Take(5).ToList();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < board.Count; i++)
            {
                var sp = PlayerTool.getSteamPlayer(new Steamworks.CSteamID(board[i].SteamId));
                string nm = sp?.playerID.characterName ?? board[i].SteamId.ToString();
                if (nm.Length > 12) nm = nm.Substring(0, 12);
                sb.AppendLine($"{i + 1}. {nm}  {board[i].Kills}k");
            }
            string scoreboard = sb.ToString();

            for (int i = 0; i < rankedByTime.Count; i++)
            {
                var part = rankedByTime[i];
                var player = PlayerTool.getPlayer(new Steamworks.CSteamID(part.SteamId));
                var tc = Conn(player);
                if (tc == null) continue;

                if (_hudShown.Add(part.SteamId))
                    EffectManager.sendUIEffect(hudId, HudKey, tc, true);

                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Rank", "#" + (i + 1) + " / " + rankedByTime.Count);
                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_MyTime", Mathf.FloorToInt(part.CumulativeSeconds) + "s");
                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Countdown", stage + " " + remStr);
                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Pool", "Pool " + _prizePool);
                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Fee", "Fee " + _eventEntryFee);
                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Scoreboard", scoreboard);

                // Kill flash: show 2.5s after each kill, then hide.
                bool flash = _lastKillAt.TryGetValue(part.SteamId, out float kt)
                    && Time.realtimeSinceStartup - kt < 2.5f;
                EffectManager.sendUIEffectVisibility(HudKey, tc, true, "Koth_KillCounter", flash);

                EffectManager.sendUIEffectVisibility(HudKey, tc, true, "Koth_PrepBtn", false);

                // Kill streak: show banner while active, else show progress toward next milestone.
                var streakCfg = Configuration.Instance.KillStreakRewards;
                if (streakCfg != null && streakCfg.Count > 0)
                {
                    string streakText;
                    float now = Time.realtimeSinceStartup;
                    if (_streakBanner.TryGetValue(part.SteamId, out var banner) && now < banner.until)
                    {
                        streakText = banner.text;
                    }
                    else
                    {
                        var next = streakCfg
                            .Where(s => s.Kills > part.StreakKills)
                            .OrderBy(s => s.Kills)
                            .FirstOrDefault();
                        if (next != null)
                        {
                            string nm = GetItemName(next.ItemId);
                            if (nm.Length > 12) nm = nm.Substring(0, 12);
                            streakText = $"{part.StreakKills} kill\n>{next.Kills}: {nm}";
                        }
                        else
                            streakText = $"{part.StreakKills} kill\n★ MAX";
                    }
                    EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Streak", streakText);
                }
            }
        }

        // --- Streak panel (milestone boxes + progress bar + AutoEquip toggle) ---

        public bool GetAutoEquipPref(ulong steamId) =>
            _autoEquipPref.TryGetValue(steamId, out var v) ? v : true;

        public void ShowStreakUI(Player player)
        {
            ushort id = Configuration.Instance.StreakUIEffectId;
            var tc = Conn(player);
            if (id == 0 || tc == null) return;
            ulong sid = player.channel.owner.playerID.steamID.m_SteamID;
            EffectManager.sendUIEffect(id, StreakKey, tc, true);
            _streakShown.Add(sid);
            // Immediately push the static milestone layout (item names + kill thresholds).
            PushStreakLayout(player, tc);
        }

        // Push the static milestone layout (item icons + kill thresholds).
        // Called once on join; UpdateStreakProgress handles the per-tick dynamic parts.
        private void PushStreakLayout(Player player, SDG.NetTransport.ITransportConnection tc)
        {
            var streaks = Configuration.Instance.KillStreakRewards;
            for (int i = 0; i < MaxStreakSlots; i++)
            {
                bool has = streaks != null && i < streaks.Count;
                EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_" + i, has);
                if (!has) continue;
                var sr = streaks[i];


                EffectManager.sendUIEffectText(StreakKey, tc, true, "Streak_" + i + "_Kills", sr.Kills + " kill");
                EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_" + i + "_Done", false);
            }
            EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_AE_On",      false);
            EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_AE_Knob",    false);
            EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_AE_KnobOff", false);
        }

        private static void PushAEState(ITransportConnection tc, bool on)
        {
            EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_AE_On",      on);
            EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_AE_Knob",    on);
            EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_AE_KnobOff", !on);
        }

        public void ShowKillFlash(Player player, uint kills)
        {
            if (Configuration.Instance.HudEffectId == 0) return;
            var tc = Conn(player);
            if (tc == null) return;
            ulong sid = player.channel.owner.playerID.steamID.m_SteamID;
            _lastKillAt[sid] = Time.realtimeSinceStartup;
            EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_KillCount", kills.ToString());
            EffectManager.sendUIEffectVisibility(HudKey, tc, true, "Koth_KillCounter", true);
        }

        // Push per-tick dynamic state: progress bar + achieved milestones + kill count.
        public void UpdateStreakProgress(Player player, ParticipantData part)
        {
            ushort id = Configuration.Instance.StreakUIEffectId;
            if (id == 0) return;
            var tc = Conn(player);
            if (tc == null) return;

            var streaks = Configuration.Instance.KillStreakRewards;
            if (streaks == null || streaks.Count == 0) return;

            int kills = (int)part.StreakKills;
            int maxKills = streaks[streaks.Count - 1].Kills;

            // Fill segments: K of StreakBarSegments shown.
            int segsOn = maxKills > 0 ? Mathf.Min(StreakBarSegments, kills * StreakBarSegments / maxKills) : 0;
            for (int s = 0; s < StreakBarSegments; s++)
                EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_Seg_" + s, s < segsOn);

            EffectManager.sendUIEffectText(StreakKey, tc, true, "Streak_KillText", kills + " kill");

            // Achieved overlays.
            for (int i = 0; i < streaks.Count && i < MaxStreakSlots; i++)
                EffectManager.sendUIEffectVisibility(StreakKey, tc, true, "Streak_" + i + "_Done", kills >= streaks[i].Kills);
        }

        public void ClearStreakUI(Player player)
        {
            ushort id = Configuration.Instance.StreakUIEffectId;
            var tc = Conn(player);
            if (id != 0 && tc != null) EffectManager.askEffectClearByID(id, tc);
            ulong sid = player.channel.owner.playerID.steamID.m_SteamID;
            _streakShown.Remove(sid);
            _autoEquipPref.Remove(sid);
        }

        public void ClearAllStreakUI()
        {
            ushort id = Configuration.Instance.StreakUIEffectId;
            if (id == 0) { _streakShown.Clear(); _autoEquipPref.Clear(); return; }
            foreach (var sid in _streakShown)
            {
                var tc = Conn(PlayerTool.getPlayer(new Steamworks.CSteamID(sid)));
                if (tc != null) EffectManager.askEffectClearByID(id, tc);
            }
            _streakShown.Clear();
            _autoEquipPref.Clear();
        }

        // Show a temporary kill-streak milestone banner on the player's HUD (auto-reverts to progress).
        public void ShowStreakBanner(Player player, string text, float seconds)
        {
            if (Configuration.Instance.HudEffectId == 0 || player == null) return;
            ulong sid = player.channel.owner.playerID.steamID.m_SteamID;
            _streakBanner[sid] = (Time.realtimeSinceStartup + seconds, text);
            var tc = Conn(player);
            if (tc != null)
                EffectManager.sendUIEffectText(HudKey, tc, true, "Koth_Streak", text);
        }

        // Clear one player's HUD (used when a single player leaves the event).
        private void ClearPlayerHud(Player player)
        {
            ushort hudId = Configuration.Instance.HudEffectId;
            var tc = Conn(player);
            if (hudId != 0 && tc != null) EffectManager.askEffectClearByID(hudId, tc);
            ulong sid = player.channel.owner.playerID.steamID.m_SteamID;
            _hudShown.Remove(sid);
            _streakBanner.Remove(sid);
            ClearStreakUI(player);
        }

        private void ClearAllHuds()
        {
            ushort hudId = Configuration.Instance.HudEffectId;
            if (hudId == 0) { _hudShown.Clear(); }
            else
            {
                foreach (var sid in _hudShown)
                {
                    var tc = Conn(PlayerTool.getPlayer(new Steamworks.CSteamID(sid)));
                    if (tc != null) EffectManager.askEffectClearByID(hudId, tc);
                }
                _hudShown.Clear();
            }
            _awaitingRespawn.Clear();
            _streakBanner.Clear();
            ClearAllStreakUI();
        }
    }
}
