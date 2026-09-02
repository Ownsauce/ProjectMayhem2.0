using System;

namespace AO.Client.Characters
{
    public sealed class CharacterSummary
    {
        public CharacterSummary(
            string id,
            string name,
            int level,
            int professionId,
            int breedId,
            int playfieldId = 0,
            int genderId = 0,
            string areaName = "",
            int status = 0)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A character ID is required.", nameof(id));

            Id = id;
            Name = name ?? string.Empty;
            Level = level;
            ProfessionId = professionId;
            BreedId = breedId;
            PlayfieldId = playfieldId;
            GenderId = genderId;
            AreaName = areaName ?? string.Empty;
            Status = status;
        }

        // Kept opaque because identity formats differ between server implementations.
        public string Id { get; }

        public string Name { get; }

        public int Level { get; }

        public int ProfessionId { get; }

        public int BreedId { get; }

        public int PlayfieldId { get; }

        public int GenderId { get; }

        public string AreaName { get; }

        public int Status { get; }
    }
}
