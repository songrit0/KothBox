using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace KothBox
{
    [XmlRoot("ItemSnapshot")]
    public class ItemSnapshot
    {
        [XmlAttribute]
        public byte Page { get; set; }
        [XmlAttribute]
        public byte X { get; set; }
        [XmlAttribute]
        public byte Y { get; set; }
        [XmlAttribute]
        public byte Rot { get; set; }
        [XmlAttribute]
        public ushort Id { get; set; }
        [XmlAttribute]
        public byte Amount { get; set; }
        [XmlAttribute]
        public byte Quality { get; set; }
        [XmlElement]
        public string StateData { get; set; }
    }

    [XmlRoot("InventoryStash")]
    public class InventoryStash
    {
        [XmlElement("Item")]
        public List<ItemSnapshot> Items { get; set; } = new List<ItemSnapshot>();

        public static void Serialize(string path, List<ItemSnapshot> items)
        {
            var stash = new InventoryStash { Items = items };
            var ser = new XmlSerializer(typeof(InventoryStash));
            // Crash-safe: write temp then atomically swap (a torn stash = the player's real
            // inventory lost forever, so never write the live file in place).
            var tmp = path + ".tmp";
            try
            {
                using (var w = new StreamWriter(tmp))
                    ser.Serialize(w, stash);
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
            }
            catch { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        public static List<ItemSnapshot> Deserialize(string path)
        {
            var ser = new XmlSerializer(typeof(InventoryStash));
            foreach (var p in new[] { path, path + ".bak" })
            {
                if (!File.Exists(p)) continue;
                try
                {
                    using (var r = new StreamReader(p))
                        return ((InventoryStash)ser.Deserialize(r))?.Items ?? new List<ItemSnapshot>();
                }
                catch { }
            }
            return new List<ItemSnapshot>();
        }
    }
}
