using System;
using System.IO;
using AO.Client.Backends.AORebirth;

internal static partial class WorldDeltaTests
{
    private static int Main()
    {
        TestReadCancellation();
        TestZoneLogin();
        TestZoneRedirection();
        TestAppearance();
        TestItemMoves();
        TestEquipmentRevisionThroughStats();
        TestNanoCastTargets();
        TestNativeItemUse();
        TestMovement();
        TestMobPathMovement();
        TestOutboundPlayerMovement();
        TestChatText();
        TestFullCharacterState();
        TestHealthStats();
        TestNativeCombat();
        TestNativeSpecials();
        TestCapturedGameplay();
        TestRunningNanoRestore();
        TestCapturedDeathAndCorpses();
        TestImmediateWorldDelivery();
        TestDespawn();
        TestVendingMachine();
        Console.WriteLine("PASS AO.Client world delta decoding");
        return 0;
    }

    private static void TestNanoCastTargets()
    {
        var npc = new AO.Client.World.NearbyEntity(50000, 99, AO.Client.World.NearbyEntityKind.Npc,
            "Combat target", 1, 100, 0, 127, 0, 0, 0);
        var player = new AO.Client.World.NearbyEntity(50000, 42, AO.Client.World.NearbyEntityKind.Player,
            "Other player", 1, 100, 0, 127, 0, 0, 0);
        // Installed Composite Utility metadata: Can=0, Flags=0x10000, NCU=4.
        var composite = AO.Client.World.NanoCastTargetPolicy.Resolve(18, 0, 0x10000, npc);
        var request = AORebirthProtocol.CreateNanoCast(18, 287046, composite.IdentityType, composite.IdentityInstance);
        Require(new BigEndianReader(request, 16 + 25).ReadInt32() == 0,
            "Composite Utility uses caster NCU with an NPC still selected");
        Require(AO.Client.World.NanoCastTargetPolicy.Resolve(18, 0, 0x10000, null).IdentityInstance == 18,
            "personal buff defaults to self with no target");
        Require(AO.Client.World.NanoCastTargetPolicy.Resolve(18, 1 << 23, 0x10004, player).IdentityInstance == 18,
            "self-only Pronouncement stays on caster when another player is selected");
        Require(AO.Client.World.NanoCastTargetPolicy.Resolve(18, 0, 0x10000, player).IdentityInstance == 42,
            "friendly buff can still target another player");
        Require(AO.Client.World.NanoCastTargetPolicy.Resolve(18, 1 << 21, 0, npc).IdentityInstance == 99,
            "explicit friendly targeting keeps NPC and pet recipients");
        Require(AO.Client.World.NanoCastTargetPolicy.Resolve(18, 1 << 22, 1 << 15, npc).IdentityInstance == 99,
            "hostile nano retains combat target");
        var cancel = AORebirthProtocol.CreateNanoCancel(18, 82834);
        Require(cancel.Length == 55 && new BigEndianReader(cancel, 16 + 13).ReadInt32() == 0x41
            && new BigEndianReader(cancel, 16 + 21).ReadInt32() == 0
            && new BigEndianReader(cancel, 16 + 33).ReadInt32() == 82834,
            "NCU right-click sends native RemoveFriendlyNano with program id in Parameter2");
        bool rejected = false;
        try { AO.Client.World.NanoCastTargetPolicy.Resolve(18, 1 << 22, 1 << 15, null); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "hostile nano with no target does not become a self cast");
    }

