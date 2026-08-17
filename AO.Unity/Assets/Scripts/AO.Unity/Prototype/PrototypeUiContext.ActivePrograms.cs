using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AO.Core.Stats;
using AO.Data.Unity;
using UnityEngine;

namespace AO.Unity.Prototype
{
    public partial class PrototypeUiContext
    {
        private int GetActiveProgramModifierSum(int statId, bool updateBeforeRead = true)
        {
            if (statId <= 0)
                return 0;

            if (statId == VisualProfessionStatId)
            {
                int overrideId = GetCurrentVisualProfessionId();
                return overrideId - CharacterProfessionId;
            }

            if (updateBeforeRead && !_isUpdatingActivePrograms)
                UpdateActiveProgramsInternal();
            int total = 0;
            for (int i = 0; i < _activePrograms.Count; i++)
            {
                var active = _activePrograms[i];
                if (active?.StatModifiers == null)
                    continue;

                if (active.StatModifiers.TryGetValue(statId, out int value))
                    total += value;
            }

            return total;
        }

        private int GetCurrentVisualProfessionId()
        {
            if (!_isUpdatingActivePrograms)
                UpdateActiveProgramsInternal();
            for (int i = _activePrograms.Count - 1; i >= 0; i--)
            {
                var active = _activePrograms[i];
                if (active?.VisualProfessionOverrideId.HasValue == true && active.VisualProfessionOverrideId.Value > 0)
                    return active.VisualProfessionOverrideId.Value;
            }

            int statValue = Character?.StatsContainer?.GetBaseStat(VisualProfessionStatId) ?? 0;
            if (statValue > 0)
                return statValue;

            return CharacterProfessionId;
        }

        private int? ResolveVisualProfessionOverrideId(AO.Data.Core.Item rawNano)
        {
            if (rawNano?.SpellData == null)
                return null;

            foreach (var group in rawNano.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null || spell.Stat != VisualProfessionStatId)
                        continue;

                    if (TryParseVisualProfessionFromSpell(spell, out int visualProf) && visualProf > 0)
                        return visualProf;
                }
            }

