namespace AO.Core.Modifiers
{
    /// <summary>
    /// Simple stat modifier structure
    /// </summary>
    public struct StatModifier
    {
        public int StatId;
        public int Value;

        public StatModifier(int statId, int value)
        {
            StatId = statId;
            Value = value;
        }
    }
}
