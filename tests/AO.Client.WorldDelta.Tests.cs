using System;
using System.IO;
using AO.Client.Backends.AORebirth;

internal static class WorldDeltaTests
{
    private static int Main()
    {
        TestMovement();
        TestMobPathMovement();
        TestOutboundPlayerMovement();
        TestFullCharacterState();
        TestHealthStats();
        TestDespawn();
        TestVendingMachine();
        Console.WriteLine("PASS AO.Client world delta decoding");
        return 0;
    }

    private static void TestFullCharacterState()
    {
        using var body = new MemoryStream();
        WriteInt(body, AORebirthProtocol.FullCharacter);
        WriteInt(body, 50000); WriteInt(body, 18);
        body.WriteByte(1);
        WriteInt(body, 1);
        WriteInt(body, 5 * 0x03F1);
        WriteCharacterSlot(body, 0x06, 101, 200001, 100001, 100002, 25);
        WriteCharacterSlot(body, 0x11, 102, 200002, 100003, 100004, 50);
        WriteCharacterSlot(body, 0x21, 103, 200003, 100005, 100006, 75);
        WriteCharacterSlot(body, 0x31, 104, 200004, 100007, 100008, 100);
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
        Require(state.Slots.Count == 4, "full character slot count");
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
