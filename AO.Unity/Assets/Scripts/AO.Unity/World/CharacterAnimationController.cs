using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AO.Unity.World
{
    public sealed class CharacterAnimationController : MonoBehaviour
    {
        [Serializable]
        public sealed class ActionConfig
        {
            public string actionKey = string.Empty;
            public string clipOverride = string.Empty;
            public float loopStartSeconds = 0f;
            public float loopEndSeconds = 0f;
        }

        [SerializeField] private CharacterAppearanceController appearanceController;
        [SerializeField] private bool autoApplyInPlayMode = true;
        [SerializeField] private List<ActionConfig> actionConfigs = new()
        {
            new ActionConfig { actionKey = "Run", loopStartSeconds = 0.95f, loopEndSeconds = 2.18f },
            new ActionConfig { actionKey = "Walk", loopStartSeconds = 1.1f, loopEndSeconds = 2f },
            new ActionConfig { actionKey = "Strafe right", loopStartSeconds = 0.5f, loopEndSeconds = 1.2f },
            new ActionConfig { actionKey = "Strafe Left", loopStartSeconds = 0.5f, loopEndSeconds = 1.2f },
            new ActionConfig { actionKey = "Run Backwards", loopStartSeconds = 0.4f, loopEndSeconds = 1f },
            new ActionConfig { actionKey = "Sit Down", loopStartSeconds = 0f, loopEndSeconds = 0f },
            new ActionConfig { actionKey = "Idle MA" },
            new ActionConfig { actionKey = "Idle Sit" },
            new ActionConfig { actionKey = "Stand Up", loopStartSeconds = 0f, loopEndSeconds = 0f },
            new ActionConfig { actionKey = "Jumping from idle stance", loopStartSeconds = 0f, loopEndSeconds = 0f },
            new ActionConfig { actionKey = "Jump Forward", loopStartSeconds = 0f, loopEndSeconds = 0f },
            new ActionConfig { actionKey = "Land after jump", loopStartSeconds = 0f, loopEndSeconds = 0.5f },
            new ActionConfig { actionKey = "Land after jump and keep running", loopStartSeconds = 0f, loopEndSeconds = 0.25f }
        };

        private void Awake()
        {
            if (appearanceController == null)
                appearanceController = GetComponent<CharacterAppearanceController>();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || !autoApplyInPlayMode)
                return;

            if (appearanceController == null)
                appearanceController = GetComponent<CharacterAppearanceController>();
            appearanceController?.ApplyAnimationOverridesNow();
        }

        public string GetClipOverride(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return string.Empty;

            var config = actionConfigs.FirstOrDefault(c =>
                c != null && string.Equals(c.actionKey, actionKey, StringComparison.OrdinalIgnoreCase));
            return config?.clipOverride ?? string.Empty;
        }

        public bool TryGetLoopWindow(string actionKey, out float loopStartSeconds, out float loopEndSeconds)
        {
            loopStartSeconds = 0f;
            loopEndSeconds = 0f;
            if (string.IsNullOrWhiteSpace(actionKey))
                return false;

            var config = actionConfigs.FirstOrDefault(c =>
                c != null && string.Equals(c.actionKey, actionKey, StringComparison.OrdinalIgnoreCase));
            if (config == null)
                return false;

            loopStartSeconds = Mathf.Max(0f, config.loopStartSeconds);
            loopEndSeconds = Mathf.Max(0f, config.loopEndSeconds);
            return true;
        }

        public void ApplyNow()
        {
            if (appearanceController == null)
                appearanceController = GetComponent<CharacterAppearanceController>();
            appearanceController?.ApplyAnimationOverridesNow();
        }
    }
}
