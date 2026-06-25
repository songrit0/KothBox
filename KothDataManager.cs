using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace KothBox
{
    public class KothDataManager
    {
        private readonly string _pluginDir;
        private readonly XmlSerializer _boxListSerializer = new XmlSerializer(typeof(KothBoxList));
        private readonly XmlSerializer _stateSerializer = new XmlSerializer(typeof(KothEventState));
        private readonly XmlSerializer _rewardsSerializer = new XmlSerializer(typeof(PendingRewardsList));
        private readonly XmlSerializer _leaderboardSerializer = new XmlSerializer(typeof(LeaderboardData));
        private readonly XmlSerializer _prepRoomSerializer = new XmlSerializer(typeof(PrepRoomTemplate));

        public KothDataManager(string pluginDir)
        {
            _pluginDir = pluginDir;
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(Path.Combine(_pluginDir, "stash"));
            Directory.CreateDirectory(Path.Combine(_pluginDir, "kits"));
            Directory.CreateDirectory(Path.Combine(_pluginDir, "defaultkits"));
        }

        // --- Kits (player MY SET + admin default sets; serialized like a stash) ---
        public string GetKitPath(ulong steamId) => Path.Combine(_pluginDir, "kits", $"{steamId}.dat");
        // snapshot ตอนเข้า dome (หลัง prep room); ใช้เป็น respawn kit แทน kit ต้นฉบับ
        public string GetDomeEntryPath(ulong steamId) => Path.Combine(_pluginDir, "stash", $"dome_{steamId}.dat");
        public void DeleteDomeEntry(ulong steamId) { try { File.Delete(GetDomeEntryPath(steamId)); } catch { } }
        public string GetDefaultKitPath(string name) =>
            Path.Combine(_pluginDir, "defaultkits", $"{SafeName(name)}.dat");
        public List<string> ListDefaultKits()
        {
            var dir = Path.Combine(_pluginDir, "defaultkits");
            var list = new List<string>();
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.dat"))
                    list.Add(Path.GetFileNameWithoutExtension(f));
            return list;
        }
        private static string SafeName(string n) =>
            string.Concat((n ?? "kit").Split(Path.GetInvalidFileNameChars()));

        // --- Boxes ---
        public KothBoxList LoadBoxes()
        {
            var path = Path.Combine(_pluginDir, "kothboxes.dat");
            if (!File.Exists(path))
                return new KothBoxList();

            try
            {
                using (var reader = new StreamReader(path))
                {
                    return (KothBoxList)_boxListSerializer.Deserialize(reader) ?? new KothBoxList();
                }
            }
            catch
            {
                return new KothBoxList();
            }
        }

        public void SaveBoxes(KothBoxList boxes)
        {
            var path = Path.Combine(_pluginDir, "kothboxes.dat");
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    _boxListSerializer.Serialize(writer, boxes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] Failed to save boxes: {ex.Message}");
            }
        }

        // --- Event State ---
        public KothEventState LoadState()
        {
            var path = Path.Combine(_pluginDir, "kothstate.xml");
            if (!File.Exists(path))
                return new KothEventState();

            try
            {
                using (var reader = new StreamReader(path))
                {
                    return (KothEventState)_stateSerializer.Deserialize(reader) ?? new KothEventState();
                }
            }
            catch
            {
                return new KothEventState();
            }
        }

        public void SaveState(KothEventState state)
        {
            var path = Path.Combine(_pluginDir, "kothstate.xml");
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    _stateSerializer.Serialize(writer, state);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] Failed to save state: {ex.Message}");
            }
        }

        // --- Pending Rewards ---
        public PendingRewardsList LoadRewards()
        {
            var path = Path.Combine(_pluginDir, "pendingrewards.xml");
            if (!File.Exists(path))
                return new PendingRewardsList();

            try
            {
                using (var reader = new StreamReader(path))
                {
                    return (PendingRewardsList)_rewardsSerializer.Deserialize(reader) ?? new PendingRewardsList();
                }
            }
            catch
            {
                return new PendingRewardsList();
            }
        }

        public void SaveRewards(PendingRewardsList rewards)
        {
            var path = Path.Combine(_pluginDir, "pendingrewards.xml");
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    _rewardsSerializer.Serialize(writer, rewards);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] Failed to save rewards: {ex.Message}");
            }
        }

        // --- Leaderboard ---
        public LeaderboardData LoadLeaderboard()
        {
            var path = Path.Combine(_pluginDir, "kothleaderboard.xml");
            if (!File.Exists(path))
                return new LeaderboardData();

            try
            {
                using (var reader = new StreamReader(path))
                {
                    return (LeaderboardData)_leaderboardSerializer.Deserialize(reader) ?? new LeaderboardData();
                }
            }
            catch
            {
                return new LeaderboardData();
            }
        }

        public void SaveLeaderboard(LeaderboardData leaderboard)
        {
            var path = Path.Combine(_pluginDir, "kothleaderboard.xml");
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    _leaderboardSerializer.Serialize(writer, leaderboard);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] Failed to save leaderboard: {ex.Message}");
            }
        }

        // --- Prep Room ---
        public PrepRoomTemplate LoadPrepRoom()
        {
            var path = Path.Combine(_pluginDir, "preproom.xml");
            if (!File.Exists(path)) return null;
            try
            {
                using (var reader = new StreamReader(path))
                    return (PrepRoomTemplate)_prepRoomSerializer.Deserialize(reader);
            }
            catch { return null; }
        }

        public void DeletePrepRoom()
        {
            var path = Path.Combine(_pluginDir, "preproom.xml");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public void SavePrepRoom(PrepRoomTemplate prepRoom)
        {
            var path = Path.Combine(_pluginDir, "preproom.xml");
            try
            {
                using (var writer = new StreamWriter(path))
                    _prepRoomSerializer.Serialize(writer, prepRoom);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] Failed to save prep room: {ex.Message}");
            }
        }

        // --- Stash ---
        public string GetStashPath(ulong steamId) => Path.Combine(_pluginDir, "stash", $"{steamId}.dat");

        public void DeleteStash(ulong steamId)
        {
            var path = GetStashPath(steamId);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] Failed to delete stash {steamId}: {ex.Message}");
            }
        }
    }
}
