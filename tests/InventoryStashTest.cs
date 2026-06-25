// Standalone round-trip self-test for the Phase 3 inventory stash.
// Mirrors KothInventory.cs (ItemSnapshot + InventoryStash) without the
// Unturned dependency so it can compile & run on its own:
//   csc /target:exe InventoryStashTest.cs && InventoryStashTest.exe
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace KothBox.Tests
{
    [XmlRoot("ItemSnapshot")]
    public class ItemSnapshot
    {
        [XmlAttribute] public byte Page { get; set; }
        [XmlAttribute] public byte X { get; set; }
        [XmlAttribute] public byte Y { get; set; }
        [XmlAttribute] public byte Rot { get; set; }
        [XmlAttribute] public ushort Id { get; set; }
        [XmlAttribute] public byte Amount { get; set; }
        [XmlAttribute] public byte Quality { get; set; }
        [XmlElement] public string StateData { get; set; }
    }

    [XmlRoot("InventoryStash")]
    public class InventoryStash
    {
        [XmlElement("Item")] public List<ItemSnapshot> Items { get; set; } = new List<ItemSnapshot>();

        public static void Serialize(string path, List<ItemSnapshot> items)
        {
            var ser = new XmlSerializer(typeof(InventoryStash));
            using (var w = new StreamWriter(path)) ser.Serialize(w, new InventoryStash { Items = items });
        }

        public static List<ItemSnapshot> Deserialize(string path)
        {
            if (!File.Exists(path)) return new List<ItemSnapshot>();
            var ser = new XmlSerializer(typeof(InventoryStash));
            using (var r = new StreamReader(path)) return ((InventoryStash)ser.Deserialize(r)).Items;
        }
    }

    public class Program
    {
        public static int Main()
        {
            var original = new List<ItemSnapshot>
            {
                new ItemSnapshot { Page = 0, X = 0, Y = 0, Rot = 0, Id = 363, Amount = 1, Quality = 100, StateData = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) },
                new ItemSnapshot { Page = 0, X = 1, Y = 0, Rot = 1, Id = 364, Amount = 30, Quality = 0, StateData = "" },
                new ItemSnapshot { Page = 2, X = 0, Y = 0, Rot = 0, Id = 15, Amount = 1, Quality = 50, StateData = null },
            };

            var tmp = Path.Combine(Path.GetTempPath(), "koth_stash_test.xml");
            try
            {
                InventoryStash.Serialize(tmp, original);
                var restored = InventoryStash.Deserialize(tmp);

                Assert(restored.Count == original.Count, "item count");
                for (int i = 0; i < original.Count; i++)
                {
                    var o = original[i]; var r = restored[i];
                    Assert(o.Page == r.Page && o.X == r.X && o.Y == r.Y && o.Rot == r.Rot, $"slot {i}");
                    Assert(o.Id == r.Id && o.Amount == r.Amount && o.Quality == r.Quality, $"item attrs {i}");
                    var os = o.StateData ?? ""; var rs = r.StateData ?? "";
                    Assert(os == rs, $"state {i}");
                }

                Console.WriteLine("[PASS] stash round-trip preserves all items + state.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + ex.Message);
                return 1;
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        private static void Assert(bool cond, string what)
        {
            if (!cond) throw new Exception("mismatch: " + what);
        }
    }
}
