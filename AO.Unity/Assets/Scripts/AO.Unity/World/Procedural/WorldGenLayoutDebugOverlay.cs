using System;
using System.Collections.Generic;
using System.Linq;
using WorldGen.Dungeons;
using WorldGen.Spatial;
using UnityEngine;

namespace AO.Unity.World.Procedural
{
    /// <summary>
    /// Development-only, engine adapter view of a verified generated layout. The
    /// logical labels, bounds, corridors, and portals all come from WorldGen Core.
    /// </summary>
    public sealed class WorldGenLayoutDebugOverlay : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private const float Margin = 42f;
        private const float HeaderHeight = 88f;
        private static Texture2D _pixel;

        private DungeonLayout _layout;
        private IReadOnlyDictionary<string, DungeonDoorStateSnapshot> _doorStates;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _miniLabelStyle;
        private readonly Dictionary<string, string> _roomLabels =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _mechanicalDoorIds =
            new HashSet<string>(StringComparer.Ordinal);
        private PrototypeWalkerController _walker;
        private AOGameServerSession _session;
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _dragging;
        private Vector2 _lastDragPosition;
        private bool _fullScreenOpen;
        private bool _miniMapOpen;

        public bool IsFullScreenOpen => _fullScreenOpen;

        public void Show(DungeonLayout layout,
            IReadOnlyDictionary<string, DungeonDoorStateSnapshot> doorStates,
            PrototypeWalkerController walker)
        {
            SetLayout(layout ?? throw new ArgumentNullException(nameof(layout)), doorStates);
            _walker = walker;
            _walker?.SetModalInputSuppressed(true);
            _fullScreenOpen = true;
            IsOpen = true;
            enabled = true;
        }

        public bool ToggleMiniMap(DungeonLayout layout,
            IReadOnlyDictionary<string, DungeonDoorStateSnapshot> doorStates)
        {
            _miniMapOpen = !_miniMapOpen;
            if (_miniMapOpen)
                SetLayout(layout ?? throw new ArgumentNullException(nameof(layout)), doorStates);
            enabled = _fullScreenOpen || _miniMapOpen;
            return _miniMapOpen;
        }

        private void SetLayout(DungeonLayout layout,
            IReadOnlyDictionary<string, DungeonDoorStateSnapshot> doorStates)
        {
            _layout = layout;
            _doorStates = doorStates;
            _session = GetComponent<AOGameServerSession>();
            BuildRoomLabels();
            _mechanicalDoorIds.Clear();
            foreach (DungeonDoor door in DungeonDoorFactory.Create(_layout))
                _mechanicalDoorIds.Add(door.Id);
        }

        public void Hide()
        {
            _walker?.SetModalInputSuppressed(false);
            _walker = null;
            _dragging = false;
            _fullScreenOpen = false;
            IsOpen = false;
            enabled = _miniMapOpen;
        }

        private void Awake()
        {
            enabled = false;
        }

        private void OnDisable()
        {
            _walker?.SetModalInputSuppressed(false);
            _walker = null;
            _session = null;
            _dragging = false;
            _fullScreenOpen = false;
            _miniMapOpen = false;
            IsOpen = false;
        }

        private void Update()
        {
            if (_fullScreenOpen && Input.GetKeyDown(KeyCode.Escape)) Hide();
        }

