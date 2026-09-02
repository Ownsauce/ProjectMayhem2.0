using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AO.Client;
using AO.Client.Authentication;
using AO.Client.Backends.AORebirth;
using AO.Client.Characters;
using AO.Client.World;
using AO.Unity.Assets;
using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>
    /// Unity-facing session boundary. Presentation code consumes neutral AO.Client
    /// models; AORebirth packet details remain inside its backend adapter.
    /// </summary>
    public sealed class AOGameServerSession : MonoBehaviour
    {
        public sealed class ZoneSnapshot
        {
            public ZoneSnapshot(WorldBootstrapResult bootstrap,
                IReadOnlyList<NearbyEntity> entities, IReadOnlyList<WorldObject> objects)
            {
                Bootstrap = bootstrap;
                Entities = entities;
                Objects = objects;
            }

            public WorldBootstrapResult Bootstrap { get; }
            public IReadOnlyList<NearbyEntity> Entities { get; }
            public IReadOnlyList<WorldObject> Objects { get; }
        }

        [SerializeField] private string serverName = "AORebirth Local";
        [SerializeField] private string serverHost = "127.0.0.1";
        [SerializeField] private int serverPort = 7500;
        [SerializeField] private string clientVersion = "18.8.62";
        private string _authentication = "aorebirth";
        private string _loginPrime = string.Empty;
        private string _loginPublicSeed = string.Empty;
        [SerializeField] private int initialWorldPacketLimit = 512;
        [SerializeField] private float worldEntryTimeoutSeconds = 45f;
        [SerializeField] private float placeholderPathMovementSpeed = 3.5f;
        [SerializeField] private float remoteMovementSmoothing = 14f;
        [SerializeField] private float remoteMovementSnapDistance = 12f;
        [SerializeField] private float remotePlayerRunSpeed = 8f;
        [SerializeField] private float movementSendRateHz = 10f;
        [SerializeField] private float idleMovementHeartbeatSeconds = 1f;

        private IGameServerBackend _backend;
        private CancellationTokenSource _lifetime;
        private CancellationTokenSource _worldUpdates;
        private Task _worldUpdateTask;
        private Transform _placeholderRoot;
        private readonly Dictionary<string, GameObject> _entityPlaceholders =
            new Dictionary<string, GameObject>();
        private readonly Dictionary<string, object> _entityVisualImporters =
            new Dictionary<string, object>();
        private readonly HashSet<string> _entityVisualLoads = new HashSet<string>();
        private readonly Dictionary<uint, GameObject> _npcVisualTemplates =
            new Dictionary<uint, GameObject>();
        private readonly List<object> _npcTemplateImporters = new List<object>();
        private readonly SemaphoreSlim _npcVisualImportGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, Vector3> _entityMovementTargets =
            new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> _remoteMovementTargets =
            new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> _remoteMovementVelocities =
            new Dictionary<string, Vector3>();
        private readonly Dictionary<string, RemoteMotionState> _remoteMotionStates =
            new Dictionary<string, RemoteMotionState>();
        private readonly List<string> _movementUpdateKeys = new List<string>();
        private readonly Dictionary<string, int> _entityRemovalVersions =
            new Dictionary<string, int>();
        private WorldBootstrapResult _worldOrigin;
        private Vector3 _worldOriginOffset;
        private int _diagnosticPackets;
        private int _diagnosticMovementPackets;
        private int _diagnosticUnmatchedMovementPackets;
        private int _diagnosticMovementDeltas;
        private int _diagnosticStatDeltas;
        private int _diagnosticOtherDeltas;
        private float _nextDiagnosticLogAt;
        private Transform _controlledPlayer;
        private Vector3 _lastSentPlayerPosition;
        private float _lastSentPlayerYaw;
        private float _nextMovementSendAt;
        private float _lastMovementSentAt;
        private bool _movementSendPending;
        private CharacterStateSnapshot _lastDeliveredCharacterState;
        private byte _lastSentMoveType = 21;
        private readonly Dictionary<string, int> _diagnosticPacketKinds =
            new Dictionary<string, int>();
        private readonly Dictionary<string, string> _diagnosticPacketSamples =
            new Dictionary<string, string>();
        private readonly Dictionary<string, byte> _diagnosticLastMoveTypes =
            new Dictionary<string, byte>();

        public event Action<InventorySnapshot> InventoryChanged;
        public event Action<CharacterStateSnapshot> CharacterStateChanged;

        public bool IsAuthenticated => _backend != null
            && (_backend.State == ClientConnectionState.Authenticated
                || _backend.State == ClientConnectionState.InWorld);

        public string ServerDisplayName => string.IsNullOrWhiteSpace(serverName) ? serverHost : serverName;
        public string ServerEndpoint => $"{serverHost}:{serverPort}";
        public string ConnectionState => _backend?.State.ToString() ?? ClientConnectionState.Disconnected.ToString();

        public string CredentialFilePath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "tmp", "projectmayhem-aorebirth.credentials"));

        public void ConfigureConnection(string name, string host, int port,
            string version = "18.8.62", string authentication = "aorebirth",
            string loginPrime = "", string loginPublicSeed = "")
        {
            serverName = string.IsNullOrWhiteSpace(name) ? "AORebirth" : name.Trim();
            serverHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            serverPort = port > 0 ? port : 7500;
            clientVersion = string.IsNullOrWhiteSpace(version) ? "18.8.62" : version.Trim();
            _authentication = string.IsNullOrWhiteSpace(authentication)
                ? "aorebirth" : authentication.Trim().ToLowerInvariant();
            _loginPrime = loginPrime ?? string.Empty;
            _loginPublicSeed = loginPublicSeed ?? string.Empty;
            Debug.Log($"[AO.Client] Configured '{serverName}' at {serverHost}:{serverPort} "
                + $"(authentication={_authentication}, version={clientVersion}).");
        }

        private void Awake()
        {
            _lifetime = new CancellationTokenSource();
        }

        private void Update()
        {
            float step = Mathf.Max(0.1f, placeholderPathMovementSpeed) * Time.deltaTime;
            CopyKeys(_entityMovementTargets, _movementUpdateKeys);
            for (int index = 0; index < _movementUpdateKeys.Count; index++)
            {
                string key = _movementUpdateKeys[index];
                if (!_entityMovementTargets.TryGetValue(key, out Vector3 target)
                    || !_entityPlaceholders.TryGetValue(key, out GameObject actor)
                    || actor == null)
                {
                    _entityMovementTargets.Remove(key);
                    continue;
                }
                actor.transform.position = Vector3.MoveTowards(
                    actor.transform.position, target, step);
                if ((actor.transform.position - target).sqrMagnitude < 0.0001f)
                    _entityMovementTargets.Remove(key);
            }
            float smoothing = Mathf.Max(0.1f, remoteMovementSmoothing);
            float blend = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            float snapDistance = Mathf.Max(0.5f, remoteMovementSnapDistance);
            CopyKeys(_remoteMovementTargets, _movementUpdateKeys);
            for (int index = 0; index < _movementUpdateKeys.Count; index++)
            {
                string key = _movementUpdateKeys[index];
                if (!_remoteMovementTargets.TryGetValue(key, out Vector3 target)
                    || !_entityPlaceholders.TryGetValue(key, out GameObject actor)
                    || actor == null)
                {
                    _remoteMovementTargets.Remove(key);
                    continue;
                }
                float distance = Vector3.Distance(actor.transform.position, target);
                actor.transform.position = distance > snapDistance
                    ? target
                    : Vector3.Lerp(actor.transform.position, target, blend);
                if (distance < 0.02f)
                    _remoteMovementTargets.Remove(key);
            }
            CopyKeys(_remoteMovementVelocities, _movementUpdateKeys);
            for (int index = 0; index < _movementUpdateKeys.Count; index++)
            {
                string key = _movementUpdateKeys[index];
                if (!_remoteMovementVelocities.TryGetValue(key, out Vector3 velocity)
                    || !_entityPlaceholders.TryGetValue(key, out GameObject actor)
                    || actor == null)
                {
                    _remoteMovementVelocities.Remove(key);
                    continue;
                }
                Vector3 predictedStep = velocity * Time.deltaTime;
                actor.transform.position += predictedStep;
                // The correction target represents the server position at the
                // time of its packet. Advance it with the same prediction so it
                // corrects error without anchoring a moving actor in the past.
                if (_remoteMovementTargets.TryGetValue(key,
                    out Vector3 correctionTarget))
                {
                    _remoteMovementTargets[key] = correctionTarget + predictedStep;
                }
            }
            TrySendPlayerMovement();
        }

        private static void CopyKeys(Dictionary<string, Vector3> source, List<string> destination)
        {
            destination.Clear();
            foreach (string key in source.Keys)
                destination.Add(key);
        }

        public void BeginPlayerMovement(Transform controlledPlayer)
        {
            _controlledPlayer = controlledPlayer;
            _lastSentPlayerPosition = controlledPlayer != null
                ? controlledPlayer.position : Vector3.zero;
            _lastSentPlayerYaw = controlledPlayer != null
                ? controlledPlayer.eulerAngles.y : 0f;
            _nextMovementSendAt = 0f;
            _lastMovementSentAt = 0f;
            _lastSentMoveType = 21;
        }

        public void SetWorldOriginOffset(Vector3 worldPosition)
        {
            _worldOriginOffset = worldPosition;
            if (_placeholderRoot != null)
                _placeholderRoot.position = worldPosition;
        }

        public void RebaseWorldOrigin(Vector3 aoPosition)
        {
            if (_worldOrigin == null)
                return;

            Vector3 localDelta = new Vector3(
                _worldOrigin.X - aoPosition.x,
                _worldOrigin.Y - aoPosition.y,
                _worldOrigin.Z - aoPosition.z);
            if (_placeholderRoot != null)
            {
                for (int index = 0; index < _placeholderRoot.childCount; index++)
                {
                    Transform child = _placeholderRoot.GetChild(index);
                    if (child != null && child.name != "Temporary Network Viewer Floor")
                        child.localPosition += localDelta;
                }
            }

            _worldOrigin = new WorldBootstrapResult(
                _worldOrigin.CharacterId,
                _worldOrigin.PlayfieldId,
                aoPosition.x,
                aoPosition.y,
                aoPosition.z,
                _worldOrigin.PacketsObserved);
            Debug.Log($"[AO.Client] Rebased invalid world origin to AO {aoPosition:F3}.");
        }

        private void TrySendPlayerMovement()
        {
            if (_controlledPlayer == null || _worldOrigin == null || _movementSendPending
                || _backend == null || _backend.State != ClientConnectionState.InWorld
                || Time.realtimeSinceStartup < _nextMovementSendAt)
                return;

            Vector3 local = _controlledPlayer.position;
            Vector3 relative = local - _worldOriginOffset;
            float yaw = _controlledPlayer.eulerAngles.y;
            bool positionChanged = (local - _lastSentPlayerPosition).sqrMagnitude > 0.000001f;
            bool rotationChanged = Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentPlayerYaw)) > 0.05f;
            bool changed = positionChanged || rotationChanged;
            bool needsStop = !positionChanged && _lastSentMoveType != 21;
            bool heartbeatDue = Time.realtimeSinceStartup - _lastMovementSentAt
                >= Mathf.Max(0.25f, idleMovementHeartbeatSeconds);
            _nextMovementSendAt = Time.realtimeSinceStartup
                + (1f / Mathf.Max(1f, movementSendRateHz));
            if (!changed && !heartbeatDue && !needsStop)
                return;

            Vector3 ao = new Vector3(
                _worldOrigin.X + relative.x,
                _worldOrigin.Y + relative.y,
                _worldOrigin.Z + relative.z);
            Quaternion heading = _controlledPlayer.rotation;
            // Captured player-client wire states: 0x05 = forward run,
            // 0x15 (decimal 21) = FullStop. Server-side NPC follow mode 25 is
            // a different contract and must not be substituted here.
            byte moveType = positionChanged ? (byte)0x05 : (byte)0x15;
            var movement = new PlayerMovementUpdate(ao.x, ao.y, ao.z,
                heading.x, heading.y, heading.z, heading.w,
                moveType, Environment.TickCount);
            _lastSentPlayerPosition = local;
            _lastSentPlayerYaw = yaw;
            _lastMovementSentAt = Time.realtimeSinceStartup;
            _lastSentMoveType = moveType;
            SendPlayerMovementAsync(movement);
        }

        private async void SendPlayerMovementAsync(PlayerMovementUpdate movement)
        {
            _movementSendPending = true;
            try
            {
                await _backend.SendPlayerMovementAsync(movement, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _movementSendPending = false;
            }
        }

        public async Task<IReadOnlyList<CharacterSummary>> AuthenticateLocalAsync()
        {
            string[] credentials = ReadCredentials(CredentialFilePath);
            return await AuthenticateAsync(credentials[0], credentials[1]);
        }

        public async Task<IReadOnlyList<CharacterSummary>> AuthenticateAsync(
            string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A username is required.", nameof(username));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("A password is required.", nameof(password));
            await DisconnectBackendAsync().ConfigureAwait(true);

            _backend = new AORebirthBackend(clientVersion, _authentication,
                _loginPrime, _loginPublicSeed);
            _backend.StateChanged += HandleBackendStateChanged;
            var address = new UriBuilder("tcp", serverHost, serverPort).Uri;
            await _backend.ConnectAsync(
                new ServerEndpoint(serverName, address), _lifetime.Token);
            AuthenticationResult authentication = await _backend.AuthenticateAsync(
                new AuthenticationRequest(username.Trim(), password), _lifetime.Token);
            if (!authentication.Succeeded)
                throw new InvalidOperationException(authentication.Message);
            return await _backend.GetCharactersAsync(_lifetime.Token);
        }

        public Task DisconnectAsync()
        {
            return DisconnectBackendAsync();
        }

        public async Task<ZoneSnapshot> EnterWorldAsync(string characterId)
        {
            if (_backend == null || !IsAuthenticated)
                throw new InvalidOperationException("Authenticate before entering the world.");

            float timeoutSeconds = Mathf.Clamp(worldEntryTimeoutSeconds, 5f, 120f);
            try
            {
                using var handoffTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                handoffTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                WorldEntryResult entry = await _backend.EnterWorldAsync(characterId, handoffTimeout.Token);
                if (!entry.Succeeded)
                    throw new InvalidOperationException(entry.Message);

                Debug.Log("[WorldEntry] Zone transport connected; waiting for playfield bootstrap.");
                using var bootstrapTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                bootstrapTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                WorldBootstrapResult bootstrap = await _backend.ReceiveWorldBootstrapAsync(
                    bootstrapTimeout.Token);
                Debug.Log($"[WorldEntry] Playfield bootstrap received for PF {bootstrap.PlayfieldId}; collecting initial entities.");
                using var snapshotTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                snapshotTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                NearbyEntitiesResult nearby = await _backend.ReceiveNearbyEntitiesAsync(
                    Mathf.Clamp(initialWorldPacketLimit, 1, 4096), snapshotTimeout.Token);
                Debug.Log($"[WorldEntry] Initial entity collection complete ({nearby.Entities.Count} entities).");
                IReadOnlyList<WorldObject> objects = await _backend.GetWorldObjectsAsync(snapshotTimeout.Token);
                InventorySnapshot inventory = _backend.GetInventorySnapshot();
                if (inventory != null)
                    InventoryChanged?.Invoke(inventory);
                CharacterStateSnapshot characterState = _backend.GetCharacterStateSnapshot();
                if (characterState != null)
                {
                    _lastDeliveredCharacterState = characterState;
                    CharacterStateChanged?.Invoke(characterState);
                    Debug.Log($"[AO.Client] FullCharacter synchronized: {characterState.Slots.Count} slots, "
                        + $"{characterState.UploadedNanoIds.Count} uploaded nanos, "
                        + $"{characterState.Stats.Count} authoritative stats.");
                }
                else
                {
                    Debug.LogWarning("[AO.Client] No FullCharacter packet was decoded during initial world entry; "
                        + "equipment and uploaded programs cannot be populated yet.");
                }
                RenderPlaceholders(bootstrap, nearby.Entities, objects);
                StartWorldUpdates();
                return new ZoneSnapshot(bootstrap, nearby.Entities, objects);
            }
            catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"World entry timed out after {timeoutSeconds:0} seconds while the backend was "
                    + $"'{_backend?.State}'. Check the preceding [AO.Client] state message for the exact stage.");
            }
            catch (IOException) when (!_lifetime.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The connection to '{serverName}' ({serverHost}:{serverPort}) closed during world entry. "
                    + $"The backend was '{_backend?.State}'.");
            }
        }

        private static void HandleBackendStateChanged(object sender,
            ConnectionStateChangedEventArgs args)
        {
            Debug.Log($"[AO.Client] State {args.PreviousState} -> {args.CurrentState}: {args.Message}");
        }

        private void RenderPlaceholders(WorldBootstrapResult bootstrap,
            IReadOnlyList<NearbyEntity> entities, IReadOnlyList<WorldObject> objects)
        {
            if (_placeholderRoot != null)
                Destroy(_placeholderRoot.gameObject);
            foreach (object importer in _entityVisualImporters.Values)
                PrototypeWorldBootstrap.DisposeImporter(importer);
            _entityVisualImporters.Clear();
            foreach (object importer in _npcTemplateImporters)
                PrototypeWorldBootstrap.DisposeImporter(importer);
            _npcTemplateImporters.Clear();
            foreach (GameObject template in _npcVisualTemplates.Values)
            {
                if (template != null)
                    Destroy(template);
            }
            _npcVisualTemplates.Clear();
            _entityVisualLoads.Clear();
            _entityRemovalVersions.Clear();
            _placeholderRoot = new GameObject("AO Network Placeholders").transform;
            _worldOrigin = bootstrap;
            _entityPlaceholders.Clear();
            _entityMovementTargets.Clear();
            _remoteMovementTargets.Clear();
            _remoteMovementVelocities.Clear();
            _remoteMotionStates.Clear();

            // Until AO playfield collision is resolved, keep the local character
            // at the authoritative origin so the received world snapshot remains
            // inspectable. This is deliberately a debug surface, not zone data.
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Temporary Network Viewer Floor";
            floor.transform.SetParent(_placeholderRoot, false);
            floor.transform.position = new Vector3(0f, -1f, 0f);
            floor.transform.localScale = new Vector3(100f, 1f, 100f);
            Renderer floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
                floorRenderer.material.color = new Color(0.07f, 0.10f, 0.13f);

            foreach (NearbyEntity entity in entities)
            {
                // The controllable CharacterRuntimeBridge already represents the
                // selected character. A second solid capsule at the same AO
                // position traps its CharacterController in overlapping collision.
                if (string.Equals(
                        entity.IdentityInstance.ToString(),
                        bootstrap.CharacterId,
                        StringComparison.Ordinal))
                    continue;

                UpsertEntityPlaceholder(entity);
            }

            foreach (WorldObject worldObject in objects.Where(item => item.HasPosition))
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"{worldObject.Kind} {worldObject.Name}";
                marker.transform.SetParent(_placeholderRoot, false);
                marker.transform.position = ToRelativeUnityPosition(
                    worldObject.X, worldObject.Y, worldObject.Z, bootstrap);
                marker.transform.localScale = new Vector3(1.2f, 1.8f, 1.2f);
                Renderer renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color(0.2f, 0.9f, 0.55f);
                AddLabel(marker.transform, worldObject.Name, 1.4f);
            }
        }

        public void SetTemporaryFloorVisible(bool visible)
        {
            if (_placeholderRoot == null) return;
            Transform floor = _placeholderRoot.Find("Temporary Network Viewer Floor");
            if (floor != null) floor.gameObject.SetActive(visible);
        }

        private void StartWorldUpdates()
        {
            _worldUpdates?.Cancel();
            _worldUpdates?.Dispose();
            _worldUpdates = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _diagnosticPackets = 0;
            _diagnosticMovementPackets = 0;
            _diagnosticUnmatchedMovementPackets = 0;
            _diagnosticMovementDeltas = 0;
            _diagnosticStatDeltas = 0;
            _diagnosticOtherDeltas = 0;
            _diagnosticPacketKinds.Clear();
            _diagnosticPacketSamples.Clear();
            _nextDiagnosticLogAt = Time.realtimeSinceStartup + 5f;
            _worldUpdateTask = RunWorldUpdatesAsync(_worldUpdates.Token);
        }

        private async Task RunWorldUpdatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested
                       && _backend != null
                       && _backend.State == ClientConnectionState.InWorld)
                {
                    WorldDeltaBatch batch = await _backend.ReceiveWorldDeltasAsync(
                        128, cancellationToken);
                    _diagnosticPackets += batch.PacketsObserved;
                    _diagnosticMovementPackets += batch.MovementPacketsObserved;
                    _diagnosticUnmatchedMovementPackets += batch.UnmatchedMovementPackets;
                    foreach (KeyValuePair<string, int> packetKind in batch.PacketKindsObserved)
                    {
                        _diagnosticPacketKinds.TryGetValue(packetKind.Key, out int currentCount);
                        _diagnosticPacketKinds[packetKind.Key] = currentCount + packetKind.Value;
                    }
                    foreach (KeyValuePair<string, string> packetSample in batch.PacketSamplesObserved)
                    {
                        if (!_diagnosticPacketSamples.ContainsKey(packetSample.Key))
                            _diagnosticPacketSamples[packetSample.Key] = packetSample.Value;
                    }
                    foreach (WorldEntityDelta delta in batch.Deltas)
                    {
                        if (delta.Kind == WorldEntityDeltaKind.Movement)
                            _diagnosticMovementDeltas++;
                        else if (delta.Kind == WorldEntityDeltaKind.Stats)
                            _diagnosticStatDeltas++;
                        else
                            _diagnosticOtherDeltas++;
                        ApplyWorldDelta(delta);
                    }
                    if (batch.Inventory != null)
                        InventoryChanged?.Invoke(batch.Inventory);
                    CharacterStateSnapshot characterState = _backend.GetCharacterStateSnapshot();
                    if (characterState != null
                        && !ReferenceEquals(characterState, _lastDeliveredCharacterState))
                    {
                        _lastDeliveredCharacterState = characterState;
                        CharacterStateChanged?.Invoke(characterState);
                    }
                    LogWorldUpdateDiagnosticsIfDue();
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal zone/session shutdown.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void LogWorldUpdateDiagnosticsIfDue()
        {
            if (Time.realtimeSinceStartup < _nextDiagnosticLogAt)
                return;
            Debug.Log("[AO.Client] World update diagnostics (last 5s): "
                + $"packets={_diagnosticPackets}, "
                + $"movement-packets={_diagnosticMovementPackets}, "
                + $"movement-deltas={_diagnosticMovementDeltas}, "
                + $"unmatched-movement={_diagnosticUnmatchedMovementPackets}, "
                + $"stat-deltas={_diagnosticStatDeltas}, "
                + $"other-deltas={_diagnosticOtherDeltas}; "
                + "packet-kinds="
                + string.Join(", ", _diagnosticPacketKinds
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key)
                    .Take(10)
                    .Select(item => $"{item.Key}={item.Value}"))
                + "; samples="
                + string.Join(", ", _diagnosticPacketSamples
                    .Where(item => item.Key.StartsWith("n3:0x260F3671", StringComparison.Ordinal)
                        || item.Key.StartsWith("n3:0x54111123", StringComparison.Ordinal))
                    .Select(item => $"{item.Key}:{item.Value}"))
                + ".");
            _diagnosticPackets = 0;
            _diagnosticMovementPackets = 0;
            _diagnosticUnmatchedMovementPackets = 0;
            _diagnosticMovementDeltas = 0;
            _diagnosticStatDeltas = 0;
            _diagnosticOtherDeltas = 0;
            _diagnosticPacketKinds.Clear();
            _diagnosticPacketSamples.Clear();
            _nextDiagnosticLogAt = Time.realtimeSinceStartup + 5f;
        }

        private void ApplyWorldDelta(WorldEntityDelta delta)
        {
            if (delta == null)
                return;
            string key = EntityKey(delta.IdentityType, delta.IdentityInstance);
            if (delta.Kind == WorldEntityDeltaKind.Movement
                && (!_diagnosticLastMoveTypes.TryGetValue(key, out byte previousMoveType)
                    || previousMoveType != delta.MoveType))
            {
                _diagnosticLastMoveTypes[key] = delta.MoveType;
                Debug.Log($"[AO.Client] Movement state {key}: 0x{delta.MoveType:X2}, "
                    + $"selected={IsSelectedCharacter(delta.Entity)}, "
                    + $"heading={delta.HasHeading}, "
                    + $"position=({delta.Entity?.X}, {delta.Entity?.Y}, {delta.Entity?.Z}).");
            }
            if (delta.Kind == WorldEntityDeltaKind.Remove)
            {
                int version = _entityRemovalVersions.TryGetValue(key, out int previousVersion)
                    ? previousVersion + 1 : 1;
                _entityRemovalVersions[key] = version;
                _entityMovementTargets.Remove(key);
                _remoteMovementTargets.Remove(key);
                _remoteMovementVelocities.Remove(key);
                _remoteMotionStates.Remove(key);
                _diagnosticLastMoveTypes.Remove(key);
                _ = RemoveEntityAfterGracePeriodAsync(key, version);
                return;
            }

            if (delta.Entity == null || IsSelectedCharacter(delta.Entity))
                return;
            _entityRemovalVersions.Remove(key);
            bool alreadyRendered = _entityPlaceholders.ContainsKey(key);
            bool setPositionImmediately = delta.Kind == WorldEntityDeltaKind.Upsert
                || !alreadyRendered;
            UpsertEntityPlaceholder(delta.Entity, setPositionImmediately);
            if (delta.Kind == WorldEntityDeltaKind.Movement && delta.HasMovementTarget)
            {
                _remoteMovementTargets.Remove(key);
                _entityMovementTargets[key] = ToRelativeUnityPosition(
                    delta.TargetX, delta.TargetY, delta.TargetZ, _worldOrigin);
            }
            else if (delta.Kind == WorldEntityDeltaKind.Movement)
            {
                _entityMovementTargets.Remove(key);
                _remoteMovementTargets[key] = ToRelativeUnityPosition(
                    delta.Entity.X, delta.Entity.Y, delta.Entity.Z, _worldOrigin);
                if (delta.HasHeading)
                {
                    var heading = new Quaternion(delta.HeadingX, delta.HeadingY,
                        delta.HeadingZ, delta.HeadingW);
                    float headingMagnitude = Mathf.Sqrt(
                        heading.x * heading.x + heading.y * heading.y
                        + heading.z * heading.z + heading.w * heading.w);
                    if (headingMagnitude > 0.0001f)
                    {
                        heading = new Quaternion(heading.x / headingMagnitude,
                            heading.y / headingMagnitude, heading.z / headingMagnitude,
                            heading.w / headingMagnitude);
                        if (_entityPlaceholders.TryGetValue(key, out GameObject actor)
                            && actor != null)
                            actor.transform.rotation = heading;
                        UpdateRemoteMotionState(key, delta.MoveType, heading);
                    }
                }
            }
        }

        private async Task RemoveEntityAfterGracePeriodAsync(string key, int version)
        {
            try
            {
                CancellationToken token = _lifetime != null
                    ? _lifetime.Token : CancellationToken.None;
                await Task.Delay(500, token);
                if (!_entityRemovalVersions.TryGetValue(key, out int currentVersion)
                    || currentVersion != version)
                    return;

                _entityRemovalVersions.Remove(key);
                if (_entityPlaceholders.TryGetValue(key, out GameObject removed))
                    Destroy(removed);
                if (_entityVisualImporters.TryGetValue(key, out object importer))
                    PrototypeWorldBootstrap.DisposeImporter(importer);
                _entityVisualImporters.Remove(key);
                _entityPlaceholders.Remove(key);
                // Keep an in-flight load registered until its own finally block. This
                // prevents a later respawn from overlapping the abandoned import.
            }
            catch (OperationCanceledException) { }
        }

        private void UpdateRemoteMotionState(string key, byte moveType, Quaternion heading)
        {
            if (!_remoteMotionStates.TryGetValue(key, out RemoteMotionState state))
            {
                state = new RemoteMotionState();
                _remoteMotionStates[key] = state;
            }

            // CharDCMove values are stateful input events. Turning, jumping,
            // heartbeats, and movement-mode changes do not cancel translation.
            switch (moveType)
            {
                case 1: state.Forward = 1f; break;       // Forward start
                case 2: if (state.Forward > 0f) state.Forward = 0f; break;
                case 3: state.Forward = -1f; break;      // Reverse start
                case 4: if (state.Forward < 0f) state.Forward = 0f; break;
                case 5: state.Strafe = 1f; break;        // Strafe right start
                case 6: if (state.Strafe > 0f) state.Strafe = 0f; break;
                case 7: state.Strafe = -1f; break;       // Strafe left start
                case 8: if (state.Strafe < 0f) state.Strafe = 0f; break;
                case 21:                                // Full stop
                    state.Forward = 0f;
                    state.Strafe = 0f;
                    break;
            }

            Vector3 forward = heading * Vector3.forward;
            Vector3 right = heading * Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            Vector3 direction = forward.normalized * state.Forward
                + right.normalized * state.Strafe;
            if (direction.sqrMagnitude > 0.0001f)
            {
                _remoteMovementVelocities[key] = direction.normalized
                    * Mathf.Max(0.1f, remotePlayerRunSpeed);
            }
            else
            {
                _remoteMovementVelocities.Remove(key);
            }
        }

        private sealed class RemoteMotionState
        {
            public float Forward;
            public float Strafe;
        }

        private void UpsertEntityPlaceholder(NearbyEntity entity,
            bool setPositionImmediately = true)
        {
            string key = EntityKey(entity.IdentityType, entity.IdentityInstance);
            if (!_entityPlaceholders.TryGetValue(key, out GameObject actor) || actor == null)
            {
                actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                actor.transform.SetParent(_placeholderRoot, false);
                actor.transform.localScale = entity.Kind == NearbyEntityKind.Player
                    ? new Vector3(0.75f, 1f, 0.75f)
                    : new Vector3(0.65f, 0.9f, 0.65f);
                Renderer renderer = actor.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = entity.Kind == NearbyEntityKind.Player
                        ? new Color(0.2f, 0.65f, 1f)
                        : new Color(1f, 0.55f, 0.15f);
                AddLabel(actor.transform, string.Empty, 2.4f);
                _entityPlaceholders[key] = actor;
                Debug.Log($"[AO.Client] Entity appearance {key}: kind={entity.Kind}, "
                    + $"name='{entity.Name}', appearance={entity.Appearance} (0x{entity.Appearance:X8}), "
                    + $"family={entity.NpcFamily}, losHeight={entity.NpcLosHeight}, "
                    + $"npcData={entity.NpcUnknown}, monsterData={entity.MonsterData}, "
                    + $"monsterScale={entity.MonsterScale}, visualFlags={entity.VisualFlags}.");
            }

            if (entity.Kind == NearbyEntityKind.Npc && entity.MonsterData > 0
                && !_entityVisualImporters.ContainsKey(key) && _entityVisualLoads.Add(key))
                _ = LoadNpcVisualAsync(key, actor, entity);

            actor.name = $"{entity.Kind} {entity.Name} ({key})";
            if (setPositionImmediately)
            {
                actor.transform.position = ToRelativeUnityPosition(
                    entity.X, entity.Y, entity.Z, _worldOrigin);
            }
            TextMesh label = actor.GetComponentInChildren<TextMesh>();
            if (label != null)
            {
                int currentHealth = Mathf.Max(0, entity.Health - entity.HealthDamage);
                string health = entity.Health > 0 ? $"\nHP {currentHealth}/{entity.Health}" : string.Empty;
                label.text = $"{entity.Name}\nLv {entity.Level}{health}";
            }
        }

        private async Task LoadNpcVisualAsync(string key, GameObject actor, NearbyEntity entity)
        {
            object importer = null;
            object headImporter = null;
            GameObject visual = null;
            bool importGateHeld = false;
            try
            {
                CancellationToken token = _lifetime != null ? _lifetime.Token : CancellationToken.None;
                await _npcVisualImportGate.WaitAsync(token);
                importGateHeld = true;
                // Give Unity a scheduling boundary between nearby NPC visual creations.
                // This avoids committing a whole crowd of cached clones in one update.
                await Task.Yield();
                if (actor == null || !_entityPlaceholders.TryGetValue(key, out GameObject current)
                    || current != actor)
                    return;

                if (_npcVisualTemplates.TryGetValue(entity.MonsterData, out GameObject template)
                    && template != null)
                {
                    visual = Instantiate(template);
                    visual.SetActive(true);
                }
                else
                {
                    visual = await DirectCatMeshRuntime.InstantiateMonsterAsync(
                        (int)entity.MonsterData, actor.transform);
                    if (visual == null)
                    {
                    string path = await AOCharacterMeshResolver.ResolveMonsterAsync(
                        entity.MonsterData, entity.Appearance, token);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                        || actor == null || !_entityPlaceholders.TryGetValue(key, out current)
                        || current != actor)
                        return;

                    // Build one inactive appearance template per MonsterData record. Repeated
                    // guards and civilians can then clone it without reparsing the same GLBs.
                    visual = new GameObject($"MonsterData_{entity.MonsterData}_Template");
                    bool loaded = await PrototypeWorldBootstrap.TryInstantiateGlbWithReflection(
                        path, visual.transform, value => importer = value, disableAnimations: true);
                    if (!loaded)
                        return;

                    Transform headAttractor = FindNpcHeadAttractor(visual.transform);
                    string headPath = headAttractor != null
                        ? await AOCharacterMeshResolver.ResolveMonsterHeadAsync(
                            entity.MonsterData, entity.Appearance, token)
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(headPath) && File.Exists(headPath)
                        && headAttractor != null)
                    {
                        var headRoot = new GameObject("Head");
                        headRoot.transform.SetParent(headAttractor, false);
                        bool headLoaded = await PrototypeWorldBootstrap.TryInstantiateGlbWithReflection(
                            headPath, headRoot.transform, value => headImporter = value,
                            disableAnimations: true);
                        if (!headLoaded)
                            Destroy(headRoot);
                    }

                    // Cache every monster body, not only humanoids with a head attractor.
                    // Outdoor crowds commonly contain many instances of the same creature.
                    // Re-importing each non-humanoid GLB serially stalls the main thread and
                    // leaves later NPC types waiting behind the repeated imports.
                    visual.SetActive(false);
                    _npcVisualTemplates[entity.MonsterData] = visual;
                    _npcTemplateImporters.Add(importer);
                    importer = null;
                    if (headImporter != null)
                    {
                        _npcTemplateImporters.Add(headImporter);
                        headImporter = null;
                    }
                    visual = Instantiate(visual);
                    visual.SetActive(true);
                    }
                }

                float scale = entity.MonsterScale > 0 ? entity.MonsterScale / 100f : 1f;
                visual.transform.localScale = new Vector3(-scale, scale, scale);
                if (actor == null
                    || !_entityPlaceholders.TryGetValue(key, out current) || current != actor)
                    return;

                Renderer meshRenderer = visual.GetComponentInChildren<Renderer>(true);
                if (meshRenderer == null)
                    return;

                visual.name = $"MonsterData_{entity.MonsterData}";
                visual.transform.SetParent(actor.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = new Vector3(-scale, scale, scale);
                Renderer capsuleRenderer = actor.GetComponent<Renderer>();
                if (capsuleRenderer != null)
                    capsuleRenderer.enabled = false;
                actor.transform.localScale = Vector3.one;
                // A direct CatMesh visual has no glTF importer, but the key must
                // still be marked complete or every movement upsert launches a
                // fresh visual load for the same entity.
                _entityVisualImporters[key] = importer;
                importer = null;
                visual = null;
                Debug.Log($"[AO.Client] Loaded NPC '{entity.Name}' from MonsterData "
                    + $"{entity.MonsterData} at scale {scale:0.###}.");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AO.Client] NPC visual load failed for '{entity.Name}' "
                    + $"(MonsterData {entity.MonsterData}): {exception.Message}");
            }
            finally
            {
                if (importGateHeld)
                    _npcVisualImportGate.Release();
                _entityVisualLoads.Remove(key);
                if (visual != null)
                    Destroy(visual);
                if (importer != null)
                    PrototypeWorldBootstrap.DisposeImporter(importer);
                if (headImporter != null)
                    PrototypeWorldBootstrap.DisposeImporter(headImporter);

                // If the identity respawned while this import was finishing, start one
                // replacement load for the current actor now that the guard is released.
                if (_entityPlaceholders.TryGetValue(key, out GameObject currentActor)
                    && currentActor != null && currentActor != actor
                    && !_entityVisualImporters.ContainsKey(key)
                    && _entityVisualLoads.Add(key))
                    _ = LoadNpcVisualAsync(key, currentActor, entity);
            }
        }

        private static Transform FindNpcHeadAttractor(Transform root)
        {
            if (root == null)
                return null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && candidate.name.StartsWith(
                    "AOAttractor_Attractor01_head", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        private bool IsSelectedCharacter(NearbyEntity entity)
        {
            return _worldOrigin != null
                && string.Equals(entity.IdentityInstance.ToString(),
                    _worldOrigin.CharacterId, StringComparison.Ordinal);
        }

        private static string EntityKey(int identityType, int identityInstance)
        {
            return identityType + ":" + identityInstance;
        }

        private static Vector3 ToRelativeUnityPosition(float x, float y, float z,
            WorldBootstrapResult origin)
        {
            return new Vector3(x - origin.X, y - origin.Y, z - origin.Z);
        }

        private static void AddLabel(Transform parent, string value, float height)
        {
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = new Vector3(0f, height, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = value ?? string.Empty;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 36;
            label.color = Color.white;
        }

        private static string[] ReadCredentials(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "AORebirth credentials were not found. Expected the ignored local credential file.", path);
            string[] values = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (values.Length != 2)
                throw new InvalidDataException(
                    "The AORebirth credential file must contain username on line one and password on line two.");
            return values;
        }

        private async Task DisconnectBackendAsync()
        {
            _worldUpdates?.Cancel();
            Task worldUpdateTask = _worldUpdateTask;
            if (worldUpdateTask != null)
            {
                try
                {
                    await worldUpdateTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during session shutdown.
                }
            }
            _worldUpdates?.Dispose();
            _worldUpdates = null;
            _worldUpdateTask = null;
            if (_backend == null)
                return;
            IGameServerBackend backend = _backend;
            _backend = null;
            backend.StateChanged -= HandleBackendStateChanged;
            try
            {
                await backend.DisconnectAsync(CancellationToken.None);
            }
            finally
            {
                backend.Dispose();
            }
        }

        private async void OnDestroy()
        {
            _lifetime?.Cancel();
            foreach (object importer in _entityVisualImporters.Values)
                PrototypeWorldBootstrap.DisposeImporter(importer);
            _entityVisualImporters.Clear();
            _entityVisualLoads.Clear();
            try
            {
                await DisconnectBackendAsync();
            }
            catch (Exception)
            {
                // Destruction must not surface transport shutdown exceptions.
            }
            _lifetime?.Dispose();
        }
    }
}
