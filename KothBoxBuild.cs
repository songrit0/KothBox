using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace KothBox
{
    public partial class KothBox
    {
        private (Vector3 center, float radius)? _boxBuildArea;

        private static string GetAdminBarricadeSavesDir()
        {
            // AdminBarricade.dll lives directly in Rocket/Plugins/ (not a subfolder).
            // KothBox.dll lives in Rocket/Plugins/KothBox/, so go up one level.
            string kothDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(kothDir, "..", "saves");
        }

        public static string SanitizeBuildName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            return sb.Length == 0 ? null : sb.ToString();
        }

        public static bool BuildExists(string cleanName)
            => File.Exists(Path.Combine(GetAdminBarricadeSavesDir(), cleanName + ".build.txt"));

        internal void LoadBoxBuild(string buildName)
        {
            try
            {
                string path = Path.Combine(GetAdminBarricadeSavesDir(), buildName + ".build.txt");
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || !lines[0].StartsWith("HDR|"))
                {
                    Logger.LogWarning("[KothBox] LoadBoxBuild: invalid header in " + buildName);
                    return;
                }

                string[] h = lines[0].Split('|');
                Vector3 center = new Vector3(PF(h[1]), PF(h[2]), PF(h[3]));
                float radius = PF(h[4]);
                _boxBuildArea = (center, radius);

                // clear then re-spawn (structures first so floors exist under barricades)
                try { BarricadeManager.DestroyBarricadesInSphere(center, radius, false, false); } catch { }
                BoxBuildClearStructures(center, radius);

                var barricadeLines = new List<string[]>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i])) continue;
                    string[] t = lines[i].Split('|');
                    if (t.Length < 13) continue;
                    if (t[0] == "S") BoxBuildSpawnStructure(t);
                    else if (t[0] == "B") barricadeLines.Add(t);
                }
                foreach (var t in barricadeLines) BoxBuildSpawnBarricade(t);

                Logger.Log(string.Format("[KothBox] Loaded build '{0}' at {1} r={2}", buildName, center, radius));
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] LoadBoxBuild"); }
        }

        internal void UnloadBoxBuild()
        {
            if (_boxBuildArea == null) return;
            var area = _boxBuildArea.Value;
            _boxBuildArea = null;
            try { BarricadeManager.DestroyBarricadesInSphere(area.center, area.radius, false, false); } catch { }
            BoxBuildClearStructures(area.center, area.radius);
        }

        private static void BoxBuildClearStructures(Vector3 center, float radius)
        {
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

        private static void BoxBuildSpawnBarricade(string[] t)
        {
            try
            {
                ushort id = ushort.Parse(t[1]);
                var asset = Assets.find(EAssetType.ITEM, id) as ItemBarricadeAsset;
                if (asset == null) return;
                var p = new Vector3(PF(t[2]), PF(t[3]), PF(t[4]));
                var q = new Quaternion(PF(t[5]), PF(t[6]), PF(t[7]), PF(t[8]));
                ulong owner = ulong.Parse(t[9]), group = ulong.Parse(t[10]);
                ushort health = ushort.Parse(t[11]);
                string st = t[12];
                var b = st.Length > 0
                    ? new Barricade(asset, health, Convert.FromBase64String(st))
                    : new Barricade(asset);
                BarricadeManager.dropNonPlantedBarricade(b, p, q, owner, group);
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] BoxBuild spawn barricade"); }
        }

        private static void BoxBuildSpawnStructure(string[] t)
        {
            try
            {
                ushort id = ushort.Parse(t[1]);
                var asset = Assets.find(EAssetType.ITEM, id) as ItemStructureAsset;
                if (asset == null) return;
                var p = new Vector3(PF(t[2]), PF(t[3]), PF(t[4]));
                var q = new Quaternion(PF(t[5]), PF(t[6]), PF(t[7]), PF(t[8]));
                ulong owner = ulong.Parse(t[9]), group = ulong.Parse(t[10]);
                ushort health = ushort.Parse(t[11]);
                StructureManager.dropReplicatedStructure(new Structure(asset, health), p, q, owner, group);
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] BoxBuild spawn structure"); }
        }

        private static float PF(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
