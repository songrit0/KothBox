using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;

namespace KothBox
{
    [XmlRoot("KothBox")]
    public class KothBoxData
    {
        [XmlAttribute]
        public byte Id { get; set; }

        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public float CenterX { get; set; }

        [XmlAttribute]
        public float CenterY { get; set; }

        [XmlAttribute]
        public float CenterZ { get; set; }

        [XmlAttribute]
        public float Radius { get; set; }

        [XmlAttribute]
        public string BuildName { get; set; }

        public Vector3 GetCenter() => new Vector3(CenterX, CenterY, CenterZ);

        public void SetCenter(Vector3 pos)
        {
            CenterX = pos.x;
            CenterY = pos.y;
            CenterZ = pos.z;
        }
    }

    [XmlRoot("KothBoxes")]
    public class KothBoxList
    {
        [XmlElement("Box")]
        public List<KothBoxData> Boxes { get; set; } = new List<KothBoxData>();
    }

    [XmlRoot("ParticipantState")]
    public class ParticipantData
    {
        [XmlAttribute]
        public ulong SteamId { get; set; }

        [XmlAttribute]
        public float ReturnX { get; set; }

        [XmlAttribute]
        public float ReturnY { get; set; }

        [XmlAttribute]
        public float ReturnZ { get; set; }

        [XmlAttribute]
        public float CumulativeSeconds { get; set; }

        [XmlAttribute]
        public string StashFile { get; set; }

        [XmlAttribute]
        public string LoadoutName { get; set; }

        // Kit file the player joined with (re-given on respawn). Empty -> auto-resolve.
        [XmlAttribute]
        public string KitPath { get; set; }

        [XmlIgnore]
        public bool IsInsideDome { get; set; }

        [XmlIgnore]
        public uint Kills { get; set; }

        [XmlIgnore]
        public uint StreakKills { get; set; }

        public Vector3 GetReturnPos() => new Vector3(ReturnX, ReturnY, ReturnZ);

        public void SetReturnPos(Vector3 pos)
        {
            ReturnX = pos.x;
            ReturnY = pos.y;
            ReturnZ = pos.z;
        }
    }

    [XmlRoot("KothState")]
    public class KothEventState
    {
        [XmlAttribute]
        public byte CurrentBoxId { get; set; }

        [XmlElement("Participant")]
        public List<ParticipantData> Participants { get; set; } = new List<ParticipantData>();

        // Players who were offline when the event ended (or at reload): their stash is still on
        // disk, waiting to be restored on their next reconnect. Kept out of Participants so they
        // don't accrue time / get teleported while offline.
        [XmlElement("Pending")]
        public List<ParticipantData> OfflinePending { get; set; } = new List<ParticipantData>();
    }

    [XmlRoot("PendingRewards")]
    public class PendingRewardData
    {
        [XmlAttribute]
        public ulong SteamId { get; set; }

        [XmlAttribute]
        public uint Coins { get; set; }

        [XmlAttribute]
        public uint MeowCoins { get; set; }

        [XmlElement("ItemId")]
        public List<ushort> ItemIds { get; set; } = new List<ushort>();

        [XmlAttribute]
        public ushort VehicleId { get; set; }

        [XmlAttribute]
        public uint Experience { get; set; }
    }

    [XmlRoot("PendingRewardsList")]
    public class PendingRewardsList
    {
        [XmlElement("Reward")]
        public List<PendingRewardData> Rewards { get; set; } = new List<PendingRewardData>();
    }

    [XmlRoot("KothLeaderboard")]
    public class LeaderboardEntry
    {
        [XmlAttribute]
        public ulong SteamId { get; set; }

        [XmlAttribute]
        public string PlayerName { get; set; }

        [XmlAttribute]
        public uint Wins { get; set; }

        [XmlAttribute]
        public uint Kills { get; set; }

        [XmlAttribute]
        public uint Deaths { get; set; }
    }

    [XmlRoot("Leaderboard")]
    public class LeaderboardData
    {
        [XmlElement("Entry")]
        public List<LeaderboardEntry> Entries { get; set; } = new List<LeaderboardEntry>();
    }

    // Prep room template: admin สร้างห้องไว้บนแมพแล้ว /savepreproom <radius> เพื่อบันทึก
    // ตอน startkoth → สร้างห้องจาก template; จบ event → ลบห้องออก
    // ผู้เล่นที่ /jkoth ช่วง warmup → วาปเข้าห้องรอ; warmup หมด → วาปเข้า dome อัตโนมัติ
    [XmlRoot("PrepRoomTemplate")]
    public class PrepRoomTemplate
    {
        // จุด teleport ผู้เล่นตอน /jkoth ช่วง warmup (ตำแหน่ง admin ตอน /savepreproom)
        [XmlAttribute] public float SpawnX { get; set; }
        [XmlAttribute] public float SpawnY { get; set; }
        [XmlAttribute] public float SpawnZ { get; set; }

        [XmlElement("B")]
        public List<BarricadeTemplate> Barricades { get; set; } = new List<BarricadeTemplate>();

        [XmlElement("S")]
        public List<StructureTemplate> Structures { get; set; } = new List<StructureTemplate>();

        public Vector3 GetSpawn() => new Vector3(SpawnX, SpawnY, SpawnZ);
        public void SetSpawn(Vector3 p) { SpawnX = p.x; SpawnY = p.y; SpawnZ = p.z; }
    }

    public class StructureTemplate
    {
        [XmlAttribute] public ushort AssetId { get; set; }
        [XmlAttribute] public float PX { get; set; }
        [XmlAttribute] public float PY { get; set; }
        [XmlAttribute] public float PZ { get; set; }
        [XmlAttribute] public float RX { get; set; }
        [XmlAttribute] public float RY { get; set; }
        [XmlAttribute] public float RZ { get; set; }
        [XmlAttribute] public float RW { get; set; }
    }

    public class BarricadeTemplate
    {
        [XmlAttribute] public ushort AssetId { get; set; }
        [XmlAttribute] public float PX { get; set; }
        [XmlAttribute] public float PY { get; set; }
        [XmlAttribute] public float PZ { get; set; }
        [XmlAttribute] public float RX { get; set; }
        [XmlAttribute] public float RY { get; set; }
        [XmlAttribute] public float RZ { get; set; }
        [XmlAttribute] public float RW { get; set; }
        // ของในกล่อง storage (spawn ว่างแล้ว fill หลัง spawn เพื่อกันของตก)
        [XmlElement("Item")]
        public List<ItemSnapshot> Items { get; set; } = new List<ItemSnapshot>();
    }
}
