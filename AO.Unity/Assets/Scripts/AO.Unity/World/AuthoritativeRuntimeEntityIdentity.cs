using UnityEngine;

namespace AO.Unity.World
{
    public sealed class AuthoritativeRuntimeEntityIdentity : MonoBehaviour
    {
        [SerializeField] private string entityId = string.Empty;
        [SerializeField] private bool attackable;

        public string EntityId => entityId ?? string.Empty;
        public bool Attackable => attackable;

        public void Initialize(string runtimeEntityId, bool isAttackable)
        {
            entityId = runtimeEntityId ?? string.Empty;
            attackable = isAttackable;
        }
    }
}
