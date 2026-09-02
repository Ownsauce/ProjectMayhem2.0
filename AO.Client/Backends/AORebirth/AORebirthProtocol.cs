using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using AO.Client.Characters;
using AO.Client.World;

namespace AO.Client.Backends.AORebirth
{
    internal static class AORebirthProtocol
    {
        public const int ServerSalt = 0x00000024;
        public const int LoginError = 0x0000000D;
        public const int CharacterList = 0x0000000E;
        public const int ZoneInfo = 0x00000017;
        public const int ZoneLogin = 0x0000001B;
        public const int InitiateCompressionPacketType = 0x7F00;
        public const int N3PacketType = 0x000A;
        public const int PlayfieldAnarchyF = 0x5F4B1A39;
        public const int SimpleCharFullUpdate = 0x271B3A6B;
        public const int MobPathMove = 0x260F3671;
        public const int Stat = 0x2B333D6E;
        public const int Despawn = 0x36510078;
        public const int DoorFullUpdate = 0x365A5071;
        public const int DropDynel = 0x47483633;
        public const int CorpseFullUpdate = 0x4F474E05;
        public const int CharDCMove = 0x54111123;
        public const int VendingMachineFullUpdate = 0x7F544905;
        public const int InventoryUpdate = 0x4E536976;
        public const int FullCharacter = 0x29304349;

        public static bool TryReadCharacterState(AORebirthPacket packet,
            out CharacterStateSnapshot snapshot)
        {
            snapshot = null;
            if (packet == null || packet.PacketType != N3PacketType || packet.Body.Length < 21)
                return false;
            try
            {
                var reader = new BigEndianReader(packet.Body);
                if (reader.ReadInt32() != FullCharacter) return false;
                reader.Skip(8); // N3 sender identity
                reader.ReadByte(); // N3 pass-on marker
                reader.ReadInt32(); // FullCharacter message version
                int slotCount = ReadX3F1Count(reader, "character inventory slot", 512);
                var slots = new List<InventoryEntrySnapshot>(slotCount);
                for (int index = 0; index < slotCount; index++)
                {
                    int placement = reader.ReadInt32();
                    reader.ReadInt16();
                    int quantity = reader.ReadInt16();
                    int identityType = reader.ReadInt32();
                    int identityInstance = reader.ReadInt32();
                    int lowId = reader.ReadInt32();
                    int highId = reader.ReadInt32();
                    int quality = reader.ReadInt32();
                    reader.ReadInt32();
                    slots.Add(new InventoryEntrySnapshot(placement, identityType,
                        identityInstance, lowId, highId, quality, quantity));
                }

                int nanoCount = ReadX3F1Count(reader, "uploaded nano", 8192);
                var nanos = new List<int>(nanoCount);
                for (int index = 0; index < nanoCount; index++)
                    nanos.Add(reader.ReadInt32());

                var stats = new Dictionary<int, int>();
                if (reader.Remaining > 0)
                {
                    int unknown2Count = ReadX3F1Count(reader, "FullCharacter Unknown2", 65536);
                    reader.Skip(checked(unknown2Count * 3)); // UnknownDataType1: byte, byte, byte.
                    reader.ReadInt32();
                    SkipInt32SizedArray(reader, "FullCharacter Unknown4", 65536, 20);
                    reader.ReadInt32();
                    SkipInt32SizedArray(reader, "FullCharacter Unknown6", 65536, 20);
                    reader.ReadInt32();
                    SkipInt32SizedArray(reader, "FullCharacter Unknown8", 65536, 20);
                    ReadStatPairs(reader, stats, "Stats1");
                    ReadStatPairs(reader, stats, "Stats2");
                }
                snapshot = new CharacterStateSnapshot(slots, nanos, stats);
                return true;
            }
            catch (EndOfStreamException) { return false; }
            catch (InvalidDataException) { return false; }
        }

