using System;
using System.Collections.Generic;
using System.IO;

namespace AO.Assets.ResourceDatabase
{
    public readonly struct AOResourceIdentity : IEquatable<AOResourceIdentity>
    {
        public AOResourceIdentity(int type, int instance)
        { Type = type; Instance = instance; }
        public int Type { get; }
        public int Instance { get; }
        public bool Equals(AOResourceIdentity other) =>
            Type == other.Type && Instance == other.Instance;
        public override bool Equals(object obj) =>
            obj is AOResourceIdentity other && Equals(other);
        public override int GetHashCode() => unchecked((Type * 397) ^ Instance);
        public override string ToString() => Type + ":" + Instance;
    }

    public sealed class AOResourceDatabase : IDisposable
    {
        private const int IndexBlockHeaderSize = 28;
        private const int IndexEntrySize = 16;
        private const int RecordEnvelopeOffset = 10;
        private readonly string _dataPath;
        private readonly Dictionary<AOResourceIdentity, ulong> _offsets =
            new Dictionary<AOResourceIdentity, ulong>();
        private readonly Dictionary<int, FileStream> _segments =
            new Dictionary<int, FileStream>();
        private readonly object _ioLock = new object();
        private uint _blockOverlap;
        private uint _segmentSize;
        private bool _disposed;

        public AOResourceDatabase(string aoInstallationRoot)
        {
            if (string.IsNullOrWhiteSpace(aoInstallationRoot))
                throw new ArgumentException("An AO installation path is required.",
                    nameof(aoInstallationRoot));
            string databaseRoot = Path.Combine(aoInstallationRoot, "cd_image", "data", "db");
            _dataPath = Path.Combine(databaseRoot, "ResourceDatabase.dat");
            LoadIndex(Path.Combine(databaseRoot, "ResourceDatabase.idx"));
        }

        public int RecordCount => _offsets.Count;

        public IEnumerable<AOResourceIdentity> EnumerateIdentities()
        {
            ThrowIfDisposed();
            return new List<AOResourceIdentity>(_offsets.Keys);
        }

        public bool Contains(int type, int instance) =>
            _offsets.ContainsKey(new AOResourceIdentity(type, instance));

        public bool TryReadRaw(int type, int instance, out byte[] data)
        {
            ThrowIfDisposed();
            data = null;
            if (!_offsets.TryGetValue(new AOResourceIdentity(type, instance),
                out ulong logicalOffset))
                return false;

            int segment = checked((int)(logicalOffset / _segmentSize));
            ulong segmentOffset = logicalOffset;
            if (segment > 0)
                segmentOffset -= (ulong)(_segmentSize - _blockOverlap) * (ulong)segment;

            lock (_ioLock)
            {
                FileStream stream = GetSegment(segment);
                long envelope = checked((long)segmentOffset + RecordEnvelopeOffset);
                if (envelope < 0 || envelope + 12 > stream.Length)
                    throw new InvalidDataException("AO resource offset is outside its data segment.");
                stream.Position = envelope;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                {
                    int storedType = reader.ReadInt32();
                    int storedInstance = reader.ReadInt32();
                    int size = reader.ReadInt32();
                    if (storedType != type || storedInstance != instance)
                        throw new InvalidDataException("AO resource identity did not match its index entry.");
                    if (size < 0 || size > stream.Length - stream.Position)
                        throw new InvalidDataException("AO resource length is invalid.");
                    data = reader.ReadBytes(size);
                    return data.Length == size;
                }
            }
        }

        private void LoadIndex(string indexPath)
        {
            using (var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 188)
                    throw new InvalidDataException("AO resource index header is truncated.");
                _blockOverlap = ReadUInt32At(reader, 12);
                int firstBlock = ReadInt32At(reader, 72);
                _segmentSize = ReadUInt32At(reader, 184);
                if (_segmentSize == 0 || _blockOverlap >= _segmentSize)
                    throw new InvalidDataException("AO resource index segment metadata is invalid.");

                var visited = new HashSet<int>();
                int block = firstBlock;
                while (block != 0)
                {
                    if (block < 0 || block + IndexBlockHeaderSize > stream.Length
                        || !visited.Add(block))
                        throw new InvalidDataException("AO resource index block chain is invalid.");
                    stream.Position = block;
                    int nextBlock = reader.ReadInt32();
                    reader.ReadInt32();
                    short count = reader.ReadInt16();
                    stream.Position += 18;
                    if (count < 0 || stream.Position + (long)count * IndexEntrySize > stream.Length)
                        throw new InvalidDataException("AO resource index entry count is invalid.");
                    for (int index = 0; index < count; index++)
                    {
                        ulong high = reader.ReadUInt32();
                        ulong low = reader.ReadUInt32();
                        ulong offset = (high << 32) | low;
                        int type = ReadBigEndianInt32(reader);
                        int instance = ReadBigEndianInt32(reader);
                        _offsets[new AOResourceIdentity(type, instance)] = offset;
                    }
                    block = nextBlock;
                }
            }
        }

        private FileStream GetSegment(int segment)
        {
            if (_segments.TryGetValue(segment, out FileStream existing)) return existing;
            string path = segment == 0 ? _dataPath : _dataPath + "." + segment.ToString("000");
            var opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _segments.Add(segment, opened);
            return opened;
        }

        private static uint ReadUInt32At(BinaryReader reader, long position)
        { reader.BaseStream.Position = position; return reader.ReadUInt32(); }
        private static int ReadInt32At(BinaryReader reader, long position)
        { reader.BaseStream.Position = position; return reader.ReadInt32(); }
        private static int ReadBigEndianInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4) throw new EndOfStreamException();
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private void ThrowIfDisposed()
        { if (_disposed) throw new ObjectDisposedException(nameof(AOResourceDatabase)); }

        public void Dispose()
        {
            if (_disposed) return;
            foreach (FileStream stream in _segments.Values) stream.Dispose();
            _segments.Clear();
            _disposed = true;
        }
    }
}