        private void OnGUI()
        {
            if (!enabled || _layout == null) return;
            EnsureStyles();
            if (!_fullScreenOpen)
            {
                if (_miniMapOpen) DrawMiniMap();
                return;
            }

            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Fill(screen, new Color(0.025f, 0.035f, 0.05f, 0.96f));
            GUI.Label(new Rect(Margin, 12f, Screen.width - Margin * 2f, 32f),
                $"WorldGen Layout — {_layout.Manifest.WorldId}", _headerStyle);
            GUI.Label(new Rect(Margin, 42f, Screen.width - Margin * 2f, 22f),
                $"seed {_layout.Manifest.Seed}   rooms {_layout.Rooms.Count}   "
                + $"zoom {_zoom:0.0}x   wheel=zoom  left-drag=pan   "
                + "Esc or .worldgen layout=close",
                _smallStyle);
            GUI.Label(new Rect(Margin, 62f, Screen.width - Margin * 2f, 22f),
                "Rooms: green=entrance  blue=normal/branch  yellow=objective  "
                + "red=boss  purple=treasure    Connections: green=open passage  "
                + "cyan=open door  orange=closed/locked/sealed",
                _smallStyle);

            Rect map = new Rect(Margin, HeaderHeight + 18f,
                Mathf.Max(1f, Screen.width - Margin * 2f),
                Mathf.Max(1f, Screen.height - HeaderHeight - Margin - 18f));
            HandleMapInput(map);
            ComputeWorldBounds(out float minX, out float maxX, out float minZ, out float maxZ);
            float spanX = Mathf.Max(1f, maxX - minX);
            float spanZ = Mathf.Max(1f, maxZ - minZ);
            float fitScale = Mathf.Min(map.width / spanX, map.height / spanZ) * 0.94f;
            float scale = fitScale * _zoom;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float offsetX = map.center.x + _pan.x - centerX * scale;
            float offsetY = map.center.y + _pan.y + centerZ * scale;

            Rect Project(WorldBounds bounds)
            {
                float x = offsetX + bounds.Minimum.X / 1000f * scale;
                float y = offsetY - bounds.Maximum.Z / 1000f * scale;
                return new Rect(x, y,
                    Mathf.Max(1f, (bounds.Maximum.X - bounds.Minimum.X) / 1000f * scale),
                    Mathf.Max(1f, (bounds.Maximum.Z - bounds.Minimum.Z) / 1000f * scale));
            }

            foreach (DungeonCorridor corridor in _layout.Corridors)
            {
                Rect rect = Project(corridor.WalkableBounds);
                Fill(rect, new Color(0.18f, 0.22f, 0.27f, 1f));
                Stroke(rect, new Color(0.48f, 0.56f, 0.64f, 1f), 1f);
            }

            foreach (DungeonRoom room in _layout.Rooms.OrderBy(value => value.Index))
            {
                Color fill = RoomColor(room);
                IReadOnlyList<WorldBounds> sections = room.Shape == DungeonRoomShape.LShaped
                    || room.Shape == DungeonRoomShape.Cavern
                    ? DungeonRoomGeometry.FloorSections(room)
                    : new[] { room.Bounds };
                for (int i = 0; i < sections.Count; i++)
                {
                    Rect rect = Project(sections[i]);
                    Fill(rect, fill);
                    Stroke(rect, Color.white, 2f);
                }

                float cx = offsetX + room.Center.X / 1000f * scale;
                float cy = offsetY - room.Center.Z / 1000f * scale;
                string label = _roomLabels.TryGetValue(room.Id, out string value)
                    ? value : room.ModuleKind.ToString();
                Vector2 size = _labelStyle.CalcSize(new GUIContent(label));
                GUI.Label(new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f,
                    size.x, size.y), label, _labelStyle);
            }

            foreach (DungeonDoorPortal portal in _layout.DoorPortals)
            {
                float px = offsetX + portal.Center.X / 1000f * scale;
                float py = offsetY - portal.Center.Z / 1000f * scale;
                bool hasDoor = _mechanicalDoorIds.Contains(portal.Id);
                bool blocks = hasDoor && _doorStates != null
                    && _doorStates.TryGetValue(portal.Id, out DungeonDoorStateSnapshot state)
                    && state.State != DungeonDoorState.Open;
                Color color = !hasDoor
                    ? new Color(0.25f, 1f, 0.38f, 1f)
                    : blocks
                    ? new Color(1f, 0.52f, 0.12f, 1f)
                    : new Color(0.1f, 0.95f, 1f, 1f);
                bool vertical = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.PositiveX;
                Rect marker = vertical
                    ? new Rect(px - 2f, py - 7f, 4f, 14f)
                    : new Rect(px - 7f, py - 2f, 14f, 4f);
                Fill(marker, color);
            }

            DrawPlayerMarker(map, offsetX, offsetY, scale);

