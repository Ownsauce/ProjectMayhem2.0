using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldGen.Contracts;
using WorldGen.Dungeons;

if (args.Length != 4 || !ulong.TryParse(args[1], out ulong firstSeed)
    || !int.TryParse(args[2], out int seedCount) || seedCount < 1
    || !int.TryParse(args[3], out int roomCount))
{
    Console.Error.WriteLine("Usage: WorldGen.SeedSweep <generator-version> <first-seed> <seed-count> <room-count>");
    return 2;
}

Console.WriteLine("version,profile,seed,valid,error,hash,rooms,connections,shortest_boss_path,max_distance,mean_degree,dead_end_ratio,loops");
int failures = 0;
foreach (DungeonLayoutProfile profile in Enum.GetValues<DungeonLayoutProfile>())
{
    for (int offset = 0; offset < seedCount; offset++)
    {
        if (ulong.MaxValue - firstSeed < (ulong)offset) throw new ArgumentOutOfRangeException(nameof(firstSeed));
        ulong seed = firstSeed + (ulong)offset;
        try
        {
            int rooms = profile == DungeonLayoutProfile.TempleReference ? 30 : roomCount;
            var manifest = new GenerationManifest("mission-dungeon", args[0], 2, seed,
                $"seed-sweep-{profile}-{seed}", "development");
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = profile.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonGenerationProfileCatalog.DefaultTheme(profile).ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] = DungeonAssetSource.Procedural.ToString();
            manifest.Parameters["roomCount"] = rooms.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DungeonLayout layout = new DungeonGenerator().Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(rooms, profile));
            var validation = DungeonValidator.Validate(layout);
            var adjacency = layout.Rooms.ToDictionary(room => room.Id,
                _ => new List<string>(), StringComparer.Ordinal);
            foreach (DungeonConnection connection in layout.Connections)
            {
                adjacency[connection.FromRoomId].Add(connection.ToRoomId);
                adjacency[connection.ToRoomId].Add(connection.FromRoomId);
            }
            string entrance = layout.Rooms.Single(room => room.Role == DungeonRoomRole.Entrance).Id;
            string boss = layout.Rooms.Single(room => room.Role == DungeonRoomRole.Boss).Id;
            var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [entrance] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(entrance);
            while (queue.Count != 0)
            {
                string current = queue.Dequeue();
                foreach (string next in adjacency[current])
                    if (distances.TryAdd(next, distances[current] + 1)) queue.Enqueue(next);
            }
            int deadEnds = adjacency.Values.Count(neighbors => neighbors.Count == 1);
            int loops = layout.Connections.Count - layout.Rooms.Count + 1;
            string error = string.Join("; ", validation.Errors);
            if (!validation.IsValid || distances.Count != layout.Rooms.Count) failures++;
            Console.WriteLine(string.Join(",", Csv(args[0]), profile, seed,
                validation.IsValid && distances.Count == layout.Rooms.Count, Csv(error),
                DungeonLayoutHasher.Compute(layout), layout.Rooms.Count, layout.Connections.Count,
                distances.GetValueOrDefault(boss, -1), distances.Values.DefaultIfEmpty(-1).Max(),
                (2.0 * layout.Connections.Count / layout.Rooms.Count).ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ((double)deadEnds / layout.Rooms.Count).ToString("F3", System.Globalization.CultureInfo.InvariantCulture), loops));
        }
        catch (Exception exception)
        {
            failures++;
            Console.WriteLine(string.Join(",", Csv(args[0]), profile, seed,
                "false", Csv(exception.GetType().Name + ": " + exception.Message),
                "", "", "", "", "", "", "", ""));
        }
    }
}
return failures == 0 ? 0 : 1;

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
