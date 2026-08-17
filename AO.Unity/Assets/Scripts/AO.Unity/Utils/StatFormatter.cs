using AO.Data.Core;
using AO.Data.Unity;

namespace AO.Unity
{
    public static class StatFormatter
    {
        public static string GetName(int statId)
        {
            return AODataManager.Instance.GetStatName(statId);
        }
    }
}
