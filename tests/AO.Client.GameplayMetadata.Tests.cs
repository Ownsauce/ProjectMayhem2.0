using System;
using System.Linq;
using AO.Assets.ResourceDatabase;
using AO.Unity.Assets;

// The resolver only uses Unity to report diagnostics; all resolution and RDB
// decoding below use the production implementation without a running editor.
namespace UnityEngine { public static class Debug { public static void LogWarning(object value) => Console.WriteLine(value); } }

// Uses the production metadata/icon readers against an installed database, without Unity or a server.
namespace AO.Unity.Assets
{
    public static class AOInstallConfiguration
    {
        public static string InstallPath;
        public static AOInstallValidation GetConfiguredInstall() => AOInstallLocator.Validate(InstallPath);
    }
}

internal static class GameplayMetadataTests
{
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }

    private static int Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        AOInstallConfiguration.InstallPath = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("PROJECTMAYHEM_AO_INSTALL");
        Require(AOInstallConfiguration.GetConfiguredInstall().IsValid, "Supply the AO installation folder or PROJECTMAYHEM_AO_INSTALL.");
        Require(AOItemObjectResolver.TryResolveNano(82834, out var nano), "Resolve uploaded Pronouncement of Greatness from nano records.");
        Require(nano.DBType == 1040005 && nano.Name == "Pronouncement of Greatness", "Nano identity must not resolve a crystal or item.");
        Require(nano.StatValues.Single(value => value.Stat == 54).RawValue == 7, "Running nano requires seven NCU.");
        int icon = nano.StatValues.Single(value => value.Stat == 79).RawValue;
        Require(icon == 296675 && AOItemIconResolver.TryReadIcon(icon, out var png)
            && png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Uploaded and running nano icon must be a loadable PNG payload.");
        Require(nano.SpellData.SelectMany(group => group.Items).Any(effect => effect.Stat == 164 && effect.Amount != 0),
            "Hover details contain Concealment effect data.");
        Require(nano.SpellData.SelectMany(group => group.Items).Any(effect => effect.SpellDescription == "MonsterShape"),
            "Hover details retain the morph effect.");
        Require(AOItemObjectResolver.TryResolveNano(287046, out var composite)
            && composite.StatValues.Any(value => value.Stat == 79 && value.RawValue > 0)
            && composite.SpellData.SelectMany(group => group.Items).Any(effect => effect.Stat > 0),
            "Composite Utility Expertise has icon and modifier metadata.");
        Require(composite.StatValues.Single(value => value.Stat == 54).RawValue == 4
            && composite.StatValues.Single(value => value.Stat == 30).RawValue == 0,
            "Composite Utility is a four-NCU buff with no explicit hostile targeting flags.");
        Require(AOItemObjectResolver.TryResolve(121567, 121567, 1, out var pistol)
            && pistol.AttackDefenseData.Attack.Any(skill => skill.Stat == 112 && skill.RawValue > 0), "Pistol attack skill needed to select one-handed ranged animations.");
        Require(AOItemObjectResolver.TryResolve(218404, 218404, 1, out var sword)
            && sword.AttackDefenseData.Attack.Any(skill => skill.Stat == 103 && skill.RawValue > 0), "Sword attack skill needed to select one-handed melee animations.");
        Require(AOItemObjectResolver.TryResolve(252158, 252158, 1, out var belt)
            && belt.SpellData.SelectMany(group => group.Items).Single(effect => effect.Stat == 181).Amount == 2,
            "Belt NCU contribution survives metadata hydration.");
        using var database = new AOResourceDatabase(AOInstallConfiguration.InstallPath);
        using var native = new global::ResourceDatabase();
        native.Initialize(AOInstallConfiguration.InstallPath);
        // Hashes independently verified with AODB 1.0.12's chunked-ground reader.
        // The runtime keeps its existing plugin and corrects its padded inflater.
        foreach (var expected in new[] {
            (Id: 3081, Width: 50u, Side: 9, Count: 49, Hash: "DFAE234F865F74C492427A102539D06AD6E5D67EF7EC39D5C0C13F778347686C"),
            (Id: 800, Width: 250u, Side: 65, Count: 16, Hash: "2E251CBB80B598195C457883A884436C44F3243DBDF0D674B17704407979F2D6"),
            (Id: 4582, Width: 500u, Side: 65, Count: 64, Hash: "E663B3D8AAE3084A29C9B8D1B3B9DCF7CA52002D555CC01E58D37E1C79546013") })
        {
            var terrain = native.Get<AODB.Common.RDBObjects.Tilemap>(expected.Id);
            Require(terrain.MapWidth == expected.Width && terrain.ChunkSize == expected.Side
                && terrain.Heightmap.Count == expected.Count, $"PF {expected.Id} retains native terrain dimensions.");
            using var stream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(stream);
            foreach (var heights in terrain.Heightmap)
            {
                Require(heights.GetLength(0) == expected.Side && heights.GetLength(1) == expected.Side,
                    "Terrain samples exclude unused inflater capacity.");
                for (int z = 0; z < expected.Side; z++)
                    for (int x = 0; x < expected.Side; x++) writer.Write(heights[x, z]);
            }
            Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())) == expected.Hash,
                $"Every PF {expected.Id} height sample matches the independent native decoder.");
            Require(terrain.TileMapDatas.Values.All(tiles => tiles.Count == (expected.Side - 1) * (expected.Side - 1)),
                "Native terrain texture grids exclude padding.");
        }
        Console.WriteLine("PASS native outdoor terrain samples and tile dimensions for PF 3081, 800, 4582");
        var shopPlacements = AO.Assets.Decoders.AOPlacedDynelDecoder.Decode(native.GetRaw(1000026, 1186));
        Require(shopPlacements.Count == 73 && shopPlacements.Count(p => p.IdentityType == 51035) == 63,
            "Fair Trade's separate native placement resource supplies 63 shops and ten other objects.");
        Require(shopPlacements[0].X == 217 && Math.Abs(shopPlacements[0].Y - 5.0097785f) < .00001f
            && Math.Abs(shopPlacements[0].HeadingY + .7071029f) < .00001f
            && Math.Abs(shopPlacements[0].HeadingW - .70711064f) < .00001f,
            "Native placed-object positions and quaternion order match the independent decoder.");
        foreach (var placed in shopPlacements)
        {
            var template = native.Get<AODB.Common.RDBObjects.ItemObject>(placed.TemplateId);
            Require(template.Stats.TryGetValue((AODB.Common.Enums.StatId)12, out uint mesh)
                && native.Get<AODB.Common.RDBObjects.RDBMesh>((int)mesh)?.SubMeshes?.Count > 0,
                $"Fair Trade object {template.Name} resolves actual installed geometry.");
        }
        Require(shopPlacements.Count(p => p.TemplateId == 43553) == 2
            && shopPlacements.Count(p => p.TemplateId == 116807) == 1,
            "Both surgery clinics and the bank are present in native placement data.");
        Console.WriteLine("PASS PF 1186 placed shops, bank, clinics, door transforms and installed meshes");
        if (args.Length > 1)
        {
            var fairTrade = AO.Assets.Conversion.AOIndoorVisualExtractor.Load(
                AOInstallConfiguration.GetConfiguredInstall(), 1186, args[1]);
            float floorY = float.PositiveInfinity;
            foreach (var room in fairTrade.Geometry.Rooms)
            {
                var floor = AO.Assets.Navigation.AOIndoorVisualFloorBuilder.Build(room);
                if (floor == null) continue;
                for (int i = 0; i < floor.Triangles.Length; i += 3)
                {
                    int a = floor.Triangles[i] * 3, b = floor.Triangles[i + 1] * 3, c = floor.Triangles[i + 2] * 3;
                    var v = floor.Vertices;
                    float denominator = (v[b + 2] - v[c + 2]) * (v[a] - v[c]) + (v[c] - v[b]) * (v[a + 2] - v[c + 2]);
                    if (Math.Abs(denominator) < .000001f) continue;
                    float u = ((v[b + 2] - v[c + 2]) * (175.001f - v[c]) + (v[c] - v[b]) * (113.015f - v[c + 2])) / denominator;
                    float w = ((v[c + 2] - v[a + 2]) * (175.001f - v[c]) + (v[a] - v[c]) * (113.015f - v[c + 2])) / denominator;
                    if (u < -.00001f || w < -.00001f || u + w > 1.00001f) continue;
                    floorY = Math.Min(floorY, u * v[a + 1] + w * v[b + 1] + (1 - u - w) * v[c + 1]);
                }
            }
            Require(floorY >= 4.5f && floorY < 6, "PF 1186 has interior floor support at its authoritative entry, below the roof.");
            Console.WriteLine($"PASS PF 1186 native entrance floor at AO (175.001, {floorY:F3}, 113.015)");
        }
        Require(MonsterDataResolver.TryResolveBodyCatMeshId(native, 17655, out int leetMesh),
            "Captured Pronouncement MonsterData resolves to an installed body mesh.");
        Require(native.Get<AODB.Common.RDBObjects.RDBCatMesh>(AODB.Common.RDBObjects.ResourceTypeId.CatMesh, leetMesh) != null,
            "Leet morph uses a decodable CAT body.");
        Require(MonsterDataResolver.TryGetAnimIds(native, 17655, 0, out var leetAnimations) && leetAnimations.Count > 0,
            "Leet morph has installed animations for locomotion and combat.");
        foreach (int model in new[] { 17655, 30365, 30252, 22794 })
        {
            var creature = new CatAnimResolver(native);
            foreach (string action in new[] { "attack-hit", "attack-idle", "death" })
            {
                Require(creature.TryResolve(model, 0, action, out int id, out string name), $"Creature {model} resolves {action}.");
                Require(!name.StartsWith("male_") && !name.StartsWith("female_"), "creature attacks never use humanoid skeleton clips");
                Require(native.Get<AODB.Common.RDBObjects.CATAnim>(AODB.Common.RDBObjects.ResourceTypeId.Anim, id)?.Animation.BoneData?.Count > 0,
                    "creature attack/death clip has bone tracks");
            }
        }
        var names = AOResourceCatalog.Load(database).GetResources(AOResourceTypes.Animation).Values;
        foreach (var sample in new[] { (Skill: 112, Dual: true), (Skill: 103, Dual: false), (Skill: 105, Dual: false), (Skill: 116, Dual: false), (Skill: 113, Dual: false) })
        {
            int set = AOCombatAnimationCatalog.GetWeaponSet(sample.Skill, sample.Dual);
            foreach (string action in new[] { "attack-idle", "attack-hit" })
                Require(AOCombatAnimationCatalog.GetTokens(action, set).Any(token => names.Any(name => name.StartsWith("male_", StringComparison.Ordinal) && name.Contains(token))),
                    $"Installed animation exists for weapon skill {sample.Skill}, action {action}.");
        }
        foreach (string profile in new[] { "male", "female", "athrox" })
        {
            var resolver = new CatAnimResolver(native, profile);
            foreach (int set in new[] { 1, 2, 3, 4, 5 })
                foreach (string action in new[] { "attack-start", "attack-idle", "attack-hit", "attack-hit-left" })
                {
                    Require(resolver.TryResolve(0, set, action, out int id, out string name),
                        $"Production resolver selects {profile}/{set}/{action}.");
                    var clip = native.Get<AODB.Common.RDBObjects.CATAnim>(AODB.Common.RDBObjects.ResourceTypeId.Anim, id);
                    Require(clip?.Animation.BoneData?.Count > 0, $"Resolved combat clip {name} has actual bone tracks.");
                    int meshId = profile == "female" ? 5934 : profile == "male" ? 5914 : 5900;
                    var body = native.Get<AODB.Common.RDBObjects.RDBCatMesh>(AODB.Common.RDBObjects.ResourceTypeId.CatMesh, meshId);
                    Require(clip.Animation.BoneData.Any(bone => bone.BoneId >= 0 && bone.BoneId < body.Joints.Count
                        && (bone.RotationKeys?.Count > 1 || bone.TranslationKeys?.Count > 1)),
                        $"Combat clip {name} contains animated tracks that bind to the actual character skeleton.");
                }
            foreach (string action in new[] { "nano-cast-start", "nano-cast-loop", "nano-cast-finish" })
                Require(resolver.TryResolve(0, 0, action, out _, out _), $"Production resolver accepts {profile}/{action}.");
        }
        foreach (string action in new[] { "special-burst", "special-fullauto", "attack-hit-left" })
            Require(AOCombatAnimationCatalog.GetTokens(action, 4).Any(token => names.Any(name => name.StartsWith("male_", StringComparison.Ordinal) && name.Contains(token))),
                $"Installed one-handed ranged animation exists for {action}.");
        Console.WriteLine("PASS installed nano icons/NCU/effects, belt capacity, weapon skills, and combat animation catalog");
        return 0;
    }
}
