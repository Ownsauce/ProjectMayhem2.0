using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using AO.Unity.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public sealed class CharacterSelectionWindowView : MonoBehaviour
    {
        public enum BodyHeightPreset
        {
            Short = 0,
            Medium = 1,
            Tall = 2
        }

        public enum BodyWeightPreset
        {
            Skinny = 0,
            Medium = 1,
            Fat = 2
        }

        public sealed class CharacterProfile
        {
            public string ServerCharacterId;
            public string Name;
            public int Level;
            public int BreedId;
            public CharacterRuntimeBridge.CharacterSex Sex;
            public int ProfessionId;
            public string ProfessionName;
            public string BreedLabel;
            public BodyHeightPreset Height;
            public BodyWeightPreset Weight;
            public string HeadMeshKey;
            public int StartPlayfieldId;
        }

        private sealed class PreviewDragProxy : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public Action<Vector2> OnDragDelta;
            private Vector2 _lastPosition;

            public void OnBeginDrag(PointerEventData eventData)
            {
                _lastPosition = eventData != null ? eventData.position : Vector2.zero;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (eventData == null)
                    return;

                Vector2 delta = eventData.delta;
                if (delta.sqrMagnitude <= 0.0001f)
                {
                    delta = eventData.position - _lastPosition;
                }

                _lastPosition = eventData.position;
                if (delta.sqrMagnitude > 0.0001f)
                    OnDragDelta?.Invoke(delta);
            }
        }

        private enum ScreenState
        {
            Select = 0,
            CreateBreedSex = 1,
            CreateBody = 2,
            CreateProfession = 3,
            CreateName = 4
        }

        private sealed class CreationDraft
        {
            public int BreedId = 1;
            public CharacterRuntimeBridge.CharacterSex Sex = CharacterRuntimeBridge.CharacterSex.Male;
            public BodyHeightPreset Height = BodyHeightPreset.Medium;
            public BodyWeightPreset Weight = BodyWeightPreset.Medium;
            public int ProfessionId = 1;
            public string ProfessionName = "Soldier";
            public string Name = string.Empty;
            public string HeadMeshKey = string.Empty;
        }

        private static readonly Dictionary<int, string> DefaultProfessionNames = new()
        {
            { 1, "Soldier" }, { 2, "Martial Artist" }, { 3, "Engineer" }, { 4, "Fixer" }, { 5, "Agent" },
            { 6, "Adventurer" }, { 7, "Trader" }, { 8, "Bureaucrat" }, { 9, "Enforcer" }, { 10, "Doctor" },
            { 11, "Nano-Technician" }, { 12, "Meta-Physicist" }, { 14, "Keeper" }, { 15, "Shade" }
        };

        private static readonly Dictionary<int, string> ProfessionDescriptions = new()
        {
            { 1, "Soldier strives for excellence in armed combat. Soldiers focus on assault and survival, using nanotechnology to protect their bodies, enhance reflexes, weapon skills, and armor. Most importantly, Soldiers can create strong damage absorption shields that make them partly invulnerable and reflect damage back to attackers." },
            { 2, "When it comes to dishing out raw combat damage a Martial Artist outshines all other professions. Fighting unarmed, they rely on special attacks and striking weak spots. Martial Artists are also very proficient healers, surpassed mainly by Doctors and Adventurers." },
            { 3, "An Engineer specializes in creating machinery. Engineers excel at building powerful battledroids and use unique nanotechnology to enhance and repair them. Their own weapon skills are modest, but the engineer-and-robot combination is formidable. High-end Engineers can also teleport team members to their location." },
            { 4, "Fixers specialize in getting people what they need when they need it. By hacking The Grid, they can transport themselves or teams around Rubi-Ka. Fixers move fastest, are hard to hit, and use mobility control plus sub-machineguns as their core combat style." },
            { 5, "An Agent’s life is spent in the shadows. Agents focus on concealment and subterfuge, and can go undercover to use nanotechnology normally tied to other professions. In combat they specialize in long-range rifle sniping and precision burst damage." },
            { 6, "An Adventurer’s soul is at home in the wild. Adventurers balance melee and ranged combat, use nanotechnology for defense and damage shields, and are excellent healers—rivaled mainly by Doctors." },
            { 7, "Traders are ultimate entrepreneurs. In combat they use unique nanotechnology to drain skills, energy, and health from opponents, transferring those benefits to themselves or allies, often reducing powerful enemies to weakened shells." },
            { 8, "The Bureaucrat brings order to chaos. Bureaucrats have limited weapon skills but strong nanotechnology, including direct damage, robotic assistants, crowd control, leadership buffs, and mental control over hostile beings. In teams, they are natural leaders." },
            { 9, "An Enforcer specializes in close combat through raw power and rage. Enforcers are physically built to sustain heavy damage and can use protective nanotechnology to survive even more. They naturally draw enemy aggression and depend on teammates to keep them healed." },
            { 10, "Doctors are biotechnology specialists. Their prime strengths are healing and protection, while also wielding debilitating biotoxins. Solo play can be challenging, but in teams the Doctor is often the deciding factor between victory and collapse." },
            { 11, "Nano-Technicians are expert users of aggressive nanotechnology, including explosive area damage and broad damage-type coverage. They can also use utility nanotechnology like warps. Their heavy nano focus comes at the cost of physical and weapon skill growth." },
            { 12, "Meta-Physicists draw power from the other side, manifesting emotions into combat entities and controlling multiple summons. They manipulate the nanotechnology fabric itself, boosting allies and weakening foes. Weapon skills are weaker, but direct nano offense is strong." },
            { 14, "The Keeper is a close-combat fighter radiating valour and heroism. Keepers excel with two-handed edged weapons and uniquely share life and supportive auras with nearby allies. As a notum-dependent engineered profession, Keepers originate in Jobe research facilities." },
            { 15, "The Shade is a predator-parasite hybrid. Aggressive and elusive, Shades drain life and energy from opponents while avoiding retaliation through concealment and mobility. They cannot wear normal armor, relying on nanotechnological tattoos for protection. Shades originate in Jobe research facilities." }
        };

        private readonly List<CharacterProfile> _profiles = new();
        private readonly List<Button> _profileButtons = new();
        private readonly List<Button> _choiceButtons = new();
        private readonly List<Button> _professionButtons = new();
        private readonly List<Image> _carouselDots = new();
        private readonly CreationDraft _draft = new();
        private readonly List<int> _professionIds = new();
        private readonly Dictionary<int, string> _professionNames = new();
        private readonly Dictionary<string, InputField> _headEditDefaultInputs = new(StringComparer.OrdinalIgnoreCase);
        private const float HeadEditPositionStep = 0.005f;
        private const float HeadEditRotationStep = 2f;

        private Action<CharacterProfile> _onPlay;
        private Action _onBack;
        private Action<bool> _onCreateFlowStateChanged;
        private Action<Vector2> _onPreviewDragged;
        private Func<int, CharacterRuntimeBridge.CharacterSex, IReadOnlyDictionary<string, string>> _headLookupProvider;
        private Action<int, CharacterRuntimeBridge.CharacterSex> _onPreviewBreedSexChanged;
        private Action<BodyHeightPreset, BodyWeightPreset> _onPreviewBodyChanged;
        private Action<string> _onPreviewHeadChanged;
        private Action<Vector3> _onHeadEditOffsetDelta;
        private Action<Vector3> _onHeadEditEulerDelta;
        private Func<bool> _onHeadEditSave;
        private Action<int> _onSelectionChanged;
        private Action<int, string, string> _onProfessionSelected;
        private Action<bool> _onProfessionStepVisibilityChanged;
        private Action _onProfilesChanged;
        private Font _font;
        private RectTransform _root;
        private RectTransform _content;
        private RectTransform _stepContent;
        private RectTransform _sideListContent;
        private RectTransform _infoCard;
        private RectTransform _sidePanel;
        private ScreenState _screenState;
        private int _selectedProfileIndex = -1;

        private Text _titleText;
        private Text _detailsText;
        private Text _statusText;
        private InputField _nameInput;
        private Button _playButton;
        private Button _editAppearanceButton;
        private Button _createButton;
        private Button _deleteButton;
        private Button _backButton;
        private RectTransform _mainButtonRow;
        private RectTransform _deleteConfirmPanel;
        private InputField _deleteNameInput;
        private RawImage _previewRawImage;
        private Text _centerNameplateText;
        private Text _previewOverlayText;
        private Text _professionDescriptionText;
        private RawImage _backgroundImage;
        private readonly List<string> _headKeys = new();
        private int _selectedHeadIndex;
        private Text _headValueText;
        private bool _editingExistingProfile;

        public void SetConnectionStatus(string message)
        {
            if (_statusText == null)
                return;
            _statusText.text = message ?? string.Empty;
            _statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
        }

        private static readonly Color AccentPrimary = new(0.23f, 0.87f, 0.95f, 0.95f);
        private static readonly Color AccentPrimarySoft = new(0.23f, 0.87f, 0.95f, 0.35f);
        private static readonly Color PanelBg = new(0.04f, 0.09f, 0.14f, 0.95f);

        public void Initialize(
            Font font,
            IReadOnlyDictionary<int, string> professionLookup,
            Action<CharacterProfile> onPlay,
            Action onBack,
            Func<int, CharacterRuntimeBridge.CharacterSex, IReadOnlyDictionary<string, string>> headLookupProvider = null,
            Action<int, CharacterRuntimeBridge.CharacterSex> onPreviewBreedSexChanged = null,
            Action<BodyHeightPreset, BodyWeightPreset> onPreviewBodyChanged = null,
            Action<string> onPreviewHeadChanged = null,
            Action<bool> onCreateFlowStateChanged = null,
            Action<Vector2> onPreviewDragged = null,
            Action<Vector3> onHeadEditOffsetDelta = null,
            Action<Vector3> onHeadEditEulerDelta = null,
            Func<bool> onHeadEditSave = null,
            Action<int> onSelectionChanged = null,
            Action<int, string, string> onProfessionSelected = null,
            Action<bool> onProfessionStepVisibilityChanged = null,
            Action onProfilesChanged = null)
        {
            _font = font;
            _onPlay = onPlay;
            _onBack = onBack;
            _headLookupProvider = headLookupProvider;
            _onPreviewBreedSexChanged = onPreviewBreedSexChanged;
            _onPreviewBodyChanged = onPreviewBodyChanged;
            _onPreviewHeadChanged = onPreviewHeadChanged;
            _onCreateFlowStateChanged = onCreateFlowStateChanged;
            _onPreviewDragged = onPreviewDragged;
            _onHeadEditOffsetDelta = onHeadEditOffsetDelta;
            _onHeadEditEulerDelta = onHeadEditEulerDelta;
            _onHeadEditSave = onHeadEditSave;
            _onSelectionChanged = onSelectionChanged;
            _onProfessionSelected = onProfessionSelected;
            _onProfessionStepVisibilityChanged = onProfessionStepVisibilityChanged;
            _onProfilesChanged = onProfilesChanged;
            _professionNames.Clear();
            _professionIds.Clear();

            foreach (var pair in DefaultProfessionNames)
                _professionNames[pair.Key] = pair.Value;

            if (professionLookup != null)
            {
                foreach (var pair in professionLookup)
                {
                    if (pair.Key <= 0 || string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    _professionNames[pair.Key] = pair.Value;
                }
            }

            foreach (var professionId in _professionNames.Keys.OrderBy(v => v))
            {
                if (professionId == 13)
                    continue;
                _professionIds.Add(professionId);
            }

            Build();
            ShowSelectionScreen();
        }

        public void SetProfiles(IEnumerable<CharacterProfile> profiles)
        {
            _profiles.Clear();
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile == null)
                        continue;
                    _profiles.Add(profile);
                }
            }

            if (_profiles.Count > 0)
                _selectedProfileIndex = 0;
            else
                _selectedProfileIndex = -1;

            if (_screenState == ScreenState.Select)
            {
                RebuildSelectionContent();
                _onSelectionChanged?.Invoke(_selectedProfileIndex);
            }
        }

        public IReadOnlyList<CharacterProfile> GetProfiles()
        {
            return _profiles;
        }

        public void ReturnToSelectionScreen()
        {
            ShowSelectionScreen();
        }

        private void Build()
        {
            _root = AOStyleUiFactory.CreatePanel("CharacterSelectionRoot", transform, new Color(0.01f, 0.03f, 0.06f, 0.98f));
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            _backgroundImage = CreateBackgroundImage(_root);

            var frame = AOStyleUiFactory.CreatePanel("Frame", _root, new Color(0.00f, 0.00f, 0.00f, 0.12f));
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;
            AddSciFiFrameAccents(frame);

            _titleText = AOStyleUiFactory.CreateText("Title", frame, "CHARACTER SELECT", _font, 30, TextAnchor.MiddleLeft);
            var titleRt = (RectTransform)_titleText.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.sizeDelta = new Vector2(1f, 1f);
            titleRt.anchoredPosition = new Vector2(-10000f, 10000f);
            _titleText.gameObject.SetActive(false);

            _infoCard = AOStyleUiFactory.CreatePanel("InfoCard", frame, new Color(0.07f, 0.10f, 0.16f, 0.55f));
            _infoCard.anchorMin = new Vector2(0.02f, 0.50f);
            _infoCard.anchorMax = new Vector2(0.19f, 0.93f);
            _infoCard.offsetMin = Vector2.zero;
            _infoCard.offsetMax = Vector2.zero;
            var infoOutline = _infoCard.gameObject.AddComponent<Outline>();
            infoOutline.effectColor = new Color(0.76f, 0.90f, 1f, 0.45f);
            infoOutline.effectDistance = new Vector2(1f, -1f);

            _detailsText = AOStyleUiFactory.CreateText("Details", _infoCard, string.Empty, _font, 14, TextAnchor.UpperLeft);
            var detailsRt = (RectTransform)_detailsText.transform;
            detailsRt.anchorMin = Vector2.zero;
            detailsRt.anchorMax = Vector2.one;
            detailsRt.offsetMin = new Vector2(10f, 10f);
            detailsRt.offsetMax = new Vector2(-10f, -10f);
            _detailsText.color = new Color(0.94f, 0.98f, 1f, 0.98f);
            _detailsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detailsText.verticalOverflow = VerticalWrapMode.Overflow;

            _content = AOStyleUiFactory.CreatePanel("Content", frame, new Color(0f, 0f, 0f, 0f));
            // Keep center/right stage so the left info/list panel remains visible.
            _content.anchorMin = new Vector2(0.20f, 0.02f);
            _content.anchorMax = new Vector2(0.99f, 0.98f);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            var contentOutline = _content.gameObject.AddComponent<Outline>();
            contentOutline.effectColor = new Color(0.60f, 0.82f, 1f, 0.22f);
            contentOutline.effectDistance = new Vector2(1f, -1f);

            _sidePanel = AOStyleUiFactory.CreatePanel("CharacterQuickList", frame, new Color(0.07f, 0.10f, 0.16f, 0.48f));
            _sidePanel.anchorMin = new Vector2(0.02f, 0.16f);
            _sidePanel.anchorMax = new Vector2(0.19f, 0.49f);
            _sidePanel.offsetMin = Vector2.zero;
            _sidePanel.offsetMax = Vector2.zero;
            var sideTitle = AOStyleUiFactory.CreateText("QuickListTitle", _sidePanel, "Characters", _font, 16, TextAnchor.MiddleLeft);
            var sideTitleRt = (RectTransform)sideTitle.transform;
            sideTitleRt.anchorMin = new Vector2(0f, 1f);
            sideTitleRt.anchorMax = new Vector2(1f, 1f);
            sideTitleRt.offsetMin = new Vector2(10f, -34f);
            sideTitleRt.offsetMax = new Vector2(-10f, -8f);

            _sideListContent = AOStyleUiFactory.CreatePanel("QuickListContent", _sidePanel, new Color(0f, 0f, 0f, 0f));
            _sideListContent.anchorMin = new Vector2(0f, 0f);
            _sideListContent.anchorMax = new Vector2(1f, 1f);
            _sideListContent.offsetMin = new Vector2(8f, 8f);
            _sideListContent.offsetMax = new Vector2(-8f, -40f);

            _stepContent = AOStyleUiFactory.CreatePanel("StepContent", frame, new Color(0.05f, 0.08f, 0.13f, 0.92f));
            _stepContent.anchorMin = new Vector2(0.02f, 0.16f);
            _stepContent.anchorMax = new Vector2(0.19f, 0.93f);
            _stepContent.offsetMin = Vector2.zero;
            _stepContent.offsetMax = Vector2.zero;
            _stepContent.gameObject.SetActive(false);

            _statusText = AOStyleUiFactory.CreateText("Status", frame, string.Empty, _font, 13, TextAnchor.MiddleLeft);
            var statusRt = (RectTransform)_statusText.transform;
            statusRt.anchorMin = new Vector2(0.02f, 0.08f);
            statusRt.anchorMax = new Vector2(0.68f, 0.14f);
            statusRt.offsetMin = Vector2.zero;
            statusRt.offsetMax = Vector2.zero;
            _statusText.color = new Color(0.83f, 0.90f, 0.97f, 0.88f);
            _statusText.gameObject.SetActive(false);

            _mainButtonRow = AOStyleUiFactory.CreatePanel("ButtonRow", frame, new Color(0f, 0f, 0f, 0f));
            _mainButtonRow.anchorMin = new Vector2(0.02f, 0.02f);
            _mainButtonRow.anchorMax = new Vector2(0.98f, 0.09f);
            _mainButtonRow.offsetMin = Vector2.zero;
            _mainButtonRow.offsetMax = Vector2.zero;

            var leftRow = AOStyleUiFactory.CreatePanel("ButtonRowLeft", _mainButtonRow, new Color(0f, 0f, 0f, 0f));
            leftRow.anchorMin = new Vector2(0f, 0f);
            leftRow.anchorMax = new Vector2(0.5f, 1f);
            leftRow.offsetMin = Vector2.zero;
            leftRow.offsetMax = Vector2.zero;
            var leftLayout = leftRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            leftLayout.spacing = 8f;
            leftLayout.childControlWidth = false;
            leftLayout.childForceExpandWidth = false;
            leftLayout.childAlignment = TextAnchor.MiddleLeft;

            var rightRow = AOStyleUiFactory.CreatePanel("ButtonRowRight", _mainButtonRow, new Color(0f, 0f, 0f, 0f));
            rightRow.anchorMin = new Vector2(0.5f, 0f);
            rightRow.anchorMax = new Vector2(1f, 1f);
            rightRow.offsetMin = Vector2.zero;
            rightRow.offsetMax = Vector2.zero;
            var rightLayout = rightRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rightLayout.spacing = 8f;
            rightLayout.childControlWidth = false;
            rightLayout.childForceExpandWidth = false;
            rightLayout.childAlignment = TextAnchor.MiddleRight;

            _backButton = AOStyleUiFactory.CreateButton("Back", leftRow, "Back", _font, () => _onBack?.Invoke(), 110f);
            _editAppearanceButton = AOStyleUiFactory.CreateButton("EditAppearance", leftRow, "Edit Appearance", _font, StartEditAppearance, 150f);
            _createButton = AOStyleUiFactory.CreateButton("Create", rightRow, "Create Character", _font, StartCreateFlow, 170f);
            _deleteButton = AOStyleUiFactory.CreateButton("Delete", rightRow, "Delete Character", _font, ShowDeleteConfirm, 170f);
            _playButton = AOStyleUiFactory.CreateButton("Play", rightRow, "Play", _font, OnPlayClicked, 110f);
            StyleActionButton(_backButton, false);
            StyleActionButton(_editAppearanceButton, false);
            StyleActionButton(_createButton, false);
            StyleActionButton(_deleteButton, false);
            StyleActionButton(_playButton, true);

            BuildDeleteConfirmPanel(frame);
        }

        private void ShowSelectionScreen()
        {
            _screenState = ScreenState.Select;
            _titleText.text = "Character Selection";
            _statusText.text = string.Empty;
            _statusText.gameObject.SetActive(true);
            if (_infoCard != null)
                _infoCard.gameObject.SetActive(true);
            if (_sidePanel != null)
                _sidePanel.gameObject.SetActive(true);
            if (_mainButtonRow != null)
                _mainButtonRow.gameObject.SetActive(true);
            if (_stepContent != null)
                _stepContent.gameObject.SetActive(false);
            ClearStepContentChildren();
            _onCreateFlowStateChanged?.Invoke(false);
            _onProfessionStepVisibilityChanged?.Invoke(false);
            ClearPreviewOverlay();
            HideDeleteConfirm();
            RebuildSelectionContent();
            _onSelectionChanged?.Invoke(_selectedProfileIndex);
        }

        private void RebuildSelectionContent()
        {
            ClearContentChildren();
            _previewOverlayText = null;
            if (_sideListContent != null)
            {
                for (int i = _sideListContent.childCount - 1; i >= 0; i--)
                    Destroy(_sideListContent.GetChild(i).gameObject);
            }
            _profileButtons.Clear();

            var carouselHost = AOStyleUiFactory.CreatePanel("CarouselHost", _content, new Color(0f, 0f, 0f, 0f));
            carouselHost.anchorMin = Vector2.zero;
            carouselHost.anchorMax = Vector2.one;
            carouselHost.offsetMin = new Vector2(6f, 6f);
            carouselHost.offsetMax = new Vector2(-6f, -6f);


            var stage = AOStyleUiFactory.CreatePanel("CarouselStage", carouselHost, new Color(0f, 0f, 0f, 0f));
            stage.anchorMin = new Vector2(0.06f, 0.04f);
            stage.anchorMax = new Vector2(0.94f, 0.96f);
            stage.offsetMin = Vector2.zero;
            stage.offsetMax = Vector2.zero;

            var stageBg = AOStyleUiFactory.CreatePanel("StageBg", stage, new Color(0f, 0f, 0f, 0f));
            stageBg.anchorMin = Vector2.zero;
            stageBg.anchorMax = Vector2.one;
            stageBg.offsetMin = Vector2.zero;
            stageBg.offsetMax = Vector2.zero;
            stageBg.SetAsFirstSibling();

            _centerNameplateText = AOStyleUiFactory.CreateText("CenterNameplate", stage, string.Empty, _font, 36, TextAnchor.MiddleCenter);
            var plateRt = (RectTransform)_centerNameplateText.transform;
            plateRt.anchorMin = new Vector2(0.15f, 0.90f);
            plateRt.anchorMax = new Vector2(0.85f, 0.99f);
            plateRt.offsetMin = Vector2.zero;
            plateRt.offsetMax = Vector2.zero;
            _centerNameplateText.color = new Color(0.97f, 0.99f, 1f, 0.98f);

            _previewOverlayText = AOStyleUiFactory.CreateText("PreviewOverlay", stage, string.Empty, _font, 15, TextAnchor.MiddleCenter);
            var overlayRt = (RectTransform)_previewOverlayText.transform;
            overlayRt.anchorMin = new Vector2(0.16f, 0.82f);
            overlayRt.anchorMax = new Vector2(0.84f, 0.89f);
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            _previewOverlayText.color = new Color(0.80f, 0.93f, 1f, 0.95f);
            _previewOverlayText.gameObject.SetActive(false);

            var ghostLeft = AOStyleUiFactory.CreateText("GhostLeft", stage, string.Empty, _font, 14, TextAnchor.MiddleLeft);
            var ghostLeftRt = (RectTransform)ghostLeft.transform;
            ghostLeftRt.anchorMin = new Vector2(0f, 0.5f);
            ghostLeftRt.anchorMax = new Vector2(0.28f, 0.56f);
            ghostLeftRt.offsetMin = Vector2.zero;
            ghostLeftRt.offsetMax = Vector2.zero;
            ghostLeft.color = new Color(0.72f, 0.84f, 0.95f, 0.6f);
            ghostLeft.gameObject.SetActive(false);

            var ghostRight = AOStyleUiFactory.CreateText("GhostRight", stage, string.Empty, _font, 14, TextAnchor.MiddleRight);
            var ghostRightRt = (RectTransform)ghostRight.transform;
            ghostRightRt.anchorMin = new Vector2(0.72f, 0.5f);
            ghostRightRt.anchorMax = new Vector2(1f, 0.56f);
            ghostRightRt.offsetMin = Vector2.zero;
            ghostRightRt.offsetMax = Vector2.zero;
            ghostRight.color = new Color(0.72f, 0.84f, 0.95f, 0.6f);
            ghostRight.gameObject.SetActive(false);

            var navHost = AOStyleUiFactory.CreatePanel("CarouselNav", carouselHost, new Color(0f, 0f, 0f, 0f));
            navHost.anchorMin = new Vector2(0.43f, 0.015f);
            navHost.anchorMax = new Vector2(0.57f, 0.065f);
            navHost.offsetMin = Vector2.zero;
            navHost.offsetMax = Vector2.zero;
            navHost.SetAsLastSibling();
            var navLayout = navHost.gameObject.AddComponent<HorizontalLayoutGroup>();
            navLayout.spacing = 10f;
            navLayout.childControlWidth = false;
            navLayout.childControlHeight = false;
            navLayout.childForceExpandWidth = false;
            navLayout.childForceExpandHeight = false;
            navLayout.childAlignment = TextAnchor.MiddleCenter;

            var prevBtn = AOStyleUiFactory.CreateButton("Prev", navHost, "<", _font, () => SelectProfile(Mathf.Max(0, _selectedProfileIndex - 1)), 30f);
            var prevImg = prevBtn.GetComponent<Image>();
            if (prevImg != null)
                prevImg.color = new Color(0.14f, 0.22f, 0.34f, 0.42f);
            var prevText = prevBtn.GetComponentInChildren<Text>();
            if (prevText != null)
                prevText.raycastTarget = false;

            var dotsHost = AOStyleUiFactory.CreatePanel("CarouselDots", navHost, new Color(0f, 0f, 0f, 0f));
            dotsHost.sizeDelta = new Vector2(160f, 18f);
            var dotsLayout = dotsHost.gameObject.AddComponent<HorizontalLayoutGroup>();
            dotsLayout.spacing = 6f;
            dotsLayout.childControlWidth = false;
            dotsLayout.childControlHeight = false;
            dotsLayout.childForceExpandWidth = false;
            dotsLayout.childForceExpandHeight = false;
            dotsLayout.childAlignment = TextAnchor.MiddleCenter;
            _carouselDots.Clear();
            for (int i = 0; i < Mathf.Max(1, _profiles.Count); i++)
            {
                var dot = AOStyleUiFactory.CreatePanel($"Dot_{i}", dotsHost, new Color(0.55f, 0.67f, 0.78f, 0.45f));
                dot.sizeDelta = new Vector2(8f, 8f);
                var img = dot.GetComponent<Image>();
                _carouselDots.Add(img);
                int dotIndex = i;
                var dotBtn = dot.gameObject.AddComponent<Button>();
                dotBtn.onClick.AddListener(() => SelectProfile(dotIndex));
            }

            var nextBtn = AOStyleUiFactory.CreateButton("Next", navHost, ">", _font, () => SelectProfile(Mathf.Min(_profiles.Count - 1, _selectedProfileIndex + 1)), 30f);
            var nextImg = nextBtn.GetComponent<Image>();
            if (nextImg != null)
                nextImg.color = new Color(0.14f, 0.22f, 0.34f, 0.42f);
            var nextText = nextBtn.GetComponentInChildren<Text>();
            if (nextText != null)
                nextText.raycastTarget = false;
            navHost.gameObject.SetActive(false);
            _carouselDots.Clear();

            for (int i = 0; i < _profiles.Count; i++)
            {
                int localIndex = i;
                var row = AOStyleUiFactory.CreatePanel($"Profile_{i}", _sideListContent, new Color(0.14f, 0.19f, 0.26f, 0.95f));
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(0f, 42f);
                row.anchoredPosition = new Vector2(0f, -i * 46f);
                var rowOutline = row.gameObject.AddComponent<Outline>();
                rowOutline.effectColor = new Color(0.35f, 0.62f, 0.95f, 0.2f);

                var selectButton = row.gameObject.AddComponent<Button>();
                selectButton.onClick.AddListener(() => SelectProfile(localIndex));
                _profileButtons.Add(selectButton);

                var nameText = AOStyleUiFactory.CreateText("Name", row, _profiles[i].Name, _font, 14, TextAnchor.MiddleLeft);
                var nameRt = (RectTransform)nameText.transform;
                nameRt.anchorMin = new Vector2(0f, 0f);
                nameRt.anchorMax = new Vector2(1f, 1f);
                nameRt.offsetMin = new Vector2(10f, 0f);
                nameRt.offsetMax = new Vector2(-10f, 0f);

            }

            if (_sideListContent != null)
            {
                float height = Mathf.Max(0f, (_profiles.Count * 46f) + 6f);
                _sideListContent.sizeDelta = new Vector2(_sideListContent.sizeDelta.x, height);
            }

            if (_selectedProfileIndex < 0 && _profiles.Count > 0)
                _selectedProfileIndex = 0;
            RefreshSelectionVisuals();
            RefreshSelectionDetails();

            string leftName = (_selectedProfileIndex > 0 && _selectedProfileIndex - 1 < _profiles.Count)
                ? _profiles[_selectedProfileIndex - 1].Name
                : string.Empty;
            string rightName = (_selectedProfileIndex + 1 >= 0 && _selectedProfileIndex + 1 < _profiles.Count)
                ? _profiles[_selectedProfileIndex + 1].Name
                : string.Empty;
            ghostLeft.text = string.IsNullOrWhiteSpace(leftName) ? string.Empty : $"◀ {leftName}";
            ghostRight.text = string.IsNullOrWhiteSpace(rightName) ? string.Empty : $"{rightName} ▶";
        }

        private void StartCreateFlow()
        {
            _editingExistingProfile = false;
            _draft.BreedId = 1;
            _draft.Sex = CharacterRuntimeBridge.CharacterSex.Male;
            _draft.Height = BodyHeightPreset.Medium;
            _draft.Weight = BodyWeightPreset.Medium;
            _draft.ProfessionId = 1;
            _draft.ProfessionName = ResolveProfessionName(1);
            _draft.Name = string.Empty;
            _draft.HeadMeshKey = string.Empty;
            RefreshHeadLookupFromCurrentDraft();
            ShowBreedSexStep();
            // Force preview state immediately so the model appears as soon as create flow opens.
            _onPreviewBreedSexChanged?.Invoke(_draft.BreedId, _draft.Sex);
            _onPreviewBodyChanged?.Invoke(_draft.Height, _draft.Weight);
            _onPreviewHeadChanged?.Invoke(_draft.HeadMeshKey);
        }

        private void StartEditAppearance()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
                return;

            CharacterProfile profile = _profiles[_selectedProfileIndex];
            _editingExistingProfile = true;
            _draft.BreedId = profile.BreedId;
            _draft.Sex = profile.Sex;
            _draft.Height = profile.Height;
            _draft.Weight = profile.Weight;
            _draft.ProfessionId = profile.ProfessionId;
            _draft.ProfessionName = profile.ProfessionName;
            _draft.Name = profile.Name;
            string selectedHead = profile.HeadMeshKey ?? string.Empty;
            RefreshHeadLookupFromCurrentDraft();
            int selectedIndex = _headKeys.FindIndex(key =>
                string.Equals(key, selectedHead, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex >= 0)
            {
                _selectedHeadIndex = selectedIndex;
                _draft.HeadMeshKey = _headKeys[selectedIndex];
            }
            ShowBodyStep();
        }

        private void SaveExistingAppearance()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
                return;
            CharacterProfile profile = _profiles[_selectedProfileIndex];
            profile.Height = _draft.Height;
            profile.Weight = _draft.Weight;
            profile.HeadMeshKey = _draft.HeadMeshKey ?? string.Empty;
            _editingExistingProfile = false;
            _onProfilesChanged?.Invoke();
            ShowSelectionScreen();
            ApplySelectionToPreview();
        }

        private void ShowBreedSexStep()
        {
            _screenState = ScreenState.CreateBreedSex;
            _titleText.text = "Create Character - Breed & Gender";
            _statusText.text = "Select one breed/gender option, then click Next.";
            PrepareCreateStepUi();
            ClearPreviewOverlay();
            _onProfessionStepVisibilityChanged?.Invoke(false);
            _choiceButtons.Clear();

            var options = new (int breedId, CharacterRuntimeBridge.CharacterSex sex, string label)[]
            {
                (1, CharacterRuntimeBridge.CharacterSex.Male, "Solitus Male"),
                (1, CharacterRuntimeBridge.CharacterSex.Female, "Solitus Female"),
                (2, CharacterRuntimeBridge.CharacterSex.Male, "Opifex Male"),
                (2, CharacterRuntimeBridge.CharacterSex.Female, "Opifex Female"),
                (3, CharacterRuntimeBridge.CharacterSex.Male, "Nanomage Male"),
                (3, CharacterRuntimeBridge.CharacterSex.Female, "Nanomage Female"),
                (4, CharacterRuntimeBridge.CharacterSex.Uni, "Uni Atrox")
            };

            BuildGridChoices(
                options.Select(v => v.label).ToList(),
                index =>
                {
                    _draft.BreedId = options[index].breedId;
                    _draft.Sex = options[index].sex;
                    RefreshHeadLookupFromCurrentDraft();
                    _onPreviewBreedSexChanged?.Invoke(_draft.BreedId, _draft.Sex);
                    RefreshChoiceVisuals(index);
                    _detailsText.text = $"Selected: {options[index].label}\n\nMesh Preview: {options[index].label}";
                    ClearPreviewOverlay();
                },
                out var nextButton);

            nextButton.onClick.AddListener(ShowBodyStep);
            if (options.Length > 0)
                _detailsText.text = "Selected: Solitus Male\n\nMesh Preview: Solitus Male";
            RefreshChoiceVisuals(0);
            _onPreviewBreedSexChanged?.Invoke(_draft.BreedId, _draft.Sex);
        }

        private void ShowBodyStep()
        {
            _screenState = ScreenState.CreateBody;
            _titleText.text = _editingExistingProfile ? "Edit Appearance" : "Create Character - Height & Weight";
            _statusText.text = _editingExistingProfile
                ? "Choose the locally rendered body and head, then click Save."
                : "Choose body size presets, then click Next.";
            PrepareCreateStepUi();
            ClearPreviewOverlay();
            _onProfessionStepVisibilityChanged?.Invoke(false);
            _choiceButtons.Clear();

            var root = AOStyleUiFactory.CreatePanel("BodySelect", _stepContent, new Color(0f, 0f, 0f, 0f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(8f, 8f);
            root.offsetMax = new Vector2(-8f, -8f);

            var form = AOStyleUiFactory.CreatePanel("BodyForm", root, new Color(0.06f, 0.10f, 0.15f, 0.92f));
            form.anchorMin = new Vector2(0f, 0f);
            form.anchorMax = new Vector2(1f, 1f);
            form.offsetMin = new Vector2(0f, 52f);
            form.offsetMax = new Vector2(0f, 0f);

            var normalMode = AOStyleUiFactory.CreatePanel("BodyNormalMode", form, new Color(0f, 0f, 0f, 0f));
            normalMode.anchorMin = Vector2.zero;
            normalMode.anchorMax = Vector2.one;
            normalMode.offsetMin = Vector2.zero;
            normalMode.offsetMax = Vector2.zero;

            var editMode = AOStyleUiFactory.CreatePanel("BodyHeadEditMode", form, new Color(0f, 0f, 0f, 0f));
            editMode.anchorMin = Vector2.zero;
            editMode.anchorMax = Vector2.one;
            editMode.offsetMin = new Vector2(6f, 6f);
            editMode.offsetMax = new Vector2(-6f, -6f);
            editMode.gameObject.SetActive(false);

            void SetButtonRect(Button button, Vector2 anchor, Vector2 pos, Vector2 size)
            {
                if (button == null)
                    return;
                var rt = (RectTransform)button.transform;
                rt.anchorMin = anchor;
                rt.anchorMax = anchor;
                rt.pivot = anchor;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                var le = button.GetComponent<LayoutElement>();
                if (le == null)
                    le = button.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                le.preferredWidth = size.x;
                le.preferredHeight = size.y;
                le.minWidth = size.x;
                le.minHeight = size.y;
            }

            var heightLabel = AOStyleUiFactory.CreateText("HeightLabel", normalMode, "Height", _font, 16, TextAnchor.MiddleLeft);
            var heightLabelRt = (RectTransform)heightLabel.transform;
            heightLabelRt.anchorMin = new Vector2(0f, 1f);
            heightLabelRt.anchorMax = new Vector2(1f, 1f);
            heightLabelRt.offsetMin = new Vector2(12f, -34f);
            heightLabelRt.offsetMax = new Vector2(-12f, -10f);

            var shortBtn = AOStyleUiFactory.CreateButton("Short", normalMode, "Short", _font, () => _draft.Height = BodyHeightPreset.Short, 62f);
            var mediumBtn = AOStyleUiFactory.CreateButton("Medium", normalMode, "Medium", _font, () => _draft.Height = BodyHeightPreset.Medium, 62f);
            var tallBtn = AOStyleUiFactory.CreateButton("Tall", normalMode, "Tall", _font, () => _draft.Height = BodyHeightPreset.Tall, 62f);
            SetButtonRect(shortBtn, new Vector2(0f, 1f), new Vector2(12f, -72f), new Vector2(62f, 24f));
            SetButtonRect(mediumBtn, new Vector2(0f, 1f), new Vector2(80f, -72f), new Vector2(62f, 24f));
            SetButtonRect(tallBtn, new Vector2(0f, 1f), new Vector2(148f, -72f), new Vector2(62f, 24f));

            var weightLabel = AOStyleUiFactory.CreateText("WeightLabel", normalMode, "Weight", _font, 16, TextAnchor.MiddleLeft);
            var weightLabelRt = (RectTransform)weightLabel.transform;
            weightLabelRt.anchorMin = new Vector2(0f, 1f);
            weightLabelRt.anchorMax = new Vector2(1f, 1f);
            weightLabelRt.offsetMin = new Vector2(12f, -124f);
            weightLabelRt.offsetMax = new Vector2(-12f, -100f);

            var skinnyBtn = AOStyleUiFactory.CreateButton("Skinny", normalMode, "Skinny", _font, () => _draft.Weight = BodyWeightPreset.Skinny, 62f);
            var weightMediumBtn = AOStyleUiFactory.CreateButton("WeightMedium", normalMode, "Medium", _font, () => _draft.Weight = BodyWeightPreset.Medium, 62f);
            var fatBtn = AOStyleUiFactory.CreateButton("Fat", normalMode, "Fat", _font, () => _draft.Weight = BodyWeightPreset.Fat, 62f);
            SetButtonRect(skinnyBtn, new Vector2(0f, 1f), new Vector2(12f, -162f), new Vector2(62f, 24f));
            SetButtonRect(weightMediumBtn, new Vector2(0f, 1f), new Vector2(80f, -162f), new Vector2(62f, 24f));
            SetButtonRect(fatBtn, new Vector2(0f, 1f), new Vector2(148f, -162f), new Vector2(62f, 24f));

            var headLabel = AOStyleUiFactory.CreateText("HeadLabel", normalMode, "Head", _font, 16, TextAnchor.MiddleLeft);
            var headLabelRt = (RectTransform)headLabel.transform;
            headLabelRt.anchorMin = new Vector2(0f, 1f);
            headLabelRt.anchorMax = new Vector2(1f, 1f);
            headLabelRt.offsetMin = new Vector2(12f, -214f);
            headLabelRt.offsetMax = new Vector2(-12f, -190f);

            var prevHeadBtn = AOStyleUiFactory.CreateButton("HeadPrev", normalMode, "<", _font, CycleHeadPrevious, 28f);
            SetButtonRect(prevHeadBtn, new Vector2(0f, 1f), new Vector2(12f, -252f), new Vector2(28f, 24f));
            var headValue = AOStyleUiFactory.CreatePanel("HeadValue", normalMode, new Color(0.13f, 0.18f, 0.25f, 0.95f));
            headValue.anchorMin = new Vector2(0f, 1f);
            headValue.anchorMax = new Vector2(0f, 1f);
            headValue.pivot = new Vector2(0f, 1f);
            headValue.anchoredPosition = new Vector2(46f, -252f);
            headValue.sizeDelta = new Vector2(132f, 24f);
            _headValueText = AOStyleUiFactory.CreateText("HeadValueText", headValue, "None", _font, 12, TextAnchor.MiddleLeft);
            var headValueRt = (RectTransform)_headValueText.transform;
            headValueRt.anchorMin = Vector2.zero;
            headValueRt.anchorMax = Vector2.one;
            headValueRt.offsetMin = new Vector2(8f, 0f);
            headValueRt.offsetMax = new Vector2(-8f, 0f);
            var nextHeadBtn = AOStyleUiFactory.CreateButton("HeadNext", normalMode, ">", _font, CycleHeadNext, 28f);
            SetButtonRect(nextHeadBtn, new Vector2(0f, 1f), new Vector2(184f, -252f), new Vector2(28f, 24f));

            var editHeadBtn = AOStyleUiFactory.CreateButton("EditHead", normalMode, "Edit Head", _font, () => { }, 78f);
            SetButtonRect(editHeadBtn, new Vector2(0f, 1f), new Vector2(134f, -286f), new Vector2(78f, 22f));
            var editBtnLabel = editHeadBtn.GetComponentInChildren<Text>();

            BuildHeadEditPanel(editMode, () =>
            {
                editMode.gameObject.SetActive(false);
                normalMode.gameObject.SetActive(true);
                if (editBtnLabel != null)
                    editBtnLabel.text = "Edit Head";
            });

            editHeadBtn.onClick.RemoveAllListeners();
            editHeadBtn.onClick.AddListener(() =>
            {
                normalMode.gameObject.SetActive(false);
                editMode.gameObject.SetActive(true);
                if (editBtnLabel != null)
                    editBtnLabel.text = "Close Edit";
            });

            var footer = AOStyleUiFactory.CreatePanel("Footer", root, new Color(0f, 0f, 0f, 0f));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = new Vector2(0f, 0f);
            footer.offsetMax = new Vector2(0f, 38f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 6f;
            footerLayout.childControlWidth = false;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            AOStyleUiFactory.CreateButton("Back", footer, "Back", _font,
                _editingExistingProfile ? ShowSelectionScreen : ShowBreedSexStep, 74f);
            AOStyleUiFactory.CreateButton(_editingExistingProfile ? "Save" : "Next", footer,
                _editingExistingProfile ? "Save" : "Next", _font,
                _editingExistingProfile ? SaveExistingAppearance : ShowProfessionStep, 74f);

            _detailsText.text = $"Current Body\nHeight: {_draft.Height}\nWeight: {_draft.Weight}";
            shortBtn.onClick.AddListener(NotifyBodyPreviewChanged);
            mediumBtn.onClick.AddListener(NotifyBodyPreviewChanged);
            tallBtn.onClick.AddListener(NotifyBodyPreviewChanged);
            skinnyBtn.onClick.AddListener(NotifyBodyPreviewChanged);
            weightMediumBtn.onClick.AddListener(NotifyBodyPreviewChanged);
            fatBtn.onClick.AddListener(NotifyBodyPreviewChanged);
            prevHeadBtn.onClick.AddListener(() => _detailsText.text = $"Current Body\nHeight: {_draft.Height}\nWeight: {_draft.Weight}");
            nextHeadBtn.onClick.AddListener(() => _detailsText.text = $"Current Body\nHeight: {_draft.Height}\nWeight: {_draft.Weight}");
            _onPreviewBreedSexChanged?.Invoke(_draft.BreedId, _draft.Sex);
            _onPreviewHeadChanged?.Invoke(_draft.HeadMeshKey);
            UpdateHeadValueText();
            NotifyBodyPreviewChanged();
        }

        private void BuildHeadEditPanel(RectTransform panel, Action onClose)
        {
            if (panel == null)
                return;

            _headEditDefaultInputs.Clear();
            for (int i = panel.childCount - 1; i >= 0; i--)
                Destroy(panel.GetChild(i).gameObject);

            var scrollHost = AOStyleUiFactory.CreatePanel("HeadEditScrollHost", panel, new Color(0f, 0f, 0f, 0f));
            scrollHost.anchorMin = new Vector2(0f, 0f);
            scrollHost.anchorMax = new Vector2(1f, 1f);
            scrollHost.offsetMin = new Vector2(0f, 40f);
            scrollHost.offsetMax = Vector2.zero;

            var scrollRect = scrollHost.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;

            var viewport = AOStyleUiFactory.CreatePanel("Viewport", scrollHost, new Color(0f, 0f, 0f, 0.06f));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport, false);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            var panelLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(8, 8, 8, 8);
            panelLayout.spacing = 4f;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandHeight = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = contentRt;

            var title = AOStyleUiFactory.CreateText(
                "HeadEditTitle",
                contentRt,
                "Head Edit: +/- nudges now. Set defaults (points), then Add Defaults.",
                _font,
                12,
                TextAnchor.UpperLeft);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            CreateHeadEditAdjustRow(contentRt, "Pos X", "pos_x", new Vector3(-HeadEditPositionStep, 0f, 0f), new Vector3(HeadEditPositionStep, 0f, 0f));
            CreateHeadEditAdjustRow(contentRt, "Pos Y", "pos_y", new Vector3(0f, -HeadEditPositionStep, 0f), new Vector3(0f, HeadEditPositionStep, 0f));
            CreateHeadEditAdjustRow(contentRt, "Pos Z", "pos_z", new Vector3(0f, 0f, -HeadEditPositionStep), new Vector3(0f, 0f, HeadEditPositionStep));
            CreateHeadEditRotateRow(contentRt, "Rot X", "rot_x", new Vector3(-HeadEditRotationStep, 0f, 0f), new Vector3(HeadEditRotationStep, 0f, 0f));
            CreateHeadEditRotateRow(contentRt, "Rot Y", "rot_y", new Vector3(0f, -HeadEditRotationStep, 0f), new Vector3(0f, HeadEditRotationStep, 0f));
            CreateHeadEditRotateRow(contentRt, "Rot Z", "rot_z", new Vector3(0f, 0f, -HeadEditRotationStep), new Vector3(0f, 0f, HeadEditRotationStep));

            var saveRow = AOStyleUiFactory.CreatePanel("HeadEditSaveRow", panel, new Color(0f, 0f, 0f, 0f));
            saveRow.anchorMin = new Vector2(0f, 0f);
            saveRow.anchorMax = new Vector2(1f, 0f);
            saveRow.offsetMin = new Vector2(0f, 0f);
            saveRow.offsetMax = new Vector2(0f, 34f);
            var saveLayout = saveRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            saveLayout.spacing = 6f;
            saveLayout.childControlWidth = false;
            saveLayout.childForceExpandWidth = false;
            saveLayout.childAlignment = TextAnchor.MiddleRight;

            AOStyleUiFactory.CreateButton("ApplyHeadDefaults", saveRow, "Add Defaults", _font, ApplyHeadEditDefaults, 94f);
            var saveBtn = AOStyleUiFactory.CreateButton("SaveHeadEdit", saveRow, "Save Head", _font, () =>
            {
                if (string.IsNullOrWhiteSpace(_draft.HeadMeshKey))
                {
                    _statusText.text = "Select a head mesh before saving edits.";
                    return;
                }

                bool ok = _onHeadEditSave != null && _onHeadEditSave.Invoke();
                _statusText.text = ok
                    ? $"Saved head override for {_draft.HeadMeshKey}."
                    : "Failed to save head override.";
            }, 86f);

            var closeBtn = AOStyleUiFactory.CreateButton("CloseHeadEdit", saveRow, "Close Edit", _font, () => onClose?.Invoke(), 86f);
            var saveLe = saveBtn.GetComponent<LayoutElement>();
            if (saveLe != null)
                saveLe.preferredHeight = 22f;
            var closeLe = closeBtn.GetComponent<LayoutElement>();
            if (closeLe != null)
                closeLe.preferredHeight = 22f;
        }

        private void CreateHeadEditAdjustRow(RectTransform parent, string label, string defaultKey, Vector3 minusDelta, Vector3 plusDelta)
        {
            var row = AOStyleUiFactory.CreatePanel($"HeadEditRow_{label}", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            AOStyleUiFactory.CreateText($"Lbl_{label}", row, label, _font, 12, TextAnchor.MiddleLeft).gameObject
                .AddComponent<LayoutElement>().preferredWidth = 44f;
            var minus = AOStyleUiFactory.CreateButton($"Minus_{label}", row, "-", _font, () => _onHeadEditOffsetDelta?.Invoke(minusDelta), 18f);
            var plus = AOStyleUiFactory.CreateButton($"Plus_{label}", row, "+", _font, () => _onHeadEditOffsetDelta?.Invoke(plusDelta), 18f);
            AOStyleUiFactory.CreateText($"DefaultLbl_{label}", row, "Def", _font, 10, TextAnchor.MiddleLeft).gameObject
                .AddComponent<LayoutElement>().preferredWidth = 20f;
            var defInput = AOStyleUiFactory.CreateInputField($"Default_{label}", row, "0", _font, 34f);
            defInput.text = "0";
            var defLe = defInput.GetComponent<LayoutElement>();
            if (defLe != null)
                defLe.preferredHeight = 16f;
            _headEditDefaultInputs[defaultKey] = defInput;
            var minusLe = minus.GetComponent<LayoutElement>();
            if (minusLe != null)
                minusLe.preferredHeight = 16f;
            var plusLe = plus.GetComponent<LayoutElement>();
            if (plusLe != null)
                plusLe.preferredHeight = 16f;
        }

        private void CreateHeadEditRotateRow(RectTransform parent, string label, string defaultKey, Vector3 minusDelta, Vector3 plusDelta)
        {
            var row = AOStyleUiFactory.CreatePanel($"HeadEditRotRow_{label}", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            AOStyleUiFactory.CreateText($"LblRot_{label}", row, label, _font, 12, TextAnchor.MiddleLeft).gameObject
                .AddComponent<LayoutElement>().preferredWidth = 44f;
            var minus = AOStyleUiFactory.CreateButton($"MinusRot_{label}", row, "-", _font, () => _onHeadEditEulerDelta?.Invoke(minusDelta), 18f);
            var plus = AOStyleUiFactory.CreateButton($"PlusRot_{label}", row, "+", _font, () => _onHeadEditEulerDelta?.Invoke(plusDelta), 18f);
            AOStyleUiFactory.CreateText($"DefaultLblRot_{label}", row, "Def", _font, 10, TextAnchor.MiddleLeft).gameObject
                .AddComponent<LayoutElement>().preferredWidth = 20f;
            var defInput = AOStyleUiFactory.CreateInputField($"DefaultRot_{label}", row, "0", _font, 34f);
            defInput.text = "0";
            var defLe = defInput.GetComponent<LayoutElement>();
            if (defLe != null)
                defLe.preferredHeight = 16f;
            _headEditDefaultInputs[defaultKey] = defInput;
            var minusLe = minus.GetComponent<LayoutElement>();
            if (minusLe != null)
                minusLe.preferredHeight = 16f;
            var plusLe = plus.GetComponent<LayoutElement>();
            if (plusLe != null)
                plusLe.preferredHeight = 16f;
        }

        private void ApplyHeadEditDefaults()
        {
            float posX = GetHeadDefaultPoints("pos_x");
            float posY = GetHeadDefaultPoints("pos_y");
            float posZ = GetHeadDefaultPoints("pos_z");
            float rotX = GetHeadDefaultPoints("rot_x");
            float rotY = GetHeadDefaultPoints("rot_y");
            float rotZ = GetHeadDefaultPoints("rot_z");

            Vector3 posDelta = new Vector3(posX, posY, posZ) * HeadEditPositionStep;
            Vector3 rotDelta = new Vector3(rotX, rotY, rotZ) * HeadEditRotationStep;

            if (posDelta.sqrMagnitude > 0.0000001f)
                _onHeadEditOffsetDelta?.Invoke(posDelta);
            if (rotDelta.sqrMagnitude > 0.0000001f)
                _onHeadEditEulerDelta?.Invoke(rotDelta);

            _statusText.text = "Applied head defaults.";
        }

        private float GetHeadDefaultPoints(string key)
        {
            if (!_headEditDefaultInputs.TryGetValue(key, out var input) || input == null)
                return 0f;

            string raw = string.IsNullOrWhiteSpace(input.text) ? "0" : input.text.Trim();
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return value;

            return 0f;
        }

        private void ShowProfessionStep()
        {
            _screenState = ScreenState.CreateProfession;
            _titleText.text = "Create Character - Profession";
            _statusText.text = "Select a profession to continue.";
            PrepareCreateStepUi();
            ClearPreviewOverlay();
            _onProfessionStepVisibilityChanged?.Invoke(true);
            _professionButtons.Clear();

            _professionDescriptionText = AOStyleUiFactory.CreateText("ProfessionDescription", _stepContent, string.Empty, _font, 12, TextAnchor.UpperLeft);
            var descRt = (RectTransform)_professionDescriptionText.transform;
            descRt.anchorMin = new Vector2(0f, 1f);
            descRt.anchorMax = new Vector2(1f, 1f);
            descRt.pivot = new Vector2(0.5f, 1f);
            descRt.anchoredPosition = new Vector2(0f, -6f);
            descRt.sizeDelta = new Vector2(0f, 92f);
            _professionDescriptionText.color = new Color(0.88f, 0.95f, 1f, 0.96f);
            _professionDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _professionDescriptionText.verticalOverflow = VerticalWrapMode.Truncate;

            var listHost = AOStyleUiFactory.CreatePanel("ProfessionList", _stepContent, new Color(0f, 0f, 0f, 0f));
            listHost.anchorMin = Vector2.zero;
            listHost.anchorMax = Vector2.one;
            listHost.offsetMin = new Vector2(6f, 44f);
            listHost.offsetMax = new Vector2(-6f, -102f);

            var scrollRect = listHost.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 36f;

            var viewport = AOStyleUiFactory.CreatePanel("Viewport", listHost, new Color(0f, 0f, 0f, 0.06f));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            var listLayout = content.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 2f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            listLayout.padding = new RectOffset(0, 0, 0, 0);
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = contentRt;

            for (int i = 0; i < _professionIds.Count; i++)
            {
                int localIndex = i;
                int professionId = _professionIds[i];
                string name = ResolveProfessionName(professionId);
                var row = AOStyleUiFactory.CreatePanel($"Profession_{professionId}", contentRt, new Color(0.13f, 0.18f, 0.25f, 0.95f));
                var rowLayout = row.gameObject.AddComponent<LayoutElement>();
                rowLayout.preferredHeight = 18f;
                rowLayout.minHeight = 18f;
                rowLayout.flexibleWidth = 1f;
                rowLayout.flexibleHeight = 0f;
                var btn = row.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    _draft.ProfessionId = professionId;
                    _draft.ProfessionName = name;
                    RefreshProfessionVisuals(localIndex);
                    string desc = ProfessionDescriptions.TryGetValue(professionId, out var value)
                        ? value
                        : "Profession description will be added.";
                    _detailsText.text = $"Selected Profession: {name}\n\n{desc}";
                    if (_professionDescriptionText != null)
                        _professionDescriptionText.text = $"{name}\n{desc}";
                    _onProfessionSelected?.Invoke(professionId, name, desc);
                });
                _professionButtons.Add(btn);

                var text = AOStyleUiFactory.CreateText("Label", row, $"{professionId}: {name}", _font, 11, TextAnchor.MiddleLeft);
                var textRt = (RectTransform)text.transform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(8f, 0f);
                textRt.offsetMax = new Vector2(-10f, 0f);
            }

            var footer = AOStyleUiFactory.CreatePanel("Footer", _stepContent, new Color(0f, 0f, 0f, 0f));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = new Vector2(6f, 6f);
            footer.offsetMax = new Vector2(-6f, 38f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 8f;
            footerLayout.childControlWidth = false;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            AOStyleUiFactory.CreateButton("Back", footer, "Back", _font, ShowBodyStep, 110f);
            AOStyleUiFactory.CreateButton("Next", footer, "Next", _font, ShowNameStep, 110f);

            int selectedIdx = Mathf.Max(0, _professionIds.IndexOf(_draft.ProfessionId));
            RefreshProfessionVisuals(selectedIdx);
            int selectedProfessionId = _professionIds.Count > selectedIdx ? _professionIds[selectedIdx] : 1;
            string selectedName = ResolveProfessionName(selectedProfessionId);
            string selectedDesc = ProfessionDescriptions.TryGetValue(selectedProfessionId, out var textValue)
                ? textValue
                : "Profession description will be added.";
            _detailsText.text = $"Selected Profession: {selectedName}\n\n{selectedDesc}";
            if (_professionDescriptionText != null)
                _professionDescriptionText.text = $"{selectedName}\n{selectedDesc}";
            _onProfessionSelected?.Invoke(selectedProfessionId, selectedName, selectedDesc);
        }

        private void ShowNameStep()
        {
            _screenState = ScreenState.CreateName;
            _titleText.text = "Create Character - Name";
            _statusText.text = "Enter a name (3-13 chars), then click Create.";
            PrepareCreateStepUi();
            ClearPreviewOverlay();
            _onProfessionStepVisibilityChanged?.Invoke(false);

            var root = AOStyleUiFactory.CreatePanel("NameRoot", _stepContent, new Color(0f, 0f, 0f, 0f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(8f, 8f);
            root.offsetMax = new Vector2(-8f, -8f);

            var prompt = AOStyleUiFactory.CreateText("Prompt", root, "Character Name", _font, 16, TextAnchor.UpperLeft);
            var promptRt = (RectTransform)prompt.transform;
            promptRt.anchorMin = new Vector2(0f, 1f);
            promptRt.anchorMax = new Vector2(1f, 1f);
            promptRt.offsetMin = new Vector2(0f, -36f);
            promptRt.offsetMax = new Vector2(0f, 0f);

            _nameInput = AOStyleUiFactory.CreateInputField("NameInput", root, "3-13 characters", _font, 300f);
            var inputRt = (RectTransform)_nameInput.transform;
            inputRt.anchorMin = new Vector2(0f, 1f);
            inputRt.anchorMax = new Vector2(0f, 1f);
            inputRt.pivot = new Vector2(0f, 1f);
            inputRt.anchoredPosition = new Vector2(0f, -58f);
            inputRt.sizeDelta = new Vector2(380f, 30f);
            var inputBg = _nameInput.GetComponent<Image>();
            if (inputBg != null)
                inputBg.color = new Color(0.16f, 0.2f, 0.28f, 1f);
            var inputOutline = _nameInput.GetComponent<Outline>();
            if (inputOutline == null)
                inputOutline = _nameInput.gameObject.AddComponent<Outline>();
            inputOutline.effectColor = new Color(0.62f, 0.78f, 0.95f, 0.55f);
            inputOutline.effectDistance = new Vector2(1f, -1f);

            string summary = $"Breed: {ResolveBreedSexLabel(_draft.BreedId, _draft.Sex)}\n" +
                             $"Height/Weight: {_draft.Height}/{_draft.Weight}\n" +
                             $"Profession: {_draft.ProfessionName}";
            var summaryText = AOStyleUiFactory.CreateText("Summary", root, summary, _font, 14, TextAnchor.UpperLeft);
            var summaryRt = (RectTransform)summaryText.transform;
            summaryRt.anchorMin = new Vector2(0f, 1f);
            summaryRt.anchorMax = new Vector2(1f, 1f);
            summaryRt.offsetMin = new Vector2(0f, -180f);
            summaryRt.offsetMax = new Vector2(0f, -98f);

            var footer = AOStyleUiFactory.CreatePanel("Footer", root, new Color(0f, 0f, 0f, 0f));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = new Vector2(0f, 0f);
            footer.offsetMax = new Vector2(0f, 44f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 8f;
            footerLayout.childControlWidth = false;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            AOStyleUiFactory.CreateButton("Back", footer, "Back", _font, ShowProfessionStep, 110f);
            AOStyleUiFactory.CreateButton("Create", footer, "Create", _font, CreateCharacter, 120f);
        }

        private void BuildGridChoices(
            List<string> labels,
            Action<int> onSelect,
            out Button nextButton)
        {
            var root = AOStyleUiFactory.CreatePanel("ChoiceGridRoot", _stepContent, new Color(0f, 0f, 0f, 0f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(8f, 8f);
            root.offsetMax = new Vector2(-8f, -8f);

            var grid = AOStyleUiFactory.CreatePanel("Grid", root, new Color(0f, 0f, 0f, 0f));
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.offsetMin = new Vector2(0f, 48f);
            grid.offsetMax = Vector2.zero;
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(250f, 52f);
            layout.spacing = new Vector2(6f, 6f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 1;
            layout.childAlignment = TextAnchor.UpperLeft;

            for (int i = 0; i < labels.Count; i++)
            {
                int local = i;
                var card = AOStyleUiFactory.CreatePanel($"Choice_{i}", grid, new Color(0.12f, 0.17f, 0.24f, 0.96f));
                card.gameObject.AddComponent<Outline>().effectColor = new Color(0.4f, 0.65f, 0.95f, 0.15f);
                var button = card.gameObject.AddComponent<Button>();
                button.onClick.AddListener(() => onSelect(local));
                _choiceButtons.Add(button);

                var text = AOStyleUiFactory.CreateText("Label", card, labels[i], _font, 15, TextAnchor.MiddleCenter);
                var textRt = (RectTransform)text.transform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(6f, 6f);
                textRt.offsetMax = new Vector2(-6f, -6f);
            }

            var footer = AOStyleUiFactory.CreatePanel("Footer", root, new Color(0f, 0f, 0f, 0f));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = Vector2.zero;
            footer.offsetMax = new Vector2(0f, 42f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 8f;
            footerLayout.childControlWidth = false;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childAlignment = TextAnchor.MiddleRight;

            AOStyleUiFactory.CreateButton("Back", footer, "Back", _font, ShowSelectionScreen, 110f);
            nextButton = AOStyleUiFactory.CreateButton("Next", footer, "Next", _font, () => { }, 110f);
        }

        private void SelectProfile(int index)
        {
            if (_profiles.Count == 0)
            {
                _selectedProfileIndex = -1;
                RefreshSelectionVisuals();
                RefreshSelectionDetails();
                return;
            }

            _selectedProfileIndex = Mathf.Clamp(index, 0, _profiles.Count - 1);
            _onSelectionChanged?.Invoke(_selectedProfileIndex);
            RefreshSelectionVisuals();
            RefreshSelectionDetails();
        }

        private void ApplySelectionToPreview()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
                return;

            var profile = _profiles[_selectedProfileIndex];
            _onPreviewBreedSexChanged?.Invoke(profile.BreedId, profile.Sex);
            _onPreviewBodyChanged?.Invoke(profile.Height, profile.Weight);
            _onPreviewHeadChanged?.Invoke(profile.HeadMeshKey ?? string.Empty);
        }

        private void RefreshSelectionVisuals()
        {
            for (int i = 0; i < _profileButtons.Count; i++)
            {
                var image = _profileButtons[i].GetComponent<Image>();
                if (image == null)
                    continue;
                image.color = i == _selectedProfileIndex
                    ? new Color(0.18f, 0.36f, 0.54f, 0.98f)
                    : new Color(0.11f, 0.18f, 0.25f, 0.95f);
            }

            if (_playButton != null)
                _playButton.interactable = _selectedProfileIndex >= 0 && _selectedProfileIndex < _profiles.Count;
            if (_editAppearanceButton != null)
            {
                _editAppearanceButton.interactable = _selectedProfileIndex >= 0
                    && _selectedProfileIndex < _profiles.Count;
                StyleActionButton(_editAppearanceButton, false);
            }
            if (_backButton != null)
                StyleActionButton(_backButton, false);
            if (_createButton != null)
                StyleActionButton(_createButton, false);
            if (_deleteButton != null)
            {
                _deleteButton.interactable = _selectedProfileIndex >= 0 && _selectedProfileIndex < _profiles.Count;
                StyleActionButton(_deleteButton, false);
            }
            if (_playButton != null)
                StyleActionButton(_playButton, true);

            for (int i = 0; i < _carouselDots.Count; i++)
            {
                var dot = _carouselDots[i];
                if (dot == null)
                    continue;
                bool active = i == _selectedProfileIndex;
                dot.color = active
                    ? new Color(0.23f, 0.87f, 0.95f, 0.95f)
                    : new Color(0.55f, 0.67f, 0.78f, 0.45f);
                var rt = dot.rectTransform;
                if (rt != null)
                    rt.sizeDelta = active ? new Vector2(14f, 8f) : new Vector2(8f, 8f);
            }
        }

        private void RefreshSelectionDetails()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
            {
                _detailsText.text = "No character selected.\nCreate a character to enable Play.";
                if (_centerNameplateText != null)
                    _centerNameplateText.text = string.Empty;
                return;
            }

            var profile = _profiles[_selectedProfileIndex];
            _detailsText.text =
                $"{(profile.Name ?? string.Empty).ToUpperInvariant()}\n" +
                $"LVL {profile.Level} {profile.ProfessionName?.ToUpperInvariant()}\n\n" +
                $"Breed: {profile.BreedLabel}\n" +
                $"Body: {profile.Height}/{profile.Weight}\n" +
                $"Head: {(string.IsNullOrWhiteSpace(profile.HeadMeshKey) ? "Default" : profile.HeadMeshKey)}";
            if (_centerNameplateText != null)
                _centerNameplateText.text = string.Empty;
        }

        private void RefreshChoiceVisuals(int selectedIndex)
        {
            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                var image = _choiceButtons[i].GetComponent<Image>();
                if (image == null)
                    continue;
                image.color = i == selectedIndex
                    ? new Color(0.24f, 0.35f, 0.48f, 0.98f)
                    : new Color(0.12f, 0.17f, 0.24f, 0.96f);
            }
        }

        private void RefreshProfessionVisuals(int selectedIndex)
        {
            for (int i = 0; i < _professionButtons.Count; i++)
            {
                var image = _professionButtons[i].GetComponent<Image>();
                if (image == null)
                    continue;
                image.color = i == selectedIndex
                    ? new Color(0.22f, 0.33f, 0.46f, 0.98f)
                    : new Color(0.13f, 0.18f, 0.25f, 0.95f);
            }
        }

        private void OnPlayClicked()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
                return;

            _onPlay?.Invoke(_profiles[_selectedProfileIndex]);
        }

        private void CreateCharacter()
        {
            string rawName = _nameInput != null ? _nameInput.text : string.Empty;
            string cleaned = (rawName ?? string.Empty).Trim();
            if (cleaned.Length < 3 || cleaned.Length > 13)
            {
                _statusText.text = "Character name must be between 3 and 13 characters.";
                return;
            }
            if (_profiles.Any(p => p != null && string.Equals((p.Name ?? string.Empty).Trim(), cleaned, StringComparison.OrdinalIgnoreCase)))
            {
                _statusText.text = $"Character name '{cleaned}' already exists. Choose a different name.";
                return;
            }

            var profile = new CharacterProfile
            {
                Name = cleaned,
                Level = 1,
                BreedId = _draft.BreedId,
                Sex = _draft.Sex,
                ProfessionId = _draft.ProfessionId,
                ProfessionName = string.IsNullOrWhiteSpace(_draft.ProfessionName) ? ResolveProfessionName(_draft.ProfessionId) : _draft.ProfessionName,
                BreedLabel = ResolveBreedSexLabel(_draft.BreedId, _draft.Sex),
                Height = _draft.Height,
                Weight = _draft.Weight,
                HeadMeshKey = _draft.HeadMeshKey,
                StartPlayfieldId = 4604
            };

            _profiles.Add(profile);
            _selectedProfileIndex = _profiles.Count - 1;
            _onProfilesChanged?.Invoke();
            _statusText.text = $"Created {profile.Name}. Entering world...";
            _onPlay?.Invoke(profile);
        }

        private void BuildDeleteConfirmPanel(RectTransform parent)
        {
            _deleteConfirmPanel = AOStyleUiFactory.CreatePanel("DeleteConfirmPanel", parent, new Color(0.05f, 0.09f, 0.14f, 0.96f));
            _deleteConfirmPanel.anchorMin = new Vector2(0.33f, 0.35f);
            _deleteConfirmPanel.anchorMax = new Vector2(0.67f, 0.63f);
            _deleteConfirmPanel.offsetMin = Vector2.zero;
            _deleteConfirmPanel.offsetMax = Vector2.zero;
            _deleteConfirmPanel.gameObject.SetActive(false);

            var title = AOStyleUiFactory.CreateText("DeleteTitle", _deleteConfirmPanel, "Delete Character", _font, 22, TextAnchor.MiddleCenter);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0.06f, 0.78f);
            titleRt.anchorMax = new Vector2(0.94f, 0.96f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            var prompt = AOStyleUiFactory.CreateText("DeletePrompt", _deleteConfirmPanel, "Type the exact character name to confirm deletion.", _font, 14, TextAnchor.MiddleCenter);
            var promptRt = (RectTransform)prompt.transform;
            promptRt.anchorMin = new Vector2(0.08f, 0.56f);
            promptRt.anchorMax = new Vector2(0.92f, 0.74f);
            promptRt.offsetMin = Vector2.zero;
            promptRt.offsetMax = Vector2.zero;

            _deleteNameInput = AOStyleUiFactory.CreateInputField("DeleteNameInput", _deleteConfirmPanel, "Character Name", _font, 0f);
            var inputRt = _deleteNameInput.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0.10f, 0.39f);
            inputRt.anchorMax = new Vector2(0.90f, 0.54f);
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;

            var row = AOStyleUiFactory.CreatePanel("DeleteButtonRow", _deleteConfirmPanel, new Color(0f, 0f, 0f, 0f));
            row.anchorMin = new Vector2(0.08f, 0.10f);
            row.anchorMax = new Vector2(0.92f, 0.30f);
            row.offsetMin = Vector2.zero;
            row.offsetMax = Vector2.zero;
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childControlWidth = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childAlignment = TextAnchor.MiddleRight;

            var cancel = AOStyleUiFactory.CreateButton("CancelDelete", row, "Cancel", _font, HideDeleteConfirm, 120f);
            var confirm = AOStyleUiFactory.CreateButton("ConfirmDelete", row, "Delete", _font, ConfirmDeleteCharacter, 120f);
            StyleActionButton(cancel, false);
            StyleActionButton(confirm, true);
        }

        private void ShowDeleteConfirm()
        {
            if (_deleteConfirmPanel == null)
                return;

            _deleteNameInput.text = string.Empty;
            _deleteConfirmPanel.gameObject.SetActive(true);
        }

        private void HideDeleteConfirm()
        {
            if (_deleteConfirmPanel == null)
                return;
            _deleteConfirmPanel.gameObject.SetActive(false);
        }

        private void ConfirmDeleteCharacter()
        {
            string typedName = (_deleteNameInput != null ? _deleteNameInput.text : string.Empty)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typedName))
            {
                _statusText.text = "Type a character name to confirm deletion.";
                return;
            }

            int index = _profiles.FindIndex(p => p != null && string.Equals((p.Name ?? string.Empty).Trim(), typedName, StringComparison.Ordinal));
            if (index < 0)
            {
                _statusText.text = $"Delete failed: no exact character match for '{typedName}'.";
                return;
            }

            string deleted = _profiles[index].Name;
            _profiles.RemoveAt(index);
            if (_profiles.Count == 0)
            {
                _selectedProfileIndex = -1;
            }
            else if (_selectedProfileIndex >= _profiles.Count)
            {
                _selectedProfileIndex = _profiles.Count - 1;
            }
            else if (_selectedProfileIndex > index)
            {
                _selectedProfileIndex--;
            }

            _onProfilesChanged?.Invoke();
            HideDeleteConfirm();
            RebuildSelectionContent();
            _statusText.text = $"Deleted character '{deleted}'.";
            _onSelectionChanged?.Invoke(_selectedProfileIndex);
        }

        private static string ResolveBreedSexLabel(int breedId, CharacterRuntimeBridge.CharacterSex sex)
        {
            string breed = breedId switch
            {
                1 => "Solitus",
                2 => "Opifex",
                3 => "Nanomage",
                4 => "Atrox",
                _ => "Unknown"
            };

            string sexLabel = sex switch
            {
                CharacterRuntimeBridge.CharacterSex.Female => "Female",
                CharacterRuntimeBridge.CharacterSex.Uni => "Uni",
                _ => "Male"
            };

            return $"{breed} {sexLabel}";
        }

        private string ResolveProfessionName(int professionId)
        {
            if (_professionNames.TryGetValue(professionId, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            if (DefaultProfessionNames.TryGetValue(professionId, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
            return $"Profession {professionId}";
        }

        private void RefreshHeadLookupFromCurrentDraft()
        {
            _headKeys.Clear();
            _headKeys.Add(string.Empty);

            if (_headLookupProvider != null)
            {
                var lookup = _headLookupProvider.Invoke(_draft.BreedId, _draft.Sex);
                if (lookup != null)
                {
                    foreach (var pair in lookup)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key))
                            continue;
                        _headKeys.Add(pair.Key);
                    }
                }
            }

            // Default to the first valid head for the selected breed/sex (if available),
            // so switching breed/sex immediately shows a matching head mesh.
            _selectedHeadIndex = _headKeys.Count > 1 ? 1 : 0;
            _draft.HeadMeshKey = _headKeys.Count > 0 ? _headKeys[_selectedHeadIndex] : string.Empty;
            UpdateHeadValueText();
            _onPreviewHeadChanged?.Invoke(_draft.HeadMeshKey);
        }

        private void CycleHeadPrevious()
        {
            if (_headKeys.Count <= 1)
                return;

            _selectedHeadIndex--;
            if (_selectedHeadIndex < 0)
                _selectedHeadIndex = _headKeys.Count - 1;
            _draft.HeadMeshKey = _headKeys[_selectedHeadIndex];
            UpdateHeadValueText();
            _onPreviewHeadChanged?.Invoke(_draft.HeadMeshKey);
        }

        private void CycleHeadNext()
        {
            if (_headKeys.Count <= 1)
                return;

            _selectedHeadIndex++;
            if (_selectedHeadIndex >= _headKeys.Count)
                _selectedHeadIndex = 0;
            _draft.HeadMeshKey = _headKeys[_selectedHeadIndex];
            UpdateHeadValueText();
            _onPreviewHeadChanged?.Invoke(_draft.HeadMeshKey);
        }

        private void UpdateHeadValueText()
        {
            if (_headValueText == null)
                return;

            if (_headKeys.Count == 0 || string.IsNullOrWhiteSpace(_headKeys[_selectedHeadIndex]))
            {
                _headValueText.text = "None";
                return;
            }

            _headValueText.text = _headKeys[_selectedHeadIndex];
        }

        private void NotifyBodyPreviewChanged()
        {
            _detailsText.text = $"Current Body\nHeight: {_draft.Height}\nWeight: {_draft.Weight}";
            _onPreviewBodyChanged?.Invoke(_draft.Height, _draft.Weight);
        }

        private void SetPreviewOverlay(string text)
        {
            if (_previewOverlayText == null)
                return;

            string cleaned = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            _previewOverlayText.text = cleaned;
            _previewOverlayText.gameObject.SetActive(!string.IsNullOrEmpty(cleaned));
        }

        private void ClearPreviewOverlay()
        {
            if (_previewOverlayText == null)
                return;

            _previewOverlayText.text = string.Empty;
            _previewOverlayText.gameObject.SetActive(false);
        }

        private static void StyleActionButton(Button button, bool primary)
        {
            if (button == null)
                return;

            var img = button.GetComponent<Image>();
            if (img != null)
                img.color = primary ? new Color(0.10f, 0.78f, 0.90f, button.interactable ? 0.92f : 0.45f) : new Color(0.12f, 0.20f, 0.30f, 0.92f);

            var txt = button.GetComponentInChildren<Text>();
            if (txt != null)
                txt.color = button.interactable ? new Color(0.94f, 0.98f, 1f, 1f) : new Color(0.72f, 0.79f, 0.86f, 0.85f);

            var rt = button.transform as RectTransform;
            if (rt != null)
                rt.sizeDelta = primary ? new Vector2(130f, 34f) : new Vector2(150f, 34f);
        }

        private static void AddSciFiFrameAccents(RectTransform parent)
        {
            if (parent == null)
                return;

            static RectTransform AddLine(RectTransform p, string name, Vector2 min, Vector2 max, Color color)
            {
                var line = AOStyleUiFactory.CreatePanel(name, p, color);
                line.anchorMin = min;
                line.anchorMax = max;
                line.offsetMin = Vector2.zero;
                line.offsetMax = Vector2.zero;
                return line;
            }

            Color lineColor = new Color(0.23f, 0.87f, 0.95f, 0.36f);
            AddLine(parent, "TopLine", new Vector2(0.02f, 0.985f), new Vector2(0.98f, 0.988f), lineColor);
            AddLine(parent, "BottomLine", new Vector2(0.02f, 0.012f), new Vector2(0.98f, 0.015f), lineColor);
            AddLine(parent, "LeftLine", new Vector2(0.012f, 0.02f), new Vector2(0.015f, 0.98f), lineColor);
            AddLine(parent, "RightLine", new Vector2(0.985f, 0.02f), new Vector2(0.988f, 0.98f), lineColor);
        }

        private static RawImage CreateBackgroundImage(RectTransform parent)
        {
            var bgGo = new GameObject("CharacterSelectBackground", typeof(RectTransform), typeof(RawImage));
            bgGo.transform.SetParent(parent, false);
            var bg = bgGo.GetComponent<RawImage>();
            var rt = (RectTransform)bgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            bg.raycastTarget = false;
            bg.color = Color.white;
            bgGo.AddComponent<RawImageAspectFill>();
            bgGo.transform.SetAsFirstSibling();

            // Priority 1: StreamingAssets override for quick art iteration without code changes.
            string uiDir = Path.Combine(Application.streamingAssetsPath, "AOData", "UI");
            string[] candidateNames =
            {
                "character_select_bg.png",
                "character_select_bg.jpg",
                "character_select_bg.jpeg",
                "player_select_bg.png",
                "player_select_bg.jpg",
                "player_select_bg.jpeg"
            };

            for (int i = 0; i < candidateNames.Length; i++)
            {
                string candidatePath = Path.Combine(uiDir, candidateNames[i]);
                if (!File.Exists(candidatePath))
                    continue;

                try
                {
                    byte[] bytes = File.ReadAllBytes(candidatePath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        bg.texture = tex;
                        Debug.Log($"[CharacterSelectBg] Loaded StreamingAssets background: {candidatePath}");
                        return bg;
                    }
                }
                catch
                {
                    // Try next candidate.
                }
            }

            // Priority 2: Resources fallback packaged with build.
            var resTex = Resources.Load<Texture2D>("UI/CharacterSelect/character_select_bg");
            if (resTex != null)
            {
                bg.texture = resTex;
                Debug.Log("[CharacterSelectBg] Loaded Resources background: UI/CharacterSelect/character_select_bg");
            }
            else
            {
                Debug.LogWarning($"[CharacterSelectBg] No background found in {uiDir}. Expected one of: {string.Join(", ", candidateNames)}");
            }

            return bg;
        }

        private void PrepareCreateStepUi()
        {
            if (_infoCard != null)
                _infoCard.gameObject.SetActive(false);
            if (_sidePanel != null)
                _sidePanel.gameObject.SetActive(false);
            if (_mainButtonRow != null)
                _mainButtonRow.gameObject.SetActive(false);
            if (_stepContent != null)
                _stepContent.gameObject.SetActive(true);

            ClearStepContentChildren();
            ClearContentChildren();
            _onCreateFlowStateChanged?.Invoke(true);
        }

        public void SetPreviewTexture(Texture texture)
        {
            if (_content == null)
                return;

            if (_previewRawImage == null)
            {
                var go = new GameObject("CharacterPreviewRawImage", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(_content, false);
                _previewRawImage = go.GetComponent<RawImage>();
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(6f, 6f);
                rt.offsetMax = new Vector2(-6f, -6f);
                _previewRawImage.color = Color.white;
                _previewRawImage.raycastTarget = true;

                var dragProxy = go.GetComponent<PreviewDragProxy>();
                if (dragProxy == null)
                    dragProxy = go.AddComponent<PreviewDragProxy>();
                dragProxy.OnDragDelta = delta => _onPreviewDragged?.Invoke(delta);
                go.transform.SetAsFirstSibling();
            }

            _previewRawImage.raycastTarget = true;
            var proxy = _previewRawImage.GetComponent<PreviewDragProxy>();
            if (proxy == null)
                proxy = _previewRawImage.gameObject.AddComponent<PreviewDragProxy>();
            proxy.OnDragDelta = delta => _onPreviewDragged?.Invoke(delta);
            _previewRawImage.texture = texture;
        }

        private void ClearContentChildren()
        {
            if (_content == null)
                return;

            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i);
                if (_previewRawImage != null && child == _previewRawImage.transform)
                    continue;
                Destroy(child.gameObject);
            }
        }

        private void ClearStepContentChildren()
        {
            if (_stepContent == null)
                return;

            for (int i = _stepContent.childCount - 1; i >= 0; i--)
                Destroy(_stepContent.GetChild(i).gameObject);
        }
    }
}
