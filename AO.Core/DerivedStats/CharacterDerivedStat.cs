namespace AO.Core.DerivedStats
{
    public class CharacterDerivedStat
    {
        public DerivedStatDefinition Definition { get; }
        public int BaseValue { get; private set; } = 0;
        public int FinalValue { get; private set; } = 0;

        public CharacterDerivedStat(DerivedStatDefinition def)
        {
            Definition = def;
        }

        public void SetBase(int value)
        {
            BaseValue = value;
        }

        public void SetFinal(int value)
        {
            FinalValue = value;
        }
    }
}
