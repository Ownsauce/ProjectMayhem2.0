namespace AO.Client.World
{
    public sealed class WorldEntryResult
    {
        private WorldEntryResult(bool succeeded, string characterId, int playfieldId, string message)
        {
            Succeeded = succeeded;
            CharacterId = characterId ?? string.Empty;
            PlayfieldId = playfieldId;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public string CharacterId { get; }

        public int PlayfieldId { get; }

        public string Message { get; }

        public static WorldEntryResult Success(string characterId, int playfieldId, string message = "") =>
            new WorldEntryResult(true, characterId, playfieldId, message);

        public static WorldEntryResult Failure(string message) =>
            new WorldEntryResult(false, string.Empty, 0, message);
    }
}
