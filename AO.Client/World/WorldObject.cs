namespace AO.Client.World
{
    public enum WorldObjectKind { Door, VendingMachine, Corpse, Chest }

    public sealed class WorldObject
    {
        public WorldObject(int identityType, int identityInstance, WorldObjectKind kind,
            string name, int playfieldId, bool hasPosition, float x, float y, float z,
            int linkedIdentityType = 0, int linkedIdentityInstance = 0)
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

        public WorldObject WithLinkedPosition(NearbyEntity linked)
        {
            if (linked == null) return this;
            return new WorldObject(IdentityType, IdentityInstance, Kind, Name,
                linked.PlayfieldId, true, linked.X, linked.Y, linked.Z,
                LinkedIdentityType, LinkedIdentityInstance);
        }
    }
}
