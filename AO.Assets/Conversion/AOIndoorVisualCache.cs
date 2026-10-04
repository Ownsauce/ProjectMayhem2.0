using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AO.Assets.Decoders;
using AO.Assets.ResourceDatabase;

namespace AO.Assets.Conversion
{
    /// <summary>Bounded decoded room cache in RAM. No exported asset files or AO binaries.</summary>
    public static class AOIndoorVisualCache
    {
        private sealed class Entry { public AOIndoorVisualSource Source; public long Bytes, Used; }
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private const long Budget = 96 * 1024 * 1024;
        private static long clock, bytes, hits, decoded;
        public static long MemoryBytes { get { lock (Sync) return bytes; } }
        public static int CachedRooms { get { lock (Sync) return Entries.Count; } }
        public static long CacheHits { get { lock (Sync) return hits; } }
        public static long DecodedRooms { get { lock (Sync) return decoded; } }
        public static void Clear() { lock (Sync) { Entries.Clear(); bytes = hits = decoded = 0; } }
        public static AOIndoorVisualSource Load(AOInstallValidation install, int pf, string helper, CancellationToken token, int[] selected)
        {
            if (selected == null) return AOIndoorVisualExtractor.Load(install, pf, helper, token);
            var wanted = selected.Distinct().OrderBy(i => i).ToArray();
            string prefix = install.RootPath + "|" + install.DatabaseFingerprint + "|" + pf + "|";
            var ready = new Dictionary<int, AOIndoorVisualSource>();
            lock (Sync) foreach (int room in wanted)
                if (Entries.TryGetValue(prefix + room, out var entry)) { entry.Used = ++clock; hits++; ready.Add(room, entry.Source); }
            var missing = wanted.Where(i => !ready.ContainsKey(i)).ToArray();
            if (missing.Length > 0)
            {
                var loaded = AOIndoorVisualExtractor.Load(install, pf, helper, token, missing);
                foreach (var room in loaded.Geometry.Rooms)
                {
                    var images = room.Meshes.Keys.ToDictionary(id => id, id => loaded.Images[id]);
                    var source = new AOIndoorVisualSource(loaded.Definition,
                        new AOIndoorVisualSet(pf, loaded.Geometry.TilemapId, new List<AOIndoorVisualRoom> { room }, loaded.Geometry.VisualResourceIds), images);
                    ready.Add(room.Index, source);
                    long size = images.Values.Sum(image => (long)image.Length) + room.Meshes.Values.Sum(m => (long)(m.Positions.Length + m.Normals.Length + m.Uvs.Length + m.Triangles.Length) * 4);
                    lock (Sync)
                    {
                        decoded++;
                        string key = prefix + room.Index;
                        if (Entries.TryGetValue(key, out var old)) bytes -= old.Bytes;
                        Entries[key] = new Entry { Source = source, Bytes = size, Used = ++clock }; bytes += size;
                        while (bytes > Budget && Entries.Count > 0)
                        {
                            var oldest = Entries.OrderBy(p => p.Value.Used).First(); bytes -= oldest.Value.Bytes; Entries.Remove(oldest.Key);
                        }
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if (ready.Count == 0) throw new ArgumentException("Select at least one native room.");
            var first = ready.Values.First(); var allImages = new Dictionary<uint, byte[]>();
            foreach (var source in ready.Values) foreach (var pair in source.Images) allImages[pair.Key] = pair.Value;
            return new AOIndoorVisualSource(first.Definition,
                new AOIndoorVisualSet(pf, first.Geometry.TilemapId, wanted.Select(i => ready[i].Geometry.Rooms[0]).ToList(),
                    ready.Values.SelectMany(s => s.Geometry.VisualResourceIds).Distinct().OrderBy(i => i).ToArray()), allImages);
        }
    }
}
