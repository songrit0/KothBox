using Rocket.API;
using Rocket.API.Collections;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static System.IO.Directory;
using static System.IO.Path;

namespace KothBox
{
    public enum EventState { Idle, Warmup, Active, Ended }

    public partial class KothBox : RocketPlugin<KothConfiguration>
    {
        public static KothBox Instance { get; private set; }

        private KothDataManager _dataManager;
        private KothCoinsDatabase _coinsDb;
        private KothBoxList _boxes;
        private KothEventState _eventState;
        private PendingRewardsList _rewards;
        private LeaderboardData _leaderboard;

        private EventState _currentState = EventState.Idle;
        private KothBoxData _activeBox;
        private ulong _eventHostId;
        private float _stateTimer;
        private float _domeRenderTimer;
        private HarmonyLib.Harmony _harmony;
        private readonly List<Transform> _wallBarricades = new List<Transform>();
        private readonly Dictionary<ulong, float> _autoJoinTimers = new Dictionary<ulong, float>();
        private long _prizePool;
        private uint _eventEntryFee;
        private bool _suppressItemDrops = false;

        // General item image URL cache (itemId → url from sv_items.image_url), loaded in background.
        internal readonly Dictionary<ushort, string> _itemImageUrls = new Dictionary<ushort, string>();

        // Prep room (safe waiting zone, toggled by clicking the configured storage box).
        private PrepRoomTemplate _prepRoom;
        private readonly Dictionary<ulong, float> _prepBanUntil = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, int> _pendingHeal = new Dictionary<ulong, int>();
        // ponytail: Zombie as key — instance is stable until respawn; cleared on StopEvent
        private readonly Dictionary<Zombie, ulong> _zombieLastHit = new Dictionary<Zombie, ulong>();
        private float _healTickTimer;

        protected override void Load()
        {
            Instance = this;
            var pluginDir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Environment.CurrentDirectory, "Rocket", "Plugins", "KothBox")).FullName;
            _dataManager = new KothDataManager(pluginDir);

            ReloadBoxes();
            _eventState = _dataManager.LoadState();
            _rewards = _dataManager.LoadRewards();
            _leaderboard = _dataManager.LoadLeaderboard();
            LoadPrepRoom();

            try
            {
                if (Configuration.Instance.Database != null)
                    _coinsDb = new KothCoinsDatabase(Configuration.Instance.Database);
            }
            catch (Exception ex) { Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] coins DB init"); }