        private static void SkipInt32SizedArray(BigEndianReader reader, string name,
            int maximum, int elementSize)
        {
            int count = reader.ReadBoundedCount(name, maximum);
            reader.Skip(checked(count * elementSize));
        }

        private static void ReadStatPairs(BigEndianReader reader,
            IDictionary<int, int> destination, string name)
        {
            int count = ReadX3F1Count(reader, name, 8192);
            for (int index = 0; index < count; index++)
                destination[reader.ReadInt32()] = reader.ReadInt32();
        }

        private static int ReadX3F1Count(BigEndianReader reader, string name, int maximum)
        {
            int encoded = reader.ReadInt32();
            if (encoded % 0x03F1 != 0)
                throw new InvalidDataException($"Invalid {name} X3F1 count: {encoded}.");
            int count = (encoded / 0x03F1) - 1;
            if (count < 0 || count > maximum)
                throw new InvalidDataException($"Invalid {name} count: {count}.");
            return count;
        }

        public static bool TryReadInventoryUpdate(AORebirthPacket packet,
            out InventorySnapshot snapshot)
        {
            snapshot = null;
            if (packet == null || packet.PacketType != N3PacketType || packet.Body.Length < 34)
                return false;
            try
            {
                var reader = new BigEndianReader(packet.Body);
                if (reader.ReadInt32() != InventoryUpdate) return false;
                reader.Skip(8); // N3 sender identity
                reader.ReadByte();
                int capacity = reader.ReadInt32();
                reader.ReadInt32();
                int encodedCount = reader.ReadInt32();
                int count = (encodedCount / 0x03F1) - 1;
                if (count < 0 || count > 1024) return false;
                var entries = new List<InventoryEntrySnapshot>(count);
                for (int i = 0; i < count; i++)
                {
                    int slot = reader.ReadInt32();
                    reader.ReadInt16();
                    int quantity = reader.ReadInt16();
                    int identityType = reader.ReadInt32();
                    int identityInstance = reader.ReadInt32();
                    int lowId = reader.ReadInt32();
                    int highId = reader.ReadInt32();
                    int quality = reader.ReadInt32();
                    reader.ReadInt32();
                    entries.Add(new InventoryEntrySnapshot(slot, identityType,
                        identityInstance, lowId, highId, quality, quantity));
                }
                int containerType = reader.ReadInt32();
                int containerInstance = reader.ReadInt32();
                int mainSlot = reader.ReadInt32();
                reader.ReadInt32();
                snapshot = new InventorySnapshot(capacity, containerType,
                    containerInstance, mainSlot, entries);
                return true;
            }
            catch (EndOfStreamException) { return false; }
            catch (InvalidDataException) { return false; }
        }
        private const int ScfuIsNpc = 0x00000001;
        private const int ScfuHasFightingTarget = 0x00000020;
        private const int ScfuHasPlayfieldId = 0x00000040;
        private const int ScfuHasHeading = 0x00000200;
        private const int ScfuHasSmallHealth = 0x00000800;
        private const int ScfuHasExtendedLevel = 0x00001000;
        private const int ScfuHasSmallHealthDamage = 0x00004000;
        private const int ScfuHasSmallNpcFamily = 0x00020000;
        private const int ScfuHasSmallNpcLosHeight = 0x00080000;
        private const int ScfuUnknownDataFlag = 0x02000000;
        private const int ScfuHasOrgName = 0x04000000;
        private const int CharacterHasVisibleName = 0x00400000;
        private const int UserLogin = 0x00000022;
        private const int UserCredentials = 0x00000025;
        private const int SelectCharacter = 0x00000016;
        private const int SystemMessagePacketType = 0x0001;

        public static byte[] CreateUserLogin(string username, string clientVersion)
        {
            using (var body = new MemoryStream())
            {
                WriteInt32(body, UserLogin);
                WriteInt32(body, 2);
                WriteFixedAscii(body, username, 40);
                WriteFixedAscii(body, clientVersion, 20);
                return CreateSystemPacket(1, 1, body.ToArray());
            }
        }

