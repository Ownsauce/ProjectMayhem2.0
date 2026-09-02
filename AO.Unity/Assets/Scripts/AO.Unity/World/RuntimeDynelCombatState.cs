using UnityEngine;

namespace AO.Unity.World
{
    public sealed class RuntimeDynelCombatState : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 120;
        [SerializeField] private int currentHealth = 120;
        [SerializeField] private int armorClass;
        [SerializeField] private int maxNano;
        [SerializeField] private bool corpseSettled;
        [SerializeField] private float corpseSettleAt = -1f;
        [SerializeField] private float corpseForceSettleAt = -1f;
        [SerializeField] private bool deathClipDetected;
        [SerializeField] private bool deathAnimationStarted;
        [SerializeField] private bool deathSuppressedAnimationDrivers;
        [SerializeField] private float lastDeathAnimationDuration;
        [SerializeField] private int lastAuthoritativeAttackTick;

        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int CurrentHealth => Mathf.Clamp(currentHealth, 0, MaxHealth);
        public int ArmorClass => Mathf.Max(0, armorClass);
        public int MaxNano => Mathf.Max(0, maxNano);
        public bool IsDead => CurrentHealth <= 0;

        public void Initialize(int resolvedMaxHealth, int resolvedArmorClass = 0, int resolvedMaxNano = 0, bool forceFullHealth = false)
        {
            int priorMaxHealth = Mathf.Max(1, maxHealth);
            maxHealth = Mathf.Max(1, resolvedMaxHealth);
            currentHealth = forceFullHealth
                ? maxHealth
                : Mathf.Clamp(currentHealth, 0, maxHealth);
            armorClass = Mathf.Max(0, resolvedArmorClass);
            maxNano = Mathf.Max(0, resolvedMaxNano);

            if (currentHealth > 0)
            {
                ClearDeathState();
            }
        }

        public int ApplyDamage(int amount)
        {
            if (amount <= 0 || IsDead)
                return CurrentHealth;

            currentHealth = Mathf.Clamp(CurrentHealth - amount, 0, MaxHealth);
            RuntimeDamageIndicator.Spawn(transform, amount);
            if (currentHealth <= 0)
            {
                TryStartDeathAnimationImmediate();
            }
            return currentHealth;
        }

        public void SetAuthoritativeHealth(int health, int resolvedMaxHealth)
        {
            bool wasDead = IsDead;
            maxHealth = Mathf.Max(1, resolvedMaxHealth);
            currentHealth = Mathf.Clamp(health, 0, maxHealth);
            if (currentHealth <= 0)
                TryStartDeathAnimationImmediate();
            else if (wasDead)
                ClearDeathState();
        }

        public void ApplyAuthoritativeAttackTick(int attackTick)
        {
            if (attackTick <= 0 || attackTick == lastAuthoritativeAttackTick || IsDead)
                return;

            lastAuthoritativeAttackTick = attackTick;
            var appearance = GetComponent<CharacterAppearanceController>();
            if (appearance != null && appearance.enabled)
                appearance.TryPlayRecentAttackHitAction(out _);
        }

        public void ApplyAuthoritativeCombatState(string combatState)
        {
            var appearance = GetComponent<CharacterAppearanceController>();
            if (appearance == null || !appearance.enabled)
                return;

            bool chasing = string.Equals(combatState, "chasing", System.StringComparison.OrdinalIgnoreCase);
            appearance.SetExternalLocomotionState(chasing, walk: false);
        }

        public void BeginCorpseSettle(float deathAnimationDurationSeconds)
        {
            if (IsDead && !deathAnimationStarted)
                TryStartDeathAnimationImmediate();

            // Give death one-shots time to start and play before freezing corpse pose.
            float safe = Mathf.Max(1.2f, deathAnimationDurationSeconds, lastDeathAnimationDuration);
            corpseSettled = false;
            corpseSettleAt = Time.time + safe;
            corpseForceSettleAt = Time.time + Mathf.Max(3.0f, safe + 1.25f);
            deathClipDetected = false;
        }

        private void TryStartDeathAnimationImmediate()
        {
            if (deathAnimationStarted)
                return;

            deathAnimationStarted = true;
            string dynelName = gameObject != null ? gameObject.name : "<null>";
            Debug.Log($"[RuntimeDeath] Attempt start for {dynelName}.");

            // Preferred path: let the active appearance animation driver play death one-shot.
            // This is usually the renderer-driving path for runtime GLB actors.
            var appearance = GetComponent<CharacterAppearanceController>();
            if (appearance != null && appearance.enabled)
            {
                var candidateNames = CollectDeathLikeClipNamesForAppearance();
                if (candidateNames.Count > 0 && appearance.TryPlayDeathOneShot(candidateNames, out float duration))
                {
                    lastDeathAnimationDuration = Mathf.Max(0.25f, duration);
                    deathClipDetected = true;
                    // Suppress movement/secondary animation drivers only after death has started.
                    SuppressAnimationDriversForDeath();
                    Debug.Log($"[RuntimeDeath] Appearance death one-shot started for {dynelName}, duration={lastDeathAnimationDuration:0.###}.");
                    return;
                }
            }
            // Prefer animator-driven rigs first; in some models legacy components exist but do not
            // drive the currently visible mesh hierarchy.
            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;
                int drivenRenderers = CountActiveSkinnedRenderers(animator.transform);
                if (drivenRenderers <= 0)
                    continue;

                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips == null || clips.Length == 0)
                    continue;

