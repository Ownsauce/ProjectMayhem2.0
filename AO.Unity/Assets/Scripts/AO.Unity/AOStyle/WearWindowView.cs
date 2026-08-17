using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AO.Unity.Prototype;
using AO.Core.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class WearWindowView : MonoBehaviour
    {
        private enum WearTab
        {
            Weapon,
            Armor,
            Implant,
            Social
        }

        private sealed class WearSlotDef
        {
            public int SlotId;
            public string Label;
            public WearTab Tab;
        }

        private sealed class RawSlotDef
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class SlotLookupFile
        {
            public Dictionary<string, string> namesById { get; set; } = new();
        }

        private static readonly IReadOnlyList<WearSlotDef> Empty = Array.Empty<WearSlotDef>();

        private PrototypeUiContext _context;
        private Font _font;
        private Canvas _canvas;
        private RectTransform _grid;
        private WearTab _activeTab = WearTab.Weapon;
        private readonly Dictionary<WearTab, IReadOnlyList<WearSlotDef>> _slotsByTab = new();
        private readonly Dictionary<WearTab, Button> _tabButtons = new();
        private readonly Dictionary<WearTab, Text> _tabLabels = new();

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _font = font;
            _canvas = GetComponentInParent<Canvas>();
            LoadSlots();
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
            var root = AOStyleUiFactory.CreatePanel("WearRoot", transform, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var tabs = AOStyleUiFactory.CreatePanel("Tabs", root, new Color(0f, 0f, 0f, 0f));
            tabs.anchorMin = new Vector2(0f, 1f);
            tabs.anchorMax = new Vector2(1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.offsetMin = new Vector2(4f, -24f);
            tabs.offsetMax = new Vector2(-4f, -2f);
            var tabsLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 2;
            tabsLayout.childControlHeight = true;
            tabsLayout.childControlWidth = true;
            tabsLayout.childForceExpandWidth = true;

            CreateTabButton(tabs, "Weap", WearTab.Weapon);
            CreateTabButton(tabs, "Arm", WearTab.Armor);
            CreateTabButton(tabs, "Imp", WearTab.Implant);
            CreateTabButton(tabs, "Soc", WearTab.Social);
            RefreshTabVisuals();

            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGo.transform.SetParent(root, false);
            _grid = (RectTransform)gridGo.transform;
            _grid.anchorMin = Vector2.zero;
            _grid.anchorMax = Vector2.one;
            _grid.offsetMin = new Vector2(6f, 6f);
            _grid.offsetMax = new Vector2(-6f, -28f);

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(54f, 54f);
            grid.spacing = new Vector2(4f, 4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
        }

        private void CreateTabButton(RectTransform parent, string label, WearTab tab)
        {
            var button = AOStyleUiFactory.CreateButton("Tab_" + label, parent, label, _font, () =>
            {
                _activeTab = tab;
                RefreshTabVisuals();
                Refresh();
            }, 36f);

            _tabButtons[tab] = button;
            var labelText = button.GetComponentInChildren<Text>();
            if (labelText != null)
                _tabLabels[tab] = labelText;
        }

        private void RefreshTabVisuals()
        {
            foreach (var kvp in _tabButtons)
            {
                var tab = kvp.Key;
                var button = kvp.Value;
                if (button == null)
                    continue;

                bool active = tab == _activeTab;
                var bg = button.GetComponent<Image>();
                if (bg != null)
                    bg.color = active
                        ? new Color(0.12f, 0.54f, 0.82f, 0.98f)
                        : new Color(0.16f, 0.22f, 0.32f, 0.92f);

                var colors = button.colors;
                if (active)
                {
                    colors.normalColor = new Color(0.12f, 0.54f, 0.82f, 0.98f);
                    colors.highlightedColor = new Color(0.16f, 0.62f, 0.92f, 1f);
                    colors.pressedColor = new Color(0.1f, 0.46f, 0.72f, 1f);
                }
                else
                {
                    colors.normalColor = new Color(0.16f, 0.22f, 0.32f, 0.92f);
                    colors.highlightedColor = new Color(0.21f, 0.29f, 0.41f, 0.98f);
                    colors.pressedColor = new Color(0.14f, 0.2f, 0.29f, 0.98f);
                }
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.12f, 0.14f, 0.18f, 0.8f);
                button.colors = colors;

                if (_tabLabels.TryGetValue(tab, out var label) && label != null)
                {
                    label.color = active ? new Color(0.95f, 0.98f, 1f, 1f) : new Color(0.82f, 0.9f, 1f, 0.95f);
                    label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private IReadOnlyList<WearSlotDef> GetVisibleSlots()
        {
            return _slotsByTab.TryGetValue(_activeTab, out var slots) ? slots : Empty;
        }

        private void Refresh()
        {
            if (_grid == null || _context == null) return;
            AOStyleUiFactoryCleanup.Clear(_grid);

            var equipped = _activeTab == WearTab.Social
                ? _context.GetSocialEquipped()
                : _context.GetEquipped();
            var visible = GetVisibleSlots();
            int totalSlots = Mathf.Max(9, ((visible.Count + 2) / 3) * 3);

            for (int i = 0; i < totalSlots; i++)
            {
                if (i >= visible.Count)
                {
                    AOStyleUiFactory.CreatePanel("EmptySlot", _grid, new Color(0.09f, 0.11f, 0.15f, 1f));
                    continue;
                }

                var def = visible[i];
                if (def.SlotId <= 0)
                {
                    AOStyleUiFactory.CreatePanel("EmptySlot", _grid, new Color(0.09f, 0.11f, 0.15f, 1f));
                    continue;
                }

                var slot = AOStyleUiFactory.CreatePanel("WearSlot", _grid, new Color(0.12f, 0.15f, 0.2f, 1f));
                var drop = slot.gameObject.AddComponent<WearDropTarget>();
                drop.Context = _context;
                drop.SlotId = def.SlotId;
                drop.IsSocial = def.Tab == WearTab.Social;

                if (equipped.TryGetValue(def.SlotId, out var instanceId))
                {
                    var dataItem = _context.GetDataItem(instanceId);
                    if (!IsItemVisibleForTab(dataItem))
                        goto DrawLabel;

                    var sprite = dataItem != null ? _context.GetIconForData(dataItem) : null;

                    var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    iconObj.transform.SetParent(slot, false);
                    var iconRt = (RectTransform)iconObj.transform;
                    iconRt.anchorMin = new Vector2(0.08f, 0.26f);
                    iconRt.anchorMax = new Vector2(0.92f, 0.92f);
                    iconRt.offsetMin = Vector2.zero;
                    iconRt.offsetMax = Vector2.zero;
                    var icon = iconObj.GetComponent<Image>();
                    icon.sprite = sprite;
                    icon.color = sprite == null ? new Color(0.2f, 0.2f, 0.2f, 1f) : Color.white;

                    var click = slot.gameObject.AddComponent<WearSlotPointerClick>();
                    click.Context = _context;
                    click.SlotId = def.SlotId;
                    click.InstanceId = instanceId;
                    click.IsSocial = def.Tab == WearTab.Social;

                    if (_canvas == null)
                        _canvas = GetComponentInParent<Canvas>();

                    if (_canvas != null)
                    {
                        click.Canvas = _canvas;
                        click.Icon = icon;

                        var hover = slot.gameObject.AddComponent<ItemHoverTooltip>();
                        hover.Context = _context;
                        hover.Canvas = _canvas;
                        hover.Font = _font;
                        hover.InstanceId = instanceId;
                    }
                }

            DrawLabel:
                var lbl = AOStyleUiFactory.CreateText("Lbl", slot, def.Label, _font, 10, TextAnchor.LowerCenter);
                var lblRt = (RectTransform)lbl.transform;
                lblRt.anchorMin = new Vector2(0f, 0f);
                lblRt.anchorMax = new Vector2(1f, 0.24f);
                lblRt.offsetMin = Vector2.zero;
                lblRt.offsetMax = Vector2.zero;
            }
        }

        private bool IsItemVisibleForTab(AO.Data.Core.ItemInstance dataItem)
        {
            if (dataItem?.Definition == null)
                return false;

            int classId = dataItem.Definition.StatModifiers?
                .Where(m => m.StatId == 76)
                .Select(m => m.Value)
                .FirstOrDefault() ?? 0;

            if (classId == 0)
            {
                var raw = AO.Data.Unity.AODataManager.Instance?.Items?
                    .FirstOrDefault(i => i != null && i.AOID == dataItem.DefinitionId);
                if (raw?.StatValues != null)
                    classId = raw.StatValues.FirstOrDefault(v => v != null && v.Stat == 76)?.RawValue ?? 0;
            }

            if (classId == 0)
                classId = dataItem.Definition.SlotType;

            var itemClass = (ItemClass)classId;
            return _activeTab switch
            {
                WearTab.Weapon => itemClass == ItemClass.Weapon,
                WearTab.Armor => itemClass == ItemClass.Armor,
                WearTab.Implant => itemClass == ItemClass.Implant,
                WearTab.Social => itemClass == ItemClass.Armor || itemClass == ItemClass.Weapon,
                _ => false
            };
        }

        private void LoadSlots()
        {
            _slotsByTab[WearTab.Weapon] = LoadWeaponSlots();
            _slotsByTab[WearTab.Armor] = LoadArmorSlots();
            _slotsByTab[WearTab.Implant] = LoadImplantSlots();
            _slotsByTab[WearTab.Social] = LoadSocialSlots();
        }

        private IReadOnlyList<WearSlotDef> LoadWeaponSlots()
        {
            var raw = LoadRawSlots("weapon_slots.json");
            if (raw == null || raw.Count == 0)
                return Empty;

            // Preserve AO-like panel flow with Hud2 near other HUD slots.
            var order = new Dictionary<int, int>
            {
                [1] = 0,   // Hud1
                [15] = 1,  // Hud2
                [2] = 2,   // Hud3
                [3] = 3,   // Utils1
                [4] = 4,   // Utils2
                [5] = 5,   // Utils3
                [6] = 6,   // RightHand
                [7] = 7,   // Deck
                [8] = 8,   // LeftHand
                [9] = 9,   // Deck1
                [10] = 10, // Deck2
                [11] = 11, // Deck3
                [12] = 12, // Deck4
                [13] = 13, // Deck5
                [14] = 14, // Deck6
            };

            return raw
                .Where(slot => slot.Id > 0)
                .OrderBy(slot => order.TryGetValue(slot.Id, out var rank) ? rank : 1000 + slot.Id)
                .Select(slot => new WearSlotDef
                {
                    SlotId = (slot.Id == 6 || slot.Id == 8) ? slot.Id : 1000 + slot.Id,
                    Label = FormatSlotLabel(slot.Name),
                    Tab = WearTab.Weapon
                })
                .ToArray();
        }

        private IReadOnlyList<WearSlotDef> LoadSlotsFromFile(string fileName, WearTab tab, Func<RawSlotDef, bool> include, Func<int, int> slotIdMap = null)
        {
            var raw = LoadRawSlots(fileName);
            if (raw == null || raw.Count == 0)
                return Empty;

            return raw
                .Where(include)
                .Select(slot => new WearSlotDef
                {
                    SlotId = slotIdMap != null ? slotIdMap(slot.Id) : slot.Id,
                    Label = FormatSlotLabel(slot.Name),
                    Tab = tab
                })
                .OrderBy(slot => slot.SlotId)
                .ToArray();
        }

        private IReadOnlyList<WearSlotDef> LoadImplantSlots()
        {
            var raw = LoadRawSlots("implant_slots.json");
            if (raw == null || raw.Count == 0)
                return Empty;

            var ordered = raw
                .Where(slot => slot.Id > 0 && slot.Id <= 13)
                .OrderBy(slot => slot.Id)
                .Select(slot => new WearSlotDef
                {
                    SlotId = 100 + slot.Id,
                    Label = FormatSlotLabel(slot.Name),
                    Tab = WearTab.Implant
                })
                .ToList();

            int feetIndex = ordered.FindIndex(slot => string.Equals(slot.Label, "Feet", StringComparison.OrdinalIgnoreCase));
            if (feetIndex >= 0)
            {
                var feet = ordered[feetIndex];
                ordered.RemoveAt(feetIndex);
                ordered.Add(new WearSlotDef { SlotId = 0, Label = string.Empty, Tab = WearTab.Implant });
                ordered.Add(feet);
            }

            return ordered;
        }

        private IReadOnlyList<WearSlotDef> LoadArmorSlots()
        {
            var raw = LoadRawSlots("armor_slots.json");
            if (raw == null || raw.Count == 0)
                return Empty;

            // Exact AO armor tab layout:
            // Neck, Head, Back
            // Right Shoulder, Chest, Left Shoulder
            // Right Arm, Hands, Left Arm
            // Right Wrist, Legs, Left Wrist
            // Right Finger, Feet, Left Finger
            var order = new Dictionary<int, int>
            {
                [1] = 0, [2] = 1, [3] = 2,
                [4] = 3, [5] = 4, [6] = 5,
                [7] = 6, [8] = 7, [9] = 8,
                [10] = 9, [11] = 10, [12] = 11,
                [13] = 12, [14] = 13, [15] = 14
            };

            return raw
                .Where(slot => slot.Id > 0 && slot.Id <= 15)
                .OrderBy(slot => order.TryGetValue(slot.Id, out var rank) ? rank : 1000 + slot.Id)
                .Select(slot => new WearSlotDef
                {
                    SlotId = (slot.Id == 6 || slot.Id == 8) ? 3000 + slot.Id : slot.Id,
                    Label = FormatSlotLabel(slot.Name),
                    Tab = WearTab.Armor
                })
                .ToArray();
        }

        private IReadOnlyList<WearSlotDef> LoadSocialSlots()
        {
            var raw = LoadRawSlots("armor_slots.json");
            if (raw == null || raw.Count == 0)
                return Empty;

            var list = new List<WearSlotDef>();
            foreach (var slot in raw.Where(slot => slot.Id > 0 && slot.Id <= 15).OrderBy(slot => slot.Id))
            {
                int slotId = PrototypeUiContext.ToSocialSlotId(slot.Id);
                string label = FormatSlotLabel(slot.Name);

                if (slot.Id == 13)
                {
                    label = "Right Hand";
                }
                else if (slot.Id == 15)
                {
                    label = "Left Hand";
                }

                list.Add(new WearSlotDef
                {
                    SlotId = slotId,
                    Label = label,
                    Tab = WearTab.Social
                });
            }

            return list;
        }

        private static List<RawSlotDef> LoadRawSlots(string fileName)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "AOData", fileName);
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);

            // Supports both legacy array format and new lookup-object format.
            try
            {
                var list = JsonConvert.DeserializeObject<List<RawSlotDef>>(json);
                if (list != null && list.Count > 0)
                    return list;
            }
            catch
            {
                // Ignore and try lookup-object format below.
            }

            var lookup = JsonConvert.DeserializeObject<SlotLookupFile>(json);
            if (lookup?.namesById == null || lookup.namesById.Count == 0)
                return new List<RawSlotDef>();

            var converted = new List<RawSlotDef>();
            foreach (var pair in lookup.namesById)
            {
                if (!int.TryParse(pair.Key, out int id))
                    continue;

                converted.Add(new RawSlotDef
                {
                    Id = id,
                    Name = pair.Value
                });
            }

            return converted;
        }

        private static string FormatSlotLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Slot";

            var chars = new List<char>(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(raw[i - 1]))
                    chars.Add(' ');
                chars.Add(c);
            }

            return new string(chars.ToArray());
        }
    }

    public class WearDropTarget : MonoBehaviour, IDropHandler, IPointerClickHandler
    {
        public PrototypeUiContext Context;
        public int SlotId;
        public bool IsSocial;

        public void OnDrop(PointerEventData eventData)
        {
            if (Context == null) return;
            if (!ItemDragPayload.IsDragging || ItemDragPayload.InstanceId == 0) return;
            bool equipped;
            if (IsSocial)
            {
                equipped = Context.TryEquipSocialByInstanceId(
                    ItemDragPayload.InstanceId,
                    SlotId,
                    sourceZone: ItemDragPayload.FromEquipment ? null : ItemDragPayload.SourceZone,
                    sourceIndex: ItemDragPayload.FromEquipment ? -1 : ItemDragPayload.SourceIndex);
            }
            else
            {
                equipped = Context.TryEquipByInstanceId(
                    ItemDragPayload.InstanceId,
                    SlotId,
                    strictPreferredSlot: true,
                    sourceZone: ItemDragPayload.FromEquipment ? null : ItemDragPayload.SourceZone,
                    sourceIndex: ItemDragPayload.FromEquipment ? -1 : ItemDragPayload.SourceIndex);
            }
            if (equipped)
                InventoryDragSource.ForceEndDragVisual();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Context == null)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (ItemDragPayload.IsDragging)
                    InventoryDragSource.ForceEndDragVisual();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!ItemDragPayload.IsDragging || ItemDragPayload.InstanceId == 0)
                return;

            bool equipped = IsSocial
                ? Context.TryEquipSocialByInstanceId(
                    ItemDragPayload.InstanceId,
                    SlotId,
                    sourceZone: ItemDragPayload.FromEquipment ? null : ItemDragPayload.SourceZone,
                    sourceIndex: ItemDragPayload.FromEquipment ? -1 : ItemDragPayload.SourceIndex)
                : Context.TryEquipByInstanceId(
                    ItemDragPayload.InstanceId,
                    SlotId,
                    strictPreferredSlot: true,
                    sourceZone: ItemDragPayload.FromEquipment ? null : ItemDragPayload.SourceZone,
                    sourceIndex: ItemDragPayload.FromEquipment ? -1 : ItemDragPayload.SourceIndex);

            if (equipped)
                InventoryDragSource.ForceEndDragVisual();
        }
    }

    public class WearSlotPointerClick : MonoBehaviour, IPointerClickHandler
    {
        public PrototypeUiContext Context;
        public int SlotId;
        public long InstanceId;
        public Image Icon;
        public Canvas Canvas;
        public bool IsSocial;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Context == null || InstanceId == 0)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (ItemDragPayload.IsDragging)
                {
                    InventoryDragSource.ForceEndDragVisual();
                    return;
                }

                if (IsSocial)
                    Context.UnequipSocialSlot(SlotId);
                else
                    Context.UnequipSlot(SlotId);
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (ItemDragPayload.IsDragging && ItemDragPayload.InstanceId != 0)
            {
                bool equipped = IsSocial
                    ? Context.TryEquipSocialByInstanceId(
                        ItemDragPayload.InstanceId,
                        SlotId,
                        sourceZone: ItemDragPayload.FromEquipment ? null : ItemDragPayload.SourceZone,
                        sourceIndex: ItemDragPayload.FromEquipment ? -1 : ItemDragPayload.SourceIndex)
                    : Context.TryEquipByInstanceId(
                        ItemDragPayload.InstanceId,
                        SlotId,
                        strictPreferredSlot: true,
                        sourceZone: ItemDragPayload.FromEquipment ? null : ItemDragPayload.SourceZone,
                        sourceIndex: ItemDragPayload.FromEquipment ? -1 : ItemDragPayload.SourceIndex);

                if (equipped)
                    InventoryDragSource.ForceEndDragVisual();
                return;
            }

            if (Canvas == null)
                return;

            if (IsSocial)
                return;

            InventoryDragSource.BeginEquippedClickPickup(InstanceId, Icon != null ? Icon.sprite : null, Canvas, SlotId);
        }
    }
}
