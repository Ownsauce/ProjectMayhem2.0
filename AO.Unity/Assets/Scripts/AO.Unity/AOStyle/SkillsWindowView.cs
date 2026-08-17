using System;
using System.Collections.Generic;
using System.Linq;
using AO.Core.Characters;
using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.AOStyle
{
    public class SkillsWindowView : MonoBehaviour
    {
        private static readonly string[] Categories =
        {
            "Abilities",
            "Body & Defense",
            "Melee Weapons",
            "Melee Specials",
            "Ranged Weapons",
            "Ranged Specials",
            "Nanos & Casting",
            "Exploring",
            "Combat & Healing",
            "Trade & Repair",
            "Disabled / Legacy",
            "Buffed"
        };

        private static readonly int[] AbilityStatIds = { 16, 17, 18, 19, 20, 21 };
        private static readonly int[] BodyDefenseStatIds = { 152, 132, 155, 154, 153, 168, 156, 118, 119, 120 };
        private static readonly int[] MeleeWeaponStatIds = { 100, 102, 103, 104, 105, 106, 107, 101 };
        private static readonly int[] MeleeSpecialStatIds = { 142, 143, 144, 145, 146, 147 };
        private static readonly int[] RangedWeaponStatIds = { 111, 112, 113, 114, 115, 116, 133, 134, 108, 109, 110 };
        private static readonly int[] RangedSpecialStatIds = { 121, 148, 150, 151, 167 };
        private static readonly int[] NanosCastingStatIds = { 122, 127, 128, 129, 130, 131, 149 };
        private static readonly int[] ExploringStatIds = { 137, 138, 136, 164, 140, 141, 166, 139, 117 };
        private static readonly int[] CombatHealingStatIds = { 123, 124 };
        private static readonly int[] TradeRepairStatIds = { 125, 126, 157, 158, 159, 160, 161, 162, 163, 165, 135 };
        private static readonly int[] HiddenBuffedStatIds = { 343, 364 }; // HealDelta / NanoDelta

        private PrototypeUiContext _context;
        private Font _font;
        private RectTransform _rowsRoot;
        private RectTransform _detailsRoot;
        private Text _availableIpText;
        private Text _plannedIpText;
        private string _activeCategory = "Abilities";
        private int _selectedStatId = 16;
        private readonly Dictionary<int, int> _pendingByStatId = new();
        private readonly Dictionary<string, int[]> _categoryStatIds = new(StringComparer.OrdinalIgnoreCase);
        private bool _holdRepeatActive;
        private int _holdRepeatStatId;
        private int _holdRepeatDelta;
        private float _nextHoldRepeatAt;
        private const float HoldRepeatInitialDelay = 0.35f;
        private const float HoldRepeatInterval = 0.08f;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _font = font;
            BuildCategoryMap();
            Build();
            _context.StateChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            StopHoldRepeat();
            if (_context != null)
                _context.StateChanged -= Refresh;
        }

        private void Update()
        {
            if (!_holdRepeatActive)
                return;

            if (!IsPrimaryPointerHeld())
            {
                StopHoldRepeat();
                return;
            }

            if (Time.unscaledTime < _nextHoldRepeatAt)
                return;

            if (!ChangePending(_holdRepeatStatId, _holdRepeatDelta, IsModifierPressed()))
            {
                StopHoldRepeat();
                return;
            }

            _nextHoldRepeatAt = Time.unscaledTime + HoldRepeatInterval;
        }

        private void StartHoldRepeat(int statId, int delta)
        {
            _holdRepeatActive = true;
            _holdRepeatStatId = statId;
            _holdRepeatDelta = delta;
            _nextHoldRepeatAt = Time.unscaledTime + HoldRepeatInitialDelay;
        }

        private void StopHoldRepeat()
        {
            _holdRepeatActive = false;
        }

        private bool IsReadOnlyCategory()
        {
            return string.Equals(_activeCategory, "Buffed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrimaryPointerHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.leftButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        private void BuildCategoryMap()
        {
            _categoryStatIds.Clear();
            _categoryStatIds["Abilities"] = AbilityStatIds;
            _categoryStatIds["Body & Defense"] = BodyDefenseStatIds;
            _categoryStatIds["Melee Weapons"] = MeleeWeaponStatIds;
            _categoryStatIds["Melee Specials"] = MeleeSpecialStatIds;
            _categoryStatIds["Ranged Weapons"] = RangedWeaponStatIds;
            _categoryStatIds["Ranged Specials"] = RangedSpecialStatIds;
            _categoryStatIds["Nanos & Casting"] = NanosCastingStatIds;
            _categoryStatIds["Exploring"] = ExploringStatIds;
            _categoryStatIds["Combat & Healing"] = CombatHealingStatIds;
            _categoryStatIds["Trade & Repair"] = TradeRepairStatIds;

            var known = new HashSet<int>(_context?.GetKnownSkillStatIds() ?? Array.Empty<int>());
            foreach (var configured in _categoryStatIds.Values.SelectMany(v => v))
                known.Remove(configured);

            var buffed = new HashSet<int>();
            if (_context != null)
            {
                foreach (var statId in (_context?.GetStats().Keys ?? Array.Empty<int>()))
                {
                    int baseValue = _context.GetBaseStatValue(statId);
                    int modified = ResolveBuffedStat(statId, baseValue, 0);
                    if (modified != baseValue)
                        buffed.Add(statId);
                }

                foreach (var knownId in (_context?.GetKnownSkillStatIds() ?? Array.Empty<int>()))
                {
                    int baseValue = _context.GetBaseStatValue(knownId);
                    int modified = ResolveBuffedStat(knownId, baseValue, 0);
                    if (modified != baseValue)
                        buffed.Add(knownId);
                }
            }

            foreach (int hiddenId in HiddenBuffedStatIds)
                buffed.Add(hiddenId);

            _categoryStatIds["Buffed"] = buffed.OrderBy(v => v).ToArray();
            _categoryStatIds["Disabled / Legacy"] = known.OrderBy(v => v).ToArray();
        }

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("SkillsRoot", transform, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var top = AOStyleUiFactory.CreatePanel("Top", root, new Color(0.08f, 0.12f, 0.16f, 0.95f));
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.offsetMin = new Vector2(6f, -38f);
            top.offsetMax = new Vector2(-6f, -6f);
            _availableIpText = AOStyleUiFactory.CreateText("AvailableIp", top, "", _font, 14, TextAnchor.MiddleLeft);
            var topTextRt = (RectTransform)_availableIpText.transform;
            topTextRt.anchorMin = Vector2.zero;
            topTextRt.anchorMax = Vector2.one;
            topTextRt.offsetMin = new Vector2(8f, 0f);
            topTextRt.offsetMax = new Vector2(-8f, 0f);

            var body = AOStyleUiFactory.CreatePanel("Body", root, new Color(0f, 0f, 0f, 0f));
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 1f);
            body.offsetMin = new Vector2(6f, 42f);
            body.offsetMax = new Vector2(-6f, -42f);

            var left = AOStyleUiFactory.CreatePanel("Categories", body, new Color(0.05f, 0.08f, 0.12f, 0.95f));
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 1f);
            left.sizeDelta = new Vector2(188f, 0f);

            var leftLayout = left.gameObject.AddComponent<VerticalLayoutGroup>();
            leftLayout.padding = new RectOffset(6, 6, 6, 6);
            leftLayout.spacing = 3f;
            leftLayout.childControlWidth = true;
            leftLayout.childForceExpandWidth = true;
            leftLayout.childControlHeight = true;
            leftLayout.childForceExpandHeight = false;

            foreach (var category in Categories)
            {
                if (string.Equals(category, "Buffed", StringComparison.OrdinalIgnoreCase))
                {
                    var spacer = new GameObject("BuffedSpacer", typeof(RectTransform), typeof(LayoutElement));
                    spacer.transform.SetParent(left, false);
                    var spacerLe = spacer.GetComponent<LayoutElement>();
                    spacerLe.preferredHeight = 12f;
                }

                var localCategory = category;
                var btn = AOStyleUiFactory.CreateButton(
                    $"Cat_{category}",
                    left,
                    category,
                    _font,
                    () =>
                    {
                        StopHoldRepeat();
                        _activeCategory = localCategory;
                        var first = GetCategoryStatIds().FirstOrDefault();
                        if (first > 0)
                            _selectedStatId = first;
                        Refresh();
                    },
                    width: 160f);

                var le = btn.GetComponent<LayoutElement>();
                if (le != null)
                    le.preferredHeight = 18f;
            }

            var content = AOStyleUiFactory.CreatePanel("Content", body, new Color(0f, 0f, 0f, 0f));
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.offsetMin = new Vector2(196f, 0f);
            content.offsetMax = Vector2.zero;

            var topList = AOStyleUiFactory.CreatePanel("TopList", content, new Color(0.05f, 0.08f, 0.12f, 0.95f));
            topList.anchorMin = new Vector2(0f, 0.42f);
            topList.anchorMax = new Vector2(1f, 1f);
            topList.offsetMin = new Vector2(0f, 4f);
            topList.offsetMax = Vector2.zero;
            _rowsRoot = AOStyleUiFactory.CreateScrollContent(topList);

            var details = AOStyleUiFactory.CreatePanel("Details", content, new Color(0.05f, 0.08f, 0.12f, 0.95f));
            details.anchorMin = new Vector2(0f, 0f);
            details.anchorMax = new Vector2(1f, 0.42f);
            details.offsetMin = Vector2.zero;
            details.offsetMax = new Vector2(0f, -4f);
            _detailsRoot = AOStyleUiFactory.CreateScrollContent(details);

            var bottom = AOStyleUiFactory.CreatePanel("Bottom", root, new Color(0.08f, 0.12f, 0.16f, 0.95f));
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.pivot = new Vector2(0.5f, 0f);
            bottom.offsetMin = new Vector2(6f, 6f);
            bottom.offsetMax = new Vector2(-6f, 36f);

            var bottomLayout = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            bottomLayout.padding = new RectOffset(8, 8, 4, 4);
            bottomLayout.spacing = 8f;
            bottomLayout.childControlWidth = false;
            bottomLayout.childForceExpandWidth = false;
            bottomLayout.childControlHeight = true;
            bottomLayout.childForceExpandHeight = true;

            _plannedIpText = AOStyleUiFactory.CreateText("PlannedIp", bottom, "", _font, 12, TextAnchor.MiddleLeft);
            _plannedIpText.gameObject.AddComponent<LayoutElement>().preferredWidth = 340f;

            AOStyleUiFactory.CreateButton("SaveSkills", bottom, "Save Changes", _font, CommitPending, 120f);
            AOStyleUiFactory.CreateButton("CancelSkills", bottom, "Cancel", _font, CancelPending, 80f);
        }

        private void Refresh()
        {
            if (_context == null || _context.Character == null || _rowsRoot == null || _detailsRoot == null)
                return;

            BuildCategoryMap();

            int available = _context.GetAvailableIp();
            int planned = GetPlannedTotalCost();
            _availableIpText.text = $"Available Improvement Points: {available}   (Level {_context.CharacterLevel})";
            _plannedIpText.text = $"Planned spend: {planned}   Remaining after save: {Mathf.Max(0, available - planned)}";

            AOStyleUiFactoryCleanup.Clear(_rowsRoot);
            AOStyleUiFactoryCleanup.Clear(_detailsRoot);

            var statIds = GetCategoryStatIds();
            if (statIds.Length == 0)
            {
                AOStyleUiFactory.CreateText("Empty", _rowsRoot, "No skills configured for this tab.", _font, 12, TextAnchor.MiddleLeft);
                return;
            }

            for (int i = 0; i < statIds.Length; i += 2)
            {
                var gridRow = AOStyleUiFactory.CreatePanel($"SkillGrid_{i}", _rowsRoot, new Color(0f, 0f, 0f, 0f));
                gridRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
                var h = gridRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(2, 2, 2, 2);
                h.spacing = 8f;
                h.childControlWidth = true;
                h.childForceExpandWidth = true;
                h.childControlHeight = true;
                h.childForceExpandHeight = false;

                BuildSkillRow(gridRow, statIds[i]);
                if (i + 1 < statIds.Length)
                    BuildSkillRow(gridRow, statIds[i + 1]);
            }

            if (!statIds.Contains(_selectedStatId))
                _selectedStatId = statIds[0];

            BuildSelectedStatDetails();
        }

        private void BuildSkillRow(Transform parent, int statId)
        {
            bool readOnly = IsReadOnlyCategory();
            string statName = GetStatLabel(statId);
            var row = AOStyleUiFactory.CreatePanel($"Row_{statId}", parent, new Color(0.07f, 0.1f, 0.14f, 0.9f));
            row.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var data = GetDisplayData(statId);

            var name = AOStyleUiFactory.CreateButton($"Select_{statId}", row, statName, _font, () =>
            {
                _selectedStatId = statId;
                Refresh();
            }, 90f);
            var nameLe = name.GetComponent<LayoutElement>();
            if (nameLe != null) nameLe.preferredHeight = 18f;
            var nameText = name.GetComponentInChildren<Text>();
            if (nameText != null)
                nameText.color = ResolveSkillColor(_context.GetSkillColorName(statId));

            var cur = AOStyleUiFactory.CreateText("Cur", row, data.BuffedSkill.ToString(), _font, 12, TextAnchor.MiddleCenter);
            cur.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;
            if (data.BuffedSkill > data.BaseSkill)
                cur.color = new Color(0.45f, 0.95f, 0.45f, 1f);
            else if (data.BuffedSkill < data.BaseSkill)
                cur.color = new Color(0.95f, 0.4f, 0.4f, 1f);

            if (!readOnly)
            {
                var minus = AOStyleUiFactory.CreateButton("Minus", row, "-", _font, () => ChangePending(statId, -1, IsModifierPressed()), 16f);
                var minusLe = minus.GetComponent<LayoutElement>();
                if (minusLe != null) minusLe.preferredHeight = 16f;
                var minusRepeat = minus.gameObject.AddComponent<HoldRepeatButton>();
                minusRepeat.PointerDownAction = () => StartHoldRepeat(statId, -1);
                minusRepeat.PointerUpAction = StopHoldRepeat;
                minusRepeat.PointerExitAction = StopHoldRepeat;

                var plus = AOStyleUiFactory.CreateButton("Plus", row, "+", _font, () => ChangePending(statId, +1, IsModifierPressed()), 16f);
                var plusLe = plus.GetComponent<LayoutElement>();
                if (plusLe != null) plusLe.preferredHeight = 16f;
                var plusRepeat = plus.gameObject.AddComponent<HoldRepeatButton>();
                plusRepeat.PointerDownAction = () => StartHoldRepeat(statId, +1);
                plusRepeat.PointerUpAction = StopHoldRepeat;
                plusRepeat.PointerExitAction = StopHoldRepeat;
            }
            else
            {
                AOStyleUiFactory.CreatePanel("MinusSpacer", row, new Color(0f, 0f, 0f, 0f)).gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;
                AOStyleUiFactory.CreatePanel("PlusSpacer", row, new Color(0f, 0f, 0f, 0f)).gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;
            }

            var progressHost = AOStyleUiFactory.CreatePanel($"Progress_{statId}", row, new Color(0.16f, 0.18f, 0.22f, 1f));
            progressHost.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;
            var progressFill = AOStyleUiFactory.CreatePanel("Fill", progressHost, new Color(0.35f, 0.8f, 0.95f, 1f));
            progressFill.anchorMin = new Vector2(0f, 0f);
            progressFill.anchorMax = new Vector2(data.Progress01, 1f);
            progressFill.offsetMin = Vector2.zero;
            progressFill.offsetMax = Vector2.zero;
        }

        private void BuildSelectedStatDetails()
        {
            string statName = GetStatLabel(_selectedStatId);
            var data = GetDisplayData(_selectedStatId);

            AOStyleUiFactory.CreateText("StatTitle", _detailsRoot, statName, _font, 24, TextAnchor.MiddleCenter);
            AOStyleUiFactory.CreateText("Line1", _detailsRoot, $"Base Skill: {data.BaseSkill}", _font, 13, TextAnchor.MiddleLeft);
            AOStyleUiFactory.CreateText("Line2", _detailsRoot, $"Buffed Skill: {data.BuffedSkill}", _font, 13, TextAnchor.MiddleLeft);
            AOStyleUiFactory.CreateText("Line3", _detailsRoot, $"Maximum skill allowed: {data.MaxSkillAllowed}", _font, 13, TextAnchor.MiddleLeft);
            AOStyleUiFactory.CreateText("Line4", _detailsRoot, $"Current Improvement: {data.CurrentImprovement}", _font, 13, TextAnchor.MiddleLeft);
            AOStyleUiFactory.CreateText("Line5", _detailsRoot, $"Cost to improve: {data.CostToImprove}", _font, 13, TextAnchor.MiddleLeft);
            AOStyleUiFactory.CreateText("Line6", _detailsRoot, $"Total cost: {data.TotalCost}", _font, 13, TextAnchor.MiddleLeft);
        }

        private DisplayData GetDisplayData(int statId)
        {
            string statName = GetStatLabel(statId);
            int currentBaseRaw = _context.GetBaseStatValue(statId);
            int pending = _pendingByStatId.TryGetValue(statId, out int v) ? v : 0;
            int projectedBaseRaw = currentBaseRaw + pending;
            int baseSkill = projectedBaseRaw;

            int buffedSkill = ResolveBuffedStat(statId, currentBaseRaw, pending);

            int initialBaseRaw = AO.Core.Characters.Character.GetStartingBaseStatForBreed?.Invoke(statId, _context.CharacterBreedId) ?? 5;
            int maxAllowed = ResolveMaxAllowedForUi(statId, initialBaseRaw, projectedBaseRaw);
            if (maxAllowed < initialBaseRaw)
                maxAllowed = initialBaseRaw;

            int currentImprovement = Mathf.Max(0, baseSkill - initialBaseRaw);
            int costToImprove = GetNextPointCost(statId, baseSkill, statName);
            int totalCost = SumCostFromInitial(statId, statName, initialBaseRaw, projectedBaseRaw);

            float progress = 0f;
            if (maxAllowed > initialBaseRaw)
                progress = Mathf.Clamp01((baseSkill - initialBaseRaw) / (float)(maxAllowed - initialBaseRaw));

            return new DisplayData
            {
                BaseSkill = baseSkill,
                BuffedSkill = buffedSkill,
                MaxSkillAllowed = maxAllowed,
                CurrentImprovement = currentImprovement,
                CostToImprove = Mathf.Max(0, costToImprove),
                TotalCost = Mathf.Max(0, totalCost),
                Progress01 = progress
            };
        }

        private bool ChangePending(int statId, int delta, bool modifierClick)
        {
            if (IsReadOnlyCategory())
                return false;

            string statName = GetStatLabel(statId);
            int currentPending = _pendingByStatId.TryGetValue(statId, out int v) ? v : 0;
            int currentBase = _context.Character.StatsContainer.GetBaseStat(statId);
            int initialBase = AO.Core.Characters.Character.GetStartingBaseStatForBreed?.Invoke(statId, _context.CharacterBreedId) ?? 5;
            int cap = ResolveMaxAllowedForUi(statId, initialBase, currentBase + currentPending);
            int plannedTotal = GetPlannedTotalCost();
            int currentCostForStat = currentPending > 0 ? _context.Character.GetIpCostForStatIncrease(statName, currentPending) : 0;
            int budgetForStat = Mathf.Max(0, _context.GetAvailableIp() - (plannedTotal - currentCostForStat));

            int nextPending;
            if (modifierClick && delta > 0)
            {
                int capPending = Mathf.Max(0, cap - currentBase);
                nextPending = FindMaxAffordablePending(statName, capPending, budgetForStat);
            }
            else if (modifierClick && delta < 0)
            {
                nextPending = 0;
            }
            else
            {
                nextPending = Mathf.Max(0, currentPending + delta);
            }

            if (nextPending == currentPending)
                return false;

            if (currentBase + nextPending > cap)
            {
                _context.PublishStatus($"{statName} is capped at {cap} for this character.");
                return false;
            }

            if (nextPending == 0)
                _pendingByStatId.Remove(statId);
            else
                _pendingByStatId[statId] = nextPending;

            int nextCostForStat = nextPending > 0 ? _context.Character.GetIpCostForStatIncrease(statName, nextPending) : 0;
            int nextTotal = plannedTotal - currentCostForStat + nextCostForStat;
            if (nextTotal > _context.GetAvailableIp())
            {
                if (currentPending == 0)
                    _pendingByStatId.Remove(statId);
                else
                    _pendingByStatId[statId] = currentPending;

                _context.PublishStatus($"Not enough IP for {statName}.");
                return false;
            }

            _selectedStatId = statId;
            Refresh();
            return true;
        }

        private int GetPlannedTotalCost()
        {
            int total = 0;
            foreach (var pair in _pendingByStatId)
            {
                if (pair.Value <= 0)
                    continue;

                string name = GetStatLabel(pair.Key);
                total += _context.Character.GetIpCostForStatIncrease(name, pair.Value);
            }

            return total;
        }

        private void CommitPending()
        {
            if (_pendingByStatId.Count == 0)
            {
                _context.PublishStatus("No pending skill changes.");
                return;
            }

            _context.RequestSkillIncreases(new Dictionary<int, int>(_pendingByStatId));
            _pendingByStatId.Clear();
            Refresh();
        }

        private void CancelPending()
        {
            _pendingByStatId.Clear();
            _context.PublishStatus("Canceled pending skill changes.");
            Refresh();
        }

        private int[] GetCategoryStatIds()
        {
            return _categoryStatIds.TryGetValue(_activeCategory, out var ids)
                ? ids
                : Array.Empty<int>();
        }

        private string GetStatLabel(int statId)
        {
            string name = _context.GetStatName(statId);
            if (string.IsNullOrWhiteSpace(name))
                return $"Stat {statId}";
            return name;
        }

        private int SumCostFromInitial(int statId, string statName, int initialBase, int projectedBase)
        {
            if (projectedBase <= initialBase)
                return 0;

            int delta = projectedBase - initialBase;
            if (AbilityStatIds.Contains(statId))
            {
                int factor = AO.Core.Characters.Character.GetAbilityCostFactorForBreed?.Invoke(statId, _context.CharacterBreedId) ?? 2;
                int total = 0;
                for (int value = initialBase; value < projectedBase; value++)
                    total += factor * value;
                return total;
            }

            float factorCost = _context.GetSkillCostFactorForCurrentProfession(statId) ?? 0f;
            if (factorCost > 0f)
            {
                int total = 0;
                for (int value = initialBase; value < projectedBase; value++)
                    total += Mathf.Max(1, Mathf.CeilToInt(value * factorCost));
                return total;
            }

            return _context.Character.GetIpCostForStatIncrease(statName, delta);
        }

        private int FindMaxAffordablePending(string statName, int maxPending, int budget)
        {
            if (maxPending <= 0 || budget <= 0)
                return 0;

            int lo = 0;
            int hi = maxPending;
            int best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int cost = mid > 0 ? _context.Character.GetIpCostForStatIncrease(statName, mid) : 0;
                if (cost <= budget)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best;
        }

        private int GetNextPointCost(int statId, int projectedBase, string statName)
        {
            if (AbilityStatIds.Contains(statId))
            {
                int factor = AO.Core.Characters.Character.GetAbilityCostFactorForBreed?.Invoke(statId, _context.CharacterBreedId) ?? 2;
                return Mathf.Max(0, factor * projectedBase);
            }

            float factorCost = _context.GetSkillCostFactorForCurrentProfession(statId) ?? 0f;
            if (factorCost > 0f)
                return Mathf.Max(1, Mathf.CeilToInt(projectedBase * factorCost));

            return Mathf.Max(0, _context.Character.GetIpCostForStatIncrease(statName, 1));
        }

        private int ResolveMaxAllowedForUi(int statId, int initialBase, int fallback)
        {
            if (AbilityStatIds.Contains(statId))
            {
                int abilityPerLevel = Mathf.Max(1, _context.GetAbilityImprovementsPerLevel());
                int level = Mathf.Max(1, _context.CharacterLevel);
                int abilityCap = initialBase + (abilityPerLevel * level);

                int? dataCap = _context.GetStatCapById(statId);
                if (dataCap.HasValue)
                    return Mathf.Min(dataCap.Value, abilityCap);

                return abilityCap;
            }

            return _context.GetStatCapById(statId) ?? fallback;
        }

        private static bool IsModifierPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
#else
            return Input.GetKey(KeyCode.LeftShift) ||
                   Input.GetKey(KeyCode.RightShift) ||
                   Input.GetKey(KeyCode.LeftControl) ||
                   Input.GetKey(KeyCode.RightControl);
#endif
        }

        private static Color ResolveSkillColor(string colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
                return Color.white;

            string n = colorName.Trim().ToLowerInvariant();
            return n switch
            {
                "green" => new Color(0.45f, 0.95f, 0.45f, 1f),
                "aqua" => new Color(0.40f, 0.95f, 0.90f, 1f),
                "teal-light-blue" => new Color(0.40f, 0.75f, 1.00f, 1f),
                "light-blue-dark-blue" => new Color(0.33f, 0.62f, 1.00f, 1f),
                "dark-blue" => new Color(0.35f, 0.50f, 0.95f, 1f),
                "light blue" => new Color(0.45f, 0.75f, 1.00f, 1f),
                _ => Color.white
            };
        }

        private int GetProjectedTrickleBonus(int statId)
        {
            if (AbilityStatIds.Contains(statId))
                return 0;

            var weights = _context.GetSkillTrickleWeights(statId);
            if (weights == null || weights.Count < 6)
                return _context.Character.StatsContainer.GetTrickleBonus(statId);

            float total = 0f;
            for (int i = 0; i < AbilityStatIds.Length; i++)
            {
                int abilityId = AbilityStatIds[i];
                int abilityBuffed = _context.GetModifiedStatValue(abilityId);
                if (_pendingByStatId.TryGetValue(abilityId, out int pendingAbility))
                    abilityBuffed += pendingAbility;

                total += (weights[i] / 4f) * abilityBuffed;
            }

            return Mathf.Max(0, Mathf.FloorToInt(total));
        }

        private int ResolveBuffedStat(int statId, int currentBaseRaw, int pendingDelta)
        {
            int baseSkill = currentBaseRaw + pendingDelta;
            int modified = _context.GetModifiedStatValue(statId);
            int statContainerDelta = modified - currentBaseRaw;
            int activeDelta = _context.GetActiveProgramModifierValue(statId);
            int abilityTrickleFromActive = GetAbilityTrickleDeltaFromActivePrograms(statId);
            int abilityTrickleFromPending = GetAbilityTrickleDeltaFromPending(statId);
            return baseSkill + statContainerDelta + activeDelta + abilityTrickleFromActive + abilityTrickleFromPending;
        }

        private int GetAbilityTrickleDeltaFromActivePrograms(int statId)
        {
            if (AbilityStatIds.Contains(statId))
                return 0;

            var weights = _context.GetSkillTrickleWeights(statId);
            if (weights == null || weights.Count < 6)
                return 0;

            float total = 0f;
            for (int i = 0; i < AbilityStatIds.Length; i++)
            {
                int abilityId = AbilityStatIds[i];
                int activeAbilityDelta = _context.GetActiveProgramModifierValue(abilityId);
                total += (weights[i] / 4f) * activeAbilityDelta;
            }

            return Mathf.FloorToInt(total);
        }

        private int GetAbilityTrickleDeltaFromPending(int statId)
        {
            if (AbilityStatIds.Contains(statId))
                return 0;

            var weights = _context.GetSkillTrickleWeights(statId);
            if (weights == null || weights.Count < 6)
                return 0;

            float total = 0f;
            for (int i = 0; i < AbilityStatIds.Length; i++)
            {
                int abilityId = AbilityStatIds[i];
                if (!_pendingByStatId.TryGetValue(abilityId, out int pendingAbility) || pendingAbility == 0)
                    continue;

                total += (weights[i] / 4f) * pendingAbility;
            }

            return Mathf.FloorToInt(total);
        }

        private sealed class DisplayData
        {
            public int BaseSkill { get; set; }
            public int BuffedSkill { get; set; }
            public int MaxSkillAllowed { get; set; }
            public int CurrentImprovement { get; set; }
            public int CostToImprove { get; set; }
            public int TotalCost { get; set; }
            public float Progress01 { get; set; }
        }
    }
}