        public static byte[] CreateUserCredentials(string username, string loginKey)
        {
            using (var body = new MemoryStream())
            {
                WriteInt32(body, UserCredentials);
                WriteFixedAscii(body, username, 40);
                WriteInt32(body, loginKey.Length);
                WriteFixedAscii(body, loginKey, loginKey.Length);
                return CreateSystemPacket(2, 1, body.ToArray());
            }
        }

        public static byte[] CreateSelectCharacter(int characterId)
        {
            using (var body = new MemoryStream())
            {
                WriteInt32(body, SelectCharacter);
                WriteInt32(body, characterId);
                return CreateSystemPacket(3, 1, body.ToArray());
            }
        }

        public static byte[] CreateZoneLogin(int characterId, uint cookie1, uint cookie2)
        {
            using (var body = new MemoryStream())
            {
                WriteInt32(body, ZoneLogin);
                WriteInt32(body, characterId);
                WriteInt32(body, unchecked((int)cookie1));
                WriteInt32(body, unchecked((int)cookie2));
                return CreateSystemPacket(1, 1, body.ToArray());
            }
        }

        public static byte[] CreatePlayerMovement(int characterId,
            PlayerMovementUpdate movement)
        {
            if (movement == null)
                throw new ArgumentNullException(nameof(movement));
            using (var body = new MemoryStream())
            {
                WriteInt32(body, CharDCMove);
                WriteInt32(body, 50000);
                WriteInt32(body, characterId);
                body.WriteByte(0); // N3 pass-on marker used by AORebirth replies.
                body.WriteByte(movement.MoveType);
                WriteSingle(body, movement.HeadingX);
                WriteSingle(body, movement.HeadingY);
                WriteSingle(body, movement.HeadingZ);
                WriteSingle(body, movement.HeadingW);
                WriteSingle(body, movement.X);
                WriteSingle(body, movement.Y);
                WriteSingle(body, movement.Z);
                WriteInt32(body, movement.Tick);
                WriteSingle(body, 0f);
                WriteSingle(body, 0f);
                return CreateN3Packet(characterId, body.ToArray());
            }
        }

        public static AORebirthZoneInfo ReadZoneInfo(AORebirthPacket packet)
        {
            if (packet == null || packet.SystemMessageType != ZoneInfo)
                throw new InvalidDataException("The packet is not an AO zone-info message.");

            var reader = new BigEndianReader(packet.Body, 4);
            int characterId = reader.ReadInt32();
            var address = new IPAddress(reader.ReadBytes(4));
            int port = reader.ReadUInt16();
            uint cookie1 = reader.ReadUInt32();
            uint cookie2 = reader.ReadUInt32();
            if (port <= 0)
                throw new InvalidDataException("The AO zone-info message contains an invalid port.");
            return new AORebirthZoneInfo(characterId, address, port, cookie1, cookie2);
        }

        public static bool TryReadPlayfieldBootstrap(
            AORebirthPacket packet,
            out AORebirthPlayfieldBootstrap bootstrap)
        {
            bootstrap = null;
            if (packet == null
                || packet.PacketType != N3PacketType
                || packet.Body.Length < 38)
            {
                return false;
            }

            var reader = new BigEndianReader(packet.Body);
            if (reader.ReadInt32() != PlayfieldAnarchyF)
                return false;

            reader.ReadInt32(); // Playfield2 identity type
            reader.ReadInt32(); // Playfield2 identity instance
            reader.ReadByte(); // N3 unknown
            reader.ReadInt32(); // Unknown1
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            reader.ReadByte(); // Unknown2
            reader.ReadInt32(); // Playfield identity type
            int playfieldId = reader.ReadInt32();
            bootstrap = new AORebirthPlayfieldBootstrap(0, playfieldId, x, y, z);
            return true;
        }

