namespace AO.Core.Stats
{
    public interface IStatProvider
    {
        int GetBaseStat(int statId);
        int GetModifiedStat(int statId);
    }
}
