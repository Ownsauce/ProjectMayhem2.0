using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AO.Assets.ResourceDatabase;
using AO.Unity.Assets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace AO.Unity.AOStyle
{
    public class F10SettingsWindowView : MonoBehaviour
    {
        private const string PrefsKeyShowSocialArmor = "AO.Prefs.ShowSocialArmor";
        private const string PrefsKeyAllowTells = "AO.Prefs.AllowTells";
        private const string PrefsKeyShortcutBars = "AO.Prefs.ShortcutBars";
        private const string PrefsKeyMasterVolume = "AO.Prefs.MasterVolume";

        private sealed class PreferencesData
        {
            public bool ShowSocialArmor = true;
            public bool AllowTells = true;
            public int ShortcutBars = 1;
            public int MasterVolume = 100;
        }

        private enum CaptureMode
        {
            None = 0,
            Edit = 1,
            Add = 2
        }

        private Font _font;
        private RectTransform _tabContentRoot;
        private string _activeTab = "Key Bindings";
        private Action<Dictionary<string, List<KeyBindingStore.KeyChord>>> _onBindingsApplied;

        private List<KeyBindingStore.ActionBinding> _savedBindings;
        private List<KeyBindingStore.ActionBinding> _workingBindings;
        private string _selectedActionId;
        private int _selectedChordIndex;
        private bool _hasUnsavedChanges;
        private CaptureMode _captureMode;

        private Text _dirtyLabel;
        private Text _hintLabel;
        private PreferencesData _savedPreferences;
        private PreferencesData _workingPreferences;
        private bool _preferencesDirty;
        private string _aoInstallPath = string.Empty;
        private string _aoInstallStatus = string.Empty;
        private bool _aoInstallValid;

        public bool HasUnsavedChanges => _hasUnsavedChanges || _preferencesDirty;

        public void Initialize(Font font, Action<Dictionary<string, List<KeyBindingStore.KeyChord>>> onBindingsApplied = null)
        {
            _font = font;
            _onBindingsApplied = onBindingsApplied;
            _savedBindings = KeyBindingStore.CloneBindings(KeyBindingStore.LoadOrDefault());
            _workingBindings = KeyBindingStore.CloneBindings(_savedBindings);
            _selectedActionId = _workingBindings.FirstOrDefault()?.ActionId;
            _selectedChordIndex = 0;
            _savedPreferences = LoadPreferences();
            _workingPreferences = ClonePreferences(_savedPreferences);
            _preferencesDirty = false;

            AOInstallValidation configuredInstall = AOInstallConfiguration.GetConfiguredInstall();
            _aoInstallPath = configuredInstall.IsValid ? configuredInstall.RootPath : string.Empty;
            SetInstallStatus(configuredInstall);

            Build();
            RenderActiveTab();
            _onBindingsApplied?.Invoke(KeyBindingStore.ToRuntimeMap(_savedBindings));
        }

        public void SaveChanges()
        {
            if (_hasUnsavedChanges)
            {
                _savedBindings = KeyBindingStore.CloneBindings(_workingBindings);
                KeyBindingStore.Save(_savedBindings);
                _onBindingsApplied?.Invoke(KeyBindingStore.ToRuntimeMap(_savedBindings));
            }

            if (_preferencesDirty)
            {
                _savedPreferences = ClonePreferences(_workingPreferences);
                SavePreferences(_savedPreferences);
            }

            _hasUnsavedChanges = false;
            _preferencesDirty = false;
            _captureMode = CaptureMode.None;
            RenderActiveTab();
        }

        public void DiscardChanges()
        {
            _workingBindings = KeyBindingStore.CloneBindings(_savedBindings);
            _workingPreferences = ClonePreferences(_savedPreferences);
            _hasUnsavedChanges = false;
            _preferencesDirty = false;
            _captureMode = CaptureMode.None;
            _selectedActionId = _workingBindings.FirstOrDefault()?.ActionId;
            _selectedChordIndex = 0;
            RenderActiveTab();
        }

        private void Update()
        {
            if (_captureMode == CaptureMode.None || _activeTab != "Key Bindings")
                return;

            if (TryCaptureChord(out var chord))
            {
                ApplyCapturedChord(chord);
                _captureMode = CaptureMode.None;
                RenderActiveTab();
            }
        }

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("F10Root", transform, new Color(0.08f, 0.12f, 0.16f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var tabs = AOStyleUiFactory.CreatePanel("Tabs", root, new Color(0.11f, 0.16f, 0.22f, 0.95f));
            tabs.anchorMin = new Vector2(0f, 1f);
            tabs.anchorMax = new Vector2(1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.offsetMin = new Vector2(6f, -34f);
            tabs.offsetMax = new Vector2(-6f, -6f);

            var tabsLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.padding = new RectOffset(4, 4, 2, 2);
            tabsLayout.spacing = 6f;
            tabsLayout.childControlWidth = false;
            tabsLayout.childControlHeight = true;
            tabsLayout.childForceExpandWidth = false;
            tabsLayout.childForceExpandHeight = true;

            AOStyleUiFactory.CreateButton("TabPreferences", tabs, "Preferences", _font, () => SetActiveTab("Preferences"), 130f);
            AOStyleUiFactory.CreateButton("TabAOAssets", tabs, "AO Assets", _font, () => SetActiveTab("AO Assets"), 130f);
            AOStyleUiFactory.CreateButton("TabFixedKeys", tabs, "Fixed Keys", _font, () => SetActiveTab("Fixed Keys"), 130f);
            AOStyleUiFactory.CreateButton("TabKeyBindings", tabs, "Key Bindings", _font, () => SetActiveTab("Key Bindings"), 130f);

            _tabContentRoot = AOStyleUiFactory.CreatePanel("TabContent", root, new Color(0f, 0f, 0f, 0f));
            _tabContentRoot.anchorMin = new Vector2(0f, 0f);
            _tabContentRoot.anchorMax = new Vector2(1f, 1f);
            _tabContentRoot.offsetMin = new Vector2(6f, 6f);
            _tabContentRoot.offsetMax = new Vector2(-6f, -38f);
        }

        private void SetActiveTab(string tabName)
        {
            _activeTab = tabName;
            if (tabName != "Key Bindings")
                _captureMode = CaptureMode.None;
            RenderActiveTab();
        }

        private void RenderActiveTab()
        {
            if (_tabContentRoot == null)
                return;

            AOStyleUiFactoryCleanup.Clear(_tabContentRoot);

            if (_activeTab == "Preferences")
            {
                RenderPreferencesTab();
                return;
            }

            if (_activeTab == "Fixed Keys")
            {
                RenderPlaceholder(
                    "Fixed Keys",
                    "Fixed Keys tab scaffolded.\nReserved for non-rebindable/system-reserved shortcuts.");
                return;
            }

            if (_activeTab == "AO Assets")
            {
                RenderAOAssetsTab();
                return;
            }

            RenderKeyBindings();
        }

        private void RenderAOAssetsTab()
        {
            var panel = AOStyleUiFactory.CreatePanel("AOAssetsPanel", _tabContentRoot,
                new Color(0.09f, 0.14f, 0.2f, 0.95f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var title = AOStyleUiFactory.CreateText("Title", panel,
                "Anarchy Online Installation", _font, 16, TextAnchor.UpperLeft);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(12f, -32f);
            titleRt.offsetMax = new Vector2(-12f, -8f);

            var help = AOStyleUiFactory.CreateText("Help", panel,
                "Enter the folder containing Anarchy.exe and the cd_image folder. "
                + "Project Mayhem reads assets from your local installation and stores converted files in its private cache.",
                _font, 13, TextAnchor.UpperLeft);
            var helpRt = (RectTransform)help.transform;
            helpRt.anchorMin = new Vector2(0f, 1f);
            helpRt.anchorMax = new Vector2(1f, 1f);
            helpRt.offsetMin = new Vector2(12f, -84f);
            helpRt.offsetMax = new Vector2(-12f, -40f);
            help.horizontalOverflow = HorizontalWrapMode.Wrap;

            var pathInput = AOStyleUiFactory.CreateInputField("AOInstallPath", panel,
                "/path/to/Anarchy Online", _font, 600f);
            var pathRt = (RectTransform)pathInput.transform;
            pathRt.anchorMin = new Vector2(0f, 1f);
            pathRt.anchorMax = new Vector2(1f, 1f);
            pathRt.offsetMin = new Vector2(12f, -126f);
            pathRt.offsetMax = new Vector2(-12f, -98f);
            pathInput.text = _aoInstallPath;
            pathInput.onValueChanged.AddListener(value => _aoInstallPath = value);

            var validate = AOStyleUiFactory.CreateButton("ValidateInstall", panel,
                "Validate and Save", _font, ValidateAndSaveInstall, 150f);
            var validateRt = (RectTransform)validate.transform;
            validateRt.anchorMin = new Vector2(0f, 1f);
            validateRt.anchorMax = new Vector2(0f, 1f);
            validateRt.pivot = new Vector2(0f, 1f);
            validateRt.anchoredPosition = new Vector2(12f, -140f);
            validateRt.sizeDelta = new Vector2(150f, 26f);

            var clear = AOStyleUiFactory.CreateButton("ClearInstall", panel,
                "Clear", _font, ClearInstall, 80f);
            var clearRt = (RectTransform)clear.transform;
            clearRt.anchorMin = new Vector2(0f, 1f);
            clearRt.anchorMax = new Vector2(0f, 1f);
            clearRt.pivot = new Vector2(0f, 1f);
            clearRt.anchoredPosition = new Vector2(170f, -140f);
            clearRt.sizeDelta = new Vector2(80f, 26f);

            var status = AOStyleUiFactory.CreateText("InstallStatus", panel,
                _aoInstallStatus, _font, 13, TextAnchor.UpperLeft);
            var statusRt = (RectTransform)status.transform;
            statusRt.anchorMin = new Vector2(0f, 1f);
            statusRt.anchorMax = new Vector2(1f, 1f);
            statusRt.offsetMin = new Vector2(12f, -230f);
            statusRt.offsetMax = new Vector2(-12f, -176f);
            status.color = _aoInstallValid
                ? new Color(0.55f, 1f, 0.68f, 1f)
                : new Color(1f, 0.65f, 0.5f, 1f);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void ValidateAndSaveInstall()
        {
            AOInstallValidation validation = AOInstallConfiguration.SetInstallPath(_aoInstallPath);
            if (validation.IsValid) _aoInstallPath = validation.RootPath;
            SetInstallStatus(validation);
            RenderActiveTab();
        }

        private void ClearInstall()
        {
            AOInstallConfiguration.ClearInstallPath();
            _aoInstallPath = string.Empty;
            _aoInstallValid = false;
            _aoInstallStatus = "No AO installation is configured.";
            RenderActiveTab();
        }

        private void SetInstallStatus(AOInstallValidation validation)
        {
            _aoInstallValid = validation != null && validation.IsValid;
            if (_aoInstallValid)
            {
                _aoInstallStatus = "Valid AO installation — version "
                    + validation.ClientVersion + "\n" + validation.ResourceDatabasePath;
                return;
            }

            _aoInstallStatus = validation == null || validation.Errors == null
                ? "No AO installation is configured."
                : string.Join(" ", validation.Errors);
        }

        private void RenderPlaceholder(string title, string body)
        {
            var panel = AOStyleUiFactory.CreatePanel("PlaceholderPanel", _tabContentRoot, new Color(0.09f, 0.14f, 0.2f, 0.95f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var titleText = AOStyleUiFactory.CreateText("Title", panel, title, _font, 16, TextAnchor.UpperLeft);
            var titleRt = (RectTransform)titleText.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(12f, -32f);
            titleRt.offsetMax = new Vector2(-12f, -8f);
            titleText.color = new Color(0.9f, 0.97f, 1f, 1f);

            var bodyText = AOStyleUiFactory.CreateText("Body", panel, body, _font, 13, TextAnchor.UpperLeft);
            var bodyRt = (RectTransform)bodyText.transform;
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.offsetMin = new Vector2(12f, 12f);
            bodyRt.offsetMax = new Vector2(-12f, -44f);
            bodyText.color = new Color(0.75f, 0.86f, 0.96f, 0.95f);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void RenderPreferencesTab()
        {
            var panel = AOStyleUiFactory.CreatePanel("PrefsPanel", _tabContentRoot, new Color(0.09f, 0.14f, 0.2f, 0.95f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var titleText = AOStyleUiFactory.CreateText("Title", panel, "Preferences", _font, 16, TextAnchor.UpperLeft);
            var titleRt = (RectTransform)titleText.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(12f, -32f);
            titleRt.offsetMax = new Vector2(-12f, -8f);
            titleText.color = new Color(0.9f, 0.97f, 1f, 1f);

            float y = -66f;
            BuildPrefToggleRow(panel, ref y, "Show Social Armor", _workingPreferences.ShowSocialArmor, v =>
            {
                _workingPreferences.ShowSocialArmor = v;
                _preferencesDirty = true;
                RenderActiveTab();
            });

            BuildPrefToggleRow(panel, ref y, "Allow Tells / Messages", _workingPreferences.AllowTells, v =>
            {
                _workingPreferences.AllowTells = v;
                _preferencesDirty = true;
                RenderActiveTab();
            });

            BuildPrefStepperRow(panel, ref y, "Shortcut Bars", _workingPreferences.ShortcutBars.ToString(), () =>
            {
                _workingPreferences.ShortcutBars = Mathf.Clamp(_workingPreferences.ShortcutBars - 1, 1, 3);
                _preferencesDirty = true;
                RenderActiveTab();
            }, () =>
            {
                _workingPreferences.ShortcutBars = Mathf.Clamp(_workingPreferences.ShortcutBars + 1, 1, 3);
                _preferencesDirty = true;
                RenderActiveTab();
            });

            BuildPrefStepperRow(panel, ref y, "Master Volume", $"{_workingPreferences.MasterVolume}%", () =>
            {
                _workingPreferences.MasterVolume = Mathf.Clamp(_workingPreferences.MasterVolume - 5, 0, 100);
                _preferencesDirty = true;
                RenderActiveTab();
            }, () =>
            {
                _workingPreferences.MasterVolume = Mathf.Clamp(_workingPreferences.MasterVolume + 5, 0, 100);
                _preferencesDirty = true;
                RenderActiveTab();
            });

            var dirty = AOStyleUiFactory.CreateText(
                "PrefsDirty",
                panel,
                _preferencesDirty ? "Unsaved preference changes" : "No pending preference changes",
                _font,
                12,
                TextAnchor.UpperLeft);
            var dirtyRt = (RectTransform)dirty.transform;
            dirtyRt.anchorMin = new Vector2(0f, 0f);
            dirtyRt.anchorMax = new Vector2(1f, 0f);
            dirtyRt.offsetMin = new Vector2(12f, 38f);
            dirtyRt.offsetMax = new Vector2(-12f, 60f);
            dirty.color = _preferencesDirty ? new Color(1f, 0.75f, 0.42f, 1f) : new Color(0.7f, 0.86f, 0.98f, 0.9f);

            var footer = AOStyleUiFactory.CreatePanel("PrefsFooter", panel, new Color(0f, 0f, 0f, 0f));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = new Vector2(8f, 8f);
            footer.offsetMax = new Vector2(-8f, 32f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.padding = new RectOffset(0, 0, 0, 0);
            footerLayout.spacing = 6f;
            footerLayout.childControlWidth = false;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = false;

            AOStyleUiFactory.CreateButton("PrefsResetDefaults", footer, "Reset Defaults", _font, () =>
            {
                _workingPreferences = BuildDefaultPreferences();
                _preferencesDirty = true;
                RenderActiveTab();
            }, 124f);
            AOStyleUiFactory.CreateButton("PrefsCancel", footer, "Cancel", _font, () =>
            {
                _workingPreferences = ClonePreferences(_savedPreferences);
                _preferencesDirty = false;
                RenderActiveTab();
            }, 84f);
            AOStyleUiFactory.CreateButton("PrefsSave", footer, "Save", _font, SaveChanges, 84f);
        }

        private void BuildPrefToggleRow(RectTransform parent, ref float yOffset, string label, bool value, Action<bool> onSet)
        {
            var labelText = AOStyleUiFactory.CreateText(label + "_Label", parent, label, _font, 13, TextAnchor.MiddleLeft);
            var labelRt = (RectTransform)labelText.transform;
            labelRt.anchorMin = new Vector2(0f, 1f);
            labelRt.anchorMax = new Vector2(0.68f, 1f);
            labelRt.offsetMin = new Vector2(12f, yOffset - 24f);
            labelRt.offsetMax = new Vector2(-6f, yOffset);

            var toggleBtn = AOStyleUiFactory.CreateButton(
                label + "_Toggle",
                parent,
                value ? "On" : "Off",
                _font,
                () => onSet?.Invoke(!value),
                84f);
            var btnRt = (RectTransform)toggleBtn.transform;
            if (btnRt != null)
            {
                btnRt.anchorMin = new Vector2(1f, 1f);
                btnRt.anchorMax = new Vector2(1f, 1f);
                btnRt.pivot = new Vector2(1f, 1f);
                btnRt.anchoredPosition = new Vector2(-12f, yOffset);
                btnRt.sizeDelta = new Vector2(84f, 24f);
            }

            yOffset -= 34f;
        }

        private void BuildPrefStepperRow(RectTransform parent, ref float yOffset, string label, string value, Action onMinus, Action onPlus)
        {
            var labelText = AOStyleUiFactory.CreateText(label + "_Label", parent, label, _font, 13, TextAnchor.MiddleLeft);
            var labelRt = (RectTransform)labelText.transform;
            labelRt.anchorMin = new Vector2(0f, 1f);
            labelRt.anchorMax = new Vector2(0.56f, 1f);
            labelRt.offsetMin = new Vector2(12f, yOffset - 24f);
            labelRt.offsetMax = new Vector2(-6f, yOffset);

            var minus = AOStyleUiFactory.CreateButton(label + "_Minus", parent, "-", _font, () => onMinus?.Invoke(), 28f);
            var minusRt = (RectTransform)minus.transform;
            minusRt.anchorMin = new Vector2(0.62f, 1f);
            minusRt.anchorMax = new Vector2(0.62f, 1f);
            minusRt.pivot = new Vector2(0f, 1f);
            minusRt.anchoredPosition = new Vector2(0f, yOffset);
            minusRt.sizeDelta = new Vector2(28f, 24f);

            var valueText = AOStyleUiFactory.CreateText(label + "_Value", parent, value, _font, 12, TextAnchor.MiddleCenter);
            var valueRt = (RectTransform)valueText.transform;
            valueRt.anchorMin = new Vector2(0.62f, 1f);
            valueRt.anchorMax = new Vector2(0.82f, 1f);
            valueRt.offsetMin = new Vector2(32f, yOffset - 24f);
            valueRt.offsetMax = new Vector2(-32f, yOffset);

            var plus = AOStyleUiFactory.CreateButton(label + "_Plus", parent, "+", _font, () => onPlus?.Invoke(), 28f);
            var plusRt = (RectTransform)plus.transform;
            plusRt.anchorMin = new Vector2(0.82f, 1f);
            plusRt.anchorMax = new Vector2(0.82f, 1f);
            plusRt.pivot = new Vector2(0f, 1f);
            plusRt.anchoredPosition = new Vector2(8f, yOffset);
            plusRt.sizeDelta = new Vector2(28f, 24f);

            yOffset -= 34f;
        }

        private static PreferencesData ClonePreferences(PreferencesData source)
        {
            return new PreferencesData
            {
                ShowSocialArmor = source != null && source.ShowSocialArmor,
                AllowTells = source == null || source.AllowTells,
                ShortcutBars = Mathf.Clamp(source?.ShortcutBars ?? 1, 1, 3),
                MasterVolume = Mathf.Clamp(source?.MasterVolume ?? 100, 0, 100)
            };
        }

        private static PreferencesData BuildDefaultPreferences()
        {
            return new PreferencesData
            {
                ShowSocialArmor = true,
                AllowTells = true,
                ShortcutBars = 1,
                MasterVolume = 100
            };
        }

        private static PreferencesData LoadPreferences()
        {
            var defaults = BuildDefaultPreferences();
            return new PreferencesData
            {
                ShowSocialArmor = PlayerPrefs.GetInt(PrefsKeyShowSocialArmor, defaults.ShowSocialArmor ? 1 : 0) != 0,
                AllowTells = PlayerPrefs.GetInt(PrefsKeyAllowTells, defaults.AllowTells ? 1 : 0) != 0,
                ShortcutBars = Mathf.Clamp(PlayerPrefs.GetInt(PrefsKeyShortcutBars, defaults.ShortcutBars), 1, 3),
                MasterVolume = Mathf.Clamp(PlayerPrefs.GetInt(PrefsKeyMasterVolume, defaults.MasterVolume), 0, 100)
            };
        }

        private static void SavePreferences(PreferencesData data)
        {
            if (data == null)
                data = BuildDefaultPreferences();
            PlayerPrefs.SetInt(PrefsKeyShowSocialArmor, data.ShowSocialArmor ? 1 : 0);
            PlayerPrefs.SetInt(PrefsKeyAllowTells, data.AllowTells ? 1 : 0);
            PlayerPrefs.SetInt(PrefsKeyShortcutBars, Mathf.Clamp(data.ShortcutBars, 1, 3));
            PlayerPrefs.SetInt(PrefsKeyMasterVolume, Mathf.Clamp(data.MasterVolume, 0, 100));
            PlayerPrefs.Save();
        }

        private void RenderKeyBindings()
        {
            var host = AOStyleUiFactory.CreatePanel("BindingsHost", _tabContentRoot, new Color(0.07f, 0.11f, 0.16f, 0.95f));
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;

            var split = AOStyleUiFactory.CreatePanel("Split", host, new Color(0f, 0f, 0f, 0f));
            split.anchorMin = Vector2.zero;
            split.anchorMax = Vector2.one;
            split.offsetMin = new Vector2(4f, 4f);
            split.offsetMax = new Vector2(-4f, -4f);

            var listPanel = AOStyleUiFactory.CreatePanel("ListPanel", split, new Color(0.08f, 0.12f, 0.18f, 0.9f));
            listPanel.anchorMin = new Vector2(0f, 0f);
            listPanel.anchorMax = new Vector2(0.64f, 1f);
            listPanel.offsetMin = Vector2.zero;
            listPanel.offsetMax = new Vector2(-4f, 0f);

            var editPanel = AOStyleUiFactory.CreatePanel("EditPanel", split, new Color(0.08f, 0.13f, 0.2f, 0.92f));
            editPanel.anchorMin = new Vector2(0.64f, 0f);
            editPanel.anchorMax = new Vector2(1f, 1f);
            editPanel.offsetMin = new Vector2(4f, 0f);
            editPanel.offsetMax = Vector2.zero;

            BuildBindingsList(listPanel);
            BuildEditorPanel(editPanel);
        }

        private void BuildBindingsList(RectTransform parent)
        {
            var header = AOStyleUiFactory.CreatePanel("Header", parent, new Color(0.12f, 0.19f, 0.28f, 0.98f));
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.offsetMin = new Vector2(4f, -28f);
            header.offsetMax = new Vector2(-4f, -4f);
            CreateHeaderColumn(header, "Category", 0f, 0.24f);
            CreateHeaderColumn(header, "Action", 0.24f, 0.66f);
            CreateHeaderColumn(header, "Binding", 0.66f, 1f);

            var contentHost = AOStyleUiFactory.CreatePanel("ContentHost", parent, new Color(0f, 0f, 0f, 0f));
            contentHost.anchorMin = new Vector2(0f, 0f);
            contentHost.anchorMax = new Vector2(1f, 1f);
            contentHost.offsetMin = new Vector2(4f, 4f);
            contentHost.offsetMax = new Vector2(-4f, -30f);

            var content = AOStyleUiFactory.CreateScrollContent(contentHost);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 2f;
                layout.padding = new RectOffset(2, 2, 2, 2);
            }

            string currentCategory = string.Empty;
            foreach (var entry in _workingBindings)
            {
                if (!string.Equals(currentCategory, entry.Category, StringComparison.Ordinal))
                {
                    currentCategory = entry.Category;
                    CreateCategoryRow(content, currentCategory);
                }

                CreateBindingRow(content, entry);
            }
        }

        private void BuildEditorPanel(RectTransform parent)
        {
            var title = AOStyleUiFactory.CreateText("EditorTitle", parent, "Keybind Editor", _font, 14, TextAnchor.UpperLeft);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(8f, -28f);
            titleRt.offsetMax = new Vector2(-8f, -6f);
            title.color = new Color(0.92f, 0.98f, 1f, 1f);

            var selected = GetSelectedBinding();
            string selectedLabel = selected == null
                ? "No keybind selected."
                : $"{selected.ActionLabel}{(selected.IsStub ? " (stub)" : string.Empty)}";
            var selectedText = AOStyleUiFactory.CreateText("Selected", parent, selectedLabel, _font, 12, TextAnchor.UpperLeft);
            var selectedRt = (RectTransform)selectedText.transform;
            selectedRt.anchorMin = new Vector2(0f, 1f);
            selectedRt.anchorMax = new Vector2(1f, 1f);
            selectedRt.offsetMin = new Vector2(8f, -52f);
            selectedRt.offsetMax = new Vector2(-8f, -28f);
            selectedText.color = new Color(0.82f, 0.92f, 1f, 1f);

            var bindingsLabel = AOStyleUiFactory.CreateText("BindingsLabel", parent, "Bindings", _font, 12, TextAnchor.UpperLeft);
            var bindingsLabelRt = (RectTransform)bindingsLabel.transform;
            bindingsLabelRt.anchorMin = new Vector2(0f, 1f);
            bindingsLabelRt.anchorMax = new Vector2(1f, 1f);
            bindingsLabelRt.offsetMin = new Vector2(8f, -78f);
            bindingsLabelRt.offsetMax = new Vector2(-8f, -56f);
            bindingsLabel.color = new Color(0.9f, 0.97f, 1f, 0.95f);

            var bindingsListHost = AOStyleUiFactory.CreatePanel("BindingsListHost", parent, new Color(0.06f, 0.1f, 0.16f, 0.95f));
            bindingsListHost.anchorMin = new Vector2(0f, 1f);
            bindingsListHost.anchorMax = new Vector2(1f, 1f);
            bindingsListHost.offsetMin = new Vector2(8f, -206f);
            bindingsListHost.offsetMax = new Vector2(-8f, -80f);

            if (selected != null)
            {
                var bindingContent = AOStyleUiFactory.CreateScrollContent(bindingsListHost);
                var vlg = bindingContent.GetComponent<VerticalLayoutGroup>();
                if (vlg != null)
                {
                    vlg.spacing = 2f;
                    vlg.padding = new RectOffset(2, 2, 2, 2);
                }

                for (int i = 0; i < selected.Chords.Count; i++)
                {
                    int index = i;
                    var row = AOStyleUiFactory.CreateButton(
                        "Chord_" + i,
                        bindingContent,
                        $"{(index == _selectedChordIndex ? ">" : " ")} {selected.Chords[i].ToDisplayString()}",
                        _font,
                        () => { _selectedChordIndex = index; RenderActiveTab(); },
                        240f);
                    var rowImage = row.GetComponent<Image>();
                    if (rowImage != null)
                        rowImage.color = index == _selectedChordIndex
                            ? new Color(0.25f, 0.4f, 0.57f, 0.98f)
                            : new Color(0.16f, 0.24f, 0.34f, 0.95f);
                }
            }

            var commandRow = AOStyleUiFactory.CreatePanel("CommandRow", parent, new Color(0f, 0f, 0f, 0f));
            commandRow.anchorMin = new Vector2(0f, 1f);
            commandRow.anchorMax = new Vector2(1f, 1f);
            commandRow.offsetMin = new Vector2(8f, -234f);
            commandRow.offsetMax = new Vector2(-8f, -210f);
            var commandLayout = commandRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            commandLayout.padding = new RectOffset(0, 0, 0, 0);
            commandLayout.spacing = 4f;
            commandLayout.childControlWidth = false;
            commandLayout.childControlHeight = true;
            commandLayout.childForceExpandWidth = false;

            AOStyleUiFactory.CreateButton("EditBinding", commandRow, "Edit", _font, BeginEditBinding, 72f);
            AOStyleUiFactory.CreateButton("AddBinding", commandRow, "Add", _font, BeginAddBinding, 72f);
            AOStyleUiFactory.CreateButton("RemoveBinding", commandRow, "Remove", _font, RemoveSelectedBinding, 72f);

            _hintLabel = AOStyleUiFactory.CreateText("Hint", parent, GetCaptureHint(), _font, 12, TextAnchor.UpperLeft);
            var hintRt = (RectTransform)_hintLabel.transform;
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(1f, 1f);
            hintRt.offsetMin = new Vector2(8f, -262f);
            hintRt.offsetMax = new Vector2(-8f, -236f);
            _hintLabel.color = _captureMode == CaptureMode.None
                ? new Color(0.8f, 0.9f, 0.98f, 0.9f)
                : new Color(1f, 0.86f, 0.56f, 1f);

            _dirtyLabel = AOStyleUiFactory.CreateText(
                "Dirty",
                parent,
                _hasUnsavedChanges ? "Unsaved changes" : "No pending changes",
                _font,
                12,
                TextAnchor.UpperLeft);
            var dirtyRt = (RectTransform)_dirtyLabel.transform;
            dirtyRt.anchorMin = new Vector2(0f, 1f);
            dirtyRt.anchorMax = new Vector2(1f, 1f);
            dirtyRt.offsetMin = new Vector2(8f, -288f);
            dirtyRt.offsetMax = new Vector2(-8f, -262f);
            _dirtyLabel.color = _hasUnsavedChanges
                ? new Color(1f, 0.75f, 0.42f, 1f)
                : new Color(0.7f, 0.86f, 0.98f, 0.9f);

            var footer = AOStyleUiFactory.CreatePanel("Footer", parent, new Color(0f, 0f, 0f, 0f));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = new Vector2(8f, 8f);
            footer.offsetMax = new Vector2(-8f, 32f);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.padding = new RectOffset(0, 0, 0, 0);
            footerLayout.spacing = 6f;
            footerLayout.childControlWidth = false;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = false;

            AOStyleUiFactory.CreateButton("ResetDefaults", footer, "Reset Defaults", _font, ResetDefaults, 116f);
            AOStyleUiFactory.CreateButton("CancelChanges", footer, "Cancel", _font, DiscardChanges, 84f);
            AOStyleUiFactory.CreateButton("SaveChanges", footer, "Save", _font, SaveChanges, 84f);
        }

        private void CreateHeaderColumn(RectTransform parent, string text, float minX, float maxX)
        {
            var label = AOStyleUiFactory.CreateText(text + "Header", parent, text, _font, 12, TextAnchor.MiddleLeft);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 1f);
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            label.color = new Color(0.86f, 0.94f, 1f, 0.98f);
            label.fontStyle = FontStyle.Bold;
        }

        private void CreateCategoryRow(Transform parent, string category)
        {
            var row = AOStyleUiFactory.CreatePanel("Category_" + category, parent, new Color(0.1f, 0.16f, 0.23f, 0.95f));
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;

            var text = AOStyleUiFactory.CreateText("CategoryText", row, category, _font, 12, TextAnchor.MiddleLeft);
            var rt = (RectTransform)text.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            text.color = new Color(0.82f, 0.92f, 1f, 0.98f);
            text.fontStyle = FontStyle.Bold;
        }

        private void CreateBindingRow(Transform parent, KeyBindingStore.ActionBinding entry)
        {
            var rowBtn = AOStyleUiFactory.CreateButton(
                "BindingRow_" + entry.ActionId,
                parent,
                string.Empty,
                _font,
                () =>
                {
                    _selectedActionId = entry.ActionId;
                    _selectedChordIndex = 0;
                    _captureMode = CaptureMode.None;
                    RenderActiveTab();
                },
                100f);

            var rowRt = (RectTransform)rowBtn.transform;
            var rowLe = rowBtn.gameObject.GetComponent<LayoutElement>() ?? rowBtn.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 24f;
            rowLe.preferredWidth = -1f;
            rowRt.sizeDelta = new Vector2(0f, 24f);

            var rowImage = rowBtn.GetComponent<Image>();
            bool selected = string.Equals(entry.ActionId, _selectedActionId, StringComparison.OrdinalIgnoreCase);
            rowImage.color = selected ? new Color(0.2f, 0.33f, 0.48f, 0.98f) : new Color(0.08f, 0.12f, 0.18f, 0.9f);

            CreateRowColumn(rowRt, entry.Category, 0f, 0.24f, new Color(0.67f, 0.8f, 0.93f, 0.98f));
            CreateRowColumn(rowRt, $"{entry.ActionLabel}{(entry.IsStub ? " (stub)" : string.Empty)}", 0.24f, 0.66f, new Color(0.87f, 0.93f, 0.98f, 0.98f));
            CreateRowColumn(rowRt, KeyBindingStore.FormatChords(entry.Chords), 0.66f, 1f, new Color(0.96f, 0.84f, 0.55f, 0.98f));
        }

        private void CreateRowColumn(RectTransform parent, string text, float minX, float maxX, Color color)
        {
            var label = AOStyleUiFactory.CreateText("Col", parent, text, _font, 11, TextAnchor.MiddleLeft);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 1f);
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            label.color = color;
        }

        private KeyBindingStore.ActionBinding GetSelectedBinding()
        {
            if (string.IsNullOrWhiteSpace(_selectedActionId))
                return null;
            return _workingBindings.FirstOrDefault(b => string.Equals(b.ActionId, _selectedActionId, StringComparison.OrdinalIgnoreCase));
        }

        private void BeginEditBinding()
        {
            var selected = GetSelectedBinding();
            if (selected == null || selected.Chords.Count == 0)
                return;
            _selectedChordIndex = Mathf.Clamp(_selectedChordIndex, 0, selected.Chords.Count - 1);
            _captureMode = CaptureMode.Edit;
            RenderActiveTab();
        }

        private void BeginAddBinding()
        {
            if (GetSelectedBinding() == null)
                return;
            _captureMode = CaptureMode.Add;
            RenderActiveTab();
        }

        private void RemoveSelectedBinding()
        {
            var selected = GetSelectedBinding();
            if (selected == null || selected.Chords.Count <= 1)
                return;

            _selectedChordIndex = Mathf.Clamp(_selectedChordIndex, 0, selected.Chords.Count - 1);
            selected.Chords.RemoveAt(_selectedChordIndex);
            _selectedChordIndex = Mathf.Clamp(_selectedChordIndex, 0, selected.Chords.Count - 1);
            _hasUnsavedChanges = true;
            _captureMode = CaptureMode.None;
            RenderActiveTab();
        }

        private void ResetDefaults()
        {
            var defaults = KeyBindingStore.LoadOrDefault();
            foreach (var entry in defaults)
            {
                entry.Chords = entry.DefaultChords.ToList();
            }

            _workingBindings = KeyBindingStore.CloneBindings(defaults);
            _selectedActionId = _workingBindings.FirstOrDefault()?.ActionId;
            _selectedChordIndex = 0;
            _captureMode = CaptureMode.None;
            _hasUnsavedChanges = true;
            RenderActiveTab();
        }

        private void ApplyCapturedChord(KeyBindingStore.KeyChord chord)
        {
            var selected = GetSelectedBinding();
            if (selected == null)
                return;

            if (_captureMode == CaptureMode.Edit)
            {
                if (selected.Chords.Count == 0)
                    selected.Chords.Add(chord);
                else
                {
                    _selectedChordIndex = Mathf.Clamp(_selectedChordIndex, 0, selected.Chords.Count - 1);
                    selected.Chords[_selectedChordIndex] = chord;
                }
            }
            else if (_captureMode == CaptureMode.Add)
            {
                selected.Chords.Add(chord);
                _selectedChordIndex = selected.Chords.Count - 1;
            }

            _hasUnsavedChanges = true;
        }

        private string GetCaptureHint()
        {
            return _captureMode switch
            {
                CaptureMode.Edit => "Editing binding: press any key or mouse button...",
                CaptureMode.Add => "Adding binding: press any key or mouse button...",
                _ => "Select a keybind row, then click Edit or Add."
            };
        }

        private static bool IsModifierToken(string token)
        {
            return token == "LeftCtrl" || token == "RightCtrl"
                || token == "LeftShift" || token == "RightShift"
                || token == "LeftAlt" || token == "RightAlt";
        }

        private bool TryCaptureChord(out KeyBindingStore.KeyChord chord)
        {
            chord = default;
            string keyToken = string.Empty;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                foreach (var key in kb.allKeys)
                {
                    if (!key.wasPressedThisFrame)
                        continue;
                    keyToken = InputSystemKeyToToken(key);
                    if (string.IsNullOrWhiteSpace(keyToken) || IsModifierToken(keyToken))
                        continue;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(keyToken) && Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame) keyToken = "Mouse0";
                else if (Mouse.current.rightButton.wasPressedThisFrame) keyToken = "Mouse1";
                else if (Mouse.current.middleButton.wasPressedThisFrame) keyToken = "Mouse2";
            }
#else
            if (Input.GetMouseButtonDown(0)) keyToken = "Mouse0";
            else if (Input.GetMouseButtonDown(1)) keyToken = "Mouse1";
            else if (Input.GetMouseButtonDown(2)) keyToken = "Mouse2";

            if (string.IsNullOrWhiteSpace(keyToken))
            {
                foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
                {
                    if (!Input.GetKeyDown(keyCode))
                        continue;
                    keyToken = LegacyKeyCodeToToken(keyCode);
                    if (string.IsNullOrWhiteSpace(keyToken) || IsModifierToken(keyToken))
                        continue;
                    break;
                }
            }
#endif

            if (string.IsNullOrWhiteSpace(keyToken))
                return false;

#if ENABLE_INPUT_SYSTEM
            bool ctrl = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            bool shift = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            bool alt = Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
#else
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif

            chord = new KeyBindingStore.KeyChord
            {
                Ctrl = ctrl,
                Shift = shift,
                Alt = alt,
                KeyToken = keyToken
            };
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        private static string InputSystemKeyToToken(KeyControl key)
        {
            if (key == null)
                return string.Empty;
            if (key == Keyboard.current.leftCtrlKey) return "LeftCtrl";
            if (key == Keyboard.current.rightCtrlKey) return "RightCtrl";
            if (key == Keyboard.current.leftShiftKey) return "LeftShift";
            if (key == Keyboard.current.rightShiftKey) return "RightShift";
            if (key == Keyboard.current.leftAltKey) return "LeftAlt";
            if (key == Keyboard.current.rightAltKey) return "RightAlt";
            string display = key.displayName;
            if (string.IsNullOrWhiteSpace(display))
                return string.Empty;
            return display switch
            {
                "0" => "Digit0",
                "1" => "Digit1",
                "2" => "Digit2",
                "3" => "Digit3",
                "4" => "Digit4",
                "5" => "Digit5",
                "6" => "Digit6",
                "7" => "Digit7",
                "8" => "Digit8",
                "9" => "Digit9",
                "," => "Comma",
                "." => "Period",
                "Space" => "Space",
                "Backspace" => "Backspace",
                "UpArrow" => "UpArrow",
                "DownArrow" => "DownArrow",
                "LeftArrow" => "LeftArrow",
                "RightArrow" => "RightArrow",
                _ => NormalizeInputSystemDisplay(display)
            };
        }

        private static string NormalizeInputSystemDisplay(string display)
        {
            if (string.IsNullOrWhiteSpace(display))
                return string.Empty;
            display = display.Trim();
            if (display.Length == 1 && char.IsLetter(display[0]))
                return display.ToUpperInvariant();
            if (display.StartsWith("Numpad ", StringComparison.OrdinalIgnoreCase))
                return "Numpad" + display.Substring(7);
            return display.Replace(" ", string.Empty);
        }
#else
        private static string LegacyKeyCodeToToken(KeyCode keyCode)
        {
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
                return keyCode.ToString();
            if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
                return keyCode.ToString();
            return keyCode switch
            {
                KeyCode.Alpha0 => "Digit0",
                KeyCode.Alpha1 => "Digit1",
                KeyCode.Alpha2 => "Digit2",
                KeyCode.Alpha3 => "Digit3",
                KeyCode.Alpha4 => "Digit4",
                KeyCode.Alpha5 => "Digit5",
                KeyCode.Alpha6 => "Digit6",
                KeyCode.Alpha7 => "Digit7",
                KeyCode.Alpha8 => "Digit8",
                KeyCode.Alpha9 => "Digit9",
                KeyCode.Keypad0 => "Numpad0",
                KeyCode.Keypad1 => "Numpad1",
                KeyCode.Keypad2 => "Numpad2",
                KeyCode.Keypad3 => "Numpad3",
                KeyCode.Keypad4 => "Numpad4",
                KeyCode.Keypad5 => "Numpad5",
                KeyCode.Keypad6 => "Numpad6",
                KeyCode.Keypad7 => "Numpad7",
                KeyCode.Keypad8 => "Numpad8",
                KeyCode.Keypad9 => "Numpad9",
                KeyCode.Comma => "Comma",
                KeyCode.Period => "Period",
                KeyCode.Space => "Space",
                KeyCode.Backspace => "Backspace",
                KeyCode.UpArrow => "UpArrow",
                KeyCode.DownArrow => "DownArrow",
                KeyCode.LeftArrow => "LeftArrow",
                KeyCode.RightArrow => "RightArrow",
                KeyCode.LeftControl => "LeftCtrl",
                KeyCode.RightControl => "RightCtrl",
                KeyCode.LeftShift => "LeftShift",
                KeyCode.RightShift => "RightShift",
                KeyCode.LeftAlt => "LeftAlt",
                KeyCode.RightAlt => "RightAlt",
                _ => string.Empty
            };
        }
#endif
    }
}
