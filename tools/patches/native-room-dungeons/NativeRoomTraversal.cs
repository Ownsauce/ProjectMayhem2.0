using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldGen.Spatial;

namespace WorldGen.Dungeons
{
    /// <summary>Shared validation of bidirectional room passages and complete entrance reachability.</summary>
    public static class NativeRoomTraversal
    {
        public static bool CanUse(NativeRoomRecipe recipe, NativeRoomPlacement room, int socket)
        {
            var template = recipe.Template(room);
            int region = template.Sockets[socket].Region;
            if (region < 0) return false;
            if (room.Index == 0 && region != template.SpawnRegion) return false;
            return room.UsedSockets.All(used => template.Sockets[used].Region == region);
        }
        public static void Validate(NativeRoomRecipe recipe)
        {
            if (recipe == null || recipe.Rooms.Count < 1 || recipe.Joins.Count != recipe.Rooms.Count - 1)
                throw new InvalidDataException("Invalid native room connection graph.");
            var adjacency = recipe.Rooms.ToDictionary(r => r.Index, r => new List<int>());
            var ports = new HashSet<string>(StringComparer.Ordinal);
            foreach (var join in recipe.Joins)
            {
                if (!adjacency.ContainsKey(join.From) || !adjacency.ContainsKey(join.To) || join.From == join.To)
                    throw new InvalidDataException("Unknown or self-connected native room.");
                var a = recipe.Rooms[join.From]; var b = recipe.Rooms[join.To];
                var ta = recipe.Template(a); var tb = recipe.Template(b);
                if (join.FromSocket < 0 || join.FromSocket >= ta.Sockets.Length || join.ToSocket < 0 || join.ToSocket >= tb.Sockets.Length)
                    throw new InvalidDataException("Unknown native doorway.");
                var sa = ta.Sockets[join.FromSocket]; var sb = tb.Sockets[join.ToSocket];
                if (!ports.Add(join.From + ":" + join.FromSocket) || !ports.Add(join.To + ":" + join.ToSocket)
                    || sa.Exterior || sb.Exterior || sa.Blocked || sb.Blocked || sa.Excluded || sb.Excluded || sa.Width != sb.Width
                    || ((sa.Facing + a.QuarterTurns + 2) & 3) != ((sb.Facing + b.QuarterTurns) & 3)
                    || !NativeRoomDungeon.Transform(a, new WorldVector3(sa.X, sa.Y, sa.Z)).Equals(NativeRoomDungeon.Transform(b, new WorldVector3(sb.X, sb.Y, sb.Z))))
                    throw new InvalidDataException("Unsafe or mismatched native doorway.");
                adjacency[a.Index].Add(b.Index); adjacency[b.Index].Add(a.Index);
            }
            foreach (var room in recipe.Rooms)
            {
                if (!recipe.Template(room).Enabled) throw new InvalidDataException("Recipe uses an excluded room.");
                foreach (int socket in room.UsedSockets)
                    if (!CanUse(recipe, room, socket)) throw new InvalidDataException("Room " + room.Index + " has no walking route between its used doorways.");
            }
            var seen = new HashSet<int> { 0 }; var pending = new Queue<int>(); pending.Enqueue(0);
            while (pending.Count > 0) foreach (int next in adjacency[pending.Dequeue()]) if (seen.Add(next)) pending.Enqueue(next);
            if (seen.Count != recipe.Rooms.Count) throw new InvalidDataException("Native dungeon contains unreachable rooms.");
        }
    }
    [Serializable] public sealed class NativeRoomPlacementData { public int Index, SourceIndex, X, Y, Z, QuarterTurns; }
    [Serializable] public sealed class NativeRoomRecipeData
    {
        public int Version = 1;
        public NativeRoomPlacementData[] Rooms;
        public NativeRoomJoin[] Joins;
        public static NativeRoomRecipeData Capture(NativeRoomRecipe recipe) => new NativeRoomRecipeData {
            Rooms = recipe.Rooms.Select(r => new NativeRoomPlacementData { Index = r.Index, SourceIndex = r.SourceIndex,
                X = r.X, Y = r.Y, Z = r.Z, QuarterTurns = r.QuarterTurns }).ToArray(),
            Joins = recipe.Joins.Select(j => new NativeRoomJoin { From = j.From, FromSocket = j.FromSocket, To = j.To, ToSocket = j.ToSocket }).ToArray()
        };
        public NativeRoomRecipe Restore(NativeRoomCatalog catalog)
        {
            NativeRoomDungeon.Validate(catalog);
            if (Version != 1 || Rooms == null || Rooms.Length < 4 || Rooms.Length > 24 || Joins == null)
                throw new InvalidDataException("Invalid saved native recipe.");
            var recipe = new NativeRoomRecipe { Catalog = catalog };
            for (int i = 0; i < Rooms.Length; i++)
            {
                var r = Rooms[i];
                if (r == null || r.Index != i || r.QuarterTurns < 0 || r.QuarterTurns > 3 || !catalog.Rooms.Any(t => t.SourceIndex == r.SourceIndex))
                    throw new InvalidDataException("Invalid saved native placement.");
                recipe.Rooms.Add(new NativeRoomPlacement { Index = i, SourceIndex = r.SourceIndex, X = r.X, Y = r.Y, Z = r.Z, QuarterTurns = r.QuarterTurns });
            }
            if (recipe.Rooms[0].SourceIndex != catalog.EntranceSourceRoom) throw new InvalidDataException("Saved native entrance changed.");
            foreach (var j in Joins)
            {
                if (j == null || j.From < 0 || j.From >= Rooms.Length || j.To < 0 || j.To >= Rooms.Length) throw new InvalidDataException("Invalid saved native join.");
                recipe.Joins.Add(new NativeRoomJoin { From = j.From, FromSocket = j.FromSocket, To = j.To, ToSocket = j.ToSocket });
                recipe.Rooms[j.From].UsedSockets.Add(j.FromSocket); recipe.Rooms[j.To].UsedSockets.Add(j.ToSocket);
            }
            NativeRoomTraversal.Validate(recipe);
            return recipe;
        }
    }
}
