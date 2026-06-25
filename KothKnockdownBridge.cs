using System;
using System.Reflection;
using Steamworks;

namespace KothBox
{
    // Soft (reflection) bridge to the optional Knockdown plugin. While a player is in a
    // KOTH event we opt them OUT of Knockdown so lethal hits kill them normally and our
    // in-dome respawn fires (instead of leaving them crawling/downed). No hard dependency:
    // if Knockdown isn't installed, every call is a no-op.
    internal static class KothKnockdownBridge
    {
        private static bool _resolved;
        private static object _instance;
        private static MethodInfo _setOptOut;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("Knockdown.Knockdown");
                    if (t != null) break;
                }
                if (t == null) return;

                var instProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _instance = instProp?.GetValue(null, null);
                _setOptOut = t.GetMethod("SetOptOut", new[] { typeof(CSteamID), typeof(bool) });
            }
            catch { /* Knockdown absent or shape changed -> stay no-op */ }
        }

        public static void SetOptOut(ulong steamId, bool optOut)
        {
            Resolve();
            if (_instance == null || _setOptOut == null) return;
            try { _setOptOut.Invoke(_instance, new object[] { new CSteamID(steamId), optOut }); }
            catch { /* ignore */ }
        }
    }
}
