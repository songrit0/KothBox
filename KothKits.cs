using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KothBox
{
    // Custom loadouts ("kits"): a player saves their own arranged inventory (gun + attachments,
    // captured via item state bytes) as MY SET; admins save named default sets. On join/respawn we
    // give MY SET, else the configured default kit, else fall back to the old config Loadout.
    public partial class KothBox
    {
        // Save the caller's current inventory + worn clothing as their personal kit. Returns item count.
        public int SaveMyKit(UnturnedPlayer player)
        {
            var snaps = SnapshotKit(player.Player);
            InventoryStash.Serialize(_dataManager.GetKitPath(player.CSteamID.m_SteamID), snaps);
            return snaps.Count;
        }

        // Save the caller's current inventory + worn clothing as a named admin default kit.
        public int SaveDefaultKit(UnturnedPlayer player, string name)
        {
            var snaps = SnapshotKit(player.Player);
            InventoryStash.Serialize(_dataManager.GetDefaultKitPath(name), snaps);
            return snaps.Count;
        }

        // Inventory grid + worn clothing. Clothing rides in the same list with Page 200..206
        // (shirt/pants/hat/backpack/vest/mask/glasses) so it serializes with no format change.
        private const byte ClothingPageBase = 200;
        private static List<ItemSnapshot> SnapshotKit(Player p)
        {
            var items = SnapshotGrid(p);
            var c = p?.clothing;
            if (c != null)
            {
                AddClothing(items, 0, c.shirt, c.shirtQuality, c.shirtState);
                AddClothing(items, 1, c.pants, c.pantsQuality, c.pantsState);
                AddClothing(items, 2, c.hat, c.hatQuality, c.hatState);
                AddClothing(items, 3, c.backpack, c.backpackQuality, c.backpackState);
                AddClothing(items, 4, c.vest, c.vestQuality, c.vestState);
                AddClothing(items, 5, c.mask, c.maskQuality, c.maskState);
                AddClothing(items, 6, c.glasses, c.glassesQuality, c.glassesState);
            }
            return items;
        }

        private static void AddClothing(List<ItemSnapshot> items, byte slot, ushort id, byte quality, byte[] state)
        {
            if (id == 0) return;
            items.Add(new ItemSnapshot
            {
                Page = (byte)(ClothingPageBase + slot),
                Id = id,
                Amount = 1,
                Quality = quality,
                StateData = state != null ? Convert.ToBase64String(state) : ""
            });
        }

        // The pickable kit list shown in the loadout UI: MY SET first (if saved), then admin default kits.
        public List<KeyValuePair<string, string>> GetKitOptions(ulong steamId)
        {
            var opts = new List<KeyValuePair<string, string>>();
            string my = _dataManager.GetKitPath(steamId);
            if (System.IO.File.Exists(my)) opts.Add(new KeyValuePair<string, string>("MY SET", my));
            foreach (var name in _dataManager.ListDefaultKits())
                opts.Add(new KeyValuePair<string, string>(name.ToUpper(), _dataManager.GetDefaultKitPath(name)));
            return opts;
        }

        // Give the kit the player PICKED in the UI (index into GetKitOptions); fall back to auto-resolve.
        private void GiveSelectedKit(Player p, ParticipantData participant, int index)
        {
            var opts = GetKitOptions(p.channel.owner.playerID.steamID.m_SteamID);
            if (index >= 0 && index < opts.Count)
            {
                var snaps = InventoryStash.Deserialize(opts[index].Value);
                if (snaps.Count > 0)
                {
                    GiveKit(p, snaps);
                    if (participant != null) participant.KitPath = opts[index].Value;
                    var u = UnturnedPlayer.FromPlayer(p);
                    if (u != null) UnturnedChat.Say(u, $"[PVP] Gave kit '{opts[index].Key}' ({snaps.Count} items).", Color.green);
                    return;
                }
            }
            GiveParticipantKit(p, participant);
        }

        // MY SET -> configured default kit -> first default kit -> config Loadout (final fallback).
        private void GiveParticipantKit(Player p, ParticipantData participant)
        {
            if (p == null) return;
            var up = UnturnedPlayer.FromPlayer(p);

            var mine = InventoryStash.Deserialize(_dataManager.GetKitPath(p.channel.owner.playerID.steamID.m_SteamID));
            if (mine.Count > 0)
            {
                GiveKit(p, mine);
                if (up != null) UnturnedChat.Say(up, $"[PVP] Gave MY SET ({mine.Count} items).", Color.green);
                return;
            }

            string defName = Configuration.Instance.DefaultKitName;
            var defaults = _dataManager.ListDefaultKits();
            if (string.IsNullOrEmpty(defName) || !defaults.Contains(defName))
                defName = defaults.Count > 0 ? defaults[0] : null;
            if (defName != null)
            {
                var kit = InventoryStash.Deserialize(_dataManager.GetDefaultKitPath(defName));
                if (kit.Count > 0)
                {
                    GiveKit(p, kit);
                    if (up != null) UnturnedChat.Say(up, $"[PVP] Gave default kit '{defName}' ({kit.Count} items).", Color.cyan);
                    return;
                }
            }

            // No kits saved yet -> old behaviour.
            GiveLoadout(p, Configuration.Instance.Loadouts?.Find(l => l.Name == participant.LoadoutName) ?? PickLoadout(0));
            if (up != null) UnturnedChat.Say(up, "[PVP] Gave config loadout (no kit saved).", Color.yellow);
        }

        // Restore a kit into the player's inventory (gun keeps attachments via state bytes).
        // Auto-places like GiveLoadout/RestoreInventory (proven to work) — exact-slot addItem
        // silently dropped items when a clothing page wasn't equipped.
        private void GiveKit(Player p, List<ItemSnapshot> snaps)
        {
            if (p?.inventory == null || snaps == null) return;
            _suppressItemDrops = true;
            try
            {
                ClearGrid(p);
                StripClothing(p); // remove whatever they're wearing (clean slate)
                ClearGrid(p);     // drop any stripped clothing back out of the grid

                // Clothing FIRST (backpack/vest give storage space for the items that follow).
                foreach (var s in snaps)
                {
                    if (s.Page < ClothingPageBase) continue;
                    byte[] state = string.IsNullOrEmpty(s.StateData) ? new byte[0] : Convert.FromBase64String(s.StateData);
                    WearClothing(p, (byte)(s.Page - ClothingPageBase), s.Id, s.Quality, state);
                }

                foreach (var s in snaps)
                {
                    if (s.Page >= ClothingPageBase) continue;
                    var item = new Item(s.Id, true) { amount = s.Amount, quality = s.Quality };
                    if (!string.IsNullOrEmpty(s.StateData))
                        item.state = Convert.FromBase64String(s.StateData);
                    p.inventory.tryAddItem(item, true); // silently skip if no space
                }
            }
            finally { _suppressItemDrops = false; }
        }

        private static void StripClothing(Player p)
        {
            var c = p?.clothing;
            if (c == null) return;
            var empty = new byte[0];
            c.askWearShirt(0, 0, empty, false);
            c.askWearPants(0, 0, empty, false);
            c.askWearHat(0, 0, empty, false);
            c.askWearBackpack(0, 0, empty, false);
            c.askWearVest(0, 0, empty, false);
            c.askWearMask(0, 0, empty, false);
            c.askWearGlasses(0, 0, empty, false);
        }

        private static void WearClothing(Player p, byte slot, ushort id, byte quality, byte[] state)
        {
            var c = p.clothing;
            switch (slot)
            {
                case 0: c.askWearShirt(id, quality, state, true); break;
                case 1: c.askWearPants(id, quality, state, true); break;
                case 2: c.askWearHat(id, quality, state, true); break;
                case 3: c.askWearBackpack(id, quality, state, true); break;
                case 4: c.askWearVest(id, quality, state, true); break;
                case 5: c.askWearMask(id, quality, state, true); break;
                case 6: c.askWearGlasses(id, quality, state, true); break;
            }
        }
    }
}
