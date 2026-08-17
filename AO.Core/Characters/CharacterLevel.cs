namespace AO.Core.Characters
{
    public class CharacterLevel
    {
        public int Level { get; private set; } = 1;
        // XP progress within the current level.
        public long Experience { get; private set; }
        // Lifetime XP earned.
        public long TotalExperience { get; private set; }

        public void AddExperience(long amount)
        {
            if (amount <= 0)
                return;

            Experience += amount;
            TotalExperience += amount;
        }

        public void SetExperience(long amount)
        {
            Experience = amount < 0 ? 0 : amount;
        }

        public void SetTotalExperience(long amount)
        {
            TotalExperience = amount < 0 ? 0 : amount;
        }

        public void LevelUp()
        {
            Level++;
        }

        public void SetLevel(int level)
        {
            Level = level < 1 ? 1 : level;
        }
    }
}