        public static bool TryReadNearbyEntity(AORebirthPacket packet, int defaultPlayfieldId,
            out NearbyEntity entity)
        {
            entity = null;
            if (packet == null || packet.PacketType != N3PacketType || packet.Body.Length < 40)
                return false;
            try
            {
                var reader = new BigEndianReader(packet.Body);
                if (reader.ReadInt32() != SimpleCharFullUpdate)
                    return false;
                int identityType = reader.ReadInt32();
                int identityInstance = reader.ReadInt32();
                reader.Skip(2); // N3 unknown and SCFU version
                int flags = reader.ReadInt32();
                int playfieldId = (flags & ScfuHasPlayfieldId) != 0
                    ? reader.ReadInt32() : defaultPlayfieldId;
                if ((flags & ScfuHasFightingTarget) != 0)
                    reader.Skip(8);
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                if ((flags & ScfuHasHeading) != 0)
                    reader.Skip(16);
                uint appearance = reader.ReadUInt32();
                string name = reader.ReadByteSizedAscii("entity name", 255);
                int characterFlags = reader.ReadInt32();
                reader.Skip(4); // Account flags and expansions

                bool isNpc = (flags & ScfuIsNpc) != 0;
                int npcFamily = 0;
                int npcLosHeight = 0;
                int npcUnknown = 0;
                if (isNpc)
                {
                    npcFamily = (flags & ScfuHasSmallNpcFamily) != 0
                        ? reader.ReadByte() : reader.ReadUInt16();
                    npcLosHeight = (flags & ScfuHasSmallNpcLosHeight) != 0
                        ? reader.ReadByte() : reader.ReadUInt16();
                    npcUnknown = (flags & ScfuUnknownDataFlag) != 0
                        ? reader.ReadByte() : reader.ReadUInt16();
                    if (reader.ReadInt16() > 0)
                        reader.Skip(1);
                }
                else
                {
                    reader.Skip(22); // Nano, team, swim, and six base abilities
                    if ((characterFlags & CharacterHasVisibleName) != 0)
                    {
                        reader.ReadInt16SizedAscii("first name", 1024);
                        reader.ReadInt16SizedAscii("last name", 1024);
                    }
                    if ((flags & ScfuHasOrgName) != 0)
                        reader.ReadInt16SizedAscii("organization name", 2048);
                }

                int level = (flags & ScfuHasExtendedLevel) != 0 ? reader.ReadInt16() : reader.ReadByte();
                int health = (flags & ScfuHasSmallHealth) != 0 ? reader.ReadUInt16() : reader.ReadInt32();
                int healthDamage = (flags & ScfuHasSmallHealthDamage) != 0
                    ? reader.ReadByte()
                    : (flags & ScfuHasSmallHealth) != 0 ? reader.ReadUInt16() : reader.ReadInt32();
                uint monsterData = reader.ReadUInt32();
                int monsterScale = reader.ReadInt16();
                int visualFlags = reader.ReadInt16();
                int visibleTitle = reader.ReadByte();
                NearbyEntityKind kind = isNpc ? NearbyEntityKind.Npc : NearbyEntityKind.Player;
                entity = new NearbyEntity(identityType, identityInstance, kind, name, level,
                    health, healthDamage, playfieldId, x, y, z, appearance,
                    npcFamily, npcLosHeight, npcUnknown, monsterData, monsterScale,
                    visualFlags, visibleTitle);
                return true;
            }
            catch (EndOfStreamException) { return false; }
            catch (InvalidDataException) { return false; }
        }

        public static bool TryReadMovement(AORebirthPacket packet, out AORebirthMovement movement)
        {
            movement = null;
            if (!TryCreateN3Reader(packet, CharDCMove, 43, out BigEndianReader reader))
                return false;
            int type = reader.ReadInt32();
            int instance = reader.ReadInt32();
            reader.ReadByte(); // N3 pass-on marker.
            byte moveType = reader.ReadByte();
            float headingX = reader.ReadSingle();
            float headingY = reader.ReadSingle();
            float headingZ = reader.ReadSingle();
            float headingW = reader.ReadSingle();
            movement = new AORebirthMovement(type, instance,
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                false, 0f, 0f, 0f, moveType, true,
                headingX, headingY, headingZ, headingW);
            return true;
        }

