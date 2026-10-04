using System;
using System.Collections.Generic;
using AO.Unity.AOStyle;
using AO.Unity.World;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.Prototype
{
    public partial class PrototypeClientUGUI
    {
        private readonly List<CharacterRuntimeBridge> _cachedFightTargets = new();
        private float _nextFightTargetCacheRefreshAt;
        private Vector3 _lastFightTargetCacheSelfPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        private float _nextFightTargetCycleAllowedAt;
        private int _fightTargetCycleCursor = -1;

        private void OnWindowTransformCommitted(RectTransform root)
        {
            if (_characterFlowActive || root == null)
                return;

            PersistWindowLayoutForRoot(root);
            SaveLocalClientPreferences();
        }

#if ENABLE_INPUT_SYSTEM
        private void HandleConfiguredHotkeysInputSystem(bool textInputFocused)
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;
            if (textInputFocused)
                return;

            // Editor-friendly fallback: F10 may be consumed by Unity's menu bar.
            if (kb.f9Key.wasPressedThisFrame) { ToggleF10WindowWithUnsavedCheck(); return; }

            if (IsActionPressedInputSystem("toggle_f10", kb)) { ToggleF10WindowWithUnsavedCheck(); return; }
            if (IsActionPressedInputSystem("toggle_item_browser", kb)) ToggleWindow(_itemBrowserWindow);
            if (IsActionPressedInputSystem("toggle_inventory", kb)) ToggleWindow(_inventoryWindow);

            if (IsActionPressedInputSystem("equipment_window", kb)) ToggleWindow(_wearWindow);
            if (IsActionPressedInputSystem("programs_window", kb)) ToggleProgramsWindow();
            if (IsActionPressedInputSystem("stats_window", kb)) ToggleWindow(_statsWindow);
            if (IsActionPressedInputSystem("ncu_window", kb)) ToggleWindow(_ncuWindow);
            if (IsActionPressedInputSystem("skills_window", kb)) ToggleSkillsWindow();
            if (IsActionPressedInputSystem("quest_editor_window", kb)) ToggleWindow(_questEditorWindow);
            if (IsActionPressedInputSystem("teleport_window", kb)) ToggleWindow(_teleportWindow);
            if (IsActionPressedInputSystem("status_window", kb)) ToggleWindow(_chatWindow);
            if (IsActionPressedInputSystem("character_settings_window", kb)) ToggleWindow(_characterSettingsWindow);

            if (IsActionPressedInputSystem("actions_window", kb)) PublishStatusStub("Actions Window", "stub");
            if (IsActionPressedInputSystem("knowledge_window", kb)) PublishStatusStub("Knowledge Window", "stub");
            if (IsActionPressedInputSystem("missions_window", kb)) PublishStatusStub("Missions Window", "stub");
            if (IsActionPressedInputSystem("team_window", kb)) PublishStatusStub("Team Window", "stub");
            if (IsActionPressedInputSystem("mini_map", kb)) _gameServerSession?.ToggleWorldGenMiniMap();
            if (IsActionPressedInputSystem("friends_window", kb)) PublishStatusStub("Friends Window", "stub");

            if (IsActionPressedInputSystem("screenshot", kb)) CaptureScreenshot();
            if (IsActionPressedInputSystem("camera_toggle_view", kb)) PublishStatusStub("Toggle 1st/3rd Person View", "stub");
            if (IsActionPressedInputSystem("camera_next_view", kb)) PublishStatusStub("Camera Next View", "stub");
            if (IsActionPressedInputSystem("camera_previous_view", kb)) PublishStatusStub("Camera Previous View", "stub");

            if (IsActionPressedInputSystem("raid_window", kb)) PublishStatusStub("Raid Window", "stub");
            if (IsActionPressedInputSystem("looking_for_team", kb)) PublishStatusStub("Looking for Team Window", "stub");
            if (IsActionPressedInputSystem("research_window", kb)) PublishStatusStub("Research Window", "stub");
            if (IsActionPressedInputSystem("tradeskill_window", kb)) PublishStatusStub("Tradeskill Window", "stub");
            if (IsActionPressedInputSystem("perks_window", kb)) PublishStatusStub("Perks Window", "stub");
            if (IsActionPressedInputSystem("planet_map", kb)) PublishStatusStub("Planet Map", "stub");
            if (IsActionPressedInputSystem("toggle_shortcut_bars", kb)) PublishStatusStub("Show/Hide Shortcut Bars", "stub");

            if (IsActionPressedInputSystem("select_team_1", kb)) PublishStatusStub("Select Team Member 1", "stub");
            if (IsActionPressedInputSystem("select_team_2", kb)) PublishStatusStub("Select Team Member 2", "stub");
            if (IsActionPressedInputSystem("select_team_3", kb)) PublishStatusStub("Select Team Member 3", "stub");
            if (IsActionPressedInputSystem("select_team_4", kb)) PublishStatusStub("Select Team Member 4", "stub");
            if (IsActionPressedInputSystem("select_team_5", kb)) PublishStatusStub("Select Team Member 5", "stub");
            if (IsActionPressedInputSystem("copy_to_clipboard", kb)) CopyTargetNameToClipboard();

            if (IsActionPressedInputSystem("switch_fight_target", kb)) TryCycleFightTargetHotkey();
            if (IsActionPressedInputSystem("aimed_shot", kb)) PublishStatusStub("Aimed Shot", "stub");
            if (IsActionPressedInputSystem("bow_special_attack", kb)) PublishStatusStub("Bow Special Attack", "stub");
            if (IsActionPressedInputSystem("brawl", kb)) PublishStatusStub("Brawl", "stub");
            if (IsActionPressedInputSystem("burst", kb)) TryUseBurstHotkey();
            if (IsActionPressedInputSystem("create_reference", kb)) PublishStatusStub("Create Reference", "stub");
            if (IsActionPressedInputSystem("dimach", kb)) PublishStatusStub("Dimach", "stub");
            if (IsActionPressedInputSystem("fast_attack", kb)) PublishStatusStub("Fast Attack", "stub");
            if (IsActionPressedInputSystem("fling_shot", kb)) TryUseFlingShotHotkey();
            if (IsActionPressedInputSystem("full_auto", kb)) PublishStatusStub("Full Auto", "stub");
            if (IsActionPressedInputSystem("look_at", kb)) ToggleLookAtWindow();
            if (IsActionPressedInputSystem("pickup_item", kb)) PublishStatusStub("Pickup Item", "stub");
            if (IsActionPressedInputSystem("reload", kb)) PublishStatusStub("Reload", "stub");
            if (IsActionPressedInputSystem("sneak", kb)) PublishStatusStub("Sneak", "stub");
            if (IsActionPressedInputSystem("sneak_attack", kb)) PublishStatusStub("Sneak Attack", "stub");
            if (IsActionPressedInputSystem("use", kb)) PublishStatusStub("Use", "stub");

            if (IsActionPressedInputSystem("auto_run", kb)) PublishStatusStub("Auto Run", "stub");
            if (IsActionPressedInputSystem("toggle_walk", kb)) PublishStatusStub("Toggle Walk", "stub");

            if (IsActionPressedInputSystem("shortcut_1", kb)) PublishStatusStub("Shortcut 1", "stub");
            if (IsActionPressedInputSystem("shortcut_2", kb)) PublishStatusStub("Shortcut 2", "stub");
            if (IsActionPressedInputSystem("shortcut_3", kb)) PublishStatusStub("Shortcut 3", "stub");
            if (IsActionPressedInputSystem("shortcut_4", kb)) PublishStatusStub("Shortcut 4", "stub");
            if (IsActionPressedInputSystem("shortcut_5", kb)) PublishStatusStub("Shortcut 5", "stub");
            if (IsActionPressedInputSystem("shortcut_6", kb)) PublishStatusStub("Shortcut 6", "stub");
            if (IsActionPressedInputSystem("shortcut_7", kb)) PublishStatusStub("Shortcut 7", "stub");
            if (IsActionPressedInputSystem("shortcut_8", kb)) PublishStatusStub("Shortcut 8", "stub");
            if (IsActionPressedInputSystem("shortcut_9", kb)) PublishStatusStub("Shortcut 9", "stub");
            if (IsActionPressedInputSystem("shortcut_10", kb)) PublishStatusStub("Shortcut 10", "stub");
        }
#endif

        private void HandleConfiguredHotkeysLegacy(bool textInputFocused)
        {
            if (textInputFocused)
                return;

            // Editor-friendly fallback: F10 may be consumed by Unity's menu bar.
            if (Input.GetKeyDown(KeyCode.F9)) { ToggleF10WindowWithUnsavedCheck(); return; }

            if (IsActionPressedLegacy("toggle_f10")) { ToggleF10WindowWithUnsavedCheck(); return; }
            if (IsActionPressedLegacy("toggle_item_browser")) ToggleWindow(_itemBrowserWindow);
            if (IsActionPressedLegacy("toggle_inventory")) ToggleWindow(_inventoryWindow);

            if (IsActionPressedLegacy("equipment_window")) ToggleWindow(_wearWindow);
            if (IsActionPressedLegacy("programs_window")) ToggleProgramsWindow();
            if (IsActionPressedLegacy("stats_window")) ToggleWindow(_statsWindow);
            if (IsActionPressedLegacy("ncu_window")) ToggleWindow(_ncuWindow);
            if (IsActionPressedLegacy("skills_window")) ToggleSkillsWindow();
            if (IsActionPressedLegacy("quest_editor_window")) ToggleWindow(_questEditorWindow);
            if (IsActionPressedLegacy("teleport_window")) ToggleWindow(_teleportWindow);
            if (IsActionPressedLegacy("status_window")) ToggleWindow(_chatWindow);
            if (IsActionPressedLegacy("character_settings_window")) ToggleWindow(_characterSettingsWindow);

            if (IsActionPressedLegacy("actions_window")) PublishStatusStub("Actions Window", "stub");
            if (IsActionPressedLegacy("knowledge_window")) PublishStatusStub("Knowledge Window", "stub");
            if (IsActionPressedLegacy("missions_window")) PublishStatusStub("Missions Window", "stub");
            if (IsActionPressedLegacy("team_window")) PublishStatusStub("Team Window", "stub");
            if (IsActionPressedLegacy("mini_map")) _gameServerSession?.ToggleWorldGenMiniMap();
            if (IsActionPressedLegacy("friends_window")) PublishStatusStub("Friends Window", "stub");

            if (IsActionPressedLegacy("screenshot")) CaptureScreenshot();
            if (IsActionPressedLegacy("camera_toggle_view")) PublishStatusStub("Toggle 1st/3rd Person View", "stub");
            if (IsActionPressedLegacy("camera_next_view")) PublishStatusStub("Camera Next View", "stub");
            if (IsActionPressedLegacy("camera_previous_view")) PublishStatusStub("Camera Previous View", "stub");

            if (IsActionPressedLegacy("raid_window")) PublishStatusStub("Raid Window", "stub");
            if (IsActionPressedLegacy("looking_for_team")) PublishStatusStub("Looking for Team Window", "stub");
            if (IsActionPressedLegacy("research_window")) PublishStatusStub("Research Window", "stub");
            if (IsActionPressedLegacy("tradeskill_window")) PublishStatusStub("Tradeskill Window", "stub");
            if (IsActionPressedLegacy("perks_window")) PublishStatusStub("Perks Window", "stub");
            if (IsActionPressedLegacy("planet_map")) PublishStatusStub("Planet Map", "stub");
            if (IsActionPressedLegacy("toggle_shortcut_bars")) PublishStatusStub("Show/Hide Shortcut Bars", "stub");

            if (IsActionPressedLegacy("select_team_1")) PublishStatusStub("Select Team Member 1", "stub");
            if (IsActionPressedLegacy("select_team_2")) PublishStatusStub("Select Team Member 2", "stub");
            if (IsActionPressedLegacy("select_team_3")) PublishStatusStub("Select Team Member 3", "stub");
            if (IsActionPressedLegacy("select_team_4")) PublishStatusStub("Select Team Member 4", "stub");
            if (IsActionPressedLegacy("select_team_5")) PublishStatusStub("Select Team Member 5", "stub");
            if (IsActionPressedLegacy("copy_to_clipboard")) CopyTargetNameToClipboard();

            if (IsActionPressedLegacy("switch_fight_target")) TryCycleFightTargetHotkey();
            if (IsActionPressedLegacy("aimed_shot")) PublishStatusStub("Aimed Shot", "stub");
            if (IsActionPressedLegacy("bow_special_attack")) PublishStatusStub("Bow Special Attack", "stub");
            if (IsActionPressedLegacy("brawl")) PublishStatusStub("Brawl", "stub");
            if (IsActionPressedLegacy("burst")) TryUseBurstHotkey();
            if (IsActionPressedLegacy("create_reference")) PublishStatusStub("Create Reference", "stub");
            if (IsActionPressedLegacy("dimach")) PublishStatusStub("Dimach", "stub");
            if (IsActionPressedLegacy("fast_attack")) PublishStatusStub("Fast Attack", "stub");
            if (IsActionPressedLegacy("fling_shot")) TryUseFlingShotHotkey();
            if (IsActionPressedLegacy("full_auto")) PublishStatusStub("Full Auto", "stub");
            if (IsActionPressedLegacy("look_at")) ToggleLookAtWindow();
            if (IsActionPressedLegacy("pickup_item")) PublishStatusStub("Pickup Item", "stub");
            if (IsActionPressedLegacy("reload")) PublishStatusStub("Reload", "stub");
            if (IsActionPressedLegacy("sneak")) PublishStatusStub("Sneak", "stub");
            if (IsActionPressedLegacy("sneak_attack")) PublishStatusStub("Sneak Attack", "stub");
            if (IsActionPressedLegacy("use")) PublishStatusStub("Use", "stub");

            if (IsActionPressedLegacy("auto_run")) PublishStatusStub("Auto Run", "stub");
            if (IsActionPressedLegacy("toggle_walk")) PublishStatusStub("Toggle Walk", "stub");

            if (IsActionPressedLegacy("shortcut_1")) PublishStatusStub("Shortcut 1", "stub");
            if (IsActionPressedLegacy("shortcut_2")) PublishStatusStub("Shortcut 2", "stub");
            if (IsActionPressedLegacy("shortcut_3")) PublishStatusStub("Shortcut 3", "stub");
            if (IsActionPressedLegacy("shortcut_4")) PublishStatusStub("Shortcut 4", "stub");
            if (IsActionPressedLegacy("shortcut_5")) PublishStatusStub("Shortcut 5", "stub");
            if (IsActionPressedLegacy("shortcut_6")) PublishStatusStub("Shortcut 6", "stub");
            if (IsActionPressedLegacy("shortcut_7")) PublishStatusStub("Shortcut 7", "stub");
            if (IsActionPressedLegacy("shortcut_8")) PublishStatusStub("Shortcut 8", "stub");
            if (IsActionPressedLegacy("shortcut_9")) PublishStatusStub("Shortcut 9", "stub");
            if (IsActionPressedLegacy("shortcut_10")) PublishStatusStub("Shortcut 10", "stub");
        }

        private void OnKeyBindingsApplied(Dictionary<string, List<KeyBindingStore.KeyChord>> runtimeMap)
        {
            _runtimeHotkeys = runtimeMap ?? new Dictionary<string, List<KeyBindingStore.KeyChord>>(StringComparer.OrdinalIgnoreCase);
        }

        private void ToggleF10WindowWithUnsavedCheck()
        {
            if (_f10Window?.Root == null)
                return;

            if (_f10Window.Root.gameObject.activeSelf)
                TryCloseF10Window();
            else
            {
                _f10Window.Root.gameObject.SetActive(true);
                _f10Window.Root.SetAsLastSibling();
                if (_f10UnsavedWindow?.Root != null)
                    _f10UnsavedWindow.Root.gameObject.SetActive(false);
            }
        }

        private void TryCloseF10Window()
        {
            if (_f10Window?.Root == null)
                return;

            if (_f10SettingsView != null && _f10SettingsView.HasUnsavedChanges)
            {
                ShowF10UnsavedPrompt();
                return;
            }

            _f10Window.Root.gameObject.SetActive(false);
            if (_f10UnsavedWindow?.Root != null)
                _f10UnsavedWindow.Root.gameObject.SetActive(false);
        }

        private void ShowF10UnsavedPrompt()
        {
            if (_f10Window?.Root == null)
                return;

            if (_f10UnsavedWindow?.Root == null)
            {
                var parent = _f10Window.Root.parent as RectTransform;
                if (parent == null)
                    return;
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _f10UnsavedWindow = AOStyleUiFactory.CreateWindow(parent, font, "Unsaved Changes", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                ConfigureWindowFrame(_f10UnsavedWindow, new Vector2(420f, 180f), Vector2.zero, new Vector2(360f, 150f), new Vector2(460f, 220f));
                BuildUnsavedPromptContent(_f10UnsavedWindow.Content, font);
            }

            _f10UnsavedWindow.Root.gameObject.SetActive(true);
            _f10UnsavedWindow.Root.SetAsLastSibling();
        }

        private void BuildUnsavedPromptContent(RectTransform content, Font font)
        {
            AOStyleUiFactoryCleanup.Clear(content);
            var panel = AOStyleUiFactory.CreatePanel("UnsavedPromptBody", content, new Color(0.08f, 0.12f, 0.18f, 0.95f));
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var text = AOStyleUiFactory.CreateText(
                "PromptText",
                panel,
                "You have unsaved keybind changes.\nSave changes before closing F10?",
                font,
                13,
                TextAnchor.UpperLeft);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = new Vector2(0f, 0f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.offsetMin = new Vector2(10f, 44f);
            textRt.offsetMax = new Vector2(-10f, -10f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var buttons = AOStyleUiFactory.CreatePanel("Buttons", panel, new Color(0f, 0f, 0f, 0f));
            buttons.anchorMin = new Vector2(0f, 0f);
            buttons.anchorMax = new Vector2(1f, 0f);
            buttons.offsetMin = new Vector2(10f, 10f);
            buttons.offsetMax = new Vector2(-10f, 34f);
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            AOStyleUiFactory.CreateButton("SaveAndClose", buttons, "Save & Close", font, () =>
            {
                _f10SettingsView?.SaveChanges();
                _f10Window.Root.gameObject.SetActive(false);
                if (_f10UnsavedWindow?.Root != null)
                    _f10UnsavedWindow.Root.gameObject.SetActive(false);
            }, 110f);

            AOStyleUiFactory.CreateButton("DiscardAndClose", buttons, "Discard & Close", font, () =>
            {
                _f10SettingsView?.DiscardChanges();
                _f10Window.Root.gameObject.SetActive(false);
                if (_f10UnsavedWindow?.Root != null)
                    _f10UnsavedWindow.Root.gameObject.SetActive(false);
            }, 120f);

            AOStyleUiFactory.CreateButton("CancelClose", buttons, "Cancel", font, () =>
            {
                if (_f10UnsavedWindow?.Root != null)
                    _f10UnsavedWindow.Root.gameObject.SetActive(false);
            }, 80f);

            if (_f10UnsavedWindow?.CloseButton != null)
            {
                _f10UnsavedWindow.CloseButton.onClick.RemoveAllListeners();
                _f10UnsavedWindow.CloseButton.onClick.AddListener(() =>
                {
                    if (_f10UnsavedWindow?.Root != null)
                        _f10UnsavedWindow.Root.gameObject.SetActive(false);
                });
            }
        }

#if ENABLE_INPUT_SYSTEM
        private bool IsActionPressedInputSystem(string actionId, Keyboard kb)
        {
            if (kb == null || !_runtimeHotkeys.TryGetValue(actionId, out var chords) || chords == null)
                return false;

            bool ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shiftHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool altHeld = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            foreach (var chord in chords)
            {
                if (!ModifiersMatch(chord, ctrlHeld, shiftHeld, altHeld))
                    continue;
                if (WasInputSystemKeyPressedThisFrame(kb, chord.KeyToken))
                    return true;
            }

            return false;
        }
#endif

        private bool IsActionPressedLegacy(string actionId)
        {
            if (!_runtimeHotkeys.TryGetValue(actionId, out var chords) || chords == null)
                return false;

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            foreach (var chord in chords)
            {
                if (!ModifiersMatch(chord, ctrlHeld, shiftHeld, altHeld))
                    continue;
                if (WasLegacyKeyPressedThisFrame(chord.KeyToken))
                    return true;
            }

            return false;
        }

        private static bool ModifiersMatch(KeyBindingStore.KeyChord chord, bool ctrlHeld, bool shiftHeld, bool altHeld)
        {
            return chord.Ctrl == ctrlHeld
                && chord.Shift == shiftHeld
                && chord.Alt == altHeld;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool WasInputSystemKeyPressedThisFrame(Keyboard kb, string keyToken)
        {
            return keyToken switch
            {
                "A" => kb.aKey.wasPressedThisFrame,
                "B" => kb.bKey.wasPressedThisFrame,
                "C" => kb.cKey.wasPressedThisFrame,
                "D" => kb.dKey.wasPressedThisFrame,
                "E" => kb.eKey.wasPressedThisFrame,
                "F" => kb.fKey.wasPressedThisFrame,
                "G" => kb.gKey.wasPressedThisFrame,
                "H" => kb.hKey.wasPressedThisFrame,
                "I" => kb.iKey.wasPressedThisFrame,
                "J" => kb.jKey.wasPressedThisFrame,
                "K" => kb.kKey.wasPressedThisFrame,
                "L" => kb.lKey.wasPressedThisFrame,
                "M" => kb.mKey.wasPressedThisFrame,
                "N" => kb.nKey.wasPressedThisFrame,
                "O" => kb.oKey.wasPressedThisFrame,
                "P" => kb.pKey.wasPressedThisFrame,
                "Q" => kb.qKey.wasPressedThisFrame,
                "R" => kb.rKey.wasPressedThisFrame,
                "S" => kb.sKey.wasPressedThisFrame,
                "T" => kb.tKey.wasPressedThisFrame,
                "U" => kb.uKey.wasPressedThisFrame,
                "V" => kb.vKey.wasPressedThisFrame,
                "W" => kb.wKey.wasPressedThisFrame,
                "X" => kb.xKey.wasPressedThisFrame,
                "Y" => kb.yKey.wasPressedThisFrame,
                "Z" => kb.zKey.wasPressedThisFrame,
                "F1" => kb.f1Key.wasPressedThisFrame,
                "F2" => kb.f2Key.wasPressedThisFrame,
                "F3" => kb.f3Key.wasPressedThisFrame,
                "F4" => kb.f4Key.wasPressedThisFrame,
                "F5" => kb.f5Key.wasPressedThisFrame,
                "F6" => kb.f6Key.wasPressedThisFrame,
                "F7" => kb.f7Key.wasPressedThisFrame,
                "F8" => kb.f8Key.wasPressedThisFrame,
                "F9" => kb.f9Key.wasPressedThisFrame,
                "F10" => kb.f10Key.wasPressedThisFrame,
                "F11" => kb.f11Key.wasPressedThisFrame,
                "F12" => kb.f12Key.wasPressedThisFrame,
                "Digit0" => kb.digit0Key.wasPressedThisFrame,
                "Digit1" => kb.digit1Key.wasPressedThisFrame,
                "Digit2" => kb.digit2Key.wasPressedThisFrame,
                "Digit3" => kb.digit3Key.wasPressedThisFrame,
                "Digit4" => kb.digit4Key.wasPressedThisFrame,
                "Digit5" => kb.digit5Key.wasPressedThisFrame,
                "Digit6" => kb.digit6Key.wasPressedThisFrame,
                "Digit7" => kb.digit7Key.wasPressedThisFrame,
                "Digit8" => kb.digit8Key.wasPressedThisFrame,
                "Digit9" => kb.digit9Key.wasPressedThisFrame,
                "Numpad0" => kb.numpad0Key.wasPressedThisFrame,
                "Numpad1" => kb.numpad1Key.wasPressedThisFrame,
                "Numpad2" => kb.numpad2Key.wasPressedThisFrame,
                "Numpad3" => kb.numpad3Key.wasPressedThisFrame,
                "Numpad4" => kb.numpad4Key.wasPressedThisFrame,
                "Numpad5" => kb.numpad5Key.wasPressedThisFrame,
                "Numpad6" => kb.numpad6Key.wasPressedThisFrame,
                "Numpad7" => kb.numpad7Key.wasPressedThisFrame,
                "Numpad8" => kb.numpad8Key.wasPressedThisFrame,
                "Numpad9" => kb.numpad9Key.wasPressedThisFrame,
                "Comma" => kb.commaKey.wasPressedThisFrame,
                "Period" => kb.periodKey.wasPressedThisFrame,
                "Space" => kb.spaceKey.wasPressedThisFrame,
                "Tab" => kb.tabKey.wasPressedThisFrame,
                "Backspace" => kb.backspaceKey.wasPressedThisFrame,
                "UpArrow" => kb.upArrowKey.wasPressedThisFrame,
                "DownArrow" => kb.downArrowKey.wasPressedThisFrame,
                "LeftArrow" => kb.leftArrowKey.wasPressedThisFrame,
                "RightArrow" => kb.rightArrowKey.wasPressedThisFrame,
                "Mouse0" => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame,
                "Mouse1" => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame,
                "Mouse2" => Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame,
                "LeftBracket" => kb.leftBracketKey.wasPressedThisFrame,
                "RightBracket" => kb.rightBracketKey.wasPressedThisFrame,
                _ => false
            };
        }
#endif

        private static bool WasLegacyKeyPressedThisFrame(string keyToken)
        {
            return keyToken switch
            {
                "A" => Input.GetKeyDown(KeyCode.A),
                "B" => Input.GetKeyDown(KeyCode.B),
                "C" => Input.GetKeyDown(KeyCode.C),
                "D" => Input.GetKeyDown(KeyCode.D),
                "E" => Input.GetKeyDown(KeyCode.E),
                "F" => Input.GetKeyDown(KeyCode.F),
                "G" => Input.GetKeyDown(KeyCode.G),
                "H" => Input.GetKeyDown(KeyCode.H),
                "I" => Input.GetKeyDown(KeyCode.I),
                "J" => Input.GetKeyDown(KeyCode.J),
                "K" => Input.GetKeyDown(KeyCode.K),
                "L" => Input.GetKeyDown(KeyCode.L),
                "M" => Input.GetKeyDown(KeyCode.M),
                "N" => Input.GetKeyDown(KeyCode.N),
                "O" => Input.GetKeyDown(KeyCode.O),
                "P" => Input.GetKeyDown(KeyCode.P),
                "Q" => Input.GetKeyDown(KeyCode.Q),
                "R" => Input.GetKeyDown(KeyCode.R),
                "S" => Input.GetKeyDown(KeyCode.S),
                "T" => Input.GetKeyDown(KeyCode.T),
                "U" => Input.GetKeyDown(KeyCode.U),
                "V" => Input.GetKeyDown(KeyCode.V),
                "W" => Input.GetKeyDown(KeyCode.W),
                "X" => Input.GetKeyDown(KeyCode.X),
                "Y" => Input.GetKeyDown(KeyCode.Y),
                "Z" => Input.GetKeyDown(KeyCode.Z),
                "F1" => Input.GetKeyDown(KeyCode.F1),
                "F2" => Input.GetKeyDown(KeyCode.F2),
                "F3" => Input.GetKeyDown(KeyCode.F3),
                "F4" => Input.GetKeyDown(KeyCode.F4),
                "F5" => Input.GetKeyDown(KeyCode.F5),
                "F6" => Input.GetKeyDown(KeyCode.F6),
                "F7" => Input.GetKeyDown(KeyCode.F7),
                "F8" => Input.GetKeyDown(KeyCode.F8),
                "F9" => Input.GetKeyDown(KeyCode.F9),
                "F10" => Input.GetKeyDown(KeyCode.F10),
                "F11" => Input.GetKeyDown(KeyCode.F11),
                "F12" => Input.GetKeyDown(KeyCode.F12),
                "Digit0" => Input.GetKeyDown(KeyCode.Alpha0),
                "Digit1" => Input.GetKeyDown(KeyCode.Alpha1),
                "Digit2" => Input.GetKeyDown(KeyCode.Alpha2),
                "Digit3" => Input.GetKeyDown(KeyCode.Alpha3),
                "Digit4" => Input.GetKeyDown(KeyCode.Alpha4),
                "Digit5" => Input.GetKeyDown(KeyCode.Alpha5),
                "Digit6" => Input.GetKeyDown(KeyCode.Alpha6),
                "Digit7" => Input.GetKeyDown(KeyCode.Alpha7),
                "Digit8" => Input.GetKeyDown(KeyCode.Alpha8),
                "Digit9" => Input.GetKeyDown(KeyCode.Alpha9),
                "Numpad0" => Input.GetKeyDown(KeyCode.Keypad0),
                "Numpad1" => Input.GetKeyDown(KeyCode.Keypad1),
                "Numpad2" => Input.GetKeyDown(KeyCode.Keypad2),
                "Numpad3" => Input.GetKeyDown(KeyCode.Keypad3),
                "Numpad4" => Input.GetKeyDown(KeyCode.Keypad4),
                "Numpad5" => Input.GetKeyDown(KeyCode.Keypad5),
                "Numpad6" => Input.GetKeyDown(KeyCode.Keypad6),
                "Numpad7" => Input.GetKeyDown(KeyCode.Keypad7),
                "Numpad8" => Input.GetKeyDown(KeyCode.Keypad8),
                "Numpad9" => Input.GetKeyDown(KeyCode.Keypad9),
                "Comma" => Input.GetKeyDown(KeyCode.Comma),
                "Period" => Input.GetKeyDown(KeyCode.Period),
                "Space" => Input.GetKeyDown(KeyCode.Space),
                "Tab" => Input.GetKeyDown(KeyCode.Tab),
                "Backspace" => Input.GetKeyDown(KeyCode.Backspace),
                "UpArrow" => Input.GetKeyDown(KeyCode.UpArrow),
                "DownArrow" => Input.GetKeyDown(KeyCode.DownArrow),
                "LeftArrow" => Input.GetKeyDown(KeyCode.LeftArrow),
                "RightArrow" => Input.GetKeyDown(KeyCode.RightArrow),
                "Mouse0" => Input.GetMouseButtonDown(0),
                "Mouse1" => Input.GetMouseButtonDown(1),
                "Mouse2" => Input.GetMouseButtonDown(2),
                "LeftBracket" => Input.GetKeyDown(KeyCode.LeftBracket),
                "RightBracket" => Input.GetKeyDown(KeyCode.RightBracket),
                _ => false
            };
        }

        private void CaptureScreenshot()
        {
            string fileName = $"ao_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            ScreenCapture.CaptureScreenshot(fileName);
            _context?.PublishStatus($"Screenshot saved: {fileName}");
        }

        private void CopyTargetNameToClipboard()
        {
            string text = "AO Prototype";
            var target = _context?.SelectedTarget;
            if (target != null && target.Character != null && !string.IsNullOrWhiteSpace(target.Character.Name))
                text = target.Character.Name;
            GUIUtility.systemCopyBuffer = text;
            _context?.PublishStatus($"Copied to clipboard: {text}");
        }

        private void PublishStatusStub(string label, string suffix)
        {
            _context?.PublishStatus($"{label} ({suffix}).");
        }

        private void TryUseBurstHotkey()
        {
            if (_context == null)
                return;

            if (_context.TryUseBurstSpecial(out _))
                TryPlaySpecialAttackAnimation(isBurst: true);
        }

        private void TryUseFlingShotHotkey()
        {
            if (_context == null)
                return;

            if (_context.TryUseFlingShotSpecial(out _))
                TryPlaySpecialAttackAnimation(isBurst: false);
        }

        private void TryPlaySpecialAttackAnimation(bool isBurst)
        {
            if (_selfBridge == null)
                return;

            var appearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (appearance == null)
                return;

            if (!isBurst && appearance.TryPlayRecentAttackHitAction(out _))
                return;

            string[] candidates;
            if (isBurst)
            {
                bool oneHandedRanged = _context != null && _context.IsEquippedBurstWeaponOneHandedRanged();
                candidates = oneHandedRanged
                    ? new[]
                    {
                        "male_smallarms-burstl_01_02", "male_smallarms-burstr_01_02",
                        "female_smallarms-burstl_01_02", "female_smallarms-burstr_01_02",
                        "athrox_smallarms-burstl_01_02", "athrox_smallarms-burstr_01_02",
                        "1h ranged Burst Lhand", "1h ranged Burst Rhand",
                        "male_rifle-burst_01_01", "female_rifle-burst_01_01", "athrox_rifle-burst_01_01",
                        "2h ranged Burst Special"
                    }
                    : new[]
                    {
                        "male_rifle-burst_01_01", "female_rifle-burst_01_01", "athrox_rifle-burst_01_01",
                        "2h ranged Burst Special",
                        "male_smallarms-burstl_01_02", "male_smallarms-burstr_01_02",
                        "female_smallarms-burstl_01_02", "female_smallarms-burstr_01_02",
                        "athrox_smallarms-burstl_01_02", "athrox_smallarms-burstr_01_02",
                        "1h ranged Burst Lhand", "1h ranged Burst Rhand"
                    };
            }
            else
            {
                candidates = new[]
                {
                    "1h ranged Attack Rhand", "1h ranged Attack Lhand", "2h ranged Attack",
                    "male_smallarms-shoot_01_01", "male_rifle-shoot_01_01"
                };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (appearance.TryPlayOneShotAction(candidates[i], out _))
                    break;
            }
        }

        private void TryCycleFightTargetHotkey()
        {
            if (_context == null || _selfBridge == null)
                return;
            if (Time.unscaledTime < _nextFightTargetCycleAllowedAt)
                return;
            _nextFightTargetCycleAllowedAt = Time.unscaledTime + 0.1f;

            Vector3 selfPos = _selfBridge.transform.position;
            const float maxRange = 22f;
            bool needsRefresh =
                Time.unscaledTime >= _nextFightTargetCacheRefreshAt
                || _cachedFightTargets.Count == 0;

            if (needsRefresh)
            {
                _cachedFightTargets.Clear();
                var all = _cachedRuntimeBridges;
                if (all == null || all.Length == 0)
                {
                    all = FindObjectsByType<CharacterRuntimeBridge>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    _cachedRuntimeBridges = all ?? Array.Empty<CharacterRuntimeBridge>();
                }
                if (all == null || all.Length == 0)
                    return;

                float maxRangeSqr = maxRange * maxRange;
                for (int i = 0; i < all.Length; i++)
                {
                    var bridge = all[i];
                    if (bridge == null || bridge == _selfBridge || bridge.transform == null)
                        continue;

                    var runtimeCombat = bridge.GetComponent<RuntimeDynelCombatState>();
                    if (runtimeCombat != null && runtimeCombat.IsDead)
                        continue;

                    Vector3 delta = bridge.transform.position - selfPos;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > maxRangeSqr)
                        continue;
                    _cachedFightTargets.Add(bridge);
                }

                _lastFightTargetCacheSelfPos = selfPos;
                _nextFightTargetCacheRefreshAt = Time.unscaledTime + 1.1f;
                _fightTargetCycleCursor = -1;
            }

            if (_cachedFightTargets.Count == 0)
            {
                _context.PublishStatus($"No targets in range ({maxRange:0}m).");
                return;
            }

            var current = _context.SelectedTarget;
            if (_fightTargetCycleCursor < 0 || _fightTargetCycleCursor >= _cachedFightTargets.Count)
            {
                _fightTargetCycleCursor = current != null ? _cachedFightTargets.IndexOf(current) : -1;
            }

            int nextIndex = (_fightTargetCycleCursor + 1) % _cachedFightTargets.Count;
            var nextTarget = _cachedFightTargets[nextIndex];
            if (nextTarget == null || nextTarget == current)
                return;

            _fightTargetCycleCursor = nextIndex;
            _context.TrySetSelectedTarget(
                nextTarget,
                evaluateQuestSelection: false,
                publishStatus: false,
                notifyState: false);
        }

        private void ToggleSkillsWindow()
        {
            if (_skillsWindow?.Root == null)
                return;

            bool next = !_skillsWindow.Root.gameObject.activeSelf;
            _skillsWindow.Root.gameObject.SetActive(next);
            if (next)
                _skillsWindow.Root.SetAsLastSibling();
        }

        private void ToggleProgramsWindow()
        {
            if (_programsWindow?.Root == null)
                return;

            bool next = !_programsWindow.Root.gameObject.activeSelf;
            _programsWindow.Root.gameObject.SetActive(next);
            if (next)
                _programsWindow.Root.SetAsLastSibling();
        }

        private static void ToggleWindow(AOStyleUiFactory.WindowRefs window)
        {
            if (window?.Root == null)
                return;

            bool next = !window.Root.gameObject.activeSelf;
            window.Root.gameObject.SetActive(next);
            if (next)
                window.Root.SetAsLastSibling();
        }
    }
}
