using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AO.Client.Authentication;
using AO.Client.Characters;
using AO.Client.World;

namespace AO.Client.Backends.AORebirth
{
    public sealed class AORebirthBackend : IGameServerBackend
    {
        private readonly string _clientVersion;
        private readonly string _authentication;
        private readonly string _loginPrime;
        private readonly string _loginPublicSeed;
        private TcpClient _client;
        private NetworkStream _stream;
        private Stream _worldStream;
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
        private ushort _messageId = 1;
        private bool _sentInPlay;
        private uint _zoneCookie1;
        private uint _zoneCookie2;
        private readonly Queue<string> _bootstrapServerMessages = new Queue<string>();

        private sealed class WorldPacketMailbox
        {
            private readonly ConcurrentQueue<AORebirthPacket> _packets =
                new ConcurrentQueue<AORebirthPacket>();
            private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
            private Exception _completionError;
            private bool _completed;

            public void Write(AORebirthPacket packet)
            {
                _packets.Enqueue(packet);
                _available.Release();
            }

            public void Complete(Exception error = null)
            {
                _completionError = error;
                _completed = true;
                _available.Release();
            }

            public async Task<AORebirthPacket> ReadAsync(CancellationToken cancellationToken)
            {
                while (true)
                {
                    await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (_packets.TryDequeue(out AORebirthPacket packet))
                        return packet;
                    if (_completed)
                        throw _completionError ?? new EndOfStreamException(
                            "The AORebirth world packet stream ended.");
                }
            }
        }

        private WorldPacketMailbox _worldPackets;
        private Task _worldReaderTask;
        private IReadOnlyList<CharacterSummary> _characters = Array.Empty<CharacterSummary>();
        private CharacterSummary _selectedCharacter;
        private int _currentPlayfieldId;
        private readonly Dictionary<string, NearbyEntity> _worldEntities =
            new Dictionary<string, NearbyEntity>();
        private readonly Dictionary<string, WorldObject> _worldObjects =
            new Dictionary<string, WorldObject>();
        private InventorySnapshot _inventory;
        private CharacterStateSnapshot _characterState;
        private bool _inventoryChanged;
        private bool _disposed;

        /// <summary>Hex for a selected-character SCFU that could not be decoded. Contains appearance data only.</summary>
        public string LastSelectedAppearanceDecodeFailure { get; private set; }

        public AORebirthBackend(string clientVersion = "18.8.62",
            string authentication = "aorebirth", string loginPrime = "",
            string loginPublicSeed = "")
        {
            if (string.IsNullOrWhiteSpace(clientVersion))
                throw new ArgumentException("An AO client version is required.", nameof(clientVersion));
            _clientVersion = clientVersion;
            _authentication = string.IsNullOrWhiteSpace(authentication)
                ? "aorebirth" : authentication.Trim().ToLowerInvariant();
            _loginPrime = loginPrime ?? string.Empty;
            _loginPublicSeed = loginPublicSeed ?? string.Empty;
        }

        public string BackendId => "aorebirth";

        public string DisplayName => "AORebirth";

        public BackendCapabilities Capabilities =>
            BackendCapabilities.Authentication
            | BackendCapabilities.CharacterList
            | BackendCapabilities.CharacterSelection
            | BackendCapabilities.WorldEntry
            | BackendCapabilities.WorldState
            | BackendCapabilities.Inventory
            | BackendCapabilities.ItemMovement;

        public ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        public async Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (State != ClientConnectionState.Disconnected && State != ClientConnectionState.Faulted)
                throw new InvalidOperationException($"Cannot connect while the backend is {State}.");
            if (endpoint.Address.Port <= 0)
                throw new ArgumentException("The server endpoint must specify a TCP port.", nameof(endpoint));

            await CloseTransportAsync().ConfigureAwait(false);
            _inventory = null;
            _characterState = null;
            _inventoryChanged = false;
            LastSelectedAppearanceDecodeFailure = null;
            SetState(ClientConnectionState.Connecting, $"Connecting to {endpoint.Name}.");
            _messageId = 1;
            _sentInPlay = false;
            try
            {
                _client = new TcpClient();
                using (cancellationToken.Register(() => _client?.Close()))
                {
                    await _client.ConnectAsync(endpoint.Address.Host, endpoint.Address.Port).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                _stream = _client.GetStream();
                SetState(ClientConnectionState.Connected, $"Connected to {endpoint.Name}.");
            }
            catch
            {
                await CloseTransportAsync().ConfigureAwait(false);
                SetState(ClientConnectionState.Faulted, "Connection failed.");
                throw;
            }
        }

        public async Task<AuthenticationResult> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (State != ClientConnectionState.Connected)
                throw new InvalidOperationException("Connect before authenticating.");

