namespace AO.Core.Skills
{
    public class CharacterSkill
    {
        public SkillDefinition Definition { get; }

        public int BaseValue { get; private set; }

        public int FinalValue { get; private set; }

        public CharacterSkill(SkillDefinition definition, int baseValue = 0)
        {
            Definition = definition;
            BaseValue = baseValue;
        }

        public void IncreaseBase(int amount)
        {
            BaseValue += amount;
        }

        public void SetFinalValue(int value)
        {
            FinalValue = value;
        }
    }
}
