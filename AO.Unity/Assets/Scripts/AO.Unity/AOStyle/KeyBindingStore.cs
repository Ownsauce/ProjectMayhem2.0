using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AO.Unity.AOStyle
{
    public static class KeyBindingStore
    {
        private const string PlayerPrefsKey = "AO.KeyBindings.v1";

        [Serializable]
        private sealed class BindingSaveData
        {
            public List<ActionBindingData> Actions = new List<ActionBindingData>();
        }

        [Serializable]
        private sealed class ActionBindingData
        {
            public string ActionId;
            public List<KeyChordData> Chords = new List<KeyChordData>();
        }

        [Serializable]
        private sealed class KeyChordData
        {
            public bool Ctrl;
            public bool Shift;
            public bool Alt;
            public string KeyToken;
        }

        public sealed class ActionBinding
        {
            public string Category;
            public string ActionId;
            public string ActionLabel;
            public bool IsStub;
            public List<KeyChord> DefaultChords = new List<KeyChord>();
            public List<KeyChord> Chords = new List<KeyChord>();
        }

        public struct KeyChord
        {
            public bool Ctrl;
            public bool Shift;
            public bool Alt;
            public string KeyToken;

            public string ToDisplayString()
            {
                var parts = new List<string>(4);
                if (Ctrl) parts.Add("Ctrl");
                if (Shift) parts.Add("Shift");
                if (Alt) parts.Add("Alt");
                parts.Add(KeyTokenToDisplay(KeyToken));
                return string.Join("+", parts);
            }
        }

        public static IReadOnlyList<ActionBinding> LoadOrDefault()
        {
            var defaults = BuildDefaultBindings();
            if (!PlayerPrefs.HasKey(PlayerPrefsKey))
                return defaults;

            try
            {
                string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                    return defaults;

                var saved = JsonUtility.FromJson<BindingSaveData>(json);
                if (saved == null || saved.Actions == null)
                    return defaults;

                var lookup = saved.Actions.ToDictionary(a => a.ActionId, a => a, StringComparer.OrdinalIgnoreCase);
                foreach (var action in defaults)
                {
                    if (!lookup.TryGetValue(action.ActionId, out var loaded) || loaded?.Chords == null || loaded.Chords.Count == 0)
                        continue;

                    action.Chords = loaded.Chords
                        .Where(c => c != null && !string.IsNullOrWhiteSpace(c.KeyToken))
                        .Select(c => new KeyChord
                        {
                            Ctrl = c.Ctrl,
                            Shift = c.Shift,
                            Alt = c.Alt,
                            KeyToken = NormalizeKeyToken(c.KeyToken)
                        })
                        .Where(c => !string.IsNullOrWhiteSpace(c.KeyToken))
                        .ToList();
                }
            }
            catch
            {
                return defaults;
            }

            return defaults;
        }

        public static void Save(IReadOnlyList<ActionBinding> bindings)
        {
            var save = new BindingSaveData();
            foreach (var action in bindings)
            {
                var entry = new ActionBindingData { ActionId = action.ActionId };
                foreach (var chord in action.Chords)
                {
                    if (string.IsNullOrWhiteSpace(chord.KeyToken))
                        continue;
                    entry.Chords.Add(new KeyChordData
                    {
                        Ctrl = chord.Ctrl,
                        Shift = chord.Shift,
                        Alt = chord.Alt,
                        KeyToken = NormalizeKeyToken(chord.KeyToken)
                    });
                }

                if (entry.Chords.Count > 0)
                    save.Actions.Add(entry);
            }

            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
        }

        public static Dictionary<string, List<KeyChord>> ToRuntimeMap(IReadOnlyList<ActionBinding> bindings)
        {
            var map = new Dictionary<string, List<KeyChord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in bindings)
            {
                map[action.ActionId] = action.Chords
                    .Where(c => !string.IsNullOrWhiteSpace(c.KeyToken))
                    .ToList();
            }

            return map;
        }

        public static List<ActionBinding> CloneBindings(IReadOnlyList<ActionBinding> source)
        {
            return source.Select(s => new ActionBinding
            {
                Category = s.Category,
                ActionId = s.ActionId,
                ActionLabel = s.ActionLabel,
                IsStub = s.IsStub,
                DefaultChords = s.DefaultChords.ToList(),
                Chords = s.Chords.ToList()
            }).ToList();
        }

        public static string FormatChords(IReadOnlyList<KeyChord> chords)
        {
            if (chords == null || chords.Count == 0)
                return "(unbound)";
            return string.Join(" / ", chords.Select(c => c.ToDisplayString()));
        }

        public static string KeyTokenToDisplay(string keyToken)
        {
            keyToken = NormalizeKeyToken(keyToken);
            return keyToken switch
            {
                "Digit0" => "0",
                "Digit1" => "1",
                "Digit2" => "2",
                "Digit3" => "3",
                "Digit4" => "4",
                "Digit5" => "5",
                "Digit6" => "6",
                "Digit7" => "7",
                "Digit8" => "8",
                "Digit9" => "9",
                "Numpad0" => "Numpad0",
                "Numpad1" => "Numpad1",
                "Numpad2" => "Numpad2",
                "Numpad3" => "Numpad3",
                "Numpad4" => "Numpad4",
                "Numpad5" => "Numpad5",
                "Numpad6" => "Numpad6",
                "Numpad7" => "Numpad7",
                "Numpad8" => "Numpad8",
                "Numpad9" => "Numpad9",
                "Comma" => ",",
                "Period" => ".",
                "Space" => "Space",
                "Backspace" => "Backspace",
                "UpArrow" => "CursorUp",
                "DownArrow" => "CursorDown",
                "LeftArrow" => "CursorLeft",
                "RightArrow" => "CursorRight",
                "Mouse0" => "MouseLeft",
                "Mouse1" => "MouseRight",
                "Mouse2" => "MiddleMouse",
                "LeftBracket" => "[",
                "RightBracket" => "]",
                _ => keyToken
            };
        }

        public static string NormalizeKeyToken(string keyToken)
        {
            if (string.IsNullOrWhiteSpace(keyToken))
                return string.Empty;
            return keyToken.Trim();
        }

        private static List<ActionBinding> BuildDefaultBindings()
        {
            var list = new List<ActionBinding>
            {
                A("Actions","attack","Attack",false,K("Q")),
                A("Actions","aimed_shot","Aimed Shot",false,K("O")),
                A("Actions","bow_special_attack","Bow Special Attack",false,K("Period")),
                A("Actions","brawl","Brawl",false,K("B")),
                A("Actions","burst","Burst",false,K("M")),
                A("Actions","create_reference","Create Reference",true,K("G")),
                A("Actions","dimach","Dimach",false,K("K")),
                A("Actions","fast_attack","Fast Attack",false,K("N")),
                A("Actions","fling_shot","Fling Shot",false,K("L")),
                A("Actions","full_auto","Full Auto",false,K("Comma")),
                A("Actions","look_at","Look At",true,K("T")),
                A("Actions","pickup_item","Pickup Item",true,K("R")),
                A("Actions","reload","Reload",true,K("V")),
                A("Actions","sit","Sit",false,K("X")),
                A("Actions","sneak","Sneak",false,K("H")),
                A("Actions","sneak_attack","Sneak Attack",false,K("J")),
                A("Actions","switch_fight_target","Switch Fight Target",false,K("Tab")),
                A("Actions","use","Use",true,K("E")),

                A("Camera","camera_next_view","Next View",true,K("F8", ctrl: true)),
                A("Camera","camera_previous_view","Previous View",true,K("F8", shift: true)),
                A("Camera","screenshot","Screenshot",false,K("F12", ctrl: true)),
                A("Camera","camera_toggle_view","Toggle 1st/3rd View",true,K("F8")),

                A("Movement","auto_run","Auto Run",true,K("Numpad0")),
                A("Movement","move_back","Back",false,K("S")),
                A("Movement","move_back_global","Back (Global)",true,K("DownArrow")),
                A("Movement","move_forward","Forward",false,K("W"),K("Mouse2")),
                A("Movement","move_forward_global","Forward (Global)",true,K("UpArrow")),
                A("Movement","jump","Jump",false,K("Space")),
                A("Movement","turn_left","Turn Left",false,K("A")),
                A("Movement","turn_right","Turn Right",false,K("D")),
                A("Movement","turn_left_global","Left (Global)",true,K("LeftArrow")),
                A("Movement","turn_right_global","Right (Global)",true,K("RightArrow")),
                A("Movement","strafe_left","Strafe Left",false,K("Z")),
                A("Movement","strafe_right","Strafe Right",false,K("C")),
                A("Movement","toggle_walk","Toggle Walk",false,K("Backspace")),

                A("Shortcuts","shortcut_1","Shortcut 1",false,K("Digit1")),
                A("Shortcuts","shortcut_2","Shortcut 2",false,K("Digit2")),
                A("Shortcuts","shortcut_3","Shortcut 3",false,K("Digit3")),
                A("Shortcuts","shortcut_4","Shortcut 4",false,K("Digit4")),
                A("Shortcuts","shortcut_5","Shortcut 5",false,K("Digit5")),
                A("Shortcuts","shortcut_6","Shortcut 6",false,K("Digit6")),
                A("Shortcuts","shortcut_7","Shortcut 7",false,K("Digit7")),
                A("Shortcuts","shortcut_8","Shortcut 8",false,K("Digit8")),
                A("Shortcuts","shortcut_9","Shortcut 9",false,K("Digit9")),
                A("Shortcuts","shortcut_10","Shortcut 10",false,K("Digit0")),
                A("Shortcuts","select_self","Select Self",false,K("F1")),
                A("Shortcuts","select_team_1","Select Team Member 1",false,K("F2")),
                A("Shortcuts","select_team_2","Select Team Member 2",false,K("F3")),
                A("Shortcuts","select_team_3","Select Team Member 3",false,K("F4")),
                A("Shortcuts","select_team_4","Select Team Member 4",false,K("F5")),
                A("Shortcuts","select_team_5","Select Team Member 5",false,K("F6")),
                A("Shortcuts","copy_to_clipboard","Copy To Clipboard",false,K("C", ctrl: true)),

                A("Windows","equipment_window","Equipment Window",false,K("Digit1", ctrl: true),K("Numpad1", ctrl: true)),
                A("Windows","actions_window","Actions Window",true,K("Digit2", ctrl: true),K("Numpad2", ctrl: true)),
                A("Windows","knowledge_window","Knowledge Window",true,K("Digit3", ctrl: true),K("Numpad3", ctrl: true)),
                A("Windows","missions_window","Missions Window",true,K("Digit4", ctrl: true),K("Numpad4", ctrl: true)),
                A("Windows","team_window","Team Window",true,K("Digit5", ctrl: true),K("Numpad5", ctrl: true)),
                A("Windows","mini_map","Mini Map",true,K("Digit6", ctrl: true),K("Numpad6", ctrl: true)),
                A("Windows","friends_window","Friends Window",true,K("Digit7", ctrl: true),K("Numpad7", ctrl: true)),
                A("Windows","programs_window","Programs Window",false,K("Digit8", ctrl: true),K("Numpad8", ctrl: true)),
                A("Windows","stats_window","Stats Window",false,K("Digit9", ctrl: true),K("Numpad9", ctrl: true)),
                A("Windows","ncu_window","NCU Window",false,K("Digit0", ctrl: true),K("Numpad0", ctrl: true)),
                A("Windows","looking_for_team","Looking For Team Window",true,K("F", ctrl: true)),
                A("Windows","planet_map","Planet Map",true,K("P")),
                A("Windows","raid_window","Raid Window",true,K("R", ctrl: true, shift: true)),
                A("Windows","research_window","Research Window",true,K("O", shift: true)),
                A("Windows","toggle_shortcut_bars","Show/Hide Shortcut Bars",true,K("Y")),
                A("Windows","skills_window","Skills",false,K("U")),
                A("Windows","tradeskill_window","Tradeskill Window",true,K("T", shift: true)),
                A("Windows","perks_window","Perks Window",true,K("P", shift: true)),
                A("Windows","quest_editor_window","Quest Editor",false,K("F11")),
                A("Windows","teleport_window","Teleport",false,K("T", ctrl: true)),
                A("Windows","status_window","Status Window",false,K("LeftBracket", ctrl: true)),
                A("Windows","character_settings_window","Character Settings Window",false,K("RightBracket", ctrl: true)),
                A("Windows","toggle_inventory","Inventory",false,K("I")),
                A("Windows","toggle_item_browser","Item Browser",false,K("F12")),
                A("Windows","toggle_f10","F10 Settings",false,K("F10"))
            };

            foreach (var action in list)
            {
                action.DefaultChords = action.Chords.ToList();
            }

            return list;
        }

        private static ActionBinding A(string category, string id, string label, bool stub, params KeyChord[] chords)
        {
            return new ActionBinding
            {
                Category = category,
                ActionId = id,
                ActionLabel = label,
                IsStub = stub,
                Chords = chords.ToList()
            };
        }

        private static KeyChord K(string keyToken, bool ctrl = false, bool shift = false, bool alt = false)
        {
            return new KeyChord
            {
                Ctrl = ctrl,
                Shift = shift,
                Alt = alt,
                KeyToken = keyToken
            };
        }
    }
}
