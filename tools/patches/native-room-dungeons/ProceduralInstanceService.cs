namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;

    using WorldGen.Contracts;
    using WorldGen.Dungeons;
    using Vector3 = AORebirth.Core.Vector.Vector3;

    /// <summary>
    /// Owns deterministic procedural layouts on the authoritative zone server.
    /// This service does not mutate a live playfield; callers explicitly decide
    /// when a validated layout should become an active instance.
    /// </summary>
    public sealed class ProceduralInstanceService
    {
        private readonly ConcurrentDictionary<string, ProceduralInstance> _instances =
            new ConcurrentDictionary<string, ProceduralInstance>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<int, ProceduralInstance> _instancesByPlayfield = new();
        private readonly DungeonGenerator _generator = new DungeonGenerator();
        private readonly ConcurrentDictionary<int, ProceduralReturnLocation> _returns = new();
        public const int FirstGeneratedPlayfield = 900000;
        private int _nextPlayfieldId = FirstGeneratedPlayfield - 1;
        private readonly object _nativeSync = new();

        public ProceduralInstance GenerateDungeon(
            string instanceId,
            ulong seed,
            int roomCount,
            string contentCatalogHash,
            DungeonLayoutProfile profile = DungeonLayoutProfile.Facility,
            DungeonVisualTheme theme = DungeonVisualTheme.Industrial,
            DungeonAssetSource assetSource = DungeonAssetSource.Procedural,
            int excludedPlayfieldId = 0)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Instance ID is required.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(contentCatalogHash))
                throw new ArgumentException("Content catalog hash is required.", nameof(contentCatalogHash));

            GenerationManifest manifest = WorldGenerationCompatibility.CreateDungeonManifest(
                seed, instanceId, contentCatalogHash);
            manifest.Parameters["roomCount"] = roomCount.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = profile.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] = theme.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] = assetSource.ToString();
            DungeonLayout layout = _generator.Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(roomCount, profile));
            int playfieldId;
            do
            {
                playfieldId = Interlocked.Increment(ref _nextPlayfieldId);
            }
            while (playfieldId == excludedPlayfieldId
                   || _instancesByPlayfield.ContainsKey(playfieldId));
            var generated = new ProceduralInstance(playfieldId, layout, DungeonLayoutHasher.Compute(layout));

            if (!_instances.TryAdd(instanceId, generated))
                throw new InvalidOperationException("A procedural instance with that ID already exists: " + instanceId);
            if (!_instancesByPlayfield.TryAdd(playfieldId, generated))
            {
                _instances.TryRemove(instanceId, out _);
                throw new InvalidOperationException("Procedural playfield ID collision: " + playfieldId);
            }
            return generated;
        }

        public ProceduralInstance GenerateNativeCopy(string instanceId, ulong seed, int excludedPlayfieldId, int sourcePlayfield = 127, bool originalClient = false, int? clientPlayfield = null, Func<int, bool>? isLoaded = null)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("Instance ID is required.");
            NativeCopyPackage package = NativeCopyPackage.Load(sourcePlayfield);
            var manifest = new GenerationManifest("native-playfield-copy", "1.0.0", 1,
                seed, instanceId, package.ContentHash);
            manifest.Parameters["nativePlayfield"] = sourcePlayfield.ToString(CultureInfo.InvariantCulture);
            if (originalClient) manifest.Parameters[NativeClientPlayfieldAdapter.ModeParameter] = NativeClientPlayfieldAdapter.SourceCopyMode;
            manifest.Parameters["sourceSha256"] = package.SourceSha256;
            manifest.Parameters["surfaceSha256"] = package.SurfaceSha256;
            var layout = new DungeonLayout(manifest, Array.Empty<DungeonRoom>(),
                Array.Empty<DungeonConnection>(), Array.Empty<DungeonCorridor>(),
                Array.Empty<DungeonDoorPortal>(), new[] {
                    new DungeonSpawnPoint("native-entrance", "native-source", DungeonSpawnRole.PlayerEntrance,
                        new WorldGen.Spatial.WorldVector3((int)(package.SpawnX*1000),
                            (int)(package.SpawnY*1000), (int)(package.SpawnZ*1000)), 0) });
            if (originalClient)
            {
                lock (_nativeSync)
                {
                    var slots = NativeClientPlayfieldSlots.Load();
                    if (slots.SourcePlayfield != sourcePlayfield)
                        throw new InvalidOperationException("No original-client slots are configured for this source.");
                    int target = clientPlayfield ?? slots.ReservedPlayfields.FirstOrDefault(id => id != excludedPlayfieldId
                        && !_instancesByPlayfield.ContainsKey(id) && !(isLoaded?.Invoke(id) ?? false));
                    if (!slots.ReservedPlayfields.Contains(target) || target == excludedPlayfieldId
                        || _instancesByPlayfield.ContainsKey(target) || (isLoaded?.Invoke(target) ?? false))
                        throw new InvalidOperationException("Original-client playfield slot is unavailable or not reserved. Choose another configured slot or restart the local server to clear the in-memory copy.");
                    manifest.Parameters["nativeClientPlayfield"] = target.ToString(CultureInfo.InvariantCulture);
                    return RegisterCopy(target, layout, package.ContentHash);
                }
            }
            int playfieldId;
            do { playfieldId = Interlocked.Increment(ref _nextPlayfieldId); }
            while (playfieldId == excludedPlayfieldId || _instancesByPlayfield.ContainsKey(playfieldId));
            return RegisterCopy(playfieldId, layout, package.ContentHash);
        }

        public ProceduralInstance GenerateNativeAcg(string instanceId, int seed, int targetRooms, int excludedPlayfieldId,
            string gameDataRoot, int? clientPlayfield, Func<int, bool> isLoaded)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || seed < 0) throw new ArgumentException("Invalid native dungeon name or seed.");
            if (targetRooms < AORebirth.DungeonGenerator.DungeonGenerator.MinFloorSize
                || targetRooms > AORebirth.DungeonGenerator.DungeonGenerator.MaxFloorSize)
                throw new ArgumentOutOfRangeException(nameof(targetRooms));
            lock (_nativeSync)
            {
                var slots = NativeClientPlayfieldSlots.Load();
                if (slots.AcgStyle <= 0) throw new InvalidOperationException("No native ACG style is configured.");
                int target = clientPlayfield ?? slots.ReservedPlayfields.FirstOrDefault(id => id != excludedPlayfieldId
                    && !_instancesByPlayfield.ContainsKey(id) && !isLoaded(id));
                if (!slots.ReservedPlayfields.Contains(target) || target == excludedPlayfieldId
                    || _instancesByPlayfield.ContainsKey(target) || isLoaded(target))
                    throw new InvalidOperationException("Native client playfield slot is unavailable. Use another configured slot or restart the local server.");
                var builder = new AORebirth.DungeonGenerator.DungeonGenerator(gameDataRoot);
                var generated = builder.Generate(new AORebirth.DungeonGenerator.DungeonGenerationRequest {
                    StyleId = slots.AcgStyle, Seed = seed, FloorCount = 1, FloorSize = targetRooms, BuildingInstance = target
                });
                var collision = AORebirth.World.Collision.DungeonCollisionBuilder.BuildLayout(gameDataRoot, generated.Generator);
                if (!collision.Collision.HasCollision || collision.Placements.Count != generated.Rooms.Count)
                    throw new InvalidOperationException("Native layout collision is incomplete.");
                var spawn = NativeClientAcgLayout.SupportedSpawn(collision, generated.Spawn);
                var manifest = new GenerationManifest(NativeClientAcgLayout.GeneratorId, "1.0.0", 1,
                    (ulong)seed, instanceId, NativeClientAcgLayout.CatalogHash(gameDataRoot, slots.AcgStyle));
                manifest.Parameters["acgPayload"] = Convert.ToBase64String(generated.Generator.ToByteArray());
                manifest.Parameters["nativeClientPlayfield"] = target.ToString(CultureInfo.InvariantCulture);
                manifest.Parameters["nativeStyle"] = slots.AcgStyle.ToString(CultureInfo.InvariantCulture);
                manifest.Parameters["roomCount"] = generated.Rooms.Count.ToString(CultureInfo.InvariantCulture);
                manifest.Parameters["targetRoomCount"] = targetRooms.ToString(CultureInfo.InvariantCulture);
                var layout = new DungeonLayout(manifest, Array.Empty<DungeonRoom>(), Array.Empty<DungeonConnection>(),
                    Array.Empty<DungeonCorridor>(), Array.Empty<DungeonDoorPortal>(), new[] {
                        new DungeonSpawnPoint("native-acg-entrance", "native-acg", DungeonSpawnRole.PlayerEntrance,
                            new WorldGen.Spatial.WorldVector3((int)(spawn.X*1000), (int)(spawn.Y*1000), (int)(spawn.Z*1000)), 0)
                    });
                return RegisterCopy(target, layout, DungeonLayoutHasher.Compute(layout));
            }
        }

        private ProceduralInstance RegisterCopy(int playfieldId, DungeonLayout layout, string contentHash)
        {
            string instanceId = layout.Manifest.WorldId;
            var instance = new ProceduralInstance(playfieldId, layout, contentHash);
            if (!_instances.TryAdd(instanceId, instance))
                throw new InvalidOperationException("Instance already exists: " + instanceId);
            if (!_instancesByPlayfield.TryAdd(playfieldId, instance))
            { _instances.TryRemove(instanceId, out _); throw new InvalidOperationException("Playfield ID collision."); }
            return instance;
        }

        public ProceduralInstance GenerateNativeRooms(string instanceId, ulong seed, int count, string pool, int excludedPlayfieldId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("Instance ID is required.");
            lock (_nativeSync)
            {
                if (_instances.ContainsKey(instanceId) || NativeRoomArchive.Exists(instanceId))
                    throw new InvalidOperationException("Instance already exists. Enter the saved dungeon or use a new name: " + instanceId);
                var catalog = NativeRoomPackage.Catalog(out string sha);
                var recipe = NativeRoomDungeon.Generate(catalog, seed, count, pool);
                var manifest = NativeRoomArchive.Manifest(instanceId, seed, count, pool, catalog, sha, recipe);
                var instance = RegisterNative(manifest, recipe, excludedPlayfieldId);
                try { NativeRoomArchive.Save(manifest, recipe); }
                catch { _instances.TryRemove(instanceId, out _); _instancesByPlayfield.TryRemove(instance.PlayfieldId, out _); throw; }
                return instance;
            }
        }

        private ProceduralInstance RegisterNative(GenerationManifest manifest, NativeRoomRecipe recipe, int excludedPlayfieldId)
        {
            int playfieldId;
            do { playfieldId = Interlocked.Increment(ref _nextPlayfieldId); }
            while (playfieldId == excludedPlayfieldId || _instancesByPlayfield.ContainsKey(playfieldId));
            var instance = new ProceduralInstance(playfieldId, NativeRoomDungeon.Layout(manifest, recipe), NativeRoomDungeon.Hash(manifest, recipe));
            if (!_instances.TryAdd(manifest.WorldId, instance)) throw new InvalidOperationException("Instance already exists: " + manifest.WorldId);
            if (!_instancesByPlayfield.TryAdd(playfieldId, instance))
            { _instances.TryRemove(manifest.WorldId, out _); throw new InvalidOperationException("Playfield ID collision."); }
            return instance;
        }

        public bool TryGet(string instanceId, out ProceduralInstance? instance)
        {
            if (_instances.TryGetValue(instanceId, out instance)) return true;
            lock (_nativeSync)
            {
                if (_instances.TryGetValue(instanceId, out instance)) return true;
                if (!NativeRoomArchive.TryLoad(instanceId, out var manifest, out var recipe)) return false;
                instance = RegisterNative(manifest!, recipe!, 0);
                return true;
            }
        }

        public bool TryGetByPlayfield(int playfieldId, out ProceduralInstance? instance) =>
            _instancesByPlayfield.TryGetValue(playfieldId, out instance);

        public bool Remove(string instanceId)
        {
            lock (_nativeSync)
            {
                if (!_instances.TryGetValue(instanceId, out var removed))
                { bool saved = NativeRoomArchive.Exists(instanceId); if (saved) NativeRoomArchive.Delete(instanceId); return saved; }
                if (removed.Layout.Manifest.GeneratorId == NativeRoomDungeon.GeneratorId) NativeRoomArchive.Delete(instanceId);
                _instances.TryRemove(instanceId, out _); _instancesByPlayfield.TryRemove(removed.PlayfieldId, out _);
                return true;
            }
        }

        public void SetReturnLocation(int characterId, int playfieldId, Vector3 position)
        {
            if (characterId <= 0 || playfieldId <= 0) throw new ArgumentOutOfRangeException();
            _returns[characterId] = new ProceduralReturnLocation(playfieldId, position);
        }

        public bool TryTakeReturnLocation(int characterId, out ProceduralReturnLocation location) =>
            _returns.TryRemove(characterId, out location!);
    }

    public sealed class ProceduralInstance
    {
        private readonly ConcurrentDictionary<string, DungeonDoorStateSnapshot> _doors = new();
        private readonly Dictionary<string, DungeonDoor> _doorDefinitions =
            new Dictionary<string, DungeonDoor>(StringComparer.Ordinal);
        private readonly object _doorSync = new object();
        private long _doorRevision;

        public ProceduralInstance(int playfieldId, DungeonLayout layout, string layoutHash)
        {
            if (playfieldId <= 0) throw new ArgumentOutOfRangeException(nameof(playfieldId));
            PlayfieldId = playfieldId;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            LayoutHash = layoutHash ?? throw new ArgumentNullException(nameof(layoutHash));
            foreach (DungeonDoor door in DungeonDoorFactory.Create(layout))
            {
                _doorDefinitions.Add(door.Id, door);
                _doors[door.Id] = new DungeonDoorStateSnapshot(
                    door.Id, DungeonDoorState.Open, 0);
            }
        }

        public int PlayfieldId { get; }
        public DungeonLayout Layout { get; }
        public string LayoutHash { get; }

        public ICollection<DungeonDoorStateSnapshot> Doors => _doors.Values;

        public DungeonDoorStateEnvelope CreateDoorSnapshotEnvelope() =>
            new DungeonDoorStateEnvelope(
                Layout.Manifest.WorldId,
                true,
                _doors.Values.OrderBy(door => door.DoorId, StringComparer.Ordinal).ToArray());

        public DungeonDoorStateEnvelope CreateDoorDeltaEnvelope(DungeonDoorStateSnapshot door) =>
            new DungeonDoorStateEnvelope(
                Layout.Manifest.WorldId,
                false,
                new[] { door ?? throw new ArgumentNullException(nameof(door)) });

        public bool TryGetDoor(string doorId, out DungeonDoorStateSnapshot? state) =>
            _doors.TryGetValue(doorId, out state);

        public bool TryGetDoorDefinition(string doorId, out DungeonDoor? door) =>
            _doorDefinitions.TryGetValue(doorId, out door);

        public bool TrySetDoor(string doorId, DungeonDoorState state,
            string requiredKeyId,
            out DungeonDoorStateSnapshot? snapshot)
        {
            lock (_doorSync)
            {
                snapshot = null;
                if (!_doors.ContainsKey(doorId)) return false;
                snapshot = new DungeonDoorStateSnapshot(
                    doorId, state, Interlocked.Increment(ref _doorRevision),
                    state == DungeonDoorState.Locked ? requiredKeyId : string.Empty);
                _doors[doorId] = snapshot;
                return true;
            }
        }

        public bool TryUseDoor(string doorId, long expectedRevision,
            Func<string, bool> hasRequiredKey,
            out DungeonDoorStateSnapshot? snapshot, out string denial)
        {
            snapshot = null;
            denial = string.Empty;
            lock (_doorSync)
            {
                if (!_doors.TryGetValue(doorId, out DungeonDoorStateSnapshot? current))
                { denial = "Unknown door."; return false; }
                if (current.Revision != expectedRevision)
                { denial = "Door state changed; try again."; return false; }
                if (current.State == DungeonDoorState.Locked)
                {
                    if (string.IsNullOrWhiteSpace(current.RequiredKeyId))
                    { denial = "The door is locked and has no configured key."; return false; }
                    if (hasRequiredKey == null || !hasRequiredKey(current.RequiredKeyId))
                    { denial = "The door is locked. Required key item: " + current.RequiredKeyId + "."; return false; }
                    snapshot = new DungeonDoorStateSnapshot(
                        doorId, DungeonDoorState.Open,
                        Interlocked.Increment(ref _doorRevision), current.RequiredKeyId);
                    _doors[doorId] = snapshot;
                    return true;
                }
                if (current.State == DungeonDoorState.Sealed)
                { denial = "The door is sealed."; return false; }
                DungeonDoorState next = current.State == DungeonDoorState.Open
                    ? DungeonDoorState.Closed : DungeonDoorState.Open;
                snapshot = new DungeonDoorStateSnapshot(
                    doorId, next, Interlocked.Increment(ref _doorRevision), current.RequiredKeyId);
                _doors[doorId] = snapshot;
                return true;
            }
        }
    }

    public sealed class ProceduralReturnLocation
    {
        public ProceduralReturnLocation(int playfieldId, Vector3 position)
        { PlayfieldId = playfieldId; Position = position ?? throw new ArgumentNullException(nameof(position)); }
        public int PlayfieldId { get; }
        public Vector3 Position { get; }
    }
}
