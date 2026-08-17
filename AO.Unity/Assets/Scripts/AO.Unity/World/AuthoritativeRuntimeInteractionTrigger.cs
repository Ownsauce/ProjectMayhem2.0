using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.World
{
    public sealed class AuthoritativeRuntimeInteractionTrigger : MonoBehaviour
    {
        private AuthoritativeNetworkClient _client;
        private string _entityId;
        private string _label;
        private string _interactionType;
        private float _radius;
        private float _nextRequestTime;
        public string EntityId => _entityId ?? string.Empty;

        public void Initialize(AuthoritativeNetworkClient client, string entityId, string label, string interactionType, float radius)
        {
            _client = client;
            _entityId = entityId ?? string.Empty;
            _label = label ?? string.Empty;
            _interactionType = interactionType ?? string.Empty;
            _radius = Mathf.Max(1f, radius);
        }

        private void OnTriggerEnter(Collider other)
        {
            // Intentionally no auto-interact.
            // Runtime interactions are explicit (right-click/use) to avoid
            // unsolicited conversation/teleport spam when colliders overlap.
        }

        private void OnTriggerStay(Collider other)
        {
            if (!WasRightClickPressedThisFrame())
                return;
            if (!IsPointerOverThisTrigger())
                return;

            TryInteract(other);
        }

        private void TryInteract(Collider other)
        {
            if (_client == null || string.IsNullOrWhiteSpace(_entityId) || other == null)
                return;

            if (Time.unscaledTime < _nextRequestTime)
                return;

            var bridge = other.GetComponentInParent<CharacterRuntimeBridge>();
            if (bridge == null)
                return;

            // Keep a tiny debounce to avoid duplicate requests from multiple colliders
            // in the same short time window.
            _nextRequestTime = Time.unscaledTime + 0.15f;
            Debug.Log($"AO.Server runtime interaction requested type={_interactionType} entity={_entityId} label={_label} radius={_radius:0.##}");
            _client.RequestRuntimeInteraction(_entityId);
        }

        public void RequestInteractionFromUi()
        {
            if (_client == null || string.IsNullOrWhiteSpace(_entityId))
                return;

            _client.RequestRuntimeInteraction(_entityId);
        }

        private static bool WasRightClickPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                return true;
            return false;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private bool IsPointerOverThisTrigger()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return false;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
                return false;
            Vector2 screenPos = mouse.position.ReadValue();
#else
            Vector2 screenPos = Input.mousePosition;
#endif

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 5000f, ~0, QueryTriggerInteraction.Collide))
                return false;

            Transform hitTransform = hit.collider != null ? hit.collider.transform : null;
            return hitTransform != null && (hitTransform == transform || hitTransform.IsChildOf(transform));
        }
    }
}
