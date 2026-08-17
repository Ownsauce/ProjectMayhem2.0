using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AO.Server;
using AO.Server.Contracts;

namespace AO.Server.Transport
{
    public sealed class TcpJsonGameServerHost : IAsyncDisposable
    {
        private const int MaxHealthStatId = 1;
        private const int HealthStatId = 27;
        private const int CurrentNanoStatId = 214;
        private const int MaxNanoStatId = 221;
        private readonly AuthoritativeGameServer _server;
        private readonly object _serverLock = new();
        private readonly TcpListener _listener;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
        private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;
        private Task? _tickLoopTask;
        private int _lastBroadcastPlayerCount = -1;
        private int _lastBroadcastTick = -1;

        public TcpJsonGameServerHost(AuthoritativeGameServer server, IPAddress ipAddress, int port)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _listener = new TcpListener(ipAddress, port);
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_cts != null)
                throw new InvalidOperationException("Server host already started.");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener.Start();

            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            _tickLoopTask = Task.Run(() => TickLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (_cts == null)
                return;

            _cts.Cancel();
            _listener.Stop();

            if (_acceptLoopTask != null)
                await SafeAwaitAsync(_acceptLoopTask);
            if (_tickLoopTask != null)
                await SafeAwaitAsync(_tickLoopTask);

            foreach (var connection in _connections.Values)
                await connection.DisposeAsync();

            _connections.Clear();
            _cts.Dispose();
            _cts = null;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient tcpClient;
                try
                {
                    tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                Console.WriteLine($"[AO.Server] TCP client accepted from {tcpClient.Client.RemoteEndPoint}");
                _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            await using var connection = new ClientConnection(tcpClient);
            Guid? sessionId = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line = await connection.Reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(line))
                        break;

                    Console.WriteLine($"[AO.Server] <- {line}");
                    var message = JsonSerializer.Deserialize<ClientMessage>(line, _jsonOptions);
                    if (message == null)
                        continue;

                    if (sessionId == null)
                    {
                        if (!string.Equals(message.Type, "hello", StringComparison.OrdinalIgnoreCase))
                        {
                            var firstMessageError = new ServerMessage
                            {
                                Type = "error",
                                Message = "First message must be 'hello'.",
                                Tick = _server.Tick
                            };
                            await connection.SendAsync(firstMessageError, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(Guid.Empty, firstMessageError);
                            continue;
                        }

                        bool allowDebugCharacterEditing = IsLoopback(connection.RemoteEndPoint);
                        var player = _server.ConnectPlayer(
                            string.IsNullOrWhiteSpace(message.Name) ? "UnityPlayer" : message.Name,
                            message.BreedId <= 0 ? 1 : message.BreedId,
                            message.ProfessionId <= 0 ? 1 : message.ProfessionId,
                            message.Level <= 0 ? 1 : message.Level,
                            message.PlayfieldId <= 0 ? 800 : message.PlayfieldId,
                            allowDebugCharacterEditing);

                        sessionId = player.SessionId;
                        connection.SessionId = player.SessionId;
                        _connections[player.SessionId] = connection;
                        Console.WriteLine(
                            $"[AO.Server] Session {player.SessionId} connected as '{player.Profile.Name}' " +
                            $"breed={message.BreedId} profession={message.ProfessionId} level={message.Level} pf={player.CurrentPlayfieldId} localhostDebug={player.AllowDebugCharacterEditing}");

                        var welcome = new ServerMessage
                        {
                            Type = "welcome",
                            SessionId = player.SessionId.ToString(),
                            Tick = _server.Tick,
                            Message = "Connected to authoritative server.",
                            PlayfieldId = player.CurrentPlayfieldId,
                            TemplatePlayfieldId = player.CurrentInstanceKey.TemplatePlayfieldId,
                            RouteKey = player.CurrentInstanceKey.RouteKey,
                            ShardId = player.CurrentInstanceKey.ShardId,
                            Players = BuildSnapshot(player.SessionId),
                            RuntimeEntities = new List<RuntimeEntitySnapshot>()
                        };
                        await connection.SendAsync(welcome, _jsonOptions, cancellationToken);
                        LogOutgoingMessage(player.SessionId, welcome);
                        await SendZoneBootstrapAsync(connection, player.SessionId, player.CurrentPlayfieldId, "Initial zone bootstrap.", cancellationToken);

                        continue;
                    }

                    if (string.Equals(message.Type, "zone_request", StringComparison.OrdinalIgnoreCase))
                    {
                        ZoneTransitionDecision? decision = _server.TryResolveZoneTransition(
                            sessionId.Value,
                            message.ZoneLinkId,
                            message.PlayfieldId);

                        if (decision.HasValue)
                        {
                            var zone = new ServerMessage
                            {
                                Type = "zone_transition",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = $"Zone transition approved to PF {decision.Value.PlayfieldId}.",
                                ZoneLinkId = decision.Value.ZoneLinkId,
                                PlayfieldId = decision.Value.PlayfieldId,
                                SpawnAoX = decision.Value.SpawnAoX,
                                SpawnAoY = decision.Value.SpawnAoY,
                                SpawnAoZ = decision.Value.SpawnAoZ,
                                TargetYaw = decision.Value.TargetYaw,
                                TemplatePlayfieldId = decision.Value.TemplatePlayfieldId,
                                RouteKey = decision.Value.RouteKey,
                                ShardId = decision.Value.ShardId,
                                UsedReturnContext = decision.Value.UsedReturnContext,
                                Players = BuildSnapshot(sessionId.Value),
                                RuntimeEntities = new List<RuntimeEntitySnapshot>()
                            };
                            await connection.SendAsync(zone, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, zone);
                            await SendZoneBootstrapAsync(connection, sessionId.Value, decision.Value.PlayfieldId, $"Zone bootstrap for PF {decision.Value.PlayfieldId}.", cancellationToken);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = "Zone transition rejected by authoritative rules."
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "interact_runtime", StringComparison.OrdinalIgnoreCase))
                    {
                        RuntimeInteractionResult result = _server.ResolveRuntimeInteraction(sessionId.Value, message.EntityId);
                        if (result.ZoneDecision.HasValue)
                        {
                            var zone = new ServerMessage
                            {
                                Type = "zone_transition",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = FormatInteractionMessage(result),
                                EntityId = result.EntityId,
                                ZoneLinkId = result.ZoneDecision.Value.ZoneLinkId,
                                PlayfieldId = result.ZoneDecision.Value.PlayfieldId,
                                SpawnAoX = result.ZoneDecision.Value.SpawnAoX,
                                SpawnAoY = result.ZoneDecision.Value.SpawnAoY,
                                SpawnAoZ = result.ZoneDecision.Value.SpawnAoZ,
                                TargetYaw = result.ZoneDecision.Value.TargetYaw,
                                TemplatePlayfieldId = result.ZoneDecision.Value.TemplatePlayfieldId,
                                RouteKey = result.ZoneDecision.Value.RouteKey,
                                ShardId = result.ZoneDecision.Value.ShardId,
                                UsedReturnContext = result.ZoneDecision.Value.UsedReturnContext,
                                Players = BuildSnapshot(sessionId.Value),
                                RuntimeEntities = new List<RuntimeEntitySnapshot>()
                            };
                            await connection.SendAsync(zone, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, zone);
                            await SendZoneBootstrapAsync(connection, sessionId.Value, result.ZoneDecision.Value.PlayfieldId, $"Zone bootstrap for PF {result.ZoneDecision.Value.PlayfieldId}.", cancellationToken);
                        }
                        else if (result.Success)
                        {
                            int questNpcId = 0;
                            if (string.Equals(result.InteractionType, "talk", StringComparison.OrdinalIgnoreCase))
                                int.TryParse(result.InteractionId, out questNpcId);

                            var interaction = new ServerMessage
                            {
                                Type = string.Equals(result.InteractionType, "loot", StringComparison.OrdinalIgnoreCase)
                                    ? "loot_result"
                                    : "interaction_result",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                EntityId = result.EntityId,
                                InteractionType = result.InteractionType ?? string.Empty,
                                InteractionId = result.InteractionId ?? string.Empty,
                                QuestNpcId = questNpcId,
                                LootCredits = result.LootCredits,
                                LootItems = result.LootItems?.ToList() ?? new List<LootItemSnapshot>(),
                                Message = FormatInteractionMessage(result)
                            };
                            await connection.SendAsync(interaction, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, interaction);
                            await SendRuntimeEntityDeltaAsync(connection, sessionId.Value, (_server.TryGetPlayer(sessionId.Value, out var runtimePlayer) ? runtimePlayer.CurrentPlayfieldId : message.PlayfieldId), result.EntityId, cancellationToken);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                EntityId = result.EntityId,
                                Message = FormatInteractionMessage(result)
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, error);
                            await SendRuntimeEntityDeltaAsync(connection, sessionId.Value, (_server.TryGetPlayer(sessionId.Value, out var runtimePlayer) ? runtimePlayer.CurrentPlayfieldId : message.PlayfieldId), result.EntityId, cancellationToken);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "loot_take", StringComparison.OrdinalIgnoreCase))
                    {
                        RuntimeInteractionResult result = _server.TakeCorpseLoot(sessionId.Value, message.EntityId, (int)message.InstanceId, message.SlotId);
                        var loot = new ServerMessage
                        {
                            Type = "loot_result",
                            SessionId = sessionId.Value.ToString(),
                            Tick = _server.Tick,
                            EntityId = result.EntityId,
                            InteractionType = result.InteractionType ?? string.Empty,
                            InteractionId = result.InteractionId ?? string.Empty,
                            LootCredits = result.LootCredits,
                            LootTakenAoid = result.LootTakenAoid,
                            LootItems = result.LootItems?.ToList() ?? new List<LootItemSnapshot>(),
                            Message = FormatInteractionMessage(result)
                        };
                        await connection.SendAsync(loot, _jsonOptions, cancellationToken);
                        LogOutgoingMessage(sessionId.Value, loot);
                        await SendRuntimeEntityDeltaAsync(connection, sessionId.Value, (_server.TryGetPlayer(sessionId.Value, out var runtimePlayer) ? runtimePlayer.CurrentPlayfieldId : message.PlayfieldId), result.EntityId, cancellationToken);
                        continue;
                    }

                    if (string.Equals(message.Type, "quest_dialog_start", StringComparison.OrdinalIgnoreCase))
                    {
                        int npcId = message.QuestNpcId;
                        if (_server.TryOpenQuestDialog(sessionId.Value, npcId, out var dialog, out var dialogMessage))
                        {
                            await SendQuestDialogAsync(connection, sessionId.Value, dialog, dialogMessage, cancellationToken);
                        }
                        else
                        {
                            var interaction = new ServerMessage
                            {
                                Type = "interaction_result",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                QuestNpcId = npcId,
                                Message = string.IsNullOrWhiteSpace(dialogMessage)
                                    ? "No quest dialogue was available."
                                    : dialogMessage
                            };
                            await connection.SendAsync(interaction, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, interaction);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "quest_dialog_choice", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_server.TryBeginQuestTradeFromOption(sessionId.Value, message.QuestOptionId, out var trade, out var tradeMessage))
                        {
                            await SendQuestTradeAsync(connection, sessionId.Value, trade, tradeMessage, cancellationToken);
                            continue;
                        }

                        if (_server.TryApplyQuestDialogOption(sessionId.Value, message.QuestOptionId, out var dialog, out var dialogMessage))
                        {
                            await SendQuestDialogAsync(connection, sessionId.Value, dialog, dialogMessage, cancellationToken);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                QuestNpcId = message.QuestNpcId,
                                Message = string.IsNullOrWhiteSpace(dialogMessage)
                                    ? "Invalid quest dialog option."
                                    : dialogMessage
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, error);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "quest_trade_submit", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_server.TrySubmitQuestTrade(
                                sessionId.Value,
                                message.QuestTradeSessionId,
                                message.QuestTradeOffers,
                                out var dialog,
                                out var submitMessage))
                        {
                            await SendQuestDialogAsync(connection, sessionId.Value, dialog, submitMessage, cancellationToken);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = string.IsNullOrWhiteSpace(submitMessage)
                                    ? "Quest trade submit failed."
                                    : submitMessage
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, error);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "quest_trade_cancel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_server.TryCancelQuestTrade(sessionId.Value, message.QuestTradeSessionId, out var dialog, out var cancelMessage))
                        {
                            await SendQuestDialogAsync(connection, sessionId.Value, dialog, cancelMessage, cancellationToken);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = string.IsNullOrWhiteSpace(cancelMessage)
                                    ? "Quest trade cancel failed."
                                    : cancelMessage
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, error);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "quest_dialog_close", StringComparison.OrdinalIgnoreCase))
                    {
                        var closed = new ServerMessage
                        {
                            Type = "quest_dialog",
                            SessionId = sessionId.Value.ToString(),
                            Tick = _server.Tick,
                            QuestNpcId = message.QuestNpcId,
                            DialogTitle = "Dialogue",
                            DialogText = "Conversation ended.",
                            DialogCanClose = true,
                            QuestDialogOptions = new List<QuestDialogOptionSnapshot>(),
                            QuestJournal = new List<QuestJournalEntrySnapshot>(),
                            Message = "Quest dialogue closed."
                        };
                        await connection.SendAsync(closed, _jsonOptions, cancellationToken);
                        LogOutgoingMessage(sessionId.Value, closed);
                        continue;
                    }

                    if (string.Equals(message.Type, "admin_teleport", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_server.TryCreateAdminTeleportDecision(
                                sessionId.Value,
                                message.PlayfieldId,
                                message.SpawnAoX,
                                message.SpawnAoY,
                                message.SpawnAoZ,
                                message.UseTeleportDefaultSpawn,
                                message.TargetYaw,
                                out var decision,
                                out var decisionMessage))
                        {
                            var zone = new ServerMessage
                            {
                                Type = "zone_transition",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = decisionMessage,
                                ZoneLinkId = decision.ZoneLinkId,
                                PlayfieldId = decision.PlayfieldId,
                                SpawnAoX = decision.SpawnAoX,
                                SpawnAoY = decision.SpawnAoY,
                                SpawnAoZ = decision.SpawnAoZ,
                                TargetYaw = decision.TargetYaw,
                                TemplatePlayfieldId = decision.TemplatePlayfieldId,
                                RouteKey = decision.RouteKey,
                                ShardId = decision.ShardId,
                                UsedReturnContext = decision.UsedReturnContext,
                                IsAdminAction = true,
                                Players = BuildSnapshot(sessionId.Value),
                                RuntimeEntities = new List<RuntimeEntitySnapshot>()
                            };

                            await connection.SendAsync(zone, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, zone);
                            await SendZoneBootstrapAsync(connection, sessionId.Value, decision.PlayfieldId, $"Zone bootstrap for PF {decision.PlayfieldId}.", cancellationToken);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                IsAdminAction = true,
                                Message = string.IsNullOrWhiteSpace(decisionMessage)
                                    ? "Admin teleport rejected."
                                    : decisionMessage
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, error);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "admin_grant_item", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_server.TryGrantAdminItem(sessionId.Value, message.InstanceId, out var grantedInstanceId, out var grantMessage))
                        {
                            var granted = new ServerMessage
                            {
                                Type = "admin_item_granted",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                InstanceId = grantedInstanceId,
                                IsAdminAction = true,
                                Message = grantMessage
                            };
                            await connection.SendAsync(granted, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, granted);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                IsAdminAction = true,
                                Message = string.IsNullOrWhiteSpace(grantMessage)
                                    ? "Admin item grant rejected."
                                    : grantMessage
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, error);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "equip", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = _server.HandleAction(
                            sessionId.Value,
                            new EquipItemAction(message.SlotId, message.InstanceId));

                        if (result.Success)
                        {
                            long appliedInstanceId = message.InstanceId;
                            if (_server.TryGetPlayer(sessionId.Value, out var equippedPlayer))
                            {
                                var equipped = equippedPlayer.Profile?.Equipment?.GetAllEquipped();
                                if (equipped != null && equipped.TryGetValue(message.SlotId, out var slotInstanceId) && slotInstanceId > 0)
                                    appliedInstanceId = slotInstanceId;
                            }

                            var approved = new ServerMessage
                            {
                                Type = "equip_applied",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message,
                                SlotId = message.SlotId,
                                InstanceId = appliedInstanceId,
                                Players = BuildSnapshot(sessionId.Value)
                            };
                            await connection.SendAsync(approved, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, approved);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "unequip", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = _server.HandleAction(
                            sessionId.Value,
                            new UnequipSlotAction(message.SlotId));

                        if (result.Success)
                        {
                            var approved = new ServerMessage
                            {
                                Type = "unequip_applied",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message,
                                SlotId = message.SlotId,
                                InstanceId = message.InstanceId,
                                Players = BuildSnapshot(sessionId.Value)
                            };
                            await connection.SendAsync(approved, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, approved);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "increase_stat", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = _server.HandleAction(
                            sessionId.Value,
                            new IncreaseStatAction(message.StatName, message.Amount));

                        if (result.Success && _server.TryGetPlayer(sessionId.Value, out var updatedPlayer))
                        {
                            var approved = new ServerMessage
                            {
                                Type = "stat_increase_applied",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message,
                                StatName = message.StatName,
                                Amount = message.Amount,
                                AvailableIp = updatedPlayer.Profile.AvailableIp,
                                Level = updatedPlayer.Profile.Level.Level,
                                BreedId = updatedPlayer.Profile.BreedId,
                                ProfessionId = updatedPlayer.Profile.ProfessionId,
                                Sex = updatedPlayer.CharacterSexCode,
                                Experience = updatedPlayer.Profile.Level.Experience,
                                PlayfieldId = updatedPlayer.CurrentPlayfieldId,
                                Players = BuildSnapshot(sessionId.Value)
                            };
                            await connection.SendAsync(approved, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, approved);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                        }

                        continue;
                    }

                    if (string.Equals(message.Type, "set_character", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = _server.HandleAction(
                            sessionId.Value,
                            new SetCharacterSettingsAction(message.Level, message.BreedId, message.ProfessionId, message.Sex, message.Experience));

                        if (result.Success && _server.TryGetPlayer(sessionId.Value, out var updatedPlayer))
                        {
                            var approved = new ServerMessage
                            {
                                Type = "character_settings_applied",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message,
                                Level = updatedPlayer.Profile.Level.Level,
                                BreedId = updatedPlayer.Profile.BreedId,
                                ProfessionId = updatedPlayer.Profile.ProfessionId,
                                Sex = updatedPlayer.CharacterSexCode,
                                Experience = message.Experience,
                                PlayfieldId = updatedPlayer.CurrentPlayfieldId,
                                Players = BuildSnapshot(sessionId.Value)
                            };
                            await connection.SendAsync(approved, _jsonOptions, cancellationToken);
                            LogOutgoingMessage(sessionId.Value, approved);
                        }
                        else
                        {
                            var error = new ServerMessage
                            {
                                Type = "error",
                                SessionId = sessionId.Value.ToString(),
                                Tick = _server.Tick,
                                Message = result.Message
                            };
                            await connection.SendAsync(error, _jsonOptions, cancellationToken);
                        }

                        continue;
                    }

                    var actionResult = HandleClientMessage(sessionId.Value, message);
                    Console.WriteLine(
                        $"[AO.Server] Action session={sessionId.Value} type={message.Type} " +
                        $"success={actionResult.Success} message=\"{actionResult.Message}\"");
                    if (!actionResult.Success)
                    {
                        var error = new ServerMessage
                        {
                            Type = "error",
                            SessionId = sessionId.Value.ToString(),
                            Tick = _server.Tick,
                            Message = actionResult.Message
                        };
                        await connection.SendAsync(error, _jsonOptions, cancellationToken);
                    }
                    else if (string.Equals(message.Type, "zone_loaded", StringComparison.OrdinalIgnoreCase))
                    {
                        int bootstrapPlayfieldId = message.PlayfieldId;
                        if (bootstrapPlayfieldId <= 0 && _server.TryGetPlayer(sessionId.Value, out var runtimePlayer))
                            bootstrapPlayfieldId = runtimePlayer.CurrentPlayfieldId;

                        if (bootstrapPlayfieldId > 0)
                        {
                            await SendZoneBootstrapAsync(
                                connection,
                                sessionId.Value,
                                bootstrapPlayfieldId,
                                $"Zone bootstrap refresh for PF {bootstrapPlayfieldId}.",
                                cancellationToken);
                        }
                    }
                    else if (string.Equals(message.Type, "attack_runtime", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(message.EntityId))
                    {
                        int playfieldId = _server.TryGetPlayer(sessionId.Value, out var runtimePlayer)
                            ? runtimePlayer.CurrentPlayfieldId
                            : message.PlayfieldId;
                        await SendRuntimeEntityDeltaAsync(connection, sessionId.Value, playfieldId, message.EntityId, cancellationToken);
                    }

                    await SendDueRuntimeRespawnDeltasAsync(connection, sessionId.Value, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (sessionId.HasValue)
                {
                    _connections.TryRemove(sessionId.Value, out _);
                    _server.DisconnectPlayer(sessionId.Value);
                    Console.WriteLine($"[AO.Server] Session {sessionId.Value} disconnected.");
                    await BroadcastWorldStateAsync(CancellationToken.None);
                }
            }
        }
        private async Task SendRuntimeEntityDeltaAsync(
            ClientConnection connection,
            Guid sessionId,
            int playfieldId,
            string entityId,
            CancellationToken cancellationToken)
        {
            if (connection == null || playfieldId <= 0 || string.IsNullOrWhiteSpace(entityId))
                return;

            List<RuntimeEntityDelta> deltas = BuildRuntimeEntityDeltas(playfieldId, entityId);
            if (deltas.Count == 0)
                return;

            var deltaMessage = new ServerMessage
            {
                Type = "runtime_entity_delta",
                SessionId = sessionId.ToString(),
                Tick = _server.Tick,
                PlayfieldId = playfieldId,
                EntityId = entityId,
                Message = $"Runtime delta for {entityId}.",
                RuntimeEntityDeltas = deltas
            };

            await connection.SendAsync(deltaMessage, _jsonOptions, cancellationToken);
            LogOutgoingMessage(sessionId, deltaMessage);
        }

        private async Task SendDueRuntimeRespawnDeltasAsync(
            ClientConnection connection,
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            if (connection == null)
                return;
            if (!_server.TryGetPlayer(sessionId, out var player) || player == null || player.CurrentPlayfieldId <= 0)
                return;

            List<RuntimeEntityDelta> deltas;
            int tick;
            lock (_serverLock)
            {
                var snapshots = _server.BuildRuntimeSnapshot(player.CurrentPlayfieldId);
                var respawnedIds = _server.GetRecentRespawnedRuntimeEntityIds(player.CurrentPlayfieldId);
                if (respawnedIds.Count == 0)
                    return;

                deltas = new List<RuntimeEntityDelta>();
                for (int i = 0; i < respawnedIds.Count; i++)
                {
                    string respawnedId = respawnedIds[i];
                    RuntimeEntitySnapshot respawnedEntity = snapshots
                        ?.FirstOrDefault(e => e != null
                            && string.Equals(e.EntityId, respawnedId, StringComparison.OrdinalIgnoreCase));
                    if (respawnedEntity == null)
                        continue;

                    deltas.Add(new RuntimeEntityDelta
                    {
                        Operation = "upsert",
                        Entity = respawnedEntity
                    });
                }

                tick = _server.Tick;
            }

            if (deltas.Count == 0)
                return;

            var deltaMessage = new ServerMessage
            {
                Type = "runtime_entity_delta",
                SessionId = sessionId.ToString(),
                Tick = tick,
                PlayfieldId = player.CurrentPlayfieldId,
                Message = $"Runtime respawn delta count={deltas.Count}.",
                RuntimeEntityDeltas = deltas
            };

            await connection.SendAsync(deltaMessage, _jsonOptions, cancellationToken);
            LogOutgoingMessage(sessionId, deltaMessage);
        }

        private List<RuntimeEntityDelta> BuildRuntimeEntityDeltas(int playfieldId, string entityId)
        {
            List<RuntimeEntitySnapshot> snapshots;
            lock (_serverLock)
            {
                snapshots = _server.BuildRuntimeSnapshot(playfieldId);
            }

            if (string.IsNullOrWhiteSpace(entityId))
                return new List<RuntimeEntityDelta>();

            var result = snapshots
                ?.Where(s => s != null && string.Equals(s.EntityId, entityId, StringComparison.OrdinalIgnoreCase))
                .Select(s => new RuntimeEntityDelta
                {
                    Operation = "upsert",
                    Entity = s
                })
                .ToList()
                ?? new List<RuntimeEntityDelta>();

            if (result.Count > 0)
                return result;

            string corpsePrefix = entityId + ":corpse:";
            var corpseResults = snapshots
                ?.Where(s => s != null
                    && !string.IsNullOrWhiteSpace(s.EntityId)
                    && s.EntityId.StartsWith(corpsePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
                .Select(s => new RuntimeEntityDelta
                {
                    Operation = "upsert",
                    Entity = s
                })
                .ToList()
                ?? new List<RuntimeEntityDelta>();
            if (corpseResults.Count > 0)
            {
                corpseResults.Add(new RuntimeEntityDelta
                {
                    Operation = "remove",
                    Entity = new RuntimeEntitySnapshot
                    {
                        EntityId = entityId,
                        PlayfieldId = playfieldId
                    }
                });
                return corpseResults;
            }

            return new List<RuntimeEntityDelta>
            {
                new RuntimeEntityDelta
                {
                    Operation = "remove",
                    Entity = new RuntimeEntitySnapshot
                    {
                        EntityId = entityId,
                        PlayfieldId = playfieldId
                    }
                }
            };
        }
        private async Task SendZoneBootstrapAsync(
            ClientConnection connection,
            Guid sessionId,
            int playfieldId,
            string message,
            CancellationToken cancellationToken)
        {
            var bootstrap = BuildZoneBootstrapMessage(sessionId, playfieldId, message);
            await connection.SendAsync(bootstrap, _jsonOptions, cancellationToken);
            LogOutgoingMessage(sessionId, bootstrap);
        }

        private async Task SendQuestDialogAsync(
            ClientConnection connection,
            Guid sessionId,
            QuestDialogState dialog,
            string message,
            CancellationToken cancellationToken)
        {
            var payload = new ServerMessage
            {
                Type = "quest_dialog",
                SessionId = sessionId.ToString(),
                Tick = _server.Tick,
                QuestNpcId = dialog?.NpcId ?? 0,
                DialogTitle = dialog?.Title ?? "Dialogue",
                DialogText = dialog?.Body ?? string.Empty,
                DialogCanClose = dialog?.CanClose ?? true,
                QuestDialogOptions = dialog?.Options ?? new List<QuestDialogOptionSnapshot>(),
                QuestJournal = dialog?.Journal ?? new List<QuestJournalEntrySnapshot>(),
                QuestRewardGrants = dialog?.RewardGrants ?? new List<QuestRewardGrantSnapshot>(),
                Message = string.IsNullOrWhiteSpace(message) ? "Quest dialogue updated." : message
            };

            await connection.SendAsync(payload, _jsonOptions, cancellationToken);
            LogOutgoingMessage(sessionId, payload);
        }

        private async Task SendQuestTradeAsync(
            ClientConnection connection,
            Guid sessionId,
            QuestTradeState trade,
            string notice,
            CancellationToken cancellationToken)
        {
            var payload = new ServerMessage
            {
                Type = "quest_trade",
                SessionId = sessionId.ToString(),
                Tick = _server.Tick,
                QuestTradeSessionId = trade?.SessionId ?? string.Empty,
                QuestTradeTitle = trade?.Title ?? "Quest Trade",
                QuestTradePrompt = trade?.Prompt ?? string.Empty,
                QuestTradeRequirements = trade?.Requirements ?? new List<QuestTradeRequirementSnapshot>(),
                QuestTradeAutofill = trade?.Autofill ?? new List<QuestTradeOfferEntry>(),
                Message = string.IsNullOrWhiteSpace(notice) ? "Quest trade opened." : notice
            };

            await connection.SendAsync(payload, _jsonOptions, cancellationToken);
            LogOutgoingMessage(sessionId, payload);
        }

        private ServerMessage BuildZoneBootstrapMessage(Guid sessionId, int playfieldId, string message)
        {
            int templatePlayfieldId = playfieldId;
            string routeKey = "default";
            int shardId = 1;
            int tick;
            List<PlayerSnapshot> players;
            List<RuntimeEntitySnapshot> runtimeEntities;

            lock (_serverLock)
            {
                if (_server.TryGetPlayer(sessionId, out var player))
                {
                    templatePlayfieldId = player.CurrentInstanceKey.TemplatePlayfieldId;
                    routeKey = player.CurrentInstanceKey.RouteKey;
                    shardId = player.CurrentInstanceKey.ShardId;
                }

                tick = _server.Tick;
                players = BuildSnapshotUnlocked(sessionId);
                runtimeEntities = _server.BuildRuntimeSnapshot(playfieldId);
            }

            return new ServerMessage
            {
                Type = "zone_bootstrap",
                SessionId = sessionId.ToString(),
                Tick = tick,
                BootstrapVersion = tick,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"Zone bootstrap ready for PF {playfieldId}."
                    : message,
                PlayfieldId = playfieldId,
                TemplatePlayfieldId = templatePlayfieldId,
                RouteKey = routeKey,
                ShardId = shardId,
                Players = players,
                RuntimeEntities = runtimeEntities
            };
        }

        private ServerActionResult HandleClientMessage(Guid sessionId, ClientMessage message)
        {
            lock (_serverLock)
            {
                return message.Type.ToLowerInvariant() switch
                {
                    "move" => _server.HandleAction(sessionId, new MoveAction(message.X, message.Z)),
                    "stop" => _server.HandleAction(sessionId, new StopMoveAction()),
                    "equip" => _server.HandleAction(sessionId, new EquipItemAction(message.SlotId, message.InstanceId)),
                    "unequip" => _server.HandleAction(sessionId, new UnequipSlotAction(message.SlotId)),
                    "increase_stat" => _server.HandleAction(sessionId, new IncreaseStatAction(message.StatName, message.Amount)),
                    "gain_xp" => _server.HandleAction(sessionId, new GainExperienceAction(message.Amount)),
                    "zone_loaded" => _server.HandleAction(sessionId, new ZoneLoadedAction(message.PlayfieldId, message.X, message.Y, message.Z)),
                    "zone_bootstrap_loaded" => ServerActionResult.Ok($"Zone bootstrap acknowledged: pf={message.PlayfieldId} v={message.BootstrapVersion}."),
                    "interact_runtime" => _server.HandleAction(sessionId, new InteractWithRuntimeEntityAction(message.EntityId)),
                    "attack_runtime" => _server.HandleAction(sessionId, new AttackRuntimeEntityAction(message.EntityId, message.X, message.Y, message.Z, message.Amount)),
                    _ => ServerActionResult.Fail($"Unknown client message type '{message.Type}'.")
                };
            }
        }

        private async Task TickLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(AO.Core.Simulation.GameLoop.TickDelta));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    lock (_serverLock)
                    {
                        _server.TickOnce();
                    }

                    await BroadcastWorldStateAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AO.Server] Tick loop stopped unexpectedly: {ex}");
            }
        }

        private async Task BroadcastWorldStateAsync(CancellationToken cancellationToken)
        {
            var deadSessions = new List<Guid>();
            List<string> respawnedEntityIds;
            lock (_serverLock)
            {
                respawnedEntityIds = _server.ConsumeRespawnedRuntimeEntityIds();
            }

            foreach (var pair in _connections)
            {
                try
                {
                    ServerMessage snapshot;
                    lock (_serverLock)
                    {
                        snapshot = new ServerMessage
                        {
                            Type = "world_state",
                            Tick = _server.Tick,
                            Players = BuildSnapshotUnlocked(pair.Key)
                        };
                        if (_server.TryGetPlayer(pair.Key, out var viewer))
                        {
                            snapshot.PlayfieldId = viewer.CurrentPlayfieldId;
                            snapshot.RuntimeEntities = _server.BuildRuntimeSnapshot(viewer.CurrentPlayfieldId);
                            var viewerRespawnedEntityIds = _server.GetRecentRespawnedRuntimeEntityIds(viewer.CurrentPlayfieldId);
                            if (respawnedEntityIds.Count > 0)
                            {
                                for (int i = 0; i < respawnedEntityIds.Count; i++)
                                {
                                    if (!viewerRespawnedEntityIds.Contains(respawnedEntityIds[i], StringComparer.OrdinalIgnoreCase))
                                        viewerRespawnedEntityIds.Add(respawnedEntityIds[i]);
                                }
                            }

                            if (viewerRespawnedEntityIds.Count > 0)
                            {
                                for (int i = 0; i < viewerRespawnedEntityIds.Count; i++)
                                {
                                    string respawnedEntityId = viewerRespawnedEntityIds[i];
                                    RuntimeEntitySnapshot respawnedEntity = snapshot.RuntimeEntities
                                        ?.FirstOrDefault(e => e != null
                                            && string.Equals(e.EntityId, respawnedEntityId, StringComparison.OrdinalIgnoreCase));
                                    if (respawnedEntity == null)
                                    {
                                        Console.WriteLine(
                                            $"[AO.Server] Runtime respawn upsert skipped; entityId={respawnedEntityId} playfield={viewer.CurrentPlayfieldId} was not in the current snapshot.");
                                        continue;
                                    }

                                    snapshot.RuntimeEntityDeltas.Add(new RuntimeEntityDelta
                                    {
                                        Operation = "upsert",
                                        Entity = respawnedEntity
                                    });
                                    Console.WriteLine(
                                        $"[AO.Server] Runtime respawn upsert queued entityId={respawnedEntityId} playfield={viewer.CurrentPlayfieldId} tick={snapshot.Tick}.");
                                }
                            }
                        }
                    }

                    if (snapshot.Players.Count != _lastBroadcastPlayerCount)
                    {
                        _lastBroadcastTick = snapshot.Tick;
                        _lastBroadcastPlayerCount = snapshot.Players.Count;
                        Console.WriteLine(
                            $"[AO.Server] Broadcasting world_state tick={snapshot.Tick} players={snapshot.Players.Count}");
                    }

                    await SendWithTimeoutAsync(pair.Value, snapshot, cancellationToken);
                }
                catch
                {
                    deadSessions.Add(pair.Key);
                }
            }

            foreach (var sessionId in deadSessions)
            {
                if (_connections.TryRemove(sessionId, out var connection))
                    await connection.DisposeAsync();
                lock (_serverLock)
                {
                    _server.DisconnectPlayer(sessionId);
                }
            }
        }

        private async Task SendWithTimeoutAsync(
            ClientConnection connection,
            ServerMessage message,
            CancellationToken cancellationToken)
        {
            if (connection == null || message == null)
                return;

            try
            {
                await connection.SendAsync(message, _jsonOptions, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                Console.WriteLine(
                    $"[AO.Server] Timed out sending {message.Type} to session={connection.SessionId}; dropping connection.");
                throw;
            }
        }

        private List<PlayerSnapshot> BuildSnapshot(Guid viewerSessionId)
        {
            lock (_serverLock)
            {
                return BuildSnapshotUnlocked(viewerSessionId);
            }
        }

        private List<PlayerSnapshot> BuildSnapshotUnlocked(Guid viewerSessionId)
        {
            var players = new List<PlayerSnapshot>();
            foreach (var player in _server.Players)
            {
                if (!_server.IsSameVisibleInstance(viewerSessionId, player))
                    continue;

                players.Add(new PlayerSnapshot
                {
                    SessionId = player.SessionId.ToString(),
                    Name = player.Profile.Name,
                    PlayfieldId = player.CurrentPlayfieldId,
                    TemplatePlayfieldId = player.CurrentInstanceKey.TemplatePlayfieldId,
                    RouteKey = player.CurrentInstanceKey.RouteKey,
                    ShardId = player.CurrentInstanceKey.ShardId,
                    X = player.WorldCharacter.Position.X,
                    Y = player.WorldCharacter.Position.Y,
                    Z = player.WorldCharacter.Position.Z,
                    Health = Math.Clamp(player.CurrentHealth, 0, player.Profile.StatsContainer.GetFinalStat(MaxHealthStatId)),
                    MaxHealth = player.Profile.StatsContainer.GetFinalStat(MaxHealthStatId),
                    Nano = player.Profile.StatsContainer.GetFinalStat(CurrentNanoStatId),
                    MaxNano = player.Profile.StatsContainer.GetFinalStat(MaxNanoStatId),
                    Experience = player.Profile.Level.Experience
                });
            }

            return players;
        }

        private static bool IsLoopback(EndPoint? remoteEndPoint)
        {
            if (remoteEndPoint is IPEndPoint ipEndPoint)
            {
                if (IPAddress.IsLoopback(ipEndPoint.Address))
                    return true;

                if (ipEndPoint.Address.IsIPv4MappedToIPv6)
                {
                    IPAddress mapped = ipEndPoint.Address.MapToIPv4();
                    if (IPAddress.IsLoopback(mapped))
                        return true;
                }
            }
            return false;
        }


        private static string FormatInteractionMessage(RuntimeInteractionResult result)
        {
            string baseMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "Interaction processed."
                : result.Message;
            return
                $"{baseMessage} " +
                $"type={result.InteractionType} " +
                $"enabled={result.IsEnabled} " +
                $"uses={result.UseCount} " +
                $"cooldown={result.CooldownRemainingSeconds:0.0}s";
        }
        private static void LogOutgoingMessage(Guid sessionId, ServerMessage message)
        {
            if (string.Equals(message.Type, "world_state", StringComparison.OrdinalIgnoreCase))
                return;

            Console.WriteLine(
                $"[AO.Server] -> session={sessionId} type={message.Type} tick={message.Tick} players={message.Players.Count} pf={message.PlayfieldId} message=\"{message.Message}\"");
        }

        private static async Task SafeAwaitAsync(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }

        private sealed class ClientConnection : IAsyncDisposable
        {
            private readonly TcpClient _tcpClient;
            public Guid SessionId { get; set; }
            public EndPoint? RemoteEndPoint => _tcpClient.Client.RemoteEndPoint;
            public StreamReader Reader { get; }
            public StreamWriter Writer { get; }

            public ClientConnection(TcpClient tcpClient)
            {
                _tcpClient = tcpClient;
                var stream = tcpClient.GetStream();
                Reader = new StreamReader(stream);
                Writer = new StreamWriter(stream) { AutoFlush = true };
            }

            public Task SendAsync(ServerMessage message, JsonSerializerOptions options, CancellationToken cancellationToken)
            {
                string payload = JsonSerializer.Serialize(message, options);
                return Writer.WriteLineAsync(payload);
            }

            public ValueTask DisposeAsync()
            {
                Writer.Dispose();
                Reader.Dispose();
                _tcpClient.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
















