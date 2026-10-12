using System;

namespace AO.Client.Characters
{
    public sealed class CharacterCreationRequest
    {
        public CharacterCreationRequest(string name, int breedId, int genderId,
            int professionId, int headMeshId, int monsterScale = 100, int fatness = 1,
            bool startInShadowlands = false)
        {
            Name = (name ?? string.Empty).Trim();
            if (Name.Length < 3 || Name.Length > 13)
                throw new ArgumentException("Character names must contain 3–13 letters.", nameof(name));
            foreach (char letter in Name)
                if (!((letter >= 'a' && letter <= 'z') || (letter >= 'A' && letter <= 'Z')))
                    throw new ArgumentException("Character names must contain letters only.", nameof(name));
            if (breedId < 1 || breedId > 4) throw new ArgumentOutOfRangeException(nameof(breedId));
            if (genderId < 1 || genderId > 3 || (breedId == 4 ? genderId != 1 : genderId == 1))
                throw new ArgumentException("Choose a valid gender for this breed.", nameof(genderId));
            if (professionId < 1 || professionId > 15 || professionId == 13)
                throw new ArgumentOutOfRangeException(nameof(professionId));
            if (headMeshId <= 0) throw new ArgumentException("Choose a head before creating a character.", nameof(headMeshId));
            if (monsterScale < 80 || monsterScale > 120) throw new ArgumentOutOfRangeException(nameof(monsterScale));
            if (fatness < 0 || fatness > 2) throw new ArgumentOutOfRangeException(nameof(fatness));
            BreedId = breedId; GenderId = genderId; ProfessionId = professionId;
            HeadMeshId = headMeshId; MonsterScale = monsterScale; Fatness = fatness;
            StartInShadowlands = startInShadowlands;
        }

        public string Name { get; }
        public int BreedId { get; }
        public int GenderId { get; }
        public int ProfessionId { get; }
        public int HeadMeshId { get; }
        public int MonsterScale { get; }
        public int Fatness { get; }
        public bool StartInShadowlands { get; }
    }

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
