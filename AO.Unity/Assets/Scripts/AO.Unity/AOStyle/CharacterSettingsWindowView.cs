using System.Collections.Generic;
using System.Linq;
using AO.Unity.Prototype;
using AO.Unity.World;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class CharacterSettingsWindowView : MonoBehaviour
    {
        private static readonly Dictionary<int, string> DefaultBreeds = new()
        {
            { 0, "Unknown" }, { 1, "Solitus" }, { 2, "Opifex" }, { 3, "Nanomage" }, { 4, "Atrox" }, { 7, "HumanMonster" }
        };

        private static readonly Dictionary<int, string> DefaultProfessions = new()
        {
            { 0, "Unknown" }, { 1, "Soldier" }, { 2, "MartialArtist" }, { 3, "Engineer" }, { 4, "Fixer" },
            { 5, "Agent" }, { 6, "Adventurer" }, { 7, "Trader" }, { 8, "Bureaucrat" }, { 9, "Enforcer" },
            { 10, "Doctor" }, { 11, "NanoTechnician" }, { 12, "MetaPhysicist" }, { 13, "Monster" }, { 14, "Keeper" }, { 15, "Shade" }
        };

        private PrototypeUiContext _context;
        private Font _font;
        private InputField _levelInput;
        private InputField _xpInput;
        private InputField _runSpeedInput;
        private InputField _jumpHeightInput;
        private Dropdown _movementModeDropdown;
        private Dropdown _breedDropdown;
        private Dropdown _sexDropdown;
        private Dropdown _professionDropdown;
        private Text _visualProfessionValueText;
        private Dropdown _headDropdown;
        private readonly List<int> _breedIds = new();
        private readonly List<CharacterRuntimeBridge.CharacterSex> _sexIds = new();
        private readonly List<int> _professionIds = new();
        private readonly List<string> _headKeys = new();
        private readonly List<PrototypeWalkerController.MovementMode> _movementModes = new();
        private bool _suppressSelectionCallbacks;

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

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("CharacterSettingsRoot", transform, new Color(0f, 0f, 0f, 0f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var topRow = AOStyleUiFactory.CreatePanel("LevelRow", root, new Color(0f, 0f, 0f, 0f));
            topRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var topLayout = topRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            topLayout.spacing = 6f;
            topLayout.childControlWidth = false;
            topLayout.childForceExpandWidth = false;
            topLayout.childControlHeight = true;
            topLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("LevelLabel", topRow, "Level", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            _levelInput = AOStyleUiFactory.CreateInputField("LevelInput", topRow, "1", _font, 120f);

            var breedRow = AOStyleUiFactory.CreatePanel("BreedRow", root, new Color(0f, 0f, 0f, 0f));
            breedRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var breedLayout = breedRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            breedLayout.spacing = 6f;
            breedLayout.childControlWidth = false;
            breedLayout.childForceExpandWidth = false;
            breedLayout.childControlHeight = true;
            breedLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("BreedLabel", breedRow, "Breed", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            _breedDropdown = CreateDropdown("BreedDropdown", breedRow, 360f);
            _breedDropdown.onValueChanged.AddListener(_ => OnBreedSelectionChanged());

            var sexRow = AOStyleUiFactory.CreatePanel("SexRow", root, new Color(0f, 0f, 0f, 0f));
            sexRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var sexLayout = sexRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            sexLayout.spacing = 6f;
            sexLayout.childControlWidth = false;
            sexLayout.childForceExpandWidth = false;
            sexLayout.childControlHeight = true;
            sexLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("SexLabel", sexRow, "S/G", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            _sexDropdown = CreateDropdown("SexDropdown", sexRow, 360f);

            var profRow = AOStyleUiFactory.CreatePanel("ProfessionRow", root, new Color(0f, 0f, 0f, 0f));
            profRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var profLayout = profRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            profLayout.spacing = 6f;
            profLayout.childControlWidth = false;
            profLayout.childForceExpandWidth = false;
            profLayout.childControlHeight = true;
            profLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("ProfessionLabel", profRow, "Profession", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;
            _professionDropdown = CreateDropdown("ProfessionDropdown", profRow, 360f);

            var visualProfRow = AOStyleUiFactory.CreatePanel("VisualProfessionRow", root, new Color(0f, 0f, 0f, 0f));
            visualProfRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            var visualProfLayout = visualProfRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            visualProfLayout.spacing = 6f;
            visualProfLayout.childControlWidth = false;
            visualProfLayout.childForceExpandWidth = false;
            visualProfLayout.childControlHeight = true;
            visualProfLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("VisualProfessionLabel", visualProfRow, "VisualProfession", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 100f;
            _visualProfessionValueText = AOStyleUiFactory.CreateText("VisualProfessionValue", visualProfRow, string.Empty, _font, 12, TextAnchor.MiddleLeft);
            _visualProfessionValueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 320f;
            _visualProfessionValueText.color = new Color(0.84f, 0.93f, 1f, 0.96f);

            var headRow = AOStyleUiFactory.CreatePanel("HeadRow", root, new Color(0f, 0f, 0f, 0f));
            headRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var headLayout = headRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            headLayout.spacing = 6f;
            headLayout.childControlWidth = false;
            headLayout.childForceExpandWidth = false;
            headLayout.childControlHeight = true;
            headLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("HeadLabel", headRow, "Head", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;
            _headDropdown = CreateDropdown("HeadDropdown", headRow, 440f);
            _headDropdown.onValueChanged.AddListener(_ => OnHeadSelectionChanged());

            var xpRow = AOStyleUiFactory.CreatePanel("XpRow", root, new Color(0f, 0f, 0f, 0f));
            xpRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var xpLayout = xpRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            xpLayout.spacing = 6f;
            xpLayout.childControlWidth = false;
            xpLayout.childForceExpandWidth = false;
            xpLayout.childControlHeight = true;
            xpLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("XPLabel", xpRow, "XP", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;
            _xpInput = AOStyleUiFactory.CreateInputField("XPInput", xpRow, "0", _font, 360f);

            var movementRow = AOStyleUiFactory.CreatePanel("MovementRow", root, new Color(0f, 0f, 0f, 0f));
            movementRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var movementLayout = movementRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            movementLayout.spacing = 6f;
            movementLayout.childControlWidth = false;
            movementLayout.childForceExpandWidth = false;
            movementLayout.childControlHeight = true;
            movementLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("RunSpeedLabel", movementRow, "Run", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            _runSpeedInput = AOStyleUiFactory.CreateInputField("RunSpeedInput", movementRow, "8", _font, 90f);

            AOStyleUiFactory.CreateText("JumpHeightLabel", movementRow, "Jump", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 45f;
            _jumpHeightInput = AOStyleUiFactory.CreateInputField("JumpHeightInput", movementRow, "1.2", _font, 90f);

            var movementModeRow = AOStyleUiFactory.CreatePanel("MovementModeRow", root, new Color(0f, 0f, 0f, 0f));
            movementModeRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var movementModeLayout = movementModeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            movementModeLayout.spacing = 6f;
            movementModeLayout.childControlWidth = false;
            movementModeLayout.childForceExpandWidth = false;
            movementModeLayout.childControlHeight = true;
            movementModeLayout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("MovementModeLabel", movementModeRow, "Mode", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            _movementModeDropdown = CreateDropdown("MovementModeDropdown", movementModeRow, 220f);

            AOStyleUiFactory.CreateButton("ApplySettings", root, "Set Character", _font, ApplySettings, 140f);
        }

        private Dropdown CreateDropdown(string name, Transform parent, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.16f, 0.2f, 0.28f, 1f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 22f;

            var dropdown = go.GetComponent<Dropdown>();

            var label = AOStyleUiFactory.CreateText("Label", go.transform, "", _font, 12, TextAnchor.MiddleLeft);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-24f, -2f);

            var arrow = AOStyleUiFactory.CreateText("Arrow", go.transform, "v", _font, 12, TextAnchor.MiddleCenter);
            var arrowRt = (RectTransform)arrow.transform;
            arrowRt.anchorMin = new Vector2(1f, 0f);
            arrowRt.anchorMax = new Vector2(1f, 1f);
            arrowRt.pivot = new Vector2(1f, 0.5f);
            arrowRt.sizeDelta = new Vector2(18f, 0f);
            arrowRt.anchoredPosition = new Vector2(-4f, 0f);

            var template = AOStyleUiFactory.CreatePanel("Template", go.transform, new Color(0.07f, 0.09f, 0.13f, 0.98f));
            template.gameObject.SetActive(false);
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -2f);
            template.sizeDelta = new Vector2(0f, 150f);
            template.gameObject.AddComponent<ScrollRect>().vertical = true;
            template.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var viewport = AOStyleUiFactory.CreatePanel("Viewport", template, new Color(0f, 0f, 0f, 0.01f));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            var contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 0f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var contentFitter = content.GetComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var item = AOStyleUiFactory.CreatePanel("Item", contentRt, new Color(0.16f, 0.2f, 0.28f, 1f));
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            item.gameObject.AddComponent<Toggle>();
            var itemLabel = AOStyleUiFactory.CreateText("ItemLabel", item, "Option", _font, 12, TextAnchor.MiddleLeft);
            itemLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            itemLabel.verticalOverflow = VerticalWrapMode.Overflow;
            var itemLabelRt = (RectTransform)itemLabel.transform;
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(6f, 0f);
            itemLabelRt.offsetMax = new Vector2(-6f, 0f);

            dropdown.template = template;
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;

            var scrollRect = template.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = contentRt;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            return dropdown;
        }

        private void Refresh()
        {
            if (_context == null)
                return;

            _suppressSelectionCallbacks = true;
            _levelInput.text = _context.CharacterLevel.ToString();
            var breeds = _context.GetBreedLookup();
            if (breeds == null || breeds.Count == 0)
                breeds = DefaultBreeds;

            var professions = _context.GetProfessionLookup();
            if (professions == null || professions.Count == 0)
                professions = DefaultProfessions;
            var headLookup = _context.GetHeadVisualLookup() ?? new Dictionary<string, string>();
            if (_runSpeedInput != null)
                _runSpeedInput.text = _context.GetRunSpeed().ToString("0.##");
            if (_jumpHeightInput != null)
                _jumpHeightInput.text = _context.GetJumpHeight().ToString("0.##");
            FillMovementModeLookup(_context.GetMovementMode());

            FillLookup(breeds, _breedDropdown, _breedIds, _context.CharacterBreedId);
            FillSexLookup(_context.CharacterBreedId, _context.CharacterSex);
            FillLookup(professions, _professionDropdown, _professionIds, _context.CharacterProfessionId);
            if (_visualProfessionValueText != null)
                _visualProfessionValueText.text = _context.GetCurrentVisualProfessionName();
            FillHeadLookup(headLookup, _context.CharacterHeadMeshKey);
            _suppressSelectionCallbacks = false;
        }

        private static void FillLookup(
            IReadOnlyDictionary<int, string> lookup,
            Dropdown dropdown,
            List<int> idList,
            int selectedId)
        {
            idList.Clear();
            dropdown.options.Clear();

            foreach (var pair in lookup.OrderBy(k => k.Key))
            {
                idList.Add(pair.Key);
                dropdown.options.Add(new Dropdown.OptionData($"{pair.Key}: {pair.Value}"));
            }

            int idx = idList.FindIndex(v => v == selectedId);
            dropdown.value = idx >= 0 ? idx : 0;
            dropdown.RefreshShownValue();
            if (dropdown.captionText != null && dropdown.options.Count > 0 && dropdown.value >= 0 && dropdown.value < dropdown.options.Count)
                dropdown.captionText.text = dropdown.options[dropdown.value].text;
        }

        private void FillSexLookup(int breedId, CharacterRuntimeBridge.CharacterSex selectedSex)
        {
            if (_sexDropdown == null)
                return;

            _sexIds.Clear();
            _sexDropdown.options.Clear();

            bool atrox = breedId == 4;
            if (atrox)
            {
                _sexIds.Add(CharacterRuntimeBridge.CharacterSex.Uni);
                _sexDropdown.options.Add(new Dropdown.OptionData("Uni"));
                _sexDropdown.value = 0;
                _sexDropdown.interactable = false;
            }
            else
            {
                _sexIds.Add(CharacterRuntimeBridge.CharacterSex.Male);
                _sexIds.Add(CharacterRuntimeBridge.CharacterSex.Female);
                _sexDropdown.options.Add(new Dropdown.OptionData("Male"));
                _sexDropdown.options.Add(new Dropdown.OptionData("Female"));
                _sexDropdown.value = selectedSex == CharacterRuntimeBridge.CharacterSex.Female ? 1 : 0;
                _sexDropdown.interactable = true;
            }

            var bg = _sexDropdown.GetComponent<Image>();
            if (bg != null)
                bg.color = _sexDropdown.interactable
                    ? new Color(0.16f, 0.2f, 0.28f, 1f)
                    : new Color(0.24f, 0.24f, 0.24f, 0.8f);

            _sexDropdown.RefreshShownValue();
        }

        private void OnBreedSelectionChanged()
        {
            if (_suppressSelectionCallbacks)
                return;

            if (_breedDropdown == null || _breedIds.Count == 0)
                return;

            int selectedBreedId = _breedDropdown.value >= 0 && _breedDropdown.value < _breedIds.Count
                ? _breedIds[_breedDropdown.value]
                : 1;
            var selectedSex = _sexIds.Count > 0 && _sexDropdown != null && _sexDropdown.value >= 0 && _sexDropdown.value < _sexIds.Count
                ? _sexIds[_sexDropdown.value]
                : CharacterRuntimeBridge.CharacterSex.Male;
            FillSexLookup(selectedBreedId, selectedSex);
        }

        private void FillHeadLookup(IReadOnlyDictionary<string, string> lookup, string selectedKey)
        {
            if (_headDropdown == null)
                return;

            _headKeys.Clear();
            _headDropdown.options.Clear();

            foreach (var pair in lookup.OrderBy(k => k.Value))
            {
                _headKeys.Add(pair.Key);
                _headDropdown.options.Add(new Dropdown.OptionData(pair.Value));
            }

            int idx = _headKeys.FindIndex(v => string.Equals(v, selectedKey ?? string.Empty, System.StringComparison.OrdinalIgnoreCase));
            _headDropdown.value = idx >= 0 ? idx : 0;
            _headDropdown.RefreshShownValue();
        }

        private void OnHeadSelectionChanged()
        {
            if (_suppressSelectionCallbacks)
                return;

            if (_context == null || _headDropdown == null || _headKeys.Count == 0)
                return;

            string selectedHeadKey = _headDropdown.value >= 0 && _headDropdown.value < _headKeys.Count
                ? _headKeys[_headDropdown.value]
                : string.Empty;
            _context.SetDebugHeadVisual(selectedHeadKey);
        }

        private void FillMovementModeLookup(PrototypeWalkerController.MovementMode selectedMode)
        {
            if (_movementModeDropdown == null)
                return;

            _movementModes.Clear();
            _movementModeDropdown.options.Clear();

            _movementModes.Add(PrototypeWalkerController.MovementMode.Grounded);
            _movementModes.Add(PrototypeWalkerController.MovementMode.Flight);
            _movementModeDropdown.options.Add(new Dropdown.OptionData("Grounded"));
            _movementModeDropdown.options.Add(new Dropdown.OptionData("Flight Mode"));

            int idx = _movementModes.FindIndex(v => v == selectedMode);
            _movementModeDropdown.value = idx >= 0 ? idx : 0;
            _movementModeDropdown.RefreshShownValue();
        }

        private void ApplySettings()
        {
            int level = 1;
            if (_levelInput != null && int.TryParse(_levelInput.text, out int parsedLevel))
                level = Mathf.Max(1, parsedLevel);

            long xpToAdd = 0;
            if (_xpInput != null && long.TryParse(_xpInput.text, out long parsedXp))
                xpToAdd = System.Math.Max(0L, parsedXp);

            int breedId = _breedIds.Count > 0 && _breedDropdown.value >= 0 && _breedDropdown.value < _breedIds.Count
                ? _breedIds[_breedDropdown.value]
                : 1;
            int professionId = _professionIds.Count > 0 && _professionDropdown.value >= 0 && _professionDropdown.value < _professionIds.Count
                ? _professionIds[_professionDropdown.value]
                : 1;
            var sex = _sexIds.Count > 0 && _sexDropdown != null && _sexDropdown.value >= 0 && _sexDropdown.value < _sexIds.Count
                ? _sexIds[_sexDropdown.value]
                : CharacterRuntimeBridge.CharacterSex.Male;
            float runSpeed = _context.GetRunSpeed();
            if (_runSpeedInput != null && float.TryParse(_runSpeedInput.text, out float parsedRunSpeed))
                runSpeed = Mathf.Max(0.1f, parsedRunSpeed);
            float jumpHeight = _context.GetJumpHeight();
            if (_jumpHeightInput != null && float.TryParse(_jumpHeightInput.text, out float parsedJumpHeight))
                jumpHeight = Mathf.Max(0f, parsedJumpHeight);
            var movementMode = _movementModes.Count > 0 && _movementModeDropdown != null
                && _movementModeDropdown.value >= 0 && _movementModeDropdown.value < _movementModes.Count
                ? _movementModes[_movementModeDropdown.value]
                : PrototypeWalkerController.MovementMode.Grounded;

            _context.RequestCharacterSettings(level, breedId, professionId, sex, xpToAdd);
            _context.ApplyMovementSettings(runSpeed, jumpHeight, movementMode);
        }
    }
}