        public static bool TryReadDropPosition(AORebirthPacket packet, out AORebirthMovement movement)
        {
            movement = null;
            if (!TryCreateN3Reader(packet, DropDynel, 24, out BigEndianReader reader))
                return false;
            int type = reader.ReadInt32();
            int instance = reader.ReadInt32();
            movement = new AORebirthMovement(type, instance,
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return true;
        }

        public static bool TryReadMobPathMovement(AORebirthPacket packet,
            out AORebirthMovement movement)
        {
            movement = null;
            if (!TryCreateN3Reader(packet, MobPathMove, 40, out BigEndianReader reader))
                return false;
            int type = reader.ReadInt32();
            int instance = reader.ReadInt32();
            reader.Skip(4); // N3 marker and two-waypoint path header.
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float targetX = reader.ReadSingle();
            float targetY = reader.ReadSingle();
            float targetZ = reader.ReadSingle();
            movement = new AORebirthMovement(type, instance, x, y, z,
                true, targetX, targetY, targetZ);
            return true;
        }

        public static bool TryReadDespawn(AORebirthPacket packet, out AORebirthIdentity identity)
        {
            identity = null;
            if (!TryCreateN3Reader(packet, Despawn, 13, out BigEndianReader reader))
                return false;
            identity = new AORebirthIdentity(reader.ReadInt32(), reader.ReadInt32());
            return true;
        }

        public static bool TryReadHealthStats(AORebirthPacket packet, out AORebirthHealthStats health)
        {
            health = null;
            if (!TryReadStatUpdate(packet, out AORebirthStatUpdate update))
                return false;
            int? maximum = null;
            int? current = null;
            if (update.Values.TryGetValue(1, out int maximumValue)) maximum = maximumValue;
            if (update.Values.TryGetValue(27, out int currentValue)) current = currentValue;
            if (!maximum.HasValue && !current.HasValue)
                return false;
            health = new AORebirthHealthStats(update.Type, update.Instance, maximum, current);
            return true;
        }

        public static bool TryReadStatUpdate(AORebirthPacket packet, out AORebirthStatUpdate update)
        {
            update = null;
            if (!TryCreateN3Reader(packet, Stat, 17, out BigEndianReader reader))
                return false;
            int type = reader.ReadInt32();
            int instance = reader.ReadInt32();
            reader.Skip(1);
            int count = reader.ReadBoundedCount("stat update", 2048);
            var values = new Dictionary<int, int>(count);
            for (int index = 0; index < count; index++)
                values[reader.ReadInt32()] = unchecked((int)reader.ReadUInt32());
            update = new AORebirthStatUpdate(type, instance, values);
            return true;
        }

        public static bool TryReadWorldObject(AORebirthPacket packet, int defaultPlayfieldId,
            out WorldObject worldObject)
        {
            worldObject = null;
            if (packet == null || packet.PacketType != N3PacketType || packet.Body.Length < 13)
                return false;
            try
            {
                var reader = new BigEndianReader(packet.Body);
                int opcode = reader.ReadInt32();
                int identityType = reader.ReadInt32();
                int identityInstance = reader.ReadInt32();
                reader.Skip(1);
                if (opcode == VendingMachineFullUpdate)
                {
                    reader.ReadInt32(); // Type identifier
                    int linkedType = reader.ReadInt32();
                    int linkedInstance = reader.ReadInt32();
                    bool hasPosition = linkedInstance == 0;
                    float x = 0, y = 0, z = 0;
                    if (hasPosition)
                    {
                        x = reader.ReadSingle(); y = reader.ReadSingle(); z = reader.ReadSingle();
                        reader.Skip(16); // Heading
                    }
                    int playfieldId = reader.ReadInt32();
                    reader.Skip(10);
                    int encodedStats = reader.ReadInt32();
                    int statCount = DecodeX3F1Count(encodedStats);
                    reader.Skip(statCount * 8);
                    string name = reader.ReadSizedAscii("vending name", 4096);
                    worldObject = new WorldObject(identityType, identityInstance,
                        WorldObjectKind.VendingMachine, name, playfieldId, hasPosition,
                        x, y, z, linkedType, linkedInstance);
                    return true;
                }
                if (opcode == DoorFullUpdate)
                {
                    worldObject = new WorldObject(identityType, identityInstance,
                        WorldObjectKind.Door, string.Empty, defaultPlayfieldId,
                        false, 0, 0, 0);
                    return true;
                }
                if (opcode == CorpseFullUpdate)
                {
                    worldObject = new WorldObject(identityType, identityInstance,
                        WorldObjectKind.Corpse, string.Empty, defaultPlayfieldId,
                        false, 0, 0, 0);
                    return true;
                }
                return false;
            }
            catch (EndOfStreamException) { return false; }
            catch (InvalidDataException) { return false; }
        }

        private static int DecodeX3F1Count(int encoded)
        {
            if (encoded < 0x03F1 || encoded % 0x03F1 != 0)
                throw new InvalidDataException("Invalid X3F1 array count.");
            int count = encoded / 0x03F1 - 1;
            if (count < 0 || count > 4096)
                throw new InvalidDataException("X3F1 array count is out of range.");
            return count;
        }

        private static bool TryCreateN3Reader(AORebirthPacket packet, int opcode,
            int minimumBodyLength, out BigEndianReader reader)
        {
            reader = null;
            if (packet == null || packet.PacketType != N3PacketType || packet.Body.Length < minimumBodyLength)
                return false;
            reader = new BigEndianReader(packet.Body);
            if (reader.ReadInt32() == opcode)
                return true;
            reader = null;
            return false;
        }

        public static byte[] ReadServerSalt(AORebirthPacket packet)
        {
            if (packet == null || packet.SystemMessageType != ServerSalt || packet.Body.Length < 36)
                throw new InvalidDataException("AORebirth did not return a valid server-salt message.");

            byte[] salt = new byte[32];
            Buffer.BlockCopy(packet.Body, 4, salt, 0, salt.Length);
            return salt;
        }

        public static int ReadLoginError(AORebirthPacket packet)
        {
            if (packet == null || packet.SystemMessageType != LoginError || packet.Body.Length < 8)
                throw new InvalidDataException("The packet is not a valid AO login-error message.");

            return new BigEndianReader(packet.Body, 4).ReadInt32();
        }

        public static IReadOnlyList<CharacterSummary> ReadCharacterList(AORebirthPacket packet)
        {
            if (packet == null || packet.SystemMessageType != CharacterList)
                throw new InvalidDataException("The packet is not an AO character list.");

            var reader = new BigEndianReader(packet.Body, 4);
            int count = reader.ReadBoundedCount("character", 64);
            var characters = new List<CharacterSummary>(count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadInt32(); // Unknown1
                int id = reader.ReadInt32();
                reader.ReadByte(); // PlayfieldProxyVersion
                reader.ReadInt32(); // Playfield identity type
                int playfieldId = reader.ReadInt32();
                reader.ReadInt32(); // PlayfieldAttribute
                reader.ReadInt32(); // ExitDoor
                reader.ReadInt32(); // ExitDoor identity type
                reader.ReadInt32(); // ExitDoor identity instance
                reader.ReadInt32(); // Unknown2
                reader.ReadInt32(); // CharacterInfoVersion
                int characterId = reader.ReadInt32();
                reader.ReadInt32(); // Unknown3
                string name = reader.ReadSizedAscii("character name", 256);
                int breed = reader.ReadInt32();
                int gender = reader.ReadInt32();
                int profession = reader.ReadInt32();
                int level = reader.ReadInt32();
                string areaName = reader.ReadSizedAscii("area name", 512);
                for (int unknown = 0; unknown < 5; unknown++)
                    reader.ReadInt32();
                int status = reader.ReadInt32();

                int stableId = characterId != 0 ? characterId : id;
                characters.Add(new CharacterSummary(
                    stableId.ToString(CultureInfo.InvariantCulture),
                    name,
                    level,
                    profession,
                    breed,
                    playfieldId,
                    gender,
                    areaName,
                    status));
            }

            reader.ReadInt32(); // AllowedCharacters
            reader.ReadInt32(); // Expansions
            return characters;
        }

        private static byte[] CreateSystemPacket(ushort messageId, int receiver, byte[] body)
        {
            int size = 16 + body.Length;
            int paddedSize = (size + 3) & ~3;
            using (var packet = new MemoryStream())
            {
                WriteUInt16(packet, messageId);
                WriteInt16(packet, SystemMessagePacketType);
                WriteInt16(packet, 1);
                WriteInt16(packet, size);
                WriteInt32(packet, 0);
                WriteInt32(packet, receiver);
                packet.Write(body, 0, body.Length);
                while (packet.Length < paddedSize)
                    packet.WriteByte(0);
                return packet.ToArray();
            }
        }

        private static byte[] CreateN3Packet(int sender, byte[] body)
        {
            int size = 16 + body.Length;
            using (var packet = new MemoryStream())
            {
                WriteUInt16(packet, 0xDFDF);
                WriteInt16(packet, N3PacketType);
                WriteInt16(packet, 1);
                WriteInt16(packet, size);
                WriteInt32(packet, sender);
                WriteInt32(packet, 0);
                packet.Write(body, 0, body.Length);
                return packet.ToArray();
            }
        }

        private static void WriteFixedAscii(Stream stream, string value, int length)
        {
            byte[] bytes = new byte[length];
            int count = Math.Min(value.Length, length);
            Encoding.ASCII.GetBytes(value, 0, count, bytes, 0);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUInt16(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteInt16(Stream stream, int value) => WriteUInt16(stream, unchecked((ushort)value));

        private static void WriteInt32(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteSingle(Stream stream, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    internal sealed class AORebirthPacket
    {
        public AORebirthPacket(int packetType, int systemMessageType, byte[] body)
        {
            PacketType = packetType;
            SystemMessageType = systemMessageType;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public int PacketType { get; }
        public int SystemMessageType { get; }
        public byte[] Body { get; }
    }

    internal sealed class BigEndianReader
    {
        private readonly byte[] _buffer;
        private int _offset;

        public BigEndianReader(byte[] buffer, int offset = 0)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _offset = offset;
        }

        public int Remaining => _buffer.Length - _offset;

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return _buffer[_offset++];
        }

        public byte[] ReadBytes(int count)
        {
            EnsureAvailable(count);
            byte[] value = new byte[count];
            Buffer.BlockCopy(_buffer, _offset, value, 0, count);
            _offset += count;
            return value;
        }

        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            ushort value = (ushort)((_buffer[_offset] << 8) | _buffer[_offset + 1]);
            _offset += 2;
            return value;
        }

        public short ReadInt16() => unchecked((short)ReadUInt16());

        public uint ReadUInt32()
        {
            return unchecked((uint)ReadInt32());
        }

        public float ReadSingle()
        {
            uint bits = ReadUInt32();
            byte[] bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }

        public int ReadInt32()
        {
            EnsureAvailable(4);
            int value = (_buffer[_offset] << 24)
                        | (_buffer[_offset + 1] << 16)
                        | (_buffer[_offset + 2] << 8)
                        | _buffer[_offset + 3];
            _offset += 4;
            return value;
        }

        public int ReadBoundedCount(string name, int maximum)
        {
            int value = ReadInt32();
            if (value < 0 || value > maximum)
                throw new InvalidDataException($"Invalid {name} count: {value}.");
            return value;
        }

        public string ReadSizedAscii(string name, int maximumLength)
        {
            int length = ReadInt32();
            if (length < 0 || length > maximumLength)
                throw new InvalidDataException($"Invalid {name} length: {length}.");
            EnsureAvailable(length);
            string value = Encoding.ASCII.GetString(_buffer, _offset, length).TrimEnd('\0');
            _offset += length;
            return value;
        }

        public string ReadByteSizedAscii(string name, int maximumLength)
        {
            return ReadAscii(name, ReadByte(), maximumLength);
        }

        public string ReadInt16SizedAscii(string name, int maximumLength)
        {
            return ReadAscii(name, ReadInt16(), maximumLength);
        }

        public void Skip(int count)
        {
            EnsureAvailable(count);
            _offset += count;
        }

        private string ReadAscii(string name, int length, int maximumLength)
        {
            if (length < 0 || length > maximumLength)
                throw new InvalidDataException($"Invalid {name} length: {length}.");
            EnsureAvailable(length);
            string value = Encoding.ASCII.GetString(_buffer, _offset, length).TrimEnd('\0');
            _offset += length;
            return value;
        }

        private void EnsureAvailable(int count)
        {
            if (count < 0 || _offset > _buffer.Length - count)
                throw new EndOfStreamException("The AO packet ended unexpectedly.");
        }
    }

    internal sealed class AORebirthZoneInfo
    {
        public AORebirthZoneInfo(int characterId, IPAddress address, int port, uint cookie1, uint cookie2)
        {
            CharacterId = characterId;
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Port = port;
            Cookie1 = cookie1;
            Cookie2 = cookie2;
        }

        public int CharacterId { get; }
        public IPAddress Address { get; }
        public int Port { get; }
        public uint Cookie1 { get; }
        public uint Cookie2 { get; }
    }

    internal sealed class AORebirthPlayfieldBootstrap
    {
        public AORebirthPlayfieldBootstrap(int characterId, int playfieldId, float x, float y, float z)
        {
            CharacterId = characterId;
            PlayfieldId = playfieldId;
            X = x;
            Y = y;
            Z = z;
        }

        public int CharacterId { get; }
        public int PlayfieldId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    internal class AORebirthIdentity
    {
        public AORebirthIdentity(int type, int instance) { Type = type; Instance = instance; }
        public int Type { get; }
        public int Instance { get; }
    }

    internal sealed class AORebirthMovement : AORebirthIdentity
    {
        public AORebirthMovement(int type, int instance, float x, float y, float z,
            bool hasDestination = false, float destinationX = 0f,
            float destinationY = 0f, float destinationZ = 0f,
            byte moveType = 0, bool hasHeading = false,
            float headingX = 0f, float headingY = 0f,
            float headingZ = 0f, float headingW = 1f)
            : base(type, instance)
        {
            X = x; Y = y; Z = z;
            HasDestination = hasDestination;
            DestinationX = destinationX;
            DestinationY = destinationY;
            DestinationZ = destinationZ;
            MoveType = moveType;
            HasHeading = hasHeading;
            HeadingX = headingX; HeadingY = headingY;
            HeadingZ = headingZ; HeadingW = headingW;
        }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public bool HasDestination { get; }
        public float DestinationX { get; }
        public float DestinationY { get; }
        public float DestinationZ { get; }
        public byte MoveType { get; }
        public bool HasHeading { get; }
        public float HeadingX { get; }
        public float HeadingY { get; }
        public float HeadingZ { get; }
        public float HeadingW { get; }
    }

    internal sealed class AORebirthHealthStats : AORebirthIdentity
    {
        public AORebirthHealthStats(int type, int instance, int? maximum, int? current)
            : base(type, instance) { Maximum = maximum; Current = current; }
        public int? Maximum { get; }
        public int? Current { get; }
    }

    internal sealed class AORebirthStatUpdate : AORebirthIdentity
    {
        public AORebirthStatUpdate(int type, int instance,
            IReadOnlyDictionary<int, int> values) : base(type, instance)
        {
            Values = values ?? new Dictionary<int, int>();
        }
        public IReadOnlyDictionary<int, int> Values { get; }
    }
}
