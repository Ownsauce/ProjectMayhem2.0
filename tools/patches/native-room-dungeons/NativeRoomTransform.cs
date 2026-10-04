namespace WorldGen.Dungeons
{
    /// <summary>Source metres to placed metres without quantizing collision vertices.</summary>
    public static class NativeRoomTransform
    {
        public static void SourcePoint(NativeRoomTemplate template, NativeRoomPlacement placement,
            float x, float y, float z, out float worldX, out float worldY, out float worldZ)
        {
            x -= template.OriginX * .001f; z -= template.OriginZ * .001f;
            float rx = x, rz = z;
            switch (placement.QuarterTurns & 3)
            { case 1: rx = z; rz = -x; break; case 2: rx = -x; rz = -z; break; case 3: rx = -z; rz = x; break; }
            worldX = placement.X * .001f + rx;
            worldY = placement.Y * .001f + y - template.OriginY * .001f;
            worldZ = placement.Z * .001f + rz;
        }
    }
}