            return null;
        }

        private static bool TryParseVisualProfessionFromSpell(AO.Data.Core.ItemSpellData spell, out int visualProfessionId)
        {
            visualProfessionId = 0;
            if (spell == null || spell.Stat != VisualProfessionStatId)
                return false;

            if (spell.Amount > 0)
            {
                visualProfessionId = spell.Amount;
                return true;
            }

            string text = spell.SpellDescription ?? spell.SpellFormat ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var toMatch = Regex.Match(text, @"VisualProfession\s+to\s+(\d+)", RegexOptions.IgnoreCase);
            if (toMatch.Success && toMatch.Groups.Count >= 2 && int.TryParse(toMatch.Groups[1].Value, out int parsedTo) && parsedTo > 0)
            {
                visualProfessionId = parsedTo;
                return true;
            }

            var setMatch = Regex.Match(text, @"VisualProfession.*?(\d+)", RegexOptions.IgnoreCase);
            if (setMatch.Success && setMatch.Groups.Count >= 2 && int.TryParse(setMatch.Groups[1].Value, out int parsedSet) && parsedSet > 0)
            {
                visualProfessionId = parsedSet;
                return true;
            }

            return false;
        }

        private int GetEquippedItemModifierSum(int statId)
        {
            if (statId <= 0 || Character == null)
                return 0;

            int total = 0;
            var equipped = GetEquipped();
            foreach (var pair in equipped)
            {
                if (pair.Value <= 0)
                    continue;

                var dataItem = AODataManager.Instance.GetItemInstance(pair.Value);
                var modifiers = dataItem?.Definition?.StatModifiers;
                if (modifiers == null)
                    continue;

                for (int i = 0; i < modifiers.Count; i++)
                {
                    var mod = modifiers[i];
                    if (mod == null || mod.StatId != statId)
                        continue;

                    total += mod.Value;
                }
            }

            return total;
        }

        private bool UpdateActiveProgramsInternal()
        {
            if (_isUpdatingActivePrograms)
                return false;

            _isUpdatingActivePrograms = true;
            bool changed = false;
            try
            {
                float now = Time.unscaledTime;
                for (int i = _activePrograms.Count - 1; i >= 0; i--)
                {
                    var active = _activePrograms[i];
                    if (active == null)
                    {
                        _activePrograms.RemoveAt(i);
                        changed = true;
                        continue;
                    }

                    if (active.DurationSeconds <= 0f)
                        continue;

                    if (ProcessPeriodicNanoEffects(active, now))
                        changed = true;

                    if (active.StartedAt + active.DurationSeconds > now)
                        continue;

                    _activePrograms.RemoveAt(i);
                    changed = true;
                }

                if (changed)
                    NotifyStateChanged();
                return changed;
            }
            finally
            {
                _isUpdatingActivePrograms = false;
            }
        }

        private bool ProcessPeriodicNanoEffects(ActiveNanoProgram active, float now)
        {
            if (active?.PeriodicEffects == null || active.PeriodicEffects.Count == 0)
                return false;

            bool changed = false;
            for (int i = active.PeriodicEffects.Count - 1; i >= 0; i--)
            {
                var effect = active.PeriodicEffects[i];
                if (effect == null || effect.RemainingTicks <= 0)
                {
                    active.PeriodicEffects.RemoveAt(i);
                    continue;
                }

                float safeInterval = Mathf.Max(0.05f, effect.TickIntervalSeconds);
                while (effect.RemainingTicks > 0 && now >= effect.NextTickAt)
                {
                    int delta = ResolvePeriodicAmount(effect);
                    if (ApplyResourceDelta(effect.StatId, delta))
                        changed = true;

                    effect.RemainingTicks--;
                    effect.NextTickAt += safeInterval;
                }

                if (effect.RemainingTicks <= 0)
                    active.PeriodicEffects.RemoveAt(i);
            }

            return changed;
        }

        private bool ApplyResourceDelta(int statId, int delta)
        {
            if (delta == 0)
                return false;

            if (statId == StatIds.Health || statId == StatIds.RemainingHealth)
            {
                int max = GetMaxHealthValue();
                int current = GetCurrentHealthValue();
                int next = Mathf.Clamp(current + delta, 0, max);
                if (next == current)
                    return false;

                _localHealthCurrent = next;
                return true;
            }

            if (statId == StatIds.CurrentNano || statId == StatIds.NanoPool)
            {
                int max = GetMaxNanoValue();
                int current = GetCurrentNanoValue();
                int next = Mathf.Clamp(current + delta, 0, max);
                if (next == current)
                    return false;

                _localNanoCurrent = next;
                return true;
            }

            return false;
        }

        private static bool IsPeriodicSpell(AO.Data.Core.ItemSpellData spell)
        {
            return spell != null && (spell.TickCount > 1 || spell.TickInterval > 0);
        }

        private static float ConvertTickIntervalToSeconds(int rawTickInterval)
        {
            if (rawTickInterval <= 0)
                return 1f;

            return Mathf.Max(0.05f, rawTickInterval / 100f);
        }

        private static string ResolveNanoSchoolTabName(int schoolRaw)
        {
            return schoolRaw switch
            {
                1 => "Combat",
                2 => "Medical",
                3 => "Prot",
                4 => "Psi",
                5 => "Space",
                _ => "All"
            };
        }

        private void ApplyVisualProfessionBaseline(int professionId)
        {
            if (Character == null)
                return;

            string visualProfName = AODataManager.Instance.GetStatName(VisualProfessionStatId);
            if (string.IsNullOrWhiteSpace(visualProfName))
                visualProfName = "VisualProfession";

            Character.SetBaseStatValue(visualProfName, Mathf.Max(1, professionId));
        }

        private void SyncLocalResourcePools()
        {
            int resolvedHealthMax = ResolveMaxHealthForUi();
            SyncLocalPool(ref _localHealthCurrent, ref _localHealthMax, resolvedHealthMax, ResolveCurrentHealthForUi);

            int resolvedNanoMax = ResolveMaxNanoForUi();
            SyncLocalPool(ref _localNanoCurrent, ref _localNanoMax, resolvedNanoMax, ResolveCurrentNanoForUi);
        }

        private static void SyncLocalPool(ref int current, ref int max, int resolvedMax, Func<int, int> resolveCurrentFallback)
        {
            int nextMax = Mathf.Max(1, resolvedMax);
            int previousMax = Mathf.Max(1, max);
            int previousCurrent = Mathf.Clamp(current, 0, previousMax);

            if (max <= 0)
            {
                max = nextMax;
                current = Mathf.Clamp(resolveCurrentFallback(nextMax), 0, nextMax);
                return;
            }

            if (nextMax == previousMax)
            {
                max = nextMax;
                current = Mathf.Clamp(previousCurrent, 0, nextMax);
                return;
            }

            bool wasFull = previousCurrent >= previousMax;
            max = nextMax;
            if (wasFull)
            {
                current = nextMax;
                return;
            }

            int delta = nextMax - previousMax;
            current = Mathf.Clamp(previousCurrent + delta, 0, nextMax);
        }

        private int ResolveCurrentHealthForUi(int maxHealth)
        {
            int currentStat = Character?.StatsContainer?.GetFinalStat(StatIds.Health) ?? 0;
            int remainingStat = Character?.StatsContainer?.GetFinalStat(StatIds.RemainingHealth) ?? 0;
            int current = currentStat > 0 ? currentStat : remainingStat;
            if (current <= 0)
                current = maxHealth;
            return Mathf.Clamp(current, 0, Mathf.Max(1, maxHealth));
        }

        private int ResolveMaxHealthForUi()
        {
            int maxHealth = Character?.StatsContainer?.GetFinalStat(StatIds.MaxHealth) ?? 0;
            int health = Character?.StatsContainer?.GetFinalStat(StatIds.Health) ?? 0;
            maxHealth += GetActiveProgramModifierSum(StatIds.MaxHealth, updateBeforeRead: false);
            health += GetActiveProgramModifierSum(StatIds.Health, updateBeforeRead: false);
            int value = maxHealth > 0 ? maxHealth : health;
            if (value <= 0)
                value = 1;
            return Mathf.Max(1, value);
        }

        private int ResolveCurrentNanoForUi(int maxNano)
        {
            int currentStat = Character?.StatsContainer?.GetFinalStat(StatIds.CurrentNano) ?? 0;
            int poolStat = Character?.StatsContainer?.GetFinalStat(StatIds.NanoPool) ?? 0;
            int current = currentStat > 0 ? currentStat : poolStat;
            if (current <= 0)
                current = maxNano;
            return Mathf.Clamp(current, 0, Mathf.Max(1, maxNano));
        }

        private int ResolveMaxNanoForUi()
        {
            int maxNanoEnergy = Character?.StatsContainer?.GetFinalStat(StatIds.MaxNanoEnergy) ?? 0;
            int nanoPool = Character?.StatsContainer?.GetFinalStat(StatIds.NanoPool) ?? 0;
            maxNanoEnergy += GetActiveProgramModifierSum(StatIds.MaxNanoEnergy, updateBeforeRead: false);
            nanoPool += GetActiveProgramModifierSum(StatIds.NanoPool, updateBeforeRead: false);
            int value = maxNanoEnergy > 0 ? maxNanoEnergy : nanoPool;
            if (value <= 0)
                value = 1;
            return Mathf.Max(1, value);
        }

    }
}
