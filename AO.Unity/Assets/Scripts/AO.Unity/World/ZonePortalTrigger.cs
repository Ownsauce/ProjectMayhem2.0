using AO.Data.Unity;
using UnityEngine;

namespace AO.Unity.World
{
    public class ZonePortalTrigger : MonoBehaviour
    {
        private ZoneTransitionManager _manager;
        private ZoneLinkRow _link;
        private float _armedAtTime;

        public void Initialize(ZoneTransitionManager manager, ZoneLinkRow link, float armDelaySeconds = 0f)
        {
            _manager = manager;
            _link = link;
            _armedAtTime = Time.unscaledTime + Mathf.Max(0f, armDelaySeconds);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_manager == null || _link == null || other == null)
                return;
            if (Time.unscaledTime < _armedAtTime)
                return;

            var bridge = other.GetComponentInParent<CharacterRuntimeBridge>();
            if (bridge == null)
                return;

            Debug.Log(
                $"Zone portal trigger hit: {_link.Id ?? "unknown"} " +
                $"by {bridge.name} (collider={other.name}).");
            _manager.TryTransition(_link, other);
        }
    }
}
