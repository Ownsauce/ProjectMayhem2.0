using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AO.Core.Characters;
using AO.Core.Stats;
using AO.Client.Characters;
using AO.Client.World;
using AO.Unity.AOStyle;
using AO.Unity.Assets;
using AO.Unity.World;
using AO.Assets.ResourceDatabase;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public partial class PrototypeClientUGUI
    {
        private sealed class CharacterPreviewSlot
        {
            public CharacterRuntimeBridge Bridge;
            public CharacterAppearanceController Appearance;
            public Transform Transform;
            public TextMesh NameLabel;
            public Vector3 BaseScale = Vector3.one;
            public Vector3 TargetLocalPosition;
            public Quaternion TargetRotation = Quaternion.identity;
            public Vector3 TargetLocalScale = Vector3.one;
            public int VisualRevision;
        }

        private sealed class ProfessionPreviewActor
        {
            public int ProfessionId;
            public string ProfessionName;
            public string Description;
            public GameObject Root;
            public bool Loaded;
        }

        [Serializable]
        private sealed class ServerCatalogFile
        {
            public List<ServerCatalogEntry> servers = new();
        }

        [Serializable]
        private sealed class DimensionCatalogFile
        {
            public List<DimensionCatalogEntry> dimensions = new();
        }

        [Serializable]
        private sealed class DimensionCatalogEntry
        {
            public string id = string.Empty;
            public string name = string.Empty;
            public string host = string.Empty;
            public int port;
            public string clientVersion = "18.8.62";
            public string authentication = string.Empty;
            public string seed1 = string.Empty;
            public string seed2 = string.Empty;
            public string privateKey = string.Empty;
            public string publicKey = string.Empty;
        }

        [Serializable]
        private sealed class ServerCatalogEntry
        {
            public string id = string.Empty;
            public string displayName = string.Empty;
            public string host = string.Empty;
            public int port;
            public string version = "18.8.62";
            public string authentication = string.Empty;
            public string loginPrime = string.Empty;
            public string loginPublicSeed = string.Empty;
        }

        private readonly CharacterPreviewSlot[] _characterCreatePreviewSlots = new CharacterPreviewSlot[3];
        private readonly Dictionary<string, string> _defaultHeadByBreedSex = new(StringComparer.Ordinal);
        private int _carouselSelectedIndex = -1;
        private readonly Dictionary<int, ProfessionPreviewActor> _professionPreviewActors = new();
        private readonly List<int> _professionPreviewOrder = new();
        private bool _professionStepActive;
        private bool _createFlowUiActive;
        private int _selectedProfessionPreviewId = -1;
        private RectTransform _connectionSetupRoot;
        private Dropdown _connectionServerDropdown;
        private readonly List<ServerCatalogEntry> _connectionServers = new();
        private InputField _connectionUsernameInput;
        private InputField _connectionPasswordInput;
        private InputField _connectionInstallInput;
        private Text _connectionStatusText;
        private Button _connectionContinueButton;

        private void BuildCharacterSelectionFlow(RectTransform body, RectTransform root, Font font)
        {
            if (font == null)
                return;

            var overlayParent = root != null ? root : body;
            if (overlayParent == null)
                return;

            var overlay = AOStyleUiFactory.CreatePanel("CharacterSelectionOverlay", overlayParent, new Color(0f, 0f, 0f, 1f));
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            overlay.SetAsLastSibling();

            _characterSelectionRoot = overlay;
            _characterSelectionView = overlay.gameObject.AddComponent<CharacterSelectionWindowView>();
            _characterSelectionView.Initialize(
                font,
                _context?.GetProfessionLookup(),
                HandleCharacterPlayRequested,
                HandleCharacterBackRequested,
                ProvideHeadLookupForDraft,
                PreviewDraftBreedSex,
                PreviewDraftBody,
                PreviewDraftHead,
                HandleCreateFlowStateChanged,
                HandleCharacterCreatePreviewDragged,
                HandleHeadEditOffsetDelta,
                HandleHeadEditEulerDelta,
                HandleHeadEditSaveRequested,
                HandleCharacterSelectionChanged,
                HandleProfessionPreviewSelected,
                HandleProfessionStepVisibilityChanged,
                HandleCharacterProfilesChanged);
            var profiles = LoadPersistedCharacterProfiles();
            if (profiles.Count == 0)
                profiles = CreateSeedProfiles();
            _characterSelectionView.SetProfiles(profiles);
            if (profiles.Count > 0 && !string.IsNullOrWhiteSpace(profiles[0].Name))
                _activeProfileName = profiles[0].Name.Trim();
            SetupCharacterCreatePreview();
            SetCharacterFlowActive(true);
            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            bootstrap?.MarkCharacterFlowReady();
            _gameServerSession = GetComponent<AOGameServerSession>();
            if (_gameServerSession == null)
                _gameServerSession = gameObject.AddComponent<AOGameServerSession>();
            _gameServerSession.InventoryChanged -= HandleServerInventoryChanged;
            _gameServerSession.InventoryChanged += HandleServerInventoryChanged;
            _gameServerSession.CharacterStateChanged -= HandleServerCharacterStateChanged;
            _gameServerSession.CharacterStateChanged += HandleServerCharacterStateChanged;
            _characterSelectionRoot.gameObject.SetActive(false);
            BuildConnectionSetup(overlayParent, font);
        }

        private void HandleServerInventoryChanged(InventorySnapshot snapshot)
        {
            _context?.ApplyServerInventory(snapshot);
        }

        private void HandleServerCharacterStateChanged(CharacterStateSnapshot snapshot)
        {
            _context?.ApplyServerCharacterState(snapshot);
        }

        private void BuildConnectionSetup(RectTransform parent, Font font)
        {
            _connectionSetupRoot = AOStyleUiFactory.CreatePanel(
                "ConnectionSetupOverlay", parent, new Color(0.005f, 0.015f, 0.03f, 1f));
            _connectionSetupRoot.anchorMin = Vector2.zero;
            _connectionSetupRoot.anchorMax = Vector2.one;
            _connectionSetupRoot.offsetMin = Vector2.zero;
            _connectionSetupRoot.offsetMax = Vector2.zero;
            _connectionSetupRoot.SetAsLastSibling();

            RectTransform panel = AOStyleUiFactory.CreatePanel(
                "ConnectionSetupPanel", _connectionSetupRoot, new Color(0.06f, 0.1f, 0.15f, 0.98f));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(620f, 520f);

            Text title = AOStyleUiFactory.CreateText("Title", panel,
                "PROJECT MAYHEM", font, 28, TextAnchor.MiddleCenter);
            PlaceSetupControl(title.rectTransform, 32f, 52f);
            title.color = new Color(0.65f, 0.9f, 1f, 1f);

            Text subtitle = AOStyleUiFactory.CreateText("Subtitle", panel,
                "Connection Setup", font, 17, TextAnchor.MiddleCenter);
            PlaceSetupControl(subtitle.rectTransform, 84f, 30f);

            CreateServerDropdown(panel, font, 130f);
            _connectionUsernameInput = CreateSetupInput(panel, font, "Username",
                "AO account username", string.Empty, 196f);
            _connectionPasswordInput = CreateSetupInput(panel, font, "Password",
                "AO account password", string.Empty, 262f);
            _connectionPasswordInput.contentType = InputField.ContentType.Password;
            _connectionPasswordInput.ForceLabelUpdate();

            AOInstallValidation configured = AOInstallConfiguration.GetConfiguredInstall();
            _connectionInstallInput = CreateSetupInput(panel, font, "Anarchy Online installation",
                "Folder containing Anarchy.exe and cd_image",
                configured != null && configured.IsValid ? configured.RootPath : string.Empty, 328f);

            Button detect = AOStyleUiFactory.CreateButton("DetectInstall", panel,
                "Detect", font, DetectAoInstallForSetup, 92f);
            RectTransform detectRt = (RectTransform)detect.transform;
            detectRt.anchorMin = detectRt.anchorMax = new Vector2(1f, 1f);
            detectRt.pivot = new Vector2(1f, 1f);
            detectRt.anchoredPosition = new Vector2(-34f, -373f);
            detectRt.sizeDelta = new Vector2(92f, 28f);

#if UNITY_EDITOR
            Button browse = AOStyleUiFactory.CreateButton("BrowseInstall", panel,
                "Browse...", font, BrowseAoInstallForSetup, 92f);
            RectTransform browseRt = (RectTransform)browse.transform;
            browseRt.anchorMin = browseRt.anchorMax = new Vector2(1f, 1f);
            browseRt.pivot = new Vector2(1f, 1f);
            browseRt.anchoredPosition = new Vector2(-134f, -373f);
            browseRt.sizeDelta = new Vector2(92f, 28f);
#endif

            _connectionStatusText = AOStyleUiFactory.CreateText("ConnectionStatus", panel,
                "Choose the server and local AO installation, then sign in.",
                font, 13, TextAnchor.MiddleLeft);
            PlaceSetupControl(_connectionStatusText.rectTransform, 410f, 38f);
            _connectionStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _connectionContinueButton = AOStyleUiFactory.CreateButton("Continue", panel,
                "CONNECT", font, SubmitConnectionSetup, 180f);
            RectTransform continueRt = (RectTransform)_connectionContinueButton.transform;
            continueRt.anchorMin = continueRt.anchorMax = new Vector2(0.5f, 1f);
            continueRt.pivot = new Vector2(0.5f, 1f);
            continueRt.anchoredPosition = new Vector2(0f, -466f);
            continueRt.sizeDelta = new Vector2(180f, 36f);

            ConfigureConnectionSetupNavigation();
        }

        private void ConfigureConnectionSetupNavigation()
        {
            ConfigureExplicitNavigation(_connectionServerDropdown,
                _connectionContinueButton, _connectionUsernameInput);
            ConfigureExplicitNavigation(_connectionUsernameInput,
                _connectionServerDropdown, _connectionPasswordInput);
            ConfigureExplicitNavigation(_connectionPasswordInput,
                _connectionUsernameInput, _connectionInstallInput);
            ConfigureExplicitNavigation(_connectionInstallInput,
                _connectionPasswordInput, _connectionContinueButton);
            ConfigureExplicitNavigation(_connectionContinueButton,
                _connectionInstallInput, _connectionServerDropdown);
        }

        private static void ConfigureExplicitNavigation(
            Selectable selectable, Selectable previous, Selectable next)
        {
            if (selectable == null)
                return;
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = previous;
            navigation.selectOnLeft = previous;
            navigation.selectOnDown = next;
            navigation.selectOnRight = next;
            selectable.navigation = navigation;
        }

        private void HandleConnectionSetupTabNavigation()
        {
            if (_connectionSetupRoot == null || !_connectionSetupRoot.gameObject.activeInHierarchy)
                return;

#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
                return;
            bool backwards = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
#else
            if (!Input.GetKeyDown(KeyCode.Tab))
                return;
            bool backwards = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif

            Selectable[] order =
            {
                _connectionServerDropdown,
                _connectionUsernameInput,
                _connectionPasswordInput,
                _connectionInstallInput,
                _connectionContinueButton
            };
            GameObject selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            InputField selectedInput = order.OfType<InputField>()
                .FirstOrDefault(input => input != null && input.gameObject == selected);
            if (selectedInput != null && selectedInput.text.IndexOf('\t') >= 0)
                selectedInput.text = selectedInput.text.Replace("\t", string.Empty);
            int index = Array.FindIndex(order, item => item != null && item.gameObject == selected);
            index = index < 0 ? (backwards ? order.Length : -1) : index;
            for (int attempts = 0; attempts < order.Length; attempts++)
            {
                index = (index + (backwards ? -1 : 1) + order.Length) % order.Length;
                Selectable next = order[index];
                if (next == null || !next.IsInteractable())
                    continue;
                next.Select();
                if (next is InputField input)
                    input.ActivateInputField();
                break;
            }
        }

        private static InputField CreateSetupInput(RectTransform panel, Font font,
            string label, string placeholder, string value, float top)
        {
            Text labelText = AOStyleUiFactory.CreateText(label + "Label", panel,
                label, font, 13, TextAnchor.MiddleLeft);
            PlaceSetupControl(labelText.rectTransform, top, 20f);
            InputField input = AOStyleUiFactory.CreateInputField(label + "Input", panel,
                placeholder, font, 540f);
            RectTransform inputRt = (RectTransform)input.transform;
            inputRt.anchorMin = inputRt.anchorMax = new Vector2(0.5f, 1f);
            inputRt.pivot = new Vector2(0.5f, 1f);
            inputRt.anchoredPosition = new Vector2(0f, -(top + 22f));
            inputRt.sizeDelta = new Vector2(540f, 30f);
            input.text = value ?? string.Empty;
            return input;
        }

        private void CreateServerDropdown(RectTransform panel, Font font, float top)
        {
            Text label = AOStyleUiFactory.CreateText("ServerLabel", panel,
                "Server", font, 13, TextAnchor.MiddleLeft);
            PlaceSetupControl(label.rectTransform, top, 20f);

            GameObject dropdownObject = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            dropdownObject.name = "ServerDropdown";
            dropdownObject.transform.SetParent(panel, false);
            _connectionServerDropdown = dropdownObject.GetComponent<Dropdown>();
            RectTransform dropdownRt = (RectTransform)dropdownObject.transform;
            dropdownRt.anchorMin = dropdownRt.anchorMax = new Vector2(0.5f, 1f);
            dropdownRt.pivot = new Vector2(0.5f, 1f);
            dropdownRt.anchoredPosition = new Vector2(0f, -(top + 22f));
            dropdownRt.sizeDelta = new Vector2(540f, 30f);
            foreach (Text text in dropdownObject.GetComponentsInChildren<Text>(true))
            {
                text.font = font;
                text.fontSize = 13;
            }

            LoadServerCatalog();
            _connectionServerDropdown.ClearOptions();
            _connectionServerDropdown.AddOptions(_connectionServers
                .Select(server => string.IsNullOrWhiteSpace(server.displayName)
                    ? server.id : server.displayName)
                .ToList());
            int local = _connectionServers.FindIndex(server =>
                string.Equals(server.id, "aorebirth-local", StringComparison.OrdinalIgnoreCase));
            _connectionServerDropdown.value = Mathf.Max(0, local);
            _connectionServerDropdown.RefreshShownValue();
        }

        private void LoadServerCatalog()
        {
            _connectionServers.Clear();
            string catalogFolder = Path.Combine(Application.streamingAssetsPath, "AOData");
            string serversPath = Path.Combine(catalogFolder, "servers.json");
            string dimensionsPath = Path.Combine(catalogFolder, "dimensions.json");
            try
            {
                ServerCatalogFile catalog = File.Exists(serversPath)
                    ? JsonConvert.DeserializeObject<ServerCatalogFile>(File.ReadAllText(serversPath))
                    : null;
                if (catalog?.servers != null)
                {
                    foreach (ServerCatalogEntry server in catalog.servers)
                        AddServerCatalogEntry(server);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load server catalog '{serversPath}': {exception.Message}");
            }

            // dimensions.json is an alternative catalog. Prefer servers.json when
            // both are present so the same dimensions are not listed twice.
            if (_connectionServers.Count == 0)
            {
                try
                {
                    DimensionCatalogFile catalog = File.Exists(dimensionsPath)
                        ? JsonConvert.DeserializeObject<DimensionCatalogFile>(File.ReadAllText(dimensionsPath))
                        : null;
                    if (catalog?.dimensions != null)
                    {
                        foreach (DimensionCatalogEntry dimension in catalog.dimensions)
                        {
                            if (dimension == null)
                                continue;
                            AddServerCatalogEntry(new ServerCatalogEntry
                            {
                                id = dimension.id,
                                displayName = dimension.name,
                                host = dimension.host,
                                port = dimension.port,
                                version = dimension.clientVersion,
                                authentication = dimension.authentication,
                                loginPrime = string.IsNullOrWhiteSpace(dimension.seed1)
                                    ? dimension.privateKey : dimension.seed1,
                                loginPublicSeed = string.IsNullOrWhiteSpace(dimension.seed2)
                                    ? dimension.publicKey : dimension.seed2
                            });
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not load dimension catalog '{dimensionsPath}': {exception.Message}");
                }
            }

            if (_connectionServers.Count == 0)
            {
                _connectionServers.Add(new ServerCatalogEntry
                {
                    id = "aorebirth-local",
                    displayName = "AORebirth Local",
                    host = "127.0.0.1",
                    port = 7500,
                    version = "18.8.62",
                    authentication = "aorebirth"
                });
            }
        }

        private void AddServerCatalogEntry(ServerCatalogEntry server)
        {
            if (server == null || string.IsNullOrWhiteSpace(server.host)
                || server.port <= 0 || server.port > 65535)
                return;
            if (string.IsNullOrWhiteSpace(server.authentication))
            {
                server.authentication = server.port == 7500 ? "aorebirth"
                    : server.port == 7505 || server.port == 7506 ? "live"
                    : server.port == 7000 ? "project-rubika" : string.Empty;
            }
            _connectionServers.Add(server);
        }

        private static void PlaceSetupControl(RectTransform control, float top, float height)
        {
            control.anchorMin = control.anchorMax = new Vector2(0.5f, 1f);
            control.pivot = new Vector2(0.5f, 1f);
            control.anchoredPosition = new Vector2(0f, -top);
            control.sizeDelta = new Vector2(540f, height);
        }

        private void DetectAoInstallForSetup()
        {
            AOInstallValidation detected = AOInstallConfiguration.GetConfiguredInstall();
            if (detected != null && detected.IsValid)
            {
                _connectionInstallInput.text = detected.RootPath;
                SetConnectionSetupStatus($"AO {detected.ClientVersion} installation found.", true);
            }
            else
            {
                SetConnectionSetupStatus("No valid AO installation was detected. Enter its folder path.", false);
            }
        }

#if UNITY_EDITOR
        private void BrowseAoInstallForSetup()
        {
            string selected = UnityEditor.EditorUtility.OpenFolderPanel(
                "Select Anarchy Online installation", _connectionInstallInput?.text ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(selected))
                _connectionInstallInput.text = selected;
        }
#endif

        private void SubmitConnectionSetup()
        {
            string username = _connectionUsernameInput?.text?.Trim() ?? string.Empty;
            string password = _connectionPasswordInput?.text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                SetConnectionSetupStatus("Enter both username and password.", false);
                return;
            }

            AOInstallValidation install = AOInstallConfiguration.SetInstallPath(
                _connectionInstallInput?.text?.Trim() ?? string.Empty);
            if (install == null || !install.IsValid)
            {
                string error = install?.Errors != null ? string.Join(" ", install.Errors) : "Invalid AO installation.";
                SetConnectionSetupStatus(error, false);
                return;
            }
            _connectionInstallInput.text = install.RootPath;

            int serverIndex = _connectionServerDropdown != null
                ? _connectionServerDropdown.value : -1;
            if (serverIndex < 0 || serverIndex >= _connectionServers.Count)
            {
                SetConnectionSetupStatus("Choose a server.", false);
                return;
            }
            ServerCatalogEntry server = _connectionServers[serverIndex];
            if (!string.Equals(server.authentication, "aorebirth", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(server.loginPrime)
                    || string.IsNullOrWhiteSpace(server.loginPublicSeed)))
            {
                SetConnectionSetupStatus(
                    $"Server '{server.displayName}' is missing its legacy authentication seeds.", false);
                return;
            }
            _gameServerSession.ConfigureConnection(server.displayName, server.host, server.port,
                server.version, server.authentication, server.loginPrime, server.loginPublicSeed);
            _connectionContinueButton.interactable = false;
            SetConnectionSetupStatus("Connecting and authenticating...", true);
            LoadServerCharactersAsync(username, password);
        }

        private void SetConnectionSetupStatus(string message, bool success)
        {
            if (_connectionStatusText == null)
                return;
            _connectionStatusText.text = message ?? string.Empty;
            _connectionStatusText.color = success
                ? new Color(0.55f, 1f, 0.7f, 1f)
                : new Color(1f, 0.62f, 0.48f, 1f);
        }

        private async void LoadServerCharactersAsync(string username, string password)
        {
            // This scene also contains the older Project Mayhem AO.Server JSON
            // client. It must not enforce its strict-disconnected movement mode
            // while AO.Client owns a live external-server session.
            if (_authoritativeClient != null)
                _authoritativeClient.enabled = false;
            EnableExternalServerViewerMovement();

            _characterSelectionView?.SetConnectionStatus("Connecting to AORebirth Local...");
            try
            {
                IReadOnlyList<CharacterSummary> characters =
                    await _gameServerSession.AuthenticateAsync(username, password);
                var profiles = characters.Select(character =>
                    new CharacterSelectionWindowView.CharacterProfile
                    {
                        ServerCharacterId = character.Id,
                        Name = character.Name,
                        Level = Mathf.Max(1, character.Level),
                        BreedId = character.BreedId <= 0 ? 1 : character.BreedId,
                        Sex = ResolveServerCharacterSex(character.BreedId, character.GenderId),
                        ProfessionId = character.ProfessionId <= 0 ? 1 : character.ProfessionId,
                        ProfessionName = ResolveServerProfessionName(character.ProfessionId),
                        BreedLabel = ResolveBreedLabel(
                            character.BreedId,
                            (int)ResolveServerCharacterSex(character.BreedId, character.GenderId)),
                        Height = CharacterSelectionWindowView.BodyHeightPreset.Medium,
                        Weight = CharacterSelectionWindowView.BodyWeightPreset.Medium,
                        StartPlayfieldId = character.PlayfieldId
                    }).ToList();
                var savedProfiles = LoadPersistedCharacterProfiles()
                    .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    .GroupBy(profile => profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var profile in profiles)
                {
                    if (!savedProfiles.TryGetValue(profile.Name?.Trim() ?? string.Empty, out var saved)
                        || saved.BreedId != profile.BreedId
                        || saved.Sex != profile.Sex)
                        continue;
                    profile.HeadMeshKey = saved.HeadMeshKey ?? string.Empty;
                    profile.Height = saved.Height;
                    profile.Weight = saved.Weight;
                }
                foreach (var profile in profiles)
                    EnsureProfileHasHead(profile);
                // An empty character list is still a complete server response. Always
                // replace the view's profiles so characters from the previous account
                // cannot remain visible after switching servers or accounts.
                _characterSelectionView?.SetProfiles(profiles);
                if (profiles.Count > 0)
                {
                    _characterSelectionView?.SetConnectionStatus(
                        $"Loading {profiles.Count} character appearance(s)...");
                    HandleCharacterSelectionChanged(0);
                    await WaitForVisibleCharacterPreviewsAsync();
                }
                else
                {
                    _activeProfileName = string.Empty;
                    HandleCharacterSelectionChanged(-1);
                }
                _characterSelectionView?.SetConnectionStatus(
                    $"Authenticated — {profiles.Count} server character(s) received.");
                if (_connectionSetupRoot != null)
                    _connectionSetupRoot.gameObject.SetActive(false);
                if (_characterSelectionRoot != null)
                    _characterSelectionRoot.gameObject.SetActive(true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetConnectionSetupStatus(exception.Message, false);
                if (_connectionContinueButton != null)
                    _connectionContinueButton.interactable = true;
            }
        }

        private string ResolveServerProfessionName(int professionId)
        {
            IReadOnlyDictionary<int, string> lookup = _context?.GetProfessionLookup();
            return lookup != null && lookup.TryGetValue(professionId, out string name)
                ? name
                : $"Profession {professionId}";
        }

        private static CharacterRuntimeBridge.CharacterSex ResolveServerCharacterSex(int breedId, int genderId)
        {
            // AO protocol stat values differ from CharacterSex: Uni=1, Male=2, Female=3.
            if (breedId == 4 || genderId == 1)
                return CharacterRuntimeBridge.CharacterSex.Uni;
            if (genderId == 3)
                return CharacterRuntimeBridge.CharacterSex.Female;
            return CharacterRuntimeBridge.CharacterSex.Male;
        }

        private List<CharacterSelectionWindowView.CharacterProfile> CreateSeedProfiles()
        {
            var profiles = new List<CharacterSelectionWindowView.CharacterProfile>();
            string professionName = "Prototype";
            int professionId = _context?.CharacterProfessionId ?? 1;
            var professionLookup = _context?.GetProfessionLookup();
            if (professionLookup != null && professionLookup.TryGetValue(professionId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
                professionName = resolved;

            int breedId = _context?.CharacterBreedId ?? 1;
            var sex = _context != null ? _context.CharacterSex : CharacterRuntimeBridge.CharacterSex.Male;
            string breedLabel = breedId switch
            {
                1 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Solitus Female" : "Solitus Male",
                2 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Opifex Female" : "Opifex Male",
                3 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Nanomage Female" : "Nanomage Male",
                4 => "Uni Atrox",
                _ => "Solitus Male"
            };

            profiles.Add(new CharacterSelectionWindowView.CharacterProfile
            {
                Name = _context != null ? _context.GetSelfDisplayName() : "PrototypeCharacter",
                Level = Mathf.Max(1, _context?.CharacterLevel ?? 1),
                BreedId = breedId,
                Sex = sex,
                ProfessionId = professionId,
                ProfessionName = professionName,
                BreedLabel = breedLabel,
                Height = CharacterSelectionWindowView.BodyHeightPreset.Medium,
                Weight = CharacterSelectionWindowView.BodyWeightPreset.Medium,
                HeadMeshKey = _selfBridge != null ? _selfBridge.DebugHeadMeshKey : string.Empty,
                StartPlayfieldId = 0
            });

            return profiles;
        }

        private List<CharacterSelectionWindowView.CharacterProfile> LoadPersistedCharacterProfiles()
        {
            var loaded = new List<CharacterSelectionWindowView.CharacterProfile>();
            if (_clientPrefs?.Characters == null)
                return loaded;

            foreach (var entry in _clientPrefs.Characters)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                loaded.Add(new CharacterSelectionWindowView.CharacterProfile
                {
                    Name = entry.Name.Trim(),
                    Level = Mathf.Max(1, entry.Level),
                    BreedId = entry.BreedId <= 0 ? 1 : entry.BreedId,
                    Sex = Enum.IsDefined(typeof(CharacterRuntimeBridge.CharacterSex), entry.Sex)
                        ? (CharacterRuntimeBridge.CharacterSex)entry.Sex
                        : CharacterRuntimeBridge.CharacterSex.Male,
                    ProfessionId = entry.ProfessionId <= 0 ? 1 : entry.ProfessionId,
                    ProfessionName = string.IsNullOrWhiteSpace(entry.ProfessionName) ? "Prototype" : entry.ProfessionName,
                    BreedLabel = string.IsNullOrWhiteSpace(entry.BreedLabel)
                        ? ResolveBreedLabel(entry.BreedId, entry.Sex)
                        : entry.BreedLabel,
                    Height = Enum.IsDefined(typeof(CharacterSelectionWindowView.BodyHeightPreset), entry.Height)
                        ? (CharacterSelectionWindowView.BodyHeightPreset)entry.Height
                        : CharacterSelectionWindowView.BodyHeightPreset.Medium,
                    Weight = Enum.IsDefined(typeof(CharacterSelectionWindowView.BodyWeightPreset), entry.Weight)
                        ? (CharacterSelectionWindowView.BodyWeightPreset)entry.Weight
                        : CharacterSelectionWindowView.BodyWeightPreset.Medium,
                    HeadMeshKey = entry.HeadMeshKey ?? string.Empty,
                    StartPlayfieldId = entry.StartPlayfieldId <= 0 ? 4604 : entry.StartPlayfieldId
                });
            }

            Debug.Log($"[Prefs] Loaded {loaded.Count} persisted character profile(s) from {GetLocalClientPrefsFilePath()}.");
            return loaded;
        }

        private static string ResolveBreedLabel(int breedId, int sexRaw)
        {
            var sex = Enum.IsDefined(typeof(CharacterRuntimeBridge.CharacterSex), sexRaw)
                ? (CharacterRuntimeBridge.CharacterSex)sexRaw
                : CharacterRuntimeBridge.CharacterSex.Male;

            return breedId switch
            {
                1 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Solitus Female" : "Solitus Male",
                2 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Opifex Female" : "Opifex Male",
                3 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Nanomage Female" : "Nanomage Male",
                4 => "Uni Atrox",
                _ => "Solitus Male"
            };
        }

        private void HandleCharacterPlayRequested(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null)
                return;

            if (!string.IsNullOrWhiteSpace(profile.ServerCharacterId))
            {
                EnterServerWorldAsync(profile);
                return;
            }

            CompleteCharacterPlay(profile);
        }

        private async void EnterServerWorldAsync(CharacterSelectionWindowView.CharacterProfile profile)
        {
            _characterSelectionView?.SetConnectionStatus($"Entering the world as {profile.Name}...");
            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            bootstrap?.BeginExternalWorldEntryLoading();
            // Character selection must never become the visual fallback while world entry
            // continues. Restore it only from the failure path below.
            if (_characterSelectionRoot != null)
                _characterSelectionRoot.gameObject.SetActive(false);
            // Apply the selected identity before any world work begins. The previous order
            // left the bootstrap/default Solitus male visible until the playfield completed.
            ApplyCharacterProfile(profile);
            var selectedAppearance = _selfBridge != null
                ? _selfBridge.GetComponent<CharacterAppearanceController>()
                : null;
            selectedAppearance?.PrewarmCurrentVisual();
            selectedAppearance?.SetBodyVisualVisible(false);
            try
            {
                Debug.Log($"[WorldEntry] Requesting zone handoff for '{profile.Name}'.");
                AOGameServerSession.ZoneSnapshot zone =
                    await _gameServerSession.EnterWorldAsync(profile.ServerCharacterId);
                Debug.Log($"[WorldEntry] Zone bootstrap received: PF {zone.Bootstrap.PlayfieldId}.");
                profile.StartPlayfieldId = zone.Bootstrap.PlayfieldId;
                if (_selfBridge != null)
                    _selfBridge.transform.position = Vector3.zero;
                bool validBootstrapPosition = float.IsFinite(zone.Bootstrap.X)
                    && float.IsFinite(zone.Bootstrap.Y)
                    && float.IsFinite(zone.Bootstrap.Z)
                    && zone.Bootstrap.X >= 0f
                    && zone.Bootstrap.Z >= 0f
                    && zone.Bootstrap.Y > -100f
                    && zone.Bootstrap.Y < 10000f;
                bool loadedPlayfield = bootstrap != null && _selfBridge != null
                    && bootstrap.TransitionToPlayfield(
                        zone.Bootstrap.PlayfieldId,
                        _selfBridge.transform,
                        validBootstrapPosition
                            ? new Vector3(zone.Bootstrap.X, zone.Bootstrap.Y, zone.Bootstrap.Z)
                            : null,
                        explicitYaw: null,
                        preferTeleportDefault: false);
                if (loadedPlayfield)
                {
                    // GLB playfields defer the authoritative spawn until all renderers and
                    // colliders are ready. Do not inspect the temporary origin or enable
                    // outbound movement before that deferred spawn has completed.
                    float playfieldLoadDeadline = Time.realtimeSinceStartup + 60f;
                    while (bootstrap.ActivePlayfieldGlbLoadInProgress
                           && Time.realtimeSinceStartup < playfieldLoadDeadline)
                        await Task.Yield();

                    if (bootstrap.ActivePlayfieldGlbLoadInProgress)
                    {
                        Debug.LogError($"[WorldEntry] PF {zone.Bootstrap.PlayfieldId} static geometry "
                            + "did not finish within 60 seconds; releasing the loading overlay and "
                            + "continuing with the network viewer fallback.");
                        loadedPlayfield = false;
                    }

                    Vector3 sessionOriginWorld = Vector3.zero;
                    if (loadedPlayfield && !validBootstrapPosition)
                    {
                        Vector3 safeAoPosition = bootstrap.ConvertWorldToAo(_selfBridge.transform.position);
                        _gameServerSession.RebaseWorldOrigin(safeAoPosition);
                        sessionOriginWorld = bootstrap.ConvertAoToWorld(safeAoPosition);
                        Debug.LogWarning(
                            $"[AO.Client] Replaced invalid PF {zone.Bootstrap.PlayfieldId} bootstrap position "
                            + $"({zone.Bootstrap.X:F3}, {zone.Bootstrap.Y:F3}, {zone.Bootstrap.Z:F3}) "
                            + $"with safe AO position {safeAoPosition:F3}.");
                    }
                    else if (loadedPlayfield)
                    {
                        Vector3 bootstrapAo = new Vector3(
                            zone.Bootstrap.X, zone.Bootstrap.Y, zone.Bootstrap.Z);
                        Vector3 resolvedAo = bootstrap.ConvertWorldToAo(_selfBridge.transform.position);
                        if (Vector3.Distance(bootstrapAo, resolvedAo) > 0.01f)
                        {
                            _gameServerSession.RebaseWorldOrigin(resolvedAo);
                            sessionOriginWorld = bootstrap.ConvertAoToWorld(resolvedAo);
                            Debug.Log($"[AO.Client] Initial PF {zone.Bootstrap.PlayfieldId} spawn "
                                + $"used teleport default {resolvedAo:F3} instead of bootstrap {bootstrapAo:F3}.");
                        }
                        else
                        {
                            sessionOriginWorld = bootstrap.ConvertAoToWorld(bootstrapAo);
                        }
                    }

                    if (loadedPlayfield && !bootstrap.EnsureCharacterOnPlayfieldSurface(_selfBridge.transform))
                        Debug.LogWarning($"[AO.Client] No walkable surface found near the PF {zone.Bootstrap.PlayfieldId} spawn.");
                    if (loadedPlayfield)
                    {
                        _gameServerSession.SetWorldOriginOffset(sessionOriginWorld);
                        _gameServerSession.SetTemporaryFloorVisible(false);
                    }
                }
                if (!loadedPlayfield)
                {
                    Debug.LogWarning($"[AO.Client] Static playfield load failed for PF {zone.Bootstrap.PlayfieldId}; using network viewer floor.");
                    _gameServerSession.SetWorldOriginOffset(Vector3.zero);
                    _gameServerSession.SetTemporaryFloorVisible(true);
                }
                _gameServerSession.BeginPlayerMovement(_selfBridge != null
                    ? _selfBridge.transform : null);
                EnableExternalServerViewerMovement();
                Debug.Log($"[AO.Client] Entered playfield {zone.Bootstrap.PlayfieldId} at "
                    + $"({zone.Bootstrap.X:F3}, {zone.Bootstrap.Y:F3}, {zone.Bootstrap.Z:F3}); "
                    + $"rendered {zone.Entities.Count} entities and {zone.Objects.Count} objects.");
                bool selectedVisualReady = await WaitForBodyVisualAsync(selectedAppearance);
                if (selectedVisualReady)
                    selectedAppearance?.SetBodyVisualVisible(true);
                else
                    Debug.LogWarning("Selected character body did not become ready; refusing to show the stale bootstrap body.");
                CompleteCharacterPlay(profile, transitionPrototypePlayfield: false);
                bootstrap?.EndExternalWorldEntryLoading();
            }
            catch (Exception exception)
            {
                bootstrap?.EndExternalWorldEntryLoading();
                selectedAppearance?.SetBodyVisualVisible(true);
                Debug.LogException(exception);
                if (_characterSelectionRoot != null)
                {
                    _characterSelectionRoot.gameObject.SetActive(true);
                    _characterSelectionRoot.SetAsLastSibling();
                }
                _characterSelectionView?.SetConnectionStatus(
                    $"World entry failed: {exception.Message}");
            }
        }

        private void EnableExternalServerViewerMovement()
        {
            if (_selfBridge == null)
                return;
            var walker = _selfBridge.GetComponent<PrototypeWalkerController>();
            walker?.SetLocalMovementEnabled(true);
        }

        private void CompleteCharacterPlay(CharacterSelectionWindowView.CharacterProfile profile,
            bool transitionPrototypePlayfield = true)
        {

            ApplyCharacterProfile(profile);
            _activeProfileName = string.IsNullOrWhiteSpace(profile.Name) ? "PrototypeCharacter" : profile.Name.Trim();
            Debug.Log($"[Prefs] Play selected '{_activeProfileName}'. Deferring layout apply until UI is stable.");
            if (transitionPrototypePlayfield)
                TryTransitionToProfilePlayfield(profile);
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
            SetCharacterFlowActive(false);
            StartCoroutine(ApplyWindowLayoutWhenStable(_activeProfileName));
            if (_characterSelectionRoot != null)
                _characterSelectionRoot.gameObject.SetActive(false);
        }

        private async void HandleCharacterBackRequested()
        {
            _characterSelectionView?.SetConnectionStatus("Disconnecting...");
            try
            {
                if (_gameServerSession != null)
                    await _gameServerSession.DisconnectAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Server disconnect while returning to login failed: {exception.Message}");
            }

            _activeProfileName = string.Empty;
            _characterSelectionView?.SetProfiles(Array.Empty<CharacterSelectionWindowView.CharacterProfile>());
            HandleCharacterSelectionChanged(-1);
            if (_connectionPasswordInput != null)
                _connectionPasswordInput.text = string.Empty;
            if (_connectionContinueButton != null)
                _connectionContinueButton.interactable = true;
            SetConnectionSetupStatus("Choose a server and sign in.", true);
            if (_characterSelectionRoot != null)
                _characterSelectionRoot.gameObject.SetActive(false);
            if (_connectionSetupRoot != null)
            {
                _connectionSetupRoot.gameObject.SetActive(true);
                _connectionSetupRoot.SetAsLastSibling();
            }
            if (_connectionUsernameInput != null)
            {
                _connectionUsernameInput.Select();
                _connectionUsernameInput.ActivateInputField();
            }
        }

        private void HandleCharacterProfilesChanged()
        {
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
        }

        private void ApplyCharacterProfile(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null)
                return;

            _context?.RequestCharacterSettings(
                Mathf.Max(1, profile.Level),
                profile.BreedId <= 0 ? 1 : profile.BreedId,
                profile.ProfessionId <= 0 ? 1 : profile.ProfessionId,
                profile.Sex,
                0);

            if (_selfBridge == null)
                return;

            _selfBridge.Sex = profile.Sex;
            _selfBridge.DisplayNameOverride = string.IsNullOrWhiteSpace(profile.Name) ? "PrototypeCharacter" : profile.Name.Trim();
            _selfBridge.DebugHeadMeshKey = profile.HeadMeshKey ?? string.Empty;

            var character = _selfBridge.Character;
            if (character != null)
            {
                character.SetIdentity(
                    profile.BreedId <= 0 ? 1 : profile.BreedId,
                    profile.ProfessionId <= 0 ? 1 : profile.ProfessionId,
                    new Profession { Name = string.IsNullOrWhiteSpace(profile.ProfessionName) ? "Prototype" : profile.ProfessionName });
                character.SetLevelAndRebuildIp(Mathf.Max(1, profile.Level), preserveSpent: false);
            }

            _selfBridge.transform.localScale = ComputeCharacterScale(profile.Height, profile.Weight);
            var selfAppearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (selfAppearance != null)
                selfAppearance.ReloadHeadPreviewOffsets();
        }

        private void TryTransitionToProfilePlayfield(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null || profile.StartPlayfieldId <= 0 || _selfBridge == null)
                return;

            if (_authoritativeClient != null && _authoritativeClient.IsServerAvailable)
            {
                // Server-authoritative spawn resolution:
                // use teleport_defaults/highest-point policy on AO.Server and apply only zone_transition.
                _authoritativeClient.RequestAdminTeleport(
                    profile.StartPlayfieldId,
                    Vector3.zero,
                    targetYaw: 0f,
                    useDefaultSpawn: true);
                return;
            }

            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (bootstrap == null)
                return;

            var persisted = FindPersistedProfileByName(profile.Name);
            Vector3? worldTarget = null;
            float? yawTarget = null;
            if (persisted != null
                && persisted.HasLastLocation
                && persisted.StartPlayfieldId > 0
                && persisted.StartPlayfieldId == profile.StartPlayfieldId)
            {
                worldTarget = new Vector3(persisted.LastWorldX, persisted.LastWorldY, persisted.LastWorldZ);
                yawTarget = persisted.LastYaw;
            }

            if (bootstrap.ActivePlayfieldId == profile.StartPlayfieldId && worldTarget.HasValue)
            {
                _selfBridge.transform.position = ResolveSafeGroundedWorldPosition(worldTarget.Value, _selfBridge.transform.position);
                var e = _selfBridge.transform.eulerAngles;
                e.y = yawTarget ?? e.y;
                _selfBridge.transform.eulerAngles = e;
                var walker = _selfBridge.GetComponent<AO.Unity.World.PrototypeWalkerController>();
                walker?.RecenterCameraBehindCharacter();
                Debug.Log($"[Prefs] Restored same-PF world position for '{profile.Name}' to {_selfBridge.transform.position:F3}.");
                return;
            }

            if (bootstrap.ActivePlayfieldId == profile.StartPlayfieldId)
                return;

            bool transitioned = bootstrap.TransitionToPlayfield(profile.StartPlayfieldId, _selfBridge.transform, null, yawTarget);
            if (transitioned)
                _gameServerSession?.SetTemporaryFloorVisible(false);
            if (!transitioned)
                Debug.LogWarning($"Failed to transition newly selected character to PF {profile.StartPlayfieldId}.");
            else if (worldTarget.HasValue)
                StartCoroutine(RestoreWorldPositionAfterTransition(profile.StartPlayfieldId, worldTarget.Value, yawTarget ?? 0f));
        }

        private IEnumerator RestoreWorldPositionAfterTransition(int playfieldId, Vector3 worldPosition, float yaw)
        {
            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            float timeoutAt = Time.realtimeSinceStartup + 10f;
            while (bootstrap != null
                && (bootstrap.ActivePlayfieldId != playfieldId || bootstrap.ActivePlayfieldGlbLoadInProgress)
                && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (_selfBridge == null)
                yield break;

            _selfBridge.transform.position = ResolveSafeGroundedWorldPosition(worldPosition, _selfBridge.transform.position);
            var e = _selfBridge.transform.eulerAngles;
            e.y = yaw;
            _selfBridge.transform.eulerAngles = e;
            var walker = _selfBridge.GetComponent<AO.Unity.World.PrototypeWalkerController>();
            walker?.RecenterCameraBehindCharacter();

            var rb = _selfBridge.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log($"[Prefs] Restored post-transition world position to {_selfBridge.transform.position:F3} for PF {playfieldId}.");
        }

        private static Vector3 ResolveSafeGroundedWorldPosition(Vector3 candidate, Vector3 fallback)
        {
            bool hasCandidate = TryFindWalkableGround(candidate, out var candidateGrounded);
            bool hasFallback = TryFindWalkableGround(fallback, out var fallbackGrounded);

            if (hasCandidate && hasFallback)
            {
                // Reject implausibly high saved points relative to active fallback spawn.
                if (candidateGrounded.y > fallbackGrounded.y + 12f)
                    return fallbackGrounded + Vector3.up * 0.35f;
                return candidateGrounded + Vector3.up * 0.35f;
            }

            if (hasCandidate)
                return candidateGrounded + Vector3.up * 0.35f;
            if (hasFallback)
                return fallbackGrounded + Vector3.up * 0.35f;
            return fallback;
        }

        private static bool IsLikelyPersistableWorldPosition(Vector3 current)
        {
            if (current.y < -50f || current.y > 10000f)
                return false;
            if (!TryFindWalkableGround(current, out var grounded))
                return false;
            return Mathf.Abs(grounded.y - current.y) <= 5f;
        }

        private static bool TryFindWalkableGround(Vector3 source, out Vector3 grounded)
        {
            grounded = source;
            Vector3 origin = new Vector3(source.x, source.y + 80f, source.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 500f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null)
                    continue;
                if (hit.normal.y < 0.55f)
                    continue;
                grounded = hit.point;
                return true;
            }

            return false;
        }

        private IEnumerator ApplyWindowLayoutWhenStable(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                yield break;

            yield return null;
            ApplyWindowLayoutForCharacter(characterName);
            yield return null;
            ApplyWindowLayoutForCharacter(characterName);
            yield return new WaitForSecondsRealtime(0.2f);
            ApplyWindowLayoutForCharacter(characterName);
            yield return new WaitForSecondsRealtime(0.5f);
            ApplyWindowLayoutForCharacter(characterName);
        }

        private IReadOnlyDictionary<string, string> ProvideHeadLookupForDraft(int breedId, CharacterRuntimeBridge.CharacterSex sex)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { string.Empty, "None" }
            };

            string root = Path.Combine(Application.streamingAssetsPath, "AOData", "ItemMeshes");
            string breedToken = breedId switch
            {
                1 => "solitus",
                2 => "opifex",
                3 => "nanomage",
                4 => "athrox",
                _ => string.Empty
            };

            string sexPrefixToken = sex switch
            {
                CharacterRuntimeBridge.CharacterSex.Female => "female",
                CharacterRuntimeBridge.CharacterSex.Uni => string.Empty,
                _ => "male"
            };

            string expectedPrefix = string.IsNullOrWhiteSpace(breedToken)
                ? "head_"
                : $"head_{breedToken}{sexPrefixToken}";

            foreach (var file in Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.glb")
                : Enumerable.Empty<string>())
            {
                string meshKey = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(meshKey))
                    continue;
                if (!meshKey.StartsWith("head_", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Use strict prefix matching so "male" does not match inside "female".
                if (!meshKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!result.ContainsKey(meshKey))
                    result[meshKey] = meshKey;
            }

            try
            {
                AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
                if (install != null && install.IsValid)
                {
                    string databasePrefix = breedId switch
                    {
                        1 => "head_solitus" + sexPrefixToken,
                        2 => "head_opifex" + sexPrefixToken,
                        3 => "head_nano" + (sex == CharacterRuntimeBridge.CharacterSex.Female ? "female" : "male"),
                        4 => "head_atrox",
                        _ => string.Empty
                    };
                    using (var database = new AOResourceDatabase(install.RootPath))
                    {
                        AOResourceCatalog catalog = AOResourceCatalog.Load(database);
                        foreach (var pair in catalog.GetResources(AOResourceTypes.Mesh))
                        {
                            string meshKey = Path.GetFileNameWithoutExtension(pair.Value ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(databasePrefix)
                                || !meshKey.StartsWith(databasePrefix, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!result.ContainsKey(meshKey))
                                result[meshKey] = meshKey;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not enumerate AO head meshes for the character picker: {exception.Message}");
            }

            return result;
        }

        private void PreviewDraftBreedSex(int breedId, CharacterRuntimeBridge.CharacterSex sex)
        {
            if (_characterCreatePreviewBridge == null)
                return;

            var previewCharacter = _characterCreatePreviewBridge.Character;
            if (previewCharacter != null)
            {
                int professionId = _context != null ? Mathf.Max(1, _context.CharacterProfessionId) : 1;
                string professionName = "Prototype";
                var lookup = _context?.GetProfessionLookup();
                if (lookup != null && lookup.TryGetValue(professionId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
                    professionName = resolved;

                previewCharacter.SetIdentity(
                    breedId <= 0 ? 1 : breedId,
                    professionId,
                    new Profession { Name = professionName });
                previewCharacter.SetLevelAndRebuildIp(1, preserveSpent: false);
            }

            _characterCreatePreviewBridge.Sex = sex;
            // Always switch to the first valid head for this breed/sex so preview never
            // keeps a mismatched head from a previous selection.
            string defaultHeadMeshKey = string.Empty;
            var headLookup = ProvideHeadLookupForDraft(breedId, sex);
            if (headLookup != null)
            {
                foreach (var pair in headLookup)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;
                    defaultHeadMeshKey = pair.Key;
                    break;
                }
            }
            _characterCreatePreviewBridge.DebugHeadMeshKey = defaultHeadMeshKey;
            _characterCreatePreviewBridge.DisplayNameOverride = "Preview";
            _characterCreatePreviewAppearance?.PrewarmCurrentVisual();
            if (_characterCreatePreviewSlots[1]?.NameLabel != null)
                _characterCreatePreviewSlots[1].NameLabel.text = "Preview";
        }

        private void PreviewDraftBody(
            CharacterSelectionWindowView.BodyHeightPreset height,
            CharacterSelectionWindowView.BodyWeightPreset weight)
        {
            if (_characterCreatePreviewBridge == null)
                return;
            _characterCreatePreviewBridge.transform.localScale = ComputeCharacterScale(height, weight);
            _characterCreatePreviewAppearance?.PrewarmCurrentVisual();
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void PreviewDraftHead(string headMeshKey)
        {
            if (_characterCreatePreviewBridge == null)
                return;
            _characterCreatePreviewBridge.DebugHeadMeshKey = headMeshKey ?? string.Empty;
            _characterCreatePreviewAppearance?.PrewarmCurrentVisual();
        }

        private void SetupCharacterCreatePreview()
        {
            CleanupCharacterCreatePreview();
            _characterCreatePreviewOrbitYaw = 165f;

            var previewRoot = new GameObject("CharacterCreatePreviewRoot");
            previewRoot.transform.position = new Vector3(5000f, -500f, 5000f);
            _characterCreatePreviewRoot = previewRoot;

            string professionName = "Prototype";
            int professionId = _context != null ? Mathf.Max(1, _context.CharacterProfessionId) : 1;
            var lookup = _context?.GetProfessionLookup();
            if (lookup != null && lookup.TryGetValue(professionId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
                professionName = resolved;

            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var previewActor = new GameObject($"CharacterCreatePreviewActor_{i}");
                previewActor.transform.SetParent(previewRoot.transform, false);
                float x = (i - 1) * 1.8f;
                float z = i == 1 ? 0f : 0.55f;
                previewActor.transform.localPosition = new Vector3(x, 0f, z);
                previewActor.transform.localRotation = Quaternion.identity;
                previewActor.transform.localScale = i == 1 ? Vector3.one : new Vector3(0.9f, 0.9f, 0.9f);

                var bridge = previewActor.AddComponent<CharacterRuntimeBridge>();
                var appearance = previewActor.AddComponent<CharacterAppearanceController>();
                appearance.ConfigureCreateVisualIfMissing(true);
                appearance.ConfigureDisableCharacterMeshAnimations(true);

                var character = new Character($"CharacterPreview_{i}", new Profession { Name = professionName }, 0, breedId: 1, professionId: professionId);
                character.SetLevelAndRebuildIp(1, preserveSpent: false);
                bridge.SetCharacter(character);
                bridge.Sex = CharacterRuntimeBridge.CharacterSex.Male;
                bridge.DebugHeadMeshKey = string.Empty;
                bridge.DisplayNameOverride = string.Empty;
                appearance.PrewarmCurrentVisual();

                var labelGo = new GameObject($"PreviewName_{i}");
                labelGo.transform.SetParent(previewActor.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                var textMesh = labelGo.AddComponent<TextMesh>();
                textMesh.text = string.Empty;
                textMesh.fontSize = 48;
                textMesh.characterSize = 0.03f;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.color = new Color(0.92f, 0.97f, 1f, 0.95f);
                textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var labelRenderer = textMesh.GetComponent<MeshRenderer>();
                if (labelRenderer != null)
                    labelRenderer.material = textMesh.font.material;

                _characterCreatePreviewSlots[i] = new CharacterPreviewSlot
                {
                    Bridge = bridge,
                    Appearance = appearance,
                    Transform = previewActor.transform,
                    NameLabel = textMesh
                };
            }

            _characterCreatePreviewBridge = _characterCreatePreviewSlots[1]?.Bridge;
            _characterCreatePreviewAppearance = _characterCreatePreviewSlots[1]?.Appearance;

            _characterCreatePreviewTexture = new RenderTexture(1536, 1536, 24, RenderTextureFormat.ARGB32);
            _characterCreatePreviewTexture.name = "CharacterCreatePreviewRT";

            var camGo = new GameObject("CharacterCreatePreviewCamera");
            camGo.transform.SetParent(previewRoot.transform, false);
            _characterCreatePreviewCamera = camGo.AddComponent<Camera>();
            _characterCreatePreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            _characterCreatePreviewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _characterCreatePreviewCamera.fieldOfView = 28f;
            _characterCreatePreviewCamera.nearClipPlane = 0.03f;
            _characterCreatePreviewCamera.farClipPlane = 100f;
            _characterCreatePreviewCamera.targetTexture = _characterCreatePreviewTexture;
            _characterCreatePreviewCamera.enabled = true;
            UpdateCharacterCreatePreviewCameraFraming();

            _characterSelectionView?.SetPreviewTexture(_characterCreatePreviewTexture);
            PreviewDraftBreedSex(1, CharacterRuntimeBridge.CharacterSex.Male);
            PreviewDraftBody(
                CharacterSelectionWindowView.BodyHeightPreset.Medium,
                CharacterSelectionWindowView.BodyWeightPreset.Medium);
            PreviewDraftHead(string.Empty);
            HandleCharacterSelectionChanged(0);
            _ = EnsureProfessionPreviewActorsLoadedAsync();
        }

        private void HandleCharacterSelectionChanged(int selectedIndex)
        {
            if (_characterSelectionView == null)
                return;

            var profiles = _characterSelectionView.GetProfiles();
            if (profiles == null || profiles.Count == 0)
            {
                _carouselSelectedIndex = -1;
                for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                {
                    var slot = _characterCreatePreviewSlots[i];
                    if (slot?.Transform != null)
                        slot.Transform.gameObject.SetActive(false);
                }
                return;
            }

            int center = Mathf.Clamp(selectedIndex, 0, profiles.Count - 1);
            int delta = _carouselSelectedIndex >= 0 ? center - _carouselSelectedIndex : 0;
            if (delta == 1)
            {
                CharacterPreviewSlot recycled = _characterCreatePreviewSlots[0];
                _characterCreatePreviewSlots[0] = _characterCreatePreviewSlots[1];
                _characterCreatePreviewSlots[1] = _characterCreatePreviewSlots[2];
                _characterCreatePreviewSlots[2] = recycled;
            }
            else if (delta == -1)
            {
                CharacterPreviewSlot recycled = _characterCreatePreviewSlots[2];
                _characterCreatePreviewSlots[2] = _characterCreatePreviewSlots[1];
                _characterCreatePreviewSlots[1] = _characterCreatePreviewSlots[0];
                _characterCreatePreviewSlots[0] = recycled;
            }
            _characterCreatePreviewBridge = _characterCreatePreviewSlots[1]?.Bridge;
            _characterCreatePreviewAppearance = _characterCreatePreviewSlots[1]?.Appearance;
            int[] indices = { center - 1, center, center + 1 };
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform == null)
                    continue;

                int profileIndex = indices[i];
                bool hasProfile = profileIndex >= 0 && profileIndex < profiles.Count;
                slot.Transform.gameObject.SetActive(hasProfile);
                if (!hasProfile)
                    continue;

                var profile = profiles[profileIndex];
                EnsureProfileHasHead(profile);
                bool actorAlreadyMatches = string.Equals(
                    slot.Bridge.DisplayNameOverride, profile.Name, StringComparison.Ordinal);
                if (!actorAlreadyMatches)
                    ApplyProfileToPreviewSlot(slot, profile);
            }

            UpdateCharacterSelectionCarouselLayout(_carouselSelectedIndex < 0 || Math.Abs(delta) > 1);
            _carouselSelectedIndex = center;
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void EnsureProfileHasHead(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null || !string.IsNullOrWhiteSpace(profile.HeadMeshKey))
                return;
            string cacheKey = $"{profile.BreedId}:{(int)profile.Sex}";
            if (!_defaultHeadByBreedSex.TryGetValue(cacheKey, out string headKey))
            {
                headKey = string.Empty;
                IReadOnlyDictionary<string, string> heads = ProvideHeadLookupForDraft(profile.BreedId, profile.Sex);
                if (heads != null)
                {
                    foreach (string candidate in heads.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(candidate))
                        {
                            headKey = candidate;
                            break;
                        }
                    }
                }
                _defaultHeadByBreedSex[cacheKey] = headKey;
            }
            profile.HeadMeshKey = headKey ?? string.Empty;
        }

        private void ApplyProfileToPreviewSlot(CharacterPreviewSlot slot, CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (slot == null || slot.Bridge == null || profile == null)
                return;

            var previewCharacter = slot.Bridge.Character;
            if (previewCharacter != null)
            {
                previewCharacter.SetIdentity(
                    profile.BreedId <= 0 ? 1 : profile.BreedId,
                    profile.ProfessionId <= 0 ? 1 : profile.ProfessionId,
                    new Profession { Name = string.IsNullOrWhiteSpace(profile.ProfessionName) ? "Prototype" : profile.ProfessionName });
                previewCharacter.SetLevelAndRebuildIp(Mathf.Max(1, profile.Level), preserveSpent: false);
            }

            slot.Bridge.Sex = profile.Sex;
            slot.Bridge.DebugHeadMeshKey = profile.HeadMeshKey ?? string.Empty;
            slot.Bridge.DisplayNameOverride = profile.Name ?? string.Empty;
            slot.BaseScale = ComputeCharacterScale(profile.Height, profile.Weight);
            int visualRevision = ++slot.VisualRevision;
            if (slot.NameLabel != null)
                slot.NameLabel.text = profile.Name ?? string.Empty;
            slot.Appearance?.PrewarmCurrentVisual();
            if (slot.Appearance != null
                && (!slot.Appearance.IsCurrentBodyVisualReady
                    || !slot.Appearance.IsCurrentHeadVisualReady))
            {
                slot.Appearance.SetBodyVisualVisible(false);
                _ = RevealPreviewWhenReadyAsync(slot, visualRevision);
            }
        }

        private static async Task RevealPreviewWhenReadyAsync(CharacterPreviewSlot slot, int visualRevision)
        {
            if (slot?.Appearance == null)
                return;
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((!slot.Appearance.IsCurrentBodyVisualReady
                    || !slot.Appearance.IsCurrentHeadVisualReady)
                   && Time.realtimeSinceStartup < deadline)
                await Task.Yield();
            if (slot.VisualRevision == visualRevision
                && slot.Transform != null && slot.Transform.gameObject.activeSelf
                && slot.Appearance.IsCurrentBodyVisualReady)
                slot.Appearance.SetBodyVisualVisible(true);
        }

        private async Task WaitForVisibleCharacterPreviewsAsync()
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool ready = true;
                for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                {
                    CharacterPreviewSlot slot = _characterCreatePreviewSlots[i];
                    if (slot?.Transform == null || !slot.Transform.gameObject.activeSelf)
                        continue;
                    if (slot.Appearance == null
                        || !slot.Appearance.IsCurrentBodyVisualReady
                        || !slot.Appearance.IsCurrentHeadVisualReady)
                    {
                        ready = false;
                        break;
                    }
                }
                if (ready)
                    return;
                await Task.Yield();
            }
            Debug.LogWarning("Character preview preloading timed out; keeping available previews.");
        }

        private static async Task<bool> WaitForBodyVisualAsync(CharacterAppearanceController appearance)
        {
            if (appearance == null)
                return false;
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((!appearance.IsCurrentBodyVisualReady || !appearance.IsCurrentHeadVisualReady)
                   && Time.realtimeSinceStartup < deadline)
                await Task.Yield();
            return appearance.IsCurrentBodyVisualReady;
        }

        private void UpdateCharacterSelectionCarouselLayout(bool immediate = false)
        {
            if (_characterCreatePreviewSlots == null || _characterCreatePreviewSlots.Length == 0)
                return;

            // Match the profession carousel style: selected in front, neighbors curved back.
            // Slots are ordered: 0=left neighbor, 1=selected, 2=right neighbor.
            const float radius = 2.2f;
            const float zCenter = 1.45f;
            const float stepDeg = 42f;
            int[] relative = { -1, 0, 1 };

            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform == null || !slot.Transform.gameObject.activeSelf || slot.Bridge == null)
                    continue;

                int rel = relative[i];
                float angleDeg = rel * stepDeg;
                float rad = angleDeg * Mathf.Deg2Rad;
                float x = Mathf.Sin(rad) * radius;
                float z = zCenter + Mathf.Cos(rad) * radius;
                slot.TargetLocalPosition = new Vector3(x, 0f, z);

                // Face camera on yaw only so characters are always readable.
                if (_characterCreatePreviewCamera != null)
                {
                    Vector3 targetWorldPosition = slot.Transform.parent != null
                        ? slot.Transform.parent.TransformPoint(slot.TargetLocalPosition)
                        : slot.TargetLocalPosition;
                    Vector3 toCamera = _characterCreatePreviewCamera.transform.position - targetWorldPosition;
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.0001f)
                        slot.TargetRotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }

                // Keep selected character emphasized.
                float emphasis = rel == 0 ? 1.08f : 0.82f;
                slot.TargetLocalScale = slot.BaseScale * emphasis;
                if (immediate)
                {
                    slot.Transform.localPosition = slot.TargetLocalPosition;
                    slot.Transform.rotation = slot.TargetRotation;
                    slot.Transform.localScale = slot.TargetLocalScale;
                }
                if (slot.NameLabel != null)
                {
                    slot.NameLabel.gameObject.SetActive(rel == 0);
                    slot.NameLabel.transform.localPosition = rel == 0 ? new Vector3(0f, 1.20f, 0f) : new Vector3(0f, 2.2f, 0f);
                }
            }
        }

        private void TickCharacterSelectionCarousel()
        {
            if (_createFlowUiActive)
                return;
            float blend = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                CharacterPreviewSlot slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform == null || !slot.Transform.gameObject.activeSelf)
                    continue;
                slot.Transform.localPosition = Vector3.Lerp(
                    slot.Transform.localPosition, slot.TargetLocalPosition, blend);
                slot.Transform.rotation = Quaternion.Slerp(
                    slot.Transform.rotation, slot.TargetRotation, blend);
                slot.Transform.localScale = Vector3.Lerp(
                    slot.Transform.localScale, slot.TargetLocalScale, blend);
            }
            // The selected actor itself moves from a neighboring carousel slot. Keep
            // the camera framed on it during that interpolation instead of its old slot.
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void HandleCreateFlowStateChanged(bool inCreateFlow)
        {
            _createFlowUiActive = inCreateFlow;
            if (!inCreateFlow)
            {
                SetProfessionActorsVisible(false);
                HandleCharacterSelectionChanged(0);
                return;
            }

            if (_characterCreatePreviewAppearance != null)
                _characterCreatePreviewAppearance.PrewarmCurrentVisual();
            if (_characterSelectionView != null && _characterCreatePreviewTexture != null)
                _characterSelectionView.SetPreviewTexture(_characterCreatePreviewTexture);
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform != null)
                    slot.Transform.gameObject.SetActive(i == 1);
            }
            SetProfessionActorsVisible(_professionStepActive);
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void HandleProfessionStepVisibilityChanged(bool visible)
        {
            _professionStepActive = visible;
            if (visible)
                _ = EnsureProfessionPreviewActorsLoadedAsync();
            SetProfessionActorsVisible(visible);
            if (visible)
            {
                for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                {
                    var slot = _characterCreatePreviewSlots[i];
                    if (slot?.Transform != null)
                        slot.Transform.gameObject.SetActive(false);
                }
            }
            else
            {
                // If we're still inside create flow, keep only the draft preview model active.
                // Do not restore character-selection previews here, or we overwrite visuals/draft continuity.
                if (_createFlowUiActive)
                {
                    for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                    {
                        var slot = _characterCreatePreviewSlots[i];
                        if (slot?.Transform != null)
                            slot.Transform.gameObject.SetActive(i == 1);
                    }
                }
                else
                {
                    HandleCharacterSelectionChanged(0);
                }
            }

            UpdateCharacterCreatePreviewCameraFraming();
            if (visible)
            {
                // Apply layout/facing again after camera framing is set so initial entry
                // doesn't show sideways models until next selection.
                UpdateProfessionPreviewCarouselLayout();
                RefreshPreviewNameLabelFacing();
            }
        }

        private void HandleProfessionPreviewSelected(int professionId, string professionName, string description)
        {
            _selectedProfessionPreviewId = professionId;
            if (_professionPreviewActors.TryGetValue(professionId, out var actor))
            {
                actor.ProfessionName = professionName ?? actor.ProfessionName;
                actor.Description = description ?? actor.Description;
            }

            if (_professionStepActive)
            {
                UpdateProfessionPreviewCarouselLayout();
                FocusSelectedProfessionPreview();
            }
        }

        private void HandleCharacterCreatePreviewDragged(Vector2 delta)
        {
            if (!_characterFlowActive || _characterCreatePreviewCamera == null)
                return;

            _characterCreatePreviewOrbitYaw -= delta.x * 0.18f;
            if (_characterCreatePreviewOrbitYaw < -180f)
                _characterCreatePreviewOrbitYaw += 360f;
            else if (_characterCreatePreviewOrbitYaw > 180f)
                _characterCreatePreviewOrbitYaw -= 360f;

            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void HandleHeadEditOffsetDelta(Vector3 delta)
        {
            if (_characterCreatePreviewAppearance == null)
                return;
            _characterCreatePreviewAppearance.AdjustCurrentDebugHeadMeshOffset(delta);
        }

        private void HandleHeadEditEulerDelta(Vector3 delta)
        {
            if (_characterCreatePreviewAppearance == null)
                return;
            _characterCreatePreviewAppearance.AdjustCurrentDebugHeadMeshEulerOffset(delta);
        }

        private bool HandleHeadEditSaveRequested()
        {
            if (_characterCreatePreviewAppearance == null)
                return false;
            bool ok = _characterCreatePreviewAppearance.SaveHeadPreviewOffsets();
            if (!ok)
                return false;

            var selfAppearance = _selfBridge != null ? _selfBridge.GetComponent<CharacterAppearanceController>() : null;
            if (selfAppearance != null)
                selfAppearance.ReloadHeadPreviewOffsets();
            _characterCreatePreviewAppearance.ReloadHeadPreviewOffsets();
            return true;
        }

        private void UpdateCharacterCreatePreviewCameraFraming()
        {
            if (_characterCreatePreviewCamera == null)
                return;

            Vector3 rootPos;
            Vector3 lookTarget;
            if (_professionStepActive && TryGetSelectedProfessionFocusPoint(out var focusPoint))
            {
                rootPos = focusPoint;
                lookTarget = rootPos + Vector3.up * 1.10f;
            }
            else
            {
                if (_characterCreatePreviewBridge == null)
                    return;
                rootPos = _characterCreatePreviewBridge.transform.position;
                lookTarget = rootPos + Vector3.up * 0.90f;
            }

            Quaternion orbit = Quaternion.Euler(8f, _characterCreatePreviewOrbitYaw, 0f);
            Vector3 offset = orbit * new Vector3(0f, 0.00f, -6.25f);
            _characterCreatePreviewCamera.transform.position = lookTarget + offset;
            _characterCreatePreviewCamera.transform.LookAt(lookTarget);
            RefreshPreviewNameLabelFacing();
        }

        private void FocusSelectedProfessionPreview()
        {
            if (!_professionStepActive)
                return;
            _characterCreatePreviewOrbitYaw = 180f;
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private bool TryGetSelectedProfessionFocusPoint(out Vector3 focus)
        {
            focus = Vector3.zero;
            if (_selectedProfessionPreviewId <= 0)
                return false;
            if (!_professionPreviewActors.TryGetValue(_selectedProfessionPreviewId, out var actor))
                return false;
            if (actor?.Root == null || !actor.Root.activeInHierarchy)
                return false;

            focus = actor.Root.transform.position;
            return true;
        }

        private void SetProfessionActorsVisible(bool visible)
        {
            foreach (var actor in _professionPreviewActors.Values)
            {
                if (actor?.Root != null)
                    actor.Root.SetActive(visible);
            }
        }

        private async Task EnsureProfessionPreviewActorsLoadedAsync()
        {
            if (_characterCreatePreviewRoot == null)
                return;
            if (_professionPreviewActors.Count > 0)
                return;

            var professionLookup = _context?.GetProfessionLookup();
            if (professionLookup == null || professionLookup.Count == 0)
                return;

            var ids = professionLookup.Keys.Where(id => id > 0 && id != 13).OrderBy(id => id).Take(14).ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                int professionId = ids[i];
                string name = professionLookup.TryGetValue(professionId, out var n) ? n : $"Profession {professionId}";

                var actorRoot = new GameObject($"ProfessionPreview_{professionId}_{name}");
                actorRoot.transform.SetParent(_characterCreatePreviewRoot.transform, false);
                bool leftColumn = i < 7;
                int row = leftColumn ? i : (i - 7);
                float x = leftColumn ? -4.8f : 4.8f;
                float z = 3.8f - (row * 1.25f);
                actorRoot.transform.localPosition = new Vector3(x, 0f, z);
                actorRoot.transform.localRotation = Quaternion.Euler(0f, leftColumn ? 35f : -35f, 0f);
                actorRoot.SetActive(false);

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(actorRoot.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 2.0f, 0f);
                var tm = labelGo.AddComponent<TextMesh>();
                tm.text = name;
                tm.fontSize = 42;
                tm.characterSize = 0.03f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.82f, 0.93f, 1f, 0.95f);
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                string meshPath = ResolveProfessionPreviewMeshPath(name);
                bool loaded = false;
                if (!string.IsNullOrWhiteSpace(meshPath))
                    loaded = await TryInstantiateGlbViaCharacterAppearanceHelper(meshPath, actorRoot.transform);

                if (!loaded)
                {
                    // Fallback placeholder so layout still works even if a GLB is missing.
                    var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    capsule.name = "ProfessionPreviewFallback";
                    capsule.transform.SetParent(actorRoot.transform, false);
                    capsule.transform.localPosition = new Vector3(0f, 1f, 0f);
                    capsule.transform.localScale = new Vector3(0.7f, 1.0f, 0.7f);
                    var renderer = capsule.GetComponent<Renderer>();
                    if (renderer != null)
                        renderer.material.color = leftColumn ? new Color(0.18f, 0.46f, 0.66f, 1f) : new Color(0.16f, 0.36f, 0.56f, 1f);
                }

                _professionPreviewActors[professionId] = new ProfessionPreviewActor
                {
                    ProfessionId = professionId,
                    ProfessionName = name,
                    Root = actorRoot,
                    Loaded = loaded
                };
                _professionPreviewOrder.Add(professionId);
            }

            SetProfessionActorsVisible(_professionStepActive);
            if (_professionStepActive)
            {
                UpdateCharacterCreatePreviewCameraFraming();
                UpdateProfessionPreviewCarouselLayout();
                FocusSelectedProfessionPreview();
            }
        }

        private static string ResolveProfessionPreviewMeshPath(string professionName)
        {
            if (string.IsNullOrWhiteSpace(professionName))
                return null;

            string token = professionName.Trim().ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_");
            var candidates = new[]
            {
                Path.Combine(Application.streamingAssetsPath, "AOData", "ProfessionMeshes", $"{token}_3d.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", "CharacterMeshes", $"{token}_3d.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", "UI", $"{token}_3d.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", $"{token}_3d.glb"),
            };

            if (string.Equals(token, "bureaucrat", StringComparison.OrdinalIgnoreCase))
            {
                string typo = Path.Combine(Application.streamingAssetsPath, "AOData", "ProfessionMeshes", "bureaucraft_3d.glb");
                if (File.Exists(typo))
                    return typo;
            }
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private void UpdateProfessionPreviewCarouselLayout()
        {
            if (_professionPreviewOrder.Count == 0)
                return;

            int selectedIndex = _professionPreviewOrder.IndexOf(_selectedProfessionPreviewId);
            if (selectedIndex < 0)
                selectedIndex = 0;

            const float radius = 5.4f;
            const float zCenter = 1.4f;
            float stepDeg = 360f / _professionPreviewOrder.Count;

            for (int i = 0; i < _professionPreviewOrder.Count; i++)
            {
                int professionId = _professionPreviewOrder[i];
                if (!_professionPreviewActors.TryGetValue(professionId, out var actor) || actor?.Root == null)
                    continue;
                bool isSelected = i == selectedIndex;

                float angleDeg = (i - selectedIndex) * stepDeg;
                float rad = angleDeg * Mathf.Deg2Rad;
                float x = Mathf.Sin(rad) * radius;
                float z = zCenter + Mathf.Cos(rad) * radius;
                actor.Root.transform.localPosition = new Vector3(x, 0f, z);
                actor.Root.transform.localScale = isSelected ? new Vector3(2f, 2f, 2f) : Vector3.one;
                var label = actor.Root.transform.Find("Label");
                if (label != null)
                {
                    label.gameObject.SetActive(isSelected);
                    // Keep selected label near head even when mesh is scaled up.
                    label.localPosition = isSelected ? new Vector3(0f, 1.15f, 0f) : new Vector3(0f, 2.0f, 0f);
                }
                // Always face the active preview camera so we never see backs.
                if (_characterCreatePreviewCamera != null)
                {
                    Vector3 toCamera = _characterCreatePreviewCamera.transform.position - actor.Root.transform.position;
                    // Keep models upright by rotating on yaw only (ignore vertical pitch component).
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.0001f)
                        actor.Root.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
            }
        }

        private static async Task<bool> TryInstantiateGlbViaCharacterAppearanceHelper(string glbPath, Transform parent)
        {
            try
            {
                var helperType = typeof(CharacterAppearanceController);
                var method = helperType.GetMethod(
                    "TryInstantiateGlbWithReflection",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                    return false;

                object taskObj = method.Invoke(null, new object[] { glbPath, parent, null, false });
                if (taskObj is Task<bool> boolTask)
                    return await boolTask;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Profession preview GLB load failed for '{glbPath}': {ex.Message}");
            }

            return false;
        }

        private void RefreshPreviewNameLabelFacing()
        {
            if (_characterCreatePreviewCamera == null)
                return;

            Vector3 camPos = _characterCreatePreviewCamera.transform.position;
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var label = _characterCreatePreviewSlots[i]?.NameLabel;
                if (label == null)
                    continue;

                var labelTransform = label.transform;
                Vector3 toCamera = camPos - labelTransform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                    // TextMesh front-face orientation is opposite of our model labels, so invert to avoid mirrored text.
                    labelTransform.rotation = Quaternion.LookRotation((-toCamera).normalized, Vector3.up);
            }

            foreach (var actor in _professionPreviewActors.Values)
            {
                if (actor?.Root == null)
                    continue;
                var labelTransform = actor.Root.transform.Find("Label");
                if (labelTransform == null)
                    continue;
                Vector3 toCamera = camPos - labelTransform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                    labelTransform.rotation = Quaternion.LookRotation((-toCamera).normalized, Vector3.up);
            }
        }

        private static Vector3 ComputeCharacterScale(
            CharacterSelectionWindowView.BodyHeightPreset height,
            CharacterSelectionWindowView.BodyWeightPreset weight)
        {
            float heightScale = height switch
            {
                CharacterSelectionWindowView.BodyHeightPreset.Short => 0.92f,
                CharacterSelectionWindowView.BodyHeightPreset.Tall => 1.08f,
                _ => 1f
            };

            float weightScale = weight switch
            {
                CharacterSelectionWindowView.BodyWeightPreset.Skinny => 0.92f,
                CharacterSelectionWindowView.BodyWeightPreset.Fat => 1.08f,
                _ => 1f
            };

            float xzScale = heightScale * weightScale;
            return new Vector3(xzScale, heightScale, xzScale);
        }

        private void SetCharacterFlowActive(bool active)
        {
            _characterFlowActive = active;
            if (active)
                UpdateCharacterCreatePreviewCameraFraming();
            else
            {
                ApplyWindowLayoutForCharacter(_activeProfileName);
                StartCoroutine(ApplyWindowLayoutDeferred(_activeProfileName));
                StartCoroutine(ApplyWindowLayoutWhenStable(_activeProfileName));
            }

            if (_selfBridge == null)
                return;

            var walker = _selfBridge.GetComponent<PrototypeWalkerController>();
            if (walker == null)
                return;

            if (active)
            {
                _savedWalkerEnabled = walker.enabled;
                walker.enabled = false;
                return;
            }

            walker.enabled = _savedWalkerEnabled;
        }

        private void OnChatCommandIssued(string commandText)
        {
            string cmd = (commandText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cmd))
                return;

            if (string.Equals(cmd, "camp", StringComparison.OrdinalIgnoreCase))
            {
                EnterCharacterSelectFromCamp();
                return;
            }

            _context?.PublishStatus($"Unknown command: {cmd}");
        }

        private void EnterCharacterSelectFromCamp()
        {
            CaptureWindowLayoutForCharacter(_activeProfileName);
            UpdateActiveCharacterLastLocation();
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();

            SetCharacterFlowActive(true);
            if (_characterSelectionRoot != null)
                _characterSelectionRoot.gameObject.SetActive(true);
            _characterSelectionView?.ReturnToSelectionScreen();
            _context?.PublishStatus("Camped to character selection.");
        }

        private void CleanupCharacterCreatePreview()
        {
            if (_characterCreatePreviewCamera != null)
            {
                _characterCreatePreviewCamera.targetTexture = null;
                Destroy(_characterCreatePreviewCamera.gameObject);
            }

            if (_characterCreatePreviewRoot != null)
                Destroy(_characterCreatePreviewRoot);

            if (_characterCreatePreviewTexture != null)
            {
                _characterCreatePreviewTexture.Release();
                Destroy(_characterCreatePreviewTexture);
            }

            _characterCreatePreviewRoot = null;
            _characterCreatePreviewBridge = null;
            _characterCreatePreviewAppearance = null;
            _characterCreatePreviewCamera = null;
            _characterCreatePreviewTexture = null;
            _carouselSelectedIndex = -1;
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                _characterCreatePreviewSlots[i] = null;
        }
    }
}
