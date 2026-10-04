using System;
using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World.Procedural
{
    [CreateAssetMenu(fileName = "TempleDungeonKit", menuName = "AO/World Generation/Temple Dungeon Kit")]
    public sealed class TempleDungeonKit : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public GameObject Prefab;
            public Vector3 SizeMetres;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries.AsReadOnly();

        public GameObject Resolve(string id)
        {
            foreach (Entry entry in entries)
                if (entry != null && entry.Id == id) return entry.Prefab;
            return null;
        }

        public void ReplaceEntries(IEnumerable<Entry> replacement)
        {
            entries.Clear();
            if (replacement != null) entries.AddRange(replacement);
        }
    }
}
