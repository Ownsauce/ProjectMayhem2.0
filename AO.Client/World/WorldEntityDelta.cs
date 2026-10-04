namespace AO.Client.World
{
    public enum WorldEntityDeltaKind { Upsert, Movement, Stats, Remove, Appearance }

    public sealed class WorldEntityDelta
    {
        public WorldEntityDelta(WorldEntityDeltaKind kind, int identityType,
            int identityInstance, NearbyEntity entity,
            bool hasMovementTarget = false, float targetX = 0f,
            float targetY = 0f, float targetZ = 0f,
            byte moveType = 0, bool hasHeading = false,
            float headingX = 0f, float headingY = 0f,
            float headingZ = 0f, float headingW = 1f)
        {
            Kind = kind;
            IdentityType = identityType;
            IdentityInstance = identityInstance;
            Entity = entity;
            HasMovementTarget = hasMovementTarget;
            TargetX = targetX;
            TargetY = targetY;
            TargetZ = targetZ;
            MoveType = moveType;
            HasHeading = hasHeading;
            HeadingX = headingX;
            HeadingY = headingY;
            HeadingZ = headingZ;
            HeadingW = headingW;
        }

        public WorldEntityDeltaKind Kind { get; }
        public int IdentityType { get; }
        public int IdentityInstance { get; }
        public NearbyEntity Entity { get; }
        public bool HasMovementTarget { get; }
        public float TargetX { get; }
        public float TargetY { get; }
        public float TargetZ { get; }
        public byte MoveType { get; }
        public bool HasHeading { get; }
        public float HeadingX { get; }
        public float HeadingY { get; }
        public float HeadingZ { get; }
        public float HeadingW { get; }
    }
}
