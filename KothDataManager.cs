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

        // Crash-safe write: serialize to a temp file, then atomically swap it over the real file
        // (keeping a .bak). A crash mid-serialize can only corrupt the temp, never the live file.
        private static void WriteAtomic(string path, XmlSerializer ser, object obj)
        {
            var tmp = path + ".tmp";
            try
            {
                using (var writer = new StreamWriter(tmp))
                    ser.Serialize(writer, obj);
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KothBox] atomic write {Path.GetFileName(path)} failed: {ex.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // Read path; on a torn/parse failure fall back to the .bak from the last good write.
        private static T ReadOrBackup<T>(string path, XmlSerializer ser) where T : class, new()
        {
            foreach (var p in new[] { path, path + ".bak" })
            {
                if (!File.Exists(p)) continue;
                try { using (var r = new StreamReader(p)) return (T)ser.Deserialize(r) ?? new T(); }
                catch { }
            }
            return new T();
        }

        // --- Boxes ---
        public KothBoxList LoadBoxes()
        {
            var path = Path.Combine(_pluginDir, "kothboxes.dat");
            return ReadOrBackup<KothBoxList>(path, _boxListSerializer);
        }

        public void SaveBoxes(KothBoxList boxes)
            => WriteAtomic(Path.Combine(_pluginDir, "kothboxes.dat"), _boxListSerializer, boxes);

        // --- Event State ---
        public KothEventState LoadState()
        {
            var path = Path.Combine(_pluginDir, "kothstate.xml");
            return ReadOrBackup<KothEventState>(path, _stateSerializer);
        }

        public void SaveState(KothEventState state)
            => WriteAtomic(Path.Combine(_pluginDir, "kothstate.xml"), _stateSerializer, state);

        // --- Pending Rewards ---
        public PendingRewardsList LoadRewards()
        {
            var path = Path.Combine(_pluginDir, "pendingrewards.xml");
            return ReadOrBackup<PendingRewardsList>(path, _rewardsSerializer);
        }

        public void SaveRewards(PendingRewardsList rewards)
            => WriteAtomic(Path.Combine(_pluginDir, "pendingrewards.xml"), _rewardsSerializer, rewards);

        // --- Leaderboard ---
        public LeaderboardData LoadLeaderboard()
        {
            var path = Path.Combine(_pluginDir, "kothleaderboard.xml");
            return ReadOrBackup<LeaderboardData>(path, _leaderboardSerializer);
        }

        public void SaveLeaderboard(LeaderboardData leaderboard)
            => WriteAtomic(Path.Combine(_pluginDir, "kothleaderboard.xml"), _leaderboardSerializer, leaderboard);

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
            => WriteAtomic(Path.Combine(_pluginDir, "preproom.xml"), _prepRoomSerializer, prepRoom);

        // --- Stash ---
        public string GetStashPath(ulong steamId) => Path.Combine(_pluginDir, "stash", $"{steamId}.dat");

        public void DeleteStash(ulong steamId)
        {
            var path = GetStashPath(steamId);
            // Delete the .bak/.tmp too, else a stale backup could "restore" already-returned items.
            foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception ex) { Debug.LogError($"[KothBox] Failed to delete stash {steamId}: {ex.Message}"); }
            }
        }
    }
}
