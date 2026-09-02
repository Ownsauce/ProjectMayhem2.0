using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AO.Assets.ResourceDatabase
{
    public static class AOResourceTypes
    {
        public const int Playfield = 1000001;
        public const int Tilemap = 1000009;
        public const int InfoObject = 1000010;
        public const int Surface = 1000013;
        public const int Item = 1000020;
        public const int PlayfieldDynels = 1000026;
        public const int Mesh = 1010001;
        public const int CharacterMesh = 1010002;
        public const int Animation = 1010003;
        public const int Texture = 1010004;
        public const int GroundTexture = 1010006;
        public const int Icon = 1010008;
        public const int WallTexture = 1010009;
        public const int SkinTexture = 1010011;
        public const int MonsterData = 1040023;
    }

    public sealed class AOResourceCatalog
    {
        private const int RecordHeaderSize = 12;
        private readonly Dictionary<int, Dictionary<int, string>> _names;

        private AOResourceCatalog(Dictionary<int, Dictionary<int, string>> names)
        {
            _names = names;
        }

        public IEnumerable<int> ResourceTypes => _names.Keys;

        public static AOResourceCatalog Load(AOResourceDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (!database.TryReadRaw(AOResourceTypes.InfoObject, 1, out byte[] record))
                throw new InvalidDataException("The AO resource-name catalog was not found.");
            return Parse(record);
        }

        public static AOResourceCatalog Parse(byte[] record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            using (var stream = new MemoryStream(record, false))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < RecordHeaderSize + 4)
                    throw new InvalidDataException("The AO resource-name catalog is truncated.");
                int storedType = reader.ReadInt32();
                reader.ReadInt32(); // record instance
                reader.ReadInt32(); // record version
                if (storedType != AOResourceTypes.InfoObject)
                    throw new InvalidDataException("The record is not an AO resource-name catalog.");

                int typeCount = ReadCount(reader, "resource type");
                var names = new Dictionary<int, Dictionary<int, string>>(typeCount);
                for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
                {
                    EnsureRemaining(stream, 8);
                    int type = reader.ReadInt32();
                    int instanceCount = ReadCount(reader, "resource instance");
                    var instances = new Dictionary<int, string>(instanceCount);
                    for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
                    {
                        EnsureRemaining(stream, 8);
                        int instance = reader.ReadInt32();
                        int byteCount = ReadCount(reader, "resource name byte");
                        EnsureRemaining(stream, byteCount);
                        string name = DecodeWindows1252(reader.ReadBytes(byteCount))
                            .TrimEnd('\0');
                        instances[instance] = name;
                    }
                    names[type] = instances;
                }
                return new AOResourceCatalog(names);
            }
        }

        public bool TryGetName(int type, int instance, out string name)
        {
            name = null;
            return _names.TryGetValue(type, out Dictionary<int, string> instances)
                && instances.TryGetValue(instance, out name);
        }

        public IReadOnlyDictionary<int, string> GetResources(int type)
        {
            if (_names.TryGetValue(type, out Dictionary<int, string> instances))
                return instances;
            return new Dictionary<int, string>();
        }

        public IEnumerable<AOResourceCatalogEntry> Find(string text, int? type = null)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (KeyValuePair<int, Dictionary<int, string>> group in _names)
            {
                if (type.HasValue && group.Key != type.Value) continue;
                foreach (KeyValuePair<int, string> resource in group.Value)
                    if (resource.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        yield return new AOResourceCatalogEntry(group.Key, resource.Key,
                            resource.Value);
            }
        }

        private static int ReadCount(BinaryReader reader, string label)
        {
            EnsureRemaining(reader.BaseStream, 4);
            int count = reader.ReadInt32();
            if (count < 0 || count > 10_000_000)
                throw new InvalidDataException("The AO " + label + " count is invalid.");
            return count;
        }

        private static void EnsureRemaining(Stream stream, int count)
        {
            if (count < 0 || count > stream.Length - stream.Position)
                throw new InvalidDataException("The AO resource-name catalog is truncated.");
        }

        private static string DecodeWindows1252(byte[] bytes)
        {
            // Avoid Encoding.GetEncoding(1252), whose provider is optional in modern
            // .NET and unavailable in some Unity compatibility profiles.
            const string extended = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F"
                + "\u0090‘’“”•–—˜™š›œ\u009DžŸ";
            var characters = new char[bytes.Length];
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = bytes[index];
                characters[index] = value >= 0x80 && value <= 0x9F
                    ? extended[value - 0x80]
                    : (char)value;
            }
            return new string(characters);
        }
    }

    public readonly struct AOResourceCatalogEntry
    {
        public AOResourceCatalogEntry(int type, int instance, string name)
        { Type = type; Instance = instance; Name = name; }
        public int Type { get; }
        public int Instance { get; }
        public string Name { get; }
        public override string ToString() => Type + ":" + Instance + " " + Name;
    }
}
