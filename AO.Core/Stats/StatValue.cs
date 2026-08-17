namespace AO.Core.Stats
{
    public class StatValue
    {
        public int StatId { get; }
        public int BaseValue { get; set; }
        public int ModifiedValue { get; set; }

        public StatValue(int statId)
        {
            StatId = statId;
        }
    }
}
