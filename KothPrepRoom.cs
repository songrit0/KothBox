// Prep room: admin สร้างห้องบนแมพ → /savepreproom <radius> บันทึก template
// startkoth   → SpawnPrepRoom() สร้างห้องจาก template
// /jkoth warmup → player วาปเข้าห้อง prep รอ (ไม่เข้า dome ทันที)
// warmup หมด → ForceEnterDome() วาปทุกคนในห้องเข้า dome แล้วลบห้อง
// event จบ   → DestroyPrepRoom() ลบห้องออก (ถ้ายังค้างอยู่)

using Rocket.Core;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KothBox
{
    public partial class KothBox
    {
        public PrepRoomTemplate PrepRoom => _prepRoom;

        private readonly HashSet<ulong> _inPrepRoom = new HashSet<ulong>();
        // track spawned barricades: root transform + items to restock
        private readonly List<(Transform root, List<ItemSnapshot> items)> _prepRoomPairs
            = new List<(Transform, List<ItemSnapshot>)>();
        // instanceIDs for O(1) invulnerability check
        private readonly HashSet<uint> _prepRoomIds = new HashSet<uint>();
        private readonly List<Vector3> _prepRoomStructurePositions = new List<Vector3>();
        private bool _pendingBarricadeRefresh;
        // 3-step tp: step1=outside room, step2=inside room, step3=respawn barricades (player now in region)
        private readonly Dictionary<ulong, (Vector3 finalPos, float yaw, float step2At, float step3At)> _pendingTp
            = new Dictionary<ulong, (Vector3, float, float, float)>();
        private float _restockTimer;
        private float _pendingClearAt = -1f;
        private Vector3 _pendingClearPos;
        private float _pendingClearRadius;

        public void LoadPrepRoom() => _prepRoom = _dataManager.LoadPrepRoom();

        public void TickPendingClear()
        {
            // deferred item clear หลัง savepreproom
            if (_pendingClearAt >= 0f && UnityEngine.Time.realtimeSinceStartup >= _pendingClearAt)
            {
                _pendingClearAt = -1f;
                try { ItemManager.ServerClearItemsInSphere(_pendingClearPos, _pendingClearRadius); } catch { }
            }

            // 3-step teleport sequence
            if (_pendingTp.Count > 0)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                var done = new List<ulong>();
                foreach (var kv in _pendingTp.ToList())
                {
                    var (finalPos, yaw, step2At, step3At) = kv.Value;
                    var p = PlayerTool.getPlayer(new CSteamID(kv.Key));
                    if (p == null) { done.Add(kv.Key); continue; }

                    if (now >= step3At)
                    {
                        done.Add(kv.Key);
                        // step3: player อยู่ใน room แล้ว → respawn barricades ให้ client รับ spawn packets
                        DestroyPrepRoomObjects();
                        SpawnPrepRoomObjects();
                        if (_prepRoom != null)
                            try { ItemManager.ServerClearItemsInSphere(_prepRoom.GetSpawn(), 64f); } catch { }
                    }
                    else if (now >= step2At)
                    {
                        // step2: tp เข้าห้องจริง (client ได้โหลด region จาก step1 แล้ว)
                        p.teleportToLocation(finalPos, yaw);
                        // อัพ entry เพื่อรอ step3
                        _pendingTp[kv.Key] = (finalPos, yaw, step2At, now + 1f);
                    }
                }
                foreach (var id in done) _pendingTp.Remove(id);
            }
        }

        // block damage on prep room barricades (invulnerable while spawned)
        public void OnDamageBarricadeRequested(CSteamID instigatorSteamID, Transform barricadeTransform,
            ref ushort pendingTotalDamage, ref bool shouldAllow, EDamageOrigin damageOrigin)
        {
            if (!shouldAllow || _prepRoomIds.Count == 0) return;
            var drop = BarricadeManager.FindBarricadeByRootTransform(barricadeTransform);
            if (drop != null && _prepRoomIds.Contains(drop.instanceID))
                shouldAllow = false;
        }

        // /savepreproom <radius> — admin ยืนในห้อง บันทึก barricades ทั้งหมดในรัศมี
        // แล้วลบ barricades เดิมออกทันที (ห้องจะ spawn ใหม่ตอน startkoth เท่านั้น)
        public bool SavePrepRoom(UnturnedPlayer admin, float radius, out string msg)
        {
            if (BarricadeManager.regions == null) { msg = "BarricadeManager ยังไม่พร้อม"; return false; }

            var center = admin.Position;
            var template = new PrepRoomTemplate();
            template.SetSpawn(center);

            // scan ก่อน แล้วค่อยลบ (ห้าม modify regions ขณะ iterate)
            var toRemove = new List<Transform>();

            foreach (BarricadeRegion region in BarricadeManager.regions)
            {
                if (region?.drops == null) continue;
                foreach (var drop in region.drops)
                {
                    if (drop?.asset == null || drop.model == null) continue;
                    if (Vector3.Distance(drop.model.position, center) > radius) continue;

                    var rot = drop.model.rotation;
                    var bt = new BarricadeTemplate
                    {
                        AssetId = drop.asset.id,
                        PX = drop.model.position.x, PY = drop.model.position.y, PZ = drop.model.position.z,
                        RX = rot.x, RY = rot.y, RZ = rot.z, RW = rot.w
                    };

                    // ถ้าเป็น storage box ให้เก็บ items แยก (ไม่ใช้ state bytes เพื่อกันของตกพื้น)
                    var storage = drop.model.GetComponentInChildren<InteractableStorage>();
                    if (storage?.items != null)
                    {
                        byte count = storage.items.getItemCount();
                        for (byte i = 0; i < count; i++)
                        {
                            var jar = storage.items.getItem(i);
                            if (jar?.item == null) continue;
                            bt.Items.Add(new ItemSnapshot
                            {
                                Id = jar.item.id,
                                Amount = jar.item.amount,
                                Quality = jar.item.quality,
                                StateData = jar.item.state != null ? Convert.ToBase64String(jar.item.state) : ""
                            });
                        }
                    }

                    template.Barricades.Add(bt);
                    toRemove.Add(drop.model);
                }
            }

            // ลบ items บนพื้นก่อน (items ที่ admin วางค้างไว้ในห้อง)
            try { ItemManager.ServerClearItemsInSphere(center, radius); } catch { }

            // ลบ barricades เดิมออกจากแมพ (จะถูก spawn ใหม่ตอน startkoth)
            foreach (var t in toRemove) DestroyBarricadeTransform(t);

            // scan + ลบ structures ในรัศมีเดียวกัน (walls, floors, pillars) และเก็บ template
            if (StructureManager.regions != null)
            {
                var strucsToRemove = new List<(Transform t, StructureTemplate st)>();
                foreach (StructureRegion region in StructureManager.regions)
                {
                    if (region?.drops == null) continue;
                    foreach (var drop in region.drops)
                    {
                        if (drop?.model == null || drop.asset == null) continue;
                        if (Vector3.Distance(drop.model.position, center) > radius) continue;
                        var rot = drop.model.rotation;
                        strucsToRemove.Add((drop.model, new StructureTemplate
                        {
                            AssetId = drop.asset.id,
                            PX = drop.model.position.x, PY = drop.model.position.y, PZ = drop.model.position.z,
                            RX = rot.x, RY = rot.y, RZ = rot.z, RW = rot.w
                        }));
                    }
                }
                foreach (var (t, st) in strucsToRemove)
                {
                    template.Structures.Add(st);
                    DestroyStructureTransform(t);
                }
            }

            _prepRoom = template;
            _dataManager.SavePrepRoom(template);
            // ลบ items ที่ตกพื้นหลัง 1 วิ (ของที่หลุดจาก barricades/structures ที่ถูกลบ)
            _pendingClearAt = UnityEngine.Time.realtimeSinceStartup + 1f;
            _pendingClearPos = center;
            _pendingClearRadius = radius;
            msg = $"บันทึก {template.Barricades.Count} barricade ในรัศมี {radius}m (ลบออกจากแมพแล้ว จะ spawn ตอน startkoth)";
            return true;
        }

        // startkoth → สร้างห้องจาก template
        public void SpawnPrepRoom()
        {
            string buildName = Configuration.Instance.PrepBuildName;
            if (!string.IsNullOrEmpty(buildName))
            {
                // PrepBuildName mode: barricades are permanent (admin ran /loadbuild once).
                // Only read the spawn point from the build header — do NOT call loadbuild again.
                LoadPrepRoomSpawnFromBuild(buildName);
                return;
            }
            if (_prepRoom == null || _prepRoom.Barricades.Count == 0) return;
            DestroyPrepRoomObjects();
            try { ItemManager.ServerClearItemsInSphere(_prepRoom.GetSpawn(), 32f); } catch { }
            SpawnPrepRoomObjects();
            // respawn generators หลัง 1s เหมือน PrepBuildName path (ไฟฟ้าทำงาน)
            var spawnCenter = _prepRoom.GetSpawn();
            Rocket.Core.Utils.TaskDispatcher.QueueOnMainThread(() =>
                RespawnGeneratorsLast(spawnCenter, 64f), 1f);
        }

        // spawn barricades + structures จาก template (ส่ง spawn packets ไปทุก client ที่ online)
        private void SpawnPrepRoomObjects()
        {
            if (_prepRoom == null) return;
            foreach (var b in _prepRoom.Barricades)
            {
                var asset = Assets.find(EAssetType.ITEM, b.AssetId) as ItemBarricadeAsset;
                if (asset == null) continue;
                try
                {
                    // ใช้ default state ของ asset เพื่อป้องกัน IndexOutOfRange ใน Generator/Spot.updateState
                    byte[] defState;
                    try { defState = new Item(asset.id, true).state; } catch { defState = new byte[0]; }
                    var barricade = new Barricade(asset, asset.health, defState);
                    var pos = new Vector3(b.PX, b.PY, b.PZ);
                    var rot = new Quaternion(b.RX, b.RY, b.RZ, b.RW);
                    var t = BarricadeManager.dropNonPlantedBarricade(barricade, pos, rot, 0UL, 0UL);
                    if (t == null) continue;

                    var drop = BarricadeManager.FindBarricadeByRootTransform(t);
                    if (drop != null) _prepRoomIds.Add(drop.instanceID);
                    _prepRoomPairs.Add((t, b.Items));
                    if (b.Items.Count > 0) FillStorage(t, b.Items);
                }
                catch (Exception ex)
                {
                    Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] SpawnPrepRoomObjects barricade");
                }
            }
            foreach (var s in _prepRoom.Structures)
            {
                var asset = Assets.find(EAssetType.ITEM, s.AssetId) as ItemStructureAsset;
                if (asset == null) continue;
                try
                {
                    var pos = new Vector3(s.PX, s.PY, s.PZ);
                    var rot = new Quaternion(s.RX, s.RY, s.RZ, s.RW);
                    StructureManager.dropReplicatedStructure(new Structure(asset, asset.health), pos, rot, 0UL, 0UL);
                    _prepRoomStructurePositions.Add(pos);
                }
                catch (Exception ex)
                {
                    Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] SpawnPrepRoomObjects structure");
                }
            }
            Rocket.Core.Logging.Logger.Log($"[KothBox] PrepRoom spawned {_prepRoomPairs.Count} barricades + {_prepRoomStructurePositions.Count} structures");
        }

        private static void FillStorage(Transform root, List<ItemSnapshot> items)
        {
            if (items == null || items.Count == 0) return;
            var storage = root.GetComponentInChildren<InteractableStorage>();
            if (storage?.items == null) return;
            // clear แล้ว fill ใหม่
            for (int i = storage.items.getItemCount() - 1; i >= 0; i--)
                storage.items.removeItem((byte)i);
            foreach (var snap in items)
            {
                byte[] meta = string.IsNullOrEmpty(snap.StateData)
                    ? (Assets.find(EAssetType.ITEM, snap.Id) as ItemAsset)?.getState() ?? new byte[0]
                    : Convert.FromBase64String(snap.StateData);
                storage.items.tryAddItem(new Item(snap.Id, snap.Amount, snap.Quality, meta));
            }
        }

        // restock ทุก 5 วิตลอด warmup (คนอื่นหยิบของไปแล้วคนต่อไปยังมีของ)
        public void TickPrepRoomRestock(float dt)
        {
            if (_prepRoomPairs.Count == 0 || _currentState != EventState.Warmup) return;
            _restockTimer += dt;
            if (_restockTimer < 5f) return;
            _restockTimer = 0f;
            foreach (var (root, items) in _prepRoomPairs)
            {
                if (root == null || items.Count == 0) continue;
                var storage = root.GetComponentInChildren<InteractableStorage>();
                if (storage == null) continue;
                // restock เฉพาะเมื่อกล่องว่าง (ไม่ขัดจังหวะคนที่กำลังเปิดอยู่)
                if (storage.items.getItemCount() == 0)
                    FillStorage(root, items);
            }
        }

        // ลบเฉพาะ objects (barricades + structures) ไม่ยุ่งกับ _inPrepRoom
        private void DestroyPrepRoomObjects()
        {
            foreach (var (root, _) in _prepRoomPairs)
                DestroyBarricadeTransform(root);
            _prepRoomPairs.Clear();
            _prepRoomIds.Clear();

            if (_prepRoomStructurePositions.Count > 0 && StructureManager.regions != null)
            {
                var toDestroy = new List<Transform>();
                foreach (StructureRegion region in StructureManager.regions)
                {
                    if (region?.drops == null) continue;
                    foreach (var drop in region.drops)
                    {
                        if (drop?.model == null) continue;
                        if (_prepRoomStructurePositions.Any(p => Vector3.Distance(drop.model.position, p) < 0.5f))
                            toDestroy.Add(drop.model);
                    }
                }
                foreach (var t in toDestroy) DestroyStructureTransform(t);
                _prepRoomStructurePositions.Clear();
            }
        }

        // event จบ → ลบห้องทั้งหมด (objects + player tracking)
        public void DestroyPrepRoom()
        {
            string buildName = Configuration.Instance.PrepBuildName;
            if (!string.IsNullOrEmpty(buildName))
            {
                // PrepBuildName mode: barricades are permanent — don't destroy them.
                // Just clear player tracking so next event starts clean.
            }
            else
                DestroyPrepRoomObjects();
            _inPrepRoom.Clear();
            _pendingTp.Clear();
            _restockTimer = 0f;
            Vector3? center = !string.IsNullOrEmpty(buildName) ? _prepRoomSpawn
                : _prepRoom != null ? (Vector3?)_prepRoom.GetSpawn() : null;
            if (center.HasValue)
                try { ItemManager.ServerClearItemsInSphere(center.Value, 64f); } catch { }
        }

        // /clearpreproom — ลบ template ออก
        public void ClearPrepRoom(out string msg)
        {
            DestroyPrepRoom();
            _prepRoom = null;
            _dataManager.DeletePrepRoom();
            msg = "ลบ prep room template แล้ว";
        }

        // /jkoth ช่วง warmup → วาปเข้าห้อง prep แทน dome (HomeTeleport style)
        public bool TryTeleportToPrepRoom(Player player)
        {
            string buildName = Configuration.Instance.PrepBuildName;
            bool useBuild = !string.IsNullOrEmpty(buildName);

            if (useBuild && _prepRoomSpawn == null) return false;
            if (!useBuild && (_prepRoom == null || _prepRoomPairs.Count == 0)) return false;
            if (_currentState != EventState.Warmup) return false;

            var up = UnturnedPlayer.FromPlayer(player);
            if (up == null) return false;

            ulong sid = up.CSteamID.m_SteamID;

            // Already in prep room: no re-teleport, just refresh barricades for client visibility
            if (_inPrepRoom.Contains(sid))
            {
                UnturnedChat.Say(up, "[PVP] คุณอยู่ในห้อง Prep แล้ว!", Color.cyan);
                SchedulePrepRoomRefresh();
                return true;
            }
            _inPrepRoom.Add(sid);

            Vector3 spawn = useBuild ? _prepRoomSpawn.Value : _prepRoom.GetSpawn();
            spawn.y += 1.5f; // spawn above floor to avoid being inside structure geometry
            float yaw = up.Rotation;
            up.Player.teleportToLocation(spawn, yaw);
            // Refresh barricades 1s after teleport so the client (now in region) receives fresh spawn packets
            SchedulePrepRoomRefresh();
            // Clear dropped items 2s after teleport (after player finishes loading in)
            var plugin = this;
            Rocket.Core.Utils.TaskDispatcher.QueueOnMainThread(() => plugin.ClearPrepRoomGroundItems(), 2f);
            UnturnedChat.Say(up, "[PVP] รออยู่ในห้อง Prep จนวอมอัพหมดจะเข้าสนาม PVP อัตโนมัติ", Color.cyan);
            return true;
        }

        private void SchedulePrepRoomRefresh()
        {
            if (_pendingBarricadeRefresh) return;
            _pendingBarricadeRefresh = true;
            var plugin = this;
            Rocket.Core.Utils.TaskDispatcher.QueueOnMainThread(() =>
            {
                plugin._pendingBarricadeRefresh = false;
                if (plugin._currentState == EventState.Warmup && plugin._prepRoomPairs.Count > 0)
                {
                    plugin.DestroyPrepRoomObjects();
                    plugin.SpawnPrepRoomObjects();
                }
            }, 1f);
        }

        // spawn point + radius สำหรับ PrepBuildName mode (อ่านจาก HDR ของ .build.txt)
        private Vector3? _prepRoomSpawn;
        private float _prepRoomBuildRadius;

        public void LoadPrepRoomSpawnFromBuild(string buildName)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string path = System.IO.Path.Combine(dir, "..", "AdminBarricade", "saves", buildName + ".build.txt");
                string header = System.IO.File.ReadLines(path).First();
                if (!header.StartsWith("HDR|")) return;
                string[] h = header.Split('|');
                float x = float.Parse(h[1], System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(h[2], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(h[3], System.Globalization.CultureInfo.InvariantCulture);
                _prepRoomSpawn = new Vector3(x, y, z);
                _prepRoomBuildRadius = float.Parse(h[4], System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogException(ex, "[KothBox] LoadPrepRoomSpawnFromBuild");
                _prepRoomSpawn = null;
            }
        }

        // ลบ barricades + structures ในรัศมี (สำหรับ PrepBuildName mode)
        private void DestroyBuildPrepRoom()
        {
            if (_prepRoomSpawn == null) return;
            Vector3 center = _prepRoomSpawn.Value;
            float radius = _prepRoomBuildRadius > 0f ? _prepRoomBuildRadius : 64f;
            try { BarricadeManager.DestroyBarricadesInSphere(center, radius, false, false); } catch { }
            // structures
            float r2 = radius * radius;
            var kill = new List<StructureDrop>();
            if (StructureManager.regions != null)
                foreach (StructureRegion region in StructureManager.regions)
                {
                    if (region?.drops == null) continue;
                    foreach (var drop in region.drops)
                        if (drop?.model != null && (drop.model.position - center).sqrMagnitude <= r2)
                            kill.Add(drop);
                }
            foreach (var drop in kill)
                try
                {
                    byte x, y; ushort idx; StructureRegion reg; StructureDrop d2;
                    if (StructureManager.tryGetInfo(drop.model, out x, out y, out idx, out reg, out d2))
                        StructureManager.destroyStructure(d2, x, y, Vector3.zero);
                }
                catch { }
        }

        // destroy + respawn generators LAST เพื่อให้ scan powered nodes ได้ครบ
        private static void RespawnGeneratorsLast(Vector3 center, float radius)
        {
            float r2 = radius * radius;
            var gens = new List<(ushort id, Vector3 pos, Quaternion rot, ulong owner, ulong group, ushort health, byte[] state)>();
            var toDestroy = new List<Transform>();

            foreach (BarricadeRegion region in BarricadeManager.regions)
            {
                if (region?.drops == null) continue;
                foreach (var drop in region.drops)
                {
                    if (drop?.model == null || drop.asset == null) continue;
                    if ((drop.model.position - center).sqrMagnitude > r2) continue;
                    if (drop.model.GetComponentInChildren<InteractableGenerator>() == null) continue;
                    var d = drop.GetServersideData();
                    gens.Add((drop.asset.id, d.point, d.rotation, d.owner, d.group,
                        d.barricade?.health ?? drop.asset.health, d.barricade?.state ?? new byte[0]));
                    toDestroy.Add(drop.model);
                }
            }

            foreach (var t in toDestroy)
                try { DestroyBarricadeTransform(t); } catch { }

            foreach (var (id, pos, rot, owner, group, health, state) in gens)
            {
                var asset = Assets.find(EAssetType.ITEM, id) as ItemBarricadeAsset;
                if (asset == null) continue;
                try { BarricadeManager.dropNonPlantedBarricade(new Barricade(asset, health, state), pos, rot, owner, group); }
                catch { }
            }
        }

        // warmup → active: วาปทุกคนในห้อง prep เข้า dome แล้วลบห้อง
        public void ForceEnterDome()
        {
            Rocket.Core.Logging.Logger.Log($"[KothBox] ForceEnterDome: {_inPrepRoom.Count} players in prep room");
            foreach (var sid in _inPrepRoom.ToList())
            {
                var player = PlayerTool.getPlayer(new CSteamID(sid));
                if (player?.transform != null)
                {
                    // snapshot loadout ที่แต่งใน prep room → respawn kit + MY SET สำหรับครั้งหน้า
                    var snap = SnapshotKit(player);
                    var domeKitPath = _dataManager.GetDomeEntryPath(sid);
                    InventoryStash.Serialize(domeKitPath, snap);
                    InventoryStash.Serialize(_dataManager.GetKitPath(sid), snap); // MY SET = dome-entry ของครั้งนี้
                    var part = GetParticipant(sid);
                    if (part != null) part.KitPath = domeKitPath;

                    var dest = RandomDomeSpawn();
                    Rocket.Core.Logging.Logger.Log($"[KothBox] ForceEnterDome: tp {sid} → {dest}");
                    player.teleportToLocation(dest, player.look?.yaw ?? 0f);
                }
                else
                    Rocket.Core.Logging.Logger.Log($"[KothBox] ForceEnterDome: player {sid} not found/no transform");
            }
            _inPrepRoom.Clear();
            DestroyPrepRoom(); // ลบห้อง prep ทันทีที่ทุกคนเข้าวงแล้ว
        }
    }
}
