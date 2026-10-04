using System;
using System.Collections.Generic;
using WorldGen.Contracts;
using WorldGen.Determinism;
using WorldGen.Dungeons;
using WorldGen.Identity;
using WorldGen.Spatial;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RandomSequenceIsRepeatable();
            NamedStreamsAreIndependent();
            RangesRemainValid();
            ManifestHashIsCanonical();
            StableIdsAreStableAndScoped();
            BoundsUseQuantizedCoordinates();
            DungeonGenerationIsDeterministicAndValid();
            SubwayProfileProducesRailSpineAndBranches();
            RaisedSubwayServiceRoomHasContinuousFloor();
            SubwayTrainChamberIsTraversableAndPlatformsAreSafe();
            SubwayCollisionBakeIsCompleteAndCanonical();
            SubwayStairSupportCoversRoomOneDescent();
            GroupDungeonHasWingsHallsAndLandmarks();
            CaveDungeonHasMeanderingReadableRoute();
            RoomArchetypesProduceSharedModularGeometry();
            DungeonSeedChangesLayout();
            VariedSubwaySeedsProduceDistinctValidTopologies();
            GrandHallUsesSeededLateSubwayBranches();
            TempleSeedsProduceDistinctSacredProgressions();
            Subway510RecoversOccupiedBranchCells();
            Subway511KeepsRailLineAndBranchDoorsWalkable();
            Subway512KeepsPedestrianDoorsOnRailSidesAndTrainAislesClear();
            TempleReferenceReconstructsPf1931ReviewGraph();
            ManifestEnvelopeRoundTrips();
            DoorStateSemanticsAreExplicit();
            DoorPortalsHaveIndependentDoors();
            DoorStateEnvelopeRoundTrips();
            RoomVisibilityRespectsDoorStateAndPrefetchesBlockedNeighbor();
            CorridorDirectionComesFromPortalsRatherThanBoundsShape();
            Console.WriteLine("PASS WorldGen.Core determinism and contracts");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL " + exception);
            return 1;
        }
    }

    private static void Subway511KeepsRailLineAndBranchDoorsWalkable()
    {
        var generator = new DungeonGenerator();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var manifest = new GenerationManifest("mission-dungeon", "5.11.0", 2,
                seed, "subway-511-" + seed, "development");
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = "Subway";
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] = "Subway";
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] = "Procedural";
            manifest.Parameters["roomCount"] = "30";
            DungeonLayout layout = generator.Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(30, DungeonLayoutProfile.Subway));
            True(DungeonValidator.Validate(layout).IsValid,
                "Subway 5.11 failed validation for seed " + seed);
            int tunnels = 0, junctions = 0;
            foreach (DungeonRoom room in layout.Rooms)
            {
                if (room.ModuleKind == DungeonModuleKind.TrackTunnel) tunnels++;
                if (room.ModuleKind == DungeonModuleKind.TrackJunction) junctions++;
            }
            Equal(2, tunnels, "Subway 5.11 should retain only the train-adjacent tunnels");
            Equal(0, junctions, "Subway 5.11 should have no optional track junction");
            foreach (DungeonConnection connection in layout.Connections)
            {
                DungeonRoom from = FindRoomForSubway(layout, connection.FromRoomId);
                DungeonRoom to = FindRoomForSubway(layout, connection.ToRoomId);
                if (from.ModuleKind != DungeonModuleKind.TrackTunnel
                    && to.ModuleKind != DungeonModuleKind.TrackTunnel) continue;
                DungeonModuleKind other = from.ModuleKind == DungeonModuleKind.TrackTunnel
                    ? to.ModuleKind : from.ModuleKind;
                True(other == DungeonModuleKind.TrainChamber
                    || other == DungeonModuleKind.StationConcourse
                    || other == DungeonModuleKind.Restroom,
                    "Track tunnel has an unsupported neighbor for seed " + seed);
            }
            if (seed != 1) continue;
            DungeonConnection branch = null;
            foreach (DungeonConnection connection in layout.Connections)
                if (FindRoomForSubway(layout, connection.FromRoomId).Index == 16
                    && FindRoomForSubway(layout, connection.ToRoomId).Index == 19)
                    branch = connection;
            True(branch != null, "Seed 1 lost Concourse 8 to 11 connection");
            foreach (DungeonCorridor corridor in layout.Corridors)
                if (corridor.ConnectionId == branch.Id)
                    Equal(4000, corridor.WalkableBounds.Maximum.Z
                        - corridor.WalkableBounds.Minimum.Z,
                        "Concourse 8 to 11 connector remains narrow");
            int portalCount = 0;
            foreach (DungeonDoorPortal portal in layout.DoorPortals)
                if (portal.ConnectionId == branch.Id)
                {
                    portalCount++;
                    Equal(3600, portal.ClosedBlockingBounds.Maximum.Z
                        - portal.ClosedBlockingBounds.Minimum.Z,
                        "Concourse 8 to 11 doorway remains narrow");
                }
            Equal(2, portalCount, "Concourse 8 to 11 lost a portal");
        }
    }

    private static void Subway512KeepsPedestrianDoorsOnRailSidesAndTrainAislesClear()
    {
        var generator = new DungeonGenerator();
        foreach (int roomCount in new[] { 24, 30 })
            for (ulong seed = 1; seed <= 1000; seed++)
            {
                var manifest = new GenerationManifest("mission-dungeon", "5.12.0", 2,
                    seed, "subway-512-" + roomCount + "-" + seed, "development");
                manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = "Subway";
                manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] = "Subway";
                manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] = "Procedural";
                manifest.Parameters["roomCount"] = roomCount.ToString();
                DungeonLayout layout = generator.Generate(manifest,
                    DungeonGenerationProfileCatalog.CreateParameters(roomCount,
                        DungeonLayoutProfile.Subway));
                True(DungeonValidator.Validate(layout).IsValid,
                    "Subway 5.12 invalid for seed " + seed);
                DungeonRoom train = null;
                foreach (DungeonRoom room in layout.Rooms)
                    if (room.ModuleKind == DungeonModuleKind.TrainChamber) train = room;
                True(train != null, "Subway 5.12 missing train chamber");
                bool alongX = train.Size.X > train.Size.Z;
                Equal(42000, Math.Min(train.Size.X, train.Size.Z),
                    "Subway 5.12 train chamber should use wide aisle geometry");
                WorldBounds car = DungeonRoomGeometry.SubwayTrainCarBounds(train, 1);
                WorldBounds platform = DungeonRoomGeometry.SubwayPlatformEdgeSegmentBounds(
                    train, true, true);
                int aisle = alongX ? platform.Minimum.Z - car.Maximum.Z
                    : platform.Minimum.X - car.Maximum.X;
                True(aisle >= 2200, "Train side aisle is too narrow for seed " + seed);
                foreach (DungeonConnection connection in layout.Connections)
                {
                    DungeonRoom from = FindRoomForSubway(layout, connection.FromRoomId);
                    DungeonRoom to = FindRoomForSubway(layout, connection.ToRoomId);
                    DungeonRoom rail = from.ModuleKind == DungeonModuleKind.TrackTunnel
                        || from.ModuleKind == DungeonModuleKind.TrainChamber ? from :
                        to.ModuleKind == DungeonModuleKind.TrackTunnel
                        || to.ModuleKind == DungeonModuleKind.TrainChamber ? to : null;
                    if (rail == null) continue;
                    DungeonRoom other = rail == from ? to : from;
                    if (other.ModuleKind == DungeonModuleKind.TrackTunnel
                        || other.ModuleKind == DungeonModuleKind.TrainChamber) continue;
                    True(alongX ? rail.Center.X == other.Center.X
                        : rail.Center.Z == other.Center.Z,
                        "Pedestrian room attaches to rail end for seed " + seed + " rooms " + roomCount + " link " + rail.Index + "->" + other.Index);
                }
            }
    }

    private static DungeonRoom FindRoomForSubway(DungeonLayout layout, string id)
    {
        foreach (DungeonRoom room in layout.Rooms)
            if (room.Id == id) return room;
        throw new InvalidOperationException("Missing Subway room " + id);
    }

    private static void Subway510RecoversOccupiedBranchCells()
    {
        var generator = new DungeonGenerator();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var manifest = new GenerationManifest("mission-dungeon", "5.10.0", 2,
                seed, "subway-510-" + seed, "development");
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                DungeonLayoutProfile.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonVisualTheme.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
                DungeonAssetSource.Procedural.ToString();
            manifest.Parameters["roomCount"] = "24";
            DungeonLayout layout = generator.Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(24,
                    DungeonLayoutProfile.Subway));
            True(DungeonValidator.Validate(layout).IsValid,
                "Subway 5.10 layout failed validation for seed " + seed);
            Equal(DungeonLayoutHasher.Compute(layout),
                DungeonLayoutHasher.Compute(generator.Generate(manifest,
                    DungeonGenerationProfileCatalog.CreateParameters(24,
                        DungeonLayoutProfile.Subway))),
                "Subway 5.10 layout changed between runs for seed " + seed);
        }
        var oldManifest = new GenerationManifest("mission-dungeon", "5.9.0", 2,
            1, "seed-sweep-Subway-1", "development");
        oldManifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = "Subway";
        oldManifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] = "Subway";
        oldManifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] = "Procedural";
        oldManifest.Parameters["roomCount"] = "30";
        Equal("a9ae06fedb10dd6e96d972d0c87e252fc4c07d3c99275b2de7f53943da67dd4a",
            DungeonLayoutHasher.Compute(generator.Generate(oldManifest,
                DungeonGenerationProfileCatalog.CreateParameters(30,
                    DungeonLayoutProfile.Subway))),
            "Subway 5.9 golden layout changed");
    }

    private static void TempleReferenceReconstructsPf1931ReviewGraph()
    {
        var manifest = new GenerationManifest("mission-dungeon", "5.9.0", 2,
            1931, "temple-reference", "development");
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.TempleReference.ToString();
        manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
            DungeonVisualTheme.Temple.ToString();
        manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
            DungeonAssetSource.Procedural.ToString();
        manifest.Parameters["roomCount"] = "30";
        DungeonLayout layout = new DungeonGenerator().Generate(manifest,
            DungeonGenerationProfileCatalog.CreateParameters(30,
                DungeonLayoutProfile.TempleReference));
        Equal(30, layout.Rooms.Count,
            "PF 1931 reference room count changed");
        Equal(32, layout.Connections.Count,
            "PF 1931 reference connection graph changed");
        True(layout.Rooms[7].DisplayName == "Entry Hall"
            && layout.Rooms[17].DisplayName == "Great Hall"
            && layout.Rooms[26].DisplayName == "Future Temple",
            "PF 1931 reference labels changed");
        foreach (DungeonConnection connection in layout.Connections)
        {
            DungeonRoom from = null, to = null;
            foreach (DungeonRoom room in layout.Rooms)
            {
                if (room.Id == connection.FromRoomId) from = room;
                if (room.Id == connection.ToRoomId) to = room;
            }
            True(from != null && to != null
                && (from.Center.X == to.Center.X || from.Center.Z == to.Center.Z),
                "Temple reference connection is not axis-aligned: " + connection.Id);
        }
        foreach (DungeonCorridor corridor in layout.Corridors)
        {
            DungeonConnection owner = null;
            foreach (DungeonConnection connection in layout.Connections)
                if (connection.Id == corridor.ConnectionId) owner = connection;
            True(owner != null, "Temple reference corridor lost its connection");
            foreach (DungeonRoom room in layout.Rooms)
            {
                if (room.Id == owner.FromRoomId || room.Id == owner.ToRoomId) continue;
                bool overlaps = corridor.WalkableBounds.Minimum.X < room.Bounds.Maximum.X
                    && corridor.WalkableBounds.Maximum.X > room.Bounds.Minimum.X
                    && corridor.WalkableBounds.Minimum.Y < room.Bounds.Maximum.Y
                    && corridor.WalkableBounds.Maximum.Y > room.Bounds.Minimum.Y
                    && corridor.WalkableBounds.Minimum.Z < room.Bounds.Maximum.Z
                    && corridor.WalkableBounds.Maximum.Z > room.Bounds.Minimum.Z;
                True(!overlaps,
                    "Temple reference corridor crosses unrelated room: "
                    + room.DisplayName);
            }
        }
        int[] throughRooms = { 4, 5, 16 };
        foreach (int roomIndex in throughRooms)
        {
            var facings = new HashSet<DungeonPortalFacing>();
            foreach (DungeonDoorPortal portal in layout.DoorPortals)
                if (portal.RoomId == layout.Rooms[roomIndex].Id)
                    facings.Add(portal.Facing);
            True(facings.Count >= 2,
                "Temple reference through-room has no usable exit: "
                + layout.Rooms[roomIndex].DisplayName);
        }
        bool cryptHallEntrance = false;
        foreach (DungeonDoorPortal portal in layout.DoorPortals)
            cryptHallEntrance |= portal.RoomId == layout.Rooms[27].Id;
        True(cryptHallEntrance, "Temple reference Crypt Hall has no entrance");
        var futureSplitFacings = new HashSet<DungeonPortalFacing>();
        foreach (DungeonDoorPortal portal in layout.DoorPortals)
            if (portal.RoomId == layout.Rooms[25].Id)
                futureSplitFacings.Add(portal.Facing);
        Equal(4, futureSplitFacings.Count,
            "Future Chambers I must expose entry plus all three split paths");
        int[] futureRouteRooms = { 1, 2, 3 };
        foreach (int roomIndex in futureRouteRooms)
        {
            int portalCount = 0;
            foreach (DungeonDoorPortal portal in layout.DoorPortals)
                if (portal.RoomId == layout.Rooms[roomIndex].Id) portalCount++;
            Equal(2, portalCount,
                "Future split route is missing an entrance or exit: "
                + layout.Rooms[roomIndex].DisplayName);
        }
        True(DungeonValidator.Validate(layout).IsValid,
            "PF 1931 reference reconstruction failed validation");
    }

    private static void TempleSeedsProduceDistinctSacredProgressions()
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 93001; seed <= 93030; seed++)
        {
            var manifest = new GenerationManifest("mission-dungeon", "5.9.0", 2,
                seed, "temple-variety-" + seed, "development");
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                DungeonLayoutProfile.Temple.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonVisualTheme.Temple.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
                DungeonAssetSource.Procedural.ToString();
            manifest.Parameters["roomCount"] = "24";
            DungeonLayout layout = new DungeonGenerator().Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(24,
                    DungeonLayoutProfile.Temple));
            True(DungeonValidator.Validate(layout).IsValid,
                "Temple layout failed validation for seed " + seed);
            True(layout.Rooms[0].ModuleKind == DungeonModuleKind.PilgrimEntry,
                "Temple must begin at the Pilgrim Entry");
            True(layout.Rooms[1].ModuleKind == DungeonModuleKind.ProcessionalHall,
                "Temple entry must feed a Processional Hall");
            int bossIndex = -1, objectiveIndex = -1, hubIndex = -1;
            bool hasGate = false, hasGallery = false, hasCrypt = false,
                hasChapel = false;
            foreach (DungeonRoom room in layout.Rooms)
            {
                if (room.ModuleKind == DungeonModuleKind.NaveHub) hubIndex = room.Index;
                if (room.ModuleKind == DungeonModuleKind.InnerGate) hasGate = true;
                if (room.ModuleKind == DungeonModuleKind.ExarchGallery) hasGallery = true;
                if (room.ModuleKind == DungeonModuleKind.HighSanctuary)
                    objectiveIndex = room.Index;
                if (room.ModuleKind == DungeonModuleKind.ProfaneSanctum)
                    bossIndex = room.Index;
                if (room.ModuleKind == DungeonModuleKind.SunkenCrypt) hasCrypt = true;
                if (room.ModuleKind == DungeonModuleKind.RitualChapel) hasChapel = true;
            }
            True(hubIndex > 1 && hasGate && hasGallery && hasCrypt && hasChapel,
                "Temple semantic landmarks are incomplete");
            True(objectiveIndex > hubIndex && bossIndex > objectiveIndex,
                "Temple sanctuary progression is out of order");
            signatures.Add(CriticalPathSignature(layout));
        }
        True(signatures.Count >= 5,
            "Temple seeds did not select all five substantial macro layouts");
    }

    private static void GrandHallUsesSeededLateSubwayBranches()
    {
        var hallIndices = new HashSet<int>();
        for (ulong seed = 92000; seed < 92032; seed++)
        {
            var manifest = new GenerationManifest("mission-dungeon", "5.9.0", 2,
                seed, "subway-landmarks-" + seed, "development");
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                DungeonLayoutProfile.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonVisualTheme.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
                DungeonAssetSource.Procedural.ToString();
            manifest.Parameters["roomCount"] = "30";
            DungeonLayout layout = new DungeonGenerator().Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(30,
                    DungeonLayoutProfile.Subway));
            DungeonRoom hall = null, train = null;
            foreach (DungeonRoom room in layout.Rooms)
            {
                if (room.ModuleKind == DungeonModuleKind.GrandHall) hall = room;
                if (room.ModuleKind == DungeonModuleKind.TrainChamber) train = room;
            }
            True(hall != null && train != null, "Subway landmark module is missing");
            True(hall.Index >= 20,
                "WorldGen 5.9 Grand Hall should be on a middle/late branch");
            foreach (DungeonConnection connection in layout.Connections)
                True(!((connection.FromRoomId == hall.Id && connection.ToRoomId == train.Id)
                    || (connection.FromRoomId == train.Id && connection.ToRoomId == hall.Id)),
                    "Grand Hall must not connect directly to the Train Chamber");
            hallIndices.Add(hall.Index);
        }
        True(hallIndices.Count >= 5,
            "Grand Hall placement should vary across middle/late subway branches");
    }

    private static void VariedSubwaySeedsProduceDistinctValidTopologies()
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var exampleSignatures = new HashSet<string>(StringComparer.Ordinal);
        var criticalSignatures = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 91000; seed < 91024; seed++)
        {
            var manifest = new GenerationManifest("mission-dungeon", "5.8.0", 2,
                seed, "subway-variety-" + seed, "development");
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                DungeonLayoutProfile.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonVisualTheme.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
                DungeonAssetSource.Procedural.ToString();
            manifest.Parameters["roomCount"] = "30";
            DungeonLayout layout = new DungeonGenerator().Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(30,
                    DungeonLayoutProfile.Subway));
            True(DungeonValidator.Validate(layout).IsValid,
                "Varied subway layout failed validation for seed " + seed);
            var signature = new System.Text.StringBuilder();
            foreach (DungeonRoom room in layout.Rooms)
                signature.Append(room.Center.X).Append(',').Append(room.Center.Z).Append(';');
            signatures.Add(signature.ToString());
            criticalSignatures.Add(CriticalPathSignature(layout));
            if (seed >= 91001 && seed <= 91003)
                exampleSignatures.Add(CriticalPathSignature(layout));
        }
        True(signatures.Count >= 6,
            "WorldGen 5.8 subway seeds did not select all substantial layout variants");
        True(criticalSignatures.Count >= 6,
            "WorldGen 5.8 subway seeds did not select all critical-path variants");
        Equal(3, exampleSignatures.Count,
            "Documented WorldGen 5.8 example seeds should produce distinct layouts");
    }

    private static string CriticalPathSignature(DungeonLayout layout)
    {
        var signature = new System.Text.StringBuilder();
        for (int i = 1; i < layout.Rooms.Count; i++)
        {
            DungeonConnection connection = layout.Connections[i - 1];
            if (!connection.IsCriticalPath) continue;
            DungeonRoom from = null, to = null;
            foreach (DungeonRoom room in layout.Rooms)
            {
                if (room.Id == connection.FromRoomId) from = room;
                if (room.Id == connection.ToRoomId) to = room;
            }
            signature.Append(to.Center.X - from.Center.X != 0 ? 'X' : 'Z');
        }
        return signature.ToString();
    }




    private static void DoorStateSemanticsAreExplicit()
    {
        var open = new DungeonDoorStateSnapshot("portal-1", DungeonDoorState.Open, 4);
        True(!open.BlocksPassage && open.CanToggleWithoutKey,
            "Open door should be traversable and toggleable");
        var locked = new DungeonDoorStateSnapshot(
            "portal-1", DungeonDoorState.Locked, 5, "key-alpha");
        True(locked.BlocksPassage && !locked.CanToggleWithoutKey,
            "Locked door should block unkeyed toggles");
        Equal("key-alpha", locked.RequiredKeyId, "Door key identity changed");
    }

    private static void DoorPortalsHaveIndependentDoors()
    {
        DungeonLayout layout = new DungeonGenerator().Generate(
            Manifest(), new DungeonGenerationParameters(12));
        var doors = DungeonDoorFactory.Create(layout);
        Equal(layout.DoorPortals.Count, doors.Count,
            "Every portal should produce one independently controlled door");
        foreach (DungeonDoor door in doors)
        {
            Equal(door.Portal.Id, door.Id, "Door identity is not portal-specific");
            Equal(door.ConnectionId, door.Portal.ConnectionId,
                "Door portal belongs to another connection");
        }
    }

    private static void DoorStateEnvelopeRoundTrips()
    {
        var states = new[]
        {
            new DungeonDoorStateSnapshot("door-a", DungeonDoorState.Open, 2),
            new DungeonDoorStateSnapshot("door-b", DungeonDoorState.Locked, 7, "key-b")
        };
        string wire = DungeonDoorStateEnvelopeCodec.Encode(
            new DungeonDoorStateEnvelope("instance-1", true, states));
        True(DungeonDoorStateEnvelopeCodec.TryDecode(wire,
            out DungeonDoorStateEnvelope decoded, out string error),
            "Door envelope did not decode: " + error);
        Equal("instance-1", decoded.WorldId, "Door envelope world changed");
        True(decoded.IsSnapshot, "Door envelope lost snapshot semantics");
        Equal(2, decoded.Doors.Count, "Door envelope state count changed");
        Equal("door-b", decoded.Doors[1].DoorId, "Door identity changed");
        Equal(7L, decoded.Doors[1].Revision, "Door revision changed");
        Equal("key-b", decoded.Doors[1].RequiredKeyId, "Door key changed");
        True(!DungeonDoorStateEnvelopeCodec.TryDecode("AOWD1:not-base64", out _, out _),
            "Malformed door envelope was accepted");
    }

    private static void RoomVisibilityRespectsDoorStateAndPrefetchesBlockedNeighbor()
    {
        DungeonLayout layout = new DungeonGenerator().Generate(
            Manifest(), new DungeonGenerationParameters(12));
        DungeonRoom current = layout.Rooms[0];
        Equal(current.Id, DungeonVisibilityResolver.FindContainingRoom(
            layout, current.Center).Id, "Room membership changed");

        DungeonLocation roomLocation = DungeonVisibilityResolver.FindContainingLocation(layout, current.Center);
        True(roomLocation.Kind == DungeonLocationKind.Room, "Room location kind changed");
        DungeonVisibilitySet allOpen = DungeonVisibilityResolver.Resolve(
            layout, roomLocation, new System.Collections.Generic.Dictionary<string, DungeonDoorStateSnapshot>());
        Equal(layout.Rooms.Count, allOpen.VisibleRoomIds.Count,
            "Open connected dungeon should be fully visible");

        DungeonConnection blocked = null;
        for (int i = 0; i < layout.Connections.Count; i++)
            if (layout.Connections[i].FromRoomId == current.Id
                || layout.Connections[i].ToRoomId == current.Id)
            { blocked = layout.Connections[i]; break; }
        True(blocked != null, "Current room has no connection");
        DungeonDoorPortal blockedPortal = null;
        for (int i = 0; i < layout.DoorPortals.Count; i++)
            if (layout.DoorPortals[i].ConnectionId == blocked.Id
                && layout.DoorPortals[i].RoomId == current.Id)
            { blockedPortal = layout.DoorPortals[i]; break; }
        True(blockedPortal != null, "Current room portal is missing");
        var states = new System.Collections.Generic.Dictionary<string, DungeonDoorStateSnapshot>
        {
            [blockedPortal.Id] = new DungeonDoorStateSnapshot(blockedPortal.Id, DungeonDoorState.Closed, 1)
        };
        DungeonVisibilitySet split = DungeonVisibilityResolver.Resolve(layout, roomLocation, states);
        True(!Contains(split.VisibleConnectionIds, blocked.Id),
            "Closed connection remained visible");
        True(Contains(split.PrefetchedConnectionIds, blocked.Id),
            "First blocked corridor was not prefetched");

        DungeonCorridor corridor = null;
        for (int i = 0; i < layout.Corridors.Count; i++)
            if (layout.Corridors[i].ConnectionId == blocked.Id) { corridor = layout.Corridors[i]; break; }
        True(corridor != null, "Blocked corridor is missing");
        var bothClosed = new System.Collections.Generic.Dictionary<string, DungeonDoorStateSnapshot>();
        for (int i = 0; i < layout.DoorPortals.Count; i++)
            if (layout.DoorPortals[i].ConnectionId == blocked.Id)
                bothClosed[layout.DoorPortals[i].Id] = new DungeonDoorStateSnapshot(
                    layout.DoorPortals[i].Id, DungeonDoorState.Closed, 1);
        DungeonLocation corridorLocation = DungeonVisibilityResolver.FindContainingLocation(
            layout, new WorldVector3(
                (corridor.WalkableBounds.Minimum.X + corridor.WalkableBounds.Maximum.X) / 2,
                (corridor.WalkableBounds.Minimum.Y + corridor.WalkableBounds.Maximum.Y) / 2,
                (corridor.WalkableBounds.Minimum.Z + corridor.WalkableBounds.Maximum.Z) / 2));
        True(corridorLocation.Kind == DungeonLocationKind.Corridor,
            "Corridor location was not detected");
        DungeonVisibilitySet enclosed = DungeonVisibilityResolver.Resolve(layout, corridorLocation, bothClosed);
        True(Contains(enclosed.VisibleConnectionIds, blocked.Id),
            "Occupied corridor disappeared when both endpoint doors closed");
        True(Contains(enclosed.PrefetchedRoomIds, blocked.FromRoomId)
             && Contains(enclosed.PrefetchedRoomIds, blocked.ToRoomId),
            "Both rooms adjoining an occupied corridor should remain prefetched");
    }

    private static bool Contains(System.Collections.Generic.IReadOnlyCollection<string> values,
        string expected)
    {
        foreach (string value in values)
            if (value == expected) return true;
        return false;
    }

    private static void CorridorDirectionComesFromPortalsRatherThanBoundsShape()
    {
        var manifest = new GenerationManifest(
            "mission-dungeon", "1.0.0", 1, 90124, "pooling-test", "development");
        DungeonLayout layout = new DungeonGenerator().Generate(
            manifest, new DungeonGenerationParameters(48));
        bool foundShortWideCorridor = false;
        for (int i = 0; i < layout.Corridors.Count; i++)
        {
            DungeonCorridor corridor = layout.Corridors[i];
            DungeonDoorPortal portal = null;
            for (int p = 0; p < layout.DoorPortals.Count; p++)
                if (layout.DoorPortals[p].ConnectionId == corridor.ConnectionId)
                { portal = layout.DoorPortals[p]; break; }
            True(portal != null, "Corridor portal missing");
            bool expectedAlongX = portal.Facing == DungeonPortalFacing.NegativeX
                                  || portal.Facing == DungeonPortalFacing.PositiveX;
            Equal(expectedAlongX, DungeonCorridorGeometry.IsAlongX(layout, corridor),
                "Corridor direction diverged from its portal facing");
            int sizeX = corridor.WalkableBounds.Maximum.X - corridor.WalkableBounds.Minimum.X;
            int sizeZ = corridor.WalkableBounds.Maximum.Z - corridor.WalkableBounds.Minimum.Z;
            if (expectedAlongX != (sizeX >= sizeZ)) foundShortWideCorridor = true;
        }
        True(foundShortWideCorridor,
            "Regression fixture no longer contains a corridor that defeats size-based direction inference");
    }

    private static void RaisedSubwayServiceRoomHasContinuousFloor()
    {
        var manifest = new GenerationManifest("mission-dungeon", "1.6.0", 2,
            90221, "subway-depth-test", "development");
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.Subway.ToString();
        manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
            DungeonVisualTheme.Subway.ToString();
        manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
            DungeonAssetSource.Procedural.ToString();
        manifest.Parameters["roomCount"] = "30";
        DungeonLayout layout = new DungeonGenerator().Generate(manifest,
            DungeonGenerationProfileCatalog.CreateParameters(30, DungeonLayoutProfile.Subway));
        DungeonRoom room = null;
        foreach (DungeonRoom candidate in layout.Rooms)
            if (candidate.ModuleKind == DungeonModuleKind.StationConcourse
                && candidate.Role == DungeonRoomRole.Branch)
            {
                room = candidate;
                break;
            }
        True(room != null, "Subway fixture should contain a clean branch circulation room");

        foreach (DungeonDoorPortal portal in layout.DoorPortals)
        {
            if (portal.RoomId != room.Id) continue;
            DungeonCorridor corridor = null;
            foreach (DungeonCorridor candidate in layout.Corridors)
                if (candidate.ConnectionId == portal.ConnectionId) corridor = candidate;
            True(corridor != null, "Room 28 corridor is missing");
            bool hasRoomFloor = false;
            foreach (WorldBounds section in DungeonRoomGeometry.FloorSections(room))
                if (section.Minimum.Y == portal.Center.Y
                    && section.Minimum.X <= portal.Center.X && section.Maximum.X >= portal.Center.X
                    && section.Minimum.Z <= portal.Center.Z && section.Maximum.Z >= portal.Center.Z)
                    hasRoomFloor = true;
            bool hasCorridorFloor = false;
            foreach (WorldBounds section in DungeonCorridorGeometry.FloorSections(layout, corridor))
                if (section.Minimum.Y == portal.Center.Y
                    && section.Minimum.X <= portal.Center.X && section.Maximum.X >= portal.Center.X
                    && section.Minimum.Z <= portal.Center.Z && section.Maximum.Z >= portal.Center.Z)
                    hasCorridorFloor = true;
            True(hasRoomFloor && hasCorridorFloor,
                "Room 28 portal has a floor gap or elevation discontinuity");
        }
    }

    private static void SubwayTrainChamberIsTraversableAndPlatformsAreSafe()
    {
        var manifest = new GenerationManifest("mission-dungeon", "1.8.0", 2,
            90221, "subway-depth-test", "development");
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.Subway.ToString();
        DungeonLayout layout = new DungeonGenerator().Generate(manifest,
            DungeonGenerationProfileCatalog.CreateParameters(30, DungeonLayoutProfile.Subway));
        DungeonRoom room = layout.Rooms[1];
        True(room.ModuleKind == DungeonModuleKind.StationConcourse,
            "Room 1 clean circulation fixture changed");
        True(!DungeonRoomGeometry.IsSubwayTrackRoom(room)
             && DungeonRoomGeometry.FloorSections(room).Count == 1,
            "Ordinary station platforms must have a continuous safe floor");
        DungeonRoom train = null;
        foreach (DungeonRoom candidate in layout.Rooms)
            if (candidate.ModuleKind == DungeonModuleKind.TrainChamber) train = candidate;
        True(train != null, "Subway layout has no train chamber");
        True(Math.Max(train.Size.X, train.Size.Z) >= 44000
             && Math.Min(train.Size.X, train.Size.Z) >= 40000
             && train.Size.Y >= 10000,
            "Train chamber is not large enough for a train and two platforms");
        True(DungeonRoomGeometry.FloorSections(train).Count == 19,
            "Train chamber must have an actual recessed track floor");
        Equal(train.Bounds.Minimum.Y - 1200,
            DungeonRoomGeometry.SubwayTrackFloorY(train),
            "Station track bed needs a meaningful physical recess");
        foreach (WorldBounds section in DungeonRoomGeometry.FloorSections(train))
            if (section.Minimum.Y == DungeonRoomGeometry.SubwayTrackFloorY(train))
            {
                int alongLength = train.Size.X >= train.Size.Z
                    ? section.Maximum.X - section.Minimum.X
                    : section.Maximum.Z - section.Minimum.Z;
                if (alongLength != Math.Max(train.Size.X, train.Size.Z)) continue;
                int crossWidth = train.Size.X >= train.Size.Z
                    ? section.Maximum.Z - section.Minimum.Z
                    : section.Maximum.X - section.Minimum.X;
                Equal(DungeonRoomGeometry.SubwayTrackWidth, crossWidth,
                    "Station trench must match the rail connector width");
            }
        WorldBounds foundation = DungeonRoomGeometry.SubwayTrackFoundationBounds(train);
        Equal(train.Bounds.Minimum.X, foundation.Minimum.X,
            "Station foundation must cover the complete room width");
        Equal(train.Bounds.Maximum.X, foundation.Maximum.X,
            "Station foundation must cover the complete room width");
        Equal(train.Bounds.Minimum.Z, foundation.Minimum.Z,
            "Station foundation must cover the complete room depth");
        Equal(train.Bounds.Maximum.Z, foundation.Maximum.Z,
            "Station foundation must cover the complete room depth");
        Equal(DungeonRoomGeometry.SubwayTrackFloorY(train), foundation.Maximum.Y,
            "Station foundation top must meet the bottom of the track trench");
        True(foundation.Maximum.Y - foundation.Minimum.Y >= 2000,
            "Station foundation must be thick enough to catch high-speed falls");
        IReadOnlyList<WorldBounds> accessSteps =
            DungeonRoomGeometry.SubwayTrackAccessSteps(train);
        Equal(12, accessSteps.Count,
            "Both platforms need six broad track-access steps");
        for (int step = 0; step < accessSteps.Count; step++)
        {
            int alongWidth = train.Size.X >= train.Size.Z
                ? accessSteps[step].Maximum.X - accessSteps[step].Minimum.X
                : accessSteps[step].Maximum.Z - accessSteps[step].Minimum.Z;
            True(alongWidth >= 7000,
                "Track-access steps must be broad enough for group traversal");
        }
        foreach (int end in new[] { -1, 1 })
        {
            int along = (train.Size.X >= train.Size.Z ? train.Center.X : train.Center.Z)
                        + end * ((train.Size.X >= train.Size.Z ? train.Size.X : train.Size.Z) / 2 - 1500);
            bool railContinues = false;
            int railX = train.Size.X >= train.Size.Z ? along : train.Center.X;
            int railZ = train.Size.X >= train.Size.Z ? train.Center.Z : along;
            foreach (WorldBounds floor in DungeonRoomGeometry.FloorSections(train))
                if (floor.Minimum.Y == DungeonRoomGeometry.SubwayTrackFloorY(train)
                    && floor.Minimum.X <= railX && floor.Maximum.X >= railX
                    && floor.Minimum.Z <= railZ && floor.Maximum.Z >= railZ)
                    railContinues = true;
            True(railContinues,
                "Train rails must continue through both longitudinal openings");
            foreach (int side in new[] { -1, 1 })
            {
                int cross = (train.Size.X >= train.Size.Z ? train.Center.Z : train.Center.X)
                            + side * (train.Size.X >= train.Size.Z
                                ? train.Size.Z : train.Size.X) * 3 / 8;
                int x = train.Size.X >= train.Size.Z ? along : cross;
                int z = train.Size.X >= train.Size.Z ? cross : along;
                bool fullBridge = false;
                foreach (WorldBounds floor in DungeonRoomGeometry.FloorSections(train))
                    if (floor.Minimum.Y == train.Bounds.Minimum.Y
                        && floor.Minimum.X <= x && floor.Maximum.X >= x
                        && floor.Minimum.Z <= z && floor.Maximum.Z >= z)
                        fullBridge = true;
                True(fullBridge, "Train station platform does not reach the tunnel end");
            }
        }
        WorldBounds edge = DungeonRoomGeometry.SubwayPlatformEdgeBounds(train);
        WorldBounds outer = DungeonRoomGeometry.SubwayPlatformEdgeBounds(train, true);
        True(edge.Minimum.Y < train.Bounds.Minimum.Y
             && edge.Maximum.Y < train.Bounds.Minimum.Y
             && outer.Minimum.Y < train.Bounds.Minimum.Y,
            "Retaining walls must contain the trench without blocking track access");
        int connectedRooms = 0;
        bool sidePositive = false;
        bool sideNegative = false;
        foreach (DungeonDoorPortal portal in layout.DoorPortals)
        {
            if (portal.RoomId != train.Id) continue;
            connectedRooms++;
            bool alongX = portal.Facing == DungeonPortalFacing.NegativeX
                          || portal.Facing == DungeonPortalFacing.PositiveX;
            bool tracksAlongX = train.Size.X >= train.Size.Z;
            if (alongX != tracksAlongX)
            {
                sidePositive |= portal.Facing == DungeonPortalFacing.PositiveX
                                || portal.Facing == DungeonPortalFacing.PositiveZ;
                sideNegative |= portal.Facing == DungeonPortalFacing.NegativeX
                                || portal.Facing == DungeonPortalFacing.NegativeZ;
            }
            if (alongX == tracksAlongX)
            {
                int transversePortal = alongX ? portal.Center.Z : portal.Center.X;
                int transverseEdgeMax = alongX ? edge.Maximum.Z : edge.Maximum.X;
                True(transverseEdgeMax <= transversePortal - 400,
                    "Platform barrier intrudes into the through-route doorway");
                foreach (int offset in new[] { -1300, 0, 1300 })
                {
                    bool trackFloor = false;
                    foreach (WorldBounds floor in DungeonRoomGeometry.FloorSections(train))
                    {
                        int transverse = transversePortal + offset;
                        if (floor.Minimum.Y == DungeonRoomGeometry.SubwayTrackFloorY(train)
                            && (alongX
                                ? floor.Minimum.Z <= transverse && floor.Maximum.Z >= transverse
                                : floor.Minimum.X <= transverse && floor.Maximum.X >= transverse))
                            trackFloor = true;
                    }
                    True(trackFloor,
                        "Rail doorway must continue through the lowered track bed");
                }
            }
            else
            {
                int portalAxis = alongX ? portal.Center.X : portal.Center.Z;
                int edgeAxisMax = alongX ? edge.Maximum.X : edge.Maximum.Z;
                int edgeAxisMin = alongX ? edge.Minimum.X : edge.Minimum.Z;
                True(Math.Min(Math.Abs(portalAxis - edgeAxisMax),
                    Math.Abs(portalAxis - edgeAxisMin)) >= 400,
                    "Platform barrier intrudes into a side-room doorway");
            }
        }
        True(connectedRooms >= 3,
            "Train chamber needs two rail tunnels and at least one platform branch");
        True(sidePositive || sideNegative,
            "Train chamber should retain a platform-side service connection");
        for (int car = 0; car < 3; car++)
        {
            WorldBounds body = DungeonRoomGeometry.SubwayTrainCarBounds(train, car);
            True(body.Minimum.X > train.Bounds.Minimum.X
                 && body.Maximum.X < train.Bounds.Maximum.X
                 && body.Minimum.Z > train.Bounds.Minimum.Z
                 && body.Maximum.Z < train.Bounds.Maximum.Z,
                "Train car blocks the chamber boundary or doorway crossing");
            True(body.Minimum.Y > DungeonRoomGeometry.SubwayTrackFloorY(train),
                "Train body intersects the track floor");
            True(body.Minimum.Y - DungeonRoomGeometry.SubwayTrackFloorY(train) >= 650,
                "Train body needs visible clearance for its undercarriage and wheels");
        }
    }

    private static void SubwayStairSupportCoversRoomOneDescent()
    {
        var manifest = new GenerationManifest("mission-dungeon", "1.9.0", 2,
            90261, "subway-station-test", "development");
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.Subway.ToString();
        DungeonLayout layout = new DungeonGenerator().Generate(manifest,
            DungeonGenerationProfileCatalog.CreateParameters(30, DungeonLayoutProfile.Subway));
        DungeonRoom room1 = layout.Rooms[1];
        DungeonRoom room2 = layout.Rooms[2];
        DungeonConnection connection = null;
        foreach (DungeonConnection candidate in layout.Connections)
            if (candidate.FromRoomId == room1.Id && candidate.ToRoomId == room2.Id)
                connection = candidate;
        True(connection != null, "Room 1 descent connection is missing");
        DungeonCorridor corridor = null;
        foreach (DungeonCorridor candidate in layout.Corridors)
            if (candidate.ConnectionId == connection.Id) corridor = candidate;
        True(corridor != null && corridor.ChangesElevation,
            "Room 1 descent should use a stair corridor");
        var steps = DungeonCorridorGeometry.FloorSections(layout, corridor);
        True(steps.Count > 2, "Room 1 descent has no stair treads");
        foreach (WorldBounds step in steps)
        {
            int supportDepth = DungeonCorridorGeometry.FloorSupportDepth(corridor, step);
            Equal(corridor.WalkableBounds.Minimum.Y - 750,
                step.Minimum.Y - supportDepth,
                "A stair tread leaves a void beneath the room 1 descent");
        }
    }

    private static void SubwayCollisionBakeIsCompleteAndCanonical()
    {
        var manifest = new GenerationManifest("mission-dungeon", "5.6.0", 2,
            90554, "subway-collision-bake-test", "development");
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.Subway.ToString();
        DungeonLayout layout = new DungeonGenerator().Generate(manifest,
            DungeonGenerationProfileCatalog.CreateParameters(30, DungeonLayoutProfile.Subway));
        DungeonRoom train = null;
        foreach (DungeonRoom room in layout.Rooms)
            if (room.ModuleKind == DungeonModuleKind.TrainChamber) train = room;
        True(train != null, "Subway collision fixture has no train chamber");

        IReadOnlyList<DungeonCollisionBox> boxes =
            DungeonCollisionBaker.BakeRoom(layout, train);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int floors = 0, ceilings = 0, foundations = 0, retaining = 0, trains = 0;
        foreach (DungeonCollisionBox box in boxes)
        {
            True(ids.Add(box.Id), "Collision bake emitted a duplicate stable ID");
            True(box.Bounds.Maximum.X > box.Bounds.Minimum.X
                 && box.Bounds.Maximum.Y > box.Bounds.Minimum.Y
                 && box.Bounds.Maximum.Z > box.Bounds.Minimum.Z,
                "Collision bake emitted a degenerate box");
            if (box.Kind == DungeonCollisionKind.Floor) floors++;
            if (box.Kind == DungeonCollisionKind.Ceiling) ceilings++;
            if (box.Kind == DungeonCollisionKind.Foundation) foundations++;
            if (box.Kind == DungeonCollisionKind.RetainingWall) retaining++;
            if (box.Kind == DungeonCollisionKind.TrainBody) trains++;
        }
        Equal(DungeonRoomGeometry.FloorSections(train).Count, floors,
            "Collision bake omitted a station floor or access step");
        Equal(floors, ceilings,
            "Collision bake floor and ceiling sections diverged");
        Equal(1, foundations, "Collision bake needs one continuous station foundation");
        Equal(8, retaining,
            "Collision bake needs platform edges and two closed cheeks per staircase");
        Equal(3, trains, "Collision bake needs all stopped train bodies");
    }

    private static void GroupDungeonHasWingsHallsAndLandmarks()
    {
        var manifest = new GenerationManifest("mission-dungeon", "2.0.0", 2,
            90301, "keep-group-test", "development");
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.GroupDungeon.ToString();
        manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
            DungeonVisualTheme.Keep.ToString();
        var generator = new DungeonGenerator();
        DungeonGenerationParameters parameters = DungeonGenerationProfileCatalog.CreateParameters(
            24, DungeonLayoutProfile.GroupDungeon);
        DungeonLayout layout = generator.Generate(manifest, parameters);
        DungeonLayout repeated = generator.Generate(manifest, parameters);
        Equal(DungeonLayoutHasher.Compute(layout), DungeonLayoutHasher.Compute(repeated),
            "Group dungeon layout is not deterministic");
        True(DungeonValidator.Validate(layout).IsValid, "Group dungeon layout is invalid");
        int main = 0;
        int side = 0;
        int wideHalls = 0;
        int chambers = 0;
        foreach (DungeonConnection connection in layout.Connections)
            if (connection.IsCriticalPath) main++; else side++;
        foreach (DungeonRoom room in layout.Rooms)
        {
            if (room.Shape == DungeonRoomShape.WideHall
                || room.Shape == DungeonRoomShape.LongHall) wideHalls++;
            if (room.Shape == DungeonRoomShape.GrandChamber) chambers++;
            Equal(0, room.Bounds.Minimum.Y, "Group dungeon should be safe single-level geometry");
        }
        True(main >= 9 && side >= 8, "Group dungeon lacks a main route and optional wings");
        True(wideHalls >= 3 && chambers >= 2,
            "Group dungeon lacks varied halls and landmark chambers");
        DungeonRoom finale = null;
        foreach (DungeonRoom room in layout.Rooms)
            if (room.Role == DungeonRoomRole.Boss) finale = room;
        True(finale != null && finale.ModuleKind == DungeonModuleKind.BossArena
             && finale.Size.X >= 24000 && finale.Size.Z >= 24000,
            "Group dungeon finale chamber is missing or undersized");
        foreach (int count in new[] { 12, 24, 48 })
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sample = new GenerationManifest("mission-dungeon", "2.0.0", 2,
                    seed, "group-sample-" + count + "-" + seed, "development");
                sample.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                    DungeonLayoutProfile.GroupDungeon.ToString();
                DungeonLayout candidate = generator.Generate(sample,
                    DungeonGenerationProfileCatalog.CreateParameters(count,
                        DungeonLayoutProfile.GroupDungeon));
                True(DungeonValidator.Validate(candidate).IsValid,
                    "Group dungeon sample failed validation");
                foreach (DungeonDoorPortal portal in candidate.DoorPortals)
                {
                    DungeonRoom portalRoom = null;
                    foreach (DungeonRoom candidateRoom in candidate.Rooms)
                        if (candidateRoom.Id == portal.RoomId) portalRoom = candidateRoom;
                    True(portalRoom != null, "Group dungeon portal has no room");
                    bool coveredByWall = false;
                    foreach (DungeonRoomWallSection wall in DungeonRoomGeometry.WallSections(portalRoom))
                    {
                        if (!wall.AcceptsPortals || wall.Facing != portal.Facing) continue;
                        int cross = portal.Facing == DungeonPortalFacing.NegativeX
                                    || portal.Facing == DungeonPortalFacing.PositiveX
                            ? portal.Center.Z : portal.Center.X;
                        if (wall.Start <= cross && cross <= wall.End) coveredByWall = true;
                    }
                    True(coveredByWall,
                        "Group dungeon portal does not land on a valid wall opening");
                }
            }
    }

    private static void CaveDungeonHasMeanderingReadableRoute()
    {
        var generator = new DungeonGenerator();
        True(DungeonGenerationProfileCatalog.DefaultTheme(DungeonLayoutProfile.CaveDungeon)
             == DungeonVisualTheme.Cavern,
            "Cave dungeon should default to the cavern theme");
        for (int countIndex = 0; countIndex < 3; countIndex++)
        {
            int count = new[] { 12, 24, 48 }[countIndex];
            for (ulong sample = 1; sample <= 32; sample++)
            {
                ulong seed = sample <= 30 ? sample : sample == 31 ? 90331UL : 90341UL;
                var manifest = new GenerationManifest("mission-dungeon", "3.3.0", 2,
                    seed, "cave-sample-" + count + "-" + seed, "development");
                manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                    DungeonLayoutProfile.CaveDungeon.ToString();
                DungeonLayout layout = generator.Generate(manifest,
                    DungeonGenerationProfileCatalog.CreateParameters(count,
                        DungeonLayoutProfile.CaveDungeon));
                True(DungeonValidator.Validate(layout).IsValid,
                    "Cave dungeon sample failed validation");
                Equal(0, DungeonDoorFactory.Create(layout).Count,
                    "Cave dungeon passages should remain doorless");
                DungeonRoom entrance = layout.Rooms[0];
                True(DungeonCaveChamber.IsEntranceModule(layout, entrance),
                    "Cave entrance did not select the authored chamber module");
                Equal(9000, entrance.Size.Y,
                    "Authored cave chamber should have its taller vault clearance");
                WorldVector3[] chamberOutline = DungeonCaveChamber.Outline(entrance);
                Equal(24, DungeonCaveChamber.ControlPoints(entrance).Length,
                    "Authored cave chamber control points changed unexpectedly");
                Equal(96, chamberOutline.Length,
                    "Smoothed cave chamber outline changed unexpectedly");
                foreach (WorldVector3 point in chamberOutline)
                    True(point.X >= entrance.Bounds.Minimum.X && point.X <= entrance.Bounds.Maximum.X
                        && point.Z >= entrance.Bounds.Minimum.Z && point.Z <= entrance.Bounds.Maximum.Z,
                        "Authored chamber outline escaped its flat collision support");
                var chamberWalls = DungeonCaveChamber.WallSegments(entrance, layout.DoorPortals);
                True(chamberWalls.Count >= 24 && chamberWalls.Count < chamberOutline.Length,
                    "Authored chamber failed to leave a door mouth in its angled wall ring: "
                    + chamberWalls.Count + " seed=" + seed + " rooms=" + count);
                foreach (DungeonDoorPortal socket in layout.DoorPortals)
                {
                    if (socket.RoomId != entrance.Id) continue;
                    True(DungeonCaveSockets.Resolve(socket).Kind
                        == DungeonCaveSocketKind.Standard,
                        "Authored entrance advertised an unsupported opening height");
                    int requiredSocketRadius = DungeonCaveSockets.Resolve(socket).VisualWidth / 2;
                    foreach (DungeonCaveChamber.WallSegment wall in chamberWalls)
                    {
                        double dx = wall.End.X - wall.Start.X;
                        double dz = wall.End.Z - wall.Start.Z;
                        double lengthSquared = dx * dx + dz * dz;
                        if (lengthSquared < 1) continue;
                        double t = Math.Max(0, Math.Min(1,
                            ((socket.Center.X - wall.Start.X) * dx
                            + (socket.Center.Z - wall.Start.Z) * dz) / lengthSquared));
                        double gapX = wall.Start.X + dx * t - socket.Center.X;
                        double gapZ = wall.Start.Z + dz * t - socket.Center.Z;
                        True(gapX * gapX + gapZ * gapZ
                            >= requiredSocketRadius * requiredSocketRadius,
                            "Smoothed chamber wall intrudes into entrance door capsule clearance");
                    }
                }
                DungeonCorridor firstTunnel = layout.Corridors[0];
                True(DungeonCaveTunnel.IsFirstBend(layout, firstTunnel),
                    "Cave first corridor did not select the authored bend module");
                DungeonCaveTunnel.Section[] bend =
                    DungeonCaveTunnel.Sections(layout, firstTunnel);
                Equal(33, bend.Length, "Authored cave bend sample count changed");
                DungeonDoorPortal minimumSocket = null, maximumSocket = null;
                int minimumAxis = int.MaxValue, maximumAxis = int.MinValue;
                bool firstAlongX = DungeonCorridorGeometry.IsAlongX(layout, firstTunnel);
                foreach (DungeonDoorPortal socket in layout.DoorPortals)
                    if (socket.ConnectionId == firstTunnel.ConnectionId)
                    {
                        int axis = firstAlongX ? socket.Center.X : socket.Center.Z;
                        if (axis < minimumAxis) { minimumAxis = axis; minimumSocket = socket; }
                        if (axis > maximumAxis) { maximumAxis = axis; maximumSocket = socket; }
                    }
                Equal(DungeonCaveSockets.Resolve(minimumSocket).VisualWidth, bend[0].Width,
                    "Cave bend entrance does not match its chamber socket");
                Equal(DungeonCaveSockets.Resolve(maximumSocket).VisualWidth,
                    bend[bend.Length - 1].Width,
                    "Cave bend exit does not match its destination socket");
                bool bendsPositive = false, bendsNegative = false;
                bool bendAlongX = DungeonCorridorGeometry.IsAlongX(layout, firstTunnel);
                int crossMiddle = bendAlongX
                    ? (firstTunnel.WalkableBounds.Minimum.Z
                        + firstTunnel.WalkableBounds.Maximum.Z) / 2
                    : (firstTunnel.WalkableBounds.Minimum.X
                        + firstTunnel.WalkableBounds.Maximum.X) / 2;
                foreach (DungeonDoorPortal socket in layout.DoorPortals)
                    if (socket.ConnectionId == firstTunnel.ConnectionId)
                        Equal(crossMiddle, bendAlongX ? socket.Center.Z : socket.Center.X,
                            "First cave bend socket is offset from its doorway");
                for (int bendIndex = 0; bendIndex < bend.Length; bendIndex++)
                {
                    DungeonCaveTunnel.Section section = bend[bendIndex];
                    int cross = bendAlongX ? section.Center.Z : section.Center.X;
                    if (cross > crossMiddle + 300) bendsPositive = true;
                    if (cross < crossMiddle - 300) bendsNegative = true;
                    True(section.Width >= 3600, "Cave bend narrows below safe group clearance");
                    True(bendAlongX
                        ? section.Left.Z >= firstTunnel.WalkableBounds.Minimum.Z + 200
                          && section.Right.Z <= firstTunnel.WalkableBounds.Maximum.Z - 200
                        : section.Left.X >= firstTunnel.WalkableBounds.Minimum.X + 200
                          && section.Right.X <= firstTunnel.WalkableBounds.Maximum.X - 200,
                        "Cave bend escaped its fallback collision corridor");
                }
                True(bendsPositive && bendsNegative,
                    "Authored cave bend did not actually turn both ways");
                Equal(crossMiddle, bendAlongX ? bend[0].Center.Z : bend[0].Center.X,
                    "Cave bend entrance is not aligned with its doorway");
                Equal(crossMiddle, bendAlongX ? bend[bend.Length - 1].Center.Z
                    : bend[bend.Length - 1].Center.X,
                    "Cave bend exit is not aligned with its doorway");
                DungeonCorridor wideDescent = layout.Corridors[1];
                True(DungeonCaveTunnel.IsWideDescent(layout, wideDescent),
                    "Second cave connection should be the wide elevation module");
                True(wideDescent.ChangesElevation,
                    "Wide cave passage should connect different floor elevations");
                bool descentAlongX = DungeonCorridorGeometry.IsAlongX(layout, wideDescent);
                int descentLength = descentAlongX
                    ? wideDescent.WalkableBounds.Maximum.X - wideDescent.WalkableBounds.Minimum.X
                    : wideDescent.WalkableBounds.Maximum.Z - wideDescent.WalkableBounds.Minimum.Z;
                int descentWidth = descentAlongX
                    ? wideDescent.WalkableBounds.Maximum.Z - wideDescent.WalkableBounds.Minimum.Z
                    : wideDescent.WalkableBounds.Maximum.X - wideDescent.WalkableBounds.Minimum.X;
                True(descentLength >= 450000 && descentWidth >= 210000,
                    "Wide cave passage should accommodate group movement and combat");
                int wideDoors = 0;
                foreach (DungeonDoorPortal portal in layout.DoorPortals)
                    if (portal.ConnectionId == wideDescent.ConnectionId)
                    {
                        int opening = descentAlongX
                            ? portal.ClosedBlockingBounds.Maximum.Z - portal.ClosedBlockingBounds.Minimum.Z
                            : portal.ClosedBlockingBounds.Maximum.X - portal.ClosedBlockingBounds.Minimum.X;
                        Equal(DungeonCaveSockets.Resolve(portal).Width, opening,
                            "Wide passage endpoint does not match its selected opening");
                        wideDoors++;
                    }
                Equal(2, wideDoors, "Wide passage should have doors at both landings");
                DungeonCaveTunnel.Section[] descent =
                    DungeonCaveTunnel.Sections(layout, wideDescent);
                Equal(129, descent.Length, "Wide descent should have a smooth sampled curve");
                int expectedDrop = (seed & 1UL) != 0 ? 18000 : 3000;
                True(Math.Abs(descent[0].Center.Y - descent[descent.Length - 1].Center.Y)
                    == expectedDrop,
                    "Wide descent should connect both landing elevations");
                True(Math.Abs((descentAlongX ? descent[descent.Length / 2].Center.Z
                    : descent[descent.Length / 2].Center.X) -
                    (descentAlongX ? descent[0].Center.Z : descent[0].Center.X)) > 160000,
                    "Wide descent needs a visible bend, not a straight rectangular shaft");
                IReadOnlyList<WorldBounds> curvedFloor =
                    DungeonCorridorGeometry.FloorSections(layout, wideDescent);
                WorldBounds[] ledge = DungeonCaveTunnel.LedgeSections(layout, wideDescent);
                DungeonCaveTunnel.LedgeSegment[] ledgeSupports =
                    DungeonCaveTunnel.LedgeSegments(layout, wideDescent);
                Equal(128, curvedFloor.Count, "Curved floor needs local collision strips");
                Equal(256, ledge.Length,
                    "Raised ledge needs two overlapping solid supports per curve segment");
                const int ledgeLaneCount = 6;
                Equal(128 * ledgeLaneCount, ledgeSupports.Length,
                    "Raised ledge needs narrow collision lanes per curve segment");
                for (int lane = 0; lane < ledgeLaneCount; lane++)
                for (int segment = lane * 128 + 1; segment < (lane + 1) * 128; segment++)
                {
                    Equal(ledgeSupports[segment - 1].End.X,
                        ledgeSupports[segment].Start.X,
                        "Raised ledge collision centerline has an X seam");
                    Equal(ledgeSupports[segment - 1].End.Z,
                        ledgeSupports[segment].Start.Z,
                        "Raised ledge collision centerline has a Z seam");
                    True(Math.Abs(ledgeSupports[segment].FloorY
                        - ledgeSupports[segment - 1].FloorY) <= 240,
                        "Raised ledge collision step exceeds the safe rise");
                    True(ledgeSupports[segment].Width >= 0
                        && ledgeSupports[segment].Width <= 5000,
                        "Raised ledge support width escaped its curved cross-section");
                }
                bool fromAtMinimumAxis = descent[0].Center.Y == wideDescent.FromFloorY;
                int startLedgeStrip = fromAtMinimumAxis ? 0 : ledge.Length - 1;
                int startFloorStrip = fromAtMinimumAxis ? 0 : curvedFloor.Count - 1;
                True(ledge[startLedgeStrip].Minimum.Y
                    - curvedFloor[startFloorStrip].Minimum.Y < 300,
                    "Ledge should be accessible at the route entrance");
                int entryFloorStrip = fromAtMinimumAxis ? 3 : curvedFloor.Count - 4;
                int entrySupport = entryFloorStrip;
                True(ledgeSupports[entrySupport].FloorY
                    - curvedFloor[entryFloorStrip].Minimum.Y
                    < 200, "Ledge entry must stay level near the reported snag point");
                int maximumLedgeLift = 0;
                for (int strip = 0; strip < curvedFloor.Count; strip++)
                    maximumLedgeLift = Math.Max(maximumLedgeLift,
                        ledge[strip * 2].Minimum.Y - curvedFloor[strip].Minimum.Y);
                True(maximumLedgeLift >= 5800,
                    "Ledge should rise about 6 m above the main route");
                Equal(0, DungeonCaveTunnel.UpperRouteWidthAt(layout, wideDescent,
                    fromAtMinimumAxis ? 1.0 : 0.0),
                    "Upper route should merge before the far room socket");
                if (seed == 1 && countIndex == 1)
                {
                    var mirroredManifest = new GenerationManifest("mission-dungeon", "3.3.0", 2,
                        seed, "cave-mirror-test", "development");
                    mirroredManifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                        DungeonLayoutProfile.CaveDungeon.ToString();
                    mirroredManifest.Parameters[DungeonCaveTunnel.LedgeSideParameter] = "left";
                    DungeonLayout mirrored = generator.Generate(mirroredManifest,
                        DungeonGenerationProfileCatalog.CreateParameters(count,
                            DungeonLayoutProfile.CaveDungeon));
                    Equal(-DungeonCaveTunnel.LedgeCrossSign(layout, wideDescent),
                        DungeonCaveTunnel.LedgeCrossSign(mirrored, mirrored.Corridors[1]),
                        "Raised ledge mirror setting should swap sides");
                }
                int main = 0;
                int side = 0;
                int caveShapes = 0;
                int sectionedCaverns = 0;
                foreach (DungeonConnection connection in layout.Connections)
                    if (connection.IsCriticalPath) main++; else side++;
                foreach (DungeonRoom room in layout.Rooms)
                {
                    if (room.Shape == DungeonRoomShape.Cavern
                        || room.Shape == DungeonRoomShape.LShaped
                        || room.Shape == DungeonRoomShape.GrandChamber) caveShapes++;
                    if (room.Shape == DungeonRoomShape.Cavern)
                    {
                        var sections = DungeonRoomGeometry.FloorSections(room);
                        Equal(5, sections.Count, "Cavern should have a connected five-section floor");
                        int area = 0;
                        bool centerCovered = false;
                        foreach (WorldBounds section in sections)
                        {
                            area += (section.Maximum.X - section.Minimum.X)
                                * (section.Maximum.Z - section.Minimum.Z) / 1000;
                            if (section.Minimum.X <= room.Center.X && room.Center.X <= section.Maximum.X
                                && section.Minimum.Z <= room.Center.Z && room.Center.Z <= section.Maximum.Z)
                                centerCovered = true;
                        }
                        True(centerCovered, "Cavern spawn/route center has no floor");
                        True(area < (room.Size.X * room.Size.Z / 1000),
                            "Cavern footprint did not remove rectangular corners");
                        if (seed == 1 && countIndex == 1)
                            foreach (WorldBounds section in sections)
                            {
                                var surface = DungeonCaveSurface.BuildFloor(section, room.Index);
                                True(surface.Vertices.Length > 9 && surface.Triangles.Length > 24,
                                    "Walkable cave surface has insufficient tessellation");
                                foreach (WorldVector3 vertex in surface.Vertices)
                                    True(vertex.Y >= section.Minimum.Y
                                        && vertex.Y <= section.Minimum.Y + 45,
                                        "Walkable cave relief escaped its safe height range");
                            }
                        sectionedCaverns++;
                    }
                    if (room.Index < 2)
                        Equal(0, room.Bounds.Minimum.Y, "Cave approach should stay level");
                    if (room.Index == 2)
                        Equal((seed & 1UL) != 0 ? -18000 : -3000,
                            room.Bounds.Minimum.Y, "Cave route elevation should follow its variant");
                }
                True(main > 4 && side > 0,
                    "Cave dungeon lacks a readable through-route with optional wings");
                True(caveShapes >= 2, "Cave dungeon lacks irregular chamber silhouettes");
                True(sectionedCaverns > 0, "Cave sample lacks a non-rectangular walkable cavern");
                foreach (DungeonDoorPortal portal in layout.DoorPortals)
                {
                    DungeonRoom room = null;
                    foreach (DungeonRoom candidate in layout.Rooms)
                        if (candidate.Id == portal.RoomId) room = candidate;
                    True(room != null, "Cave portal has no room");
                    bool opensOnWall = false;
                    foreach (DungeonRoomWallSection wall in DungeonRoomGeometry.WallSections(room))
                    {
                        if (!wall.AcceptsPortals || wall.Facing != portal.Facing) continue;
                        int cross = portal.Facing == DungeonPortalFacing.NegativeX
                                    || portal.Facing == DungeonPortalFacing.PositiveX
                            ? portal.Center.Z : portal.Center.X;
                        int openingMinimum = portal.Facing == DungeonPortalFacing.NegativeX
                                             || portal.Facing == DungeonPortalFacing.PositiveX
                            ? portal.ClosedBlockingBounds.Minimum.Z
                            : portal.ClosedBlockingBounds.Minimum.X;
                        int openingMaximum = portal.Facing == DungeonPortalFacing.NegativeX
                                             || portal.Facing == DungeonPortalFacing.PositiveX
                            ? portal.ClosedBlockingBounds.Maximum.Z
                            : portal.ClosedBlockingBounds.Maximum.X;
                        if (wall.Start <= cross && cross <= wall.End
                            && wall.Start <= openingMinimum && openingMaximum <= wall.End)
                            opensOnWall = true;
                    }
                    True(opensOnWall, "Cave portal misses full wall aperture: room "
                        + room.Index + " shape=" + room.Shape + " facing=" + portal.Facing
                        + " seed=" + seed + " count=" + count);
                    DungeonCorridor corridor = null;
                    foreach (DungeonCorridor candidate in layout.Corridors)
                        if (candidate.ConnectionId == portal.ConnectionId) corridor = candidate;
                    True(corridor != null, "Cave portal has no connecting tunnel");
                    bool xFacing = portal.Facing == DungeonPortalFacing.NegativeX
                                   || portal.Facing == DungeonPortalFacing.PositiveX;
                    int crossMinimum = xFacing ? corridor.WalkableBounds.Minimum.Z
                        : corridor.WalkableBounds.Minimum.X;
                    int crossMaximum = xFacing ? corridor.WalkableBounds.Maximum.Z
                        : corridor.WalkableBounds.Maximum.X;
                    int center = xFacing ? portal.Center.Z : portal.Center.X;
                    True(crossMinimum <= center - 2100 && crossMaximum >= center + 2100,
                        "Offset cave tunnel does not cover the whole doorway width");
                }
            }
        }
    }

    private static void ManifestEnvelopeRoundTrips()
    {
        GenerationManifest manifest = Manifest();
        manifest.Parameters["roomCount"] = "12";
        manifest.FeatureFlags.Add("doors");
        manifest.ContentPacks["industrial"] = "sha256:test";
        manifest.PersistenceRevision = 7;
        const string hash = "40a40d35c0b2d20f80f14027f09d03a9ec4cf5130547442df3054bfea887583d";
        string wire = DungeonManifestEnvelopeCodec.Encode(new DungeonManifestEnvelope(manifest, hash));
        True(DungeonManifestEnvelopeCodec.TryDecode(wire, out DungeonManifestEnvelope decoded, out string error),
            "Envelope did not decode: " + error);
        Equal(hash, decoded.ExpectedLayoutHash, "Envelope layout hash changed");
        Equal(ManifestHasher.Compute(manifest), ManifestHasher.Compute(decoded.Manifest),
            "Envelope manifest changed during round trip");
        True(!DungeonManifestEnvelopeCodec.TryDecode("AOWG1:not-base64", out _, out _),
            "Malformed envelope was accepted");
    }

    private static void RandomSequenceIsRepeatable()
    {
        var a = new DeterministicRandom(123456789UL, 42UL);
        var b = new DeterministicRandom(123456789UL, 42UL);
        for (int i = 0; i < 1000; i++) Equal(a.NextUInt32(), b.NextUInt32(), "PRNG sequence diverged");
    }

    private static void NamedStreamsAreIndependent()
    {
        ulong layout = SeedDerivation.Derive(99, "layout");
        Equal(layout, SeedDerivation.Derive(99, "layout"), "Sub-seed was not repeatable");
        NotEqual(layout, SeedDerivation.Derive(99, "decoration"), "Named streams collided");
    }

    private static void RangesRemainValid()
    {
        var random = new DeterministicRandom(8);
        for (int i = 0; i < 10000; i++)
        {
            int value = random.NextInt(-4, 11);
            True(value >= -4 && value < 11, "Integer escaped requested range");
            float unit = random.NextFloat();
            True(unit >= 0f && unit < 1f, "Float escaped unit range");
        }
    }

    private static void ManifestHashIsCanonical()
    {
        GenerationManifest a = Manifest();
        a.Parameters["rooms"] = "12";
        a.Parameters["style"] = "industrial";
        GenerationManifest b = Manifest();
        b.Parameters["style"] = "industrial";
        b.Parameters["rooms"] = "12";
        Equal(ManifestHasher.Compute(a), ManifestHasher.Compute(b), "Map insertion order changed manifest hash");
        b.Parameters["rooms"] = "13";
        NotEqual(ManifestHasher.Compute(a), ManifestHasher.Compute(b), "Manifest change did not change hash");
    }

    private static GenerationManifest Manifest() =>
        new GenerationManifest("mission-dungeon", "1.0.0", 1, 123, "instance-1", "catalog-sha256");

    private static void StableIdsAreStableAndScoped()
    {
        string a = StableWorldId.Create("instance-1", "room-07", "door-02");
        Equal(a, StableWorldId.Create("instance-1", "room-07", "door-02"), "Stable ID changed");
        NotEqual(a, StableWorldId.Create("instance-2", "room-07", "door-02"), "Scope did not affect ID");
    }

    private static void BoundsUseQuantizedCoordinates()
    {
        var bounds = new WorldBounds(new WorldVector3(-10, 0, -10), new WorldVector3(10, 30, 10));
        True(bounds.Contains(new WorldVector3(0, 15, 0)), "Bounds rejected contained point");
        True(!bounds.Contains(new WorldVector3(11, 15, 0)), "Bounds accepted exterior point");
    }

    private static void DungeonGenerationIsDeterministicAndValid()
    {
        var generator = new DungeonGenerator();
        var parameters = new DungeonGenerationParameters(18);
        DungeonLayout first = generator.Generate(Manifest(), parameters);
        DungeonLayout second = generator.Generate(Manifest(), parameters);
        Equal(18, first.Rooms.Count, "Incorrect room count");
        Equal(17, first.Connections.Count, "Generated layout should begin as a connected tree");
        Equal(17, first.Corridors.Count, "Every connection should have one corridor");
        Equal(34, first.DoorPortals.Count, "Every connection should have two door portals");
        Equal(3, first.SpawnPoints.Count, "Dungeon should expose entrance, exit, and boss spawns");
        True(DungeonValidator.Validate(first).IsValid, "Generated dungeon failed validation");
        bool criticalAlongX = false;
        bool criticalAlongZ = false;
        for (int i = 0; i < first.Connections.Count; i++)
        {
            DungeonConnection connection = first.Connections[i];
            if (!connection.IsCriticalPath) continue;
            DungeonRoom from = null;
            DungeonRoom to = null;
            for (int room = 0; room < first.Rooms.Count; room++)
            {
                if (first.Rooms[room].Id == connection.FromRoomId) from = first.Rooms[room];
                if (first.Rooms[room].Id == connection.ToRoomId) to = first.Rooms[room];
            }
            criticalAlongX |= from.Center.X != to.Center.X;
            criticalAlongZ |= from.Center.Z != to.Center.Z;
        }
        True(criticalAlongX && criticalAlongZ,
            "Critical path should contain deterministic turns rather than one straight line");
        Equal(DungeonLayoutHasher.Compute(first), DungeonLayoutHasher.Compute(second),
            "Dungeon layout was not repeatable");
    }

    private static void DungeonSeedChangesLayout()
    {
        var generator = new DungeonGenerator();
        var parameters = new DungeonGenerationParameters(18);
        DungeonLayout first = generator.Generate(Manifest(), parameters);
        var otherManifest = new GenerationManifest("mission-dungeon", "1.0.0", 1, 124,
            "instance-1", "catalog-sha256");
        DungeonLayout second = generator.Generate(otherManifest, parameters);
        NotEqual(DungeonLayoutHasher.Compute(first), DungeonLayoutHasher.Compute(second),
            "Changing the seed did not change the dungeon layout hash");
    }

    private static void SubwayProfileProducesRailSpineAndBranches()
    {
        GenerationManifest manifest = Manifest();
        manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
            DungeonLayoutProfile.Subway.ToString();
        DungeonLayout layout = new DungeonGenerator().Generate(manifest,
            DungeonGenerationProfileCatalog.CreateParameters(24, DungeonLayoutProfile.Subway));
        bool criticalAlongX = false;
        bool criticalAlongZ = false;
        int criticalConnections = 0;
        int branchConnections = 0;
        int elevationTransitions = 0;
        bool hasStation = false;
        bool hasService = false;
        bool hasTrain = false;
        bool hasTerminal = false;
        bool hasTrackJunction = false;
        bool hasGrandHall = false;
        bool hasRestroom = false;
        bool hasUnapprovedModule = false;
        int trackTunnelCount = 0;
        bool trackTunnelHasSideExit = false;
        DungeonRoom trackRoom = null;
        DungeonRoom entranceRoom = layout.Rooms[0];
        True(entranceRoom.Shape == DungeonRoomShape.LShaped,
            "Subway entrance must use the authored L footprint");
        Equal(36000, entranceRoom.Size.X,
            "Subway entrance L should retain its long horizontal leg");
        Equal(36000, entranceRoom.Size.Z,
            "Subway entrance L should retain its long vertical leg");
        bool entranceRouteUsesEndCap = false;
        for (int portalIndex = 0; portalIndex < layout.DoorPortals.Count; portalIndex++)
            if (layout.DoorPortals[portalIndex].RoomId == entranceRoom.Id
                && layout.DoorPortals[portalIndex].Facing == DungeonPortalFacing.PositiveX)
                entranceRouteUsesEndCap = true;
        True(entranceRouteUsesEndCap,
            "Subway route must leave through the L room's centered end cap");
        IReadOnlyList<WorldBounds> entranceLegs =
            DungeonRoomGeometry.FloorSections(entranceRoom);
        Equal(2, entranceLegs.Count,
            "Authored subway entrance should contain exactly two L legs");
        Equal(12000, entranceLegs[0].Maximum.Z - entranceLegs[0].Minimum.Z,
            "Horizontal entrance leg width changed");
        Equal(12000, entranceLegs[1].Maximum.X - entranceLegs[1].Minimum.X,
            "Vertical entrance leg width changed");
        bool centeredPathEnd = false;
        bool centeredDoorEnd = false;
        foreach (DungeonRoomWallSection wall in
            DungeonRoomGeometry.WallSections(entranceRoom))
        {
            if (!wall.AcceptsPortals) continue;
            int midpoint = wall.Start + (wall.End - wall.Start) / 2;
            if (wall.Facing == DungeonPortalFacing.PositiveX)
                centeredPathEnd = midpoint == entranceRoom.Center.Z;
            if (wall.Facing == DungeonPortalFacing.PositiveZ)
                centeredDoorEnd = midpoint == entranceRoom.Center.X;
        }
        True(centeredPathEnd && centeredDoorEnd,
            "Both entrance L sockets must be centered on their leg end caps");
        for (int i = 0; i < layout.Rooms.Count; i++)
        {
            DungeonModuleKind generatedKind = layout.Rooms[i].ModuleKind;
            hasUnapprovedModule |= generatedKind != DungeonModuleKind.Entrance
                && generatedKind != DungeonModuleKind.StationConcourse
                && generatedKind != DungeonModuleKind.TrainChamber
                && generatedKind != DungeonModuleKind.TrackTunnel
                && generatedKind != DungeonModuleKind.TrackJunction
                && generatedKind != DungeonModuleKind.GrandHall
                && generatedKind != DungeonModuleKind.Restroom;
            hasStation |= layout.Rooms[i].ModuleKind == DungeonModuleKind.StationConcourse;
            hasService |= layout.Rooms[i].ModuleKind == DungeonModuleKind.ServiceRoom;
            hasTrain |= layout.Rooms[i].ModuleKind == DungeonModuleKind.TrainChamber;
            hasTerminal |= layout.Rooms[i].ModuleKind == DungeonModuleKind.Terminal;
            hasTrackJunction |= layout.Rooms[i].ModuleKind == DungeonModuleKind.TrackJunction;
            if (layout.Rooms[i].ModuleKind == DungeonModuleKind.GrandHall)
            {
                hasGrandHall = true;
                Equal(44000, layout.Rooms[i].Size.X,
                    "Subway grand hall width changed");
                Equal(40000, layout.Rooms[i].Size.Z,
                    "Subway grand hall depth changed");
                True(!DungeonRoomGeometry.IsSubwayTrackRoom(layout.Rooms[i]),
                    "Flat grand hall must not receive recessed track geometry");
            }
            hasRestroom |= layout.Rooms[i].ModuleKind == DungeonModuleKind.Restroom;
            if (layout.Rooms[i].ModuleKind == DungeonModuleKind.TrackTunnel)
            {
                trackTunnelCount++;
                int longitudinalPortals = 0;
                int sidePortals = 0;
                bool alongX = layout.Rooms[i].Size.X >= layout.Rooms[i].Size.Z;
                for (int portalIndex = 0; portalIndex < layout.DoorPortals.Count; portalIndex++)
                {
                    DungeonDoorPortal portal = layout.DoorPortals[portalIndex];
                    if (portal.RoomId != layout.Rooms[i].Id) continue;
                    bool portalAlongX = portal.Facing == DungeonPortalFacing.NegativeX
                        || portal.Facing == DungeonPortalFacing.PositiveX;
                    if (portalAlongX == alongX) longitudinalPortals++;
                    else sidePortals++;
                }
                True(longitudinalPortals >= 1 && longitudinalPortals <= 2,
                    "A track bay should have a train-facing rail opening and at most one continuation");
                if (layout.Rooms[i].Role != DungeonRoomRole.Branch)
                    True(sidePortals >= 1,
                        "Ordinary circulation must enter a station-adjacent track bay from a raised side platform");
                trackTunnelHasSideExit |= sidePortals > 0;
            }
            if (trackRoom == null && DungeonRoomGeometry.IsSubwayTrackRoom(layout.Rooms[i]))
                trackRoom = layout.Rooms[i];
        }
        True(hasTrackJunction, "Subway layout should include an authored rail junction");
        True(hasGrandHall, "Subway layout should include a flat grand hall module");
        True(hasRestroom, "Subway layout should include a restroom module");
        True(!hasUnapprovedModule,
            "Subway generation must be restricted to the approved room catalog");
        bool hasCoreWallStyle = false;
        bool hasCoreLight = false;
        bool hasBlockingRestroomFixture = false;
        for (int elementIndex = 0; elementIndex < layout.Presentation.Count; elementIndex++)
        {
            DungeonPresentationElement element = layout.Presentation[elementIndex];
            hasCoreWallStyle |= element.MaterialRole == DungeonMaterialRole.WayfindingBlue;
            hasCoreLight |= element.Kind == DungeonPresentationKind.Light;
            hasBlockingRestroomFixture |= element.Kind == DungeonPresentationKind.Fixture
                && element.BlocksMovement;
        }
        True(hasCoreWallStyle && hasCoreLight && hasBlockingRestroomFixture,
            "Core must publish subway style, lighting, and authoritative fixture records");
        True(layout.ConstructiveRecipes.Count > 0,
            "Core must publish constructive fixture recipes");
        for (int i = 0; i < layout.Connections.Count; i++)
        {
            DungeonConnection connection = layout.Connections[i];
            DungeonRoom from = null;
            DungeonRoom to = null;
            for (int room = 0; room < layout.Rooms.Count; room++)
            {
                if (layout.Rooms[room].Id == connection.FromRoomId) from = layout.Rooms[room];
                if (layout.Rooms[room].Id == connection.ToRoomId) to = layout.Rooms[room];
            }
            if (connection.IsCriticalPath)
            {
                criticalAlongX |= from.Center.X != to.Center.X;
                criticalAlongZ |= from.Center.Z != to.Center.Z;
                criticalConnections++;
            }
            else branchConnections++;
        }
        for (int roomIndex = 0; roomIndex < layout.Rooms.Count; roomIndex++)
        {
            DungeonRoom tunnel = layout.Rooms[roomIndex];
            if (tunnel.ModuleKind != DungeonModuleKind.TrackTunnel) continue;
            bool joinsTrain = false;
            bool joinsJunction = false;
            for (int connectionIndex = 0; connectionIndex < layout.Connections.Count;
                 connectionIndex++)
            {
                DungeonConnection connection = layout.Connections[connectionIndex];
                string otherId = connection.FromRoomId == tunnel.Id ? connection.ToRoomId
                    : connection.ToRoomId == tunnel.Id ? connection.FromRoomId : null;
                if (otherId == null) continue;
                for (int otherIndex = 0; otherIndex < layout.Rooms.Count; otherIndex++)
                    if (layout.Rooms[otherIndex].Id == otherId
                        && layout.Rooms[otherIndex].ModuleKind
                            == DungeonModuleKind.TrainChamber)
                    {
                        joinsTrain = true;
                        Equal(layout.Rooms[otherIndex].Bounds.Minimum.Y,
                            tunnel.Bounds.Minimum.Y,
                            "Track tunnel and train chamber elevations must match");
                    }
                    else if (layout.Rooms[otherIndex].Id == otherId
                        && layout.Rooms[otherIndex].ModuleKind
                            == DungeonModuleKind.TrackJunction)
                    {
                        joinsJunction = true;
                        Equal(layout.Rooms[otherIndex].Bounds.Minimum.Y,
                            DungeonRoomGeometry.SubwayTrackFloorY(tunnel),
                            "Junction floor and connected rail bed elevations must match");
                    }
            }
            True(joinsTrain || joinsJunction,
                "Track-tunnel modules must join the train chamber or rail junction");
        }
        for (int i = 0; i < layout.Corridors.Count; i++)
        {
            DungeonCorridor corridor = layout.Corridors[i];
            if (!corridor.ChangesElevation) continue;
            elevationTransitions++;
            var steps = DungeonCorridorGeometry.FloorSections(layout, corridor);
            True(steps.Count > 2, "A subway elevation transition should have traversable steps");
            for (int step = 1; step < steps.Count; step++)
                True(Math.Abs(steps[step].Minimum.Y - steps[step - 1].Minimum.Y) <= 240,
                    "Subway stair rise exceeded the shared traversal limit");
        }
        True(criticalConnections > 1 && branchConnections > 0,
            "Subway fixture should contain a critical rail spine and optional branches");
        True(criticalAlongX && criticalAlongZ,
            "Subway critical route should contain authored bends between line segments");
        True(elevationTransitions >= 3,
            "Subway route should descend through upper, station, and deep levels");
        True(hasStation && hasTrain,
            "Approved subway catalog should include clean square and train modules");
        True(!hasService && !hasTerminal,
            "Deprecated service and terminal room treatments must not be generated");
        True(trackTunnelCount >= 2,
            "Subway profile should contain repeatable track-tunnel rooms");
        True(trackTunnelHasSideExit,
            "At least one track-tunnel room should expose a generated side branch");
        True(trackRoom != null, "Subway profile should include a recessed track room");
        var trackSections = DungeonRoomGeometry.FloorSections(trackRoom);
        int expectedTrackSections = trackRoom.ModuleKind == DungeonModuleKind.TrackTunnel
            ? 17 : 19;
        True(trackSections.Count == expectedTrackSections,
            "Track room should contain its recessed bed, platforms, and access stairs");
        int lowestTrackFloor = trackRoom.Bounds.Minimum.Y;
        for (int i = 0; i < trackSections.Count; i++)
            lowestTrackFloor = Math.Min(lowestTrackFloor, trackSections[i].Minimum.Y);
        Equal(DungeonRoomGeometry.SubwayTrackFloorY(trackRoom), lowestTrackFloor,
            "Track trench did not reach the shared authored depth");
    }

    private static void RoomArchetypesProduceSharedModularGeometry()
    {
        DungeonLayout layout = new DungeonGenerator().Generate(
            Manifest(), new DungeonGenerationParameters(64));
        DungeonRoom lRoom = null;
        bool foundWide = false;
        bool foundLong = false;
        bool foundChamber = false;
        for (int i = 0; i < layout.Rooms.Count; i++)
        {
            DungeonRoom room = layout.Rooms[i];
            if (room.Shape == DungeonRoomShape.LShaped) lRoom = room;
            foundWide |= room.Shape == DungeonRoomShape.WideHall;
            foundLong |= room.Shape == DungeonRoomShape.LongHall;
            foundChamber |= room.Shape == DungeonRoomShape.GrandChamber;
        }
        True(lRoom != null && foundWide && foundLong && foundChamber,
            "Deterministic fixture did not exercise all modular room families");
        Equal(2, DungeonRoomGeometry.FloorSections(lRoom).Count,
            "L-shaped room should have two floor/ceiling sections");
        Equal(6, DungeonRoomGeometry.WallSections(lRoom).Count,
            "L-shaped room should include two interior notch walls");
    }

    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
    { if (!expected.Equals(actual)) throw new InvalidOperationException(message); }
    private static void NotEqual<T>(T left, T right, string message) where T : IEquatable<T>
    { if (left.Equals(right)) throw new InvalidOperationException(message); }
}
