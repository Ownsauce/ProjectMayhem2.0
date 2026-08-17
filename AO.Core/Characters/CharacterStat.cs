namespace AO.Core.Characters
{
    public class CharacterStat
    {
        public string Name { get; }
        public int Value { get; private set; }

        public CharacterStat(string name, int initialValue)
        {
            Name = name;
            Value = initialValue;
        }

        public void Increase(int amount)
        {
            Value += amount;
        }

        public void SetValue(int value)
        {
            Value = value;
        }
    }
}