                AnimationClip selectedClip = null;
                int selectedScore = int.MinValue;
                float selectedLen = 0f;
                for (int c = 0; c < clips.Length; c++)
                {
                    var clip = clips[c];
                    if (clip == null || string.IsNullOrWhiteSpace(clip.name))
                        continue;
                    string n = clip.name;
                    int score = ScoreDeathClipName(n);
                    if (score <= 0)
                        continue;
                    float len = Mathf.Max(0f, clip.length);
                    if (score > selectedScore || (score == selectedScore && len > selectedLen))
                    {
                        selectedClip = clip;
                        selectedScore = score;
                        selectedLen = len;
                    }
                }

                if (selectedClip == null)
                    continue;

                var host = animator.gameObject != null ? animator.gameObject : gameObject;
                var legacy = host.GetComponent<Animation>();
                if (legacy == null)
                    legacy = host.AddComponent<Animation>();

                selectedClip.legacy = true;
                if (legacy.GetClip(selectedClip.name) == null)
                    legacy.AddClip(selectedClip, selectedClip.name);
                legacy.wrapMode = WrapMode.Once;
                var st = legacy[selectedClip.name];
                if (st != null)
                {
                    st.wrapMode = WrapMode.Once;
                    st.speed = 1f;
                    st.time = 0f;
                }