            SetState(ClientConnectionState.Authenticating, "Authenticating.");
            try
            {
                await WritePacketAsync(
                    AORebirthProtocol.CreateUserLogin(request.Username, _clientVersion),
                    cancellationToken).ConfigureAwait(false);
                AORebirthPacket saltPacket = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                byte[] salt = AORebirthProtocol.ReadServerSalt(saltPacket);
                string loginKey = _authentication == "aorebirth"
                    ? AORebirthLoginKey.Create(request.Username, request.Secret, salt)
                    : AOLegacyLoginKey.Create(request.Username, request.Secret, salt,
                        _loginPrime, _loginPublicSeed);

                await WritePacketAsync(
                    AORebirthProtocol.CreateUserCredentials(request.Username, loginKey),
                    cancellationToken).ConfigureAwait(false);
                AORebirthPacket response = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (response.SystemMessageType == AORebirthProtocol.LoginError)
                {
                    int error = AORebirthProtocol.ReadLoginError(response);
                    _characters = Array.Empty<CharacterSummary>();
                    SetState(ClientConnectionState.Connected, "Authentication rejected.");
                    string message = error == 0x14 ? "This account is already logged in."
                        : error == 0x6A ? "Invalid username or password."
                        : error == 0x6C ? "This account is banned or does not have an active subscription."
                        : $"The server rejected authentication (error 0x{error:X8}).";
                    return AuthenticationResult.Failure(message);
                }

                if (response.SystemMessageType != AORebirthProtocol.CharacterList)
                    throw new InvalidDataException($"Unexpected login response 0x{response.SystemMessageType:X8}.");

                _characters = AORebirthProtocol.ReadCharacterList(response);
                SetState(ClientConnectionState.Authenticated, "Authentication succeeded.");
                return AuthenticationResult.Success(request.Username, "Character list received.");
            }
            catch
            {
                SetState(ClientConnectionState.Faulted, "Authentication failed.");
                throw;
            }
        }