    private static void TestEquipmentRevisionThroughStats()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var backend = new AORebirthBackend();
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("18", "Test player", 1, 1, 1));
        var oldWeapon = new AO.Client.World.InventoryEntrySnapshot(6, 101, 12, 100003, 100004, 1, 1);
        var newWeapon = new AO.Client.World.InventoryEntrySnapshot(0x40, 101, 10, 100001, 100002, 1, 1);
        typeof(AORebirthBackend).GetField("_characterState", flags)!.SetValue(backend,
            new AO.Client.World.CharacterStateSnapshot(new[] { oldWeapon }, new[] { 123 }));
        typeof(AORebirthBackend).GetField("_inventory", flags)!.SetValue(backend,
            new AO.Client.World.InventorySnapshot(30, 50000, 18, 0, new[] { newWeapon }));
        var apply = typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!;
        var deltas = new System.Collections.Generic.List<AO.Client.World.WorldEntityDelta>();
        using var stats = new MemoryStream();
        WriteInt(stats, AORebirthProtocol.Stat); WriteInt(stats, 50000); WriteInt(stats, 18); stats.WriteByte(1);
        WriteInt(stats, 2); WriteInt(stats, 180); WriteInt(stats, 0); WriteInt(stats, 181); WriteInt(stats, 8);
        var statPacket = new AORebirthPacket(10, 0, stats.ToArray());
        apply.Invoke(backend, new object[] { MoveAck(101, 6, 0x42), deltas, 0, 0 });
        apply.Invoke(backend, new object[] { statPacket, deltas, 0, 0 });
        var removed = backend.GetCharacterStateSnapshot();
        Require(removed.IsStatUpdateOnly && removed.Slots.Count == 0 && removed.EquipmentRevision == 1,
            "unequip survives following stats and requires revision-zero UI to clear its icon");
        Require(Find(backend.GetInventorySnapshot().Entries, 0x42).IdentityInstance == 12,
            "removed weapon appears in inventory");
        apply.Invoke(backend, new object[] { MoveAck(104, 0x40, 6), deltas, 0, 0 });
        apply.Invoke(backend, new object[] { statPacket, deltas, 0, 0 });
        var equipped = backend.GetCharacterStateSnapshot();
        Require(equipped.IsStatUpdateOnly && equipped.EquipmentRevision == 2 && Find(equipped.Slots, 6).IdentityInstance == 10,
            "new weapon survives same-batch stats and replaces previous equipment icon");
        apply.Invoke(backend, new object[] { statPacket, deltas, 0, 0 });
        Require(backend.GetCharacterStateSnapshot().EquipmentRevision == 2,
            "ordinary stat updates preserve equipment revision without causing a rebuild");
    }

    private static void TestNativeCombat()
    {
        byte[] start = AORebirthProtocol.CreateAttackRequest(18, 50000, 99);
        Require(start.Length == 38 && new BigEndianReader(start, 16).ReadInt32() == 0x28494070,
            "Q attack uses native Attack packet");
        var started = new AORebirthPacket(10, 0, start[16..]);
        Require(AORebirthProtocol.TryReadCombatUpdate(started, out var attack)
            && attack.Kind == AO.Client.World.CombatUpdateKind.Started && attack.AttackerInstance == 18
            && attack.TargetInstance == 99, "decode server attack state");
        byte[] stop = AORebirthProtocol.CreateAttackRequest(18, 0, 0);
        Require(stop.Length == 33 && AORebirthProtocol.TryReadCombatUpdate(new AORebirthPacket(10, 0, stop[16..]), out var stopped)
            && stopped.Kind == AO.Client.World.CombatUpdateKind.Stopped, "Q stops through native StopFight");
        using var body = new MemoryStream();
        WriteInt(body, 0x46002f16); WriteInt(body, 50000); WriteInt(body, 18); body.WriteByte(0);
        WriteInt(body, 23); WriteInt(body, 19); WriteInt(body, 6);
        WriteInt(body, 50000); WriteInt(body, 99); WriteInt(body, 0); WriteInt(body, 0); WriteInt(body, 4321);
        var hit = new AORebirthPacket(10, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadCombatUpdate(hit, out var damage) && damage.Damage == 23
            && damage.TargetInstance == 99, "server weapon damage parsed independently of ammo, hand and weapon instance");
        for (int length = 0; length < body.Length; length++)
            Require(!AORebirthProtocol.TryReadCombatUpdate(new AORebirthPacket(10, 0, body.ToArray()[..length]), out _),
                "truncated hit ignored");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var backend = new AORebirthBackend();
        var entities = (System.Collections.Generic.Dictionary<string, AO.Client.World.NearbyEntity>)
            typeof(AORebirthBackend).GetField("_worldEntities", flags)!.GetValue(backend)!;
        entities["50000:99"] = new AO.Client.World.NearbyEntity(50000, 99, AO.Client.World.NearbyEntityKind.Npc,
            "Test target", 1, 100, 10, 127, 0, 0, 0);
        var deltas = new System.Collections.Generic.List<AO.Client.World.WorldEntityDelta>();
        typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!.Invoke(backend, new object[] { hit, deltas, 0, 0 });
        Require(entities["50000:99"].HealthDamage == 33 && deltas.Count == 1,
            "AttackInfo alone updates target HP without waiting for stats");
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("99", "Test player", 1, 1, 1));
        typeof(AORebirthBackend).GetField("_characterState", flags)!.SetValue(backend,
            new AO.Client.World.CharacterStateSnapshot(null, null,
                new System.Collections.Generic.Dictionary<int, int> { [27] = 90, [1] = 100 }));
        typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!.Invoke(backend, new object[] { hit, deltas, 0, 0 });
        Require(backend.GetCharacterStateSnapshot().Stats[27] == 67
            && backend.GetCharacterStateSnapshot().IsStatUpdateOnly,
            "incoming weapon hit updates controlled health without rehydrating equipment");
        using var missBody = new MemoryStream();
        WriteInt(missBody, 0x5c654b28); WriteInt(missBody, 50000); WriteInt(missBody, 18); missBody.WriteByte(1);
        WriteInt(missBody, -1); WriteInt(missBody, 6); WriteInt(missBody, 50000); WriteInt(missBody, 18);
        WriteInt(missBody, 50000); WriteInt(missBody, 99); WriteInt(missBody, 0);
        Require(AORebirthProtocol.TryReadCombatUpdate(new AORebirthPacket(10, 0, missBody.ToArray()), out var miss)
            && miss.Kind == AO.Client.World.CombatUpdateKind.Miss && miss.Damage == 0, "native miss never applies damage");
    }

    private static void TestNativeSpecials()
    {
        var request = AORebirthProtocol.CreateSpecialAttackRequest(18, 148, 50000, 99);
        Require(request.Length == 41 && new BigEndianReader(request, 16).ReadInt32() == 0x51492120
            && new BigEndianReader(request, 16 + 21).ReadInt32() == 148,
            "native Burst request addresses target and special skill");
        using var body = new MemoryStream();
        WriteInt(body, 0x754f1115); WriteInt(body, 50000); WriteInt(body, 18); body.WriteByte(0);
        WriteInt(body, 8); WriteInt(body, 72); WriteInt(body, 3);
        WriteInt(body, 50000); WriteInt(body, 99); WriteInt(body, 148); WriteInt(body, 0);
        Require(AORebirthProtocol.TryReadCombatUpdate(new AORebirthPacket(10, 0, body.ToArray()), out var hit)
            && hit.Damage == 72 && hit.SpecialStatId == 148 && hit.HandSlot == 8,
            "special hit retains damage, skill and attacking hand");
        for (int length = 0; length < body.Length; length++)
            Require(!AORebirthProtocol.TryReadCombatUpdate(new AORebirthPacket(10, 0, body.ToArray()[..length]), out _),
                "truncated special hit ignored");
        static AORebirthPacket Action(int action, int p1, int p2)
        {
            using var data = new MemoryStream();
            WriteInt(data, 0x5e477770); WriteInt(data, 50000); WriteInt(data, 18); data.WriteByte(0);
            WriteInt(data, action); WriteInt(data, 0); WriteInt(data, 0); WriteInt(data, 0);
            WriteInt(data, p1); WriteInt(data, p2); WriteShort(data, 0);
            return new AORebirthPacket(10, 0, data.ToArray());
        }
        var used = Action(0xaa, 148, 19);
        Require(AORebirthProtocol.TryReadSpecialCooldown(used, 18, out var lockUpdate)
            && lockUpdate.StatId == 148 && lockUpdate.Seconds == 19, "server special used sets a nineteen-second cooldown");
        Require(AORebirthProtocol.TryReadSpecialCooldown(Action(0xa4, 0, 148), 18, out var ready)
            && ready.StatId == 148 && ready.Seconds == 0, "server special available clears cooldown");
        Require(!AORebirthProtocol.TryReadSpecialCooldown(used, 99, out _), "another player's cooldown ignored");
        Require(!AORebirthProtocol.TryReadSpecialCooldown(Action(0x62, 148, 19), 18, out _), "nano duration cannot become a special cooldown");
        for (int length = 0; length < used.Body.Length; length++)
            Require(!AORebirthProtocol.TryReadSpecialCooldown(new AORebirthPacket(10, 0, used.Body[..length]), 18, out _),
                "truncated special cooldown ignored");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var backend = new AORebirthBackend();
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("18", "Test player", 1, 1, 1));
        var deltas = new System.Collections.Generic.List<AO.Client.World.WorldEntityDelta>();
        typeof(AORebirthBackend).GetMethod("ApplyWorldPacket", flags)!.Invoke(backend, new object[] { used, deltas, 0, 0 });
        var drain = typeof(AORebirthBackend).GetMethod("DrainSpecialCooldowns", flags)!;
        Require(((System.Collections.Generic.IReadOnlyList<AO.Client.World.SpecialCooldownUpdate>)drain.Invoke(backend, null)!).Count == 1,
            "server cooldown forwarded to Unity");
        Require(((System.Collections.Generic.IReadOnlyList<AO.Client.World.SpecialCooldownUpdate>)drain.Invoke(backend, null)!).Count == 0,
            "cooldown delivered once");
    }

    private static void TestImmediateWorldDelivery()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var backend = new AORebirthBackend();
        var mailboxType = typeof(AORebirthBackend).GetNestedType("WorldPacketMailbox", System.Reflection.BindingFlags.NonPublic)!;
        var mailbox = Activator.CreateInstance(mailboxType, true)!;
        typeof(AORebirthBackend).GetField("_worldPackets", flags)!.SetValue(backend, mailbox);
        typeof(AORebirthBackend).GetField("_worldStream", flags)!.SetValue(backend, new MemoryStream());
        typeof(AORebirthBackend).GetField("_currentPlayfieldId", flags)!.SetValue(backend, 127);
        typeof(AORebirthBackend).GetField("<State>k__BackingField", flags)!.SetValue(backend, AO.Client.ClientConnectionState.InWorld);
        var packet = new AORebirthPacket(10, 0, AORebirthProtocol.CreateAttackRequest(18, 50000, 99)[16..]);
        mailboxType.GetMethod("Write")!.Invoke(mailbox, new object[] { packet });
        var batch = backend.ReceiveWorldDeltasAsync(128);
        Require(batch.IsCompletedSuccessfully && batch.Result.PacketsObserved == 1 && batch.Result.CombatUpdates.Count == 1,
            "isolated server reply is delivered immediately with no trailing aggregation delay");
        mailboxType.GetMethod("Write")!.Invoke(mailbox, new object[] { packet });
        using var canceled = new System.Threading.CancellationTokenSource(); canceled.Cancel();
        try { backend.ReceiveWorldDeltasAsync(128, canceled.Token).GetAwaiter().GetResult(); Require(false, "cancel expected"); }
        catch (OperationCanceledException) { }
        Require(backend.ReceiveWorldDeltasAsync(128).Result.PacketsObserved == 1,
            "canceled poll leaves queued server reply available");
    }

    private static void TestZoneRedirection()
    {
        byte[] body = Convert.FromHexString("0000003C7F0000011D4D");
        var packet = new AORebirthPacket(1, AORebirthProtocol.ZoneRedirection, body);
        Require(AORebirthProtocol.TryReadZoneRedirection(packet, out var address, out int port)
            && address.ToString() == "127.0.0.1" && port == 7501,
            "decode zone redirection endpoint");
        Require(!AORebirthProtocol.TryReadZoneRedirection(
                new AORebirthPacket(1, AORebirthProtocol.ZoneRedirection, new byte[9]),
                out _, out _),
            "reject truncated zone redirection");
    }

    private static void TestChatText()
    {
        const string command = ".worldgen 12345 12 test-dungeon";
        byte[] outbound = AORebirthProtocol.CreateTextMessage(18, command);
        Require(outbound[2] == 0 && outbound[3] == AORebirthProtocol.TextMessagePacketType,
            "outbound command uses AO TextMessage packet type");

        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.ChatText);
        WriteInt(body, 50000); WriteInt(body, 18); body.WriteByte(0);
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("Generated test-dungeon");
        WriteShort(body, (short)encoded.Length); body.Write(encoded, 0, encoded.Length);
        body.WriteByte(0); body.WriteByte(0); WriteInt(body, 0);
        var inbound = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadChatText(inbound, out string text)
            && text == "Generated test-dungeon", "decode server ChatText response");
    }

    private static void TestZoneLogin()
    {
        // Synthetic identity/cookies; header sender and body identity must agree.
        byte[] expected = Convert.FromHexString(
            "000100010001002001020304000000020000001B0102030489ABCDEFFEDCBA98");
        byte[] actual = AORebirthProtocol.CreateZoneLogin(0x01020304, 0x89ABCDEF, 0xFEDCBA98);
        Require(System.Linq.Enumerable.SequenceEqual(actual, expected),
            "zone login uses character sender, receiver 2, sequence 1, and intact cookies");
    }

    private static void TestReadCancellation()
    {
        var method = typeof(AORebirthBackend).GetMethod("ReadExactAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(Stream), typeof(int), typeof(System.Threading.CancellationToken) }, null);
        foreach (bool canceled in new[] { false, true })
        foreach (bool eof in new[] { false, true })
        {
            using var cancellation = new System.Threading.CancellationTokenSource();
            using var stream = new InterruptedReadStream(cancellation, canceled, eof);
            var task = (System.Threading.Tasks.Task<byte[]>)method.Invoke(new AORebirthBackend(),
                new object[] { stream, 2, cancellation.Token });
            try { task.GetAwaiter().GetResult(); throw new Exception("Expected read failure"); }
            catch (OperationCanceledException) when (canceled) { }
            catch (IOException) when (!canceled) { }
        }
    }

    private sealed class InterruptedReadStream : MemoryStream
    {
        private readonly System.Threading.CancellationTokenSource _cancellation;
        private readonly bool _cancel;
        private readonly bool _eof;
        public InterruptedReadStream(System.Threading.CancellationTokenSource cancellation, bool cancel, bool eof)
        { _cancellation = cancellation; _cancel = cancel; _eof = eof; }
        public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count,
            System.Threading.CancellationToken cancellationToken)
        {
            if (_cancel) _cancellation.Cancel();
            return _eof ? System.Threading.Tasks.Task.FromResult(0)
                : System.Threading.Tasks.Task.FromException<int>(new IOException("Socket closed during read"));
        }
    }

    private static void TestAppearance()
    {
        var update = AppearancePacket();
        Require(AORebirthProtocol.TryReadAppearanceUpdate(update, out int type, out int id, out var appearance), "decode appearance update");
        Require(type == 50000 && id == 18 && appearance.Textures[0].Position == 1
            && appearance.Textures[0].TextureId == 123 && appearance.Meshes[0].OverrideTextureId == 789
            && appearance.Meshes[0].MeshId == 456 && appearance.Meshes[0].Layer == 5,
            "preserve body placement, mesh ID, texture override and layer");
        for (int size = 0; size < update.Body.Length; size++)
        {
            var truncated = new byte[size]; Array.Copy(update.Body, truncated, size);
            Require(!AORebirthProtocol.TryReadAppearanceUpdate(new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, truncated),
                out _, out _, out _), "reject truncated appearance update");
        }
        var invalid = (byte[])update.Body.Clone(); invalid[13] = 0x7F;
        Require(!AORebirthProtocol.TryReadAppearanceUpdate(new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, invalid),
            out _, out _, out _), "reject malformed appearance array count");
        Require(AORebirthProtocol.TryReadAppearanceUpdate(AppearancePacket(true), out _, out _, out var empty)
            && empty.Meshes.Count == 0 && empty.Textures.Count == 0, "empty update explicitly clears outfit");

        foreach (bool extended in new[] { false, true })
        {
            var packet = AppearanceFullUpdate(extended);
            Require(AORebirthProtocol.TryReadNearbyEntity(packet, 1, out var entity), "decode SCFU appearance tail");
            Require(entity.EquipmentAppearance?.HeadMeshId == 42 && entity.EquipmentAppearance.Textures[0].TextureId == 123
                && entity.EquipmentAppearance.Meshes[0].MeshId == 456, "SCFU skips nanos and waypoints and preserves head");
            var moved = entity.WithPosition(4, 5, 6).WithHealth(100, 90);
            Require(ReferenceEquals(moved.EquipmentAppearance, entity.EquipmentAppearance), "movement and health retain appearance");
            var unequipped = moved.WithEquipmentAppearance(empty);
            Require(unequipped.EquipmentAppearance.HeadMeshId == 42 && unequipped.EquipmentAppearance.Meshes.Count == 0,
                "appearance-only update preserves base head while clearing equipment");
            for (int size = packet.Body.Length - 17; size < packet.Body.Length; size++)
            {
                var truncated = new byte[size]; Array.Copy(packet.Body, truncated, size);
                Require(!AORebirthProtocol.TryReadNearbyEntity(new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, truncated),
                    1, out _), "reject incomplete SCFU appearance tail");
            }
        }
        var meshSource = new[] {
            new AO.Client.World.AppearanceMesh(0, 42, 0, 1),
            new AO.Client.World.AppearanceMesh(0, 99, 0, 5),
            new AO.Client.World.AppearanceMesh(3, 100, 0, 1),
            new AO.Client.World.AppearanceMesh(4, 101, 0, 1),
            new AO.Client.World.AppearanceMesh(1, 102, 0, 1),
            new AO.Client.World.AppearanceMesh(2, 103, 0, 1) };
        var visible = new AO.Client.World.CharacterAppearanceSnapshot(null, meshSource, 7, 42);
        meshSource[1] = default;
        Require(System.Linq.Enumerable.Count(visible.VisibleMeshes()) == 5
            && System.Linq.Enumerable.Last(visible.VisibleMeshes()).MeshId == 99, "immutable snapshot, highest helmet layer, both hands and shoulders");
        var hidden = new AO.Client.World.CharacterAppearanceSnapshot(null, visible.Meshes, 0, 42);
        Require(System.Linq.Enumerable.Count(hidden.VisibleMeshes()) == 3
            && System.Linq.Enumerable.Last(hidden.VisibleMeshes()).MeshId == 42, "hide helmet and shoulders without hiding hands or base head");
        Require(System.Linq.Enumerable.Single(new AO.Client.World.CharacterAppearanceSnapshot(null, null, 0, 42).VisibleMeshes()).MeshId == 42,
            "base head fallback after unequip");
    }

    private static void WriteAppearanceArrays(Stream body, bool empty = false)
    {
        WriteInt(body, (empty ? 1 : 2) * 0x3F1);
        if (!empty) { WriteInt(body, 1); WriteInt(body, 123); WriteInt(body, 0); }
        WriteInt(body, (empty ? 1 : 2) * 0x3F1);
        if (!empty) { body.WriteByte(1); WriteInt(body, 456); WriteInt(body, 789); body.WriteByte(5); }
    }

    private static AORebirthPacket AppearancePacket(bool empty = false)
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.AppearanceUpdate); WriteInt(body, 50000); WriteInt(body, 18); body.WriteByte(0);
        WriteAppearanceArrays(body, empty); body.WriteByte(0); body.WriteByte(7); body.WriteByte(0);
        return new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
    }

    private static AORebirthPacket AppearanceFullUpdate(bool extended, int remaining = 3600)
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.SimpleCharFullUpdate); WriteInt(body, 50000); WriteInt(body, 18);
        body.WriteByte(0); body.WriteByte(0);
        WriteInt(body, 0x80 | 0x10000 | 0x04000000 | (extended ? 0x10 : 0));
        WriteInt(body, 0); WriteInt(body, 0); WriteInt(body, 0); // position
        WriteInt(body, (1 << 5) | (2 << 8) | (1 << 10)); body.WriteByte(0); // appearance and name
        WriteInt(body, 0x00400000); WriteInt(body, 0); // visible-name character flag and account/expansion flags
        body.Write(new byte[22]); // player stats
        WriteInt(body, 1234); body.WriteByte(0); // organization identity/rank
        body.WriteByte(3); body.Write(System.Text.Encoding.ASCII.GetBytes("Ada"));
        body.WriteByte(8); body.Write(System.Text.Encoding.ASCII.GetBytes("Lovelace"));
        body.WriteByte(7); body.Write(System.Text.Encoding.ASCII.GetBytes("TestOrg"));
        body.WriteByte(1); WriteInt(body, 100); WriteInt(body, 0); // level, health, damage
        WriteInt(body, 0); body.Write(new byte[] { 0, 100, 0, 7, 0 }); // monster, scale, visuals, title
        WriteInt(body, 0); WriteInt(body, 42); body.WriteByte(10); // opaque data, head, speed
        if (extended)
        {
            WriteInt(body, 2 * 0x3F1);
            body.Write(new byte[44]);
        }
        WriteInt(body, (remaining > 0 ? 2 : 1) * 0x3F1);
        if (remaining > 0)
        {
            WriteInt(body, 53019); WriteInt(body, 82834); WriteInt(body, 123456);
            WriteInt(body, remaining); WriteInt(body, remaining);
        }
        WriteInt(body, 50000); WriteInt(body, 18); WriteInt(body, 1); body.Write(new byte[12]); // waypoint
        WriteAppearanceArrays(body);
        WriteInt(body, 2); body.WriteByte(0); // complete tail
        return new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
    }

    private static void TestItemMoves()
    {
        var inventory = new AO.Client.World.InventorySnapshot(30, 50000, 18, 0,
            new[] { Item(0x40, 10), Item(0x41, 11) });
        var state = new AO.Client.World.CharacterStateSnapshot(new[] { Item(6, 12) },
            new[] { 123 }, new System.Collections.Generic.Dictionary<int, int> { [16] = 105 });
        var wire = AORebirthProtocol.CreateItemMove(18,
            new AO.Client.World.ItemLocation(AO.Client.World.ItemArea.Inventory, 0),
            new AO.Client.World.ItemLocation(AO.Client.World.ItemArea.Weapons, 5));
        Require(Convert.ToHexString(wire) ==
            "DFDF000A0001002900000012000000005469373F0000C3500000001200000000680000004000000006",
            "inventory to weapon request matches native wire layout");
        var unequipWire = AORebirthProtocol.CreateItemMove(18,
            new AO.Client.World.ItemLocation(AO.Client.World.ItemArea.Weapons, 5),
            new AO.Client.World.ItemLocation(AO.Client.World.ItemArea.Inventory, 2));
        Require(Convert.ToHexString(unequipWire) ==
            "DFDF000A0001002900000012000000005469373F0000C350000000120000000065000000060000006F",
            "weapon unequip uses captured inventory destination marker");
        Require(AORebirthProtocol.TryApplyItemMove(MoveAck(104, 0x40, 6), 18, inventory, state,
            out var moved, out var worn), "server confirms occupied hand swap");
        Require(Find(moved.Entries, 0x40).IdentityInstance == 12 && Find(worn.Slots, 6).IdentityInstance == 10,
            "equipment swap preserves both items");
        Require(Find(inventory.Entries, 0x40).IdentityInstance == 10 && Find(state.Slots, 6).IdentityInstance == 12,
            "confirmation does not mutate previous snapshots");
        Require(worn.UploadedNanoIds[0] == 123 && worn.Stats[16] == 105 && !worn.IsStatUpdateOnly,
            "equipment update preserves nano and stat state");
        Require(AORebirthProtocol.TryApplyItemMove(MoveAck(101, 6, 0x42), 18, moved, worn,
            out var unequipped, out var emptyWear) && emptyWear.Slots.Count == 0
            && Find(unequipped.Entries, 0x42).IdentityInstance == 10, "confirmed unequip");
        Require(AORebirthProtocol.TryApplyItemMove(MoveAck(104, 0x42, 0x45), 18, unequipped, emptyWear,
            out var relocated, out _) && Find(relocated.Entries, 0x45).IdentityInstance == 10,
            "confirmed inventory move");
        Require(!AORebirthProtocol.TryApplyItemMove(MoveAck(104, 0x42, 0x45), 18, relocated, emptyWear,
            out _, out _), "duplicate move cannot move absent source");
        Require(!AORebirthProtocol.TryApplyItemMove(MoveAck(104, 0x40, 6, 19), 18, inventory, state,
            out _, out _), "ignore other character confirmation");
        Require(!AORebirthProtocol.TryApplyItemMove(MoveAck(102, 0x40, 6), 18, inventory, state,
            out _, out _), "reject mismatched source page");
        Require(!AORebirthProtocol.TryApplyItemMove(MoveAck(104, 0x40, 0x41), 18, inventory, state,
            out _, out _), "never overwrite occupied inventory on unsupported merge");
        foreach (int target in new[] { 0x11, 0x21, 0x31 })
        {
            Require(AORebirthProtocol.TryApplyItemMove(MoveAck(104, 0x40, target), 18, inventory, state,
                out var equipped, out var equipment), "equip armor, implant or social slot");
            int page = target == 0x31 ? 115 : target == 0x21 ? 103 : 102;
            Require(AORebirthProtocol.TryApplyItemMove(MoveAck(page, target, 0x44), 18, equipped, equipment,
                out var returned, out _) && Find(returned.Entries, 0x44).IdentityInstance == 10,
                "unequip each wear page");
        }
        var complete = MoveAck(104, 0x40, 6).Body;
        for (int length = 0; length < complete.Length; length++)
        {
            var truncated = new byte[length]; Array.Copy(complete, truncated, length);
            Require(!AORebirthProtocol.TryApplyItemMove(new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, truncated),
                18, inventory, state, out _, out _), "truncated confirmation rejected");
        }
    }

    private static AO.Client.World.InventoryEntrySnapshot Item(int slot, int id) =>
        new AO.Client.World.InventoryEntrySnapshot(slot, 104, id, 100, 101, 50, 1);

    private static AO.Client.World.InventoryEntrySnapshot Find(
        System.Collections.Generic.IReadOnlyList<AO.Client.World.InventoryEntrySnapshot> entries, int slot)
    {
        foreach (var entry in entries) if (entry.Slot == slot) return entry;
        throw new Exception("Missing slot " + slot);
    }

    private static AORebirthPacket MoveAck(int page, int from, int to, int owner = 18)
    {
        using var body = new MemoryStream();
        WriteInt(body, 0x47537a24); WriteInt(body, 50000); WriteInt(body, owner); body.WriteByte(0);
        WriteInt(body, page); WriteInt(body, from); WriteInt(body, 50000); WriteInt(body, owner); WriteInt(body, to);
        return new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
    }

    private static void TestNativeItemUse()
    {
        // GenericCmd has a fixed target identity, not an X3F1-prefixed target array.
        byte[] request = AORebirthProtocol.CreateItemUse(18, 104, 0x40);
        Require(request.Length == 61, "GenericCmd item use wire size");
        var reader = new BigEndianReader(request, 16);
        Require(reader.ReadInt32() == 0x52526858 && reader.ReadInt32() == 50000 && reader.ReadInt32() == 18, "use sender");
        reader.ReadByte();
        Require(reader.ReadInt32() == 0 && reader.ReadInt32() == 1 && reader.ReadInt32() == 3 && reader.ReadInt32() == 0, "use action fields");
        Require(reader.ReadInt32() == 50000 && reader.ReadInt32() == 18, "use user identity");
        Require(reader.ReadInt32() == 104 && reader.ReadInt32() == 64, "use inventory target");
        byte[] backpackRequest = AORebirthProtocol.CreateItemUse(18, 107, unchecked((60000 << 16) | 7));
        Require(new BigEndianReader(backpackRequest, 16 + 37).ReadInt32() == 107
            && new BigEndianReader(backpackRequest, 16 + 41).ReadInt32() == unchecked((60000 << 16) | 7), "packed backpack target preserves unsigned handle");
        var slot = new AO.Client.World.ItemLocation(AO.Client.World.ItemArea.Backpack, 7, 1234);
        Require(slot.ContainerInstance == 1234, "backpack address retains container identity");

        using var body = new MemoryStream();
        WriteInt(body, 0x5e477770); WriteInt(body, 50000); WriteInt(body, 18); body.WriteByte(0);
        WriteInt(body, 0xCC); WriteInt(body, 0); WriteInt(body, 50000); WriteInt(body, 18);
        WriteInt(body, 53019); WriteInt(body, 123456); WriteShort(body, 0);
        var upload = new AORebirthPacket(10, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadUploadedNano(upload, 18, out int nano) && nano == 123456, "server upload acknowledgement");
        Require(!AORebirthProtocol.TryReadUploadedNano(upload, 19, out _), "foreign upload rejected");
        for (int length = 0; length < body.Length; length++)
            Require(!AORebirthProtocol.TryReadUploadedNano(new AORebirthPacket(10, 0, body.ToArray()[..length]), 18, out _), "truncated upload rejected");

        using var backend = new AORebirthBackend();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(AORebirthBackend).GetField("_selectedCharacter", flags)!.SetValue(backend,
            new AO.Client.Characters.CharacterSummary("18", "Test", 1, 1, 1));
        typeof(AORebirthBackend).GetField("_characterState", flags)!.SetValue(backend,
            new AO.Client.World.CharacterStateSnapshot(Array.Empty<AO.Client.World.InventoryEntrySnapshot>(), Array.Empty<int>()));
        var captureState = typeof(AORebirthBackend).GetMethod("CaptureCharacterState", flags)!;
        captureState.Invoke(backend, new object[] { upload });
        captureState.Invoke(backend, new object[] { upload });
        Require(backend.GetCharacterStateSnapshot().UploadedNanoIds.Count == 1, "upload acknowledgement updates roster once");
        var main = new AO.Client.World.InventorySnapshot(30, 50000, 18, 0, Array.Empty<AO.Client.World.InventoryEntrySnapshot>());
        typeof(AORebirthBackend).GetField("_inventory", flags)!.SetValue(backend, main);
        using var inventory = new MemoryStream();
        WriteInt(inventory, AORebirthProtocol.InventoryUpdate); WriteInt(inventory, 50000); WriteInt(inventory, 18); inventory.WriteByte(1);
        WriteInt(inventory, 21); WriteInt(inventory, 3); WriteInt(inventory, 2 * 0x03F1);
        WriteCharacterSlot(inventory, 7, 5, 9001, 100009, 100010, 20);
        WriteInt(inventory, 51017); WriteInt(inventory, 1234); WriteInt(inventory, 60000); WriteInt(inventory, 1);
        var bag = new AORebirthPacket(10, 0, inventory.ToArray());
        var captureInventory = typeof(AORebirthBackend).GetMethod("CaptureInventory", flags)!;
        captureInventory.Invoke(backend, new object[] { bag });
        Require(ReferenceEquals(backend.GetInventorySnapshot(), main), "backpack update preserves main inventory");
        var contents = backend.GetContainerInventorySnapshot(1234);
        Require(contents.Capacity == 21 && contents.MainInventorySlot == 60000 && contents.Entries[0].Quantity == 5, "backpack contents and server handle retained");
        var drain = typeof(AORebirthBackend).GetMethod("DrainContainerUpdates", flags)!;
        Require(((System.Collections.Generic.IReadOnlyList<AO.Client.World.InventorySnapshot>)drain.Invoke(backend, null)!).Count == 1, "container update delivered once");
        Require(((System.Collections.Generic.IReadOnlyList<AO.Client.World.InventorySnapshot>)drain.Invoke(backend, null)!).Count == 0, "container update queue drained");
        using var openBody = new MemoryStream();
        WriteInt(openBody, 0x2049527c); WriteInt(openBody, 51017); WriteInt(openBody, 1234); openBody.WriteByte(0);
        WriteInt(openBody, 1); WriteInt(openBody, 0x64); WriteInt(openBody, 50000); WriteInt(openBody, 18);
        var reopen = new AORebirthPacket(10, 0, openBody.ToArray());
        captureInventory.Invoke(backend, new object[] { reopen });
        Require(((System.Collections.Generic.IReadOnlyList<AO.Client.World.InventorySnapshot>)drain.Invoke(backend, null)!).Count == 1, "reopen acknowledgement republishes cached contents");
        Require(!AORebirthProtocol.TryReadContainerOpen(reopen, 19, out _), "foreign container acknowledgement rejected");
        typeof(AORebirthBackend).GetField("<State>k__BackingField", flags)!.SetValue(backend, AO.Client.ClientConnectionState.InWorld);
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        using var sender = new System.Net.Sockets.TcpClient();
        sender.Connect(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        using var receiver = accept.GetAwaiter().GetResult();
        receiver.GetStream().ReadTimeout = 2000;
        typeof(AORebirthBackend).GetField("_stream", flags)!.SetValue(backend, sender.GetStream());
        var use = backend.UseItemAsync(slot);
        byte[] wire = new byte[64];
        receiver.GetStream().ReadExactly(wire);
        Require(new BigEndianReader(wire, 16 + 37).ReadInt32() == 107
            && new BigEndianReader(wire, 16 + 41).ReadInt32() == unchecked((60000 << 16) | 7), "native use sent through world transport with padding");
        Require(!use.IsCompleted, "native use waits for the server reply");
        int correlation = new BigEndianReader(wire, 16 + 25).ReadInt32();
        var ack = AORebirthProtocol.CreateItemUse(18, 107, unchecked((60000 << 16) | 7), correlation);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ack.AsSpan(16 + 13), 1);
        var captureUse = typeof(AORebirthBackend).GetMethod("CaptureItemUseResult", flags)!;
        var ackBody = new byte[45]; Array.Copy(ack, 16, ackBody, 0, 45);
        captureUse.Invoke(backend, new object[] { new AORebirthPacket(10, 0, ackBody) });
        use.GetAwaiter().GetResult();
        Require(contents.Entries[0].Quantity == 5, "using item does not optimistically consume it");
        var cast = AORebirthProtocol.CreateNanoCast(18, 123456, 50000, 18);
        Require(cast.Length == 55 && new BigEndianReader(cast, 16 + 13).ReadInt32() == 0x13
            && new BigEndianReader(cast, 16 + 33).ReadInt32() == 123456, "native CharacterAction cast fields");
        using var duration = new MemoryStream();
        WriteInt(duration, 0x5e477770); WriteInt(duration, 50000); WriteInt(duration, 18); duration.WriteByte(0);
        WriteInt(duration, 0x62); WriteInt(duration, 0); WriteInt(duration, 53019); WriteInt(duration, 123456);
        WriteInt(duration, 18); WriteInt(duration, 6000); WriteShort(duration, 0);
        var land = new AORebirthPacket(10, 0, duration.ToArray());
        Require(AORebirthProtocol.TryReadNanoEffect(land, 18, out var effect) && effect.NanoId == 123456
            && effect.DurationCentiseconds == 6000, "server nano landing duration");
        Require(!AORebirthProtocol.TryReadNanoEffect(land, 19, out _), "foreign nano landing ignored");
        for (int length = 0; length < duration.Length; length++)
            Require(!AORebirthProtocol.TryReadNanoEffect(new AORebirthPacket(10, 0, duration.ToArray()[..length]), 18, out _), "truncated nano landing ignored");
        using var removal = new MemoryStream();
        WriteInt(removal, 0x39343c68); WriteInt(removal, 50000); WriteInt(removal, 18); removal.WriteByte(0);
        WriteShort(removal, 0); WriteInt(removal, 53019); WriteInt(removal, 123456);
        Require(AORebirthProtocol.TryReadNanoEffect(new AORebirthPacket(10, 0, removal.ToArray()), 18, out var remove)
            && remove.DurationCentiseconds == 0, "server buff removal");
        bool rejected = false;
        try { backend.UseItemAsync(new AO.Client.World.ItemLocation(AO.Client.World.ItemArea.Backpack, 8, 1234)).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && backend.PacketsSent == 1, "empty backpack slot rejected before transport");
        var deniedUse = backend.UseItemAsync(slot);
        byte[] deniedWire = new byte[64]; receiver.GetStream().ReadExactly(deniedWire);
        captureUse.Invoke(backend, new object[] { new AORebirthPacket(10, 0, ackBody) });
        Require(!deniedUse.IsCompleted, "late acknowledgement cannot complete another use");
        var deniedBody = new byte[45]; Array.Copy(deniedWire, 16, deniedBody, 0, 45);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(deniedBody.AsSpan(13), 2);
        captureUse.Invoke(backend, new object[] { new AORebirthPacket(10, 0, deniedBody) });
        bool denied = false;
        try { deniedUse.GetAwaiter().GetResult(); } catch (InvalidOperationException) { denied = true; }
        Require(denied && contents.Entries[0].Quantity == 5, "server rejection surfaced without consuming an item");
        backend.CancelNanoAsync(82834).GetAwaiter().GetResult();
        byte[] cancelWire = new byte[56]; receiver.GetStream().ReadExactly(cancelWire);
        Require(new BigEndianReader(cancelWire, 16 + 13).ReadInt32() == 0x41
            && new BigEndianReader(cancelWire, 16 + 33).ReadInt32() == 82834,
            "nano cancellation reaches the native world transport");
    }

    private static void TestFullCharacterState()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.FullCharacter);
        WriteInt(body, 50000); WriteInt(body, 18);
        body.WriteByte(1);
        WriteInt(body, 1);
        WriteInt(body, 6 * 0x03F1);
        WriteCharacterSlot(body, 0x06, 101, 200001, 100001, 100002, 25);
        WriteCharacterSlot(body, 0x11, 102, 200002, 100003, 100004, 50);
        WriteCharacterSlot(body, 0x21, 103, 200003, 100005, 100006, 75);
        WriteCharacterSlot(body, 0x31, 104, 200004, 100007, 100008, 100);
        WriteCharacterSlot(body, 0x40, 3, 200005, 100009, 100010, 20);
        WriteInt(body, 3 * 0x03F1);
        WriteInt(body, 123456); WriteInt(body, 654321);
        WriteInt(body, 0x03F1); // Unknown2: empty X3F1 array.
        WriteInt(body, 0);      // Unknown3.
        WriteInt(body, 0);      // Unknown4: empty Int32-sized array.
        WriteInt(body, 0);      // Unknown5.
        WriteInt(body, 0);      // Unknown6.
        WriteInt(body, 0);      // Unknown7.
        WriteInt(body, 0);      // Unknown8.
        WriteInt(body, 4 * 0x03F1); // Stats1: three pairs.
        WriteInt(body, 53); WriteInt(body, 12345);
        WriteInt(body, 54); WriteInt(body, 25);
        WriteInt(body, 16); WriteInt(body, 100);
        WriteInt(body, 2 * 0x03F1); // Stats2: one pair, overriding Strength.
        WriteInt(body, 16); WriteInt(body, 105);

        var packet = new AORebirthPacket(
            AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadCharacterState(packet, out var state),
            "full character decode");
        Require(state.Slots.Count == 5, "full character slot count");
        using var backend = new AORebirthBackend();
        typeof(AORebirthBackend).GetMethod("CaptureCharacterState",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(backend, new object[] { packet });
        var inventory = backend.GetInventorySnapshot();
        Require(inventory.IsMainInventory && inventory.Entries.Count == 1
            && inventory.Entries[0].Slot == 0x40 && inventory.Entries[0].Quantity == 3,
            "FullCharacter populates main inventory without putting worn items in the bag");
        Require(state.Slots[0].Slot == 0x06 && state.Slots[0].IdentityInstance == 200001,
            "full character weapon slot");
        Require(state.Slots[1].Slot == 0x11 && state.Slots[1].Quality == 50,
            "full character armor slot");
        Require(state.Slots[2].Slot == 0x21 && state.Slots[2].HighId == 100006,
            "full character implant slot");
        Require(state.Slots[3].Slot == 0x31 && state.Slots[3].LowId == 100007,
            "full character social slot");
        Require(state.UploadedNanoIds.Count == 2
            && state.UploadedNanoIds[0] == 123456
            && state.UploadedNanoIds[1] == 654321,
            "full character uploaded nanos");
        Require(state.Stats.Count == 3
            && state.Stats[53] == 12345
            && state.Stats[54] == 25
            && state.Stats[16] == 105,
            "full character authoritative Stats1/Stats2");
    }

    private static void WriteCharacterSlot(Stream stream, int placement, short quantity,
        int identityInstance, int lowId, int highId, int quality)
    {
        WriteInt(stream, placement);
        WriteShort(stream, 0);
        WriteShort(stream, quantity);
        WriteInt(stream, 53019);
        WriteInt(stream, identityInstance);
        WriteInt(stream, lowId);
        WriteInt(stream, highId);
        WriteInt(stream, quality);
        WriteInt(stream, 0);
    }

    private static void TestOutboundPlayerMovement()
    {
        var update = new AO.Client.World.PlayerMovementUpdate(
            155.25f, 107.5f, 236.75f, 0f, 0.5f, 0f, 0.8660254f, 1, 12345);
        byte[] wire = AORebirthProtocol.CreatePlayerMovement(18, update);
        Require(wire.Length == 70, "outbound movement wire size");
        Require(wire[2] == 0 && wire[3] == AORebirthProtocol.N3PacketType,
            "outbound movement packet type");
        byte[] body = new byte[54];
        Buffer.BlockCopy(wire, 16, body, 0, body.Length);
        var packet = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body);
        Require(AORebirthProtocol.TryReadMovement(packet, out AORebirthMovement move),
            "outbound movement round trip");
        Require(move.Type == 50000 && move.Instance == 18, "outbound movement identity");
        Require(move.X == 155.25f && move.Y == 107.5f && move.Z == 236.75f,
            "outbound movement coordinates");
    }

    private static void TestMobPathMovement()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.MobPathMove);
        WriteInt(body, 50000); WriteInt(body, 1000008);
        WriteInt(body, 0x00011802);
        WriteFloat(body, 3622.5f); WriteFloat(body, 51.75f); WriteFloat(body, 798.125f);
        WriteFloat(body, 3612.25f); WriteFloat(body, 52.5f); WriteFloat(body, 787.75f);
        var packet = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadMobPathMovement(packet, out AORebirthMovement move),
            "mob path movement decode");
        Require(move.Type == 50000 && move.Instance == 1000008, "mob path identity");
        Require(move.X == 3622.5f && move.Z == 798.125f, "mob path origin");
        Require(move.HasDestination && move.DestinationX == 3612.25f
            && move.DestinationY == 52.5f && move.DestinationZ == 787.75f,
            "mob path destination");
    }

    private static void TestMovement()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.CharDCMove);
        WriteInt(body, 50000); WriteInt(body, 42);
        body.WriteByte(0); body.WriteByte(1);
        WriteFloat(body, 0); WriteFloat(body, 0); WriteFloat(body, 0); WriteFloat(body, 1);
        WriteFloat(body, 12.5f); WriteFloat(body, 3.25f); WriteFloat(body, -8.5f);
        WriteInt(body, 0); WriteFloat(body, 0); WriteFloat(body, 0);
        var packet = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadMovement(packet, out AORebirthMovement move), "movement decode");
        Require(move.Type == 50000 && move.Instance == 42, "movement identity");
        Require(move.X == 12.5f && move.Y == 3.25f && move.Z == -8.5f, "movement coordinates");
        Require(move.MoveType == 1 && move.HasHeading && move.HeadingW == 1f,
            "movement state and heading");
    }

    private static void TestHealthStats()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.Stat);
        WriteInt(body, 50000); WriteInt(body, 42); body.WriteByte(1);
        WriteInt(body, 2);
        WriteInt(body, 1); WriteInt(body, 200);
        WriteInt(body, 27); WriteInt(body, 125);
        var packet = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadHealthStats(packet, out AORebirthHealthStats health), "stat decode");
        Require(health.Maximum == 200 && health.Current == 125, "health values");
        Require(AORebirthProtocol.TryReadStatUpdate(packet, out AORebirthStatUpdate update)
            && update.Values[1] == 200 && update.Values[27] == 125,
            "generic character stat values");
    }

    private static void TestDespawn()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.Despawn);
        WriteInt(body, 50000); WriteInt(body, 42); body.WriteByte(1);
        var packet = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadDespawn(packet, out AORebirthIdentity identity), "despawn decode");
        Require(identity.Type == 50000 && identity.Instance == 42, "despawn identity");
    }

    private static void TestVendingMachine()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.VendingMachineFullUpdate);
        WriteInt(body, 51035); WriteInt(body, 77); body.WriteByte(1);
        WriteInt(body, 1234); WriteInt(body, 0); WriteInt(body, 0);
        WriteFloat(body, 10); WriteFloat(body, 20); WriteFloat(body, 30);
        WriteFloat(body, 0); WriteFloat(body, 0); WriteFloat(body, 0); WriteFloat(body, 1);
        WriteInt(body, 127); WriteInt(body, 0); WriteInt(body, 0);
        WriteShort(body, 0); WriteInt(body, 0x03F1);
        byte[] name = System.Text.Encoding.ASCII.GetBytes("Test Shop");
        WriteInt(body, name.Length); body.Write(name, 0, name.Length);
        var packet = new AORebirthPacket(AORebirthProtocol.N3PacketType, 0, body.ToArray());
        Require(AORebirthProtocol.TryReadWorldObject(packet, 127, out var item), "vending decode");
        Require(item.Name == "Test Shop" && item.HasPosition, "vending fields");
        Require(item.X == 10 && item.Y == 20 && item.Z == 30, "vending position");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
    }

    private static void WriteInt(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
    }

    private static void WriteFloat(Stream stream, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteShort(Stream stream, short value)
    {
        stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
    }
}
