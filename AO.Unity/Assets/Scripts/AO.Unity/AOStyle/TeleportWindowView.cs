using System;
using System.Collections.Generic;
using System.IO;
using AO.Unity.Prototype;
using AO.Unity.World;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class TeleportWindowView : MonoBehaviour
    {
        private sealed class PlayfieldListEntry
        {
            public int PlayfieldId { get; set; }
            public string Name { get; set; }
        }

        private sealed class PlayfieldFileHeader
        {
            public int PlayfieldId { get; set; }
            public string Name { get; set; }
        }

        private Font _font;
        private Dropdown _playfieldDropdown;
        private InputField _playfieldIdInput;
        private InputField _coordXInput;
        private InputField _coordYInput;
        private InputField _coordZInput;
        private InputField _statusField;
        private readonly List<PlayfieldListEntry> _playfields = new();
        private bool _suppressSelectionEvents;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _font = font;
            Build();
            ReloadPlayfields();
        }

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("TeleportRoot", transform, new Color(0f, 0f, 0f, 0f));
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

            var help = AOStyleUiFactory.CreateText(
                "Help",
                root,
                "Pick a playfield or type an ID, then optionally enter AO X/Y/Z coordinates for an exact teleport.",
                _font,
                12,
                TextAnchor.MiddleLeft);
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            var dropdownRow = CreateRow(root, 26f);
            AOStyleUiFactory.CreateText("DropdownLabel", dropdownRow, "Playfield", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;
            _playfieldDropdown = CreateDropdown("PlayfieldDropdown", dropdownRow, 360f);
            _playfieldDropdown.onValueChanged.AddListener(OnDropdownChanged);

            var idRow = CreateRow(root, 26f);
            AOStyleUiFactory.CreateText("IdLabel", idRow, "PF ID", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;
            _playfieldIdInput = AOStyleUiFactory.CreateInputField("PlayfieldIdInput", idRow, "800", _font, 120f);

            var coordsRow = CreateRow(root, 26f);
            AOStyleUiFactory.CreateText("CoordsLabel", coordsRow, "AO XYZ", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;
            _coordXInput = AOStyleUiFactory.CreateInputField("CoordXInput", coordsRow, "X", _font, 96f);
            _coordYInput = AOStyleUiFactory.CreateInputField("CoordYInput", coordsRow, "Y", _font, 96f);
            _coordZInput = AOStyleUiFactory.CreateInputField("CoordZInput", coordsRow, "Z", _font, 96f);
            ConfigureCoordinateInput(_coordXInput);
            ConfigureCoordinateInput(_coordYInput);
            ConfigureCoordinateInput(_coordZInput);

            var actionsRow = CreateRow(root, 28f);
            AOStyleUiFactory.CreateButton("ReloadPlayfields", actionsRow, "Refresh", _font, ReloadPlayfields, 90f);
            AOStyleUiFactory.CreateButton("TeleportButton", actionsRow, "Teleport", _font, TeleportToSelection, 110f);

            _statusField = AOStyleUiFactory.CreateInputField("TeleportStatus", root, "Teleport status...", _font, 0f);
            var statusLe = _statusField.gameObject.GetComponent<LayoutElement>();
            if (statusLe == null)
                statusLe = _statusField.gameObject.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 52f;
            statusLe.flexibleWidth = 1f;
            _statusField.readOnly = true;
            _statusField.lineType = InputField.LineType.MultiLineNewline;
            _statusField.textComponent.alignment = TextAnchor.UpperLeft;
            _statusField.textComponent.color = new Color(0.85f, 0.92f, 1f, 0.95f);
            _statusField.placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        }

        private static RectTransform CreateRow(Transform parent, float preferredHeight)
        {
            var row = AOStyleUiFactory.CreatePanel("Row", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childControlWidth = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = false;
            return row;
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
            template.sizeDelta = new Vector2(0f, 260f);
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

        private void ReloadPlayfields()
        {
            _playfields.Clear();

            string root = Path.Combine(Application.streamingAssetsPath, "AOData", "Playfields");
            if (!Directory.Exists(root))
            {
                SetStatus($"Playfields directory not found: {root}");
                RebuildDropdown();
                return;
            }

            foreach (string file in Directory.GetFiles(root, "*.json"))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(stem, out int playfieldId))
                    continue;

                string displayName = $"PF {playfieldId}";
                try
                {
                    var header = JsonConvert.DeserializeObject<PlayfieldFileHeader>(File.ReadAllText(file));
                    if (header != null)
                    {
                        if (header.PlayfieldId > 0)
                            playfieldId = header.PlayfieldId;
                        if (!string.IsNullOrWhiteSpace(header.Name))
                            displayName = header.Name.Trim();
                    }
                }
                catch
                {
                    // Keep the fallback name if a file is malformed.
                }

                _playfields.Add(new PlayfieldListEntry
                {
                    PlayfieldId = playfieldId,
                    Name = displayName
                });
            }

            _playfields.Sort((a, b) => a.PlayfieldId.CompareTo(b.PlayfieldId));
            RebuildDropdown();

            int activePlayfieldId = ResolveBootstrap()?.ActivePlayfieldId ?? 0;
            int activeIndex = _playfields.FindIndex(p => p.PlayfieldId == activePlayfieldId);
            if (activeIndex >= 0)
            {
                _suppressSelectionEvents = true;
                _playfieldDropdown.value = activeIndex;
                _playfieldDropdown.RefreshShownValue();
                _playfieldIdInput.text = _playfields[activeIndex].PlayfieldId.ToString();
                _suppressSelectionEvents = false;
            }
            else if (_playfields.Count > 0)
            {
                _playfieldIdInput.text = _playfields[0].PlayfieldId.ToString();
            }

            SetStatus($"Loaded {_playfields.Count} playfields from AOData/Playfields.");
        }

        private void RebuildDropdown()
        {
            if (_playfieldDropdown == null)
                return;

            _suppressSelectionEvents = true;
            _playfieldDropdown.options.Clear();
            foreach (var playfield in _playfields)
            {
                _playfieldDropdown.options.Add(new Dropdown.OptionData($"{playfield.PlayfieldId}: {playfield.Name}"));
            }

            if (_playfieldDropdown.options.Count > 0)
                _playfieldDropdown.value = 0;

            _playfieldDropdown.RefreshShownValue();
            _suppressSelectionEvents = false;
        }

        private void OnDropdownChanged(int index)
        {
            if (_suppressSelectionEvents)
                return;
            if (index < 0 || index >= _playfields.Count || _playfieldIdInput == null)
                return;

            _playfieldIdInput.text = _playfields[index].PlayfieldId.ToString();
        }

        private static void ConfigureCoordinateInput(InputField input)
        {
            if (input == null)
                return;

            input.contentType = InputField.ContentType.DecimalNumber;
            input.lineType = InputField.LineType.SingleLine;
        }

        private void TeleportToSelection()
        {
            if (_playfieldIdInput == null || !int.TryParse(_playfieldIdInput.text, out int playfieldId) || playfieldId <= 0)
            {
                SetStatus("Enter a valid playfield ID.");
                return;
            }

            var bootstrap = ResolveBootstrap();
            if (bootstrap == null)
            {
                SetStatus("PrototypeWorldBootstrap was not found in the scene.");
                return;
            }

            var player = bootstrap.ResolvePlayerTransform();
            var networkClient = player != null ? player.GetComponent<AuthoritativeNetworkClient>() : null;
            if (networkClient == null)
                networkClient = FindFirstObjectByType<AuthoritativeNetworkClient>();
            bool serverAuthoritativeTeleport = networkClient != null && networkClient.IsConnected;
            if (!serverAuthoritativeTeleport)
            {
                string detail = networkClient == null
                    ? "no AuthoritativeNetworkClient found"
                    : $"client found, serverAvailable={networkClient.IsServerAvailable}, connected={networkClient.IsConnected}";
                SetStatus($"Teleport blocked: server-authoritative zoning requires AO.Server connection ({detail}).");
                return;
            }
            Vector3? targetAoPosition = null;
            bool hasAnyCoord =
                !string.IsNullOrWhiteSpace(_coordXInput?.text) ||
                !string.IsNullOrWhiteSpace(_coordYInput?.text) ||
                !string.IsNullOrWhiteSpace(_coordZInput?.text);

            if (hasAnyCoord)
            {
                if (!TryParseCoordinateInputs(out var aoPosition))
                {
                    SetStatus("Enter valid AO coordinates in X, Y, and Z, or leave all three blank.");
                    return;
                }

                targetAoPosition = aoPosition;
            }

            bool hasManualCoords = targetAoPosition.HasValue;
            float? targetYaw = null;

            bool hasExplicitServerTarget = targetAoPosition.HasValue;
            Vector3 aoTarget = hasExplicitServerTarget ? targetAoPosition.Value : Vector3.zero;
            networkClient.RequestAdminTeleport(
                playfieldId,
                aoTarget,
                targetYaw ?? 0f,
                useDefaultSpawn: !hasManualCoords);

            string requestedDestination = hasExplicitServerTarget
                ? $"AO ({aoTarget.x:0.##}, {aoTarget.y:0.##}, {aoTarget.z:0.##})"
                : "server default/highest";
            string requestSource = hasManualCoords
                ? "manual coords"
                : "server-resolved default";
            string actualAo = string.Empty;
            if (player != null)
            {
                Vector3 currentAo = bootstrap.ConvertWorldToAo(player.position);
                actualAo = $" Current AO now: ({currentAo.x:0.##}, {currentAo.y:0.##}, {currentAo.z:0.##}).";
            }

            SetStatus(
                $"Sent server-authoritative teleport request to PF {playfieldId} at {requestedDestination} ({requestSource}). Waiting for server move.{actualAo}");
        }

        private bool TryParseCoordinateInputs(out Vector3 aoPosition)
        {
            aoPosition = Vector3.zero;
            if (_coordXInput == null || _coordYInput == null || _coordZInput == null)
                return false;

            if (!float.TryParse(_coordXInput.text, out float x))
                return false;
            if (!float.TryParse(_coordYInput.text, out float y))
                return false;
            if (!float.TryParse(_coordZInput.text, out float z))
                return false;

            aoPosition = new Vector3(x, y, z);
            return true;
        }

        private PrototypeWorldBootstrap ResolveBootstrap()
        {
            return FindFirstObjectByType<PrototypeWorldBootstrap>();
        }

        private void SetStatus(string message)
        {
            if (_statusField != null)
                _statusField.text = message ?? string.Empty;
        }
    }
}
