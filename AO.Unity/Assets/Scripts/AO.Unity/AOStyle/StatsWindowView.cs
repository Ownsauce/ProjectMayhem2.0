using AO.Core.Stats;
using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class StatsWindowView : MonoBehaviour
    {
        private sealed class StatBar
        {
            public Image Fill;
            public RectTransform FillRect;
            public Text ValueText;
            public Color FillColor;
        }

        private PrototypeUiContext _context;
        private Font _font;
        private RectTransform _contentRoot;
        private Text _nameText;
        private Text _levelText;
        private StatBar _healthBar;
        private StatBar _nanoBar;
        private StatBar _xpBar;
        private Text _duelScoreText;
        private Text _attackRatingText;
        private Text _projectileAcText;
        private Text _meleeAcText;
        private Text _energyAcText;
        private Text _chemicalAcText;
        private Text _radiationAcText;
        private Text _coldAcText;
        private Text _diseaseAcText;
        private Text _fireAcText;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _font = font;
            Build();
            _context.StateChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StateChanged -= Refresh;
        }

        private void Update()
        {
            Refresh();
        }

        private void Build()
        {
            _contentRoot = AOStyleUiFactory.CreatePanel("StatsRoot", transform, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            _contentRoot.anchorMin = Vector2.zero;
            _contentRoot.anchorMax = Vector2.one;
            _contentRoot.offsetMin = Vector2.zero;
            _contentRoot.offsetMax = Vector2.zero;

            var layout = _contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 3;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _nameText = AOStyleUiFactory.CreateText("Name", _contentRoot, string.Empty, _font, 12, TextAnchor.MiddleLeft);
            _nameText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            _levelText = AOStyleUiFactory.CreateText("Level", _contentRoot, string.Empty, _font, 12, TextAnchor.MiddleLeft);
            _levelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            _healthBar = CreateBar("HealthBar", _contentRoot, new Color(0.72f, 0.18f, 0.22f, 1f));
            _nanoBar = CreateBar("NanoBar", _contentRoot, new Color(0.16f, 0.4f, 0.82f, 1f));
            _xpBar = CreateBar("XPBar", _contentRoot, new Color(0.78f, 0.56f, 0.16f, 1f));

            _duelScoreText = CreateStatLine("DuelScore", _contentRoot);
            _attackRatingText = CreateStatLine("AttackRating", _contentRoot);
            _projectileAcText = CreateStatLine("ProjectileAC", _contentRoot);
            _meleeAcText = CreateStatLine("MeleeAC", _contentRoot);
            _energyAcText = CreateStatLine("EnergyAC", _contentRoot);
            _chemicalAcText = CreateStatLine("ChemicalAC", _contentRoot);
            _radiationAcText = CreateStatLine("RadiationAC", _contentRoot);
            _coldAcText = CreateStatLine("ColdAC", _contentRoot);
            _diseaseAcText = CreateStatLine("DiseaseAC", _contentRoot);
            _fireAcText = CreateStatLine("FireAC", _contentRoot);
        }

        private StatBar CreateBar(string name, Transform parent, Color fillColor)
        {
            var host = AOStyleUiFactory.CreatePanel(name, parent, new Color(0.06f, 0.08f, 0.12f, 1f));
            host.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var track = AOStyleUiFactory.CreatePanel("Track", host, new Color(0.02f, 0.03f, 0.05f, 1f));
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 1f);
            track.offsetMin = new Vector2(1f, 1f);
            track.offsetMax = new Vector2(-1f, -1f);

            var fill = AOStyleUiFactory.CreatePanel("Fill", track, fillColor).GetComponent<Image>();
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var valueText = AOStyleUiFactory.CreateText("Value", track, string.Empty, _font, 11, TextAnchor.MiddleCenter);
            var valueRect = (RectTransform)valueText.transform;
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;

            return new StatBar
            {
                Fill = fill,
                FillRect = fillRect,
                ValueText = valueText,
                FillColor = fillColor
            };
        }

        private Text CreateStatLine(string name, Transform parent)
        {
            var text = AOStyleUiFactory.CreateText(name, parent, string.Empty, _font, 11, TextAnchor.MiddleLeft);
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            return text;
        }

        private int GetStat(int statId)
        {
            return _context?.Character?.StatsContainer?.GetFinalStat(statId) ?? 0;
        }

        private static int ResolveCurrentPool(int primary, int fallback, int max)
        {
            if (primary > 0) return primary;
            if (fallback > 0) return fallback;
            return Mathf.Max(0, max);
        }

        private static int ResolveMaxValue(int primary, int fallback, int current)
        {
            int max = primary > 0 ? primary : fallback;
            if (max <= 0)
                max = current;
            return Mathf.Max(1, max);
        }

        private static void SetBar(StatBar bar, string label, int current, int max)
        {
            if (bar == null)
                return;

            int safeCurrent = Mathf.Max(0, current);
            int safeMax = Mathf.Max(1, max);
            float fill = Mathf.Clamp01((float)safeCurrent / safeMax);
            if (bar.Fill != null)
            {
                bar.Fill.color = bar.FillColor;
            }
            if (bar.FillRect != null)
                bar.FillRect.anchorMax = new Vector2(fill, 1f);
            if (bar.ValueText != null)
                bar.ValueText.text = $"{label}: {safeCurrent} / {safeMax}";
        }

        private void Refresh()
        {
            if (_contentRoot == null || _context?.Character == null)
                return;

            int level = _context.CharacterLevel;
            _nameText.text = $"Name: {_context.GetSelfDisplayName()}";
            _levelText.text = $"Level: {level}";

            int healthMax = Mathf.Max(1, _context.GetMaxHealthValue());
            int healthCurrent = Mathf.Clamp(_context.GetCurrentHealthValue(), 0, healthMax);

            int nanoMax = Mathf.Max(1, _context.GetMaxNanoValue());
            int nanoCurrent = Mathf.Clamp(_context.GetCurrentNanoValue(), 0, nanoMax);

            int xpCurrent = 0;
            int xpMax = 0;
            if (_context.Character?.Level != null)
            {
                xpCurrent = (int)Mathf.Clamp(_context.Character.Level.Experience, 0, int.MaxValue);
                xpMax = (int)Mathf.Clamp(_context.Character.GetExperienceToNextLevel(), 0, int.MaxValue);
            }

            int serverXp = _context.Character.StatsContainer.GetBaseStat(StatIds.XP);
            int serverLastXp = _context.Character.StatsContainer.GetBaseStat(StatIds.LastXP);
            int serverNextXp = _context.Character.StatsContainer.GetBaseStat(StatIds.NextXP);
            if (serverNextXp > serverLastXp && serverXp >= serverLastXp)
            {
                xpCurrent = Mathf.Max(0, serverXp - serverLastXp);
                xpMax = Mathf.Max(1, serverNextXp - serverLastXp);
            }

            if (_context.TryGetAuthoritativeStats(out _, out _, out _, out _, out long authoritativeExperience))
            {
                xpCurrent = (int)Mathf.Clamp(authoritativeExperience, 0, int.MaxValue);
            }

            SetBar(_healthBar, "Health", healthCurrent, healthMax);
            SetBar(_nanoBar, "Nano", nanoCurrent, nanoMax);

            xpMax = ResolveMaxValue(xpMax, 0, xpCurrent);
            SetBar(_xpBar, "Experience", xpCurrent, xpMax);

            _duelScoreText.text = $"Duel score: {GetStat(StatIds.PvpDuelScore)}";
            _attackRatingText.text = $"Attack rating: {_context.GetCurrentAttackRating()}";
            _projectileAcText.text = $"Projectile AC: {GetStat(StatIds.ProjectileAC)}";
            _meleeAcText.text = $"Melee AC: {GetStat(StatIds.MeleeAC)}";
            _energyAcText.text = $"Energy AC: {GetStat(StatIds.EnergyAC)}";
            _chemicalAcText.text = $"Chemical AC: {GetStat(StatIds.ChemicalAC)}";
            _radiationAcText.text = $"Radiation AC: {GetStat(StatIds.RadiationAC)}";
            _coldAcText.text = $"Cold AC: {GetStat(StatIds.ColdAC)}";
            _diseaseAcText.text = $"Disease AC: {GetStat(StatIds.PoisonAC)}";
            _fireAcText.text = $"Fire AC: {GetStat(StatIds.FireAC)}";
        }
    }
}