                animator.enabled = false;
                legacy.Stop();
                legacy.Play(selectedClip.name);
                deathClipDetected = true;
                lastDeathAnimationDuration = Mathf.Max(0.25f, selectedLen);
                SuppressAnimationDriversForDeath();
                Debug.Log($"[RuntimeDeath] Animator->legacy death clip '{selectedClip.name}' len={selectedLen:0.###} score={selectedScore} renderers={drivenRenderers} path={BuildHierarchyPath(animator.transform)} started for {dynelName}.");
                return;
            }

            // Fallback to existing legacy Animation components.
            var legacyAnimations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var anim = legacyAnimations[i];
                if (anim == null)
                    continue;
                int drivenRenderers = CountActiveSkinnedRenderers(anim.transform);
                if (drivenRenderers <= 0)
                    continue;

                string selected = string.Empty;
                float selectedLen = 0f;
                int selectedScore = int.MinValue;
                foreach (AnimationState state in anim)
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.name))
                        continue;
                    string n = state.name;
                    int score = ScoreDeathClipName(n);
                    if (score <= 0)
                        continue;

                    float len = Mathf.Max(0f, state.length);
                    if (score > selectedScore || (score == selectedScore && len > selectedLen))
                    {
                        selected = state.name;
                        selectedLen = len;
                        selectedScore = score;
                    }
                }

                if (!string.IsNullOrWhiteSpace(selected))
                {
                    anim.Stop();
                    anim.Play(selected);
                    var st = anim[selected];
                    if (st != null)
                    {
                        st.wrapMode = WrapMode.Once;
                        st.speed = 1f;
                        st.time = 0f;
                        st.enabled = true;
                        st.weight = 1f;
                    }
                    deathClipDetected = true;
                    lastDeathAnimationDuration = Mathf.Max(0.25f, selectedLen);
                    SuppressAnimationDriversForDeath();
                    Debug.Log($"[RuntimeDeath] Legacy death clip '{selected}' len={selectedLen:0.###} score={selectedScore} renderers={drivenRenderers} path={BuildHierarchyPath(anim.transform)} started for {dynelName}.");
                    return;
                }
            }

            Debug.LogWarning($"[RuntimeDeath] No death-like clip found for {dynelName}.");
        }

        private static int CountActiveSkinnedRenderers(Transform root)
        {
            if (root == null)
                return 0;
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int count = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                count++;
            }
            return count;
        }

        private static string BuildHierarchyPath(Transform t)
        {
            if (t == null)
                return "<null>";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Transform cur = t;
            while (cur != null)
            {
                if (sb.Length == 0)
                    sb.Insert(0, cur.name);
                else
                    sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }

        private static int ScoreDeathClipName(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
                return 0;

            int score = 0;
            if (clipName.IndexOf("die-pain", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
            if (clipName.IndexOf("diedefault", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 95;
            if (clipName.IndexOf("die", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 80;
            if (clipName.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 70;
            if (clipName.IndexOf("dead", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 50;
            if (clipName.IndexOf("collapse", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 40;
            if (clipName.IndexOf("fall", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            return score;
        }

        private void SuppressAnimationDriversForDeath()
        {
            if (deathSuppressedAnimationDrivers)
                return;

            var walker = GetComponent<PrototypeWalkerController>();
            if (walker != null && walker.enabled)
                walker.enabled = false;

            var animationController = GetComponent<CharacterAnimationController>();
            if (animationController != null && animationController.enabled)
                animationController.enabled = false;

            deathSuppressedAnimationDrivers = true;
        }

        private void RestoreAnimationDriversAfterDeath()
        {
            if (!deathSuppressedAnimationDrivers)
                return;

            var walker = GetComponent<PrototypeWalkerController>();
            if (walker != null && !walker.enabled)
                walker.enabled = true;

            var animationController = GetComponent<CharacterAnimationController>();
            if (animationController != null && !animationController.enabled)
                animationController.enabled = true;

            deathSuppressedAnimationDrivers = false;
        }

        private void ClearDeathState()
        {
            deathAnimationStarted = false;
            corpseSettled = false;
            corpseSettleAt = -1f;
            corpseForceSettleAt = -1f;
            deathClipDetected = false;
            lastDeathAnimationDuration = 0f;
            RestoreAnimationDriversAfterDeath();
            UnfreezeCorpsePose();
        }

        private void UnfreezeCorpsePose()
        {
            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;
                animator.speed = 1f;
            }

            var legacyAnimations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var legacy = legacyAnimations[i];
                if (legacy == null)
                    continue;

                foreach (AnimationState state in legacy)
                {
                    if (state == null)
                        continue;
                    state.speed = 1f;
                }
            }
        }

        private System.Collections.Generic.List<string> CollectDeathLikeClipNamesForAppearance()
        {
            var results = new System.Collections.Generic.List<string>();
            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;
                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips == null)
                    continue;
                for (int c = 0; c < clips.Length; c++)
                {
                    var clip = clips[c];
                    if (clip == null || string.IsNullOrWhiteSpace(clip.name))
                        continue;
                    if (ScoreDeathClipName(clip.name) <= 0)
                        continue;
                    if (!results.Contains(clip.name))
                        results.Add(clip.name);
                }
            }

            var legacyAnimations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var anim = legacyAnimations[i];
                if (anim == null)
                    continue;
                foreach (AnimationState state in anim)
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.name))
                        continue;
                    if (ScoreDeathClipName(state.name) <= 0)
                        continue;
                    if (!results.Contains(state.name))
                        results.Add(state.name);
                }
            }

            results.Sort((a, b) => ScoreDeathClipName(b).CompareTo(ScoreDeathClipName(a)));
            return results;
        }

        private void Update()
        {
            if (IsDead && !deathAnimationStarted)
                TryStartDeathAnimationImmediate();

            if (!IsDead || corpseSettled || corpseSettleAt < 0f)
                return;

            if (!deathClipDetected)
                deathClipDetected = IsDeathClipPlaying();

            if (Time.time < corpseSettleAt)
                return;

            // Prefer to freeze only after we observed a death clip at least once.
            if (!deathClipDetected && corpseForceSettleAt > 0f && Time.time < corpseForceSettleAt)
                return;

            FreezeInCorpsePose();
            corpseSettled = true;
            corpseSettleAt = -1f;
            corpseForceSettleAt = -1f;
        }

        private bool IsDeathClipPlaying()
        {
            var legacyAnimations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var legacy = legacyAnimations[i];
                if (legacy == null || !legacy.isPlaying)
                    continue;

                foreach (AnimationState state in legacy)
                {
                    if (state == null || !state.enabled || state.weight <= 0.001f)
                        continue;

                    string n = state.name ?? string.Empty;
                    if (n.IndexOf("die", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || !animator.enabled || !animator.isActiveAndEnabled)
                    continue;

                int layers = Mathf.Max(1, animator.layerCount);
                for (int layer = 0; layer < layers; layer++)
                {
                    var clips = animator.GetCurrentAnimatorClipInfo(layer);
                    for (int c = 0; c < clips.Length; c++)
                    {
                        var clip = clips[c].clip;
                        if (clip == null || string.IsNullOrWhiteSpace(clip.name))
                            continue;
                        string n = clip.name;
                        if (n.IndexOf("die", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void FreezeInCorpsePose()
        {
            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;
                animator.speed = 0f;
            }

            var legacyAnimations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var legacy = legacyAnimations[i];
                if (legacy == null)
                    continue;

                foreach (AnimationState state in legacy)
                {
                    if (state == null || !state.enabled)
                        continue;
                    state.speed = 0f;
                }
            }
        }
    }
}
