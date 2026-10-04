using System;
using System.Collections.Generic;
using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class ChatDamageWindowView : MonoBehaviour
    {
        private static ChatDamageWindowView _activeInstance;
        private static readonly List<ChatDamageWindowView> Instances = new List<ChatDamageWindowView>();
        private static readonly List<string> SystemHistory = new List<string>();
        private static int _systemHistoryVersion;
        public static event Action<string> ChatCommandIssued;
        private sealed class TabState
        {
            public string Id;
            public string Label;
            public bool IsDamage;
            public bool IsReadOnly;
            public Button TabButton;
            public Text TabLabel;
            public RectTransform Panel;
            public RectTransform LogRoot;
            public ScrollRect LogScrollRect;
            public Scrollbar LogScrollbar;
            public InputField SelectableLog;
            public bool AutoFollow = true;
            public bool SuppressScrollEvents;
            public InputField Input;
            public AOStyleUiFactory.WindowRefs DetachedWindow;
            public readonly List<string> Lines = new List<string>();
        }

        private Font _font;
        private PrototypeUiContext _context;
        private RectTransform _floatingParent;
        private RectTransform _bodyHost;
        private readonly Dictionary<string, TabState> _tabs = new Dictionary<string, TabState>(StringComparer.OrdinalIgnoreCase);
        private string _activeTabId = "Global";
        private int _renderedSystemHistoryVersion = -1;

        public void Initialize(PrototypeUiContext context, Font font,
            RectTransform floatingParent)
        {
            if (_context != null)
                _context.StatusChanged -= HandleSystemFeed;
            if (_activeInstance != null && _activeInstance != this
                && _activeInstance._context != null)
                _activeInstance._context.StatusChanged -= _activeInstance.HandleSystemFeed;
            _activeInstance = this;
            if (!Instances.Contains(this)) Instances.Add(this);
            _context = context;
            _font = font;
            _floatingParent = floatingParent;
            Build();
            Seed();
            if (_context != null)
                _context.StatusChanged += HandleSystemFeed;
            RefreshVisuals();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StatusChanged -= HandleSystemFeed;
            if (_activeInstance == this)
                _activeInstance = null;
            Instances.Remove(this);
        }

        private void LateUpdate()
        {
            if (_renderedSystemHistoryVersion != _systemHistoryVersion)
                SynchronizeSystemHistory(forceFollow: false);
            else if (_tabs.TryGetValue("System", out TabState system))
            {
                // Unity applies ContentSizeFitter changes after text assignment.
                // Reconcile at the end of the frame so the outer ScrollRect sees
                // the complete selectable log instead of the InputField's old height.
                EnsureLogLayout(system);
                if (system.AutoFollow)
                    ScrollToBottom(system);
            }
        }

        public static void AppendDamageFeed(string line)
        {
            if (_activeInstance == null || string.IsNullOrWhiteSpace(line))
                return;
            if (_activeInstance._tabs.TryGetValue("Damage", out var damage))
                _activeInstance.AppendLine(damage, $"[Damage] {line}");
        }

        public static void AppendSystemFeed(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            string clean = line.Trim();
            SystemHistory.Add(clean);
            while (SystemHistory.Count > 200)
                SystemHistory.RemoveAt(0);
            _systemHistoryVersion++;
            for (int i = Instances.Count - 1; i >= 0; i--)
            {
                ChatDamageWindowView instance = Instances[i];
                if (instance == null) { Instances.RemoveAt(i); continue; }
                instance.SynchronizeSystemHistory(forceFollow: false);
            }
        }

        private void HandleSystemFeed(string line) => AppendSystemFeed(line);

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("ChatRoot", transform, new Color(0.07f, 0.1f, 0.15f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var tabs = AOStyleUiFactory.CreatePanel("Tabs", root, new Color(0.11f, 0.16f, 0.22f, 0.98f));
            tabs.anchorMin = new Vector2(0f, 1f);
            tabs.anchorMax = new Vector2(1f, 1f);
            tabs.offsetMin = new Vector2(4f, -26f);
            tabs.offsetMax = new Vector2(-4f, -4f);
            var tabsLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 4f;
            tabsLayout.padding = new RectOffset(2, 2, 2, 2);
            tabsLayout.childControlWidth = false;
            tabsLayout.childControlHeight = true;
            tabsLayout.childForceExpandWidth = false;

            _bodyHost = AOStyleUiFactory.CreatePanel("BodyHost", root, new Color(0f, 0f, 0f, 0f));
            _bodyHost.anchorMin = new Vector2(0f, 0f);
            _bodyHost.anchorMax = new Vector2(1f, 1f);
            _bodyHost.offsetMin = new Vector2(4f, 4f);
            _bodyHost.offsetMax = new Vector2(-4f, -28f);

            CreateTab(tabs, "Global", isDamage: false);
            CreateTab(tabs, "Vicinity", isDamage: false);
            CreateTab(tabs, "Team", isDamage: false);
            CreateTab(tabs, "Tells", isDamage: false);
            CreateTab(tabs, "Damage", isDamage: true);
            CreateTab(tabs, "System", isDamage: false, isReadOnly: true);
        }

        private void CreateTab(RectTransform tabsRow, string label, bool isDamage,
            bool isReadOnly = false)
        {
            var state = new TabState
            {
                Id = label,
                Label = label,
                IsDamage = isDamage,
                IsReadOnly = isReadOnly
            };

            state.TabButton = AOStyleUiFactory.CreateButton(
                "Tab_" + label,
                tabsRow,
                label,
                _font,
                () => { _activeTabId = label; RefreshVisuals(); },
                82f);
            state.TabLabel = state.TabButton.GetComponentInChildren<Text>();
            AddRightClick(state.TabButton, () => ToggleDetach(state));

            state.Panel = AOStyleUiFactory.CreatePanel("Panel_" + label, _bodyHost, new Color(0.06f, 0.09f, 0.14f, 0.98f));
            state.Panel.anchorMin = Vector2.zero;
            state.Panel.anchorMax = Vector2.one;
            state.Panel.offsetMin = Vector2.zero;
            state.Panel.offsetMax = Vector2.zero;

            var logHost = AOStyleUiFactory.CreatePanel("LogHost", state.Panel, new Color(0.05f, 0.07f, 0.1f, 0.98f));
            logHost.anchorMin = new Vector2(0f, 0f);
            logHost.anchorMax = new Vector2(1f, 1f);
            logHost.offsetMin = new Vector2(4f, isDamage || isReadOnly ? 4f : 30f);
            logHost.offsetMax = new Vector2(-4f, -4f);
            state.LogRoot = AOStyleUiFactory.CreateScrollContent(logHost);
            state.LogScrollRect = state.LogRoot.GetComponentInParent<ScrollRect>();
            ConfigureLogScrolling(state, logHost);
            CreateSelectableLog(state);

            if (!isDamage && !isReadOnly)
            {
                var inputHost = AOStyleUiFactory.CreatePanel("InputHost", state.Panel, new Color(0f, 0f, 0f, 0f));
                inputHost.anchorMin = new Vector2(0f, 0f);
                inputHost.anchorMax = new Vector2(1f, 0f);
                inputHost.offsetMin = new Vector2(4f, 4f);
                inputHost.offsetMax = new Vector2(-4f, 28f);

                state.Input = AOStyleUiFactory.CreateInputField("Input_" + label, inputHost, $"Type in {label}...", _font, 999f);
                var inputRt = (RectTransform)state.Input.transform;
                inputRt.anchorMin = Vector2.zero;
                inputRt.anchorMax = Vector2.one;
                inputRt.offsetMin = Vector2.zero;
                inputRt.offsetMax = Vector2.zero;
                state.Input.onEndEdit.AddListener(value =>
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return;
                    string trimmed = value.Trim();
                    if (TryHandleCommand(trimmed))
                    {
                        state.Input.text = string.Empty;
                        state.Input.ActivateInputField();
                        return;
                    }

                    AppendLine(state, $"[You/{label}] {trimmed}");
                    state.Input.text = string.Empty;
                    state.Input.ActivateInputField();
                });
            }

            _tabs[state.Id] = state;
        }

        private static bool TryHandleCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (text[0] != '/' && text[0] != '.')
                return false;

            string command = text.Substring(1).Trim();
            if (string.IsNullOrWhiteSpace(command))
                return true;

            ChatCommandIssued?.Invoke(command);
            return true;
        }

        private void ToggleDetach(TabState state)
        {
            if (state == null || _floatingParent == null)
                return;

            if (state.DetachedWindow?.Root != null)
            {
                Reattach(state);
                return;
            }

            state.DetachedWindow = AOStyleUiFactory.CreateWindow(_floatingParent, _font, $"{state.Label} Chat", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            state.DetachedWindow.Root.sizeDelta = new Vector2(420f, 260f);
            state.DetachedWindow.Root.anchoredPosition = new Vector2(0f, -40f);
            state.Panel.SetParent(state.DetachedWindow.Content, false);
            state.Panel.anchorMin = Vector2.zero;
            state.Panel.anchorMax = Vector2.one;
            state.Panel.offsetMin = Vector2.zero;
            state.Panel.offsetMax = Vector2.zero;
            state.DetachedWindow.Root.gameObject.SetActive(true);
            state.DetachedWindow.Root.SetAsLastSibling();

            if (state.DetachedWindow.CloseButton != null)
            {
                state.DetachedWindow.CloseButton.onClick.RemoveAllListeners();
                state.DetachedWindow.CloseButton.onClick.AddListener(() => Reattach(state));
            }

            if (string.Equals(_activeTabId, state.Id, StringComparison.OrdinalIgnoreCase))
                _activeTabId = "Global";

            RefreshVisuals();
            RefreshLog(state, forceFollow: true);
        }

        private void Reattach(TabState state)
        {
            if (state == null || _bodyHost == null)
                return;

            state.Panel.SetParent(_bodyHost, false);
            state.Panel.anchorMin = Vector2.zero;
            state.Panel.anchorMax = Vector2.one;
            state.Panel.offsetMin = Vector2.zero;
            state.Panel.offsetMax = Vector2.zero;

            if (state.DetachedWindow?.Root != null)
                Destroy(state.DetachedWindow.Root.gameObject);
            state.DetachedWindow = null;
            _activeTabId = state.Id;
            RefreshVisuals();
            RefreshLog(state, forceFollow: true);
        }

        private void RefreshVisuals()
        {
            foreach (var kvp in _tabs)
            {
                var tab = kvp.Value;
                bool detached = tab.DetachedWindow?.Root != null;
                bool active = !detached && string.Equals(tab.Id, _activeTabId, StringComparison.OrdinalIgnoreCase);
                if (tab.Panel != null)
                    tab.Panel.gameObject.SetActive(active || detached);
                var image = tab.TabButton != null ? tab.TabButton.GetComponent<Image>() : null;
                if (image != null)
                    image.color = detached
                        ? new Color(0.3f, 0.24f, 0.12f, 0.95f)
                        : (active ? new Color(0.26f, 0.41f, 0.58f, 0.98f) : new Color(0.2f, 0.28f, 0.4f, 0.95f));
                if (tab.TabLabel != null)
                    tab.TabLabel.text = detached ? $"{tab.Label}*" : tab.Label;
            }
        }

        private void Seed()
        {
            if (_tabs.TryGetValue("Global", out var global))
                AppendLine(global, "[System] Global channel ready.");
            if (_tabs.TryGetValue("Vicinity", out var vicinity))
                AppendLine(vicinity, "[System] Vicinity channel ready.");
            if (_tabs.TryGetValue("Team", out var team))
                AppendLine(team, "[System] Team channel ready.");
            if (_tabs.TryGetValue("Tells", out var tells))
                AppendLine(tells, "[System] Tells channel ready.");
            if (_tabs.TryGetValue("Damage", out var damage))
                AppendLine(damage, "[Damage] Damage feed ready.");
            if (_tabs.TryGetValue("System", out var system))
            {
                SynchronizeSystemHistory(forceFollow: true);
            }
        }

        private void SynchronizeSystemHistory(bool forceFollow)
        {
            if (!_tabs.TryGetValue("System", out TabState system)
                || system.SelectableLog == null)
                return;
            system.Lines.Clear();
            system.Lines.Add("[System] Status feed ready.");
            for (int i = 0; i < SystemHistory.Count; i++)
                system.Lines.Add($"[System] {SystemHistory[i]}");
            _renderedSystemHistoryVersion = _systemHistoryVersion;
            RefreshLog(system, forceFollow);
        }

        private void AppendLine(TabState tab, string line, bool forceFollow = false)
        {
            if (tab == null || tab.LogRoot == null)
                return;

            bool shouldFollow = forceFollow || tab.AutoFollow || IsNearBottom(tab);
            tab.Lines.Add(line);
            while (tab.Lines.Count > 200)
                tab.Lines.RemoveAt(0);

            if (tab.SelectableLog == null)
                CreateSelectableLog(tab);
            UpdateSelectableLog(tab);
            Text logText = tab.SelectableLog.textComponent;
            var layout = tab.SelectableLog.GetComponent<LayoutElement>();
            if (layout != null && logText != null)
                layout.preferredHeight = Mathf.Max(18f, logText.preferredHeight + 8f);

            LayoutRebuilder.ForceRebuildLayoutImmediate(tab.LogRoot);
            if (shouldFollow)
            {
                ScrollToBottom(tab);
                tab.AutoFollow = true;
            }
        }

        private void RefreshLog(TabState tab, bool forceFollow)
        {
            if (tab?.SelectableLog == null) return;
            UpdateSelectableLog(tab);
            EnsureLogLayout(tab);
            if (tab.Panel != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(tab.Panel);
            if (forceFollow)
            {
                tab.AutoFollow = true;
                ScrollToBottom(tab);
            }
        }

        private static void EnsureLogLayout(TabState tab)
        {
            if (tab?.SelectableLog == null || tab.LogRoot == null) return;
            Text logText = tab.SelectableLog.textComponent;
            LayoutElement layout = tab.SelectableLog.GetComponent<LayoutElement>();
            if (layout != null && logText != null)
            {
                float height = Mathf.Max(18f, logText.preferredHeight + 8f);
                layout.minHeight = height;
                layout.preferredHeight = height;
                ((RectTransform)tab.SelectableLog.transform).SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical, height);
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tab.LogRoot);
        }

        private void CreateSelectableLog(TabState tab)
        {
            if (tab == null || tab.LogRoot == null || tab.SelectableLog != null) return;
            InputField field = AOStyleUiFactory.CreateInputField(
                "SelectableLog", tab.LogRoot, string.Empty, _font, 999f);
            field.readOnly = true;
            field.lineType = InputField.LineType.MultiLineNewline;
            field.navigation = Navigation.defaultNavigation;
            Image background = field.GetComponent<Image>();
            if (background != null) background.color = Color.clear;
            Text text = field.textComponent;
            if (text != null)
            {
                text.fontSize = 11;
                text.alignment = TextAnchor.UpperLeft;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = tab.IsDamage
                    ? new Color(1f, 0.82f, 0.6f, 1f)
                    : new Color(0.85f, 0.93f, 1f, 1f);
            }
            tab.SelectableLog = field;
        }

        private static void UpdateSelectableLog(TabState tab)
        {
            if (tab?.SelectableLog == null) return;
            tab.SelectableLog.SetTextWithoutNotify(string.Join("\n", tab.Lines));
            tab.SelectableLog.ForceLabelUpdate();
            Text label = tab.SelectableLog.textComponent;
            if (label == null) return;
            // Re-enabling the Graphic invalidates the cached canvas geometry that
            // otherwise refreshes only when the panel is undocked/reparented.
            label.enabled = false;
            label.enabled = true;
            label.SetAllDirty();
        }

        private void ConfigureLogScrolling(TabState tab, RectTransform logHost)
        {
            if (tab?.LogScrollRect == null || logHost == null)
                return;

            var barGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barGo.transform.SetParent(logHost, false);
            var barRt = (RectTransform)barGo.transform;
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 1f);
            barRt.offsetMin = new Vector2(-12f, 2f);
            barRt.offsetMax = new Vector2(-2f, -2f);
            barGo.GetComponent<Image>().color = new Color(0.18f, 0.24f, 0.34f, 0.95f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(barGo.transform, false);
            var handleRt = (RectTransform)handleGo.transform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            handleGo.GetComponent<Image>().color = new Color(0.62f, 0.79f, 0.95f, 0.95f);

            var scrollbar = barGo.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handleGo.GetComponent<Image>();
            scrollbar.handleRect = handleRt;
            tab.LogScrollbar = scrollbar;

            tab.LogScrollRect.verticalScrollbar = scrollbar;
            tab.LogScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            tab.LogScrollRect.onValueChanged.AddListener(_ =>
            {
                if (tab.SuppressScrollEvents)
                    return;
                tab.AutoFollow = IsNearBottom(tab);
            });
        }

        private static bool IsNearBottom(TabState tab)
        {
            if (tab?.LogScrollRect == null)
                return true;
            return tab.LogScrollRect.verticalNormalizedPosition <= 0.02f;
        }

        private static void ScrollToBottom(TabState tab)
        {
            if (tab?.LogScrollRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            tab.SuppressScrollEvents = true;
            tab.LogScrollRect.verticalNormalizedPosition = 0f;
            tab.SuppressScrollEvents = false;
        }

        private static void AddRightClick(Button button, Action onRightClick)
        {
            if (button == null || onRightClick == null)
                return;

            var trigger = button.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(evt =>
            {
                if (evt is PointerEventData ped && ped.button == PointerEventData.InputButton.Right)
                    onRightClick();
            });
            trigger.triggers.Add(entry);
        }
    }
}
