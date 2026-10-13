namespace AO.Client.World
{
    public enum WorldObjectKind { Door, VendingMachine, Corpse, Chest }

    public sealed class WorldObject
    {
        public WorldObject(int identityType, int identityInstance, WorldObjectKind kind,
            string name, int playfieldId, bool hasPosition, float x, float y, float z,
            int linkedIdentityType = 0, int linkedIdentityInstance = 0,
            NearbyEntity corpseAppearance = null,
            float headingX = 0, float headingY = 0, float headingZ = 0, float headingW = 1)
        {
            IdentityType = identityType;
            IdentityInstance = identityInstance;
            Kind = kind;
            Name = name ?? string.Empty;
            PlayfieldId = playfieldId;
            HasPosition = hasPosition;
            X = x; Y = y; Z = z;
            LinkedIdentityType = linkedIdentityType;
            LinkedIdentityInstance = linkedIdentityInstance;
            CorpseAppearance = corpseAppearance;
            HeadingX = headingX; HeadingY = headingY; HeadingZ = headingZ; HeadingW = headingW;
        }

        public int IdentityType { get; }
        public int IdentityInstance { get; }
        public WorldObjectKind Kind { get; }
        public string Name { get; }
        public int PlayfieldId { get; }
        public bool HasPosition { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int LinkedIdentityType { get; }
        public int LinkedIdentityInstance { get; }
        public NearbyEntity CorpseAppearance { get; }
        public float HeadingX { get; }
        public float HeadingY { get; }
        public float HeadingZ { get; }
        public float HeadingW { get; }

        public WorldObject WithLinkedPosition(NearbyEntity linked)
        {
            if (linked == null) return this;
            return new WorldObject(IdentityType, IdentityInstance, Kind, Name,
                linked.PlayfieldId, true, linked.X, linked.Y, linked.Z,
                LinkedIdentityType, LinkedIdentityInstance, CorpseAppearance, HeadingX, HeadingY, HeadingZ, HeadingW);
        }
    }
}
