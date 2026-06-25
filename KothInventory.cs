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
            try
            {
                using (var w = new StreamWriter(path))
                    ser.Serialize(w, stash);
            }
            catch { }
        }

        public static List<ItemSnapshot> Deserialize(string path)
        {
            if (!File.Exists(path)) return new List<ItemSnapshot>();
            var ser = new XmlSerializer(typeof(InventoryStash));
            try
            {
                using (var r = new StreamReader(path))
                    return ((InventoryStash)ser.Deserialize(r))?.Items ?? new List<ItemSnapshot>();
            }
            catch { return new List<ItemSnapshot>(); }
        }
    }
}
