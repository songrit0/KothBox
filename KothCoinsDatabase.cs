using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MySql.Data.MySqlClient;
using Logger = Rocket.Core.Logging.Logger;

namespace KothBox
{
    // One prize drawn from the shop's gacha pool (sv_gacha_prizes).
    public sealed class GachaPrize
    {
        public string Type;   // coins | meowcoins | item | vehicle | vip
        public int RefId;     // item/vehicle id
        public long Amount;
        public byte Quality;
        public string Label;
    }

    // sv_coins writer. Mirrors GameMenuDatabase.CreditCoins exactly (same table/columns).
    // Every method opens its own connection and MUST run off the main thread.
    public sealed class KothCoinsDatabase
    {
        private readonly string _conn;
        private readonly string _coins;

        public KothCoinsDatabase(DatabaseSection cfg)
        {
            _conn = cfg.ConnectionString;
            string p = SanitizeIdent(cfg.ShopPrefix, "sv_");
            _coins = p + "coins";
        }

        private static string SanitizeIdent(string raw, string fallback)
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            var sb = new StringBuilder();
            foreach (char c in raw)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            return sb.Length == 0 ? fallback : sb.ToString();
        }

        private MySqlConnection Open()
        {
            Exception last = null;
            for (int i = 0; i < 5; i++)
            {
                var c = new MySqlConnection(_conn);
                try { c.Open(); return c; }
                catch (Exception ex) { last = ex; try { c.Dispose(); } catch { } }
            }
            throw last;
        }

        /// <summary>Atomically deduct coins. Returns false if the balance was insufficient (no row
        /// updated). Mirrors GameMenu.SpendCoins — used for the KOTH entry fee.</summary>
        public bool SpendCoins(ulong steamId, long amount)
        {
            if (amount <= 0) return true;
            try
            {
                using (var c = Open())
                using (var cmd = new MySqlCommand(
                    "UPDATE `" + _coins + "` SET balance = balance - @a WHERE steam_id = @s AND balance >= @a;", c))
                {
                    cmd.Parameters.AddWithValue("@a", amount);
                    cmd.Parameters.AddWithValue("@s", steamId.ToString(CultureInfo.InvariantCulture));
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] SpendCoins"); return false; }
        }

        /// <summary>Weighted-random draw from the shop gacha pool (enabled prizes, by `weight`).
        /// Returns null if the pool is empty/unreachable. Mirrors the shop's /draw odds.</summary>
        public GachaPrize DrawGachaPrize()
        {
            try
            {
                var prizes = new List<GachaPrize>();
                var weights = new List<double>();
                double total = 0;
                using (var c = Open())
                using (var cmd = new MySqlCommand(
                    "SELECT type, ref_id, amount, quality, weight, CAST(label AS BINARY) AS label " +
                    "FROM sv_gacha_prizes WHERE enabled = 1 AND weight > 0;", c))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        double w = ToDouble(r["weight"]);
                        prizes.Add(new GachaPrize
                        {
                            Type = ToStr(r["type"]),
                            RefId = ToInt(r["ref_id"]),
                            Amount = ToLong(r["amount"], 1),
                            Quality = (byte)Math.Min(100, Math.Max(0, ToInt(r["quality"], 100))),
                            Label = ToUtf8(r["label"], "Prize")
                        });
                        weights.Add(w);
                        total += w;
                    }
                }
                if (prizes.Count == 0 || total <= 0) return null;

                double roll = _rng.NextDouble() * total;
                for (int i = 0; i < prizes.Count; i++)
                {
                    roll -= weights[i];
                    if (roll <= 0) return prizes[i];
                }
                return prizes[prizes.Count - 1];
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] DrawGachaPrize"); return null; }
        }

        private static readonly Random _rng = new Random();

        private static int ToInt(object o, int def = 0) => (o == null || o == DBNull.Value) ? def : Convert.ToInt32(o);
        private static long ToLong(object o, long def = 0) => (o == null || o == DBNull.Value) ? def : Convert.ToInt64(o);
        private static double ToDouble(object o, double def = 0) => (o == null || o == DBNull.Value) ? def : Convert.ToDouble(o);
        private static string ToStr(object o, string def = "") => (o == null || o == DBNull.Value) ? def : Convert.ToString(o);
        private static string ToUtf8(object o, string def = "")
        {
            if (o == null || o == DBNull.Value) return def;
            if (o is byte[] b) return b.Length == 0 ? def : System.Text.Encoding.UTF8.GetString(b);
            var s = Convert.ToString(o);
            return string.IsNullOrEmpty(s) ? def : s;
        }

        /// <summary>Item image URL from sv_items (image_url column). null if missing/no column.</summary>
        public string GetItemImageUrl(int id)
        {
            try
            {
                using (var c = Open())
                using (var cmd = new MySqlCommand("SELECT image_url FROM sv_items WHERE id = @i LIMIT 1;", c))
                {
                    cmd.Parameters.AddWithValue("@i", id);
                    using (var r = cmd.ExecuteReader())
                        return r.Read() ? ToStr(r["image_url"], null) : null;
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] GetItemImageUrl"); return null; }
        }

        /// <summary>Item display name from the shop catalog (sv_items). null if missing.</summary>
        public string GetItemName(int id)
        {
            try
            {
                using (var c = Open())
                using (var cmd = new MySqlCommand("SELECT CAST(name AS BINARY) AS name FROM sv_items WHERE id = @i LIMIT 1;", c))
                {
                    cmd.Parameters.AddWithValue("@i", id);
                    using (var r = cmd.ExecuteReader())
                        return r.Read() ? ToUtf8(r["name"], null) : null;
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] GetItemName"); return null; }
        }

        /// <summary>Credit coins (row created if absent). Same upsert as GameMenu.CreditCoins.</summary>
        public void CreditCoins(ulong steamId, long amount)
        {
            if (amount <= 0) return;
            try
            {
                using (var c = Open())
                using (var cmd = new MySqlCommand(
                    "INSERT INTO `" + _coins + "` (steam_id, balance) VALUES (@s, @a) " +
                    "ON DUPLICATE KEY UPDATE balance = balance + VALUES(balance);", c))
                {
                    cmd.Parameters.AddWithValue("@s", steamId.ToString(CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("@a", amount);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[KothBox] CreditCoins"); }
        }
    }
}
