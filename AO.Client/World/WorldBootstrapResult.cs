namespace AO.Client.World
{
    public sealed class WorldBootstrapResult
    {
        public WorldBootstrapResult(
            string characterId,
            int playfieldId,
            float x,
            float y,
            float z,
            int packetsObserved)
        {
            CharacterId = characterId ?? string.Empty;
            PlayfieldId = playfieldId;
            X = x;
            Y = y;
            Z = z;
            PacketsObserved = packetsObserved;
        }

        public string CharacterId { get; }
        public int PlayfieldId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int PacketsObserved { get; }
    }
}