            if (GUI.Button(new Rect(Screen.width - 112f, 18f, 72f, 30f), "Close"))
                Hide();
            if (GUI.Button(new Rect(Screen.width - 196f, 18f, 76f, 30f), "Reset view"))
            {
                _zoom = 1f;
                _pan = Vector2.zero;
            }
        }

        private void DrawMiniMap()
        {
            const float width = 330f;
            const float height = 270f;
            Rect panel = new Rect(Mathf.Max(12f, Screen.width - width - 18f), 68f,
                width, height);
            Fill(panel, new Color(0.025f, 0.035f, 0.05f, 0.88f));
            Stroke(panel, new Color(0.35f, 0.65f, 0.82f, 0.9f), 2f);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 4f, panel.width - 16f, 20f),
                $"WorldGen — {_layout.Manifest.WorldId}", _smallStyle);
            Rect map = new Rect(panel.x + 10f, panel.y + 27f,
                panel.width - 20f, panel.height - 37f);
            ComputeWorldBounds(out float minX, out float maxX, out float minZ, out float maxZ);
            float spanX = Mathf.Max(1f, maxX - minX);
            float spanZ = Mathf.Max(1f, maxZ - minZ);
            float scale = Mathf.Min(map.width / spanX, map.height / spanZ) * 0.94f;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float offsetX = map.center.x - centerX * scale;
            float offsetY = map.center.y + centerZ * scale;

            Rect Project(WorldBounds bounds) => new Rect(
                offsetX + bounds.Minimum.X / 1000f * scale,
                offsetY - bounds.Maximum.Z / 1000f * scale,
                Mathf.Max(1f, (bounds.Maximum.X - bounds.Minimum.X) / 1000f * scale),
                Mathf.Max(1f, (bounds.Maximum.Z - bounds.Minimum.Z) / 1000f * scale));

            foreach (DungeonCorridor corridor in _layout.Corridors)
            {
                Rect rect = Project(corridor.WalkableBounds);
                Fill(rect, new Color(0.18f, 0.22f, 0.27f, 0.95f));
            }
            foreach (DungeonRoom room in _layout.Rooms.OrderBy(value => value.Index))
            {
                IReadOnlyList<WorldBounds> sections = room.Shape == DungeonRoomShape.LShaped
                    || room.Shape == DungeonRoomShape.Cavern
                    ? DungeonRoomGeometry.FloorSections(room)
                    : new[] { room.Bounds };
                foreach (WorldBounds section in sections)
                {
                    Rect rect = Project(section);
                    Fill(rect, RoomColor(room));
                    Stroke(rect, Color.white, 1f);
                }
                Rect roomRect = Project(room.Bounds);
                if (roomRect.width >= 24f && roomRect.height >= 12f
                    && _roomLabels.TryGetValue(room.Id, out string roomLabel))
                    GUI.Label(roomRect, roomLabel, _miniLabelStyle);
            }
            foreach (DungeonDoorPortal portal in _layout.DoorPortals)
            {
                float px = offsetX + portal.Center.X / 1000f * scale;
                float py = offsetY - portal.Center.Z / 1000f * scale;
                bool hasDoor = _mechanicalDoorIds.Contains(portal.Id);
                bool blocks = hasDoor && _doorStates != null
                    && _doorStates.TryGetValue(portal.Id, out DungeonDoorStateSnapshot state)
                    && state.State != DungeonDoorState.Open;
                Color color = !hasDoor ? new Color(0.25f, 1f, 0.38f)
                    : blocks ? new Color(1f, 0.52f, 0.12f)
                    : new Color(0.1f, 0.95f, 1f);
                Fill(new Rect(px - 2f, py - 2f, 4f, 4f), color);
            }
            DrawPlayerMarker(map, offsetX, offsetY, scale, false);
        }

        private void HandleMapInput(Rect map)
        {
            Event current = Event.current;
            if (current == null) return;
            if (current.type == EventType.ScrollWheel && map.Contains(current.mousePosition))
            {
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.18f, -current.delta.y), 0.5f, 30f);
                float ratio = _zoom / oldZoom;
                Vector2 relative = current.mousePosition - map.center - _pan;
                _pan = current.mousePosition - map.center - relative * ratio;
                current.Use();
            }
            else if (current.type == EventType.MouseDown && current.button == 0
                && map.Contains(current.mousePosition))
            {
                _dragging = true;
                _lastDragPosition = current.mousePosition;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && _dragging)
            {
                Vector2 position = current.mousePosition;
                _pan += position - _lastDragPosition;
                _lastDragPosition = position;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 && _dragging)
            {
                _dragging = false;
                current.Use();
            }
        }

        private void DrawPlayerMarker(Rect map, float offsetX, float offsetY, float scale,
            bool showLabel = true)
        {
            if (_session == null || !_session.IsInVerifiedProceduralPlayfield
                || _session.ActiveProceduralLayout == null
                || !string.Equals(_session.ActiveProceduralLayout.Manifest.WorldId,
                    _layout.Manifest.WorldId, StringComparison.Ordinal)
                || _session.ControlledPlayerTransform == null)
                return;
            Vector3 position = _session.ControlledPlayerTransform.position;
            Vector2 point = new Vector2(offsetX + position.x * scale,
                offsetY - position.z * scale);
            if (!map.Contains(point)) return;
            const float radius = 7f;
            Fill(new Rect(point.x - radius - 2f, point.y - radius - 2f,
                (radius + 2f) * 2f, (radius + 2f) * 2f), Color.black);
            Fill(new Rect(point.x - radius, point.y - radius,
                radius * 2f, radius * 2f), new Color(1f, 0.2f, 0.25f, 1f));
            Fill(new Rect(point.x - radius - 5f, point.y - 1f,
                (radius + 5f) * 2f, 2f), Color.white);
            Fill(new Rect(point.x - 1f, point.y - radius - 5f,
                2f, (radius + 5f) * 2f), Color.white);
            if (showLabel)
                GUI.Label(new Rect(point.x + 10f, point.y - 10f, 80f, 20f), "You", _smallStyle);
        }

        private void BuildRoomLabels()
        {
            _roomLabels.Clear();
            var totals = _layout.Rooms.GroupBy(room => room.ModuleKind)
                .ToDictionary(group => group.Key, group => group.Count());
            var next = new Dictionary<DungeonModuleKind, int>();
            foreach (DungeonRoom room in _layout.Rooms.OrderBy(value => value.Index))
            {
                next.TryGetValue(room.ModuleKind, out int occurrence);
                next[room.ModuleKind] = occurrence + 1;
                string name = !string.IsNullOrWhiteSpace(room.DisplayName)
                    ? room.DisplayName : SplitPascalCase(room.ModuleKind.ToString());
                _roomLabels[room.Id] = totals[room.ModuleKind] > 1
                    ? $"{name} {occurrence}" : name;
            }
        }

        private void ComputeWorldBounds(out float minX, out float maxX,
            out float minZ, out float maxZ)
        {
            IEnumerable<WorldBounds> bounds = _layout.Rooms.Select(room => room.Bounds)
                .Concat(_layout.Corridors.Select(corridor => corridor.WalkableBounds));
            minX = bounds.Min(value => value.Minimum.X) / 1000f;
            maxX = bounds.Max(value => value.Maximum.X) / 1000f;
            minZ = bounds.Min(value => value.Minimum.Z) / 1000f;
            maxZ = bounds.Max(value => value.Maximum.Z) / 1000f;
        }

        private static Color RoomColor(DungeonRoom room)
        {
            if (room.Role == DungeonRoomRole.Entrance) return new Color(0.12f, 0.45f, 0.28f, 1f);
            if (room.Role == DungeonRoomRole.Boss) return new Color(0.55f, 0.14f, 0.18f, 1f);
            if (room.Role == DungeonRoomRole.Objective) return new Color(0.48f, 0.34f, 0.12f, 1f);
            if (room.Role == DungeonRoomRole.Treasure) return new Color(0.43f, 0.25f, 0.52f, 1f);
            return new Color(0.12f, 0.25f, 0.38f, 1f);
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null) return;
            _headerStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _smallStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 12, normal = { textColor = new Color(0.75f, 0.85f, 0.92f) } };
            _miniLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8, alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };
        }

        private static string SplitPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var result = new System.Text.StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                    result.Append(' ');
                result.Append(value[i]);
            }
            return result.ToString();
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Pixel);
            GUI.color = previous;
        }

        private static void Stroke(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Texture2D Pixel
        {
            get
            {
                if (_pixel != null) return _pixel;
                _pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
                return _pixel;
            }
        }
    }
}
