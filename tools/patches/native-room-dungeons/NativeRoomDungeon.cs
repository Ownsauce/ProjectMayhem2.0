using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WorldGen.Contracts;
using WorldGen.Determinism;
using WorldGen.Spatial;

namespace WorldGen.Dungeons
{
    [Serializable] public sealed class NativeRoomCatalog
    {
        public int Version = 3, SourcePlayfield = 127, EntranceSourceRoom = 8;
        public string SourceSha256, SurfaceSha256;
        public NativeRoomTemplate[] Rooms;
        public NativeRoomRecipeData PinnedRecipe;
    }
    [Serializable] public sealed class NativeRoomTemplate
    {
        public int SourceIndex, OriginX, OriginY, OriginZ, MinX, MinZ, MaxX, MaxZ, MinY, MaxY = 30000;
        public int SpawnX, SpawnY, SpawnZ;
        public string Name, Pool;
        public bool Enabled = true;
        public NativeRoomAnnotation Annotation;
        public NativeRoomSocket[] Sockets;
        public int SpawnRegion = -1;
        public NativeWalkPoint[] WalkPoints = Array.Empty<NativeWalkPoint>();
        public NativeRoomLight[] Lights = Array.Empty<NativeRoomLight>();
    }
    [Serializable] public sealed class NativeRoomSocket
    {
        public int X, Y, Z, Facing, Width, Height = 8000;
        public bool Exterior, Blocked, Excluded;
        public NativeClosureStyle ClosureStyle;
        public int Clearance, Region = -1;
    }
    public enum NativeClosureStyle { MetalPanel, BlackBacking }
    public sealed class NativeRoomSeal
    {
        public WorldVector3 Center, Size;
        public int Facing;
        public NativeClosureStyle Style;
    }
    [Serializable] public sealed class NativeWalkPoint { public int X, Y, Z, Ceiling, Region; }
    [Serializable] public sealed class NativeRoomLight
    {
        public int X, Y, Z;
        public float Intensity = 3, Range = 12, Emission = 8, BaseIntensity = 3, BaseRange = 12;
    }
    [Serializable] public sealed class NativeRoomPlacement
    {
        public int Index, SourceIndex, X, Y, Z, QuarterTurns;
        public HashSet<int> UsedSockets = new HashSet<int>();
    }
    [Serializable] public sealed class NativeRoomJoin
    {
        public int From, FromSocket, To, ToSocket;
    }
    public sealed class NativeRoomRecipe
    {
        public NativeRoomCatalog Catalog;
        public readonly List<NativeRoomPlacement> Rooms = new List<NativeRoomPlacement>();
        public readonly List<NativeRoomJoin> Joins = new List<NativeRoomJoin>();
        private NativeRoomCatalog indexedCatalog;
        private Dictionary<int, NativeRoomTemplate> templates;
        public NativeRoomTemplate Template(NativeRoomPlacement room)
        {
            if (!ReferenceEquals(indexedCatalog, Catalog)) { templates = Catalog.Rooms.ToDictionary(t => t.SourceIndex); indexedCatalog = Catalog; }
            return templates[room.SourceIndex];
        }
    }
    public static class NativeRoomDungeon
    {
        public const string GeneratorId = "native-room-dungeon";
        public const string GeneratorVersion = "1.2.0";
        public static NativeRoomRecipe Generate(NativeRoomCatalog catalog, ulong seed, int count, string pool)
        {
            Validate(catalog);
            if (count < 4 || count > 24) throw new ArgumentOutOfRangeException(nameof(count), "Use 4–24 rooms.");
            if (catalog.PinnedRecipe != null && count < catalog.PinnedRecipe.Rooms.Length) throw new ArgumentException("Room count is smaller than the pinned layout.");
            if (pool != "mixed" && !catalog.Rooms.Any(t => t.Pool == pool)) throw new ArgumentException("Unknown room pool: " + pool);
            var entrance = catalog.Rooms.Single(t => t.SourceIndex == catalog.EntranceSourceRoom);
            var candidates = catalog.Rooms.Where(t => t.Enabled && t.SourceIndex != entrance.SourceIndex && (pool == "mixed" || t.Pool == pool)).ToArray();
            if (candidates.Length == 0) throw new InvalidDataException("Empty native room pool.");
            for (int attempt = 0; attempt < 128; attempt++)
            {
                var random = new DeterministicRandom(seed, (ulong)attempt + 127);
                var result = catalog.PinnedRecipe != null ? catalog.PinnedRecipe.Restore(catalog) : new NativeRoomRecipe { Catalog = catalog };
                if (result.Rooms.Count == 0) result.Rooms.Add(new NativeRoomPlacement { Index = 0, SourceIndex = entrance.SourceIndex,
                    X = entrance.OriginX, Y = entrance.OriginY, Z = entrance.OriginZ });
                while (result.Rooms.Count < count)
                {
                    var options = new List<Tuple<NativeRoomPlacement, int, NativeRoomTemplate, int>>();
                    foreach (var from in result.Rooms)
                    {
                        var template = result.Template(from);
                        for (int a = 0; a < template.Sockets.Length; a++)
                        {
                            if ((template.Sockets[a].Exterior || template.Sockets[a].Blocked || template.Sockets[a].Excluded) || from.UsedSockets.Contains(a) || !NativeRoomTraversal.CanUse(result, from, a)) continue;
                            foreach (var candidate in candidates)
                            for (int b = 0; b < candidate.Sockets.Length; b++)
                                if (!candidate.Sockets[b].Exterior && !candidate.Sockets[b].Blocked && !candidate.Sockets[b].Excluded && candidate.Sockets[b].Width == template.Sockets[a].Width && candidate.WalkPoints.Any(p => p.Region == candidate.Sockets[b].Region))
                                    options.Add(Tuple.Create(from, a, candidate, b));
                        }
                    }
                    bool placed = false;
                    while (options.Count > 0)
                    {
                        int pick = random.NextInt(options.Count); var option = options[pick];
                        options[pick] = options[options.Count - 1]; options.RemoveAt(options.Count - 1);
                        var from = option.Item1; int a = option.Item2; var candidate = option.Item3; int b = option.Item4;
                        var fromSocket = result.Template(from).Sockets[a]; var toSocket = candidate.Sockets[b];
                        int turns = (fromSocket.Facing + from.QuarterTurns + 2 - toSocket.Facing) & 3;
                        var target = Transform(from, new WorldVector3(fromSocket.X, fromSocket.Y, fromSocket.Z));
                        var offset = Rotate(new WorldVector3(toSocket.X, toSocket.Y, toSocket.Z), turns);
                        var next = new NativeRoomPlacement { Index = result.Rooms.Count, SourceIndex = candidate.SourceIndex,
                            X = target.X - offset.X, Y = target.Y - offset.Y, Z = target.Z - offset.Z, QuarterTurns = turns };
                        WorldBounds bounds = Bounds(candidate, next);
                        if (result.Rooms.Any(other => Overlaps(bounds, Bounds(result.Template(other), other)))) continue;
                        from.UsedSockets.Add(a); next.UsedSockets.Add(b); result.Rooms.Add(next);
                        result.Joins.Add(new NativeRoomJoin { From = from.Index, FromSocket = a, To = next.Index, ToSocket = b });
                        placed = true; break;
                    }
                    if (!placed) break;
                }
                if (result.Rooms.Count == count) { NativeRoomTraversal.Validate(result); return result; }
            }
            throw new InvalidOperationException("No connected layout fits this room count/pool; try fewer rooms or another seed.");
        }
        public static void Validate(NativeRoomCatalog catalog)
        {
            if (catalog == null || catalog.Version != 3 || catalog.SourcePlayfield <= 0 || catalog.Rooms == null
                || catalog.Rooms.Length < 2 || catalog.Rooms.Length > 512 || catalog.Rooms.Any(t => t == null) || catalog.Rooms.Select(t => t.SourceIndex).Distinct().Count() != catalog.Rooms.Length)
                throw new InvalidDataException("Invalid native room catalog.");
            foreach (var room in catalog.Rooms)
            {
                if (room.MinX >= room.MaxX || room.MinZ >= room.MaxZ || room.MinY >= room.MaxY || room.Sockets == null || room.Sockets.Length == 0 || room.WalkPoints == null || room.WalkPoints.Length > 200000 || room.Lights == null || room.Lights.Length > 4096)
                    throw new InvalidDataException("Invalid native room template.");
                NativeRoomAnnotations.Validate(room, room.Annotation);
                foreach (var light in room.Lights)
                    if (light == null || float.IsNaN(light.Intensity) || float.IsInfinity(light.Intensity) || light.Intensity < 0 || light.Intensity > 200
                        || float.IsNaN(light.Range) || float.IsInfinity(light.Range) || light.Range <= 0 || light.Range > 128
                        || float.IsNaN(light.Emission) || float.IsInfinity(light.Emission) || light.Emission < 0 || light.Emission > 50)
                        throw new InvalidDataException("Invalid native fixture light.");
                foreach (var socket in room.Sockets)
                    if (socket == null) throw new InvalidDataException("Missing native room socket.");
                    else if (socket.Facing < 0 || socket.Facing > 3 || socket.Width <= 0 || socket.Height < 2500
                        || (!socket.Exterior && !socket.Blocked && (socket.Clearance < 1900 || socket.Region < 0)))
                        throw new InvalidDataException("Invalid native room socket.");
            }
            var entrance = catalog.Rooms.SingleOrDefault(r => r.SourceIndex == catalog.EntranceSourceRoom);
            if (entrance == null || !entrance.Enabled || entrance.SpawnRegion < 0 || !entrance.WalkPoints.Any(p => p.Region == entrance.SpawnRegion))
                throw new InvalidDataException("Native entrance has no validated walking region.");
        }
        public static WorldVector3 Rotate(WorldVector3 value, int turns)
        {
            switch (turns & 3) {
                case 1: return new WorldVector3(value.Z, value.Y, -value.X);
                case 2: return new WorldVector3(-value.X, value.Y, -value.Z);
                case 3: return new WorldVector3(-value.Z, value.Y, value.X);
                default: return value;
            }
        }
        public static WorldVector3 Transform(NativeRoomPlacement room, WorldVector3 local)
        { var rotated = Rotate(local, room.QuarterTurns); return new WorldVector3(room.X + rotated.X, room.Y + rotated.Y, room.Z + rotated.Z); }
        public static WorldBounds Bounds(NativeRoomTemplate template, NativeRoomPlacement room)
        {
            var corners = new[] { Transform(room, new WorldVector3(template.MinX,0,template.MinZ)), Transform(room,new WorldVector3(template.MaxX,0,template.MinZ)),
                Transform(room,new WorldVector3(template.MinX,0,template.MaxZ)), Transform(room,new WorldVector3(template.MaxX,0,template.MaxZ)) };
            return new WorldBounds(new WorldVector3(corners.Min(v=>v.X),room.Y+template.MinY,corners.Min(v=>v.Z)),
                new WorldVector3(corners.Max(v=>v.X),room.Y+template.MaxY,corners.Max(v=>v.Z)));
        }
        private static bool Overlaps(WorldBounds a, WorldBounds b) => a.Minimum.X < b.Maximum.X - 50 && b.Minimum.X < a.Maximum.X - 50
            && a.Minimum.Z < b.Maximum.Z - 50 && b.Minimum.Z < a.Maximum.Z - 50;
        public static DungeonLayout Layout(GenerationManifest manifest, NativeRoomRecipe recipe)
        {
            var rooms = recipe.Rooms.Select(r => {
                var template = recipe.Template(r); var bounds = Bounds(template,r);
                var size = new WorldVector3(bounds.Maximum.X-bounds.Minimum.X,bounds.Maximum.Y-bounds.Minimum.Y,bounds.Maximum.Z-bounds.Minimum.Z);
                var center = new WorldVector3((bounds.Minimum.X+bounds.Maximum.X)/2,(bounds.Minimum.Y+bounds.Maximum.Y)/2,(bounds.Minimum.Z+bounds.Maximum.Z)/2);
                return new DungeonRoom("native-"+r.Index,r.Index,r.Index==0?DungeonRoomRole.Entrance:DungeonRoomRole.Normal,center,size,bounds,
                    DungeonRoomShape.Rectangle,DungeonModuleKind.CombatRoom,template.Name);
            }).ToArray();
            var start = recipe.Rooms[0]; var source = recipe.Template(start);
            var spawn = Transform(start,new WorldVector3(source.SpawnX,source.SpawnY,source.SpawnZ));
            return new DungeonLayout(manifest,rooms,recipe.Joins.Select((j,i)=>new DungeonConnection("join-"+i,"native-"+j.From,"native-"+j.To,true)).ToArray(),
                Array.Empty<DungeonCorridor>(),Array.Empty<DungeonDoorPortal>(),new[] { new DungeonSpawnPoint("native-start","native-0",DungeonSpawnRole.PlayerEntrance,spawn,0) });
        }
        public static string Hash(GenerationManifest manifest, NativeRoomRecipe recipe)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write(ManifestHasher.Compute(manifest));
                foreach (var r in recipe.Rooms) { writer.Write(r.Index);writer.Write(r.SourceIndex);writer.Write(r.X);writer.Write(r.Y);writer.Write(r.Z);writer.Write(r.QuarterTurns); }
                foreach (var j in recipe.Joins) {writer.Write(j.From);writer.Write(j.FromSocket);writer.Write(j.To);writer.Write(j.ToSocket);}
                writer.Flush(); return Sha(stream.ToArray());
            }
        }
        public static string Sha(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        public static IEnumerable<NativeRoomSeal> Seals(NativeRoomRecipe recipe)
        {
            foreach (var room in recipe.Rooms)
            {
                var template = recipe.Template(room);
                for (int i=0;i<template.Sockets.Length;i++)
                {
                    if (room.UsedSockets.Contains(i)) continue;
                    var socket=template.Sockets[i];var center=Transform(room,new WorldVector3(socket.X,socket.Y+socket.Height/2,socket.Z));
                    yield return new NativeRoomSeal { Center = center, Size = new WorldVector3(socket.Width+400,socket.Height,400), Facing = (socket.Facing+room.QuarterTurns)&3, Style = socket.ClosureStyle };
                }
            }
        }
        public static IEnumerable<WorldVector3> SealTriangles(NativeRoomRecipe recipe)
        {
            int[] faces={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            foreach(var seal in Seals(recipe))
            {
                var vertices=new WorldVector3[8];int x=seal.Size.X/2,y=seal.Size.Y/2,z=seal.Size.Z/2;
                var local=new[]{new WorldVector3(-x,-y,-z),new WorldVector3(x,-y,-z),new WorldVector3(x,y,-z),new WorldVector3(-x,y,-z),
                    new WorldVector3(-x,-y,z),new WorldVector3(x,-y,z),new WorldVector3(x,y,z),new WorldVector3(-x,y,z)};
                for(int i=0;i<8;i++){var p=Rotate(local[i],seal.Facing);vertices[i]=new WorldVector3(p.X+seal.Center.X,p.Y+seal.Center.Y,p.Z+seal.Center.Z);}
                foreach(int index in faces)yield return vertices[index];
            }
        }
    }
}
