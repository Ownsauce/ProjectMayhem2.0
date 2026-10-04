using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AO.Assets.Decoders;
using AO.Assets.ResourceDatabase;

namespace AO.Assets.Conversion
{
    public sealed class AOIndoorVisualSource
    {
        internal AOIndoorVisualSource(AOPlayfieldDefinition definition, AOIndoorVisualSet geometry,
            Dictionary<uint, byte[]> images)
        { Definition = definition; Geometry = geometry; Images = images; }
        public AOPlayfieldDefinition Definition { get; }
        public AOIndoorVisualSet Geometry { get; }
        public IReadOnlyDictionary<uint, byte[]> Images { get; }
    }

    /// <summary>On-demand local DB reader. Call on a worker; never writes a distributable asset package.</summary>
    public static class AOIndoorVisualExtractor
    {
        public static AOIndoorVisualSource Load(AOInstallValidation install, int pf,
            string helperExecutable, CancellationToken cancellation = default, IEnumerable<int> selectedRooms = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (install == null || !install.IsValid) throw new ArgumentException("The AO installation is invalid");
            if (!File.Exists(helperExecutable)) throw new FileNotFoundException("Build the indoor runtime helper first", helperExecutable);
            string work = Path.Combine(Path.GetTempPath(), "pm-indoor-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                using (var db = new AOResourceDatabase(install.RootPath))
                {
                    if (!db.TryReadRaw(1000001, pf, out var record)) throw new InvalidDataException("Missing indoor playfield " + pf);
                    var definition = AOPlayfieldDefinitionDecoder.Decode(record, pf);
                    if (!definition.IsIndoor || definition.RoomCount == 0) throw new InvalidDataException("An indoor playfield is required");
                    string plan = Path.Combine(work, "rooms.plan"), output = Path.Combine(work, "visuals.aovr");
                    var indices = (selectedRooms ?? Enumerable.Range(0, definition.RoomCount)).Distinct().OrderBy(i => i).ToArray();
                    if (indices.Length == 0 || indices.Any(i => i < 0 || i >= definition.RoomCount)) throw new ArgumentException("Invalid selected indoor rooms");
                    WritePlan(plan, definition, indices);
                    RunHelper(install.RootPath, helperExecutable, plan, output, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    var geometry = AOIndoorVisualStreamDecoder.Decode(output, pf);
                    if (geometry.TilemapId != definition.TilemapId
                        || !geometry.Rooms.Select(r => r.Index).SequenceEqual(indices))
                        throw new InvalidDataException("Incomplete or mismatched live indoor geometry");
                    var images = new Dictionary<uint, byte[]>();
                    foreach (uint material in geometry.Rooms.SelectMany(r => r.Meshes.Keys).Distinct())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int type = (material & 0x80000000) != 0 ? 1010004 : 1010009;
                        int id = (int)(material & 0x7fffffff);
                        if (!db.TryReadRaw(type, id, out var image)) throw new InvalidDataException("Missing indoor image " + type + ":" + id);
                        images.Add(material, AOTexturePayloadDecoder.Decode(image, type, id));
                    }
                    return new AOIndoorVisualSource(definition, geometry, images);
                }
            }
            finally { Directory.Delete(work, true); }
        }

        private static void WritePlan(string path, AOPlayfieldDefinition definition, int[] indices)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Encoding.ASCII.GetBytes("AORP")); writer.Write(1);
                writer.Write(definition.Id); writer.Write(definition.TilemapId); writer.Write(indices.Length);
                foreach (int i in indices)
                {
                    var room = definition.Rooms[i];
                    writer.Write(i); writer.Write(room.RotationQuarterTurns);
                    writer.Write((int)room.TileX1); writer.Write((int)room.TileY1);
                    writer.Write((int)room.TileX2); writer.Write((int)room.TileY2);
                    writer.Write(room.X); writer.Write(room.Y); writer.Write(room.Z);
                    writer.Write(room.CenterX); writer.Write(room.CenterZ);
                }
            }
        }

        private static void RunHelper(string install, string helper, string plan, string output, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            bool wine = Path.DirectorySeparatorChar == '/';
            string NativePath(string path) => wine ? "Z:" + Path.GetFullPath(path).Replace('/', '\\') : Path.GetFullPath(path);
            var arguments = new List<string>();
            if (wine) arguments.Add(Path.GetFullPath(helper));
            arguments.Add(NativePath(install)); arguments.Add(NativePath(plan)); arguments.Add(NativePath(output));
            var start = new ProcessStartInfo
            {
                FileName = wine ? "wine" : Path.GetFullPath(helper),
                Arguments = string.Join(" ", arguments.Select(Quote)),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (wine) start.EnvironmentVariables["WINEDEBUG"] = "-all";
            using (var process = Process.Start(start) ?? throw new InvalidOperationException("Indoor helper did not start"))
            {
                // Drain both pipes concurrently so neither a verbose success nor failure can deadlock.
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                var timer = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (timer.ElapsedMilliseconds > 300000) throw new TimeoutException("Indoor database read exceeded five minutes");
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0)
                        throw new InvalidDataException("Indoor helper failed: " + stderr.GetAwaiter().GetResult().Trim());
                    stdout.GetAwaiter().GetResult(); stderr.GetAwaiter().GetResult();
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                }
            }
        }

        // ProcessStartInfo.Arguments uses Windows quoting under both Mono and Wine; no shell is used.
        private static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
                result.Append(ch); slashes = 0;
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
    }
}