            // Cache each loadout's gun name from sv_items (background thread; UI shows real names).
            if (_coinsDb != null)
            {
                var loadouts = Configuration.Instance.Loadouts;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (loadouts == null) return;
                    for (int i = 0; i < loadouts.Count; i++)
                    {
                        var ids = loadouts[i].ItemIds;
                        if (ids == null || ids.Count == 0) continue;
                        var nm = _coinsDb.GetItemName(ids[0]);
                        if (!string.IsNullOrEmpty(nm)) lock (_loadoutGunNames) _loadoutGunNames[i] = nm;
                    }
                });
            }

            // Cache item image URLs (streak + loadout first items) from sv_items in background.
            if (_coinsDb != null)
            {
                var streaks = Configuration.Instance.KillStreakRewards;
                var dataManager = _dataManager;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    // collect all item ids to pre-warm
                    var ids = new HashSet<ushort>();
                    if (streaks != null)
                        foreach (var sr in streaks) if (sr.ItemId != 0) ids.Add(sr.ItemId);
                    // default kit first items
                    try
                    {
                        foreach (var name in dataManager.ListDefaultKits())
                        {
                            var snaps = InventoryStash.Deserialize(dataManager.GetDefaultKitPath(name));
                            var first = snaps.Find(s => s.Page < ClothingPageBase);
                            if (first != null && first.Id != 0) ids.Add(first.Id);
                        }
                    }
                    catch { }
                    foreach (var id in ids)
                    {
                        var url = _coinsDb.GetItemImageUrl(id);
                        if (!string.IsNullOrEmpty(url))
                            lock (_itemImageUrls) _itemImageUrls[id] = url;
                    }
                });
            }

            // Hook events
            DamageTool.damagePlayerRequested += OnDamagePlayerRequested;
            BarricadeManager.onDamageBarricadeRequested += OnDamageBarricadeRequested;
            Rocket.Unturned.U.Events.OnPlayerConnected += OnPlayerConnected;
            Rocket.Unturned.Events.UnturnedPlayerEvents.OnPlayerDead += OnPlayerDead; // death backstop
            EffectManager.onEffectButtonClicked += OnUIClick; // Phase 8: loadout UI clicks
            ItemManager.onServerSpawningItemDrop += OnServerSpawningItemDrop; // block dropping in the arena
            PlayerLife.onPlayerDied += OnPlayerKilled;

            // Phase 3.5: Harmony patch to block escape commands during an event.
            try
            {
                _harmony = new HarmonyLib.Harmony("com.kothbox.plugin");
                _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
            }
            catch (Exception ex) { Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] Harmony patch"); }

            // On boot, wipe leftover wall barricades + dropped items inside every box
            // (a crash mid-event leaves them orphaned). Defer until the level/regions exist.
            if (Level.isLoaded) StartupCleanup();
            else Level.onLevelLoaded += OnLevelLoaded;

            ShowKothButtonToAll(); // re-show the floating button after a plugin reload

            UnturnedChat.Say("[PVP] Plugin loaded.", Color.green);
        }

        private void OnLevelLoaded(int level)
        {
            Level.onLevelLoaded -= OnLevelLoaded;
            StartupCleanup();
        }

        private void StartupCleanup()
        {
            if (_boxes == null) return;
            var wallId = Configuration.Instance.ArenaWallItemId;
            foreach (var box in _boxes.Boxes)
            {
                try { ItemManager.ServerClearItemsInSphere(box.GetCenter(), box.Radius); } catch { }
                if (wallId != 0) DestroyOrphanWalls(box, wallId);
            }
        }

        // Find + destroy any barricade of the wall item id left inside a box (orphans from a crash).
        private void DestroyOrphanWalls(KothBoxData box, ushort wallId)
        {
            if (BarricadeManager.regions == null) return;
            var center = box.GetCenter();
            var toRemove = new List<Transform>();
            foreach (BarricadeRegion region in BarricadeManager.regions)
            {
                if (region?.drops == null) continue;
                foreach (BarricadeDrop drop in region.drops)
                {
                    if (drop?.asset == null || drop.model == null) continue;
                    if (drop.asset.id == wallId && Vector3.Distance(drop.model.position, center) <= box.Radius + 6f)
                        toRemove.Add(drop.model);
                }
            }
            foreach (var t in toRemove) DestroyBarricadeTransform(t);
        }

        private static void DestroyBarricadeTransform(Transform t)
        {
            if (t == null) return;
            try
            {
                if (BarricadeManager.tryGetInfo(t, out byte x, out byte y, out ushort plant, out ushort _, out BarricadeRegion _, out BarricadeDrop drop))
                    BarricadeManager.destroyBarricade(drop, x, y, plant);
            }
            catch { }
        }

        private static void DestroyStructureTransform(Transform t)
        {
            if (t == null || StructureManager.regions == null) return;
            try
            {
                if (!Regions.tryGetCoordinate(t.position, out byte rx, out byte ry)) return;
                var region = StructureManager.regions[rx, ry];
                if (region?.drops == null) return;
                for (int i = 0; i < region.drops.Count; i++)
                {
                    var drop = region.drops[i];
                    if (drop?.model != t) continue;
                    StructureManager.destroyStructure(drop, rx, ry, Vector3.zero);
                    return;
                }
            }
            catch { }
        }

        protected override void Unload()
        {
            DamageTool.damagePlayerRequested -= OnDamagePlayerRequested;
            BarricadeManager.onDamageBarricadeRequested -= OnDamageBarricadeRequested;
            Rocket.Unturned.U.Events.OnPlayerConnected -= OnPlayerConnected;
            Rocket.Unturned.Events.UnturnedPlayerEvents.OnPlayerDead -= OnPlayerDead;
            EffectManager.onEffectButtonClicked -= OnUIClick;
            ItemManager.onServerSpawningItemDrop -= OnServerSpawningItemDrop;
            PlayerLife.onPlayerDied -= OnPlayerKilled;
            Level.onLevelLoaded -= OnLevelLoaded;
            ClearWall(); // tidy up the arena wall on plugin unload/reload
            try { _harmony?.UnpatchAll("com.kothbox.plugin"); } catch { }
            _dataManager?.SaveBoxes(_boxes);
            _dataManager?.SaveState(_eventState);
            _dataManager?.SaveRewards(_rewards);
            _dataManager?.SaveLeaderboard(_leaderboard);
            Instance = null;
            UnturnedChat.Say("[PVP] Plugin unloaded.", Color.yellow);
        }

        // Phase 3.5: reconnect recovery. If a player reconnects and their event is no
        // longer running (crash / mid-game quit), warp them back + restore their stash.
        private void OnPlayerConnected(UnturnedPlayer player)
        {
            if (player?.Player != null) ShowKothButton(player.Player); // floating open-menu button

            var participant = GetParticipant(player.CSteamID.m_SteamID);
            if (participant == null) return;

            bool eventStillRunning = _currentState != EventState.Idle &&
                                     _eventState.Participants.Contains(participant) &&
                                     _activeBox != null;
            if (!eventStillRunning)
            {
                RestoreParticipant(participant);
                _eventState.Participants.Remove(participant);
                _dataManager.SaveState(_eventState);
                UnturnedChat.Say(player, "[PVP] กลับมาแล้ว — คืน inventory แล้ว | Welcome back — inventory restored.", Color.green);
                return;
            }

            // Reconnected during active event — re-sync state
            if (_currentState == EventState.Warmup)
            {
                ulong sid = participant.SteamId;
                _inPrepRoom.Remove(sid); // allow re-teleport (in-memory state lost on disconnect)
                var plugin = this;
                var pl = player;
                Rocket.Core.Utils.TaskDispatcher.QueueOnMainThread(() =>
                {
                    if (pl?.Player == null) return;
                    // Clear items that may have dropped in PrepRoom on disconnect
                    Vector3? spawn = plugin._prepRoomSpawn;
                    if (!spawn.HasValue && plugin._prepRoom != null) spawn = plugin._prepRoom.GetSpawn();
                    if (spawn.HasValue)
                        try { ItemManager.ServerClearItemsInSphere(spawn.Value, 20f); } catch { }
                    plugin.TryTeleportToPrepRoom(pl.Player);
                }, 2f);
            }
        }

        // Block dropping items inside the arena during an event (no giving away the loadout,
        // and dead players' gear doesn't litter the floor). Covers manual + death drops.
        private void OnServerSpawningItemDrop(Item item, ref Vector3 location, ref bool shouldAllow)
        {
            if (_suppressItemDrops) { shouldAllow = false; return; }
            if (_activeBox == null) return;
            if (_currentState != EventState.Active && _currentState != EventState.Warmup) return;
            if (Vector3.Distance(location, _activeBox.GetCenter()) <= _activeBox.Radius)
                shouldAllow = false;
        }

        // Phase 5 backstop: if a participant actually dies (the damage intercept can miss
        // headshots/limb multipliers), force-respawn them in the dome with their loadout —
        // never leave them on the death/respawn screen.
        private void OnPlayerDead(UnturnedPlayer player, Vector3 position)
        {
            var participant = GetParticipant(player.CSteamID.m_SteamID);
            if (participant == null || _activeBox == null) return;
            if (_currentState != EventState.Active && _currentState != EventState.Warmup) return;
            try
            {
                player.Player.life.ServerRespawn(false);
                RespawnInDome(player.Player, participant);
            }
            catch (Exception ex) { Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] death backstop"); }
        }

        private void OnPlayerKilled(PlayerLife victim, EDeathCause cause, ELimb limb, CSteamID killer)
        {
            if (_currentState != EventState.Active) return;
            var killerId = killer.m_SteamID;
            if (killerId == 0 || killerId == victim.player.channel.owner.playerID.steamID.m_SteamID) return;
            if (GetParticipant(killerId) == null) return;
            byte amount = Configuration.Instance.KillHealAmount;
            if (amount == 0) return;
            _pendingHeal[killerId] = (_pendingHeal.TryGetValue(killerId, out int cur) ? cur : 0) + amount;
        }

        public override TranslationList DefaultTranslations => new TranslationList();

        // --- Phase 1: Box Management ---
        public void SaveBoxes() => _dataManager.SaveBoxes(_boxes);

        public void AddOrUpdateBox(string name, Vector3 center, float radius)
        {
            var existing = _boxes.Boxes.FirstOrDefault(b => b.Name == name);
            if (existing != null)
            {
                existing.SetCenter(center);
                existing.Radius = radius;
            }
            else
            {
                _boxes.Boxes.Add(new KothBoxData
                {
                    Id = (byte)_boxes.Boxes.Count,
                    Name = name,
                    Radius = radius
                });
                _boxes.Boxes.Last().SetCenter(center);
            }
            _dataManager.SaveBoxes(_boxes);
        }

        public bool DeleteBox(string name)
        {
            var box = _boxes.Boxes.FirstOrDefault(b => b.Name == name);
            if (box != null)
            {
                _boxes.Boxes.Remove(box);
                _dataManager.SaveBoxes(_boxes);
                return true;
            }
            return false;
        }

        public List<KothBoxData> GetAllBoxes() => _boxes.Boxes;

        private bool HasPlayersInside(KothBoxData box)
        {
            var center = box.GetCenter();
            foreach (var client in Provider.clients)
            {
                var p = client?.player;
                if (p?.transform != null && Vector3.Distance(p.transform.position, center) <= box.Radius)
                    return true;
            }
            return false;
        }

        public KothBoxData GetBox(string name) => _boxes.Boxes.FirstOrDefault(b => b.Name == name);

        public ParticipantData GetParticipant(ulong steamId) => _eventState.Participants.FirstOrDefault(p => p.SteamId == steamId);

        public void ReloadBoxes()
        {
            _boxes = _dataManager.LoadBoxes();
        }

        public void PreviewBox(KothBoxData box)
        {
            TriggerRingEffect(box.GetCenter(), box.Radius, 36);
        }

        // --- Phase 2: Event Lifecycle ---
        public void StartEvent(string boxName) => StartEvent(boxName, Configuration.Instance.EntryFee, 0);
        public void StartEvent(string boxName, uint fee) => StartEvent(boxName, fee, 0);

        // fee = the host-chosen entry fee, clamped to >= the configured minimum (EntryFee).
        // hostId = steamId of the player who started (gets free entry); 0 = no host bypass.
        public void StartEvent(string boxName, uint fee, ulong hostId)
        {
            // Already running -> ignore (prevents starting multiple events / stacking walls).
            if (_currentState != EventState.Idle)
            {
                UnturnedChat.Say("[PVP] An event is already running. Use /stopkoth first.");
                return;
            }

            KothBoxData box;
            if (boxName == "random" && _boxes.Boxes.Count > 0)
            {
                // Prefer boxes with no players already standing inside them.
                var empty = _boxes.Boxes.Where(b => !HasPlayersInside(b)).ToList();
                var pool = empty.Count > 0 ? empty : _boxes.Boxes;
                box = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            else
                box = GetBox(boxName);

            if (box == null) return;

            _activeBox = box;
            _currentState = EventState.Warmup;
            _stateTimer = 0;
            _eventState.CurrentBoxId = box.Id;
            _eventState.Participants.Clear();
            _prizePool = 0;
            _eventEntryFee = Math.Max(Configuration.Instance.EntryFee, fee);
            _eventHostId = hostId;

            SpawnPrepRoom();  // สร้างห้อง prep จาก template
            SpawnWall(box);   // ring of barricades = visible arena boundary
            if (!string.IsNullOrEmpty(box.BuildName)) LoadBoxBuild(box.BuildName);

            UnturnedChat.Say($"[PVP] Event เริ่มใน {Configuration.Instance.WarmupDuration}s — ค่าเข้า {_eventEntryFee} XP | Event starting in {Configuration.Instance.WarmupDuration}s — entry fee {_eventEntryFee} XP.");
        }

        public void StopEvent()
        {
            if (_currentState == EventState.Idle) return;

            ClearAllHuds();
            RestoreAllParticipants();
            ClearWall();                                 // remove the barricade ring
            UnloadBoxBuild();                            // remove per-box build if loaded
            DestroyPrepRoom();                           // ลบห้อง prep ออก
            if (Configuration.Instance.ClearItemsOnEnd && _activeBox != null)
                ClearItemsInDome(_activeBox);            // wipe dropped items inside the ring

            _currentState = EventState.Idle;
            _activeBox = null;
            _eventHostId = 0;
            _eventState.Participants.Clear();
            _pendingHeal.Clear();
            _dataManager.SaveState(_eventState);

            UnturnedChat.Say("[PVP] Event หยุดแล้ว | Event stopped.");
        }

        // --- Arena wall (barricade ring) ---
        private const int GroundMask = RayMasks.GROUND | RayMasks.ENVIRONMENT
            | RayMasks.LARGE | RayMasks.MEDIUM | RayMasks.SMALL;

        private void SpawnWall(KothBoxData box)
        {
            ClearWall();
            var cfg = Configuration.Instance;
            ushort id = cfg.ArenaWallItemId;
            if (id == 0) return;
            if (!(Assets.find(EAssetType.ITEM, id) is ItemBarricadeAsset asset)) return;

            // Segment count auto-scales with the perimeter (one every ArenaWallSpacing metres).
            float spacing = cfg.ArenaWallSpacing > 0f ? cfg.ArenaWallSpacing : 4f;
            int segments = cfg.ArenaWallSpacing > 0f
                ? Mathf.Max(12, Mathf.CeilToInt(2f * Mathf.PI * box.Radius / spacing))
                : Mathf.Max(12, cfg.ArenaWallSegments);

            var center = box.GetCenter();
            float twoPi = Mathf.PI * 2f;
            for (int i = 0; i < segments; i++)
            {
                float a = twoPi * i / segments;
                float rx = center.x + box.Radius * Mathf.Cos(a);
                float rz = center.z + box.Radius * Mathf.Sin(a);

                // Snap each segment onto the local ground/road (avoids sinking on uneven terrain).
                float y = center.y;
                if (Physics.Raycast(new Vector3(rx, center.y + 20f, rz), Vector3.down,
                        out RaycastHit hit, 80f, GroundMask))
                    y = hit.point.y;
                var pos = new Vector3(rx, y, rz);

                // Face the wall's front toward the centre so segments line up into a closed ring.
                float yaw = Mathf.Atan2(-Mathf.Cos(a), -Mathf.Sin(a)) * Mathf.Rad2Deg + cfg.ArenaWallYawOffset;

                try
                {
                    var rot = BarricadeManager.getRotation(asset, 0f, yaw, 0f);
                    var t = BarricadeManager.dropNonPlantedBarricade(new Barricade(asset), pos, rot, 0UL, 0UL);
                    if (t != null) _wallBarricades.Add(t);
                }
                catch (Exception ex) { Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] wall segment drop"); }
            }
        }

        private void ClearWall()
        {
            foreach (var t in _wallBarricades)
                DestroyBarricadeTransform(t);
            _wallBarricades.Clear();
        }

        private void ClearItemsInDome(KothBoxData box)
        {
            try { ItemManager.ServerClearItemsInSphere(box.GetCenter(), box.Radius); }
            catch (Exception ex) { Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] clear items"); }
        }

        private void ClearPrepRoomGroundItems()
        {
            Vector3? spawn = _prepRoomSpawn;
            if (!spawn.HasValue && _prepRoom != null) spawn = _prepRoom.GetSpawn();
            if (!spawn.HasValue) return;
            try { ItemManager.ServerClearItemsInSphere(spawn.Value, 30f); } catch { }
        }

        private void RestoreAllParticipants()
        {
            foreach (var participant in _eventState.Participants)
            {
                RestoreParticipant(participant);
            }
        }

        // Teleport back + restore stash for one participant. Idempotent: a missing
        // stash just means already restored. Safe to call from event-end and reconnect.
        private void RestoreParticipant(ParticipantData participant)
        {
            // Re-enable Knockdown for them now that they're leaving the arena.
            KothKnockdownBridge.SetOptOut(participant.SteamId, false);
            _awaitingRespawn.Remove(participant.SteamId);

            var player = PlayerTool.getPlayer(new CSteamID(participant.SteamId));
            if (player == null) return;
            ClearPlayerHud(player);
            player.teleportToLocation(participant.GetReturnPos(), player.look?.yaw ?? 0f);
            RestoreInventory(player, participant.StashFile);
            _dataManager.DeleteStash(participant.SteamId);
            _dataManager.DeleteDomeEntry(participant.SteamId);
        }

        private void RestoreInventory(Player player, string stashPath)
        {
            if (player?.inventory == null) return;
            // GiveKit strips the arena kit (items + clothing) and restores the stashed set + clothing.
            GiveKit(player, InventoryStash.Deserialize(stashPath));
        }

        private void FixedUpdate()
        {
            // Auto-hide the gacha result panel (runs even between events).
            if (_gachaHideAt > 0f && Time.realtimeSinceStartup >= _gachaHideAt) HideGachaResult();

            TickPendingClear(); // ลบ items ที่ตกหลัง savepreproom (runs even when idle)

            if (_currentState == EventState.Idle) return;

            float dt = Time.fixedDeltaTime;
            _stateTimer += dt;

            if (_activeBox != null && (_currentState == EventState.Warmup || _currentState == EventState.Active))
            {
                // Every frame: keep participants inside the dome (warp back if they leave).
                ContainParticipants();

                _domeRenderTimer += dt;
                if (_domeRenderTimer >= 1f)
                {
                    _domeRenderTimer = 0f;
                    RenderDome();
                    var ranked = _eventState.Participants.OrderByDescending(p => p.CumulativeSeconds).ToList();
                    UpdateHuds(ranked);
                    // Update streak panel per-player (progress bar + achieved overlays).
                    foreach (var part in ranked)
                    {
                        var pl = PlayerTool.getPlayer(new Steamworks.CSteamID(part.SteamId));
                        if (pl != null) UpdateStreakProgress(pl, part);
                    }
                    RefillMagazines(); // top up everyone's spare magazines (arena = infinite ammo)
                    TickAutoJoin(1f);  // auto-join non-participants standing in the zone
                    if (_currentState == EventState.Active) ClearItemsInDome(_activeBox); // clear zombie drops every 1s
                }

                TickPrepRoomRestock(dt); // restock prep room storage boxes ทุก 5 วิ
            }

            switch (_currentState)
            {
                case EventState.Warmup:
                    TickWarmup();
                    break;
                case EventState.Active:
                    TickActive();
                    break;
                case EventState.Ended:
                    TickEnded();
                    break;
            }
        }

        private void TickWarmup()
        {
            if (_stateTimer >= Configuration.Instance.WarmupDuration)
            {
                _currentState = EventState.Active;
                _stateTimer = 0;
                ForceEnterDome(); // warmup หมด → วาปทุกคนในห้อง prep เข้า dome

                UnturnedChat.Say("[PVP] Event เริ่มแล้ว! | Event started!");
            }
        }

        private void TickActive()
        {
            float dt = Time.fixedDeltaTime;

            // Accrue time for everyone in the event (they're kept inside by ContainParticipants).
            foreach (var participant in _eventState.Participants)
            {
                participant.CumulativeSeconds += dt;
                if (participant.CumulativeSeconds >= Configuration.Instance.WinningTime)
                {
                    EndEvent(participant.SteamId);
                    return;
                }
            }

            // Kill-heal: tick every 1 second
            _healTickTimer += dt;
            if (_healTickTimer >= 1f && _pendingHeal.Count > 0)
            {
                _healTickTimer = 0f;
                byte tick = Configuration.Instance.KillHealTickAmount;
                var done = new List<ulong>();
                foreach (var kv in new Dictionary<ulong, int>(_pendingHeal))
                {
                    var p = PlayerTool.getPlayer(new CSteamID(kv.Key));
                    if (p == null) { done.Add(kv.Key); continue; }
                    byte give = (byte)Math.Min(tick, kv.Value);
                    p.life.askHeal(give, false, false);
                    int remain = kv.Value - give;
                    if (remain <= 0) done.Add(kv.Key);
                    else _pendingHeal[kv.Key] = remain;
                }
                foreach (var id in done) _pendingHeal.Remove(id);
            }
            else if (_healTickTimer >= 1f)
            {
                _healTickTimer = 0f;
            }

            // Check duration
            if (_stateTimer >= Configuration.Instance.EventDuration)
            {
                EndEvent(0);
            }
        }

        private void TickEnded()
        {
            // Award rewards
            AwardRewards();
            StopEvent();
        }

        private bool IsInDome(Vector3 pos)
        {
            if (_activeBox == null) return false;
            return Vector3.Distance(pos, _activeBox.GetCenter()) <= _activeBox.Radius;
        }

        // Random spawn: tries 16 candidates, picks the one farthest from all participants
        // (spread spawn — won't appear right in front of an enemy).
        private Vector3 RandomDomeSpawn()
        {
            var c = _activeBox.GetCenter();
            var occupied = _eventState.Participants
                .Select(p => PlayerTool.getPlayer(new CSteamID(p.SteamId)))
                .Where(p => p?.transform != null)
                .Select(p => p.transform.position)
                .ToList();

            Vector3 best = GroundSnap(c.x, c.z, c.y);
            float bestDist = -1f;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                var p2d = UnityEngine.Random.insideUnitCircle * (_activeBox.Radius * 0.8f);
                var spawn = GroundSnap(c.x + p2d.x, c.z + p2d.y, c.y);
                if (SDG.Framework.Water.WaterUtility.isPointUnderwater(spawn)) continue;

                float minDist = occupied.Count == 0 ? float.MaxValue
                    : occupied.Min(op => Vector3.Distance(spawn, op));

                if (minDist > bestDist) { bestDist = minDist; best = spawn; }
            }
            return best;
        }

        private Vector3 GroundSnap(float x, float z, float refY)
        {
            float y = refY;
            if (Physics.Raycast(new Vector3(x, refY + 100f, z), Vector3.down,
                    out RaycastHit hit, 250f, GroundMask))
                y = hit.point.y;
            return new Vector3(x, y + 1.5f, z);
        }

        // Keep every participant inside the dome: the instant they cross the boundary,
        // warp them back to the box centre. The only way out is /leavekoth.
        private void ContainParticipants()
        {
            if (_activeBox == null) return;
            var center = _activeBox.GetCenter();
            foreach (var participant in _eventState.Participants)
            {
                // ข้ามคนที่รออยู่ในห้อง prep room (warmup) — อย่า teleport เข้า dome ก่อนเวลา
                if (_inPrepRoom.Contains(participant.SteamId)) continue;

                var player = PlayerTool.getPlayer(new CSteamID(participant.SteamId));
                if (player?.transform == null) continue;
                if (!IsInDome(player.transform.position))
                    player.teleportToLocation(center + Vector3.up * 2f, player.look?.yaw ?? 0f);

                // Infinite stamina (no fatigue). serverModifyStamina only sends when not already full.
                if (player.life != null)
                {
                    player.life.serverModifyStamina(byte.MaxValue);
                    // กันเลือดไหลและกระดูกหักในวง
                    if (player.life.isBleeding) try { player.life.serverSetBleeding(false); } catch { }
                }
            }
        }

        // Auto-join: any non-participant standing inside the zone is auto-joined after a
        // countdown (chat-warned each second). Leaving the zone cancels the countdown.
        private void TickAutoJoin(float dt)
        {
            var cfg = Configuration.Instance;
            if (!cfg.AutoJoinEnabled || _activeBox == null) return;

            float radius = cfg.AutoJoinRadius > 0f ? cfg.AutoJoinRadius : _activeBox.Radius;
            var center = _activeBox.GetCenter();

            foreach (var client in Provider.clients)
            {
                var p = client?.player;
                if (p?.transform == null) continue;
                ulong sid = client.playerID.steamID.m_SteamID;

                if (GetParticipant(sid) != null) { _autoJoinTimers.Remove(sid); continue; }

                if (Vector3.Distance(p.transform.position, center) > radius)
                {
                    _autoJoinTimers.Remove(sid);
                    continue;
                }

                var up = UnturnedPlayer.FromCSteamID(client.playerID.steamID);
                if (up == null) continue;

                // Block auto-join countdown if player can't afford the fee
                if (_eventEntryFee > 0 && up.Experience < _eventEntryFee)
                {
                    _autoJoinTimers.Remove(sid);
                    UnturnedChat.Say(up, $"[PVP] XP ไม่พอเข้า KOTH (ต้องการ {_eventEntryFee} XP)", Color.red);
                    continue;
                }

                float t = (_autoJoinTimers.TryGetValue(sid, out var v) ? v : 0f) + dt;
                _autoJoinTimers[sid] = t;

                int remaining = Mathf.CeilToInt(cfg.AutoJoinCountdown - t);

                if (remaining <= 0)
                {
                    _autoJoinTimers.Remove(sid);
                    JoinEvent(up, 0); // auto-join with the default loadout
                }
                else
                {
                    UnturnedChat.Say(up, $"[PVP] Auto-join ใน {remaining}s — ออกจากโซนเพื่อยกเลิก | Auto-joining in {remaining}s — leave the zone to cancel.", Color.cyan);
                }
            }
        }

        // Top up every participant's spare magazines to full so the arena never runs dry.
        private void RefillMagazines()
        {
            foreach (var participant in _eventState.Participants)
            {
                var player = PlayerTool.getPlayer(new CSteamID(participant.SteamId));
                if (player?.inventory == null) continue;

                for (byte page = 0; page < PlayerInventory.STORAGE; page++)
                {
                    var grid = player.inventory.items[page];
                    if (grid == null) continue;
                    byte count = grid.getItemCount();
                    for (byte i = 0; i < count; i++)
                    {
                        var jar = grid.getItem(i);
                        if (jar?.item == null) continue;
                        if (Assets.find(EAssetType.ITEM, jar.item.id) is ItemMagazineAsset mag
                            && mag.amount > 0 && jar.item.amount < mag.amount)
                        {
                            player.inventory.sendUpdateAmount(page, jar.x, jar.y, mag.amount);
                        }
                    }
                }
            }
        }

        private void EndEvent(ulong winnerId)
        {
            _currentState = EventState.Ended;
            if (winnerId != 0)
            {
                var winner = _eventState.Participants.FirstOrDefault(p => p.SteamId == winnerId);
                if (winner != null)
                {
                    var winnerSp = PlayerTool.getSteamPlayer(new CSteamID(winnerId));
                    var winnerDisplayName = winnerSp?.playerID.characterName ?? winnerId.ToString();
                    UnturnedChat.Say($"[PVP] {winnerDisplayName} ชนะ KOTH! | {winnerDisplayName} won the event!");
                }
            }
        }

        private void AwardRewards()
        {
            var sorted = _eventState.Participants.OrderByDescending(p => p.CumulativeSeconds).ToList();
            if (sorted.Count == 0) { _prizePool = 0; return; }

            UnturnedChat.Say("=== PVP Deathmatch Results ===");
            var tiers = Configuration.Instance.RewardTiers ?? new List<RewardTier>();
            var splits = Configuration.Instance.PrizeSplitPercents ?? new List<int> { 50, 30, 20 };
            for (int i = 0; i < sorted.Count; i++)
            {
                var part = sorted[i];
                RewardTier tier = i < tiers.Count ? tiers[i] : null;

                // Prize pool split by rank (e.g. 50/30/20) + fixed tier XP.
                long poolShare = (i < splits.Count) ? (long)_prizePool * splits[i] / 100 : 0;
                long xpTotal = (tier?.Experience ?? 0) + poolShare;

                var player = PlayerTool.getPlayer(new CSteamID(part.SteamId));

                // XP: if pool > 1500 → queue for /claimkoth; else credit directly now.
                bool poolBig = _prizePool > 1500;
                if (xpTotal > 0)
                {
                    if (poolBig)
                    {
                        var existing2 = _rewards.Rewards.FirstOrDefault(r => r.SteamId == part.SteamId);
                        if (existing2 != null) existing2.Experience += (uint)xpTotal;
                        else _rewards.Rewards.Add(new PendingRewardData { SteamId = part.SteamId, Experience = (uint)xpTotal });
                    }
                    else if (player?.skills != null)
                    {
                        player.skills.ServerModifyExperience((int)xpTotal);
                    }
                }

                // Gacha draws for this rank → queue all results for /claimkoth.
                int draws = tier?.GachaDraws ?? 0;
                int gachaCount = 0;
                if (draws > 0 && _coinsDb != null)
                {
                    var pending = _rewards.Rewards.FirstOrDefault(r => r.SteamId == part.SteamId);
                    for (int d = 0; d < draws; d++)
                    {
                        var prize = _coinsDb.DrawGachaPrize();
                        if (prize == null) continue;
                        if (pending == null) { pending = new PendingRewardData { SteamId = part.SteamId }; _rewards.Rewards.Add(pending); }
                        switch (prize.Type)
                        {
                            case "coins":
                            case "meowcoins":
                                pending.Experience += (uint)prize.Amount;
                                break;
                            case "item":
                                for (int k = 0; k < Math.Max(1, (int)prize.Amount) && k < 10; k++)
                                    pending.ItemIds.Add((ushort)prize.RefId);
                                gachaCount++;
                                break;
                            case "vehicle":
                                pending.VehicleId = (ushort)prize.RefId;
                                gachaCount++;
                                break;
                        }
                    }
                }

                // Leaderboard (winner gets a win; everyone's kills tallied).
                var entry = _leaderboard.Entries.FirstOrDefault(e => e.SteamId == part.SteamId);
                if (entry == null) { entry = new LeaderboardEntry { SteamId = part.SteamId }; _leaderboard.Entries.Add(entry); }
                if (i == 0) entry.Wins++;
                entry.Kills += part.Kills;
                var sp = PlayerTool.getSteamPlayer(new CSteamID(part.SteamId));
                if (sp != null) entry.PlayerName = sp.playerID.characterName;

                string name = sp?.playerID.characterName ?? part.SteamId.ToString();
                bool hasClaim = poolBig || gachaCount > 0;
                UnturnedChat.Say($"#{i + 1} {name} — {part.CumulativeSeconds:F0}s — {xpTotal} XP"
                    + (draws > 0 ? $" + {draws} gacha" : "")
                    + (hasClaim ? " (/claimkoth)" : ""));

                // Winner #1 also pulls a random gacha prize (broadcast to everyone).
                if (i == 0 && Configuration.Instance.GachaRewardForWinner && _coinsDb != null)
                    AwardGachaToWinner(part.SteamId, name);
            }

            _prizePool = 0;
            _dataManager.SaveRewards(_rewards);
            _dataManager.SaveLeaderboard(_leaderboard);
        }

        // Draw one weighted prize from the shop gacha pool, deliver it, and announce it to all.
        private void AwardGachaToWinner(ulong steamId, string winnerName)
        {
            var prize = _coinsDb.DrawGachaPrize();
            if (prize == null) return;

            switch (prize.Type)
            {
                case "coins":
                case "meowcoins":
                {
                    // gacha gave coins/meowcoins → give XP instead
                    var gp = PlayerTool.getPlayer(new CSteamID(steamId));
                    if (gp?.skills != null) gp.skills.ServerModifyExperience((int)prize.Amount);
                    else _rewards.Rewards.Add(new PendingRewardData { SteamId = steamId, Experience = (uint)prize.Amount });
                    break;
                }
                case "item":
                    var ids = new List<ushort>();
                    for (int k = 0; k < Math.Max(1, (int)prize.Amount) && k < 10; k++) ids.Add((ushort)prize.RefId);
                    _rewards.Rewards.Add(new PendingRewardData { SteamId = steamId, ItemIds = ids });
                    break;
                case "vehicle":
                    _rewards.Rewards.Add(new PendingRewardData { SteamId = steamId, VehicleId = (ushort)prize.RefId });
                    break;
                // "vip" -> announce only (granted manually / by the VIP system)
            }

            bool needsClaim = prize.Type == "item" || prize.Type == "vehicle";
            UnturnedChat.Say($"[PVP] {winnerName} ได้รางวัล Lucky Draw: {prize.Label}!"
                + (needsClaim ? " (/claimkoth)" : "")
                + $" | {winnerName} won lucky draw: {prize.Label}!"
                + (needsClaim ? " (/claimkoth)" : ""), Color.magenta);
            ShowGachaResult(winnerName, prize.Label); // panel for everyone (if bundle configured)
        }

        // Render the dome: prefer the mesh shell (white during warmup, green active);
        // fall back to stacked rings if no mesh id is set. Re-triggered ~1s so the
        // short-lifetime effect looks continuous and late joiners pick it up.
        private void RenderDome()
        {
            if (_activeBox == null) return;
            ushort meshId = _currentState == EventState.Warmup
                ? Configuration.Instance.DomeWarmupEffectId
                : Configuration.Instance.DomeActiveEffectId;

            if (meshId != 0)
            {
                var asset = Assets.find(EAssetType.EFFECT, meshId) as EffectAsset;
                if (asset != null)
                {
                    EffectManager.triggerEffect(new TriggerEffectParameters(asset)
                    {
                        position = _activeBox.GetCenter(),
                        relevantDistance = _activeBox.Radius + 64f,
                        reliable = false
                    });
                    return;
                }
            }
            TriggerRingEffect(_activeBox.GetCenter(), _activeBox.Radius, 36);
        }

        // Dome fallback (no workshop bundle yet): stack 3 effect rings to fake a dome shell.
        // Uses Configuration.DomeRingEffectId; if 0, no-op (gameplay unaffected).
        private void TriggerRingEffect(Vector3 center, float radius, int points)
        {
            ushort effectId = Configuration.Instance.DomeRingEffectId;
            if (effectId == 0) return;
            var asset = Assets.find(EAssetType.EFFECT, effectId) as EffectAsset;
            if (asset == null) return;

            float twoPi = Mathf.PI * 2f;
            // 3 horizontal rings at different heights/radii approximate a dome.
            float[] heights = { 0.5f, radius * 0.5f, radius * 0.85f };
            float[] radii = { radius, radius * 0.85f, radius * 0.5f };
            for (int h = 0; h < heights.Length; h++)
            {
                for (int i = 0; i < points; i++)
                {
                    float a = twoPi * i / points;
                    var pos = center + new Vector3(radii[h] * Mathf.Cos(a), heights[h], radii[h] * Mathf.Sin(a));
                    var p = new TriggerEffectParameters(asset) { position = pos, relevantDistance = 96f, reliable = false };
                    EffectManager.triggerEffect(p);
                }
            }
        }

        // --- Phase 3: Join / Stash / Restore ---
        public void JoinEvent(UnturnedPlayer player)
        {
            JoinEvent(player, 0);
        }

        // Phase 3 + 8: loadoutIndex chosen from the loadout UI (defaults to first).
        public void JoinEvent(UnturnedPlayer player, int loadoutIndex)
        {
            // Warmup only — Active phase is locked to prevent mid-game snipers.
            if (_currentState == EventState.Active)
            {
                UnturnedChat.Say(player, "[PVP] เกมเริ่มไปแล้ว รอรอบหน้า | Game already started. Wait for the next round.", Color.red);
                return;
            }
            if (_currentState != EventState.Warmup)
            {
                UnturnedChat.Say(player, "[PVP] ยังไม่มี event | No event is running.", Color.red);
                return;
            }

            var steamId = player.CSteamID.m_SteamID;
            var existing = _eventState.Participants.FirstOrDefault(p => p.SteamId == steamId);
            if (existing != null)
            {
                UnturnedChat.Say(player, "[PVP] คุณอยู่ใน event แล้ว | You are already in the event.", Color.red);
                return;
            }


            // Entry fee: deduct XP. Host (event creator) joins free.
            uint fee = _eventEntryFee;
            bool isHost = _eventHostId != 0 && steamId == _eventHostId;
            if (fee > 0 && !isHost)
            {
                if (player.Experience < fee)
                {
                    UnturnedChat.Say(player, $"[PVP] XP ไม่พอ (ต้องการ {fee} XP)", Color.red);
                    return;
                }
                player.Experience -= fee;
                _prizePool += fee;
                UnturnedChat.Say(player, $"[PVP] หัก {fee} XP | Prize pool: {_prizePool} XP", Color.yellow);
            }

            // Phase 3: Stash inventory + worn clothing to disk FIRST (must persist before we clear it).
            var stashPath = _dataManager.GetStashPath(steamId);
            var snapshot = SnapshotKit(player.Player);
            InventoryStash.Serialize(stashPath, snapshot);
            ClearGrid(player.Player);

            var loadout = PickLoadout(loadoutIndex);
            var participant = new ParticipantData
            {
                SteamId = steamId,
                LoadoutName = loadout?.Name ?? "none",
                CumulativeSeconds = 0,
                StashFile = stashPath
            };
            participant.SetReturnPos(player.Position);
            _eventState.Participants.Add(participant);
            _dataManager.SaveState(_eventState);

            // Opt the player out of Knockdown while in the event so lethal hits kill +
            // trigger our in-dome respawn instead of leaving them downed.
            KothKnockdownBridge.SetOptOut(steamId, true);

            // ถ้า warmup อยู่และมีห้อง prep → วาปเข้าห้อง prep รอ; ถ้าไม่มีหรือ active → เข้า dome เลย
            GiveSelectedKit(player.Player, participant, loadoutIndex);
            if (!TryTeleportToPrepRoom(player.Player) && _activeBox != null)
                player.Player.teleportToLocation(RandomDomeSpawn(), player.Rotation);

            // Show streak panel to the joining player.
            if (Configuration.Instance.StreakUIEffectId != 0)
                ShowStreakUI(player.Player);

            UnturnedChat.Say(player, $"[PVP] เข้าร่วมด้วย '{participant.LoadoutName}' — คลัง stash แล้ว | Joined '{participant.LoadoutName}'. Inventory stashed.", Color.green);
            UnturnedChat.Say(player, "[PVP] เพื่อนของคุณสามารถพิมพ์ /jkoth เพื่อเข้าร่วมได้เลย! | Your friends can type /jkoth to join!", Color.cyan);
        }

        private Loadout PickLoadout(int index)
        {
            var loadouts = Configuration.Instance.Loadouts;
            if (loadouts == null || loadouts.Count == 0) return null;
            if (index < 0 || index >= loadouts.Count) index = 0;
            return loadouts[index];
        }

        private void GiveLoadout(Player player, Loadout loadout)
        {
            if (player == null || loadout?.ItemIds == null) return;
            foreach (var id in loadout.ItemIds)
            {
                var item = new Item(id, true); // full magazine/stack
                if (!player.inventory.tryAddItem(item, true))
                    ItemManager.dropItem(item, player.transform.position, false, true, true);
            }
        }

        // --- Phase 3: Inventory snapshot / restore (pages 0..STORAGE = held + pockets) ---
        private static List<ItemSnapshot> SnapshotGrid(Player p)
        {
            var items = new List<ItemSnapshot>();
            if (p?.inventory == null) return items;
            for (byte page = 0; page < PlayerInventory.STORAGE; page++)
            {
                var grid = p.inventory.items[page];
                if (grid == null) continue;
                byte count = grid.getItemCount();
                for (byte i = 0; i < count; i++)
                {
                    var jar = grid.getItem(i);
                    if (jar?.item == null) continue;
                    items.Add(new ItemSnapshot
                    {
                        Page = page,
                        X = jar.x,
                        Y = jar.y,
                        Rot = jar.rot,
                        Id = jar.item.id,
                        Amount = jar.item.amount,
                        Quality = jar.item.quality,
                        StateData = jar.item.state != null ? Convert.ToBase64String(jar.item.state) : ""
                    });
                }
            }
            return items;
        }

        private static void ClearGrid(Player p)
        {
            if (p?.inventory == null) return;
            for (byte page = 0; page < PlayerInventory.STORAGE; page++)
            {
                var grid = p.inventory.items[page];
                if (grid == null) continue;
                for (int i = grid.getItemCount() - 1; i >= 0; i--)
                {
                    try { grid.removeItem((byte)i); } catch { }
                }
            }
        }

        public void ClaimRewards(UnturnedPlayer player)
        {
            var steamId = player.CSteamID.m_SteamID;
            var reward = _rewards.Rewards.FirstOrDefault(r => r.SteamId == steamId);
            if (reward == null)
            {
                UnturnedChat.Say(player, "[PVP] You have no pending rewards.", Color.yellow);
                return;
            }

            // XP (main thread).
            if (reward.Experience > 0 && player.Player?.skills != null)
                player.Player.skills.ServerModifyExperience((int)reward.Experience);

            // Items from the reward tier (full stack/magazine; drops to ground if inventory full).
            if (reward.ItemIds != null && player.Player != null)
            {
                foreach (var id in reward.ItemIds)
                {
                    var item = new Item(id, true);
                    if (!player.Player.inventory.tryAddItem(item, true))
                        ItemManager.dropItem(item, player.Player.transform.position, false, true, true);
                }
            }

            // Vehicle (spawned at the player's feet).
            if (reward.VehicleId > 0 && player.Player != null)
                VehicleManager.spawnVehicleV2(reward.VehicleId, player.Player.transform.position + Vector3.up, Quaternion.identity);

            _rewards.Rewards.Remove(reward);
            _dataManager.SaveRewards(_rewards);

            UnturnedChat.Say(player, $"[PVP] Claimed:" +
                (reward.Experience > 0 ? $" {reward.Experience} XP" : "") +
                (reward.ItemIds?.Count > 0 ? $", {reward.ItemIds.Count} items" : "") +
                (reward.VehicleId > 0 ? ", vehicle" : "") + ".", Color.green);
        }

        public void ShowLeaderboard(IRocketPlayer caller, int limit)
        {
            var sorted = _leaderboard.Entries.OrderByDescending(e => e.Wins).Take(limit).ToList();

            if (sorted.Count == 0)
            {
                UnturnedChat.Say(caller, "[PVP] Leaderboard is empty.", Color.yellow);
                return;
            }

            UnturnedChat.Say(caller, $"[PVP] Top {limit} Players:", Color.cyan);
            for (int i = 0; i < sorted.Count; i++)
            {
                var entry = sorted[i];
                var name = string.IsNullOrEmpty(entry.PlayerName) ? entry.SteamId.ToString() : entry.PlayerName;
                UnturnedChat.Say(caller, $"  {i + 1}. {name} - {entry.Wins} wins, {entry.Kills} kills",
                    Color.white);
            }
        }

        private void OnDamagePlayerRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow)
        {
            var victim = parameters.player;
            if (victim?.transform == null) return;

            if (_activeBox == null || _currentState == EventState.Idle) return;

            // PrepRoom players are invincible
            ulong victimSid = victim.channel.owner.playerID.steamID.m_SteamID;
            if (_inPrepRoom.Contains(victimSid)) { shouldAllow = false; return; }

            bool vicInDome = IsInDome(victim.transform.position);

            // Phase 4: Bullet blocking. parameters.killer is a CSteamID — resolve to a Player.
            // Block any damage crossing the dome boundary (one party in, one out).
            if (parameters.killer != CSteamID.Nil)
            {
                var shooter = PlayerTool.getPlayer(parameters.killer);
                if (shooter?.transform != null && shooter != victim)
                {
                    bool shooterInDome = IsInDome(shooter.transform.position);
                    if (vicInDome != shooterInDome)
                    {
                        shouldAllow = false;
                        return;
                    }

                    // Phase 7: count kills inside the dome for streak/killfeed.
                    float incoming = parameters.damage * parameters.times;
                    if (vicInDome && incoming >= victim.life.health)
                        OnDomeKill(parameters.killer, victim);
                }
            }

            // Phase 5: any lethal hit on a participant -> intercept and respawn them in the
            // dome with their original loadout (never fall through to the death/respawn screen).
            var participant = GetParticipant(victim.channel.owner.playerID.steamID.m_SteamID);
            if (participant != null)
            {
                float incoming = parameters.damage * parameters.times;
                if (incoming >= victim.life.health)
                {
                    shouldAllow = false;
                    RespawnInDome(victim, participant);
                }
            }
        }

        // /leavekoth: the only way out. Warp back to entry + restore inventory.
        public void LeaveEventByCommand(UnturnedPlayer player)
        {
            var participant = GetParticipant(player.CSteamID.m_SteamID);
            if (participant == null)
            {
                UnturnedChat.Say(player, "[PVP] คุณไม่ได้อยู่ใน event | You are not in the event.", Color.red);
                return;
            }
            player.Player.life.serverModifyHealth(100);
            RestoreParticipant(participant);
            _eventState.Participants.Remove(participant);
            _dataManager.SaveState(_eventState);
            UnturnedChat.Say(player, "[PVP] ออกจากสนามแล้ว ยินดีต้อนรับกลับ | Left the arena. Welcome back.", Color.yellow);

            // Last one out -> end the empty event so a fresh one can be hosted.
            if (_eventState.Participants.Count == 0 && _currentState != EventState.Idle)
                StopEvent();
        }

        // Phase 5: refill HP + stop status effects, re-give kit, teleport inside dome.
        private void RespawnInDome(Player victim, ParticipantData participant)
        {
            if (_activeBox == null) return;
            victim.life.serverModifyHealth(100);
            victim.life.serverModifyFood(100);
            victim.life.serverModifyWater(100);
            try { victim.life.serverSetBleeding(false); } catch { }

            // reset kill streak on death; cumulative Kills stays for scoreboard
            participant.StreakKills = 0;
            _streakBanner.Remove(participant.SteamId);
            _inPrepRoom.Remove(participant.SteamId);

            // คืน kit: ถ้าเลือก kit เฉพาะตอน join → ใช้อันเดิม; ไม่งั้น auto-resolve
            if (!string.IsNullOrEmpty(participant.KitPath))
            {
                var snaps = InventoryStash.Deserialize(participant.KitPath);
                if (snaps.Count > 0) GiveKit(victim, snaps);
                else GiveParticipantKit(victim, participant);
            }
            else
                GiveParticipantKit(victim, participant);

            victim.teleportToLocation(RandomDomeSpawn(), victim.look?.yaw ?? 0f);

            // re-show streak panel (in case it cleared on death screen)
            if (Configuration.Instance.StreakUIEffectId != 0)
                ShowStreakUI(victim);

            var up = UnturnedPlayer.FromPlayer(victim);
            if (up != null) UnturnedChat.Say(up, "[PVP] ฟื้นคืนชีพ! | Respawned!", Color.cyan);
        }

        // Phase 7: killfeed + streak.
        private void OnDomeKill(CSteamID killerId, Player victim)
        {
            var killer = GetParticipant(killerId.m_SteamID);
            if (killer != null) { killer.Kills++; killer.StreakKills++; }

            // Kill flash UI (shown 2.5s then auto-hides in UpdateHuds).
            var killerPl = PlayerTool.getPlayer(killerId);
            if (killerPl != null && killer != null)
                ShowKillFlash(killerPl, killer.StreakKills);

            var killerName = PlayerTool.getSteamPlayer(killerId)?.playerID.characterName ?? "?";
            var victimName = victim.channel.owner.playerID.characterName;
            UnturnedChat.Say($"[PVP] {killerName} ฆ่า {victimName}" +
                             (killer != null ? $" (streak: {killer.StreakKills})" : ""), Color.yellow);

            // Kill streak item rewards
            if (killer == null) return;
            var streaks = Configuration.Instance.KillStreakRewards;
            if (streaks == null) return;
            var killerPlayer = PlayerTool.getPlayer(killerId);
            if (killerPlayer == null) return;
            foreach (var sr in streaks)
            {
                if (killer.StreakKills != sr.Kills || sr.ItemId == 0) continue;
                byte qty = sr.Amount > 0 ? sr.Amount : (byte)1;
                for (byte n = 0; n < qty; n++)
                    killerPlayer.inventory.tryAddItem(new Item(sr.ItemId, true), true);
                var ksp = PlayerTool.getSteamPlayer(killerId);
                string itemName = GetItemName(sr.ItemId);
                if (ksp != null) UnturnedChat.Say(UnturnedPlayer.FromSteamPlayer(ksp), $"[PVP] {sr.Kills} คิล! ได้รับ x{qty} {itemName} | {sr.Kills} kills! Got x{qty} {itemName}.", Color.cyan);
                ShowStreakBanner(killerPlayer, $"★ {sr.Kills} KILLS — {itemName}!", 4f);
            }
        }

        // Track which participant last hit each zombie in the dome (for kill credit on death).
        private void OnDamageZombieRequested(ref DamageZombieParameters parameters, ref bool shouldAllow)
        {
            if (_currentState != EventState.Active || _activeBox == null) return;
            var zombie = parameters.zombie;
            if (zombie?.transform == null) return;
            if (Vector3.Distance(zombie.transform.position, _activeBox.GetCenter()) > _activeBox.Radius) return;

            Player killerPlayer = null;
            if (parameters.instigator is CSteamID cid)
                killerPlayer = PlayerTool.getPlayer(cid);
            else if (parameters.instigator is Player dp)
                killerPlayer = dp;
            else if (parameters.instigator is Transform tf)
                killerPlayer = tf.GetComponent<Player>() ?? tf.GetComponentInParent<Player>();
            if (killerPlayer == null) return;

            ulong sid = killerPlayer.channel.owner.playerID.steamID.m_SteamID;
            if (GetParticipant(sid) == null) return;
            _zombieLastHit[zombie] = sid;
        }

        // Poll tracked zombies; credit kill once isDead (no onZombieDead event in this Unturned version).
        private void CheckZombieKills()
        {
            if (_zombieLastHit.Count == 0) return;
            var toCredit = new List<(Zombie z, ulong sid)>();
            foreach (var kv in _zombieLastHit)
            {
                var z = kv.Key;
                if (z == null || z.isDead)
                    toCredit.Add((z, kv.Value));
            }
            foreach (var (z, sid) in toCredit)
            {
                _zombieLastHit.Remove(z);
                if (z == null) continue; // zombie was destroyed, no credit
                var killer = GetParticipant(sid);
                if (killer == null) continue;
                var killerPlayer = PlayerTool.getPlayer(new Steamworks.CSteamID(sid));
                if (killerPlayer == null) continue;

                killer.Kills++; killer.StreakKills++;
                ShowKillFlash(killerPlayer, killer.StreakKills);

                var streaks = Configuration.Instance.KillStreakRewards;
                if (streaks == null) continue;
                foreach (var sr in streaks)
                {
                    if (killer.StreakKills != sr.Kills || sr.ItemId == 0) continue;
                    byte qty = sr.Amount > 0 ? sr.Amount : (byte)1;
                    for (byte n = 0; n < qty; n++)
                        killerPlayer.inventory.tryAddItem(new Item(sr.ItemId, true), true);
                    var ksp = PlayerTool.getSteamPlayer(new Steamworks.CSteamID(sid));
                    string itemName = GetItemName(sr.ItemId);
                    if (ksp != null) UnturnedChat.Say(UnturnedPlayer.FromSteamPlayer(ksp), $"[PVP] {sr.Kills} คิล! ได้รับ x{qty} {itemName} | {sr.Kills} kills! Got x{qty} {itemName}.", Color.cyan);
                    ShowStreakBanner(killerPlayer, $"★ {sr.Kills} KILLS — {itemName}!", 4f);
                }
            }
        }

        private static string GetItemName(ushort itemId)
        {
            if (itemId == 0) return "?";
            var asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            return asset?.itemName ?? itemId.ToString();
        }

        private static void TryEquipItemToHand(Player player, ushort itemId)
        {
            if (player?.inventory == null || player.equipment == null) return;
            for (byte page = 0; page < PlayerInventory.STORAGE; page++)
            {
                var grid = player.inventory.items[page];
                if (grid == null) continue;
                byte count = grid.getItemCount();
                for (byte i = 0; i < count; i++)
                {
                    var jar = grid.getItem(i);
                    if (jar?.item?.id == itemId)
                    {
                        player.equipment.ServerEquip(page, jar.x, jar.y);
                        return;
                    }
                }
            }
        }

        // TODO: Phase 3.5 - OnPlayerConnected hook for reconnect recovery (using appropriate SDG.Unturned event)
    }
}
