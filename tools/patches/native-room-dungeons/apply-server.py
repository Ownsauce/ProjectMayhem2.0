from pathlib import Path
import shutil
root=Path('/home/cody/Coding/AORebirth/AORebirth/Server/ZoneEngine_New')
stage=Path('/home/cody/Coding/ProjectMayhem2.0/tools/patches/native-room-dungeons')
for name in ['NativeRoomPackage.cs', 'NativeRoomArchive.cs']:
 shutil.copyfile(stage/name,root/'Core/WorldGeneration'/name)
for name in ['pf127.rooms.json','pf127.rooms.collision']:
 shutil.copyfile(Path('/tmp/pf127-native-rooms')/name,root/'NativeCopies'/name)
p=root/'Core/WorldGeneration/ProceduralInstanceService.cs';s=p.read_text()
anchor='        public bool TryGet(string instanceId, out ProceduralInstance? instance) =>'
method='''        public ProceduralInstance GenerateNativeRooms(string instanceId, ulong seed, int count, string pool, int excludedPlayfieldId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("Instance ID is required.");
            var catalog = NativeRoomPackage.Catalog(out string sha);
            var recipe = NativeRoomDungeon.Generate(catalog, seed, count, pool);
            var manifest = new GenerationManifest(NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion, 1, seed, instanceId, sha);
            manifest.Parameters["nativePlayfield"] = catalog.SourcePlayfield.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters["sourceSha256"] = catalog.SourceSha256;
            manifest.Parameters["surfaceSha256"] = catalog.SurfaceSha256;
            manifest.Parameters["roomCount"] = count.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters["pool"] = pool;
            var layout = NativeRoomDungeon.Layout(manifest, recipe);
            int playfieldId;
            do { playfieldId = Interlocked.Increment(ref _nextPlayfieldId); }
            while (playfieldId == excludedPlayfieldId || _instancesByPlayfield.ContainsKey(playfieldId));
            var instance = new ProceduralInstance(playfieldId, layout, NativeRoomDungeon.Hash(manifest, recipe));
            if (!_instances.TryAdd(instanceId, instance)) throw new InvalidOperationException("Instance already exists: " + instanceId);
            if (!_instancesByPlayfield.TryAdd(playfieldId, instance))
            { _instances.TryRemove(instanceId, out _); throw new InvalidOperationException("Playfield ID collision."); }
            return instance;
        }

'''
if 'GenerateNativeRooms(' not in s:
 assert anchor in s;s=s.replace(anchor,method+anchor)
p.write_text(s)
p=root/'Core/WorldGeneration/ProceduralInstanceService.cs';s=p.read_text();s=s.replace('NativeRoomDungeon.GeneratorId, \"1.0.0\",', 'NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion,');p.write_text(s)
p=root/'Core/Commands/WorldGenCommand.cs';s=p.read_text();anchor='            if (context.Args.Length == 3 && string.Equals(context.Args[0], "pf127", StringComparison.OrdinalIgnoreCase))'
method='''            if (context.Args.Length >= 1 && string.Equals(context.Args[0], "pf127-rooms", StringComparison.OrdinalIgnoreCase))
            {
                if (context.Args.Length < 4 || context.Args.Length > 5
                    || !ulong.TryParse(context.Args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong nativeSeed)
                    || !int.TryParse(context.Args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nativeCount))
                { GmCommandFeedback.Send(context.Session, context.Player, "Usage: .worldgen pf127-rooms <seed> <4-24 rooms> <instance-id> [mixed|mall|depths]"); return; }
                string pool = context.Args.Length == 5 ? context.Args[4].ToLowerInvariant() : "mixed";
                var native = _instances.GenerateNativeRooms(context.Args[3], nativeSeed, nativeCount, pool,
                    context.Player.Playfield?.Identity.Instance ?? 0);
                GmCommandFeedback.Send(context.Session, context.Player,
                    $"Generated native rooms {context.Args[3]}: pf={native.PlayfieldId}, seed={nativeSeed}, rooms={nativeCount}, pool={pool}. Enter with .worldenter {context.Args[3]}");
                return;
            }
'''
if '"pf127-rooms"' not in s:
 assert anchor in s;s=s.replace(anchor,method+anchor)
p.write_text(s)
p=root/'Core/WorldSimulation/PlayfieldWorldSimulation.cs';s=p.read_text();anchor='            if (layout.Manifest.GeneratorId == "native-playfield-copy")'
method='''            if (layout.Manifest.GeneratorId == NativeRoomDungeon.GeneratorId)
            {
                var recipe = ZoneEngine_New.Core.WorldGeneration.NativeRoomPackage.Recipe(layout.Manifest);
                var roomTriangles = new List<Vec3>();
                foreach (var values in ZoneEngine_New.Core.WorldGeneration.NativeRoomPackage.Collision(recipe))
                    for (int i = 0; i < values.Length; i += 3) roomTriangles.Add(new Vec3(values[i], values[i+1], values[i+2]));
                var native = new PlayfieldWorldSimulation(playfieldId, null, new PlayfieldGeometryData(), destinations, gameData, logger);
                native._proceduralSurface = new ProceduralSurface(BuildTriangleMesh(roomTriangles));
                native._proceduralStaticCount = roomTriangles.Count / 3;
                logger.Info($"Native room dungeon bake playfield={playfieldId} rooms={recipe.Rooms.Count} roomTriangles={roomTriangles.Count/3}");
                return native;
            }

'''
if 'NativeRoomDungeon.GeneratorId' not in s:
 assert anchor in s;s=s.replace(anchor,method+anchor)
p.write_text(s)

# Apply the reviewed native service methods without replacing unrelated service behavior.
p=root/'Core/WorldGeneration/ProceduralInstanceService.cs';s=p.read_text();replacement=(stage/'ProceduralInstanceService.cs').read_text()
a=s.index('        public ProceduralInstance GenerateNativeRooms(');b=s.index('        public bool TryGetByPlayfield',a)
x=replacement.index('        public ProceduralInstance GenerateNativeRooms(');y=replacement.index('        public bool TryGetByPlayfield',x);s=s[:a]+replacement[x:y]+s[b:]
a=s.index('        public bool Remove(string instanceId)');b=s.index('        public void SetReturnLocation',a)
x=replacement.index('        public bool Remove(string instanceId)');y=replacement.index('        public void SetReturnLocation',x);s=s[:a]+replacement[x:y]+s[b:]
if 'private readonly object _nativeSync' not in s:s=s.replace('private int _nextPlayfieldId = 899999;', 'private int _nextPlayfieldId = 899999;\n        private readonly object _nativeSync = new();')
p.write_text(s)
