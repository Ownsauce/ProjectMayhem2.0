using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AO.Client.Authentication;
using AO.Client.Characters;
using AO.Client.World;

namespace AO.Client
{
    /// <summary>
    /// Stable client-facing boundary implemented by each supported AO server adapter.
    /// Backend packet and authentication details must not escape this interface.
    /// </summary>
    public interface IGameServerBackend : IDisposable
    {
        string BackendId { get; }

        string DisplayName { get; }

        BackendCapabilities Capabilities { get; }

        ClientConnectionState State { get; }

        event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default);

        Task<AuthenticationResult> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CharacterSummary>> GetCharactersAsync(
            CancellationToken cancellationToken = default);

        Task<WorldEntryResult> EnterWorldAsync(
            string characterId,
            CancellationToken cancellationToken = default);

        Task<WorldBootstrapResult> ReceiveWorldBootstrapAsync(
            CancellationToken cancellationToken = default);

        Task<NearbyEntitiesResult> ReceiveNearbyEntitiesAsync(
            int maximumPackets,
            CancellationToken cancellationToken = default);

        Task<WorldDeltaBatch> ReceiveWorldDeltasAsync(
            int maximumPackets,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<WorldObject>> GetWorldObjectsAsync(
            CancellationToken cancellationToken = default);

        InventorySnapshot GetInventorySnapshot();

        CharacterStateSnapshot GetCharacterStateSnapshot();

        Task SendPlayerMovementAsync(PlayerMovementUpdate movement,
            CancellationToken cancellationToken = default);

        Task MoveItemAsync(ItemLocation source, ItemLocation destination,
            CancellationToken cancellationToken = default);

        Task SendChatTextAsync(string text, CancellationToken cancellationToken = default);

        Task DisconnectAsync(CancellationToken cancellationToken = default);
    }
}