        public Task<IReadOnlyList<CharacterSummary>> GetCharactersAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (State != ClientConnectionState.Authenticated && State != ClientConnectionState.InWorld)
                throw new InvalidOperationException("Authenticate before requesting characters.");
            return Task.FromResult(_characters);
        }

        public async Task<WorldEntryResult> EnterWorldAsync(
            string characterId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != ClientConnectionState.Authenticated)
                throw new InvalidOperationException("Authenticate before selecting a character.");
            if (!int.TryParse(characterId, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedCharacterId))
                throw new ArgumentException("AORebirth character IDs must be integers.", nameof(characterId));

            CharacterSummary selected = null;
            foreach (CharacterSummary character in _characters)
            {
                if (string.Equals(character.Id, characterId, StringComparison.Ordinal))
                {
                    selected = character;
                    break;
                }
            }
            if (selected == null)
                return WorldEntryResult.Failure("The selected character is not in the authenticated character list.");

            SetState(ClientConnectionState.EnteringWorld, $"Selecting character {selected.Name}.");
            try
            {
                await WritePacketAsync(
                    AORebirthProtocol.CreateSelectCharacter(parsedCharacterId),
                    cancellationToken).ConfigureAwait(false);
                SetState(ClientConnectionState.EnteringWorld,
                    $"Character selection sent for {selected.Name} ({parsedCharacterId}); waiting for ZoneInfo.");
                AORebirthPacket response = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (response.SystemMessageType == AORebirthProtocol.LoginError)
                {
                    SetState(ClientConnectionState.Authenticated, "Character selection rejected.");
                    return WorldEntryResult.Failure("AORebirth rejected the selected character.");
                }

                AORebirthZoneInfo zone = AORebirthProtocol.ReadZoneInfo(response);
                if (zone.CharacterId != parsedCharacterId)
                    throw new InvalidDataException("AORebirth returned zone information for another character.");
                SetState(ClientConnectionState.EnteringWorld,
                    $"ZoneInfo received; connecting to {zone.Address}:{zone.Port}.");
                _zoneCookie1 = zone.Cookie1;
                _zoneCookie2 = zone.Cookie2;

                await CloseTransportAsync().ConfigureAwait(false);
                _client = new TcpClient();
                _messageId = 1;
                using (cancellationToken.Register(() => _client?.Close()))
                {
                    await _client.ConnectAsync(zone.Address.ToString(), zone.Port).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                _stream = _client.GetStream();

                // ZoneEngine sends InitiateCompression from its ZoneLogin handler,
                // so ZoneLogin must be the first packet on the new zone socket.
                // Live/PRK also require the ZoneInfo cookies in this login body.
                await WritePacketAsync(
                    AORebirthProtocol.CreateZoneLogin(
                        parsedCharacterId, zone.Cookie1, zone.Cookie2),
                    cancellationToken).ConfigureAwait(false);
                SetState(ClientConnectionState.EnteringWorld,
                    "Zone login sent; waiting for compression negotiation.");

                AORebirthPacket zoneResponse = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (zoneResponse.PacketType != AORebirthProtocol.InitiateCompressionPacketType)
                {
                    throw new InvalidDataException(
                        $"Expected compression negotiation, received packet type 0x{zoneResponse.PacketType:X4}.");
                }

                SetState(ClientConnectionState.EnteringWorld,
                    "Compression negotiation received; opening the compressed world stream.");
                await InitializeWorldCompressionAsync(cancellationToken).ConfigureAwait(false);

                _selectedCharacter = selected;
                SetState(ClientConnectionState.InWorld, $"Entered the zone as {selected.Name}.");
                return WorldEntryResult.Success(
                    selected.Id,
                    selected.PlayfieldId,
                    $"Zone handoff completed at {zone.Address}:{zone.Port}.");
            }
            catch
            {
                await CloseTransportAsync().ConfigureAwait(false);
                SetState(ClientConnectionState.Faulted, "Character selection or zone handoff failed.");
                throw;
            }
        }

        public async Task<WorldBootstrapResult> ReceiveWorldBootstrapAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != ClientConnectionState.InWorld || _worldStream == null || _selectedCharacter == null)
                throw new InvalidOperationException("Enter the world before receiving its bootstrap state.");

            const int MaximumBootstrapPackets = 64;
            var received = new List<string>();
            for (int packetIndex = 1; packetIndex <= MaximumBootstrapPackets; packetIndex++)
            {
                AORebirthPacket packet;
                try
                {
                    packet = await ReadWorldPacketAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    throw new InvalidDataException(
                        "AORebirth world framing failed before PlayfieldAnarchyF. Received: "
                        + string.Join(", ", received),
                        exception);
                }
                received.Add(
                    $"type=0x{packet.PacketType:X4} body={BitConverter.ToString(packet.Body, 0, Math.Min(12, packet.Body.Length)).Replace("-", string.Empty)}");
                if (AORebirthProtocol.TryReadChatText(packet, out string startupMessage))
                    _bootstrapServerMessages.Enqueue(startupMessage);
                CaptureInventory(packet);
                CaptureCharacterState(packet);
                if (!AORebirthProtocol.TryReadPlayfieldBootstrap(packet, out AORebirthPlayfieldBootstrap bootstrap))
                {
                    int ignoredMovement = 0, ignoredUnmatched = 0;
                    ApplyWorldPacket(packet, new List<WorldEntityDelta>(), ref ignoredMovement, ref ignoredUnmatched);
                    continue;
                }

                if (bootstrap.CharacterId != 0 && !string.Equals(
                        bootstrap.CharacterId.ToString(CultureInfo.InvariantCulture),
                        _selectedCharacter.Id,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The playfield bootstrap belongs to another character.");
                }

                _currentPlayfieldId = bootstrap.PlayfieldId;
                return new WorldBootstrapResult(
                    _selectedCharacter.Id,
                    bootstrap.PlayfieldId,
                    bootstrap.X,
                    bootstrap.Y,
                    bootstrap.Z,
                    packetIndex);
            }

            throw new InvalidDataException(
                $"AORebirth did not send PlayfieldAnarchyF within {MaximumBootstrapPackets} packets.");
        }

        public async Task<NearbyEntitiesResult> ReceiveNearbyEntitiesAsync(
            int maximumPackets,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != ClientConnectionState.InWorld || _worldStream == null || _currentPlayfieldId == 0)
                throw new InvalidOperationException("Receive the world bootstrap before nearby entities.");
            if (maximumPackets <= 0 || maximumPackets > 4096)
                throw new ArgumentOutOfRangeException(nameof(maximumPackets));

            var entities = new Dictionary<string, NearbyEntity>(_worldEntities);
            int packetsObserved = 0;
            Stopwatch batchClock = Stopwatch.StartNew();
            while (packetsObserved < maximumPackets && batchClock.Elapsed < TimeSpan.FromSeconds(10))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AORebirthPacket packet;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    idle.CancelAfter(TimeSpan.FromMilliseconds(750));
                    try
                    {
                        packet = await ReadWorldPacketAsync(idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (IOException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                packetsObserved++;
                if (AORebirthProtocol.TryReadChatText(packet, out string startupMessage))
                    _bootstrapServerMessages.Enqueue(startupMessage);
                CaptureInventory(packet);
                CaptureCharacterState(packet);
                if (AORebirthProtocol.TryReadAppearanceUpdate(packet, out int type, out int id, out var appearance))
                {
                    string appearanceKey = EntityKey(type, id);
                    if (_worldEntities.TryGetValue(appearanceKey, out var previous))
                    {
                        var updated = previous.WithEquipmentAppearance(appearance);
                        _worldEntities[appearanceKey] = updated;
                        entities[appearanceKey] = updated;
                    }
                    continue;
                }
                if (!AORebirthProtocol.TryReadNearbyEntity(packet, _currentPlayfieldId, out NearbyEntity entity))
                {
                    CaptureSelectedAppearanceFailure(packet);
                    if (AORebirthProtocol.TryReadWorldObject(packet, _currentPlayfieldId, out WorldObject worldObject))
                        StoreWorldObject(worldObject);
                    continue;
                }
                string key = entity.IdentityType.ToString(CultureInfo.InvariantCulture)
                             + ":" + entity.IdentityInstance.ToString(CultureInfo.InvariantCulture);
                entities[key] = entity;
                _worldEntities[key] = entity;
                ResolveLinkedObjectPositions(entity);
            }

            if (!_sentInPlay && _selectedCharacter != null)
            {
                await WritePacketAsync(AORebirthProtocol.CreateCharInPlay(
                    int.Parse(_selectedCharacter.Id, CultureInfo.InvariantCulture)), cancellationToken).ConfigureAwait(false);
                _sentInPlay = true;
            }
            return new NearbyEntitiesResult(new List<NearbyEntity>(entities.Values), packetsObserved);
        }

        public Task<IReadOnlyList<WorldObject>> GetWorldObjectsAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (State != ClientConnectionState.InWorld)
                throw new InvalidOperationException("Enter the world before requesting world objects.");
            return Task.FromResult<IReadOnlyList<WorldObject>>(
                new List<WorldObject>(_worldObjects.Values));
        }

        public async Task MoveItemAsync(ItemLocation source, ItemLocation destination,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != ClientConnectionState.InWorld || _selectedCharacter == null)
                throw new InvalidOperationException("Enter the world before moving items.");
            int characterId = int.Parse(_selectedCharacter.Id, CultureInfo.InvariantCulture);
            await WritePacketAsync(AORebirthProtocol.CreateItemMove(characterId, source, destination),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SendPlayerMovementAsync(PlayerMovementUpdate movement,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (movement == null)
                throw new ArgumentNullException(nameof(movement));
            if (State != ClientConnectionState.InWorld || _selectedCharacter == null)
                throw new InvalidOperationException("Enter the world before sending movement.");
            if (!int.TryParse(_selectedCharacter.Id, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int characterId))
                throw new InvalidDataException("The selected AORebirth character ID is invalid.");
            await WritePacketAsync(
                AORebirthProtocol.CreatePlayerMovement(characterId, movement),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SendChatTextAsync(string text, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Chat text is required.", nameof(text));
            if (State != ClientConnectionState.InWorld || _selectedCharacter == null)
                throw new InvalidOperationException("Enter the world before sending chat text.");
            if (!int.TryParse(_selectedCharacter.Id, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int characterId))
                throw new InvalidDataException("The selected AORebirth character ID is invalid.");
            await WritePacketAsync(AORebirthProtocol.CreateTextMessage(characterId, text),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<WorldDeltaBatch> ReceiveWorldDeltasAsync(
            int maximumPackets,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != ClientConnectionState.InWorld || _worldStream == null || _currentPlayfieldId == 0)
                throw new InvalidOperationException("Receive the world bootstrap before world deltas.");
            if (maximumPackets <= 0 || maximumPackets > 4096)
                throw new ArgumentOutOfRangeException(nameof(maximumPackets));

            var deltas = new List<WorldEntityDelta>();
            var serverMessages = new List<string>();
            int packetsObserved = 0;
            int movementPacketsObserved = 0;
            int unmatchedMovementPackets = 0;
            var packetKindsObserved = new Dictionary<string, int>();
            var packetSamplesObserved = new Dictionary<string, string>();
            WorldBootstrapResult zoneTransfer = null;
            Stopwatch batchClock = Stopwatch.StartNew();
            while (packetsObserved < maximumPackets
                   && batchClock.Elapsed < TimeSpan.FromMilliseconds(100))
            {
                AORebirthPacket packet;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    idle.CancelAfter(packetsObserved > 0
                        ? TimeSpan.FromMilliseconds(25)
                        : TimeSpan.FromMilliseconds(750));
                    try
                    {
                        packet = await ReadWorldPacketAsync(idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (IOException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                packetsObserved++;
                string packetKind;
                if (packet.PacketType == AORebirthProtocol.N3PacketType
                    && packet.Body.Length >= 4)
                {
                    int n3MessageType = (packet.Body[0] << 24)
                                        | (packet.Body[1] << 16)
                                        | (packet.Body[2] << 8)
                                        | packet.Body[3];
                    packetKind = $"n3:0x{n3MessageType:X8}/len:{packet.Body.Length}";
                }
                else if (packet.PacketType == 1)
                {
                    packetKind = $"sys:0x{packet.SystemMessageType:X8}";
                }
                else
                {
                    packetKind = $"packet:0x{packet.PacketType:X4}/len:{packet.Body.Length}";
                }
                packetKindsObserved.TryGetValue(packetKind, out int packetKindCount);
                packetKindsObserved[packetKind] = packetKindCount + 1;
                if (!packetSamplesObserved.ContainsKey(packetKind))
                {
                    packetSamplesObserved[packetKind] = BitConverter.ToString(packet.Body)
                        .Replace("-", string.Empty);
                }
                if (AORebirthProtocol.TryReadZoneRedirection(packet,
                        out IPAddress redirectAddress, out int redirectPort))
                {
                    zoneTransfer = await ReconnectToZoneAsync(
                        redirectAddress, redirectPort, cancellationToken).ConfigureAwait(false);
                    serverMessages.Add($"Zone transfer complete: PF {zoneTransfer.PlayfieldId}.");
                    break;
                }
                ApplyWorldPacket(packet, deltas,
                    ref movementPacketsObserved, ref unmatchedMovementPackets);
                if (AORebirthProtocol.TryReadChatText(packet, out string serverMessage))
                    serverMessages.Add(serverMessage);
            }
            InventorySnapshot inventory = _inventoryChanged ? _inventory : null;
            _inventoryChanged = false;
            if (_bootstrapServerMessages.Count > 0)
            {
                serverMessages.InsertRange(0, _bootstrapServerMessages);
                _bootstrapServerMessages.Clear();
            }
            return new WorldDeltaBatch(deltas, new List<NearbyEntity>(_worldEntities.Values),
                packetsObserved, movementPacketsObserved, unmatchedMovementPackets,
                packetKindsObserved, packetSamplesObserved, inventory, serverMessages,
                zoneTransfer);
        }

        private async Task<WorldBootstrapResult> ReconnectToZoneAsync(IPAddress address,
            int port, CancellationToken cancellationToken)
        {
            if (_selectedCharacter == null
                || !int.TryParse(_selectedCharacter.Id, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int characterId))
                throw new InvalidDataException("Cannot redirect without a selected character.");

            SetState(ClientConnectionState.EnteringWorld,
                $"Zone redirect received; reconnecting to {address}:{port}.");
            await CloseTransportAsync().ConfigureAwait(false);
            _client = new TcpClient();
            _messageId = 1;
            _sentInPlay = false;
            using (cancellationToken.Register(() => _client?.Close()))
            {
                await _client.ConnectAsync(address.ToString(), port).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _stream = _client.GetStream();
            await WritePacketAsync(AORebirthProtocol.CreateZoneLogin(
                characterId, _zoneCookie1, _zoneCookie2), cancellationToken).ConfigureAwait(false);
            AORebirthPacket response = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
            if (response.PacketType != AORebirthProtocol.InitiateCompressionPacketType)
                throw new InvalidDataException(
                    $"Expected compression after zone redirect, received 0x{response.PacketType:X4}.");
            await InitializeWorldCompressionAsync(cancellationToken).ConfigureAwait(false);
            SetState(ClientConnectionState.InWorld,
                $"Zone redirect connected at {address}:{port}; waiting for playfield bootstrap.");
            WorldBootstrapResult bootstrap = await ReceiveWorldBootstrapAsync(cancellationToken)
                .ConfigureAwait(false);
            await WritePacketAsync(AORebirthProtocol.CreateCharInPlay(characterId),
                cancellationToken).ConfigureAwait(false);
            _sentInPlay = true;
            _worldEntities.Clear();
            _worldObjects.Clear();
            return bootstrap;
        }

        public InventorySnapshot GetInventorySnapshot() => _inventory;

        public CharacterStateSnapshot GetCharacterStateSnapshot() => _characterState;

        private void CaptureSelectedAppearanceFailure(AORebirthPacket packet)
        {
            if (LastSelectedAppearanceDecodeFailure != null || packet?.PacketType != AORebirthProtocol.N3PacketType
                || packet.Body.Length < 12 || _selectedCharacter == null
                || !int.TryParse(_selectedCharacter.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int selected))
                return;
            byte[] bytes = packet.Body;
            int message = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
            int instance = (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11];
            if (message == AORebirthProtocol.SimpleCharFullUpdate && instance == selected)
                LastSelectedAppearanceDecodeFailure = BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        private void CaptureCharacterState(AORebirthPacket packet)
        {
            if (AORebirthProtocol.TryReadCharacterState(packet, out CharacterStateSnapshot snapshot))
            {
                _characterState = snapshot;
                // FullCharacter carries the initial main page as well as worn slots.
                var mainItems = new List<InventoryEntrySnapshot>();
                foreach (var entry in snapshot.Slots)
                    if (entry.Slot >= 0x40 && entry.Slot < 0x5E)
                        mainItems.Add(entry);
                _inventory = new InventorySnapshot(30, 50000,
                    _selectedCharacter != null && int.TryParse(_selectedCharacter.Id, out int owner) ? owner : 0,
                    0, mainItems);
                _inventoryChanged = true;
                return;
            }

            if (_characterState == null
                || _selectedCharacter == null
                || !int.TryParse(_selectedCharacter.Id, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int characterId)
                || !AORebirthProtocol.TryReadStatUpdate(packet, out AORebirthStatUpdate update)
                || update.Instance != characterId)
                return;

            var merged = new Dictionary<int, int>(_characterState.Stats);
            foreach (var pair in update.Values)
                merged[pair.Key] = pair.Value;
            _characterState = new CharacterStateSnapshot(
                _characterState.Slots, _characterState.UploadedNanoIds, merged,
                isStatUpdateOnly: true);
        }

        private void CaptureInventory(AORebirthPacket packet)
        {
            if (!AORebirthProtocol.TryReadInventoryUpdate(packet, out InventorySnapshot snapshot)
                || !snapshot.IsMainInventory)
                return;
            _inventory = snapshot;
            _inventoryChanged = true;
        }

        private void ApplyWorldPacket(AORebirthPacket packet, List<WorldEntityDelta> deltas,
            ref int movementPacketsObserved, ref int unmatchedMovementPackets)
        {
            CaptureInventory(packet);
            CaptureCharacterState(packet);
            if (_selectedCharacter != null
                && int.TryParse(_selectedCharacter.Id, out int ownerId)
                && AORebirthProtocol.TryApplyItemMove(packet, ownerId, _inventory, _characterState,
                    out InventorySnapshot movedInventory, out CharacterStateSnapshot movedCharacter))
            {
                _inventory = movedInventory;
                _characterState = movedCharacter;
                _inventoryChanged = true;
            }
            if (AORebirthProtocol.TryReadNearbyEntity(packet, _currentPlayfieldId, out NearbyEntity spawned))
            {
                string key = EntityKey(spawned.IdentityType, spawned.IdentityInstance);
                if (_worldEntities.TryGetValue(key, out var previous) && previous.EquipmentAppearance != null)
                    spawned = spawned.WithEquipmentAppearance(spawned.EquipmentAppearance == null
                        ? previous.EquipmentAppearance
                        : spawned.EquipmentAppearance.WithHead(spawned.EquipmentAppearance.HeadMeshId ?? previous.EquipmentAppearance.HeadMeshId));
                _worldEntities[key] = spawned;
                ResolveLinkedObjectPositions(spawned);
                deltas.Add(new WorldEntityDelta(WorldEntityDeltaKind.Upsert,
                    spawned.IdentityType, spawned.IdentityInstance, spawned));
                return;
            }
            if (AORebirthProtocol.TryReadAppearanceUpdate(packet, out int appearanceType,
                out int appearanceId, out CharacterAppearanceSnapshot appearance))
            {
                string key = EntityKey(appearanceType, appearanceId);
                if (_worldEntities.TryGetValue(key, out NearbyEntity current))
                {
                    var changed = current.WithEquipmentAppearance(appearance);
                    _worldEntities[key] = changed;
                    deltas.Add(new WorldEntityDelta(WorldEntityDeltaKind.Appearance,
                        appearanceType, appearanceId, changed));
                }
                return;
            }
            if (AORebirthProtocol.TryReadWorldObject(packet, _currentPlayfieldId, out WorldObject worldObject))
            {
                StoreWorldObject(worldObject);
                return;
            }
            if (AORebirthProtocol.TryReadMovement(packet, out AORebirthMovement movement)
                || AORebirthProtocol.TryReadDropPosition(packet, out movement)
                || AORebirthProtocol.TryReadMobPathMovement(packet, out movement))
            {
                movementPacketsObserved++;
                string key = EntityKey(movement.Type, movement.Instance);
                if (_worldEntities.TryGetValue(key, out NearbyEntity current))
                {
                    NearbyEntity moved = current.WithPosition(movement.X, movement.Y, movement.Z);
                    _worldEntities[key] = moved;
                    deltas.Add(new WorldEntityDelta(WorldEntityDeltaKind.Movement,
                        movement.Type, movement.Instance, moved,
                        movement.HasDestination, movement.DestinationX,
                        movement.DestinationY, movement.DestinationZ,
                        movement.MoveType, movement.HasHeading,
                        movement.HeadingX, movement.HeadingY,
                        movement.HeadingZ, movement.HeadingW));
                }
                else
                {
                    unmatchedMovementPackets++;
                }
                return;
            }
            if (AORebirthProtocol.TryReadStatUpdate(packet, out AORebirthStatUpdate visualStats)
                && (visualStats.Values.ContainsKey(64) || visualStats.Values.ContainsKey(673)))
            {
                string key = EntityKey(visualStats.Type, visualStats.Instance);
                if (_worldEntities.TryGetValue(key, out NearbyEntity current) && current.EquipmentAppearance != null)
                {
                    var outfit = current.EquipmentAppearance;
                    int? head = visualStats.Values.TryGetValue(64, out int newHead) ? newHead : outfit.HeadMeshId;
                    int flags = visualStats.Values.TryGetValue(673, out int newFlags) ? newFlags : outfit.VisualFlags;
                    var changed = current.WithEquipmentAppearance(new CharacterAppearanceSnapshot(outfit.Textures, outfit.Meshes, flags, head));
                    _worldEntities[key] = changed;
                    deltas.Add(new WorldEntityDelta(WorldEntityDeltaKind.Appearance, visualStats.Type, visualStats.Instance, changed));
                }
            }
            if (AORebirthProtocol.TryReadHealthStats(packet, out AORebirthHealthStats health))
            {
                string key = EntityKey(health.Type, health.Instance);
                if (_worldEntities.TryGetValue(key, out NearbyEntity current))
                {
                    int maximum = health.Maximum ?? current.Health;
                    int currentValue = health.Current ?? Math.Max(0, current.Health - current.HealthDamage);
                    NearbyEntity changed = current.WithHealth(maximum, currentValue);
                    _worldEntities[key] = changed;
                    deltas.Add(new WorldEntityDelta(WorldEntityDeltaKind.Stats,
                        health.Type, health.Instance, changed));
                }
                return;
            }
            if (AORebirthProtocol.TryReadDespawn(packet, out AORebirthIdentity removed))
            {
                string key = EntityKey(removed.Type, removed.Instance);
                _worldEntities.Remove(key);
                deltas.Add(new WorldEntityDelta(WorldEntityDeltaKind.Remove,
                    removed.Type, removed.Instance, null));
            }
        }

        private static string EntityKey(int type, int instance)
        {
            return type.ToString(CultureInfo.InvariantCulture) + ":"
                   + instance.ToString(CultureInfo.InvariantCulture);
        }

        private void StoreWorldObject(WorldObject worldObject)
        {
            if (!worldObject.HasPosition && worldObject.LinkedIdentityInstance != 0
                && _worldEntities.TryGetValue(
                    EntityKey(worldObject.LinkedIdentityType, worldObject.LinkedIdentityInstance),
                    out NearbyEntity linked))
            {
                worldObject = worldObject.WithLinkedPosition(linked);
            }
            _worldObjects[EntityKey(worldObject.IdentityType, worldObject.IdentityInstance)] = worldObject;
        }

        private void ResolveLinkedObjectPositions(NearbyEntity entity)
        {
            var replacements = new List<WorldObject>();
            foreach (WorldObject worldObject in _worldObjects.Values)
            {
                if (!worldObject.HasPosition
                    && worldObject.LinkedIdentityType == entity.IdentityType
                    && worldObject.LinkedIdentityInstance == entity.IdentityInstance)
                    replacements.Add(worldObject.WithLinkedPosition(entity));
            }
            foreach (WorldObject replacement in replacements)
                _worldObjects[EntityKey(replacement.IdentityType, replacement.IdentityInstance)] = replacement;
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            SetState(ClientConnectionState.Disconnecting, "Disconnecting.");
            await CloseTransportAsync().ConfigureAwait(false);
            _characters = Array.Empty<CharacterSummary>();
            _selectedCharacter = null;
            _currentPlayfieldId = 0;
            _bootstrapServerMessages.Clear();
            _worldEntities.Clear();
            _worldObjects.Clear();
            SetState(ClientConnectionState.Disconnected, "Disconnected.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _worldStream?.Dispose();
            _stream?.Dispose();
            _client?.Close();
            _worldStream = null;
            _stream = null;
            _client = null;
            _characters = Array.Empty<CharacterSummary>();
            _selectedCharacter = null;
            _currentPlayfieldId = 0;
            _bootstrapServerMessages.Clear();
            _worldEntities.Clear();
            _worldObjects.Clear();
            State = ClientConnectionState.Disconnected;
        }

        private async Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var stream = _stream ?? throw new InvalidOperationException("No AO transport is connected.");
                // Client traffic remains plaintext and four-byte aligned after negotiation.
                byte[] wire = new byte[(packet.Length + 3) & ~3];
                Buffer.BlockCopy(packet, 0, wire, 0, packet.Length);
                wire[0] = (byte)(_messageId >> 8); wire[1] = (byte)_messageId;
                if (++_messageId == 0xFFFF) _messageId = 1;
                if (wire[2] == 0 && wire[3] == AORebirthProtocol.N3PacketType)
                    wire[15] = 2;
                await stream.WriteAsync(wire, 0, wire.Length, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }

        private async Task<AORebirthPacket> ReadPacketAsync(CancellationToken cancellationToken)
        {
            return await ReadPacketAsync(_stream, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AORebirthPacket> ReadPacketAsync(
            Stream input,
            CancellationToken cancellationToken)
        {
            byte[] header = await ReadExactAsync(input, 16, cancellationToken).ConfigureAwait(false);
            int packetType = (header[2] << 8) | header[3];
            int size = (short)((header[6] << 8) | header[7]);
            int minimumSize = packetType == 1 ? 20 : 16;
            if (size < minimumSize || size > 1024 * 1024)
                throw new InvalidDataException(
                    $"Invalid AO packet size: {size}; header={BitConverter.ToString(header).Replace("-", string.Empty)}.");

            // Login packets are aligned to four bytes. Once zlib is enabled the
            // zone stream concatenates exact serialized packet sizes with no pad.
            int wireSize = ReferenceEquals(input, _worldStream)
                ? size
                : (size + 3) & ~3;
            byte[] remainder = await ReadExactAsync(
                input, wireSize - 16, cancellationToken).ConfigureAwait(false);
            byte[] body = new byte[size - 16];
            Buffer.BlockCopy(remainder, 0, body, 0, body.Length);
            int systemMessageType = 0;
            if (packetType == 1 && body.Length >= 4)
            {
                systemMessageType = (body[0] << 24)
                                    | (body[1] << 16)
                                    | (body[2] << 8)
                                    | body[3];
            }
            return new AORebirthPacket(packetType, systemMessageType, body,
                new BigEndianReader(header, 8).ReadInt32());
        }

        private async Task<byte[]> ReadExactAsync(int length, CancellationToken cancellationToken)
        {
            return await ReadExactAsync(_stream, length, cancellationToken).ConfigureAwait(false);
        }

        private async Task<byte[]> ReadExactAsync(
            Stream input,
            int length,
            CancellationToken cancellationToken)
        {
            if (input == null)
                throw new InvalidOperationException("No AORebirth transport is connected.");
            byte[] buffer = new byte[length];
            int offset = 0;
            // Unity's Mono NetworkStream may leave ReadAsync pending after token
            // cancellation. Closing the owning socket is the only reliable way to
            // interrupt a stalled login/zone-handoff read.
            using (cancellationToken.Register(() =>
            {
                if (!ReferenceEquals(input, _worldStream))
                {
                    try { _client?.Close(); }
                    catch { }
                }
            }))
            {
                while (offset < length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read;
                    try
                    {
                        read = ReferenceEquals(input, _worldStream)
                            ? input.Read(buffer, offset, length - offset)
                            : await input.ReadAsync(
                                buffer, offset, length - offset, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested
                        && (exception is IOException || exception is ObjectDisposedException))
                    {
                        throw new OperationCanceledException("AO packet read was canceled.", exception, cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (read <= 0)
                        throw new EndOfStreamException("The remote AO server closed the connection.");
                    offset += read;
                }
            }
            return buffer;
        }

        private Task CloseTransportAsync()
        {
            _worldPackets?.Complete();
            _worldStream?.Dispose();
            _stream?.Dispose();
            _client?.Close();
            _worldPackets = null;
            _worldReaderTask = null;
            _worldStream = null;
            _stream = null;
            _client = null;
            return Task.CompletedTask;
        }

        private async Task InitializeWorldCompressionAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            // AORebirth switches this socket to a continuous RFC 1950 zlib stream.
#if UNITY_5_3_OR_NEWER
            // Unity's supported framework does not expose ZLibStream. AORebirth
            // sends RFC 1950, so consume its two-byte header and let the portable
            // DeflateStream decode the continuous RFC 1951 payload.
            byte[] zlibHeader = await ReadExactAsync(_stream, 2, cancellationToken)
                .ConfigureAwait(false);
            int combinedHeader = (zlibHeader[0] << 8) | zlibHeader[1];
            if ((zlibHeader[0] & 0x0F) != 8 || combinedHeader % 31 != 0)
                throw new InvalidDataException("AORebirth sent an invalid RFC 1950 zlib header.");
            if ((zlibHeader[1] & 0x20) != 0)
                throw new InvalidDataException("AORebirth requested an unsupported preset zlib dictionary.");
            _worldStream = new DeflateStream(
                _stream,
                System.IO.Compression.CompressionMode.Decompress,
                true);
#else
            _worldStream = new ZLibStream(
                _stream,
                System.IO.Compression.CompressionMode.Decompress,
                true);
#endif
            StartWorldReader();
        }

        private void StartWorldReader()
        {
            Stream input = _worldStream;
            var packets = new WorldPacketMailbox();
            _worldPackets = packets;
            _worldReaderTask = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        AORebirthPacket packet = await ReadPacketAsync(input, CancellationToken.None)
                            .ConfigureAwait(false);
                        if (_selectedCharacter != null && int.TryParse(_selectedCharacter.Id, out int owner))
                        {
                            byte[] pong = AORebirthProtocol.CreatePong(packet, owner);
                            if (pong != null)
                            {
                                await WritePacketAsync(pong, CancellationToken.None).ConfigureAwait(false);
                                continue;
                            }
                        }
                        packets.Write(packet);
                    }
                }
                catch (Exception exception)
                {
                    packets.Complete(exception);
                }
            });
        }

        private async Task<AORebirthPacket> ReadWorldPacketAsync(CancellationToken cancellationToken)
        {
            WorldPacketMailbox packets = _worldPackets;
            if (packets == null)
                throw new InvalidOperationException("The AORebirth world reader is not running.");
            return await packets.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        private void SetState(ClientConnectionState state, string message)
        {
            ClientConnectionState previous = State;
            State = state;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previous, state, message));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AORebirthBackend));
        }
    }
}
