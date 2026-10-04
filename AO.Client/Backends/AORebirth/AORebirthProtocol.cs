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
    internal static partial class AORebirthProtocol
    {
        public const int ServerSalt = 0x00000024;
        public const int LoginError = 0x0000000D;
        public const int CharacterList = 0x0000000E;
        public const int ZoneInfo = 0x00000017;
        public const int ZoneLogin = 0x0000001B;
        public const int InitiateCompressionPacketType = 0x7F00;
        public const int TextMessagePacketType = 0x0005;
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
        public const int ChatText = 0x5F4B442A;
        public const int ZoneRedirection = 0x0000003C;

        public static bool TryReadZoneRedirection(AORebirthPacket packet,
            out IPAddress address, out int port)
        {
            address = null;
            port = 0;
            if (packet == null || packet.SystemMessageType != ZoneRedirection
                || packet.Body.Length < 10)
                return false;
            try
            {
                var reader = new BigEndianReader(packet.Body, 4);
                address = new IPAddress(reader.ReadBytes(4));
                port = reader.ReadUInt16();
                return port > 0;
            }
            catch (EndOfStreamException) { address = null; port = 0; return false; }
        }

        public static bool TryReadChatText(AORebirthPacket packet, out string text)
        {
            text = null;
            if (packet == null || packet.PacketType != N3PacketType || packet.Body.Length < 16)
                return false;
            try
            {
                var reader = new BigEndianReader(packet.Body);
                if (reader.ReadInt32() != ChatText) return false;
                reader.Skip(8); // N3 sender identity
                reader.ReadByte(); // N3 pass-on marker
                text = reader.ReadInt16SizedAscii("chat text", 32767);
                return true;
            }
            catch (EndOfStreamException) { text = null; return false; }
            catch (InvalidDataException) { text = null; return false; }
        }

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
        private const int ScfuHasExtendedTextures = 0x00000010;
        private const int ScfuHasFightingTarget = 0x00000020;
        private const int ScfuHasPlayfieldId = 0x00000040;
        private const int ScfuHasHeading = 0x00000200;
        private const int ScfuHasSmallHealth = 0x00000800;
        private const int ScfuHasExtendedLevel = 0x00001000;
        private const int ScfuHasSmallHealthDamage = 0x00004000;
        private const int ScfuHasWaypoints = 0x00010000;
        private const int ScfuHasSmallNpcFamily = 0x00020000;
        private const int ScfuHasSmallNpcLosHeight = 0x00080000;
        private const int ScfuUnknownDataFlag = 0x02000000;
        private const int ScfuHasOrgName = 0x04000000;
        private const int ScfuIsImmune = 0x00800000;
        private const int ScfuUnknownFlag3 = 0x01000000;
        private const int CharacterHasVisibleName = 0x00400000;
        private const int CharacterIsTower = 0x00020000;
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
                // Retail 18.8.62 uses receiver 2 on the zone socket. Keep the
                // header sender equal to the selected character in the body.
                return CreateSystemPacket(1, 2, body.ToArray(), characterId);
            }
        }

        public static byte[] CreateCharInPlay(int characterId)
        {
            using var body = new MemoryStream();
            WriteInt32(body, 0x570C2039);
            WriteInt32(body, 50000);
            WriteInt32(body, characterId);
            body.WriteByte(0);
            return CreateN3Packet(characterId, body.ToArray());
        }

        public static byte[] CreatePong(AORebirthPacket ping, int characterId)
        {
            if (ping.PacketType != 0xB || ping.Body.Length != 24
                || new BigEndianReader(ping.Body).ReadInt32() != 1) return null;
            using var packet = new MemoryStream();
            WriteUInt16(packet, 1); WriteInt16(packet, 0xB);
            WriteInt16(packet, 1); WriteInt16(packet, 40);
            WriteInt32(packet, characterId); WriteInt32(packet, ping.Sender);
            WriteInt32(packet, 2); WriteInt32(packet, 0);
            packet.Write(ping.Body, 8, 16);
            return packet.ToArray();
        }

        internal static int ItemPlacement(ItemLocation location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            return location.Area == ItemArea.Inventory ? 0x40 + location.Index
                : ((int)location.Area * 0x10) + 1 + location.Index;
        }

        private static int ItemPage(int slot) => slot >= 0x40 ? 104
            : slot >= 0x30 ? 115 : slot >= 0x20 ? 103 : slot >= 0x10 ? 102 : 101;

        private static bool IsItemPlacement(int slot) => (slot >= 0x40 && slot <= 0x5d)
            || (slot > 0 && slot < 0x40 && (slot & 15) != 0);

        public static byte[] CreateItemMove(int characterId, ItemLocation source, ItemLocation destination)
        {
            int from = ItemPlacement(source), to = ItemPlacement(destination);
            if (from == to) throw new ArgumentException("Source and destination are the same slot.");
            // Captured AO unequip requests use the inventory destination marker 0x6F,
            // not a concrete 0x40..0x5D slot. The server selects a free slot and its
            // ContainerAddItem acknowledgement reports the resolved destination.
            int wireTarget = source.Area != ItemArea.Inventory
                && destination.Area == ItemArea.Inventory ? 0x6F : to;
            using (var body = new MemoryStream())
            {
                WriteInt32(body, 0x5469373f); // ClientMoveItemToInventory
                WriteInt32(body, 50000); WriteInt32(body, characterId); body.WriteByte(0);
                WriteInt32(body, ItemPage(from)); WriteInt32(body, from); WriteInt32(body, wireTarget);
                return CreateN3Packet(characterId, body.ToArray());
            }
        }

        public static bool TryApplyItemMove(AORebirthPacket packet, int ownerId,
            InventorySnapshot inventory, CharacterStateSnapshot character,
            out InventorySnapshot updatedInventory, out CharacterStateSnapshot updatedCharacter)
        {
            updatedInventory = null; updatedCharacter = null;
            if (inventory == null || character == null
                || !TryCreateN3Reader(packet, 0x47537a24, 33, out BigEndianReader reader)) return false;
            try
            {
                // ContainerAddItem confirms the actual source and destination chosen by the server.
                var header = new BigEndianReader(packet.Body, 4);
                if (header.ReadInt32() != 50000 || header.ReadInt32() != ownerId) return false;
                reader.Skip(9); // sender identity and pass-on marker
                int page = reader.ReadInt32(), from = reader.ReadInt32();
                if (reader.ReadInt32() != 50000 || reader.ReadInt32() != ownerId) return false;
                int to = reader.ReadInt32();
                if (!IsItemPlacement(from) || !IsItemPlacement(to) || from == to || ItemPage(from) != page)
                    return false;
                var slots = new Dictionary<int, InventoryEntrySnapshot>();
                foreach (var entry in character.Slots)
                    if (entry != null && entry.Slot < 0x40) slots[entry.Slot] = entry;
                foreach (var entry in inventory.Entries)
                    if (entry != null) slots[entry.Slot >= 0x40 ? entry.Slot : entry.Slot + 0x40] = entry;
                if (!slots.TryGetValue(from, out var moving)) return false;
                slots.TryGetValue(to, out var displaced);
                // Only equipment destinations swap. Inventory confirmations name an empty slot.
                if (to >= 0x40 && displaced != null) return false;
                slots.Remove(from);
                slots[to] = WithSlot(moving, to);
                if (displaced != null) slots[from] = WithSlot(displaced, from);
                var worn = new List<InventoryEntrySnapshot>();
                var carried = new List<InventoryEntrySnapshot>();
                foreach (var entry in slots.Values)
                    if (entry.Slot >= 0x40) carried.Add(entry); else worn.Add(entry);
                updatedInventory = new InventorySnapshot(inventory.Capacity, inventory.ContainerType,
                    inventory.ContainerInstance, inventory.MainInventorySlot, carried);
                updatedCharacter = new CharacterStateSnapshot(worn, character.UploadedNanoIds, character.Stats);
                return true;
            }
            catch (EndOfStreamException) { return false; }
        }

        private static InventoryEntrySnapshot WithSlot(InventoryEntrySnapshot item, int slot) =>
            new InventoryEntrySnapshot(slot, item.IdentityType, item.IdentityInstance,
                item.LowId, item.HighId, item.Quality, item.Quantity);

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
                    if ((flags & ScfuUnknownDataFlag) != 0)
                    {
                        npcUnknown = reader.ReadByte();
                        reader.ReadInt16();
                    }
                }
                else
                {
                    reader.Skip(22); // Nano, team, swim, and six base abilities
                    if ((flags & ScfuHasOrgName) != 0)
                        reader.Skip(5); // Organization identity and rank/unknown byte.
                    if ((characterFlags & CharacterHasVisibleName) != 0)
                    {
                        reader.ReadByteSizedAscii("first name", 255);
                        reader.ReadByteSizedAscii("last name", 255);
                    }
                    if ((flags & ScfuHasOrgName) != 0)
                        reader.ReadByteSizedAscii("organization name", 255);
                }

                if ((characterFlags & CharacterIsTower) != 0)
                    reader.Skip(1);

                int level = (flags & ScfuHasExtendedLevel) != 0 ? reader.ReadInt16() : reader.ReadByte();
                int health = (flags & ScfuHasSmallHealth) != 0 ? reader.ReadUInt16() : reader.ReadInt32();
                int healthDamage = (flags & ScfuHasSmallHealthDamage) != 0
                    ? reader.ReadByte()
                    : (flags & ScfuHasSmallHealth) != 0 ? reader.ReadUInt16() : reader.ReadInt32();
                uint monsterData = reader.ReadUInt32();
                int monsterScale = reader.ReadInt16();
                int visualFlags = reader.ReadInt16();
                int visibleTitle = reader.ReadByte();
                CharacterAppearanceSnapshot equipmentAppearance = null;
                if (reader.Remaining > 0)
                {
                    reader.Skip(reader.ReadBoundedCount("SCFU unknown data", reader.Remaining));
                    int? head = (flags & 0x80) != 0 ? reader.ReadInt32() : (int?)null;
                    reader.Skip((flags & 0x2000) != 0 ? 2 : 1); // Run speed
                    if ((flags & 0x400) != 0) reader.Skip(8); // Attacker
                    if ((flags & ScfuHasExtendedTextures) != 0)
                        reader.Skip(checked(ReadX3F1Count(reader, "extended textures", 4096) * 44));
                    if ((flags & ScfuIsImmune) != 0) reader.Skip(1);
                    if ((flags & ScfuUnknownFlag3) != 0) reader.Skip(1);
                    int nanoCount = ReadX3F1Count(reader, "active nanos", 4096);
                    reader.Skip(checked(nanoCount * 20));
                    if ((flags & ScfuHasWaypoints) != 0)
                    {
                        reader.Skip(8); // Waypoint owner identity.
                        reader.Skip(checked(reader.ReadBoundedCount("waypoints", 4096) * 12));
                    }
                    ReadAppearanceArrays(reader, out var textures, out var meshes);
                    int flags2 = reader.ReadInt32();
                    if ((flags2 & 4) != 0) reader.Skip(4); // Owner instance; type is SimpleChar.
                    reader.Skip(1); // SCFU trailing unknown byte.
                    if ((flags2 & 0x40) != 0)
                    {
                        int attacks = reader.ReadByte();
                        for (int index = 0; index < attacks; index++)
                        {
                            short marker = reader.ReadInt16();
                            if (marker != 0) reader.Skip(14); // Four shorts, 4-byte name, final short.
                        }
                    }
                    if (reader.Remaining != 0)
                        throw new InvalidDataException($"Unexpected SCFU trailing bytes: {reader.Remaining}.");
                    equipmentAppearance = new CharacterAppearanceSnapshot(
                        textures, meshes, visualFlags, head);
                }
                NearbyEntityKind kind = isNpc ? NearbyEntityKind.Npc : NearbyEntityKind.Player;
                entity = new NearbyEntity(identityType, identityInstance, kind, name, level,
                    health, healthDamage, playfieldId, x, y, z, appearance,
                    npcFamily, npcLosHeight, npcUnknown, monsterData, monsterScale,
                    visualFlags, visibleTitle, equipmentAppearance);
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

        private static byte[] CreateSystemPacket(ushort messageId, int receiver, byte[] body, int sender = 0)
        {
            int size = 16 + body.Length;
            int paddedSize = (size + 3) & ~3;
            using (var packet = new MemoryStream())
            {
                WriteUInt16(packet, messageId);
                WriteInt16(packet, SystemMessagePacketType);
                WriteInt16(packet, 1);
                WriteInt16(packet, size);
                WriteInt32(packet, sender);
                WriteInt32(packet, receiver);
                packet.Write(body, 0, body.Length);
                while (packet.Length < paddedSize)
                    packet.WriteByte(0);
                return packet.ToArray();
            }
        }

        public static byte[] CreateTextMessage(int sender, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Chat text is required.", nameof(text));
            byte[] encoded = Encoding.UTF8.GetBytes(text);
            if (encoded.Length > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(text), "Chat text is too long.");

            using (var body = new MemoryStream())
            {
                WriteInt32(body, 3); // TextMessageRange.Say
                WriteInt32(body, 0);
                WriteInt32(body, 0);
                WriteInt32(body, 0);
                WriteInt16(body, encoded.Length);
                body.Write(encoded, 0, encoded.Length);
                body.WriteByte(0); // ChatMessageType.Say
                return CreateFramedPacket(TextMessagePacketType, sender, 0, body.ToArray(), true);
            }
        }

        private static byte[] CreateFramedPacket(int packetType, int sender, int receiver,
            byte[] body, bool padToFourBytes)
        {
            int size = 16 + body.Length;
            int wireSize = padToFourBytes ? (size + 3) & ~3 : size;
            using (var packet = new MemoryStream())
            {
                WriteUInt16(packet, 0xDFDF);
                WriteInt16(packet, packetType);
                WriteInt16(packet, 1);
                WriteInt16(packet, size);
                WriteInt32(packet, sender);
                WriteInt32(packet, receiver);
                packet.Write(body, 0, body.Length);
                while (packet.Length < wireSize) packet.WriteByte(0);
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
        public AORebirthPacket(int packetType, int systemMessageType, byte[] body, int sender = 0)
        {
            PacketType = packetType;
            Sender = sender;
            SystemMessageType = systemMessageType;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public int PacketType { get; }
        public int Sender { get; }
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
