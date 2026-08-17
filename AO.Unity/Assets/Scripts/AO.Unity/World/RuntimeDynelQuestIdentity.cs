using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World
{
    public class RuntimeDynelQuestIdentity : MonoBehaviour
    {
        private const int PlaceholderValue = 1234567890;

        [SerializeField] private int playfieldId;
        [SerializeField] private string dynelName = string.Empty;
        [SerializeField] private int identityInstance;
        [SerializeField] private int templateId;
        [SerializeField] private int staticInstance;
        [SerializeField] private int monsterData;
        [SerializeField] private int level;
        [SerializeField] private string description = string.Empty;

        public int PlayfieldId { get => playfieldId; set => playfieldId = value; }
        public string DynelName { get => dynelName ?? string.Empty; set => dynelName = value ?? string.Empty; }
        public int IdentityInstance { get => identityInstance; set => identityInstance = value; }
        public int TemplateId { get => templateId; set => templateId = value; }
        public int StaticInstance { get => staticInstance; set => staticInstance = value; }
        public int MonsterData { get => monsterData; set => monsterData = value; }
        public int Level { get => level; set => level = value; }
        public string Description { get => description ?? string.Empty; set => description = value ?? string.Empty; }

        public int PreferredStableId
        {
            get
            {
                if (IsRealId(monsterData))
                    return monsterData;
                if (IsRealId(staticInstance))
                    return staticInstance;
                if (IsRealId(templateId))
                    return templateId;
                if (IsRealId(identityInstance))
                    return identityInstance;
                return 0;
            }
        }

        public IReadOnlyList<int> GetCandidateIds()
        {
            var ids = new List<int>(4);
            AddDistinct(ids, monsterData);
            AddDistinct(ids, staticInstance);
            AddDistinct(ids, templateId);
            AddDistinct(ids, identityInstance);
            return ids;
        }

        private static void AddDistinct(List<int> ids, int value)
        {
            if (!IsRealId(value))
                return;
            if (!ids.Contains(value))
                ids.Add(value);
        }

        private static bool IsRealId(int value)
        {
            return value > 0 && value != PlaceholderValue;
        }
    }
}
