using System.Collections.Generic;

namespace AO.Data.Core
{
    public class StatRegistry
    {
        private Dictionary<int, string> _map = new Dictionary<int, string>();
        public int Count => _map.Count;

        public void Register(int id, string name)
        {
            _map[id] = name;
        }

        public string Get(int id)
        {
            return _map.TryGetValue(id, out var name)
                ? name
                : $"UnknownStat({id})";
        }
    }
}
