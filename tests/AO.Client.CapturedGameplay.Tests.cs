using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using AO.Client.Backends.AORebirth;
using AO.Client.World;

internal static partial class WorldDeltaTests
{
    private static void TestCapturedDeathAndCorpses()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var backend = new AORebirthBackend();
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("44", "Captured player", 1, 1, 1));
        var entities = (Dictionary<string, NearbyEntity>)typeof(AORebirthBackend).GetField("_worldEntities", flags)!.GetValue(backend)!;
        // These NPCs existed before capture started. An intentionally oversized HP
        // baseline proves the fatal flag wins even when the local HP estimate differs.
        foreach (int id in new[] { 1000512, 1000520, 1000535, 1000541 })
            entities[$"50000:{id}"] = new NearbyEntity(50000, id, NearbyEntityKind.Npc, "Island Reet", 1, 1000, 0, 4582, 0, 0, 0);
        var apply = typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!;
        var deltas = new List<WorldEntityDelta>();
        int corpses = 0, fatalHits = 0, stops = 0;
        string file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Combat", "raw-20261013-012807-pid816.tsv");
        foreach (string line in File.ReadLines(file).Skip(1))
        {
            var fields = line.Split('\t');
            if (fields[1] != "IN") continue;
            byte[] body = Convert.FromHexString(fields[4]);
            var packet = new AORebirthPacket(10, 0, body);
            if (AORebirthProtocol.TryReadCombatUpdate(packet, out var combat))
            {
                if (combat.Kind == CombatUpdateKind.Stopped && combat.AttackerInstance == 44) stops++;
                if (combat.TargetDied) fatalHits++;
            }
            if (int.Parse(fields[2]) == AORebirthProtocol.CorpseFullUpdate)
            {
                Require(AORebirthProtocol.TryReadWorldObject(packet, 4582, out var corpse), "captured corpse fully decoded");
                bool reet = corpse.LinkedIdentityInstance == 1000512 || corpse.LinkedIdentityInstance == 1000520;
                bool snake = corpse.LinkedIdentityInstance == 1000535;
                Require(corpse.HasPosition && corpse.PlayfieldId == 4582 && corpse.Name == (reet ? "Remains of Island Reet" : snake ? "Remains of Shore Snake" : "Remains of Surf Lizard")
                    && corpse.CorpseAppearance.IsCorpse && corpse.CorpseAppearance.IsDead
                    && corpse.CorpseAppearance.MonsterData == (reet ? 30365u : snake ? 30252u : 22794u) && corpse.LinkedIdentityType == 50000,
                    "corpse identity, position, source character and MonsterData match capture");
                for (int size = 0; size < body.Length; size++)
                    Require(!AORebirthProtocol.TryReadWorldObject(new AORebirthPacket(10, 0, body[..size]), 4582, out _),
                        "truncated corpse cannot produce phantom object");
                corpses++;
            }
            apply.Invoke(backend, new object[] { packet, deltas, 0, 0 });
            if (combat?.TargetDied == true)
                Require(entities[$"{combat.TargetType}:{combat.TargetInstance}"].IsDead,
                    "fatal hit immediately sets dead target despite mismatched local HP");
        }
        Require(corpses == 4 && fatalHits == 4 && stops == 4, "four complete captured kill lifecycles");
        Require(entities.Values.Count(e => e.IsCorpse) == 4 && entities.Values.All(e => e.IdentityType != 50000),
            "NPC despawns leave independently identified corpses in world snapshot");
        Require(deltas.Count(d => d.Kind == WorldEntityDeltaKind.Upsert && d.Entity.IsCorpse) == 4,
            "every captured corpse reaches Unity as a renderable upsert");
        var combatEvents = (IReadOnlyList<CombatUpdate>)typeof(AORebirthBackend).GetMethod("DrainCombatUpdates", flags)!.Invoke(backend, null)!;
        Require(combatEvents.Where(e => e.Kind == CombatUpdateKind.Stopped && e.AttackerInstance == 44)
            .Select(e => e.TargetInstance).SequenceEqual(new[] { 1000512, 1000520, 1000535, 1000541 }),
            "stop replies retain their previous target when a new target is selected");
        byte[] use = AORebirthProtocol.CreateWorldObjectUse(44, 51050, 2000012);
        Require(new BigEndianReader(use, 16 + 37).ReadInt32() == 51050
            && new BigEndianReader(use, 16 + 41).ReadInt32() == 2000012,
            "corpse open uses corpse identity, not dead NPC identity");
        byte[] take = AORebirthProtocol.CreateLootTake(44, 19, 7);
        Require(new BigEndianReader(take, 16 + 13).ReadInt32() == 107
            && new BigEndianReader(take, 16 + 17).ReadInt32() == (19 << 16 | 7)
            && new BigEndianReader(take, 16 + 21).ReadInt32() == 0x6f,
            "loot transfer preserves container handle and actual sparse slot; server chooses inventory destination");
        using var loot = new MemoryStream();
        WriteInt(loot, AORebirthProtocol.InventoryUpdate); WriteInt(loot, 50000); WriteInt(loot, 44); loot.WriteByte(1);
        WriteInt(loot, 21); WriteInt(loot, 3); WriteInt(loot, 2 * 0x3f1);
        WriteCharacterSlot(loot, 7, 1, 9001, 121567, 121567, 1);
        WriteInt(loot, 51050); WriteInt(loot, 2000012); WriteInt(loot, 19); WriteInt(loot, 1);
        apply.Invoke(backend, new object[] { new AORebirthPacket(10, 0, loot.ToArray()), deltas, 0, 0 });
        Require(backend.GetContainerInventorySnapshot(2000012)?.Entries.Single().Slot == 7
            && backend.GetInventorySnapshot() == null, "corpse contents are retained separately from player inventory");
        typeof(AORebirthBackend).GetField("<State>k__BackingField", flags)!.SetValue(backend, AO.Client.ClientConnectionState.InWorld);
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        using var sender = new System.Net.Sockets.TcpClient();
        sender.Connect(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        using var receiver = accept.GetAwaiter().GetResult();
        receiver.GetStream().ReadTimeout = 2000;
        typeof(AORebirthBackend).GetField("_stream", flags)!.SetValue(backend, sender.GetStream());
        backend.UseWorldObjectAsync(51050, 2000012).GetAwaiter().GetResult();
        byte[] useWire = new byte[64]; receiver.GetStream().ReadExactly(useWire);
        Require(useWire[16..61].SequenceEqual(use[16..]), "native corpse open goes through padded AO transport");
        backend.MoveItemAsync(new ItemLocation(ItemArea.Backpack, 7, 2000012), new ItemLocation(ItemArea.Inventory, 0)).GetAwaiter().GetResult();
        byte[] takeWire = new byte[44]; receiver.GetStream().ReadExactly(takeWire);
        Require(takeWire[16..41].SequenceEqual(take[16..]), "backend takes correct corpse slot through native loot protocol");
        Console.WriteLine("PASS captured fatal hits, four corpses, NPC despawns, native corpse open and loot addressing");
    }

    private static void TestRunningNanoRestore()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var backend = new AORebirthBackend();
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("18", "Returning player", 1, 1, 1));
        var apply = typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!;
        var drain = typeof(AORebirthBackend).GetMethod("DrainNanoEffectSnapshot", flags)!;
        var deltas = new List<WorldEntityDelta>();
        apply.Invoke(backend, new object[] { AppearanceFullUpdate(true, 45000), deltas, 0, 0 });
        var restored = (IReadOnlyList<NanoEffectUpdate>)drain.Invoke(backend, null)!;
        Require(restored.Count == 1 && restored[0].NanoId == 82834 && restored[0].DurationCentiseconds == 45000,
            "login restores running nano by program ID with server remaining time, not active instance ID");
        Require(drain.Invoke(backend, null) == null, "running nano snapshot delivered only once");
        var player = deltas.Last().Entity;
        Require(player.WithPosition(1, 2, 3).WithHealth(100, 50).ActiveNanos[0].NanoId == 82834,
            "world updates preserve full-update running nano metadata");
        apply.Invoke(backend, new object[] { AppearanceFullUpdate(false, 15000), deltas, 0, 0 });
        restored = (IReadOnlyList<NanoEffectUpdate>)drain.Invoke(backend, null)!;
        Require(restored[0].DurationCentiseconds == 15000, "relogin uses remaining duration instead of restarting full lifetime");
        apply.Invoke(backend, new object[] { AppearanceFullUpdate(false, 0), deltas, 0, 0 });
        restored = (IReadOnlyList<NanoEffectUpdate>)drain.Invoke(backend, null)!;
        Require(restored != null && restored.Count == 0, "empty authoritative snapshot clears stale NCU buffs");
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("99", "Another player", 1, 1, 1));
        apply.Invoke(backend, new object[] { AppearanceFullUpdate(false), deltas, 0, 0 });
        Require(drain.Invoke(backend, null) == null, "another character's running buffs do not overwrite caster NCU");
    }

    private static void TestCapturedGameplay()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Captures");
        Require(Directory.GetFiles(root, "*.tsv").Length == 4, "all four capture fixtures are deployed");
        foreach (string file in Directory.GetFiles(root, "*.tsv").OrderBy(p => p))
        {
            int hits = 0, misses = 0, outfits = 0, nanos = 0, casts = 0, locks = 0;
            bool latest = Path.GetFileName(file).StartsWith("raw-20261013");
            using var backend = new AORebirthBackend();
            typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
                new AO.Client.Characters.CharacterSummary("44", "Captured player", 1, 1, 1));
            var entities = (Dictionary<string, NearbyEntity>)typeof(AORebirthBackend)
                .GetField("_worldEntities", flags)!.GetValue(backend)!;
            entities["50000:44"] = new NearbyEntity(50000, 44, NearbyEntityKind.Player,
                "Captured player", 1, 100, 0, 4582, 0, 0, 0,
                equipmentAppearance: new CharacterAppearanceSnapshot(null, null, 31));
            // Recording began after login. Seed symbolic item identities in the
            // occupied slots required by the eight captured move acknowledgements.
            // This validates slot transitions without inventing captured item IDs.
            if (latest)
            {
                InventoryEntrySnapshot Item(int slot, int id) => new(slot, 101, id, 1, 1, 1, 1);
                typeof(AORebirthBackend).GetField("_characterState", flags)!.SetValue(backend,
                    new CharacterStateSnapshot(new[] { Item(19, 10019), Item(6, 10006), Item(8, 10008) },
                        new[] { 82834, 287046 }));
                typeof(AORebirthBackend).GetField("_inventory", flags)!.SetValue(backend,
                    new InventorySnapshot(30, 50000, 44, 0, new[] { Item(69, 10069), Item(70, 10070) }));
            }
            var apply = typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!;
            var deltas = new List<WorldEntityDelta>();
            foreach (string line in File.ReadLines(file).Skip(1))
            {
                string[] fields = line.Split('\t');
                byte[] body = Convert.FromHexString(fields[4]);
                var packet = new AORebirthPacket(10, 0, body);
                int owner = new BigEndianReader(body, 8).ReadInt32();
                int type = int.Parse(fields[2]);
                string label = $"{Path.GetFileName(file)} packet {fields[0]} type {type:X8}";
                if (fields[1] == "OUT")
                {
                    // Transport sequence/header fields differ; the actual native N3 bodies must match.
                    if (type == 0x51492120)
                    {
                        var r = new BigEndianReader(body, 13);
                        int targetType = r.ReadInt32(), target = r.ReadInt32(), stat = r.ReadInt32();
                        Require(AORebirthProtocol.CreateSpecialAttackRequest(owner, stat, targetType, target)[16..]
                            .SequenceEqual(body), label + " outbound special matches stock client");
                    }
                    if (type == 0x5e477770)
                    {
                        var r = new BigEndianReader(body, 13);
                        int action = r.ReadInt32(); r.ReadInt32();
                        int targetType = r.ReadInt32(), target = r.ReadInt32(); r.ReadInt32();
                        int nano = r.ReadInt32();
                        if (action == 0x13)
                            Require(AORebirthProtocol.CreateNanoCast(owner, nano,
                                target == 0 ? 50000 : targetType, target == 0 ? owner : target,
                                implicitSelfTarget: target == 0)[16..]
                                .SequenceEqual(body), label + " outbound cast matches stock client");
                        if (action == 0x41)
                            Require(AORebirthProtocol.CreateNanoCancel(owner, nano)[16..].SequenceEqual(body),
                                label + " outbound buff removal matches stock client");
                    }
                    continue;
                }
                if (type == 0x41624f0d)
                {
                    Require(AORebirthProtocol.TryReadAppearanceUpdate(packet, out _, out _, out _), label + " appearance");
                    outfits++;
                }
                if (type == 0x46002f16 || type == 0x754f1115 || type == 0x5c654b28
                    || type == 0x28494070 || type == 0x4a41203e)
                {
                    Require(AORebirthProtocol.TryReadCombatUpdate(packet, out var combat), label + " combat");
                    if (combat.Kind == CombatUpdateKind.Hit) hits++;
                    if (combat.Kind == CombatUpdateKind.Miss) misses++;
                }
                if (type == 0x25314d6d)
                {
                    Require(AORebirthProtocol.TryReadNanoCastUpdate(packet, out _), label + " cast");
                    if (latest)
                        for (int length = 0; length < body.Length; length++)
                            Require(!AORebirthProtocol.TryReadNanoCastUpdate(new AORebirthPacket(10, 0, body[..length]), out _),
                                label + " truncated cast rejected");
                    casts++;
                }
                if (type == 0x39343c68)
                {
                    // Older live captures also contain effect references keyed by a
                    // character instance. They are not NCU NanoProgram removals.
                    if (new BigEndianReader(body, 15).ReadInt32() != 53019) continue;
                    Require(AORebirthProtocol.TryReadNanoEffect(packet, owner, out var effect)
                        && effect.DurationCentiseconds == 0, label + " buff removal");
                    nanos++;
                }
                if (type == 0x5e477770)
                {
                    int action = new BigEndianReader(body, 13).ReadInt32();
                    if (action == 0x62)
                    {
                        Require(AORebirthProtocol.TryReadNanoEffect(packet, owner, out _), label + " buff application");
                        nanos++;
                    }
                    if (action == 0x6b || action == 0x6c)
                        Require(AORebirthProtocol.TryReadNanoCastUpdate(packet, out _), label + " cast completion");
                    if (action == 0xaa || action == 0xa4)
                    {
                        Require(AORebirthProtocol.TryReadSpecialCooldown(packet, owner, out _), label + " special cooldown");
                        locks++;
                    }
                }
                if (latest) apply.Invoke(backend, new object[] { packet, deltas, 0, 0 });
            }
            if (latest)
            {
                Require(hits == 12 && misses == 3 && outfits == 8 && nanos == 5 && casts == 3 && locks == 4,
                    "capture contains ranged/melee combat, appearance changes, buffs, casts and cooldowns");
                var player = entities["50000:44"];
                Require(player.IsMorphedPlayer && player.MonsterData == 17655,
                    "captured Pronouncement of Greatness stat changes caster into leet");
                var state = backend.GetCharacterStateSnapshot();
                Require(state.EquipmentRevision == 8 && Find(state.Slots, 6).IdentityInstance == 10008
                    && Find(state.Slots, 8).IdentityInstance == 10006 && Find(state.Slots, 19).IdentityInstance == 10019,
                    "eight captured equip/unequip/swap acknowledgements survive intervening stats");
                Require(deltas.Any(d => d.Kind == WorldEntityDeltaKind.Appearance && d.Entity.IsMorphedPlayer),
                    "morph delivered to renderer as appearance delta");
                var deliveredCasts = (IReadOnlyList<NanoCastUpdate>)typeof(AORebirthBackend)
                    .GetMethod("DrainNanoCasts", flags)!.Invoke(backend, null)!;
                Require(deliveredCasts.Count == 6 && deliveredCasts.Count(c => c.Kind == NanoCastUpdateKind.Finished) == 3,
                    "cast lifecycle is queued for Unity delivery");
                var effects = (IReadOnlyList<NanoEffectUpdate>)typeof(AORebirthBackend)
                    .GetMethod("DrainNanoEffects", flags)!.Invoke(backend, null)!;
                Require(effects.Count == 5 && effects.Any(e => e.NanoId == 82834 && e.DurationCentiseconds == 1440000)
                    && effects.Any(e => e.NanoId == 287046 && e.DurationCentiseconds == 0),
                    "NCU receives captured duration and explicit removal");
                Require(player.WithPosition(1, 2, 3).WithHealth(100, 90)
                    .WithEquipmentAppearance(player.EquipmentAppearance).IsMorphedPlayer,
                    "movement, damage and equipment updates preserve morph");
                using var clear = new MemoryStream();
                WriteInt(clear, AORebirthProtocol.Stat); WriteInt(clear, 50000); WriteInt(clear, 44); clear.WriteByte(1);
                WriteInt(clear, 1); WriteInt(clear, 359); WriteInt(clear, 0);
                apply.Invoke(backend, new object[] { new AORebirthPacket(10, 0, clear.ToArray()), deltas, 0, 0 });
                Require(!entities["50000:44"].IsMorphedPlayer && entities["50000:44"].EquipmentAppearance != null,
                    "server clearing MonsterData restores humanoid body and preserves outfit");
            }
            Console.WriteLine($"PASS captured gameplay {Path.GetFileName(file)}: hits={hits}, misses={misses}, appearance={outfits}, buffs={nanos}, casts={casts}, locks={locks}");
        }
    }
}
