using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AO.Data.Unity;
using AO.Unity.AOStyle;
using AO.Core.Characters;
using AO.Core.Stats;
using AO.Unity;
using AO.Unity.World;
using System.IO;
using Newtonsoft.Json;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.Prototype
{
    public partial class PrototypeClientUGUI : MonoBehaviour
    {
        private const string LocalPrefsFolderName = "Preferences";
        private const string LocalAccountName = "local_account";
        private const string LocalClientPrefsFileName = "client_prefs.json";

        private enum NanoCastPhase
        {
            None = 0,
            Attack = 1,
            Recharge = 2
        }

        private sealed class ActiveNanoCast
        {
            public int NanoId;
            public string Name;
            public float AttackSeconds;
            public float RechargeSeconds;
            public float PhaseStartedAt;
            public NanoCastPhase Phase;
            public float LoopClipSeconds;
            public float NextLoopAt;
            public bool PostCastQueued;
            public float PostCastAt;
        }

        [Serializable]
        private sealed class PersistedCharacterProfile
        {
            public string Name;
            public int Level;
            public int BreedId;
            public int Sex;
            public int ProfessionId;
            public string ProfessionName;
            public string BreedLabel;
            public int Height;
            public int Weight;
            public string HeadMeshKey;
            public int StartPlayfieldId;
            public bool HasLastLocation;
            public float LastAoX;
            public float LastAoY;
            public float LastAoZ;
            public float LastYaw;
            public float LastWorldX;
            public float LastWorldY;
            public float LastWorldZ;
        }

        [Serializable]
        private sealed class PersistedWindowLayoutEntry
        {
            public string Id;
            public bool Active;
            public bool Docked;
            public int DockSiblingIndex;
            public string ParentScope;
            public float AnchoredX;
            public float AnchoredY;
            public float SizeX;
            public float SizeY;
        }

        [Serializable]
        private sealed class PersistedCharacterUiLayout
        {
            public string CharacterName;
            public string UpdatedUtc;
            public List<PersistedWindowLayoutEntry> Windows = new(); // legacy schema
            public Dictionary<string, PersistedWindowLayoutEntry> WindowsById = new(StringComparer.OrdinalIgnoreCase);
            public PersistedDockLayout Dock = new();
        }

        [Serializable]
        private sealed class PersistedDockLayout
        {
            public string UpdatedUtc;
            public List<string> OrderedWindowIds = new();
        }

        [Serializable]
        private sealed class LocalClientPreferencesFile
        {
            public int Version = 1;
            public string Account = LocalAccountName;
            public List<PersistedCharacterProfile> Characters = new();
            public List<PersistedCharacterUiLayout> UiLayouts = new();
        }

        [SerializeField] private int maxVisibleBrowserItems = 300;
        [SerializeField] private int defaultEquipSlot = 6;
        [SerializeField] private MonoBehaviour characterSource;
        [SerializeField] private bool autoFindCharacterSource = true;

        private PrototypeUiContext _context;
        private AOStyleUiFactory.WindowRefs _itemBrowserWindow;
        private AOStyleUiFactory.WindowRefs _backpackWindow;
        private AOStyleUiFactory.WindowRefs _inventoryWindow;
        private AOStyleUiFactory.WindowRefs _wearWindow;
        private AOStyleUiFactory.WindowRefs _statsWindow;
        private AOStyleUiFactory.WindowRefs _skillsWindow;
        private AOStyleUiFactory.WindowRefs _programsWindow;
        private AOStyleUiFactory.WindowRefs _ncuWindow;
        private AOStyleUiFactory.WindowRefs _characterSettingsWindow;
        private AOStyleUiFactory.WindowRefs _teleportWindow;
        private AOStyleUiFactory.WindowRefs _questEditorWindow;
        private AOStyleUiFactory.WindowRefs _npcDialogWindow;
        private AOStyleUiFactory.WindowRefs _lookAtWindow;
        private AOStyleUiFactory.WindowRefs _corpseLootWindow;
        private AOStyleUiFactory.WindowRefs _f10Window;
        private AOStyleUiFactory.WindowRefs _chatWindow;
        private AOStyleUiFactory.WindowRefs _f10UnsavedWindow;
        private F10SettingsWindowView _f10SettingsView;
        private Dictionary<string, List<KeyBindingStore.KeyChord>> _runtimeHotkeys =
            new Dictionary<string, List<KeyBindingStore.KeyChord>>(StringComparer.OrdinalIgnoreCase);
        private Text _npcDialogText;
        private Text _lookAtText;
        private Text _corpseLootText;
        private Text _corpseLootHoverText;
        private Image _corpseLootHoverIcon;
        private Sprite _corpseLootHoverBackpackSprite;
        private RectTransform _corpseLootViewport;
        private RectTransform _corpseLootSlotsRoot;
        private GridLayoutGroup _corpseLootGridLayout;
        private Canvas _uiCanvas;
        private readonly List<Image> _corpseLootSlotImages = new();
        private readonly List<Image> _corpseLootSlotIconImages = new();
        private readonly List<Button> _corpseLootSlotButtons = new();
        private readonly List<Text> _corpseLootSlotLabels = new();
        private readonly List<ItemHoverTooltip> _corpseLootSlotTooltips = new();
        private readonly List<AuthoritativeNetworkClient.LootItem> _activeCorpseLootItems = new();
        private CharacterRuntimeBridge _lookAtTarget;
        private CharacterRuntimeBridge _corpseLootTarget;
        private string _activeCorpseLootEntityId = string.Empty;
        private Coroutine _emptyCorpseCleanupCoroutine;
        private RectTransform _npcDialogChatScrollContent;
        private RectTransform _npcDialogOptionsRoot;
        private string _npcDialogConversationBody = string.Empty;
        private string _npcDialogSpeakerName = "NPC";
        private int _activeQuestDialogNpcId;
        private CharacterRuntimeBridge _activeNpcDialogTarget;
        private string _activeQuestTradeSessionId = string.Empty;
        private List<AuthoritativeNetworkClient.QuestTradeOffer> _activeQuestTradeOffers = new();
        private AuthoritativeNetworkClient _authoritativeClient;
        private Font _npcDialogFont;
        private bool _suppressNextQuestDialogOpen;
        private CharacterRuntimeBridge _selfBridge;
        private RectTransform _targetMarkerRoot;
        private Text _targetMarkerText;
        private RectTransform _targetHealthBarRoot;
        private RectTransform _targetHealthBarOutline;
        private readonly List<Image> _targetHealthSegments = new();
        private RectTransform _topTargetBarRoot;
        private Text _topTargetNameText;
        private RectTransform _topTargetHealthBarOutline;
        private readonly List<Image> _topTargetHealthSegments = new();
        private readonly Dictionary<int, TextMesh> _worldNameplatesByBridgeId = new();
        private readonly Dictionary<int, float> _worldNameplateYOffsetByBridgeId = new();
        private CharacterRuntimeBridge[] _cachedRuntimeBridges = Array.Empty<CharacterRuntimeBridge>();
        private float _nextRuntimeBridgeCacheRefreshAt;
        private float _nextWorldNameplateRefreshAt;
        private RectTransform _targetOverlayRoot;
        private DockPaneController _dockPane;
        private RectTransform _nanoCastBarRoot;
        private RectTransform _nanoCastBarFillRect;
        private Image _nanoCastBarFill;
        private Text _nanoCastBarText;
        private RectTransform _quickInfoBarRoot;
        private Text _creditsText;
        private Text _ncuSummaryText;
        private ActiveNanoCast _activeNanoCast;
        private RectTransform _characterSelectionRoot;
        private CharacterSelectionWindowView _characterSelectionView;
        private AOGameServerSession _gameServerSession;
        private bool _characterFlowActive;
        private bool _savedWalkerEnabled = true;
        private GameObject _characterCreatePreviewRoot;
        private CharacterRuntimeBridge _characterCreatePreviewBridge;
        private CharacterAppearanceController _characterCreatePreviewAppearance;
        private Camera _characterCreatePreviewCamera;
        private RenderTexture _characterCreatePreviewTexture;
        private float _characterCreatePreviewOrbitYaw = 165f;
        private RectTransform _uiRoot;
        private RectTransform _uiBodyRoot;
        private readonly Dictionary<string, AOStyleUiFactory.WindowRefs> _windowByPersistId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<RectTransform, string> _persistIdByRoot = new();
        private LocalClientPreferencesFile _clientPrefs;
        private string _activeProfileName = "PrototypeCharacter";

        private void Start()
        {
            EnsureEventSystem();
            _clientPrefs = LoadLocalClientPreferences();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _context = new PrototypeUiContext(maxVisibleBrowserItems, defaultEquipSlot);
            AO.Unity.AOStyle.WindowDragHandle.WindowTransformCommitted += OnWindowTransformCommitted;

            var canvasGo = new GameObject("AO Prototype UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            _uiCanvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Keep a logical root container but no full-screen tinted background panel.
            var root = AOStyleUiFactory.CreatePanel("Root", canvasGo.transform, new Color(0f, 0f, 0f, 0f));
            var rootImage = root.GetComponent<Image>();
            if (rootImage != null) rootImage.raycastTarget = false;
            _targetOverlayRoot = root;
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 1f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            _uiRoot = root;

            var body = AOStyleUiFactory.CreatePanel("Body", root, new Color(0f, 0f, 0f, 0f));
            var bodyImage = body.GetComponent<Image>();
            if (bodyImage != null) bodyImage.raycastTarget = false;
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 1f);
            body.offsetMin = new Vector2(10f, 10f);
            body.offsetMax = new Vector2(-10f, -10f);
            _uiBodyRoot = body;

            _dockPane = BuildRightDock(body, font);

            _itemBrowserWindow = AOStyleUiFactory.CreateWindow(body, font, "Item Browser", new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            ConfigureWindowFrame(_itemBrowserWindow, new Vector2(820f, 760f), new Vector2(10f, -10f), new Vector2(520f, 320f));
            _itemBrowserWindow.Root.pivot = new Vector2(0f, 1f);
            _itemBrowserWindow.Root.anchoredPosition = new Vector2(10f, -10f);
            _itemBrowserWindow.Content.gameObject.AddComponent<ItemBrowserWindowView>().Initialize(_context, font);
            _itemBrowserWindow.Root.gameObject.SetActive(false);

            _inventoryWindow = AOStyleUiFactory.CreateWindow(body, font, "Inventory (30)", new Vector2(1f, 1f), new Vector2(1f, 1f));
            float inventoryWidth = 218f;
            ConfigureWindowFrame(
                _inventoryWindow,
                new Vector2(inventoryWidth, 218f),
                new Vector2(-10f, -10f),
                new Vector2(210f, 210f),
                new Vector2(CalcInventoryWindowWidth(10), CalcInventoryWindowHeight(10)));
            _inventoryWindow.Content.gameObject.AddComponent<InventoryWindowView>().Initialize(_context, font, canvas);

            _backpackWindow = AOStyleUiFactory.CreateWindow(root, font, "Backpack (21)", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ConfigureWindowFrame(
                _backpackWindow,
                new Vector2(218f, 218f),
                Vector2.zero,
                new Vector2(210f, 210f),
                new Vector2(CalcBackpackWindowWidth(7), CalcBackpackWindowHeight(7)));
            _backpackWindow.Root.pivot = new Vector2(0.5f, 0.5f);
            _backpackWindow.Content.gameObject.AddComponent<BackpackWindowView>().Initialize(_context, font, canvas);
            if (_backpackWindow.CloseButton != null)
                _backpackWindow.CloseButton.onClick.AddListener(() => _context?.CloseOpenBackpack());
            _backpackWindow.Root.gameObject.SetActive(false);

            _wearWindow = AOStyleUiFactory.CreateWindow(body, font, "Equipment", new Vector2(1f, 1f), new Vector2(1f, 1f));
            ConfigureWindowFrame(_wearWindow, new Vector2(218f, 360f), new Vector2(-10f, -238f), new Vector2(218f, 360f));
            _wearWindow.Content.gameObject.AddComponent<WearWindowView>().Initialize(_context, font);

            _statsWindow = AOStyleUiFactory.CreateWindow(body, font, "Stats", new Vector2(1f, 1f), new Vector2(1f, 1f));
            ConfigureWindowFrame(_statsWindow, new Vector2(218f, 320f), new Vector2(-10f, -504f), new Vector2(210f, 250f));
            _statsWindow.Content.gameObject.AddComponent<StatsWindowView>().Initialize(_context, font);

            _characterSettingsWindow = AOStyleUiFactory.CreateWindow(body, font, "Character Settings", new Vector2(1f, 1f), new Vector2(1f, 1f));
            ConfigureWindowFrame(_characterSettingsWindow, new Vector2(560f, 228f), new Vector2(-236f, -504f), new Vector2(420f, 210f));
            _characterSettingsWindow.Content.gameObject.AddComponent<CharacterSettingsWindowView>().Initialize(_context, font);

            _teleportWindow = AOStyleUiFactory.CreateWindow(body, font, "Teleport", new Vector2(0f, 1f), new Vector2(0f, 1f));
            ConfigureWindowFrame(_teleportWindow, new Vector2(520f, 228f), new Vector2(10f, -10f), new Vector2(420f, 208f));
            _teleportWindow.Root.pivot = new Vector2(0f, 1f);
            _teleportWindow.Root.anchoredPosition = new Vector2(10f, -10f);
            _teleportWindow.Content.gameObject.AddComponent<TeleportWindowView>().Initialize(_context, font);
            _teleportWindow.Root.gameObject.SetActive(false);

            _questEditorWindow = AOStyleUiFactory.CreateWindow(body, font, "Quest Editor", new Vector2(0f, 1f), new Vector2(0f, 1f));
            ConfigureWindowFrame(_questEditorWindow, new Vector2(1060f, 680f), new Vector2(10f, -10f), new Vector2(760f, 420f));
            _questEditorWindow.Root.pivot = new Vector2(0f, 1f);
            _questEditorWindow.Root.anchoredPosition = new Vector2(10f, -10f);
            _questEditorWindow.Content.gameObject.AddComponent<QuestEditorWindowView>().Initialize(_context, font);
            _questEditorWindow.Root.gameObject.SetActive(false);

            _skillsWindow = AOStyleUiFactory.CreateWindow(body, font, "Skills", new Vector2(0f, 1f), new Vector2(0f, 1f));
            ConfigureWindowFrame(_skillsWindow, new Vector2(1220f, 700f), new Vector2(10f, -10f), new Vector2(720f, 440f));
            _skillsWindow.Root.pivot = new Vector2(0f, 1f);
            _skillsWindow.Root.anchoredPosition = new Vector2(10f, -10f);
            _skillsWindow.Content.gameObject.AddComponent<SkillsWindowView>().Initialize(_context, font);
            _skillsWindow.Root.gameObject.SetActive(false);

            _programsWindow = AOStyleUiFactory.CreateWindow(body, font, "Programs", new Vector2(0f, 1f), new Vector2(0f, 1f));
            ConfigureWindowFrame(_programsWindow, new Vector2(inventoryWidth, 460f), new Vector2(10f, -10f), new Vector2(inventoryWidth, 280f));
            _programsWindow.Root.pivot = new Vector2(0f, 1f);
            _programsWindow.Root.anchoredPosition = new Vector2(10f, -10f);
            _programsWindow.Content.gameObject.AddComponent<ProgramsWindowView>().Initialize(_context, font);
            _programsWindow.Root.gameObject.SetActive(false);

            _ncuWindow = AOStyleUiFactory.CreateWindow(body, font, "NCU", new Vector2(0f, 1f), new Vector2(0f, 1f));
            ConfigureWindowFrame(_ncuWindow, new Vector2(inventoryWidth, 340f), new Vector2(640f, -10f), new Vector2(inventoryWidth, 240f));
            _ncuWindow.Root.pivot = new Vector2(0f, 1f);
            _ncuWindow.Root.anchoredPosition = new Vector2(640f, -10f);
            _ncuWindow.Content.gameObject.AddComponent<NcuWindowView>().Initialize(_context, font);
            _ncuWindow.Root.gameObject.SetActive(false);

            BuildQuickInfoBar(body, font);

            _chatWindow = AOStyleUiFactory.CreateWindow(body, font, "Chat / Damage", new Vector2(0f, 0f), new Vector2(0f, 0f));
            ConfigureWindowFrame(_chatWindow, new Vector2(520f, 240f), new Vector2(10f, 46f), new Vector2(360f, 180f));
            _chatWindow.Root.pivot = new Vector2(0f, 0f);
            _chatWindow.Root.anchoredPosition = new Vector2(10f, 46f);
            _chatWindow.Content.gameObject.AddComponent<ChatDamageWindowView>().Initialize(_context, font, body);

            _npcDialogWindow = AOStyleUiFactory.CreateWindow(body, font, "Dialog", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ConfigureWindowFrame(_npcDialogWindow, new Vector2(640f, 360f), new Vector2(0f, 0f), new Vector2(420f, 260f), new Vector2(980f, 640f));
            _npcDialogWindow.Root.pivot = new Vector2(0.5f, 0.5f);
            _npcDialogWindow.Root.anchoredPosition = new Vector2(0f, 40f);
            BuildNpcDialogContent(_npcDialogWindow.Content, font);
            if (_npcDialogWindow.CloseButton != null)
            {
                _npcDialogWindow.CloseButton.onClick.RemoveAllListeners();
                _npcDialogWindow.CloseButton.onClick.AddListener(OnNpcDialogCloseClicked);
            }
            _npcDialogWindow.Root.gameObject.SetActive(false);

            _lookAtWindow = AOStyleUiFactory.CreateWindow(body, font, "Look At", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ConfigureWindowFrame(_lookAtWindow, new Vector2(420f, 230f), new Vector2(280f, 40f), new Vector2(300f, 180f), new Vector2(560f, 320f));
            _lookAtWindow.Root.pivot = new Vector2(0.5f, 0.5f);
            _lookAtWindow.Root.anchoredPosition = new Vector2(280f, 40f);
            BuildLookAtContent(_lookAtWindow.Content, font);
            if (_lookAtWindow.CloseButton != null)
            {
                _lookAtWindow.CloseButton.onClick.RemoveAllListeners();
                _lookAtWindow.CloseButton.onClick.AddListener(CloseLookAtWindow);
            }
            _lookAtWindow.Root.gameObject.SetActive(false);

            _corpseLootWindow = AOStyleUiFactory.CreateWindow(body, font, "Remains", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ConfigureWindowFrame(
                _corpseLootWindow,
                new Vector2(inventoryWidth, 218f),
                new Vector2(220f, 20f),
                new Vector2(210f, 210f),
                new Vector2(CalcInventoryWindowWidth(10), CalcInventoryWindowHeight(10)));
            _corpseLootWindow.Root.pivot = new Vector2(0.5f, 0.5f);
            _corpseLootWindow.Root.anchoredPosition = new Vector2(220f, 20f);
            BuildCorpseLootContent(_corpseLootWindow.Content, font);
            if (_corpseLootWindow.CloseButton != null)
            {
                _corpseLootWindow.CloseButton.onClick.RemoveAllListeners();
                _corpseLootWindow.CloseButton.onClick.AddListener(CloseCorpseLootWindow);
            }
            _corpseLootWindow.Root.gameObject.SetActive(false);

            _f10Window = AOStyleUiFactory.CreateWindow(body, font, "F10 Settings", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ConfigureWindowFrame(_f10Window, new Vector2(920f, 680f), new Vector2(0f, 10f), new Vector2(640f, 420f), new Vector2(1300f, 900f));
            _f10SettingsView = _f10Window.Content.gameObject.AddComponent<F10SettingsWindowView>();
            _f10SettingsView.Initialize(font, OnKeyBindingsApplied);
            if (_f10Window.CloseButton != null)
            {
                _f10Window.CloseButton.onClick.RemoveAllListeners();
                _f10Window.CloseButton.onClick.AddListener(TryCloseF10Window);
            }
            _f10Window.Root.gameObject.SetActive(false);

            // Unity Editor reserves F10 for its native menu on some platforms.
            // Keep settings reachable without relying on the keyboard shortcut.
            var settingsButton = AOStyleUiFactory.CreateButton(
                "OpenSettingsButton", root, "Settings", font,
                ToggleF10WindowWithUnsavedCheck, 92f);
            var settingsButtonRt = (RectTransform)settingsButton.transform;
            settingsButtonRt.anchorMin = new Vector2(1f, 1f);
            settingsButtonRt.anchorMax = new Vector2(1f, 1f);
            settingsButtonRt.pivot = new Vector2(1f, 1f);
            settingsButtonRt.anchoredPosition = new Vector2(-12f, -12f);
            settingsButtonRt.sizeDelta = new Vector2(92f, 26f);

            RegisterPersistedWindow("item_browser", _itemBrowserWindow);
            RegisterPersistedWindow("inventory", _inventoryWindow);
            RegisterPersistedWindow("backpack", _backpackWindow);
            RegisterPersistedWindow("equipment", _wearWindow);
            RegisterPersistedWindow("stats", _statsWindow);
            RegisterPersistedWindow("skills", _skillsWindow);
            RegisterPersistedWindow("programs", _programsWindow);
            RegisterPersistedWindow("ncu", _ncuWindow);
            RegisterPersistedWindow("character_settings", _characterSettingsWindow);
            RegisterPersistedWindow("teleport", _teleportWindow);
            RegisterPersistedWindow("chat_damage", _chatWindow);
            RegisterPersistedWindow("npc_dialog", _npcDialogWindow);
            RegisterPersistedWindow("look_at", _lookAtWindow);
            RegisterPersistedWindow("corpse_loot", _corpseLootWindow);
            RegisterPersistedWindow("quest_editor", _questEditorWindow);
            RegisterPersistedWindow("f10", _f10Window);

            RegisterDockableWindow(_inventoryWindow, body);
            RegisterDockableWindow(_wearWindow, body);
            RegisterDockableWindow(_statsWindow, body);
            RegisterDockableWindow(_skillsWindow, body);
            RegisterDockableWindow(_programsWindow, body);
            RegisterDockableWindow(_ncuWindow, body);
            RegisterDockableWindow(_characterSettingsWindow, body);
            RegisterDockableWindow(_itemBrowserWindow, body);
            RegisterDockableWindow(_teleportWindow, body);
            RegisterDockableWindow(_questEditorWindow, body);
            RegisterDockableWindow(_f10Window, body);
            RegisterDockableWindow(_chatWindow, body);

            ClampAllWindowsToViewport(body, root);

            _context.StateChanged += OnContextStateChanged;
            AO.Unity.AOStyle.ChatDamageWindowView.ChatCommandIssued += OnChatCommandIssued;
            if (characterSource == null && autoFindCharacterSource)
                characterSource = FindCharacterSourceBehaviour();

            _selfBridge = ResolveExternalBridge(characterSource);
            if (_selfBridge == null || _selfBridge.Character == null)
                _selfBridge = ResolvePreferredSelfBridgeInScene();
            if (_selfBridge != null)
                characterSource = _selfBridge;
            _authoritativeClient = _selfBridge != null ? _selfBridge.GetComponent<AuthoritativeNetworkClient>() : null;
            if (_authoritativeClient != null)
            {
                _authoritativeClient.QuestDialogReceived += HandleQuestDialogReceived;
                _authoritativeClient.QuestTradeReceived += HandleQuestTradeReceived;
                _authoritativeClient.LootReceived += HandleLootReceived;
                _authoritativeClient.RuntimeEntityRemoved += HandleRuntimeEntityRemoved;
            }

            _context.Initialize(ResolveExternalCharacter(characterSource), _selfBridge);
            BuildTargetMarker(font);
            BuildNanoCastBar(font);
            UpdateQuickInfoBar();
            OnContextStateChanged();
            BuildCharacterSelectionFlow(body, root, font);
        }

        private void ClampAllWindowsToViewport(RectTransform body, RectTransform root)
        {
            ClampWindow(_itemBrowserWindow, body);
            ClampWindow(_inventoryWindow, body);
            ClampWindow(_backpackWindow, root);
            ClampWindow(_wearWindow, body);
            ClampWindow(_statsWindow, body);
            ClampWindow(_characterSettingsWindow, body);
            ClampWindow(_teleportWindow, body);
            ClampWindow(_questEditorWindow, body);
            ClampWindow(_skillsWindow, body);
            ClampWindow(_programsWindow, body);
            ClampWindow(_ncuWindow, body);
            ClampWindow(_chatWindow, body);
            ClampWindow(_npcDialogWindow, body);
            ClampWindow(_f10Window, body);
        }

        private static void ClampWindow(AOStyleUiFactory.WindowRefs window, RectTransform parent)
        {
            if (window?.Root == null || parent == null)
                return;
            WindowDragHandle.ClampToParentBounds(window.Root, parent);
        }

        private DockPaneController BuildRightDock(RectTransform body, Font font)
        {
            var dock = AOStyleUiFactory.CreatePanel("RightDock", body, new Color(0f, 0f, 0f, 0.001f));
            dock.anchorMin = new Vector2(1f, 0f);
            dock.anchorMax = new Vector2(1f, 1f);
            dock.pivot = new Vector2(1f, 1f);
            dock.sizeDelta = new Vector2(236f, 0f);
            dock.anchoredPosition = Vector2.zero;

            var dockListHost = AOStyleUiFactory.CreatePanel("DockListHost", dock, new Color(0f, 0f, 0f, 0.001f));
            dockListHost.anchorMin = new Vector2(0f, 0f);
            dockListHost.anchorMax = new Vector2(1f, 1f);
            dockListHost.offsetMin = new Vector2(4f, 4f);
            dockListHost.offsetMax = new Vector2(-4f, -4f);

            var dockContent = AOStyleUiFactory.CreateScrollContent(dockListHost);
            var hostImage = dockContent.parent?.parent?.GetComponent<Image>();
            if (hostImage != null)
                hostImage.color = new Color(0f, 0f, 0f, 0f);
            var viewportImage = dockContent.parent?.GetComponent<Image>();
            if (viewportImage != null)
                viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            var scrollRect = dockContent.parent?.parent?.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.scrollSensitivity = 90f;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }
            var layout = dockContent.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childForceExpandWidth = false;
                layout.padding = new RectOffset(0, 0, 0, 220);
            }

            var controller = dock.gameObject.AddComponent<DockPaneController>();
            controller.DockHost = dock;
            controller.DockContent = dockContent;
            controller.FloatingParent = body;
            controller.DockScrollRect = scrollRect;
            controller.DockHintImage = dock.GetComponent<Image>();
            controller.HiddenHintAlpha = 0.001f;
            controller.ActiveHintAlpha = 0.14f;
            controller.SetHintVisible(false);
            return controller;
        }

        private void RegisterDockableWindow(AOStyleUiFactory.WindowRefs window, RectTransform floatingParent)
        {
            if (window?.Root == null || _dockPane == null)
                return;

            var state = window.Root.gameObject.GetComponent<WindowDockState>();
            if (state == null)
                state = window.Root.gameObject.AddComponent<WindowDockState>();
            state.FloatingParent = floatingParent;

            var header = window.Root.Find("Header") as RectTransform;
            if (header == null)
                return;

            var dockHandle = header.gameObject.GetComponent<WindowDockHandle>();
            if (dockHandle == null)
                dockHandle = header.gameObject.AddComponent<WindowDockHandle>();
            dockHandle.Target = window.Root;
            dockHandle.DockController = _dockPane;
        }

        private void RegisterPersistedWindow(string id, AOStyleUiFactory.WindowRefs window)
        {
            if (string.IsNullOrWhiteSpace(id) || window?.Root == null)
                return;
            _windowByPersistId[id.Trim()] = window;
            _persistIdByRoot[window.Root] = id.Trim();
        }

        private void OnDestroy()
        {
            CaptureWindowLayoutForCharacter(_activeProfileName);
            UpdateActiveCharacterLastLocation();
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
            AO.Unity.AOStyle.WindowDragHandle.WindowTransformCommitted -= OnWindowTransformCommitted;
            AO.Unity.AOStyle.ChatDamageWindowView.ChatCommandIssued -= OnChatCommandIssued;
            SetCombatBarPause(false);
            CleanupCharacterCreatePreview();
            if (_authoritativeClient != null)
            {
                _authoritativeClient.QuestDialogReceived -= HandleQuestDialogReceived;
                _authoritativeClient.QuestTradeReceived -= HandleQuestTradeReceived;
                _authoritativeClient.LootReceived -= HandleLootReceived;
                _authoritativeClient.RuntimeEntityRemoved -= HandleRuntimeEntityRemoved;
            }
            if (_context != null)
                _context.StateChanged -= OnContextStateChanged;
            if (_gameServerSession != null)
            {
                _gameServerSession.InventoryChanged -= HandleServerInventoryChanged;
                _gameServerSession.CharacterStateChanged -= HandleServerCharacterStateChanged;
            }
        }

        private void OnApplicationQuit()
        {
            CaptureWindowLayoutForCharacter(_activeProfileName);
            UpdateActiveCharacterLastLocation();
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
        }

        private void OnContextStateChanged()
        {
            if (_context == null || _backpackWindow?.Root == null)
                return;

            bool shouldShowBackpack = _context.OpenBackpackId != 0;
            if (_backpackWindow.Root.gameObject.activeSelf != shouldShowBackpack)
                _backpackWindow.Root.gameObject.SetActive(shouldShowBackpack);

            if (shouldShowBackpack)
                _backpackWindow.Root.SetAsLastSibling();

            UpdateQuickInfoBar();
        }

        private void Update()
        {
            HandleConnectionSetupTabNavigation();
            TickCharacterSelectionCarousel();
            if (_characterFlowActive)
                return;

            bool textInputFocused = UiInputUtility.IsTextInputFocused();
            HandleTargetSelectionInput(textInputFocused);
            TickNanoCasting();
            _context?.TickRegeneration(IsSelfSitting());
            _context?.TickAutoAttack(IsSelfAttackActive());
            _context?.PumpUiStateNotifications();
            AutoCloseNpcDialogIfOutOfRange();
            if (Time.unscaledTime >= _nextWorldNameplateRefreshAt)
            {
                UpdateWorldNameplates();
                _nextWorldNameplateRefreshAt = Time.unscaledTime + 0.14f;
            }
            UpdateCorpseLootHoverIndicator();
            UpdateTargetMarker();
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return;
            HandleConfiguredHotkeysInputSystem(textInputFocused);
#else
            HandleConfiguredHotkeysLegacy(textInputFocused);
#endif
        }

        private void AutoCloseNpcDialogIfOutOfRange()
        {
            if (_npcDialogWindow?.Root == null || !_npcDialogWindow.Root.gameObject.activeSelf)
                return;
            if (_selfBridge == null)
                return;
            var dialogTarget = _activeNpcDialogTarget != null ? _activeNpcDialogTarget : _context?.SelectedTarget;
            if (dialogTarget == null)
                return;

            const float dialogMaxRange = 10f;
            float distance = Vector3.Distance(_selfBridge.transform.position, dialogTarget.transform.position);
            if (distance > dialogMaxRange)
                OnNpcDialogCloseClicked();
        }

        private void CloseCorpseLootWindow()
        {
            if (_corpseLootWindow?.Root == null)
                return;

            _corpseLootWindow.Root.gameObject.SetActive(false);
            _corpseLootTarget = null;
        }

        private bool IsSelfAttackActive()
        {
            if (_selfBridge == null)
                return false;
            var appearance = _selfBridge.GetComponent<AO.Unity.World.CharacterAppearanceController>();
            return appearance != null && appearance.IsAttackToggleActive;
        }


        private void BuildQuickInfoBar(RectTransform parent, Font font)
        {
            _quickInfoBarRoot = AOStyleUiFactory.CreatePanel("QuickInfoBar", parent, new Color(0.05f, 0.08f, 0.12f, 0.9f));
            _quickInfoBarRoot.anchorMin = new Vector2(0f, 0f);
            _quickInfoBarRoot.anchorMax = new Vector2(0f, 0f);
            _quickInfoBarRoot.pivot = new Vector2(0f, 0f);
            _quickInfoBarRoot.anchoredPosition = new Vector2(4f, 2f);
            _quickInfoBarRoot.sizeDelta = new Vector2(320f, 28f);

            var layout = _quickInfoBarRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 3, 3);
            layout.spacing = 18f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            _ncuSummaryText = AOStyleUiFactory.CreateText("NcuSummaryText", _quickInfoBarRoot, "NCU: 0/1", font, 12, TextAnchor.MiddleLeft);
            _ncuSummaryText.color = new Color(0.8f, 0.94f, 1f, 1f);
            _creditsText = AOStyleUiFactory.CreateText("CreditsText", _quickInfoBarRoot, "Credits: 0", font, 12, TextAnchor.MiddleLeft);
            _creditsText.color = new Color(0.97f, 0.92f, 0.68f, 1f);
        }

        private void UpdateQuickInfoBar()
        {
            if (_context == null)
                return;

            if (_creditsText != null)
                _creditsText.text = $"Credits: {_context.GetCurrentCredits():N0}";

            if (_ncuSummaryText != null)
                _ncuSummaryText.text = $"NCU: {_context.GetCurrentNcuUsed()}/{_context.GetCurrentNcuCapacity()}";
        }


        private void HandleTargetSelectionInput(bool textInputFocused)
        {
            if (_selfBridge == null || _selfBridge.Character == null)
                _selfBridge = ResolvePreferredSelfBridgeInScene();

#if ENABLE_INPUT_SYSTEM
            bool f1Pressed = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
            bool clickPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool rightClickPressed = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            bool f1Pressed = Input.GetKeyDown(KeyCode.F1);
            bool clickPressed = Input.GetMouseButtonDown(0);
            bool rightClickPressed = Input.GetMouseButtonDown(1);
#endif
            if (!textInputFocused && f1Pressed && _selfBridge != null)
                _context?.TrySetSelectedTarget(_selfBridge);

            if ((!clickPressed && !rightClickPressed) || EventSystem.current == null || EventSystem.current.IsPointerOverGameObject())
                return;

            if (Camera.main == null)
                return;

#if ENABLE_INPUT_SYSTEM
            Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            Vector2 mouse = Input.mousePosition;
#endif
            var ray = Camera.main.ScreenPointToRay(mouse);
            CharacterRuntimeBridge bridge = ResolveBridgeFromRay(ray, out _);
            if (bridge != null)
            {
                if (clickPressed)
                    _context?.TrySetSelectedTarget(bridge);

                if (rightClickPressed)
                {
                    if (IsDeadCorpseTarget(bridge))
                    {
                        OpenCorpseLootWindow(bridge);
                        return;
                    }

                    _activeNpcDialogTarget = bridge;

                    bool requestedAuthoritativeQuest = false;
                    if (_authoritativeClient != null && _authoritativeClient.IsConnected)
                    {
                        var runtimeTrigger = bridge.GetComponentInParent<AuthoritativeRuntimeInteractionTrigger>();
                        if (runtimeTrigger != null)
                        {
                            runtimeTrigger.RequestInteractionFromUi();
                            requestedAuthoritativeQuest = true;
                        }
                    }
                    if (!requestedAuthoritativeQuest)
                        requestedAuthoritativeQuest = TryRequestAuthoritativeQuestDialog(bridge);

                    if (IsAttackableRuntimeEntity(bridge))
                        return;

                    if (!requestedAuthoritativeQuest
                        && _context != null
                        && _context.TryInteractWithTarget(bridge, out string title, out string body))
                    {
                        ShowNpcDialog(title, body);
                    }
                }
            }
        }

        private static bool IsAttackableRuntimeEntity(CharacterRuntimeBridge bridge)
        {
            if (bridge == null)
                return false;

            var identity = bridge.GetComponentInParent<AuthoritativeRuntimeEntityIdentity>();
            return identity != null && identity.Attackable;
        }

        private static CharacterRuntimeBridge ResolveBridgeFromRay(Ray ray, out RaycastHit selectedHit)
        {
            selectedHit = default;

            // Prefer real physics colliders and ignore trigger volumes first.
            var hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    var bridge = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CharacterRuntimeBridge>() : null;
                    if (bridge != null)
                    {
                        selectedHit = hits[i];
                        return bridge;
                    }
                }
            }

            // Fallback to include triggers when needed.
            hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
                return null;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var bridge = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CharacterRuntimeBridge>() : null;
                if (bridge != null)
                {
                    selectedHit = hits[i];
                    return bridge;
                }
            }

            return null;
        }

        

        private void BuildNpcDialogContent(RectTransform content, Font font)
        {
            if (content == null || font == null)
                return;

            var panel = AOStyleUiFactory.CreatePanel("DialogContentRoot", content, new Color(0f, 0f, 0f, 0f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(4f, 4f);
            panel.offsetMax = new Vector2(-4f, -4f);

            var chatHost = AOStyleUiFactory.CreatePanel("DialogChatHost", panel, new Color(0f, 0f, 0f, 0.72f));
            chatHost.anchorMin = new Vector2(0f, 0.35f);
            chatHost.anchorMax = new Vector2(1f, 1f);
            chatHost.offsetMin = new Vector2(6f, 6f);
            chatHost.offsetMax = new Vector2(-6f, -6f);

            _npcDialogChatScrollContent = AOStyleUiFactory.CreateScrollContent(chatHost);
            _npcDialogText = AOStyleUiFactory.CreateText("DialogText", _npcDialogChatScrollContent, string.Empty, font, 13, TextAnchor.UpperLeft);
            _npcDialogFont = font;
            _npcDialogText.supportRichText = true;
            _npcDialogText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _npcDialogText.verticalOverflow = VerticalWrapMode.Overflow;
            var textRt = (RectTransform)_npcDialogText.transform;
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.offsetMin = new Vector2(2f, 0f);
            textRt.offsetMax = new Vector2(-2f, 0f);
            var fit = _npcDialogText.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var optionsHost = AOStyleUiFactory.CreatePanel("DialogOptionsHost", panel, new Color(0f, 0f, 0f, 0.16f));
            optionsHost.anchorMin = new Vector2(0f, 0f);
            optionsHost.anchorMax = new Vector2(1f, 0.35f);
            optionsHost.offsetMin = new Vector2(6f, 6f);
            optionsHost.offsetMax = new Vector2(-6f, -6f);

            _npcDialogOptionsRoot = AOStyleUiFactory.CreateScrollContent(optionsHost);
            var optionsLayout = _npcDialogOptionsRoot.GetComponent<VerticalLayoutGroup>();
            if (optionsLayout != null)
            {
                optionsLayout.spacing = 4f;
                optionsLayout.childControlHeight = false;
                optionsLayout.childForceExpandHeight = false;
            }
        }

        private void ShowNpcDialog(string title, string body)
        {
            if (_npcDialogWindow?.Root == null)
                return;

            if (_npcDialogWindow.Title != null)
                _npcDialogWindow.Title.text = string.IsNullOrWhiteSpace(title) ? "Dialog" : title.Trim();

            if (_npcDialogText != null)
                _npcDialogText.text = string.IsNullOrWhiteSpace(body) ? "..." : body.Trim();
            RebuildNpcDialogOptionButtons(new List<AuthoritativeNetworkClient.QuestDialogOption>(), true);

            _npcDialogWindow.Root.gameObject.SetActive(true);
            _npcDialogWindow.Root.SetAsLastSibling();
        }

        private void BuildLookAtContent(RectTransform content, Font font)
        {
            if (content == null || font == null)
                return;

            var panel = AOStyleUiFactory.CreatePanel("LookAtRoot", content, new Color(0.03f, 0.05f, 0.08f, 0.92f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(6f, 6f);
            panel.offsetMax = new Vector2(-6f, -6f);

            _lookAtText = AOStyleUiFactory.CreateText("LookAtText", panel, string.Empty, font, 13, TextAnchor.UpperLeft);
            _lookAtText.supportRichText = true;
            _lookAtText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lookAtText.verticalOverflow = VerticalWrapMode.Overflow;
            var textRt = (RectTransform)_lookAtText.transform;
            textRt.anchorMin = new Vector2(0f, 0f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.offsetMin = new Vector2(10f, 10f);
            textRt.offsetMax = new Vector2(-10f, -10f);
        }

        private void BuildCorpseLootContent(RectTransform content, Font font)
        {
            if (content == null || font == null)
                return;

            var panel = AOStyleUiFactory.CreatePanel("CorpseLootRoot", content, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            _corpseLootText = AOStyleUiFactory.CreateText("CorpseLootText", panel, "Loot", font, 13, TextAnchor.UpperLeft);
            _corpseLootText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _corpseLootText.verticalOverflow = VerticalWrapMode.Overflow;
            var textRt = (RectTransform)_corpseLootText.transform;
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.offsetMin = new Vector2(10f, -24f);
            textRt.offsetMax = new Vector2(-10f, 0f);

            var scrollGo = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(panel, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(6f, 6f);
            scrollRt.offsetMax = new Vector2(-22f, -30f);
            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            _corpseLootViewport = (RectTransform)viewportGo.transform;
            _corpseLootViewport.anchorMin = Vector2.zero;
            _corpseLootViewport.anchorMax = Vector2.one;
            _corpseLootViewport.offsetMin = Vector2.zero;
            _corpseLootViewport.offsetMax = Vector2.zero;
            var viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            gridGo.transform.SetParent(viewportGo.transform, false);
            _corpseLootSlotsRoot = (RectTransform)gridGo.transform;
            _corpseLootSlotsRoot.anchorMin = new Vector2(0f, 1f);
            _corpseLootSlotsRoot.anchorMax = new Vector2(0f, 1f);
            _corpseLootSlotsRoot.pivot = new Vector2(0f, 1f);
            _corpseLootSlotsRoot.anchoredPosition = Vector2.zero;
            _corpseLootSlotsRoot.sizeDelta = Vector2.zero;

            _corpseLootGridLayout = gridGo.GetComponent<GridLayoutGroup>();
            _corpseLootGridLayout.cellSize = new Vector2(50f, 50f);
            _corpseLootGridLayout.spacing = new Vector2(4f, 4f);
            _corpseLootGridLayout.padding = new RectOffset(0, 0, 0, 0);
            _corpseLootGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _corpseLootGridLayout.constraintCount = 3;
            _corpseLootGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _corpseLootGridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;

            var fitter = gridGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = _corpseLootViewport;
            scrollRect.content = _corpseLootSlotsRoot;

            var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarGo.transform.SetParent(panel, false);
            var scrollbarRt = (RectTransform)scrollbarGo.transform;
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 1f);
            scrollbarRt.offsetMin = new Vector2(-14f, 6f);
            scrollbarRt.offsetMax = new Vector2(-6f, -30f);
            scrollbarGo.GetComponent<Image>().color = new Color(0.14f, 0.18f, 0.24f, 1f);

            var handleSlide = new GameObject("SlidingArea", typeof(RectTransform));
            handleSlide.transform.SetParent(scrollbarGo.transform, false);
            var handleSlideRt = (RectTransform)handleSlide.transform;
            handleSlideRt.anchorMin = Vector2.zero;
            handleSlideRt.anchorMax = Vector2.one;
            handleSlideRt.offsetMin = new Vector2(0f, 6f);
            handleSlideRt.offsetMax = new Vector2(0f, -6f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleSlide.transform, false);
            var handleRt = (RectTransform)handle.transform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(0.35f, 0.45f, 0.62f, 1f);

            var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImage;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            BuildCorpseLootSlotGrid();
        }

        private void BuildCorpseLootSlotGrid()
        {
            _corpseLootSlotImages.Clear();
            _corpseLootSlotIconImages.Clear();
            _corpseLootSlotButtons.Clear();
            _corpseLootSlotLabels.Clear();
            _corpseLootSlotTooltips.Clear();
            if (_corpseLootSlotsRoot == null)
                return;

            const int totalSlots = 30;
            for (int i = 0; i < totalSlots; i++)
            {
                var slot = AOStyleUiFactory.CreatePanel($"CorpseLootSlot_{i}", _corpseLootSlotsRoot, new Color(0.13f, 0.16f, 0.2f, 1f));

                var background = slot.GetComponent<Image>();
                if (background != null)
                    background.raycastTarget = true;
                _corpseLootSlotImages.Add(background);

                var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(slot, false);
                var iconRt = (RectTransform)iconObj.transform;
                iconRt.anchorMin = new Vector2(0.08f, 0.08f);
                iconRt.anchorMax = new Vector2(0.92f, 0.92f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;
                var icon = iconObj.GetComponent<Image>();
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                icon.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                _corpseLootSlotIconImages.Add(icon);

                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
                colors.pressedColor = new Color(0.82f, 0.9f, 1f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = Color.white;
                colors.colorMultiplier = 1f;
                button.colors = colors;
                int slotIndex = i;
                button.onClick.AddListener(() => TryTakeCorpseLootSlot(slotIndex));
                _corpseLootSlotButtons.Add(button);

                var label = AOStyleUiFactory.CreateText("Label", (RectTransform)slot.transform, string.Empty, _npcDialogFont, 10, TextAnchor.UpperLeft);
                label.supportRichText = true;
                label.raycastTarget = false;
                label.color = new Color(0.82f, 0.92f, 1f, 1f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                var labelRt = (RectTransform)label.transform;
                labelRt.anchorMin = new Vector2(0f, 1f);
                labelRt.anchorMax = new Vector2(1f, 1f);
                labelRt.pivot = new Vector2(0f, 1f);
                labelRt.offsetMin = new Vector2(4f, -16f);
                labelRt.offsetMax = new Vector2(-2f, -2f);
                _corpseLootSlotLabels.Add(label);

                var hover = slot.gameObject.AddComponent<ItemHoverTooltip>();
                hover.Context = _context;
                hover.Canvas = _uiCanvas;
                hover.Font = _npcDialogFont;
                hover.InstanceId = 0;
                _corpseLootSlotTooltips.Add(hover);
            }

            RefreshCorpseLootSlots(0);
        }

        private void RefreshCorpseLootSlots(int itemCount)
        {
            if (_corpseLootGridLayout != null)
                _corpseLootGridLayout.constraintCount = 3;

            for (int i = 0; i < _corpseLootSlotImages.Count; i++)
            {
                var background = _corpseLootSlotImages[i];
                if (background == null)
                    continue;
                background.gameObject.SetActive(true);
                background.sprite = null;
                background.color = new Color(0.13f, 0.16f, 0.2f, 1f);

                if (i < _corpseLootSlotIconImages.Count && _corpseLootSlotIconImages[i] != null)
                {
                    _corpseLootSlotIconImages[i].sprite = null;
                    _corpseLootSlotIconImages[i].color = new Color(0.2f, 0.2f, 0.2f, 1f);
                }

                if (i < _corpseLootSlotButtons.Count && _corpseLootSlotButtons[i] != null)
                    _corpseLootSlotButtons[i].interactable = i < _activeCorpseLootItems.Count;

                if (i < _corpseLootSlotLabels.Count && _corpseLootSlotLabels[i] != null)
                    _corpseLootSlotLabels[i].text = string.Empty;

                if (i < _corpseLootSlotTooltips.Count && _corpseLootSlotTooltips[i] != null)
                    _corpseLootSlotTooltips[i].InstanceId = 0;

                if (i >= _activeCorpseLootItems.Count)
                    continue;

                var loot = _activeCorpseLootItems[i];
                var core = AODataManager.Instance != null ? AODataManager.Instance.GetCoreInstance(loot.Aoid) : null;
                Sprite sprite = core != null && _context != null ? _context.GetIconForCore(core) : null;
                background.color = new Color(0.13f, 0.16f, 0.2f, 1f);

                if (i < _corpseLootSlotIconImages.Count && _corpseLootSlotIconImages[i] != null)
                {
                    var icon = _corpseLootSlotIconImages[i];
                    icon.sprite = sprite;
                    icon.color = sprite != null ? Color.white : new Color(0.2f, 0.2f, 0.2f, 1f);
                }

                if (i < _corpseLootSlotLabels.Count && _corpseLootSlotLabels[i] != null)
                    _corpseLootSlotLabels[i].text = BuildCorpseLootSlotBadge(loot);

                if (i < _corpseLootSlotTooltips.Count && _corpseLootSlotTooltips[i] != null)
                    _corpseLootSlotTooltips[i].InstanceId = loot.Aoid;
            }
        }

        private static string BuildCorpseLootSlotBadge(AuthoritativeNetworkClient.LootItem loot)
        {
            if (loot == null)
                return string.Empty;

            if (loot.Quantity > 1)
                return $"<b>x{loot.Quantity}</b>";

            if (loot.Ql.HasValue && loot.Ql.Value > 0)
                return $"<b>{loot.Ql.Value}</b>";

            return string.Empty;
        }

        private void OpenCorpseLootWindow(CharacterRuntimeBridge target)
        {
            if (target == null || _corpseLootWindow?.Root == null)
                return;

            _corpseLootTarget = target;
            _activeCorpseLootItems.Clear();
            string targetName = ResolveTargetName(target);
            if (_corpseLootWindow.Title != null)
                _corpseLootWindow.Title.text = targetName;
            if (_corpseLootText != null)
                _corpseLootText.text = "Loading loot...";
            RefreshCorpseLootSlots(0);

            _corpseLootWindow.Root.gameObject.SetActive(true);
            _corpseLootWindow.Root.SetAsLastSibling();

            var trigger = target.GetComponentInParent<AuthoritativeRuntimeInteractionTrigger>();
            _activeCorpseLootEntityId = trigger != null ? trigger.EntityId : string.Empty;
            if (trigger != null)
                trigger.RequestInteractionFromUi();
            else if (_corpseLootText != null)
                _corpseLootText.text = "No authoritative corpse loot is available.";
        }

        private void HandleLootReceived(AuthoritativeNetworkClient.LootPayload payload)
        {
            if (string.IsNullOrWhiteSpace(payload.EntityId))
                return;

            if (!string.IsNullOrWhiteSpace(_activeCorpseLootEntityId)
                && !string.Equals(_activeCorpseLootEntityId, payload.EntityId, StringComparison.OrdinalIgnoreCase))
                return;

            _activeCorpseLootEntityId = payload.EntityId;
            if (payload.Credits > 0)
                _context?.ApplyAuthoritativeLootCredits(payload.Credits);
            if (payload.TakenAoid > 0)
                _context?.ApplyAuthoritativeLootItem(payload.TakenAoid, 1);

            _activeCorpseLootItems.Clear();
            if (payload.Items != null)
                _activeCorpseLootItems.AddRange(payload.Items.Where(i => i.Aoid > 0));

            if (_emptyCorpseCleanupCoroutine != null)
            {
                StopCoroutine(_emptyCorpseCleanupCoroutine);
                _emptyCorpseCleanupCoroutine = null;
            }

            if (_corpseLootText != null)
            {
                if (_activeCorpseLootItems.Count > 0)
                    _corpseLootText.text = payload.Credits > 0 ? $"+{payload.Credits} credits" : "Loot";
                else
                    _corpseLootText.text = payload.Credits > 0 ? $"+{payload.Credits} credits" : "No loot remaining.";
            }

            RefreshCorpseLootSlots(_activeCorpseLootItems.Count);
            if (_activeCorpseLootItems.Count == 0)
                _emptyCorpseCleanupCoroutine = StartCoroutine(CleanupEmptyCorpseAfterDelay(payload.EntityId, 3.0f));
        }

        private IEnumerator CleanupEmptyCorpseAfterDelay(string entityId, float delaySeconds)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                yield break;

            yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));

            if (string.Equals(_activeCorpseLootEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                _activeCorpseLootItems.Clear();
                _activeCorpseLootEntityId = string.Empty;
                _corpseLootTarget = null;
                RefreshCorpseLootSlots(0);
                if (_corpseLootWindow?.Root != null)
                    _corpseLootWindow.Root.gameObject.SetActive(false);
            }

            _authoritativeClient?.RemoveRuntimeEntityFromAuthoritativeView(entityId);
            _emptyCorpseCleanupCoroutine = null;
        }

        private void TryTakeCorpseLootSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _activeCorpseLootItems.Count)
                return;

            var item = _activeCorpseLootItems[slotIndex];
            if (_authoritativeClient == null || !_authoritativeClient.IsConnected)
            {
                _context?.PublishStatus("Loot blocked: server unavailable.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeCorpseLootEntityId))
                return;

            _authoritativeClient.RequestTakeLoot(_activeCorpseLootEntityId, item.Aoid, slotIndex);
        }

        private void HandleRuntimeEntityRemoved(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return;

            if (!string.Equals(_activeCorpseLootEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                return;

            _activeCorpseLootItems.Clear();
            _activeCorpseLootEntityId = string.Empty;
            _corpseLootTarget = null;
            RefreshCorpseLootSlots(0);
            if (_corpseLootWindow?.Root != null)
                _corpseLootWindow.Root.gameObject.SetActive(false);
        }

        private void ToggleLookAtWindow()
        {
            var target = _context?.SelectedTarget;
            if (_lookAtWindow?.Root == null || target == null)
            {
                _context?.PublishStatus("Look At failed: no target selected.");
                return;
            }

            if (_lookAtWindow.Root.gameObject.activeSelf && _lookAtTarget == target)
            {
                CloseLookAtWindow();
                return;
            }

            _lookAtTarget = target;
            string targetName = ResolveTargetName(target);
            int level = ResolveTargetLevel(target);
            string levelText = level > 0 ? level.ToString() : "Unknown";
            string description = ResolveTargetDescription(target);
            if (string.IsNullOrWhiteSpace(description))
                description = "Unknown";

            if (_lookAtText != null)
            {
                _lookAtText.text =
                    $"Name: {targetName}\n" +
                    $"Level: {levelText}\n" +
                    $"Description: {description}";
            }

            _lookAtWindow.Root.gameObject.SetActive(true);
            _lookAtWindow.Root.SetAsLastSibling();
        }

        private void CloseLookAtWindow()
        {
            if (_lookAtWindow?.Root == null)
                return;

            _lookAtWindow.Root.gameObject.SetActive(false);
            _lookAtTarget = null;
        }

        private string ResolveTargetDescription(CharacterRuntimeBridge target)
        {
            if (target == null)
                return string.Empty;

            var runtimeEntity = target.GetComponent<AuthoritativeRuntimeEntityIdentity>();
            if (runtimeEntity != null
                && _authoritativeClient != null
                && _authoritativeClient.TryGetRuntimeEntityDescription(runtimeEntity.EntityId, out string authoritativeDescription))
            {
                return authoritativeDescription;
            }

            var identity = target.GetComponent<RuntimeDynelQuestIdentity>();
            if (identity != null && !string.IsNullOrWhiteSpace(identity.Description))
                return identity.Description.Trim();
            if (identity != null && !string.IsNullOrWhiteSpace(identity.DynelName))
                return identity.DynelName.Trim();

            return string.Empty;
        }

        private bool TryRequestAuthoritativeQuestDialog(CharacterRuntimeBridge target)
        {
            if (_authoritativeClient == null || !_authoritativeClient.IsConnected || target == null)
                return false;
            _activeNpcDialogTarget = target;

            int npcId = 0;
            var identity = target.GetComponent<RuntimeDynelQuestIdentity>();
            if (identity != null)
            {
                npcId = identity.PreferredStableId;
                if (npcId <= 0)
                {
                    var ids = identity.GetCandidateIds();
                    if (ids != null)
                    {
                        for (int i = 0; i < ids.Count; i++)
                        {
                            if (ids[i] > 0)
                            {
                                npcId = ids[i];
                                break;
                            }
                        }
                    }
                }
            }

            if (npcId <= 0)
                return false;

            _activeQuestDialogNpcId = npcId;
            _authoritativeClient.RequestQuestDialogStart(npcId);
            _context?.PublishStatus($"Opening dialogue with NPC {npcId}...");
            return true;
        }

        private void HandleQuestDialogReceived(AuthoritativeNetworkClient.QuestDialogPayload payload)
        {
            if (_suppressNextQuestDialogOpen)
            {
                _suppressNextQuestDialogOpen = false;
                if (_npcDialogWindow?.Root != null)
                    _npcDialogWindow.Root.gameObject.SetActive(false);
                if (!string.IsNullOrWhiteSpace(payload.Message))
                    _context?.PublishStatus(payload.Message);
                return;
            }
            _suppressNextQuestDialogOpen = false;

            _activeQuestTradeSessionId = string.Empty;
            _activeQuestTradeOffers.Clear();
            _activeQuestDialogNpcId = payload.NpcId > 0 ? payload.NpcId : _activeQuestDialogNpcId;
            if (_activeNpcDialogTarget == null && payload.NpcId > 0)
                _activeNpcDialogTarget = ResolveBridgeByNpcId(payload.NpcId);
            if (_context != null && payload.RewardGrants != null && payload.RewardGrants.Count > 0)
                _context.ApplyAuthoritativeQuestRewardGrants(payload.RewardGrants);
            _npcDialogSpeakerName = ResolveQuestDialogSpeakerName(payload);
            _npcDialogConversationBody = string.IsNullOrWhiteSpace(payload.Text) ? "..." : payload.Text.Trim();
            string formattedBody = FormatQuestDialogBody(_npcDialogSpeakerName, _npcDialogConversationBody);
            ShowNpcDialog(payload.Title, string.Empty);
            AppendDialogMessage(_npcDialogSpeakerName, formattedBody, false);
            RebuildNpcDialogOptionButtons(payload.Options, payload.CanClose);
            if (!string.IsNullOrWhiteSpace(payload.Message))
                _context?.PublishStatus(payload.Message);
        }

        private string ResolveQuestDialogSpeakerName(AuthoritativeNetworkClient.QuestDialogPayload payload)
        {
            if (_context?.SelectedTarget != null)
            {
                string targetName = ResolveTargetName(_context.SelectedTarget);
                if (!string.IsNullOrWhiteSpace(targetName))
                    return targetName.Trim();
            }

            string title = payload.Title?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(title)
                && !string.Equals(title, "Quest Dialogue", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(title, "Dialogue", StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }

            return "NPC";
        }

        private static string FormatQuestDialogBody(string speakerName, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "...";

            string[] lines = body
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(l => l?.Trim() ?? string.Empty)
                .ToArray();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                bool isSystemLine =
                    line.StartsWith("Quest complete:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Reward:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Conversation ended", StringComparison.OrdinalIgnoreCase);

                if (!isSystemLine)
                {
                    break;
                }
            }

            return string.Join("\n", lines);
        }

        private void AppendDialogMessage(string speaker, string message, bool isPlayer)
        {
            if (_npcDialogText == null)
                return;

            string cleanSpeaker = string.IsNullOrWhiteSpace(speaker) ? (isPlayer ? "You" : "NPC") : speaker.Trim();
            string cleanMessage = string.IsNullOrWhiteSpace(message) ? "..." : message.Trim();
            string speakerColor = isPlayer ? "#7CFF7C" : "#9ED7FF";
            string alignPrefix = isPlayer ? "        " : string.Empty;
            string line = $"{alignPrefix}<color={speakerColor}><b>{cleanSpeaker}:</b></color> {cleanMessage}";

            if (string.IsNullOrWhiteSpace(_npcDialogText.text))
                _npcDialogText.text = line;
            else
                _npcDialogText.text = $"{_npcDialogText.text}\n\n{line}";

            if (_npcDialogChatScrollContent != null)
            {
                var scrollRect = _npcDialogChatScrollContent.GetComponentInParent<ScrollRect>();
                if (scrollRect != null)
                    scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void HandleQuestTradeReceived(AuthoritativeNetworkClient.QuestTradePayload payload)
        {
            _activeQuestTradeSessionId = payload.TradeSessionId ?? string.Empty;
            _activeQuestTradeOffers = payload.Autofill?.ToList()
                ?? new List<AuthoritativeNetworkClient.QuestTradeOffer>();

            string body = payload.Prompt ?? string.Empty;
            if (payload.Requirements != null && payload.Requirements.Count > 0)
            {
                var lines = new List<string>();
                for (int i = 0; i < payload.Requirements.Count; i++)
                {
                    var req = payload.Requirements[i];
                    string itemName = string.IsNullOrWhiteSpace(req.ItemName) ? $"Item {req.ItemAoid}" : req.ItemName.Trim();
                    lines.Add($"{itemName}: need {req.RequiredCount}, you have {req.AvailableCount}");
                }
                if (!string.IsNullOrWhiteSpace(body))
                    body += "\n\n";
                body += string.Join("\n", lines);
            }

            ShowNpcDialog(string.IsNullOrWhiteSpace(payload.Title) ? "Quest Trade" : payload.Title, body);
            RebuildQuestTradeOptionButtons();
            if (!string.IsNullOrWhiteSpace(payload.Message))
                _context?.PublishStatus(payload.Message);
        }

        private void RebuildNpcDialogOptionButtons(IReadOnlyList<AuthoritativeNetworkClient.QuestDialogOption> options, bool canClose)
        {
            if (_npcDialogOptionsRoot == null || _npcDialogWindow?.Root == null)
                return;

            for (int i = _npcDialogOptionsRoot.childCount - 1; i >= 0; i--)
                Destroy(_npcDialogOptionsRoot.GetChild(i).gameObject);

            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    string label = string.IsNullOrWhiteSpace(option.Label)
                        ? (option.IsExit ? "Goodbye" : $"Option {i + 1}")
                        : option.Label.Trim();

                    var button = AOStyleUiFactory.CreateButton(
                        $"DialogOption_{i}",
                        _npcDialogOptionsRoot,
                        label,
                        _npcDialogFont,
                        () => OnNpcDialogOptionClicked(option),
                        0f);

                    var rt = button.transform as RectTransform;
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0f, 1f);
                        rt.anchorMax = new Vector2(1f, 1f);
                        rt.pivot = new Vector2(0.5f, 1f);
                        rt.sizeDelta = new Vector2(0f, 28f);
                    }
                }
            }

            if (canClose)
            {
                var copyButton = AOStyleUiFactory.CreateButton(
                    "DialogCopy",
                    _npcDialogOptionsRoot,
                    "Copy Dialog",
                    _npcDialogFont,
                    OnNpcDialogCopyClicked,
                    0f);
                if (copyButton.transform is RectTransform copyRt)
                {
                    copyRt.anchorMin = new Vector2(0f, 1f);
                    copyRt.anchorMax = new Vector2(1f, 1f);
                    copyRt.pivot = new Vector2(0.5f, 1f);
                    copyRt.sizeDelta = new Vector2(0f, 28f);
                }

                var closeButton = AOStyleUiFactory.CreateButton(
                    "DialogClose",
                    _npcDialogOptionsRoot,
                    "Close",
                    _npcDialogFont,
                    OnNpcDialogCloseClicked,
                    0f);
                var rt = closeButton.transform as RectTransform;
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.sizeDelta = new Vector2(0f, 28f);
                }
            }
        }

        private void OnNpcDialogOptionClicked(AuthoritativeNetworkClient.QuestDialogOption option)
        {
            string selfName = _context?.Character?.Name;
            if (string.IsNullOrWhiteSpace(selfName))
                selfName = "You";
            string label = string.IsNullOrWhiteSpace(option.Label) ? "(selected)" : option.Label.Trim();
            AppendDialogMessage(selfName, label, true);

            if (_authoritativeClient != null && _authoritativeClient.IsConnected && _activeQuestDialogNpcId > 0)
            {
                _authoritativeClient.RequestQuestDialogChoice(_activeQuestDialogNpcId, option.OptionId);
                return;
            }

            if (option.IsExit)
                OnNpcDialogCloseClicked();
        }

        private void OnNpcDialogCopyClicked()
        {
            string text = _npcDialogText != null ? _npcDialogText.text : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return;

            GUIUtility.systemCopyBuffer = text;
            _context?.PublishStatus("Dialog copied to clipboard.");
        }

        private void OnNpcDialogCloseClicked()
        {
            if (_authoritativeClient != null
                && _authoritativeClient.IsConnected
                && !string.IsNullOrWhiteSpace(_activeQuestTradeSessionId))
            {
                _suppressNextQuestDialogOpen = true;
                _authoritativeClient.RequestQuestTradeCancel(_activeQuestTradeSessionId);
                _activeQuestTradeSessionId = string.Empty;
                _activeQuestTradeOffers.Clear();
            }

            if (_authoritativeClient != null && _authoritativeClient.IsConnected && _activeQuestDialogNpcId > 0)
            {
                _suppressNextQuestDialogOpen = true;
                _authoritativeClient.RequestQuestDialogClose(_activeQuestDialogNpcId);
            }

            if (_npcDialogWindow?.Root != null)
                _npcDialogWindow.Root.gameObject.SetActive(false);
        }

        private void RebuildQuestTradeOptionButtons()
        {
            if (_npcDialogOptionsRoot == null)
                return;

            for (int i = _npcDialogOptionsRoot.childCount - 1; i >= 0; i--)
                Destroy(_npcDialogOptionsRoot.GetChild(i).gameObject);

            var accept = AOStyleUiFactory.CreateButton(
                "QuestTradeAccept",
                _npcDialogOptionsRoot,
                "Checkmark (Confirm Trade)",
                _npcDialogFont,
                OnQuestTradeAcceptClicked,
                0f);
            if (accept.transform is RectTransform acceptRt)
            {
                acceptRt.anchorMin = new Vector2(0f, 1f);
                acceptRt.anchorMax = new Vector2(1f, 1f);
                acceptRt.pivot = new Vector2(0.5f, 1f);
                acceptRt.sizeDelta = new Vector2(0f, 28f);
            }

            var cancel = AOStyleUiFactory.CreateButton(
                "QuestTradeCancel",
                _npcDialogOptionsRoot,
                "X (Cancel Trade)",
                _npcDialogFont,
                OnQuestTradeCancelClicked,
                0f);
            if (cancel.transform is RectTransform cancelRt)
            {
                cancelRt.anchorMin = new Vector2(0f, 1f);
                cancelRt.anchorMax = new Vector2(1f, 1f);
                cancelRt.pivot = new Vector2(0.5f, 1f);
                cancelRt.sizeDelta = new Vector2(0f, 28f);
            }
        }

        private void OnQuestTradeAcceptClicked()
        {
            if (_authoritativeClient == null
                || !_authoritativeClient.IsConnected
                || string.IsNullOrWhiteSpace(_activeQuestTradeSessionId))
                return;

            _authoritativeClient.RequestQuestTradeSubmit(_activeQuestTradeSessionId, _activeQuestTradeOffers);
        }

        private void OnQuestTradeCancelClicked()
        {
            if (_authoritativeClient == null
                || !_authoritativeClient.IsConnected
                || string.IsNullOrWhiteSpace(_activeQuestTradeSessionId))
                return;

            _authoritativeClient.RequestQuestTradeCancel(_activeQuestTradeSessionId);
            _activeQuestTradeSessionId = string.Empty;
            _activeQuestTradeOffers.Clear();
            _activeNpcDialogTarget = null;
        }

        private CharacterRuntimeBridge ResolveBridgeByNpcId(int npcId)
        {
            if (npcId <= 0)
                return null;

            var bridges = _cachedRuntimeBridges;
            if (bridges == null || bridges.Length == 0)
                bridges = FindObjectsByType<CharacterRuntimeBridge>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (bridges == null)
                return null;

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null)
                    continue;
                var id = bridge.GetComponent<RuntimeDynelQuestIdentity>();
                if (id == null)
                    continue;
                if (id.PreferredStableId == npcId)
                    return bridge;
            }

            return null;
        }

        private static CharacterRuntimeBridge ResolvePreferredSelfBridgeInScene()
        {
            var bridges = FindObjectsByType<CharacterRuntimeBridge>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (bridges == null || bridges.Length == 0)
                return null;

            static bool IsRuntimeDynelBridge(CharacterRuntimeBridge bridge)
            {
                if (bridge == null || bridge.gameObject == null)
                    return false;
                string name = bridge.gameObject.name ?? string.Empty;
                return name.StartsWith("Runtime_", System.StringComparison.OrdinalIgnoreCase);
            }

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null)
                    continue;
                if (IsRuntimeDynelBridge(bridge))
                    continue;
                if (bridge.Character != null)
                    return bridge;
            }

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null)
                    continue;
                if (IsRuntimeDynelBridge(bridge))
                    continue;
                if (bridge.GetComponent<PrototypeWalkerController>() != null)
                    return bridge;
            }

            return null;
        }

        private void TickNanoCasting()
        {
            if (_context == null)
                return;

            if (_activeNanoCast == null)
            {
                if (_context.TryConsumePendingProgramCastRequest(out var request) && request != null)
                {
                    if (!StartNanoCasting(request))
                        SetNanoCastBarVisible(false);
                }
                else
                    SetNanoCastBarVisible(false);

                return;
            }

            float phaseDuration = _activeNanoCast.Phase == NanoCastPhase.Attack
                ? Mathf.Max(0.05f, _activeNanoCast.AttackSeconds)
                : Mathf.Max(0.05f, _activeNanoCast.RechargeSeconds);
            float elapsed = Mathf.Max(0f, Time.time - _activeNanoCast.PhaseStartedAt);
            float t = Mathf.Clamp01(elapsed / phaseDuration);

            if (_activeNanoCast.Phase == NanoCastPhase.Attack)
            {
                if (WasJumpPressedThisFrame())
                {
                    _context.PublishStatus($"Casting interrupted: {_activeNanoCast.Name}.");
                    SetCombatBarPause(false);
                    StopNanoCasting();
                    return;
                }

                if (Time.time >= _activeNanoCast.NextLoopAt)
                {
                    float loopDuration = TryPlayNanoCastLoopAnimation();
                    if (loopDuration > 0.05f)
                        _activeNanoCast.LoopClipSeconds = loopDuration;
                    _activeNanoCast.NextLoopAt = Time.time + Mathf.Max(0.15f, _activeNanoCast.LoopClipSeconds * 0.9f);
                }

                UpdateNanoCastBar(t, $"{_activeNanoCast.Name}  Cast");
                if (elapsed < phaseDuration)
                    return;

                SetCombatBarPause(false);
                float onCastDuration = TryPlayNanoCastAnimation();
                if (!_context.TryCastUploadedProgram(_activeNanoCast.NanoId))
                {
                    StopNanoCasting();
                    return;
                }

                _activeNanoCast.PostCastQueued = true;
                _activeNanoCast.PostCastAt = Time.time + Mathf.Max(0.05f, onCastDuration);
                _activeNanoCast.Phase = NanoCastPhase.Recharge;
                _activeNanoCast.PhaseStartedAt = Time.time;
                return;
            }

            if (_activeNanoCast.PostCastQueued && Time.time >= _activeNanoCast.PostCastAt)
            {
                TryPlayNanoCastPostAnimation();
                _activeNanoCast.PostCastQueued = false;
            }

            UpdateNanoCastBar(1f - t, $"{_activeNanoCast.Name}  Recharge");
            if (elapsed >= phaseDuration)
                StopNanoCasting();
        }

        private bool StartNanoCasting(PrototypeUiContext.PendingNanoCastRequest request)
        {
            if (_context == null || request == null)
                return false;

            if (!_context.TrySpendNanoForProgram(request.NanoId, out _))
                return false;

            _activeNanoCast = new ActiveNanoCast
            {
                NanoId = request.NanoId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? $"Nano {request.NanoId}" : request.Name,
                AttackSeconds = Mathf.Max(0.05f, request.AttackSeconds),
                RechargeSeconds = Mathf.Max(0.05f, request.RechargeSeconds),
                PhaseStartedAt = Time.time,
                Phase = NanoCastPhase.Attack,
                LoopClipSeconds = 0.6f,
                NextLoopAt = Time.time
            };

            SetCombatBarPause(true);
            SetNanoCastBarVisible(true);
            UpdateNanoCastBar(0f, $"{_activeNanoCast.Name}  Cast");
            _context.PublishStatus($"Casting {_activeNanoCast.Name}...");
            return true;
        }

        private void StopNanoCasting()
        {
            _activeNanoCast = null;
            SetCombatBarPause(false);
            SetNanoCastBarVisible(false);
            ItemTooltipPresenter.HideActive();
        }

        private void SetCombatBarPause(bool paused)
        {
            if (_selfBridge == null)
                return;

            var appearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (appearance != null)
                appearance.SetExternalAttackCyclePaused(paused);
        }

        private float TryPlayNanoCastAnimation()
        {
            if (_selfBridge == null)
                return 0f;

            var appearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (appearance == null)
                return 0f;

            string[] candidates;
            if (_context != null && _context.CharacterBreedId == 4)
            {
                candidates = new[] { "athrox_spell-self_01_01", "atrox_spell-self_01_01", "male_spell-self_01_01", "spell-self_01_01" };
            }
            else if (_context != null && _context.CharacterSex == CharacterRuntimeBridge.CharacterSex.Female)
            {
                candidates = new[] { "female_spell-self_01_01", "male_spell-self_01_01", "spell-self_01_01" };
            }
            else
            {
                candidates = new[] { "male_spell-self_01_01", "female_spell-self_01_01", "spell-self_01_01" };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (appearance.TryPlayOneShotOverlayByName(candidates[i], out float duration)
                    || appearance.TryPlayOneShotClipByName(candidates[i], out duration)
                    || appearance.TryPlayOneShotAction(candidates[i], out duration))
                    return duration;
            }

            return 0f;
        }

        private float TryPlayNanoCastLoopAnimation()
        {
            if (_selfBridge == null)
                return 0f;

            var appearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (appearance == null)
                return 0f;

            string[] candidates;
            if (_context != null && _context.CharacterBreedId == 4)
            {
                candidates = new[] { "athrox_spell-gen_01_01", "atrox_spell-gen_01_01", "male_spell-gen_01_01", "spell-gen_01_01", "_spell-gen_01_01.ani" };
            }
            else if (_context != null && _context.CharacterSex == CharacterRuntimeBridge.CharacterSex.Female)
            {
                candidates = new[] { "female_spell-gen_01_01", "male_spell-gen_01_01", "spell-gen_01_01", "_spell-gen_01_01.ani" };
            }
            else
            {
                candidates = new[] { "male_spell-gen_01_01", "female_spell-gen_01_01", "spell-gen_01_01", "_spell-gen_01_01.ani" };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (appearance.TryPlayOneShotOverlayByName(candidates[i], out float duration)
                    || appearance.TryPlayOneShotClipByName(candidates[i], out duration)
                    || appearance.TryPlayOneShotAction(candidates[i], out duration))
                    return duration;
            }

            return 0f;
        }

        private void TryPlayNanoCastPostAnimation()
        {
            if (_selfBridge == null)
                return;

            var appearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (appearance == null)
                return;

            string[] candidates;
            if (_context != null && _context.CharacterBreedId == 4)
            {
                candidates = new[] { "athrox_spell-self_02_01", "atrox_spell-self_02_01", "male_spell-self_02_01", "spell-self_02_01", "_spell-self_02_01.ani" };
            }
            else if (_context != null && _context.CharacterSex == CharacterRuntimeBridge.CharacterSex.Female)
            {
                candidates = new[] { "female_spell-self_02_01", "male_spell-self_02_01", "spell-self_02_01", "_spell-self_02_01.ani" };
            }
            else
            {
                candidates = new[] { "male_spell-self_02_01", "female_spell-self_02_01", "spell-self_02_01", "_spell-self_02_01.ani" };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (appearance.TryPlayOneShotOverlayByName(candidates[i], out _)
                    || appearance.TryPlayOneShotClipByName(candidates[i], out _)
                    || appearance.TryPlayOneShotAction(candidates[i], out _))
                    return;
            }
        }

        private static bool WasJumpPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

        private bool IsSelfSitting()
        {
            if (_selfBridge == null)
                return false;

            var appearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            return appearance != null && appearance.IsSitting;
        }

        private void BuildTargetMarker(Font font)
        {
            if (_targetOverlayRoot == null || font == null)
                return;

            _targetMarkerRoot = AOStyleUiFactory.CreatePanel("TargetMarker", _targetOverlayRoot, new Color(0f, 0f, 0f, 0f));
            _targetMarkerRoot.pivot = new Vector2(0.5f, 0.5f);
            _targetMarkerRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _targetMarkerRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _targetMarkerRoot.sizeDelta = new Vector2(180f, 34f);
            _targetMarkerRoot.gameObject.SetActive(false);

            CreateCorner("TopLeft", new Vector2(0f, 1f), new Vector2(0f, 1f));
            CreateCorner("TopRight", new Vector2(1f, 1f), new Vector2(1f, 1f));
            CreateCorner("BottomLeft", new Vector2(0f, 0f), new Vector2(0f, 0f));
            CreateCorner("BottomRight", new Vector2(1f, 0f), new Vector2(1f, 0f));

            _targetMarkerText = AOStyleUiFactory.CreateText("TargetName", _targetMarkerRoot, string.Empty, font, 12, TextAnchor.MiddleCenter);
            var textRt = (RectTransform)_targetMarkerText.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 8f);
            textRt.offsetMax = new Vector2(-12f, -8f);
            _targetMarkerText.color = new Color(0.9f, 0.97f, 1f, 1f);
            _targetMarkerText.gameObject.SetActive(false);

            _targetHealthBarRoot = AOStyleUiFactory.CreatePanel("TargetHealthBar", _targetMarkerRoot, new Color(0f, 0f, 0f, 0.45f));
            _targetHealthBarRoot.anchorMin = new Vector2(0.5f, 0f);
            _targetHealthBarRoot.anchorMax = new Vector2(0.5f, 0f);
            _targetHealthBarRoot.pivot = new Vector2(0.5f, 1f);
            _targetHealthBarRoot.anchoredPosition = new Vector2(0f, -8f);
            _targetHealthBarRoot.sizeDelta = new Vector2(88f, 6f);
            _targetHealthBarOutline = AOStyleUiFactory.CreatePanel("TargetHealthBarOutline", _targetMarkerRoot, new Color(0f, 0f, 0f, 0f));
            _targetHealthBarOutline.anchorMin = _targetHealthBarRoot.anchorMin;
            _targetHealthBarOutline.anchorMax = _targetHealthBarRoot.anchorMax;
            _targetHealthBarOutline.pivot = _targetHealthBarRoot.pivot;
            _targetHealthBarOutline.anchoredPosition = _targetHealthBarRoot.anchoredPosition;
            _targetHealthBarOutline.sizeDelta = _targetHealthBarRoot.sizeDelta + new Vector2(4f, 4f);
            CreateRectOutline(_targetHealthBarOutline, new Color(0.45f, 0.8f, 1f, 0.95f), 1f);

            for (int i = 0; i < 30; i++)
            {
                var seg = new GameObject($"HpSeg_{i}", typeof(RectTransform), typeof(Image));
                seg.transform.SetParent(_targetHealthBarRoot, false);
                var segRt = (RectTransform)seg.transform;
                segRt.anchorMin = new Vector2(0f, 0f);
                segRt.anchorMax = new Vector2(0f, 1f);
                segRt.pivot = new Vector2(0f, 0.5f);
                segRt.offsetMin = Vector2.zero;
                segRt.offsetMax = Vector2.zero;
                var img = seg.GetComponent<Image>();
                img.color = new Color(0.8f, 0.8f, 0.2f, 0.95f);
                _targetHealthSegments.Add(img);
            }

            _topTargetBarRoot = AOStyleUiFactory.CreatePanel("TopTargetBar", _targetOverlayRoot, new Color(0f, 0f, 0f, 0.45f));
            _topTargetBarRoot.anchorMin = new Vector2(0.5f, 1f);
            _topTargetBarRoot.anchorMax = new Vector2(0.5f, 1f);
            _topTargetBarRoot.pivot = new Vector2(0.5f, 1f);
            _topTargetBarRoot.anchoredPosition = new Vector2(0f, -8f);
            _topTargetBarRoot.sizeDelta = new Vector2(220f, 28f);
            _topTargetBarRoot.gameObject.SetActive(false);

            _topTargetNameText = AOStyleUiFactory.CreateText("TopTargetName", _topTargetBarRoot, string.Empty, font, 12, TextAnchor.UpperCenter);
            var topNameRt = (RectTransform)_topTargetNameText.transform;
            topNameRt.anchorMin = new Vector2(0f, 0.45f);
            topNameRt.anchorMax = new Vector2(1f, 1f);
            topNameRt.offsetMin = new Vector2(8f, 0f);
            topNameRt.offsetMax = new Vector2(-8f, -2f);
            _topTargetNameText.color = new Color(0.9f, 0.97f, 1f, 1f);

            var topHpRoot = AOStyleUiFactory.CreatePanel("TopTargetHealthBar", _topTargetBarRoot, new Color(0f, 0f, 0f, 0.25f));
            topHpRoot.anchorMin = new Vector2(0.5f, 0f);
            topHpRoot.anchorMax = new Vector2(0.5f, 0f);
            topHpRoot.pivot = new Vector2(0.5f, 0f);
            topHpRoot.anchoredPosition = new Vector2(0f, 3f);
            topHpRoot.sizeDelta = new Vector2(120f, 8f);
            _topTargetHealthBarOutline = AOStyleUiFactory.CreatePanel("TopTargetHealthBarOutline", _topTargetBarRoot, new Color(0f, 0f, 0f, 0f));
            _topTargetHealthBarOutline.anchorMin = topHpRoot.anchorMin;
            _topTargetHealthBarOutline.anchorMax = topHpRoot.anchorMax;
            _topTargetHealthBarOutline.pivot = topHpRoot.pivot;
            _topTargetHealthBarOutline.anchoredPosition = topHpRoot.anchoredPosition;
            _topTargetHealthBarOutline.sizeDelta = topHpRoot.sizeDelta + new Vector2(4f, 4f);
            CreateRectOutline(_topTargetHealthBarOutline, new Color(0.45f, 0.8f, 1f, 0.95f), 1f);

            for (int i = 0; i < 30; i++)
            {
                var seg = new GameObject($"TopHpSeg_{i}", typeof(RectTransform), typeof(Image));
                seg.transform.SetParent(topHpRoot, false);
                var segRt = (RectTransform)seg.transform;
                segRt.anchorMin = new Vector2(0f, 0f);
                segRt.anchorMax = new Vector2(0f, 1f);
                segRt.pivot = new Vector2(0f, 0.5f);
                segRt.offsetMin = Vector2.zero;
                segRt.offsetMax = Vector2.zero;
                var img = seg.GetComponent<Image>();
                img.color = new Color(0.8f, 0.8f, 0.2f, 0.95f);
                _topTargetHealthSegments.Add(img);
            }

            void CreateCorner(string name, Vector2 anchor, Vector2 pivot)
            {
                const float thickness = 2f;
                const float length = 12f;
                var color = new Color(0.72f, 0.9f, 1f, 0.95f);

                var horizontal = new GameObject($"{name}_H", typeof(RectTransform), typeof(Image));
                horizontal.transform.SetParent(_targetMarkerRoot, false);
                var hRt = (RectTransform)horizontal.transform;
                hRt.anchorMin = anchor;
                hRt.anchorMax = anchor;
                hRt.pivot = pivot;
                hRt.sizeDelta = new Vector2(length, thickness);
                hRt.anchoredPosition = Vector2.zero;
                horizontal.GetComponent<Image>().color = color;

                var vertical = new GameObject($"{name}_V", typeof(RectTransform), typeof(Image));
                vertical.transform.SetParent(_targetMarkerRoot, false);
                var vRt = (RectTransform)vertical.transform;
                vRt.anchorMin = anchor;
                vRt.anchorMax = anchor;
                vRt.pivot = pivot;
                vRt.sizeDelta = new Vector2(thickness, length);
                vRt.anchoredPosition = Vector2.zero;
                vertical.GetComponent<Image>().color = color;
            }

            static void CreateRectOutline(RectTransform host, Color color, float thickness)
            {
                CreateEdge(host, "Top", new Vector2(0.5f, 1f), new Vector2(host.sizeDelta.x, thickness), color);
                CreateEdge(host, "Bottom", new Vector2(0.5f, 0f), new Vector2(host.sizeDelta.x, thickness), color);
                CreateEdge(host, "Left", new Vector2(0f, 0.5f), new Vector2(thickness, host.sizeDelta.y), color);
                CreateEdge(host, "Right", new Vector2(1f, 0.5f), new Vector2(thickness, host.sizeDelta.y), color);
            }

            static void CreateEdge(RectTransform host, string name, Vector2 anchor, Vector2 size, Color color)
            {
                var go = new GameObject($"Outline_{name}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(host, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = anchor;
                rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = size;
                go.GetComponent<Image>().color = color;
            }
        }

        private void BuildNanoCastBar(Font font)
        {
            if (_targetOverlayRoot == null || font == null)
                return;

            _nanoCastBarRoot = AOStyleUiFactory.CreatePanel("NanoCastBarRoot", _targetOverlayRoot, new Color(0f, 0f, 0f, 0.55f));
            _nanoCastBarRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _nanoCastBarRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _nanoCastBarRoot.pivot = new Vector2(0.5f, 0.5f);
            _nanoCastBarRoot.anchoredPosition = new Vector2(0f, -138f);
            _nanoCastBarRoot.sizeDelta = new Vector2(180f, 12f);

            var track = AOStyleUiFactory.CreatePanel("NanoCastTrack", _nanoCastBarRoot, new Color(0.12f, 0.08f, 0.18f, 0.85f));
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 1f);
            track.offsetMin = new Vector2(1f, 1f);
            track.offsetMax = new Vector2(-1f, -1f);

            var fillGo = new GameObject("NanoCastFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track, false);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _nanoCastBarFillRect = fillRt;
            _nanoCastBarFill = fillGo.GetComponent<Image>();
            _nanoCastBarFill.color = new Color(0.64f, 0.25f, 0.92f, 0.96f);

            _nanoCastBarText = AOStyleUiFactory.CreateText("NanoCastText", _nanoCastBarRoot, string.Empty, font, 10, TextAnchor.MiddleCenter);
            var textRt = (RectTransform)_nanoCastBarText.transform;
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(0.5f, 0f);
            textRt.anchoredPosition = new Vector2(0f, 3f);
            textRt.sizeDelta = new Vector2(0f, 14f);
            _nanoCastBarText.color = new Color(0.86f, 0.74f, 1f, 0.98f);

            SetNanoCastBarVisible(false);
        }

        private void SetNanoCastBarVisible(bool visible)
        {
            if (_nanoCastBarRoot != null && _nanoCastBarRoot.gameObject.activeSelf != visible)
                _nanoCastBarRoot.gameObject.SetActive(visible);
        }

        private void UpdateNanoCastBar(float fill01, string label)
        {
            if (_nanoCastBarFill == null || _nanoCastBarText == null || _nanoCastBarFillRect == null)
                return;

            float clamped = Mathf.Clamp01(fill01);
            _nanoCastBarFillRect.anchorMax = new Vector2(clamped, 1f);
            _nanoCastBarFill.color = new Color(0.64f, 0.25f, 0.92f, 0.96f);
            _nanoCastBarText.text = label ?? string.Empty;
            SetNanoCastBarVisible(true);
        }

        private void UpdateTargetMarker()
        {
            if (_targetMarkerRoot == null || _targetOverlayRoot == null || Camera.main == null || _context == null)
                return;

            var target = _context.SelectedTarget;
            if (target == null)
            {
                _targetMarkerRoot.gameObject.SetActive(false);
                if (_topTargetBarRoot != null)
                    _topTargetBarRoot.gameObject.SetActive(false);
                return;
            }

            bool positioned = false;
            {
                float yOffset = 2f;
                if (!_worldNameplateYOffsetByBridgeId.TryGetValue(target.GetInstanceID(), out yOffset))
                {
                    var renderer = target.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                        yOffset = Mathf.Max(2.4f, renderer.bounds.extents.y + 1.35f);
                }

                Vector3 worldAnchor = target.transform.position + Vector3.up * yOffset;
                Vector3 screen = Camera.main.WorldToScreenPoint(worldAnchor);
                if (screen.z > 0f
                    && !IsScreenPointBlockedByUiWindow(screen)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(_targetOverlayRoot, screen, null, out var local))
                {
                    _targetMarkerRoot.anchoredPosition = local;
                    float nameWidth = 0f;
                    if (_worldNameplatesByBridgeId.TryGetValue(target.GetInstanceID(), out var worldText) && worldText != null)
                        nameWidth = MeasureWorldNameplateScreenWidth(worldText, Camera.main);
                    if (nameWidth <= 1f)
                        nameWidth = MeasureLabelWidth(ResolveTargetName(target), _targetMarkerText);

                    _targetMarkerRoot.sizeDelta = new Vector2(Mathf.Clamp(nameWidth + 28f, 90f, 560f), 28f);
                    positioned = true;
                }
            }

            if (!positioned)
            {
                _targetMarkerRoot.gameObject.SetActive(false);
                if (_topTargetBarRoot != null)
                    _topTargetBarRoot.gameObject.SetActive(false);
                return;
            }

            UpdateSelectedTargetHealthBar(target);
            _targetMarkerRoot.gameObject.SetActive(true);
            if (_topTargetBarRoot != null)
                _topTargetBarRoot.gameObject.SetActive(true);
        }

        private void UpdateSelectedTargetHealthBar(CharacterRuntimeBridge target)
        {
            if (_targetHealthBarRoot == null || _targetHealthSegments.Count == 0 || _context == null)
                return;

            if (IsDeadCorpseTarget(target))
            {
                _targetHealthBarRoot.gameObject.SetActive(false);
                if (_topTargetBarRoot != null)
                    _topTargetBarRoot.gameObject.SetActive(true);
                if (_topTargetNameText != null)
                    _topTargetNameText.text = ResolveTargetName(target);
                for (int i = 0; i < _topTargetHealthSegments.Count; i++)
                {
                    var seg = _topTargetHealthSegments[i];
                    if (seg != null)
                        seg.gameObject.SetActive(false);
                }
                return;
            }

            _targetHealthBarRoot.gameObject.SetActive(true);

            int selfLevel = Mathf.Max(1, _context.CharacterLevel);
            int targetLevel = ResolveTargetLevel(target);
            Color color = ResolveConColor(selfLevel, targetLevel);

            int currentHp = Mathf.Max(0, ResolveTargetCurrentHealth(target));
            int maxHp = Mathf.Max(1, ResolveTargetMaxHealth(target));
            int totalBars = Mathf.Clamp(8 + Mathf.FloorToInt(maxHp / 5400f), 8, _targetHealthSegments.Count);
            int filledBars = Mathf.Clamp(Mathf.CeilToInt((currentHp / (float)maxHp) * totalBars), 0, totalBars);
            ApplySegmentedHealthBar(_targetHealthBarRoot, _targetHealthSegments, totalBars, filledBars, color);

            if (_topTargetBarRoot != null && _topTargetNameText != null)
            {
                _topTargetNameText.text = ResolveTargetName(target);
                int topBars = Mathf.Clamp(8 + Mathf.FloorToInt(maxHp / 5400f), 8, _topTargetHealthSegments.Count);
                int topFilled = Mathf.Clamp(Mathf.CeilToInt((currentHp / (float)maxHp) * topBars), 0, topBars);
                var topBarRect = _topTargetHealthSegments.Count > 0 ? _topTargetHealthSegments[0].transform.parent as RectTransform : null;
                ApplySegmentedHealthBar(topBarRect, _topTargetHealthSegments, topBars, topFilled, color);
            }
        }

        private static void ApplySegmentedHealthBar(RectTransform barRoot, List<Image> segments, int totalBars, int filledBars, Color color)
        {
            if (barRoot == null || segments == null || segments.Count == 0)
                return;

            float barWidth = barRoot.sizeDelta.x;
            float spacing = 1f;
            float segmentWidth = (barWidth - ((totalBars - 1) * spacing)) / totalBars;
            float x = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                var img = segments[i];
                if (img == null)
                    continue;
                var rt = (RectTransform)img.transform;

                bool active = i < totalBars;
                img.gameObject.SetActive(active);
                if (!active)
                    continue;

                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(segmentWidth, 0f);
                x += segmentWidth + spacing;

                if (i < filledBars)
                {
                    img.color = color;
                }
                else
                {
                    img.color = new Color(0.18f, 0.18f, 0.18f, 0.9f);
                }
            }
        }

        private void UpdateWorldNameplates()
        {
            if (_targetOverlayRoot == null || Camera.main == null)
                return;

            const float maxNameplateDistance = 20f;
            Vector3 cameraPosition = Camera.main.transform.position;
            if (Time.unscaledTime >= _nextRuntimeBridgeCacheRefreshAt || _cachedRuntimeBridges == null || _cachedRuntimeBridges.Length == 0)
            {
                _cachedRuntimeBridges = FindObjectsByType<CharacterRuntimeBridge>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                _nextRuntimeBridgeCacheRefreshAt = Time.unscaledTime + 0.35f;
            }
            var bridges = _cachedRuntimeBridges;
            var liveIds = new HashSet<int>();
            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null || bridge.gameObject == null)
                    continue;

                int id = bridge.GetInstanceID();
                liveIds.Add(id);
                if (!_worldNameplatesByBridgeId.TryGetValue(id, out var text) || text == null)
                {
                    var go = new GameObject($"WorldNameplate_{id}");
                    go.transform.SetParent(bridge.transform, false);
                    text = go.AddComponent<TextMesh>();
                    text.anchor = TextAnchor.MiddleCenter;
                    text.alignment = TextAlignment.Center;
                    text.fontSize = 32;
                    text.characterSize = 0.04f;
                    text.color = new Color(0.92f, 0.96f, 1f, 0.92f);
                    text.text = string.Empty;
                    _worldNameplatesByBridgeId[id] = text;
                }

                float yOffset = 2.1f;
                var renderer = bridge.GetComponentInChildren<Renderer>();
                if (renderer != null)
                    yOffset = Mathf.Max(2.1f, renderer.bounds.max.y - bridge.transform.position.y + 0.3f);
                _worldNameplateYOffsetByBridgeId[id] = yOffset;
                Vector3 worldAnchor = bridge.transform.position + Vector3.up * yOffset;
                text.transform.localPosition = new Vector3(0f, yOffset, 0f);
                Vector3 screen = Camera.main.WorldToScreenPoint(worldAnchor);
                float distance = Vector3.Distance(cameraPosition, worldAnchor);

                bool occluded = false;
                if (distance > 0.01f)
                {
                    Vector3 direction = (worldAnchor - cameraPosition).normalized;
                    if (Physics.Raycast(cameraPosition, direction, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
                        occluded = hit.collider == null || !hit.collider.transform.IsChildOf(bridge.transform);
                }

                Vector2 local = Vector2.zero;
                bool projected = RectTransformUtility.ScreenPointToLocalPointInRectangle(_targetOverlayRoot, screen, null, out local);
                bool isSelf = _selfBridge != null && bridge == _selfBridge;
                bool selfSelected = _context != null && _context.SelectedTarget == _selfBridge;
                bool isDeadCorpse = IsDeadCorpseTarget(bridge);
                bool isDeadSelected = _context != null && _context.SelectedTarget == bridge;
                bool visible = screen.z > 0f
                    && projected
                    && distance <= maxNameplateDistance
                    && !occluded
                    && (!isSelf || selfSelected)
                    && (!isDeadCorpse || isDeadSelected)
                    && !IsScreenPointBlockedByUiWindow(screen);
                text.gameObject.SetActive(visible);
                if (visible)
                {
                    text.text = ResolveTargetName(bridge);
                    Vector3 toCamera = cameraPosition - text.transform.position;
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.0001f)
                        text.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            var stale = _worldNameplatesByBridgeId.Keys.Where(k => !liveIds.Contains(k)).ToList();
            for (int i = 0; i < stale.Count; i++)
            {
                int id = stale[i];
                if (_worldNameplatesByBridgeId.TryGetValue(id, out var text) && text != null)
                    Destroy(text.gameObject);
                _worldNameplatesByBridgeId.Remove(id);
                _worldNameplateYOffsetByBridgeId.Remove(id);
            }
        }

        private bool IsScreenPointBlockedByUiWindow(Vector3 screenPoint)
        {
            return IsBlockingWindowAt(_npcDialogWindow, screenPoint)
                   || IsBlockingWindowAt(_lookAtWindow, screenPoint)
                   || IsBlockingWindowAt(_corpseLootWindow, screenPoint)
                   || IsBlockingWindowAt(_itemBrowserWindow, screenPoint)
                   || IsBlockingWindowAt(_inventoryWindow, screenPoint)
                   || IsBlockingWindowAt(_backpackWindow, screenPoint)
                   || IsBlockingWindowAt(_wearWindow, screenPoint)
                   || IsBlockingWindowAt(_statsWindow, screenPoint)
                   || IsBlockingWindowAt(_skillsWindow, screenPoint)
                   || IsBlockingWindowAt(_programsWindow, screenPoint)
                   || IsBlockingWindowAt(_ncuWindow, screenPoint)
                   || IsBlockingWindowAt(_characterSettingsWindow, screenPoint)
                   || IsBlockingWindowAt(_teleportWindow, screenPoint)
                   || IsBlockingWindowAt(_questEditorWindow, screenPoint)
                   || IsBlockingWindowAt(_f10Window, screenPoint)
                   || IsBlockingWindowAt(_chatWindow, screenPoint);
        }

        private void UpdateCorpseLootHoverIndicator()
        {
            if (_targetOverlayRoot == null || Camera.main == null)
                return;

            if (_corpseLootHoverText == null)
            {
                _corpseLootHoverText = AOStyleUiFactory.CreateText("CorpseLootHoverText", _targetOverlayRoot, string.Empty, _npcDialogFont, 13, TextAnchor.MiddleLeft);
                _corpseLootHoverText.raycastTarget = false;
                _corpseLootHoverText.color = new Color(0.95f, 0.9f, 0.42f, 0.95f);
                _corpseLootHoverText.gameObject.SetActive(false);
            }
            if (_corpseLootHoverIcon == null)
            {
                var iconGo = new GameObject("CorpseLootHoverIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(_targetOverlayRoot, false);
                _corpseLootHoverIcon = iconGo.GetComponent<Image>();
                _corpseLootHoverIcon.raycastTarget = false;
                _corpseLootHoverIcon.preserveAspect = true;
                ((RectTransform)iconGo.transform).sizeDelta = new Vector2(40f, 40f);
                _corpseLootHoverIcon.gameObject.SetActive(false);
            }
            if (_corpseLootHoverBackpackSprite == null && _context != null)
            {
                // Pioneer backpack icon id
                _corpseLootHoverBackpackSprite = _context.GetIconById(151882);
            }

#if ENABLE_INPUT_SYSTEM
            Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            Vector2 mouse = Input.mousePosition;
#endif
            var ray = Camera.main.ScreenPointToRay(mouse);
            if (!Physics.Raycast(ray, out var hit, 5000f, ~0, QueryTriggerInteraction.Collide))
            {
                _corpseLootHoverText.gameObject.SetActive(false);
                if (_corpseLootHoverIcon != null)
                    _corpseLootHoverIcon.gameObject.SetActive(false);
                return;
            }

            var bridge = hit.collider != null ? hit.collider.GetComponentInParent<CharacterRuntimeBridge>() : null;
            if (!IsDeadCorpseTarget(bridge))
            {
                _corpseLootHoverText.gameObject.SetActive(false);
                if (_corpseLootHoverIcon != null)
                    _corpseLootHoverIcon.gameObject.SetActive(false);
                return;
            }

            RectTransform hoverRt = _corpseLootHoverText.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_targetOverlayRoot, mouse + new Vector2(18f, -18f), null, out var local))
            {
                _corpseLootHoverText.gameObject.SetActive(false);
                if (_corpseLootHoverIcon != null)
                    _corpseLootHoverIcon.gameObject.SetActive(false);
                return;
            }

            hoverRt.anchoredPosition = local;
            _corpseLootHoverText.text = "Loot";
            _corpseLootHoverText.gameObject.SetActive(true);
            if (_corpseLootHoverIcon != null)
            {
                var iconRt = _corpseLootHoverIcon.rectTransform;
                iconRt.anchoredPosition = local + new Vector2(0f, -28f);
                _corpseLootHoverIcon.sprite = _corpseLootHoverBackpackSprite;
                _corpseLootHoverIcon.color = _corpseLootHoverBackpackSprite != null
                    ? new Color(1f, 1f, 1f, 0.98f)
                    : new Color(0.95f, 0.9f, 0.42f, 0.95f);
                _corpseLootHoverIcon.gameObject.SetActive(true);
            }
        }

        private static bool IsDeadCorpseTarget(CharacterRuntimeBridge target)
        {
            if (target == null)
                return false;

            var runtimeCombat = target.GetComponent<RuntimeDynelCombatState>();
            return runtimeCombat != null && runtimeCombat.IsDead;
        }

        private static bool IsBlockingWindowAt(AOStyleUiFactory.WindowRefs window, Vector3 screenPoint)
        {
            RectTransform root = window?.Root;
            if (root == null || !root.gameObject.activeInHierarchy)
                return false;
            return RectTransformUtility.RectangleContainsScreenPoint(root, screenPoint, null);
        }

        private static void CreateRectOutline(RectTransform host, Color color, float thickness)
        {
            if (host == null)
                return;

            RectTransform Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 sizeDelta, Vector2 anchored)
            {
                var edge = AOStyleUiFactory.CreatePanel(name, host, color);
                edge.anchorMin = aMin;
                edge.anchorMax = aMax;
                edge.pivot = new Vector2(0.5f, 0.5f);
                edge.sizeDelta = sizeDelta;
                edge.anchoredPosition = anchored;
                var img = edge.GetComponent<Image>();
                if (img != null)
                    img.raycastTarget = false;
                return edge;
            }

            Edge("OutlineTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness), new Vector2(0f, -thickness * 0.5f));
            Edge("OutlineBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness), new Vector2(0f, thickness * 0.5f));
            Edge("OutlineLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f), new Vector2(thickness * 0.5f, 0f));
            Edge("OutlineRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f), new Vector2(-thickness * 0.5f, 0f));
        }

        private int ResolveTargetCurrentHealth(CharacterRuntimeBridge target)
        {
            if (_context != null && target == _selfBridge)
                return _context.GetCurrentHealthValue();

            var stats = target?.Character?.StatsContainer;
            if (stats != null)
                return Mathf.Max(1, stats.GetFinalStat(27));

            var runtimeCombat = target != null ? target.GetComponent<RuntimeDynelCombatState>() : null;
            return runtimeCombat != null ? Mathf.Max(0, runtimeCombat.CurrentHealth) : 1;
        }

        private int ResolveTargetMaxHealth(CharacterRuntimeBridge target)
        {
            if (_context != null && target == _selfBridge)
                return _context.GetMaxHealthValue();

            var stats = target?.Character?.StatsContainer;
            if (stats != null)
                return Mathf.Max(1, stats.GetFinalStat(1));

            var runtimeCombat = target != null ? target.GetComponent<RuntimeDynelCombatState>() : null;
            return runtimeCombat != null ? Mathf.Max(1, runtimeCombat.MaxHealth) : 1;
        }

        private static int ResolveTargetLevel(CharacterRuntimeBridge target)
        {
            int fromCharacter = target?.Character?.Level?.Level ?? 0;
            if (fromCharacter > 0)
                return fromCharacter;

            var identity = target != null ? target.GetComponent<RuntimeDynelQuestIdentity>() : null;
            if (identity != null && identity.Level > 0)
                return identity.Level;

            return 0;
        }

        private static float MeasureLabelWidth(string label, Text referenceText)
        {
            if (string.IsNullOrEmpty(label) || referenceText == null)
                return 56f;

            var settings = referenceText.GetGenerationSettings(new Vector2(4096f, 128f));
            float width = referenceText.cachedTextGeneratorForLayout.GetPreferredWidth(label, settings) / referenceText.pixelsPerUnit;
            if (!float.IsFinite(width) || width <= 0f)
                return Mathf.Max(56f, label.Length * 7f);
            return width;
        }

        private static float MeasureWorldNameplateScreenWidth(TextMesh textMesh, Camera camera)
        {
            if (textMesh == null || camera == null)
                return 0f;

            var renderer = textMesh.GetComponent<Renderer>();
            if (renderer == null)
                return 0f;

            Bounds b = renderer.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents;
            Vector3[] corners =
            {
                c + new Vector3(-e.x, -e.y, -e.z),
                c + new Vector3(-e.x, -e.y,  e.z),
                c + new Vector3(-e.x,  e.y, -e.z),
                c + new Vector3(-e.x,  e.y,  e.z),
                c + new Vector3( e.x, -e.y, -e.z),
                c + new Vector3( e.x, -e.y,  e.z),
                c + new Vector3( e.x,  e.y, -e.z),
                c + new Vector3( e.x,  e.y,  e.z)
            };

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            bool any = false;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 s = camera.WorldToScreenPoint(corners[i]);
                if (s.z <= 0f)
                    continue;
                minX = Mathf.Min(minX, s.x);
                maxX = Mathf.Max(maxX, s.x);
                any = true;
            }

            if (!any)
                return 0f;
            return Mathf.Max(0f, maxX - minX);
        }

        private static Color ResolveConColor(int selfLevel, int targetLevel)
        {
            int diff = targetLevel - selfLevel;
            int absBelow = selfLevel - targetLevel;
            int orangeAbove;
            int greenBelow;

            if (selfLevel <= 50)
            {
                orangeAbove = 5;
                greenBelow = 5;
            }
            else if (selfLevel <= 100)
            {
                orangeAbove = 10;
                greenBelow = 10;
            }
            else if (selfLevel <= 150)
            {
                orangeAbove = 15;
                greenBelow = 15;
            }
            else if (selfLevel <= 200)
            {
                orangeAbove = 20;
                greenBelow = 20;
            }
            else
            {
                orangeAbove = 5;
                greenBelow = 5;
            }

            if (Mathf.Abs(diff) <= 3)
                return new Color(0.95f, 0.88f, 0.22f, 0.96f); // yellow
            if (diff > orangeAbove)
                return new Color(0.9f, 0.18f, 0.18f, 0.96f); // red
            if (diff > 0)
                return new Color(0.93f, 0.53f, 0.16f, 0.96f); // orange
            if (absBelow > greenBelow)
                return new Color(0.55f, 0.55f, 0.55f, 0.96f); // grey
            return new Color(0.28f, 0.82f, 0.34f, 0.96f); // green
        }

        private static string ResolveTargetName(CharacterRuntimeBridge target)
        {
            if (target == null)
                return "Unknown";

            if (!string.IsNullOrWhiteSpace(target.DisplayNameOverride))
                return target.DisplayNameOverride.Trim();

            var character = target.Character;
            if (character != null)
            {
                var prop = character.GetType().GetProperty("Name");
                if (prop != null)
                {
                    var value = prop.GetValue(character) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(target.gameObject?.name) ? "Unknown" : target.gameObject.name;
        }

        private static void ConfigureWindowFrame(
            AOStyleUiFactory.WindowRefs window,
            Vector2 defaultSize,
            Vector2 anchoredPosition,
            Vector2 minSize,
            Vector2? maxSize = null)
        {
            window.Root.pivot = new Vector2(1f, 1f);
            window.Root.sizeDelta = defaultSize;
            window.Root.anchoredPosition = anchoredPosition;

            if (window.ResizeHandle != null)
            {
                window.ResizeHandle.MinSize = minSize;
                window.ResizeHandle.MaxSize = maxSize ?? Vector2.zero;
            }

            var parent = window.Root.parent as RectTransform;
            WindowDragHandle.ClampToParentBounds(window.Root, parent);
        }

        private static float CalcInventoryWindowWidth(int columns)
        {
            return CalcWindowWidth(columns);
        }

        private static float CalcInventoryWindowHeight(int rows)
        {
            return CalcWindowHeight(rows, hasBagLabel: false);
        }

        private static float CalcBackpackWindowWidth(int columns)
        {
            return CalcWindowWidth(columns);
        }

        private static float CalcBackpackWindowHeight(int rows)
        {
            return CalcWindowHeight(rows, hasBagLabel: true);
        }

        private static float CalcWindowWidth(int columns)
        {
            const float cell = 50f;
            const float spacing = 4f;
            // Grid width + scroll/content paddings and window chrome.
            return columns * cell + (columns - 1) * spacing + 40f;
        }

        private static float CalcWindowHeight(int rows, bool hasBagLabel)
        {
            const float cell = 50f;
            const float spacing = 4f;
            // Grid height + content/window chrome (+ backpack label area).
            float baseChrome = hasBagLabel ? 68f : 50f;
            return rows * cell + (rows - 1) * spacing + baseChrome;
        }

        private static void EnsureEventSystem()
        {
            var existing = FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                EnsureCompatibleInputModule(existing.gameObject);
                return;
            }

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            EnsureCompatibleInputModule(es);
        }

        private static void EnsureCompatibleInputModule(GameObject eventSystemGo)
        {
#if ENABLE_INPUT_SYSTEM
            var oldModule = eventSystemGo.GetComponent<StandaloneInputModule>();
            if (oldModule != null)
                Destroy(oldModule);

            var inputModule = eventSystemGo.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
                inputModule = eventSystemGo.AddComponent<InputSystemUIInputModule>();

            // Runtime-created modules can lack bound actions; this makes clicks/navigation work.
            if (inputModule.actionsAsset == null)
                inputModule.AssignDefaultActions();
#else
            if (eventSystemGo.GetComponent<StandaloneInputModule>() == null)
                eventSystemGo.AddComponent<StandaloneInputModule>();
#endif
        }

        private static MonoBehaviour FindCharacterSourceBehaviour()
        {
            var preferred = ResolvePreferredSelfBridgeInScene();
            if (preferred != null)
                return preferred;

            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null)
                    continue;

                if (behaviour.GetType().Name == "CharacterRuntimeBridge")
                    return behaviour;
            }

            return null;
        }

        private static Character ResolveExternalCharacter(MonoBehaviour source)
        {
            if (source == null)
                return null;

            var prop = source.GetType().GetProperty("Character");
            if (prop == null)
                return null;

            return prop.GetValue(source) as Character;
        }

        private static AO.Unity.World.CharacterRuntimeBridge ResolveExternalBridge(MonoBehaviour source)
        {
            return source as AO.Unity.World.CharacterRuntimeBridge;
        }

    }
}




