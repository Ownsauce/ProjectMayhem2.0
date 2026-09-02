namespace AO.Client.World
{
    public sealed class PlayerMovementUpdate
    {
        public PlayerMovementUpdate(float x, float y, float z,
            float headingX, float headingY, float headingZ, float headingW,
            byte moveType, int tick)
        {
            X = x; Y = y; Z = z;
            HeadingX = headingX; HeadingY = headingY;
            HeadingZ = headingZ; HeadingW = headingW;
            MoveType = moveType;
            Tick = tick;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float HeadingX { get; }
        public float HeadingY { get; }
        public float HeadingZ { get; }
        public float HeadingW { get; }
        public byte MoveType { get; }
        public int Tick { get; }
    }
}
