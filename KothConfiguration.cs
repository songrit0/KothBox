using Rocket.API;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace KothBox
{
    public class KillStreakReward
    {
        [XmlAttribute] public int Kills { get; set; }
        [XmlAttribute] public ushort ItemId { get; set; }
        [XmlAttribute] public byte Amount { get; set; }
        // true = equip to hand immediately (server can't force a hotkey number, only equip to hand)
        [XmlAttribute] public bool AutoEquip { get; set; }
    }

    public class RewardTier
    {
        // Fixed XP bonus on top of the prize pool split (optional, can be 0).
        [XmlAttribute]
        public uint Experience { get; set; }

        // How many gacha draws from sv_gacha_prizes this rank gets.
        [XmlAttribute]
        public int GachaDraws { get; set; }
    }

    public class Loadout
    {
        [XmlAttribute]
        public string Name { get; set; }

        // Items given on spawn (gun + ammo + armor + meds). Full magazine/stack by default.
        [XmlElement("ItemId")]
        public List<ushort> ItemIds { get; set; } = new List<ushort>();
    }

    // sv_coins integration — same DB the shop/GameMenu plugins use.
    public class DatabaseSection
    {
        public string ConnectionString { get; set; }
        public string ShopPrefix { get; set; } // table prefix; <prefix>coins (e.g. sv_coins)
    }

    public class KothConfiguration : IRocketPluginConfiguration
    {
        [XmlElement("WarmupDuration")]
        public float WarmupDuration { get; set; }

        [XmlElement("EventDuration")]
        public float EventDuration { get; set; }

        [XmlElement("WinningTime")]
        public float WinningTime { get; set; }

        [XmlElement("DomeDamage")]
        public byte DomeDamage { get; set; }

        // XP entry fee. The host picks the actual fee with /startkoth <box> [fee].
        // All XP pools up and split among the top finishers. 0 = free entry.
        [XmlElement("EntryFee")]
        public uint EntryFee { get; set; }

        // How the prize pool is split among the top finishers, in percent (e.g. 50/30/20 for top 3).
        [XmlArray("PrizeSplitPercents")]
        [XmlArrayItem("Percent")]
        public List<int> PrizeSplitPercents { get; set; }

        // Winner #1 also gets a random prize from the shop gacha pool (sv_gacha_prizes, by weight),
        // broadcast to everyone. false = off.
        [XmlElement("GachaRewardForWinner")]
        public bool GachaRewardForWinner { get; set; }

        // Ring-effect fallback id (used only if the mesh dome ids below are 0). 0 = no visual.
        [XmlElement("DomeRingEffectId")]
        public ushort DomeRingEffectId { get; set; }

        // Mesh-dome EffectAsset ids (the Sphere+shell bundle from BuildKothUI "Generate Dome").
        // White shell during warmup, green during the active fight. 0 = fall back to the ring.
        [XmlElement("DomeWarmupEffectId")]
        public ushort DomeWarmupEffectId { get; set; }

        [XmlElement("DomeActiveEffectId")]
        public ushort DomeActiveEffectId { get; set; }

        // EffectAsset id of the loadout-selection UI bundle (Phase 8). 0 = use /jkoth chat fallback.
        [XmlElement("LoadoutUIEffectId")]
        public ushort LoadoutUIEffectId { get; set; }

        // EffectAsset id of the HUD bundle (rank/time/countdown). 0 = no HUD.
        [XmlElement("HudEffectId")]
        public ushort HudEffectId { get; set; }

        // EffectAsset id of the game-menu/lobby panel (/koth): status + Join + Host (set fee, start).
        [XmlElement("GameMenuEffectId")]
        public ushort GameMenuEffectId { get; set; }

        // EffectAsset id of the gacha-result panel (winner's prize shown to everyone).
        [XmlElement("GachaResultEffectId")]
        public ushort GachaResultEffectId { get; set; }

        // AdminBarricade build name to load/reload as the prep room (e.g. "prep3").
        // If set, KothBox calls /loadbuild <name> on startkoth and whenever a player joins the prep room.
        // Leave empty to use KothBox's own /savepreproom system.
        [XmlElement("PrepBuildName")]
        public string PrepBuildName { get; set; }

        // Box that the in-menu HOST button starts (the lobby doesn't pick a box).
        [XmlElement("DefaultBoxName")]
        public string DefaultBoxName { get; set; }

        // Admin default kit given when a player has no saved MY SET (/savedefaultkit <name>).
        // Empty / not found -> first default kit on disk -> config Loadouts.
        [XmlElement("DefaultKitName")]
        public string DefaultKitName { get; set; }

        // EffectAsset id of the always-on floating button that opens the menu (no need to type /koth).
        [XmlElement("KothButtonEffectId")]
        public ushort KothButtonEffectId { get; set; }

        // EffectAsset id of the kill-streak progress panel (milestone boxes + progress bar + AutoEquip toggle).
        [XmlElement("StreakUIEffectId")]
        public ushort StreakUIEffectId { get; set; }

        // Arena wall: ring of this barricade around the box perimeter (visible boundary).
        // 6204 = a wall/fence barricade. 0 = no wall. Removed when the round ends.
        [XmlElement("ArenaWallItemId")]
        public ushort ArenaWallItemId { get; set; }

        // How many barricade segments to place around the ring (legacy; ignored when Spacing > 0).
        [XmlElement("ArenaWallSegments")]
        public int ArenaWallSegments { get; set; }

        // Metres between wall segments — segment COUNT auto-scales with the radius. 0 = fall back to Segments.
        [XmlElement("ArenaWallSpacing")]
        public float ArenaWallSpacing { get; set; }

        // Fine-tune wall facing (degrees) if segments aren't flush — they face the centre by default.
        [XmlElement("ArenaWallYawOffset")]
        public float ArenaWallYawOffset { get; set; }

        // HP regenerated per kill (spread over ticks). 0 = disabled.
        [XmlElement("KillHealAmount")]
        public byte KillHealAmount { get; set; }

        // HP healed per 1-second tick after a kill.
        [XmlElement("KillHealTickAmount")]
        public byte KillHealTickAmount { get; set; }

        // Clear dropped items inside the dome when the round ends.
        [XmlElement("ClearItemsOnEnd")]
        public bool ClearItemsOnEnd { get; set; }

        // Auto-join: a non-participant standing inside the arena is auto-joined after a countdown.
        [XmlElement("AutoJoinEnabled")]
        public bool AutoJoinEnabled { get; set; }

        // Seconds inside the zone before auto-join fires (counts down with a chat warning).
        [XmlElement("AutoJoinCountdown")]
        public float AutoJoinCountdown { get; set; }

        // Trigger radius around the box centre. 0 = use the box radius (the whole circle).
        [XmlElement("AutoJoinRadius")]
        public float AutoJoinRadius { get; set; }

        // While in an event, ONLY these commands are allowed (everything else is blocked).
        [XmlArray("AllowedCommandsInEvent")]
        [XmlArrayItem("Command")]
        public List<string> AllowedCommandsInEvent { get; set; }

        [XmlArray("BlockedCommands")]
        [XmlArrayItem("Command")]
        public List<string> BlockedCommands { get; set; }

        [XmlArray("RewardTiers")]
        [XmlArrayItem("Tier")]
        public List<RewardTier> RewardTiers { get; set; }

        [XmlArray("KillStreakRewards")]
        [XmlArrayItem("Streak")]
        public List<KillStreakReward> KillStreakRewards { get; set; }

        [XmlArray("Loadouts")]
        [XmlArrayItem("Loadout")]
        public List<Loadout> Loadouts { get; set; }

        public DatabaseSection Database { get; set; }

        public void LoadDefaults()
        {
            WarmupDuration = 180f;
            EventDuration = 1200f;
            WinningTime = 300f;
            DomeDamage = 15;
            EntryFee = 50;
            PrizeSplitPercents = new List<int> { 50, 30, 20 };
            GachaRewardForWinner = true;
            DomeRingEffectId = 0;
            DomeWarmupEffectId = 0;
            DomeActiveEffectId = 0;
            LoadoutUIEffectId = 0;
            HudEffectId = 0;
            GameMenuEffectId = 0;
            GachaResultEffectId = 0;
            KothButtonEffectId = 0;
            StreakUIEffectId = 0;
            PrepBuildName = "";
            DefaultBoxName = "arena";
            DefaultKitName = "starter";
            ArenaWallItemId = 6204;
            ArenaWallSegments = 60;
            ArenaWallSpacing = 4f;
            ArenaWallYawOffset = 0f;
            KillHealAmount = 30;
            KillHealTickAmount = 5;
            ClearItemsOnEnd = true;
            AutoJoinEnabled = true;
            AutoJoinCountdown = 15f;
            AutoJoinRadius = 0f; // 0 = use box radius
            AllowedCommandsInEvent = new List<string>
            {
                "koth", "leavekoth", "lkoth", "jkoth", "joinkoth", "claimkoth", "kothtop",
                "startkoth", "stopkoth", "kothboxes", "setkothbox", "deletekothbox",
                "previewkothbox", "reloadkothboxes", "testdome", "savekit", "savedefaultkit"
            };
            BlockedCommands = new List<string> { "tpa", "tpaccept", "home" };
            RewardTiers = new List<RewardTier>
            {
                new RewardTier { GachaDraws = 1 },
                new RewardTier { GachaDraws = 0 },
                new RewardTier { GachaDraws = 0 }
            };
            KillStreakRewards = new List<KillStreakReward>
            {
                new KillStreakReward { Kills = 3, ItemId = 254, Amount = 1, AutoEquip = true }
            };
            Loadouts = new List<Loadout>
            {
                // gun + 3 spare mags (auto-refilled) + 2 medicals
                new Loadout { Name = "Set 1", ItemIds = new List<ushort> { 7098, 4825, 4825, 4825, 4408, 4408 } },
                new Loadout { Name = "Set 2", ItemIds = new List<ushort> { 6400, 6992, 6992, 6992, 4408, 4408 } }
            };
            Database = new DatabaseSection
            {
                ConnectionString = "SERVER=localhost;DATABASE=s203_unturned;UID=root;PASSWORD=changeme;",
                ShopPrefix = "sv_"
            };
        }

        public IRocketPluginConfiguration DefaultConfiguration => new KothConfiguration();
    }
}
