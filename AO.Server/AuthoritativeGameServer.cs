using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AO.Core.Math;
using AO.Core.Simulation;
using AO.Core.Stats;
using CoreItems = AO.Core.Items;
using AO.Core.World;
using AO.Server.Contracts;
using AO.Server.Transport;

namespace AO.Server
{
    public sealed class AuthoritativeGameServer
    {
        private const int DefaultMaxPlayersPerInstance = 20;
        private const int CreditsStatId = 61;
        private const double DefaultPlayerAttackWindupSeconds = 1.5;
        private const double DefaultPlayerAttackRechargeSeconds = 2.0;
        private readonly AuthoritativeGameData _gameData;
        private readonly GameLoop _loop = new();
        private readonly WorldState _world = new();
        private readonly Dictionary<Guid, ConnectedPlayer> _players = new();
        private readonly Dictionary<string, ZoneLinkDefinition> _zoneLinksById = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<PlayfieldInstanceKey, HashSet<Guid>> _instanceMembers = new();
        private readonly Dictionary<string, MobCombatState> _mobCombatByEntityId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CorpseLootState> _corpseLootByEntityId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RuntimeRespawnState> _runtimeRespawnsByEntityId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _recentRuntimeRespawnUpsertsByEntityId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _respawnedRuntimeEntityIds = new();
        private DateTime _nextRuntimeRespawnDebugUtc = DateTime.MinValue;
        private readonly PlayfieldRuntimeService _playfieldRuntime;
        private readonly QuestDialogRuntimeService _questDialogs;
        private readonly LootService _lootService;
        private readonly TimeSpan _playfieldWarmTtl = TimeSpan.FromMinutes(2);
        private int _nextEntityId = 1;

        public AuthoritativeGameServer(AuthoritativeGameData gameData)
        {
            _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            _playfieldRuntime = new PlayfieldRuntimeService(_gameData.Paths);
            _questDialogs = new QuestDialogRuntimeService(_gameData.Paths, ResolveAuthoritativeItemDefinitionByAoid);
            _lootService = new LootService(_gameData.Paths);
            LoadZoneLinks();
        }

        public int Tick => _loop.Tick;

        public IReadOnlyCollection<ConnectedPlayer> Players => _players.Values;

        public ConnectedPlayer ConnectPlayer(
            string name,
            int breedId = 1,
            int professionId = 1,
            int level = 1,
            int playfieldId = 800,
            bool allowDebugCharacterEditing = false)
        {
            var profession = _gameData.ResolveProfession(professionId)
                ?? new Profession { Name = $"Profession_{professionId}" };

            var profile = new AO.Core.Characters.Character(name, profession, 0, breedId, professionId);
            profile.SetIdentity(breedId, professionId, profession);
            profile.SetLevelAndRebuildIp(level, preserveSpent: false);
            ApplyDefaultNonAbilityValues(profile);
            ApplyStartingAbilityValues(profile, breedId);
            profile.StatsContainer.Recalculate();
            profile.RecalculateDerivedStats();
            SeedStarterInventory(profile);

            // Spawn near world origin at a safe baseline height. Client-side playfield
            // conversion will position the avatar based on playfield/teleport defaults.
            var worldCharacter = new AO.Core.Entities.Character
            {
                Id = _nextEntityId++,
                Position = new Vector3(0f, 1f, 0f)
            };

            _world.Entities.Add(worldCharacter);

            var player = new ConnectedPlayer(Guid.NewGuid(), profile, worldCharacter, playfieldId, allowDebugCharacterEditing)
            {
                CurrentInstanceKey = BuildDefaultInstanceKey(playfieldId),
                CurrentHealth = Math.Max(1, profile.StatsContainer.GetFinalStat(27))
            };
            _players[player.SessionId] = player;
            _questDialogs.EnsurePlayer(player.SessionId);
            AddPlayerToInstance(player.SessionId, player.CurrentInstanceKey);
            _playfieldRuntime.MarkPlayfieldActive(playfieldId);
            Console.WriteLine(
                $"[AO.Server] Player inventory initialized session={player.SessionId} mainUsed={profile.Inventory.Main.Slots.Count(slot => slot != null)}/{profile.Inventory.Main.Capacity}");
            return player;
        }

        public bool DisconnectPlayer(Guid sessionId)
        {
            if (_players.TryGetValue(sessionId, out var player))
            {
                RemovePlayerFromInstance(sessionId, player.CurrentInstanceKey);
                _playfieldRuntime.MarkPlayfieldInactive(player.CurrentPlayfieldId);
            }
            _questDialogs.RemovePlayer(sessionId);
            return _players.Remove(sessionId);
        }

        private void SeedStarterInventory(AO.Core.Characters.Character profile)
        {
            if (profile?.Inventory == null)
                return;

            if (profile.Inventory.Main.Slots.Any(slot => slot != null))
                return;

            var backpack = _gameData.Items.FirstOrDefault(i => IsBackpackItem(i?.Name));
            if (backpack == null)
                return;

            var dataInstance = ResolveAuthoritativeItemInstance(backpack.AOID);
            var coreInstance = CreateCoreItemInstance(dataInstance, backpack.AOID);
            if (coreInstance == null)
                return;

            if (profile.Inventory.TrySetMainSlot(0, coreInstance))
            {
                Console.WriteLine(
                    $"[AO.Server] Starter inventory seeded item='{coreInstance.Definition?.Name ?? "Unknown"}' aoid={coreInstance.Definition?.AOID ?? 0} containerCapacity={coreInstance.ContainerCapacity}");
            }
        }

        public bool TryOpenQuestDialog(Guid sessionId, int npcId, out QuestDialogState dialog, out string message)
        {
            dialog = QuestDialogState.Empty(npcId);
            message = "Quest dialogue unavailable.";
            if (!_players.TryGetValue(sessionId, out var player))
                return false;

            int level = Math.Max(1, player.Profile?.Level?.Level ?? 1);
            return _questDialogs.TryOpenDialog(sessionId, player.Profile, npcId, level, out dialog, out message);
        }

        public bool TryApplyQuestDialogOption(Guid sessionId, string optionId, out QuestDialogState dialog, out string message)
        {
            dialog = QuestDialogState.Empty(0);
            message = "Quest dialogue unavailable.";
            if (!_players.TryGetValue(sessionId, out var player))
                return false;

            int level = Math.Max(1, player.Profile?.Level?.Level ?? 1);
            return _questDialogs.TryApplyOption(sessionId, player.Profile, optionId, level, out dialog, out message);
        }

        public bool TryBeginQuestTradeFromOption(Guid sessionId, string optionId, out QuestTradeState trade, out string message)
        {
            trade = QuestTradeState.Empty;
            message = "Quest trade unavailable.";
            if (!_players.TryGetValue(sessionId, out var player))
                return false;

            return _questDialogs.TryBeginTradeForOption(sessionId, player.Profile, optionId, out trade, out message);
        }

        public bool TrySubmitQuestTrade(
            Guid sessionId,
            string tradeSessionId,
            IReadOnlyList<QuestTradeOfferEntry> offers,
            out QuestDialogState dialog,
            out string message)
        {
            dialog = QuestDialogState.Empty(0);
            message = "Quest trade unavailable.";
            if (!_players.TryGetValue(sessionId, out var player))
                return false;

            int level = Math.Max(1, player.Profile?.Level?.Level ?? 1);
            return _questDialogs.TrySubmitTrade(sessionId, player.Profile, tradeSessionId, offers, level, out dialog, out message);
        }

        public bool TryCancelQuestTrade(Guid sessionId, string tradeSessionId, out QuestDialogState dialog, out string message)
        {
            dialog = QuestDialogState.Empty(0);
            message = "Quest trade unavailable.";
            if (!_players.TryGetValue(sessionId, out var player))
                return false;

            int level = Math.Max(1, player.Profile?.Level?.Level ?? 1);
            return _questDialogs.TryCancelTrade(sessionId, player.Profile, tradeSessionId, level, out dialog, out message);
        }

        public bool TryCreateAdminTeleportDecision(
            Guid sessionId,
            int targetPlayfieldId,
            float spawnAoX,
            float spawnAoY,
            float spawnAoZ,
            bool useDefaultSpawn,
            float targetYaw,
            out ZoneTransitionDecision decision,
            out string message)
        {
            decision = default;
            message = "Teleport rejected.";
            if (!_players.TryGetValue(sessionId, out var player))
            {
                message = "Unknown session.";
                return false;
            }

            if (!player.AllowDebugCharacterEditing)
            {
                message = "Teleport is restricted to localhost/admin sessions.";
                return false;
            }

            if (targetPlayfieldId <= 0)
            {
                message = "Invalid target playfield.";
                return false;
            }

            bool hasExplicitAo = !useDefaultSpawn && IsFinite(spawnAoX) && IsFinite(spawnAoY) && IsFinite(spawnAoZ);

            string spawnSource = "explicit_coords";
            if (!hasExplicitAo)
            {
                // Server-authoritative teleport policy when explicit coords are not provided:
                // 1) teleport_defaults.json
                // 2) highest collision/runtime point for the target PF
                // 3) origin fallback only if both are unavailable
                spawnSource = "teleport_defaults";
                if (!TryResolveTeleportDefaultForPlayfield(targetPlayfieldId, out var defaultAo))
                {
                    // Fallback to a best-known high point in that playfield's runtime data.
                    if (_playfieldRuntime.TryResolveHighestPoint(targetPlayfieldId, out float hx, out float hy, out float hz))
                    {
                        defaultAo = new Vector3(hx, hy, hz);
                        spawnSource = "playfield_highest_point";
                    }
                    else
                    {
                        defaultAo = new Vector3(0f, 0f, 0f);
                        spawnSource = "origin_zero_fallback";
                    }
                }

                spawnAoX = defaultAo.X;
                spawnAoY = defaultAo.Y;
                spawnAoZ = defaultAo.Z;
            }

            Console.WriteLine(
                $"[AO.Server] Admin teleport spawn resolved: pf={targetPlayfieldId} source={spawnSource} " +
                $"ao=({spawnAoX:0.###}, {spawnAoY:0.###}, {spawnAoZ:0.###})");

            string routeKey = "default";
            PlayfieldInstanceKey destinationInstance = AllocateDestinationInstance(
                player,
                targetPlayfieldId,
                routeKey,
                maxPlayersOverride: null);

            var syntheticLink = new ZoneLinkDefinition
            {
                Id = "admin_teleport",
                FromPlayfieldId = player.CurrentPlayfieldId,
                ToPlayfieldId = targetPlayfieldId,
                RouteKey = routeKey,
                TrackReturnContext = false,
                UseReturnContext = false
            };

            player.PendingZoneTransition = new PendingZoneTransitionState(
                syntheticLink,
                destinationInstance,
                pendingReturnContext: null);
            // Protect admin teleport from being hijacked by normal zone requests
            // arriving from nearby triggers while transition/load settles.
            player.AdminTeleportLockUntilUtc = DateTime.UtcNow.AddSeconds(12);
            player.WorldCharacter.InputDirection = new Vector3(0f, 0f, 0f);

            decision = new ZoneTransitionDecision(
                syntheticLink.Id,
                targetPlayfieldId,
                spawnAoX,
                spawnAoY,
                spawnAoZ,
                targetYaw,
                destinationInstance.TemplatePlayfieldId,
                destinationInstance.RouteKey,
                destinationInstance.ShardId,
                UsedReturnContext: false);

            message = $"Admin teleport approved to PF {targetPlayfieldId}.";
            return true;
        }

        private bool TryResolveTeleportDefaultForPlayfield(int playfieldId, out Vector3 aoPosition)
        {
            aoPosition = new Vector3(0f, 0f, 0f);
            if (playfieldId <= 0)
                return false;

            // Reload each request so edits are immediately respected.
            List<TeleportDefaultEntry> defaults = LoadTeleportDefaults();
            if (defaults == null || defaults.Count == 0)
                return false;

            TeleportDefaultEntry entry = defaults.FirstOrDefault(d => d != null && d.PlayfieldId == playfieldId);
            if (entry == null)
                return false;

            float x = IsFinite(entry.AoX) && MathF.Abs(entry.AoX) > 0.0001f ? entry.AoX : entry.X;
            float y = IsFinite(entry.AoY) && MathF.Abs(entry.AoY) > 0.0001f ? entry.AoY : entry.Y;
            float z = IsFinite(entry.AoZ) && MathF.Abs(entry.AoZ) > 0.0001f ? entry.AoZ : entry.Z;
            aoPosition = new Vector3(x, y, z);
            return true;
        }

        private List<TeleportDefaultEntry> LoadTeleportDefaults()
        {
            try
            {
                string path = Path.Combine(_gameData.Paths.ServerPlayfieldsRoot, "teleport_defaults.json");
                if (!File.Exists(path))
                    path = _gameData.Paths.ResolveAuthoritativeAoDataFile(Path.Combine("Playfields", "teleport_defaults.json"));
                if (!File.Exists(path))
                    return new List<TeleportDefaultEntry>();

                string json = File.ReadAllText(path);
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<TeleportDefaultEntry>();

                string trimmed = json.TrimStart();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    var list = JsonSerializer.Deserialize<List<TeleportDefaultEntry>>(json, options);
                    return list ?? new List<TeleportDefaultEntry>();
                }

                var wrapped = JsonSerializer.Deserialize<TeleportDefaultsFile>(json, options);
                if (wrapped?.Defaults != null && wrapped.Defaults.Count > 0)
                    return wrapped.Defaults;

                // Fallback for object-shaped payloads that may not use "Defaults".
                var listFallback = JsonSerializer.Deserialize<List<TeleportDefaultEntry>>(json, options);
                return listFallback ?? new List<TeleportDefaultEntry>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AO.Server] Failed loading teleport defaults: {ex.GetType().Name}: {ex.Message}");
                return new List<TeleportDefaultEntry>();
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public bool TryGrantAdminItem(Guid sessionId, long instanceId, out long grantedInstanceId, out string message)
        {
            grantedInstanceId = 0;
            message = "Item grant rejected.";
            if (!_players.TryGetValue(sessionId, out var player))
            {
                message = "Unknown session.";
                return false;
            }

            if (!player.AllowDebugCharacterEditing)
            {
                message = "Item browser grants are restricted to localhost/admin sessions.";
                return false;
            }

            var dataInstance = ResolveAuthoritativeItemInstance(instanceId);
            if (dataInstance?.Definition == null)
            {
                message = $"Grant rejected: item {instanceId} was not found in authoritative AO data.";
                return false;
            }

            var coreInstance = CreateCoreItemInstance(dataInstance, dataInstance.InstanceId);
            if (coreInstance == null)
            {
                message = $"Grant rejected: item {instanceId} could not be converted to a runtime item.";
                return false;
            }

            if (!player.Profile.Inventory.TryAddToMain(coreInstance))
            {
                message = "Grant rejected: inventory is full.";
                return false;
            }

            grantedInstanceId = dataInstance.InstanceId;
            message = $"Granted item: {dataInstance.Definition.Name}.";
            return true;
        }

        public bool TryGetPlayer(Guid sessionId, out ConnectedPlayer player)
        {
            return _players.TryGetValue(sessionId, out player);
        }

        public ServerActionResult HandleAction(Guid sessionId, ClientAction action)
        {
            if (!_players.TryGetValue(sessionId, out var player))
                return ServerActionResult.Fail("Unknown session.");

            return action switch
            {
                MoveAction move => HandleMove(player, move),
                StopMoveAction => HandleStop(player),
                EquipItemAction equip => HandleEquip(player, equip),
                UnequipSlotAction unequip => HandleUnequip(player, unequip),
                IncreaseStatAction increase => HandleIncreaseStat(player, increase),
                GainExperienceAction xp => HandleGainExperience(player, xp),
                ZoneTransitionAction zone => HandleZoneTransitionRequest(player, zone),
                ZoneLoadedAction zoneLoaded => HandleZoneLoadedWithInstanceSync(player, zoneLoaded),
                AttackRuntimeEntityAction attack => HandleAttackRuntimeEntity(player, attack),
                SetCharacterSettingsAction settings => HandleSetCharacterSettings(player, settings),
                _ => ServerActionResult.Fail($"Unsupported action type '{action.GetType().Name}'.")
            };
        }

        public ZoneTransitionDecision? TryResolveZoneTransition(Guid sessionId, string zoneLinkId, int currentPlayfieldId)
        {
            if (!_players.TryGetValue(sessionId, out var player))
                return null;

            if (string.IsNullOrWhiteSpace(zoneLinkId))
                return null;

            if (!_zoneLinksById.TryGetValue(zoneLinkId, out var link))
                return null;

            if (player.CurrentPlayfieldId != currentPlayfieldId)
                return null;

            if (link.FromPlayfieldId != player.CurrentPlayfieldId)
                return null;

            ZoneResolution resolution = ResolveZoneDestination(player, link);
            if (!resolution.Success || resolution.PlayfieldId <= 0)
                return null;

            PlayfieldInstanceKey destinationInstance = resolution.ReturnContextUsed
                ? resolution.ReturnInstanceKey
                : AllocateDestinationInstance(player, resolution.TemplatePlayfieldId, resolution.RouteKey, link.MaxPlayersPerShard);

            ReturnContextTicket? pendingReturnContext = null;
            if (link.TrackReturnContext && !link.UseReturnContext)
            {
                var sourceAo = link.SourceAOPosition != null
                    ? new ServerVec3 { X = link.SourceAOPosition.X, Y = link.SourceAOPosition.Y, Z = link.SourceAOPosition.Z }
                    : null;

                pendingReturnContext = new ReturnContextTicket(
                    player.CurrentPlayfieldId,
                    player.CurrentInstanceKey,
                    zoneLinkId,
                    sourceAo,
                    DateTime.UtcNow);
            }

            player.PendingZoneTransition = new PendingZoneTransitionState(link, destinationInstance, pendingReturnContext);
            player.WorldCharacter.InputDirection = new Vector3(0f, 0f, 0f);

            return new ZoneTransitionDecision(
                link.Id,
                resolution.PlayfieldId,
                resolution.SpawnAoX,
                resolution.SpawnAoY,
                resolution.SpawnAoZ,
                resolution.TargetYaw,
                destinationInstance.TemplatePlayfieldId,
                destinationInstance.RouteKey,
                destinationInstance.ShardId,
                resolution.ReturnContextUsed);
        }

        public void TickOnce()
        {
            DateTime now = DateTime.UtcNow;
            _loop.TickOnce(_world);
            UpdateMobCombat(AO.Core.Simulation.GameLoop.TickDelta);
            UpdatePlayerRuntimeAttacks(now);
            SweepCorpseLifecycle(now);
            _playfieldRuntime.SweepLifecycle(_playfieldWarmTtl);
        }

        public List<AO.Server.Transport.RuntimeEntitySnapshot> BuildRuntimeSnapshot(int playfieldId)
        {
            ApplyDueRuntimeRespawns(DateTime.UtcNow);
            var snapshots = _playfieldRuntime.BuildSnapshots(playfieldId);
            ApplyMobCombatToSnapshots(snapshots);
            AppendCorpseSnapshots(playfieldId, snapshots);
            return snapshots;
        }

        public RuntimeInteractionResult ResolveRuntimeInteraction(Guid sessionId, string entityId)
        {
            if (!_players.TryGetValue(sessionId, out var player))
                return RuntimeInteractionResult.Fail(entityId, "Unknown session.");

            if (TryOpenCorpseLoot(player, entityId, out RuntimeInteractionResult lootResult))
                return lootResult;

            if (!_playfieldRuntime.TryResolveInteraction(player.CurrentPlayfieldId, entityId, DateTime.UtcNow, out var interaction))
                return RuntimeInteractionResult.Fail(entityId, "Runtime interaction resolution failed.");

            if (!interaction.Success)
            {
                return RuntimeInteractionResult.Fail(
                    interaction.EntityId,
                    interaction.Message,
                    interaction.InteractionType,
                    interaction.InteractionId,
                    interaction.DisplayName,
                    interaction.UseCount,
                    interaction.CooldownRemainingSeconds,
                    interaction.IsEnabled);
            }

            if (string.Equals(interaction.InteractionType, "zone", StringComparison.OrdinalIgnoreCase))
            {
                ZoneTransitionDecision? decision = TryResolveZoneTransition(sessionId, interaction.InteractionId, player.CurrentPlayfieldId);
                return decision.HasValue
                    ? RuntimeInteractionResult.Zone(
                        interaction.EntityId,
                        $"Zone transition approved via {interaction.DisplayName}.",
                        decision.Value,
                        interaction.InteractionType,
                        interaction.InteractionId,
                        interaction.DisplayName,
                        interaction.UseCount,
                        interaction.CooldownRemainingSeconds,
                        interaction.IsEnabled)
                    : RuntimeInteractionResult.Fail(
                        interaction.EntityId,
                        $"Zone interaction rejected for {interaction.DisplayName}.",
                        interaction.InteractionType,
                        interaction.InteractionId,
                        interaction.DisplayName,
                        interaction.UseCount,
                        interaction.CooldownRemainingSeconds,
                        interaction.IsEnabled);
            }

            string displayName = string.IsNullOrWhiteSpace(interaction.DisplayName)
                ? interaction.InteractionType
                : interaction.DisplayName;

            string message = interaction.InteractionType.ToLowerInvariant() switch
            {
                "shop" => $"Shop service reached: {displayName}. Server-side shop inventory is the next slice.",
                "talk" => $"Conversation requested with {displayName}.",
                "use" => $"Interaction acknowledged for {displayName}.",
                _ => interaction.Message
            };

            return RuntimeInteractionResult.Notice(
                interaction.EntityId,
                message,
                interaction.InteractionType,
                interaction.InteractionId,
                interaction.DisplayName,
                interaction.UseCount,
                interaction.CooldownRemainingSeconds,
                interaction.IsEnabled);
        }

        private ServerActionResult HandleMove(ConnectedPlayer player, MoveAction action)
        {
            float x = Clamp(action.X, -1f, 1f);
            float z = Clamp(action.Z, -1f, 1f);
            player.WorldCharacter.InputDirection = new Vector3(x, 0f, z);
            return ServerActionResult.Ok("Movement intent accepted.");
        }

        private static ServerActionResult HandleStop(ConnectedPlayer player)
        {
            player.WorldCharacter.InputDirection = new Vector3(0f, 0f, 0f);
            return ServerActionResult.Ok("Movement stopped.");
        }

        public List<string> ConsumeRespawnedRuntimeEntityIds()
        {
            if (_respawnedRuntimeEntityIds.Count == 0)
                return new List<string>();

            var result = new List<string>(_respawnedRuntimeEntityIds);
            _respawnedRuntimeEntityIds.Clear();
            return result;
        }

        public List<string> GetRecentRespawnedRuntimeEntityIds(int playfieldId)
        {
            if (playfieldId <= 0 || _recentRuntimeRespawnUpsertsByEntityId.Count == 0)
                return new List<string>();

            DateTime now = DateTime.UtcNow;
            var expired = new List<string>();
            var result = new List<string>();
            foreach (var pair in _recentRuntimeRespawnUpsertsByEntityId)
            {
                if (pair.Value <= now)
                {
                    expired.Add(pair.Key);
                    continue;
                }

                if (_playfieldRuntime.TryGetEntity(playfieldId, pair.Key, out RuntimeEntity entity) && entity != null)
                    result.Add(pair.Key);
            }

            for (int i = 0; i < expired.Count; i++)
                _recentRuntimeRespawnUpsertsByEntityId.Remove(expired[i]);

            return result;
        }

        private ServerActionResult HandleAttackRuntimeEntity(ConnectedPlayer player, AttackRuntimeEntityAction action)
        {
            if (player == null || action == null || string.IsNullOrWhiteSpace(action.EntityId))
                return ServerActionResult.Fail("Invalid attack target.");

            if (!_playfieldRuntime.TryGetEntity(player.CurrentPlayfieldId, action.EntityId, out RuntimeEntity entity) || entity == null)
                return ServerActionResult.Fail("Attack target was not found.");

            if (!IsCombatRuntimeEntity(entity))
                return ServerActionResult.Fail($"{entity.DisplayName} is not attackable.");

            MobCombatState state = GetOrCreateMobCombatState(entity);
            if (state.Health <= 0)
                return ServerActionResult.Fail($"{entity.DisplayName} is already dead.");

            DateTime now = DateTime.UtcNow;
            if (action.DamageAmount > 0)
                return ApplyImmediatePlayerRuntimeDamage(player, entity, state, action.DamageAmount, now);

            state.TargetSessionId = player.SessionId;
            state.PlayfieldId = player.CurrentPlayfieldId;
            state.IsInCombat = true;
            state.CombatState = "attacking";
            if (IsFinite(action.TargetX) && IsFinite(action.TargetY) && IsFinite(action.TargetZ))
            {
                state.HasTargetPosition = true;
                state.TargetX = action.TargetX;
                state.TargetY = action.TargetY;
                state.TargetZ = action.TargetZ;
            }
            state.LastTouchedUtc = now;
            return ServerActionResult.Ok($"Attacking {entity.DisplayName}.");
        }

        private ServerActionResult ApplyImmediatePlayerRuntimeDamage(
            ConnectedPlayer player,
            RuntimeEntity entity,
            MobCombatState state,
            int damageAmount,
            DateTime now)
        {
            if (player == null || entity == null || state == null)
                return ServerActionResult.Fail("Invalid attack target.");

            int playerDamage = Math.Max(1, damageAmount);
            state.Health = Math.Max(0, state.Health - playerDamage);
            state.LastDamage = playerDamage;
            state.LastAttackTick = Tick;
            state.LastTouchedUtc = now;

            if (state.Health <= 0)
            {
                ClearPendingRuntimeAttack(player);
                state.IsInCombat = false;
                state.CombatState = "dead";
                CreateCorpseLoot(entity, state, player.SessionId);
                return ServerActionResult.Ok($"{entity.DisplayName} defeated. Right-click the corpse to loot.");
            }

            state.TargetSessionId = player.SessionId;
            state.PlayfieldId = player.CurrentPlayfieldId;
            state.IsInCombat = true;
            state.CombatState = "attacking";
            return ServerActionResult.Ok($"Hit {entity.DisplayName} for {playerDamage} damage.");
        }

        private ServerActionResult HandleEquip(ConnectedPlayer player, EquipItemAction action)
        {
            var dataInstance = ResolveAuthoritativeItemInstance(action.InstanceId);
            if (dataInstance?.Definition == null)
                return ServerActionResult.Fail($"Equip rejected: item {action.InstanceId} was not found in authoritative AO data.");

            var reasons = GetEquipValidationFailures(player.Profile, dataInstance, action.SlotId);
            if (reasons.Count > 0)
                return ServerActionResult.Fail($"Cannot equip {dataInstance.Definition.Name}: {string.Join(" ", reasons)}");

            var equipped = player.Profile.Equipment.GetAllEquipped();
            bool movingAlreadyEquipped = equipped.Any(kv => kv.Value == action.InstanceId);

            CoreItems.ItemInstance movingInstance = null;
            if (!movingAlreadyEquipped)
            {
                if (!TryRemoveInventoryInstanceById(player.Profile, action.InstanceId, out movingInstance) || movingInstance == null)
                {
                    TryRemoveInventoryInstanceByAoid(player.Profile, dataInstance.Definition.Id, out movingInstance);
                }
            }

            if (movingInstance == null && !movingAlreadyEquipped)
            {
                // Reconciliation path for client/server inventory drift:
                // materialize the requested authoritative item and proceed.
                var recovered = ResolveCoreInstanceById(action.InstanceId);
                if (recovered != null)
                {
                    // Prefer true inventory move when possible.
                    if (player.Profile.Inventory.TryAddToMain(recovered))
                    {
                        if (!TryRemoveInventoryInstanceById(player.Profile, recovered.InstanceId, out movingInstance) || movingInstance == null)
                            TryRemoveInventoryInstanceByAoid(player.Profile, recovered.Definition?.AOID ?? dataInstance.Definition.Id, out movingInstance);
                    }

                    // Final fallback: equip directly from recovered instance.
                    if (movingInstance == null)
                        movingInstance = recovered;
                }

                if (movingInstance == null)
                    return ServerActionResult.Fail($"Equip rejected: item {action.InstanceId} is not present in your inventory.");
            }

            long effectiveInstanceId = movingAlreadyEquipped
                ? action.InstanceId
                : (movingInstance?.InstanceId ?? action.InstanceId);

            CoreItems.ItemInstance replacedInstance = null;
            if (equipped.TryGetValue(action.SlotId, out var replacedId) && replacedId != effectiveInstanceId)
            {
                if (!player.Profile.UnequipSlot(action.SlotId))
                {
                    if (movingInstance != null)
                        player.Profile.Inventory.TryAddToMain(movingInstance);
                    return ServerActionResult.Fail("Equip rejected: could not clear occupied slot.");
                }

                replacedInstance = ResolveCoreInstanceById(replacedId);
                if (replacedInstance != null && !player.Profile.Inventory.TryAddToMain(replacedInstance))
                {
                    if (movingInstance != null)
                        player.Profile.Inventory.TryAddToMain(movingInstance);
                    player.Profile.EquipItem(action.SlotId, replacedId);
                    return ServerActionResult.Fail("Equip rejected: inventory is full for swapped item.");
                }
            }

            bool success = player.Profile.EquipItem(action.SlotId, effectiveInstanceId);
            if (!success)
            {
                if (movingInstance != null)
                    player.Profile.Inventory.TryAddToMain(movingInstance);
                if (replacedId != 0 && replacedId != effectiveInstanceId)
                    player.Profile.EquipItem(action.SlotId, replacedId);
            }
            return success
                ? ServerActionResult.Ok($"Equip accepted for {dataInstance.Definition.Name} in slot {action.SlotId}.")
                : ServerActionResult.Fail("Equip rejected by authoritative rules.");
        }

        private AO.Data.Core.ItemInstance ResolveAuthoritativeItemInstance(long instanceId)
        {
            var dataInstance = _gameData.GetItemInstance(instanceId);
            if (dataInstance?.Definition != null)
                return dataInstance;

            if (instanceId < int.MinValue || instanceId > int.MaxValue)
                return null;

            var rawItem = _gameData.Items.FirstOrDefault(i => i != null && i.AOID == (int)instanceId);
            if (rawItem == null)
                return null;

            return new AO.Data.Core.ItemInstance
            {
                InstanceId = rawItem.AOID,
                DefinitionId = rawItem.AOID,
                Quantity = 1,
                Definition = new AO.Data.Core.ItemDefinition
                {
                    Id = rawItem.AOID,
                    Name = rawItem.Name ?? string.Empty,
                    Description = rawItem.Description ?? string.Empty,
                    IconId = rawItem.StatValues?.FirstOrDefault(v => v != null && v.Stat == 79)?.RawValue ?? 0,
                    SlotType = rawItem.DBType,
                    Type = rawItem.Type,
                    RequiredLevel = rawItem.Level,
                    StatModifiers = rawItem.StatModifiers ?? new List<AO.Data.Core.StatModifier>()
                }
            };
        }

        private ServerActionResult HandleUnequip(ConnectedPlayer player, UnequipSlotAction action)
        {
            var equipped = player.Profile.Equipment.GetAllEquipped();
            if (!equipped.TryGetValue(action.SlotId, out var equippedInstanceId))
                return ServerActionResult.Fail("Unequip rejected: slot is empty.");

            bool success = player.Profile.UnequipSlot(action.SlotId);
            if (success)
            {
                var item = ResolveCoreInstanceById(equippedInstanceId);
                if (item != null)
                    player.Profile.Inventory.TryAddToMain(item);
            }
            return success
                ? ServerActionResult.Ok("Unequip accepted.")
                : ServerActionResult.Fail("Unequip rejected.");
        }

        private bool TryRemoveInventoryInstanceById(AO.Core.Characters.Character character, long instanceId, out CoreItems.ItemInstance removed)
        {
            removed = null;
            if (character?.Inventory == null || instanceId == 0)
                return false;

            return character.Inventory.TryRemoveAnyByInstanceId(instanceId, out removed);
        }

        private bool TryRemoveInventoryInstanceByAoid(AO.Core.Characters.Character character, int aoid, out CoreItems.ItemInstance removed)
        {
            removed = null;
            if (character?.Inventory == null || aoid <= 0)
                return false;

            return character.Inventory.TryRemoveAnyByAoid(aoid, out removed);
        }

        private CoreItems.ItemInstance ResolveCoreInstanceById(long instanceId)
        {
            var dataInstance = ResolveAuthoritativeItemInstance(instanceId);
            if (dataInstance?.Definition == null)
                return null;

            return CreateCoreItemInstance(dataInstance, instanceId);
        }

        private static ServerActionResult HandleIncreaseStat(ConnectedPlayer player, IncreaseStatAction action)
        {
            bool success = player.Profile.TryIncreaseStat(action.StatName, action.Amount);
            return success
                ? ServerActionResult.Ok("Stat increase accepted.")
                : ServerActionResult.Fail("Stat increase rejected.");
        }

        private static ServerActionResult HandleGainExperience(ConnectedPlayer player, GainExperienceAction action)
        {
            int levels = player.Profile.AddExperience(action.Amount);
            return ServerActionResult.Ok($"Experience accepted. Levels gained: {levels}.");
        }

        private ServerActionResult HandleZoneTransitionRequest(ConnectedPlayer player, ZoneTransitionAction action)
        {
            if (DateTime.UtcNow < player.AdminTeleportLockUntilUtc)
            {
                return ServerActionResult.Fail("Zone transition blocked: admin teleport lock is active.");
            }

            if (player.PendingZoneTransition != null
                && string.Equals(player.PendingZoneTransition.Link.Id, "admin_teleport", StringComparison.OrdinalIgnoreCase))
            {
                return ServerActionResult.Fail("Zone transition blocked: admin teleport is still in progress.");
            }

            ZoneTransitionDecision? decision = TryResolveZoneTransition(player.SessionId, action.ZoneLinkId, action.CurrentPlayfieldId);
            return decision.HasValue
                ? ServerActionResult.Ok($"Zone transition approved to PF {decision.Value.PlayfieldId}.")
                : ServerActionResult.Fail("Zone transition rejected by authoritative rules.");
        }

        private AO.Data.Core.ItemDefinition ResolveAuthoritativeItemDefinitionByAoid(int aoid)
        {
            if (aoid <= 0)
                return null;

            var rawItem = _gameData.Items.FirstOrDefault(i => i != null && i.AOID == aoid);
            if (rawItem == null)
                return null;

            return new AO.Data.Core.ItemDefinition
            {
                Id = rawItem.AOID,
                Name = rawItem.Name ?? string.Empty,
                Description = rawItem.Description ?? string.Empty,
                IconId = rawItem.StatValues?.FirstOrDefault(v => v != null && v.Stat == 79)?.RawValue ?? 0,
                SlotType = rawItem.DBType,
                Type = rawItem.Type,
                RequiredLevel = rawItem.Level,
                StatModifiers = rawItem.StatModifiers ?? new List<AO.Data.Core.StatModifier>()
            };
        }

        private static ServerActionResult HandleZoneLoaded(ConnectedPlayer player, ZoneLoadedAction action)
        {
            if (player.PendingZoneTransition != null)
            {
                int expectedPf = player.PendingZoneTransition.DestinationInstance.TemplatePlayfieldId;
                if (action.PlayfieldId != expectedPf)
                {
                    // Keep pending transition active until client acknowledges the
                    // exact authoritative destination playfield.
                    return ServerActionResult.Fail(
                        $"Zone load ignored for PF {action.PlayfieldId}; awaiting authoritative PF {expectedPf}.");
                }

                player.CurrentPlayfieldId = expectedPf;
                player.CurrentInstanceKey = player.PendingZoneTransition.DestinationInstance;
                player.ReturnContextTicket = player.PendingZoneTransition.PendingReturnContext;

                if (string.Equals(
                        player.PendingZoneTransition.Link.Id,
                        "admin_teleport",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Clear admin lock when the authoritative target PF is confirmed.
                    player.AdminTeleportLockUntilUtc = DateTime.MinValue;
                }
            }
            else if (player.CurrentInstanceKey.TemplatePlayfieldId != action.PlayfieldId)
            {
                player.CurrentPlayfieldId = action.PlayfieldId;
                player.CurrentInstanceKey = new PlayfieldInstanceKey(action.PlayfieldId, "default", 1);
            }
            else
            {
                player.CurrentPlayfieldId = action.PlayfieldId;
            }

            player.PendingZoneTransition = null;
            player.WorldCharacter.InputDirection = new Vector3(0f, 0f, 0f);
            player.WorldCharacter.Position = new Vector3(action.X, action.Y, action.Z);
            return ServerActionResult.Ok($"Zone load acknowledged for PF {action.PlayfieldId}.");
        }

        private ServerActionResult HandleZoneLoadedWithInstanceSync(ConnectedPlayer player, ZoneLoadedAction action)
        {
            int beforePlayfieldId = player.CurrentPlayfieldId;
            PlayfieldInstanceKey before = player.CurrentInstanceKey;
            ServerActionResult result = HandleZoneLoaded(player, action);
            RemovePlayerFromInstance(player.SessionId, before);
            AddPlayerToInstance(player.SessionId, player.CurrentInstanceKey);
            if (beforePlayfieldId != player.CurrentPlayfieldId)
            {
                _playfieldRuntime.MarkPlayfieldInactive(beforePlayfieldId);
                _playfieldRuntime.MarkPlayfieldActive(player.CurrentPlayfieldId);
            }
            return result;
        }
        private ServerActionResult HandleSetCharacterSettings(ConnectedPlayer player, SetCharacterSettingsAction action)
        {
            if (!player.AllowDebugCharacterEditing)
                return ServerActionResult.Fail("Character test settings are only allowed from localhost right now.");

            int nextBreedId = action.BreedId <= 0 ? 1 : action.BreedId;
            int nextProfessionId = action.ProfessionId <= 0 ? 1 : action.ProfessionId;
            int nextLevel = Math.Max(1, action.Level);
            int nextSexCode = nextBreedId == 4 ? 2 : (action.Sex == 1 ? 1 : 0);
            bool identityChanged = player.Profile.BreedId != nextBreedId || player.Profile.ProfessionId != nextProfessionId;
            bool sexChanged = player.CharacterSexCode != nextSexCode;
            string professionName = _gameData.ResolveProfession(nextProfessionId)?.Name ?? "Prototype";

            player.Profile.SetIdentity(nextBreedId, nextProfessionId, new Profession { Name = professionName });
            player.CharacterSexCode = nextSexCode;

            if (identityChanged || sexChanged)
            {
                ApplyDefaultNonAbilityValues(player.Profile, forceReset: true);
                ApplyStartingAbilityValues(player.Profile, nextBreedId);
            }

            player.Profile.SetLevelAndRebuildIp(nextLevel, preserveSpent: !(identityChanged || sexChanged));
            if (action.Experience > 0)
                player.Profile.AddExperience(action.Experience);
            player.Profile.StatsContainer.Recalculate();
            player.Profile.RecalculateDerivedStats();

            return ServerActionResult.Ok(
                $"Character settings applied: L{player.Profile.Level.Level}, Breed={player.Profile.BreedId}, Profession={player.Profile.ProfessionId}, Sex={player.CharacterSexCode}.");
        }

        private List<string> GetEquipValidationFailures(AO.Core.Characters.Character profile, AO.Data.Core.ItemInstance dataInstance, int slotId)
        {
            var reasons = new List<string>();
            var coreDef = BuildCoreItemDefinition(dataInstance.Definition);
            if (!AO.Core.Characters.EquipmentValidator.CanEquip(profile, coreDef, slotId, out var slotReason) && !string.IsNullOrWhiteSpace(slotReason))
                reasons.Add(slotReason);

            var rawItem = _gameData.Items.FirstOrDefault(i => i != null && i.AOID == dataInstance.DefinitionId);
            if (rawItem?.ActionData?.Actions == null)
                return reasons;

            foreach (var action in rawItem.ActionData.Actions)
            {
                if (action == null || (action.Action != 6 && action.Action != 8) || action.Criteria == null)
                    continue;

                foreach (var criterion in action.Criteria)
                {
                    if (criterion == null || criterion.Operator == 4 || criterion.Value1 <= 0)
                        continue;

                    if (TryBuildEquipRequirementFailure(profile, criterion, out var failure) && !string.IsNullOrWhiteSpace(failure))
                        reasons.Add(failure);
                }
            }

            return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private CoreItems.ItemDefinition BuildCoreItemDefinition(AO.Data.Core.ItemDefinition dataDefinition)
        {
            var coreDef = new CoreItems.ItemDefinition(dataDefinition?.Name ?? string.Empty, dataDefinition?.Id ?? 0, dataDefinition?.SlotType ?? 0);
            if (dataDefinition?.StatModifiers != null)
            {
                foreach (var mod in dataDefinition.StatModifiers)
                {
                    coreDef.AddModifier(new AO.Core.Modifiers.StatModifier(mod.StatId, mod.Value));
                }
            }

            return coreDef;
        }

        private bool TryBuildEquipRequirementFailure(AO.Core.Characters.Character profile, AO.Data.Core.ItemActionCriterion criterion, out string failure)
        {
            failure = string.Empty;

            int statId = criterion.Value1;
            int current = GetRequirementCurrentValue(profile, statId);
            int rawTarget = criterion.Value2;

            bool pass = criterion.Operator switch
            {
                0 => current == rawTarget,
                1 => current != rawTarget,
                2 => current > rawTarget,
                3 => current < rawTarget,
                5 => current >= rawTarget,
                6 => current <= rawTarget,
                _ => true
            };

            if (pass)
                return false;

            string statName = _gameData.GetStatName(statId);
            if (string.IsNullOrWhiteSpace(statName))
                statName = $"Stat {statId}";

            if (statId == 60)
            {
                string wantProf = _gameData.ResolveProfession(rawTarget)?.Name ?? rawTarget.ToString();
                string haveProf = _gameData.ResolveProfession(profile.ProfessionId)?.Name ?? profile.ProfessionId.ToString();
                failure = criterion.Operator switch
                {
                    0 => $"Requires profession {wantProf} (current: {haveProf}).",
                    1 => $"Requires profession not {wantProf} (current: {haveProf}).",
                    _ => $"Requirement failed: {statName}."
                };
                return true;
            }

            int displayTarget = rawTarget;
            string op = criterion.Operator switch
            {
                0 => "=",
                1 => "!=",
                2 => ">=",
                3 => "<",
                5 => ">=",
                6 => "<=",
                _ => "="
            };
            if (criterion.Operator == 2)
                displayTarget = rawTarget + 1;

            failure = $"Requires {statName} {op} {displayTarget} (current: {current}).";
            return true;
        }

        private static int GetRequirementCurrentValue(AO.Core.Characters.Character profile, int statId)
        {
            if (statId == 60)
                return profile.ProfessionId;
            if (statId == 37)
                return profile.GetCurrentTitleLevel();
            if (statId == 54)
                return Math.Max(1, profile.Level?.Level ?? 1);

            return profile.StatsContainer.GetFinalStat(statId);
        }

        private void ApplyDefaultNonAbilityValues(AO.Core.Characters.Character profile, bool forceReset = false)
        {
            foreach (int statId in _gameData.GetKnownSkillStatIds())
            {
                if (statId is >= 16 and <= 21)
                    continue;

                string statName = _gameData.GetStatName(statId);
                if (string.IsNullOrWhiteSpace(statName) || statName.StartsWith("UnknownStat(", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (forceReset)
                {
                    profile.SetBaseStatValue(statName, 5);
                    continue;
                }

                int current = profile.StatsContainer.GetBaseStat(statId);
                if (current < 5)
                    profile.SetBaseStatValue(statName, 5);
            }
        }
        private void ApplyStartingAbilityValues(AO.Core.Characters.Character profile, int breedId)
        {
            int[] abilityStatIds = { 16, 17, 18, 19, 20, 21 };
            foreach (int statId in abilityStatIds)
            {
                int? value = _gameData.GetStartingBaseStatForBreedValue(statId, breedId);
                if (!value.HasValue)
                    continue;

                string statName = _gameData.GetStatName(statId);
                if (string.IsNullOrWhiteSpace(statName))
                    continue;

                profile.SetBaseStatValue(statName, value.Value);
            }
        }

        private void LoadZoneLinks()
        {
            _zoneLinksById.Clear();
            string path = Path.Combine(_gameData.Paths.ServerPlayfieldsRoot, "zone_links.json");
            if (!File.Exists(path))
                path = Path.Combine(_gameData.Paths.PlayfieldsRoot, "zone_links.json");
            if (!File.Exists(path))
                return;

            ZoneLinksFile? file = JsonSerializer.Deserialize<ZoneLinksFile>(File.ReadAllText(path));
            if (file?.Links == null)
                return;

            foreach (var link in file.Links)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Id))
                    continue;
                _zoneLinksById[link.Id] = link;
            }
        }

        private static PlayfieldInstanceKey BuildDefaultInstanceKey(int playfieldId)
        {
            return new PlayfieldInstanceKey(playfieldId, "default", 1);
        }

        private static string NormalizeRouteKey(string? key)
        {
            return string.IsNullOrWhiteSpace(key) ? "default" : key.Trim().ToLowerInvariant();
        }

        private string? ResolveTeamKey(ConnectedPlayer player)
        {
            _ = player;
            return null;
        }

        private PlayfieldInstanceKey AllocateDestinationInstance(ConnectedPlayer player, int templatePlayfieldId, string routeKey, int? maxPlayersOverride)
        {
            if (templatePlayfieldId <= 0)
                templatePlayfieldId = player.CurrentPlayfieldId;

            string normalizedRouteKey = NormalizeRouteKey(routeKey);
            int maxPlayers = Math.Max(1, maxPlayersOverride.GetValueOrDefault(DefaultMaxPlayersPerInstance));

            string? teamKey = ResolveTeamKey(player);
            if (!string.IsNullOrWhiteSpace(teamKey))
            {
                PlayfieldInstanceKey? existingTeamInstance = _players.Values
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.TeamKey) && string.Equals(p.TeamKey, teamKey, StringComparison.Ordinal))
                    .Select(p => p.CurrentInstanceKey)
                    .FirstOrDefault(k => k.TemplatePlayfieldId == templatePlayfieldId && string.Equals(k.RouteKey, normalizedRouteKey, StringComparison.OrdinalIgnoreCase));
                if (existingTeamInstance.HasValue)
                    return existingTeamInstance.Value;
            }

            var candidateShards = _instanceMembers.Keys
                .Where(k => k.TemplatePlayfieldId == templatePlayfieldId && string.Equals(k.RouteKey, normalizedRouteKey, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.ShardId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (int shard in candidateShards)
            {
                var key = new PlayfieldInstanceKey(templatePlayfieldId, normalizedRouteKey, shard);
                if (GetInstancePopulation(key) < maxPlayers)
                    return key;
            }

            int nextShard = candidateShards.Count == 0 ? 1 : candidateShards.Max() + 1;
            return new PlayfieldInstanceKey(templatePlayfieldId, normalizedRouteKey, nextShard);
        }

        private int GetInstancePopulation(PlayfieldInstanceKey key)
        {
            return _instanceMembers.TryGetValue(key, out var members) ? members.Count : 0;
        }

        private void AddPlayerToInstance(Guid sessionId, PlayfieldInstanceKey key)
        {
            if (!_instanceMembers.TryGetValue(key, out var members))
            {
                members = new HashSet<Guid>();
                _instanceMembers[key] = members;
            }

            members.Add(sessionId);
        }

        private void RemovePlayerFromInstance(Guid sessionId, PlayfieldInstanceKey key)
        {
            if (!_instanceMembers.TryGetValue(key, out var members))
                return;

            members.Remove(sessionId);
            if (members.Count == 0)
                _instanceMembers.Remove(key);
        }

        private ZoneResolution ResolveZoneDestination(ConnectedPlayer player, ZoneLinkDefinition link)
        {
            if (link.UseReturnContext && player.ReturnContextTicket.HasValue)
            {
                var ticket = player.ReturnContextTicket.Value;
                (float spawnX, float spawnY, float spawnZ) = ResolvePreferredSpawnAo(
                    ticket.OriginPlayfieldId,
                    primary: link.TargetAOSpawn ?? ticket.OriginAOSpawn,
                    secondary: link.FallbackTargetAOSpawn);
                float yaw = link.TargetYaw ?? link.FallbackTargetYaw ?? 0f;
                return ZoneResolution.SuccessResult(
                    ticket.OriginPlayfieldId,
                    spawnX,
                    spawnY,
                    spawnZ,
                    yaw,
                    ticket.OriginInstance.TemplatePlayfieldId,
                    ticket.OriginInstance.RouteKey,
                    returnContextUsed: true,
                    ticket.OriginInstance);
            }

            if (link.ToPlayfieldId > 0)
            {
                int templatePf = link.DestinationTemplatePlayfieldId > 0 ? link.DestinationTemplatePlayfieldId : link.ToPlayfieldId;
                string routeKey = NormalizeRouteKey(!string.IsNullOrWhiteSpace(link.DestinationRouteKey) ? link.DestinationRouteKey : link.RouteKey);
                (float spawnX, float spawnY, float spawnZ) = ResolvePreferredSpawnAo(
                    link.ToPlayfieldId,
                    primary: link.TargetAOSpawn,
                    secondary: link.FallbackTargetAOSpawn);
                return ZoneResolution.SuccessResult(
                    link.ToPlayfieldId,
                    spawnX,
                    spawnY,
                    spawnZ,
                    link.TargetYaw ?? 0f,
                    templatePf,
                    routeKey,
                    returnContextUsed: false,
                    default);
            }

            if (link.FallbackToPlayfieldId > 0)
            {
                int templatePf = link.FallbackDestinationTemplatePlayfieldId > 0 ? link.FallbackDestinationTemplatePlayfieldId : link.FallbackToPlayfieldId;
                string routeKey = NormalizeRouteKey(!string.IsNullOrWhiteSpace(link.FallbackDestinationRouteKey) ? link.FallbackDestinationRouteKey : link.RouteKey);
                (float spawnX, float spawnY, float spawnZ) = ResolvePreferredSpawnAo(
                    link.FallbackToPlayfieldId,
                    primary: link.FallbackTargetAOSpawn,
                    secondary: null);
                return ZoneResolution.SuccessResult(
                    link.FallbackToPlayfieldId,
                    spawnX,
                    spawnY,
                    spawnZ,
                    link.FallbackTargetYaw ?? 0f,
                    templatePf,
                    routeKey,
                    returnContextUsed: false,
                    default);
            }

            return ZoneResolution.Fail();
        }

        private (float x, float y, float z) ResolvePreferredSpawnAo(
            int destinationPlayfieldId,
            ServerVec3? primary,
            ServerVec3? secondary)
        {
            if (primary != null && IsFinite(primary.X) && IsFinite(primary.Y) && IsFinite(primary.Z))
                return (primary.X, primary.Y, primary.Z);

            if (secondary != null && IsFinite(secondary.X) && IsFinite(secondary.Y) && IsFinite(secondary.Z))
                return (secondary.X, secondary.Y, secondary.Z);

            if (TryResolveTeleportDefaultForPlayfield(destinationPlayfieldId, out var aoDefault))
                return (aoDefault.X, aoDefault.Y, aoDefault.Z);

            if (TryResolveInboundZoneLinkSpawn(destinationPlayfieldId, out float zx, out float zy, out float zz))
                return (zx, zy, zz);

            if (_playfieldRuntime.TryResolveHighestPoint(destinationPlayfieldId, out float hx, out float hy, out float hz))
                return (hx, hy, hz);

            return (0f, 0f, 0f);
        }

        private bool TryResolveInboundZoneLinkSpawn(int destinationPlayfieldId, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            if (destinationPlayfieldId <= 0 || _zoneLinksById.Count == 0)
                return false;

            // Prefer a stable inbound portal spawn when no teleport default exists.
            // This is generally safer than arbitrary runtime-entity heights.
            ZoneLinkDefinition best = null;
            float bestY = float.MinValue;
            foreach (var link in _zoneLinksById.Values)
            {
                if (link == null || link.ToPlayfieldId != destinationPlayfieldId || link.TargetAOSpawn == null)
                    continue;

                var s = link.TargetAOSpawn;
                if (!IsFinite(s.X) || !IsFinite(s.Y) || !IsFinite(s.Z))
                    continue;

                if (s.Y > bestY)
                {
                    bestY = s.Y;
                    best = link;
                }
            }

            if (best?.TargetAOSpawn == null)
                return false;

            x = best.TargetAOSpawn.X;
            y = best.TargetAOSpawn.Y;
            z = best.TargetAOSpawn.Z;
            return true;
        }

        public bool IsSameVisibleInstance(Guid viewerSessionId, ConnectedPlayer candidate)
        {
            if (!_players.TryGetValue(viewerSessionId, out var viewer) || candidate == null)
                return false;

            if (viewer.CurrentPlayfieldId != candidate.CurrentPlayfieldId)
                return false;

            return viewer.CurrentInstanceKey == candidate.CurrentInstanceKey;
        }

        private void UpdateMobCombat(float deltaTime)
        {
            if (_mobCombatByEntityId.Count == 0)
                return;

            DateTime now = DateTime.UtcNow;
            var inactive = new List<string>();
            foreach (var kvp in _mobCombatByEntityId)
            {
                MobCombatState state = kvp.Value;
                if (state == null || !state.IsInCombat)
                    continue;

                if (!_players.TryGetValue(state.TargetSessionId, out ConnectedPlayer target)
                    || target.CurrentPlayfieldId != state.PlayfieldId)
                {
                    inactive.Add(kvp.Key);
                    continue;
                }

                Vector3 targetPos = state.HasTargetPosition
                    ? new Vector3(state.TargetX, state.TargetY, state.TargetZ)
                    : target.WorldCharacter.Position;
                float dx = targetPos.X - state.X;
                float dz = targetPos.Z - state.Z;
                float distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance > 0.001f)
                    state.YawDegrees = MathF.Atan2(dx, dz) * (180f / MathF.PI);

                float attackRange = MathF.Max(0.75f, state.AttackRange);
                if (distance > attackRange)
                {
                    state.CombatState = "chasing";
                    float move = MathF.Min(distance - attackRange, state.WalkSpeed * MathF.Max(0f, deltaTime));
                    if (move > 0f && distance > 0.001f)
                    {
                        float inv = 1f / distance;
                        state.X += dx * inv * move;
                        state.Y = state.SpawnY;
                        state.Z += dz * inv * move;
                    }

                    continue;
                }

                state.CombatState = "attacking";
                if (now < state.NextAttackUtc)
                    continue;

                int damage = RollMobDamage(state);
                state.LastDamage = damage;
                state.LastAttackTick = Tick;
                state.NextAttackUtc = now.AddSeconds(MathF.Max(0.5f, state.AttackRechargeSeconds));
                target.CurrentHealth = Math.Max(0, target.CurrentHealth - damage);
            }

            for (int i = 0; i < inactive.Count; i++)
                _mobCombatByEntityId.Remove(inactive[i]);
        }

        private void UpdatePlayerRuntimeAttacks(DateTime now)
        {
            if (_players.Count == 0)
                return;

            foreach (ConnectedPlayer player in _players.Values)
            {
                if (player == null || string.IsNullOrWhiteSpace(player.PendingRuntimeAttackEntityId))
                    continue;
                if (player.PendingRuntimeAttackImpactUtc > now)
                    continue;

                ApplyPlayerRuntimeAttack(player, player.PendingRuntimeAttackEntityId, now);
            }
        }

        private void ApplyPlayerRuntimeAttack(ConnectedPlayer player, string entityId, DateTime now)
        {
            if (player == null || string.IsNullOrWhiteSpace(entityId))
                return;

            double rechargeSeconds = Math.Max(0.1, player.PendingRuntimeAttackRechargeSeconds);
            ClearPendingRuntimeAttack(player);
            player.NextRuntimeAttackUtc = now.AddSeconds(rechargeSeconds);

            if (!_playfieldRuntime.TryGetEntity(player.CurrentPlayfieldId, entityId, out RuntimeEntity entity) || entity == null)
                return;
            if (!IsCombatRuntimeEntity(entity))
                return;

            MobCombatState state = GetOrCreateMobCombatState(entity);
            if (state == null || state.IsHidden || state.Health <= 0)
                return;

            int playerDamage = RollPlayerDamage(player);
            state.Health = Math.Max(0, state.Health - playerDamage);
            state.LastDamage = playerDamage;
            state.LastAttackTick = Tick;
            state.LastTouchedUtc = now;

            if (state.Health <= 0)
            {
                state.IsInCombat = false;
                state.CombatState = "dead";
                CreateCorpseLoot(entity, state, player.SessionId);
                return;
            }

            state.TargetSessionId = player.SessionId;
            state.PlayfieldId = player.CurrentPlayfieldId;
            state.IsInCombat = true;
            state.CombatState = "attacking";
        }

        private void ApplyMobCombatToSnapshots(List<RuntimeEntitySnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0 || _mobCombatByEntityId.Count == 0)
                return;

            DateTime now = DateTime.UtcNow;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                RuntimeEntitySnapshot snapshot = snapshots[i];
                if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EntityId))
                    continue;
                if (!_mobCombatByEntityId.TryGetValue(snapshot.EntityId, out MobCombatState state) || state == null)
                    continue;
                if (state.IsHidden)
                {
                    snapshots.RemoveAt(i);
                    continue;
                }

                snapshot.X = state.X;
                snapshot.Y = state.Y;
                snapshot.Z = state.Z;
                snapshot.YawDegrees = state.YawDegrees;
                snapshot.Health = state.Health;
                snapshot.MaxHealth = state.MaxHealth;
                snapshot.CombatState = state.IsInCombat ? state.CombatState : string.Empty;
                snapshot.CombatTargetSessionId = state.TargetSessionId == Guid.Empty ? string.Empty : state.TargetSessionId.ToString();
                snapshot.AttackRange = state.AttackRange;
                snapshot.AttackRechargeSeconds = state.AttackRechargeSeconds;
                snapshot.DamageMin = state.DamageMin;
                snapshot.DamageMax = state.DamageMax;
                snapshot.LastDamage = state.LastDamage;
                snapshot.LastAttackTick = state.LastAttackTick;

                if (state.Health <= 0)
                    snapshots.RemoveAt(i);
            }
        }

        private void AppendCorpseSnapshots(int playfieldId, List<RuntimeEntitySnapshot> snapshots)
        {
            if (playfieldId <= 0 || snapshots == null || _corpseLootByEntityId.Count == 0)
                return;

            DateTime now = DateTime.UtcNow;
            foreach (var pair in _corpseLootByEntityId)
            {
                CorpseLootState corpse = pair.Value;
                if (corpse == null || corpse.PlayfieldId != playfieldId || corpse.IsLooted)
                    continue;
                if (IsCorpseExpired(corpse, now))
                    continue;

                snapshots.Add(new RuntimeEntitySnapshot
                {
                    EntityId = corpse.EntityId,
                    ObjectType = corpse.ObjectType,
                    DisplayName = corpse.DisplayName,
                    PlayfieldId = corpse.PlayfieldId,
                    IdentityInstance = 0,
                    TemplateId = corpse.TemplateId,
                    MeshId = corpse.MeshId,
                    X = corpse.X,
                    Y = corpse.Y,
                    Z = corpse.Z,
                    YawDegrees = corpse.YawDegrees,
                    ImportKey = corpse.ImportKey,
                    MeshName = corpse.MeshName,
                    InteractionType = "loot",
                    InteractionId = corpse.EntityId,
                    InteractionLabel = "Loot",
                    InteractionRadius = 2.5f,
                    InteractionEnabled = true,
                    MaxHealth = Math.Max(1, corpse.MaxHealth),
                    Health = 0,
                    MaxNano = Math.Max(0, corpse.MaxNano),
                    Level = Math.Max(0, corpse.Level),
                    CombatState = "dead",
                    TemporaryRemainingSeconds = GetRemainingSeconds(corpse.ExpiresUtc, now),
                    TemporaryExpiresUnixMs = ToUnixMilliseconds(corpse.ExpiresUtc),
                    Description = AppendTemporaryDescription(corpse.Description, GetRemainingSeconds(corpse.ExpiresUtc, now))
                });
            }
        }

        private void CreateCorpseLoot(RuntimeEntity entity, MobCombatState state, Guid killerSessionId)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                return;

            DateTime now = DateTime.UtcNow;
            LootRuntimeSettings runtimeSettings = _lootService.ResolveRuntimeSettings(entity);
            DateTime respawnUtc = now.AddSeconds(runtimeSettings.RespawnSeconds);
            _runtimeRespawnsByEntityId[entity.EntityId] = new RuntimeRespawnState(
                entity.EntityId,
                entity.PlayfieldId,
                respawnUtc);

            state.CombatState = "dead";
            state.DeathUtc = now;
            state.RespawnUtc = respawnUtc;
            state.IsHidden = true;

            string corpseEntityId = $"{entity.EntityId}:corpse:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            if (_corpseLootByEntityId.ContainsKey(corpseEntityId))
                return;

            RolledLoot rolled = _lootService.RollLoot(entity);
            _corpseLootByEntityId[corpseEntityId] = new CorpseLootState
            {
                EntityId = corpseEntityId,
                SourceEntityId = entity.EntityId,
                PlayfieldId = entity.PlayfieldId,
                DisplayName = $"Remains of {entity.DisplayName ?? "Corpse"}",
                KillerSessionId = killerSessionId,
                CreatedUtc = now,
                ExpiresUtc = now.AddSeconds(runtimeSettings.CorpseSeconds),
                EmptyCorpseSeconds = runtimeSettings.EmptyCorpseSeconds,
                Credits = Math.Max(0, rolled.Credits),
                Items = rolled.Items?.ToList() ?? new List<RolledLootItem>(),
                ObjectType = entity.ObjectType ?? "mob",
                TemplateId = entity.TemplateId,
                MeshId = entity.MeshId,
                X = state.X,
                Y = state.Y,
                Z = state.Z,
                YawDegrees = state.YawDegrees,
                ImportKey = entity.ImportKey ?? string.Empty,
                MeshName = entity.MeshName ?? string.Empty,
                MaxHealth = Math.Max(1, state.MaxHealth),
                MaxNano = Math.Max(0, entity.MaxNano),
                Level = Math.Max(0, entity.Level),
                Description = entity.Description ?? string.Empty
            };

            Console.WriteLine(
                $"[AO.Server] Runtime mob defeated entityId={entity.EntityId} corpseEntityId={corpseEntityId} name='{entity.DisplayName}' corpseSeconds={runtimeSettings.CorpseSeconds} respawnSeconds={runtimeSettings.RespawnSeconds} emptyCorpseSeconds={runtimeSettings.EmptyCorpseSeconds} respawnUtc={respawnUtc:O}.");
        }

        public RuntimeInteractionResult TakeCorpseLoot(Guid sessionId, string entityId, int aoid, int slotIndex)
        {
            if (!_players.TryGetValue(sessionId, out ConnectedPlayer player))
                return RuntimeInteractionResult.Fail(entityId, "Unknown session.", "loot", entityId);

            if (aoid <= 0)
                return RuntimeInteractionResult.Fail(entityId, "Invalid loot item.", "loot", entityId);

            if (!_corpseLootByEntityId.TryGetValue(entityId, out CorpseLootState corpse) || corpse == null)
                return RuntimeInteractionResult.Fail(entityId, "No loot is available.", "loot", entityId);

            DateTime now = DateTime.UtcNow;
            if (IsCorpseExpired(corpse, now))
            {
                HideCorpse(entityId, now);
                return RuntimeInteractionResult.Fail(entityId, "No loot is available.", "loot", entityId);
            }

            if (corpse.PlayfieldId != player.CurrentPlayfieldId)
                return RuntimeInteractionResult.Fail(entityId, "That corpse is in another playfield.", "loot", entityId, corpse.DisplayName);

            int index = slotIndex >= 0
                && slotIndex < corpse.Items.Count
                && corpse.Items[slotIndex].Aoid == aoid
                    ? slotIndex
                    : corpse.Items.FindIndex(i => i.Aoid == aoid);
            if (index < 0)
                return RuntimeInteractionResult.Loot(entityId, "That item is no longer on the corpse.", corpse.DisplayName, 0, BuildLootSnapshots(corpse));

            RolledLootItem rolled = corpse.Items[index];
            CoreItems.ItemInstance item = CreateLootItemInstance(rolled.Aoid);
            if (item == null)
                return RuntimeInteractionResult.Loot(entityId, "Could not resolve loot item.", corpse.DisplayName, 0, BuildLootSnapshots(corpse));

            if (!TryAddLootToPlayerInventory(player, item, out string inventoryDebug))
            {
                Console.WriteLine(
                    $"[AO.Server] Loot rejected inventory_full utc={DateTime.UtcNow:O} player='{player.Profile?.Name ?? "Unknown"}' session={sessionId} corpse='{corpse.DisplayName}' entity={entityId} requestedSlot={slotIndex} aoid={rolled.Aoid} {inventoryDebug}");
                return RuntimeInteractionResult.Loot(entityId, $"Inventory is full ({inventoryDebug}).", corpse.DisplayName, 0, BuildLootSnapshots(corpse));
            }

            corpse.Items.RemoveAt(index);
            if (corpse.CreditsLooted && corpse.Items.Count == 0)
                MarkCorpseEmptied(corpse, now);

            string itemName = string.IsNullOrWhiteSpace(rolled.Name) ? item.Definition?.Name ?? rolled.Aoid.ToString() : rolled.Name;
            Console.WriteLine(
                $"[AO.Server] Loot taken utc={DateTime.UtcNow:O} player='{player.Profile?.Name ?? "Unknown"}' session={sessionId} corpse='{corpse.DisplayName}' entity={entityId} slot={index} aoid={rolled.Aoid} item='{itemName}' remaining={corpse.Items.Count}");
            return RuntimeInteractionResult.Loot(
                entityId,
                $"You looted {itemName}.",
                corpse.DisplayName,
                0,
                BuildLootSnapshots(corpse),
                rolled.Aoid);
        }

        private CoreItems.ItemInstance CreateLootItemInstance(int aoid)
        {
            var dataInstance = ResolveAuthoritativeItemInstance(aoid);
            if (dataInstance?.Definition == null)
                return null;

            long uniqueInstanceId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L
                + Math.Max(1, aoid % 1000)
                + Random.Shared.Next(0, 1000);
            return CreateCoreItemInstance(dataInstance, uniqueInstanceId);
        }

        private CoreItems.ItemInstance CreateCoreItemInstance(AO.Data.Core.ItemInstance dataInstance, long instanceId)
        {
            if (dataInstance?.Definition == null)
                return null;

            var coreDefinition = BuildCoreItemDefinition(dataInstance.Definition);
            return new CoreItems.ItemInstance(
                coreDefinition,
                quantity: Math.Max(1, dataInstance.Quantity),
                instanceId: instanceId,
                containerCapacity: ResolveContainerCapacity(dataInstance.Definition));
        }

        private static int ResolveContainerCapacity(AO.Data.Core.ItemDefinition definition)
        {
            return IsBackpackItem(definition?.Name) ? 21 : 0;
        }

        private static bool IsBackpackItem(string itemName)
        {
            return !string.IsNullOrWhiteSpace(itemName)
                && itemName.Contains("backpack", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryAddLootToPlayerInventory(ConnectedPlayer player, CoreItems.ItemInstance item, out string debug)
        {
            debug = string.Empty;
            var inventory = player?.Profile?.Inventory;
            if (inventory == null || item == null)
            {
                debug = "inventory=null";
                return false;
            }

            int mainUsed = inventory.Main?.Slots?.Count(slot => slot != null) ?? 0;
            int mainCapacity = inventory.Main?.Capacity ?? 0;
            if (inventory.TryAddToMain(item))
            {
                debug = $"placed=main main={mainUsed}/{mainCapacity}";
                return true;
            }

            var containers = inventory.Items
                .Where(i => i != null && i.IsContainer && i.InstanceId != 0)
                .ToList();

            for (int i = 0; i < containers.Count; i++)
            {
                var containerItem = containers[i];
                if (inventory.TryAddToContainer(containerItem.InstanceId, item))
                {
                    debug = $"placed=container containerInstance={containerItem.InstanceId} main={mainUsed}/{mainCapacity}";
                    return true;
                }
            }

            debug = $"main={mainUsed}/{mainCapacity} containers={containers.Count}";
            return false;
        }

        private bool TryOpenCorpseLoot(ConnectedPlayer player, string entityId, out RuntimeInteractionResult result)
        {
            result = RuntimeInteractionResult.Fail(entityId, "No loot is available.");
            if (player == null || string.IsNullOrWhiteSpace(entityId))
                return false;

            if (!_corpseLootByEntityId.TryGetValue(entityId, out CorpseLootState corpse) || corpse == null)
                return false;

            DateTime now = DateTime.UtcNow;
            if (IsCorpseExpired(corpse, now))
            {
                HideCorpse(entityId, now);
                return false;
            }

            if (corpse.PlayfieldId != player.CurrentPlayfieldId)
            {
                result = RuntimeInteractionResult.Fail(entityId, "That corpse is in another playfield.", "loot", entityId, corpse.DisplayName);
                return true;
            }

            if (corpse.IsLooted)
            {
                result = RuntimeInteractionResult.Fail(entityId, $"{corpse.DisplayName} has already been looted.", "loot", entityId, corpse.DisplayName);
                return true;
            }

            int grantedCredits = 0;
            if (!corpse.CreditsLooted)
            {
                grantedCredits = GrantCredits(player, corpse.Credits);
                corpse.CreditsLooted = true;
            }

            if (corpse.CreditsLooted && corpse.Items.Count == 0)
                MarkCorpseEmptied(corpse, now);

            string message = grantedCredits > 0
                ? $"You receive {grantedCredits} credits from the corpse."
                : $"Opened {corpse.DisplayName}.";

            result = RuntimeInteractionResult.Loot(
                entityId,
                message,
                corpse.DisplayName,
                grantedCredits,
                BuildLootSnapshots(corpse));
            return true;
        }

        private static List<LootItemSnapshot> BuildLootSnapshots(CorpseLootState corpse)
        {
            var result = new List<LootItemSnapshot>();
            if (corpse?.Items == null)
                return result;

            for (int i = 0; i < corpse.Items.Count; i++)
            {
                RolledLootItem item = corpse.Items[i];
                if (item.Aoid <= 0)
                    continue;

                result.Add(new LootItemSnapshot
                {
                    Aoid = item.Aoid,
                    Name = item.Name ?? string.Empty,
                    Quantity = 1,
                    Ql = item.Ql
                });
            }

            return result;
        }

        private void SweepCorpseLifecycle(DateTime now)
        {
            if (_corpseLootByEntityId.Count > 0)
            {
                var expiredCorpses = new List<string>();
                foreach (var pair in _corpseLootByEntityId)
                {
                    CorpseLootState corpse = pair.Value;
                    if (corpse == null)
                    {
                        expiredCorpses.Add(pair.Key);
                        continue;
                    }

                    bool shouldHide = corpse.IsLooted
                        ? corpse.AutoDespawnUtc.HasValue && corpse.AutoDespawnUtc.Value <= now
                        : corpse.ExpiresUtc <= now;
                    if (shouldHide)
                        expiredCorpses.Add(pair.Key);
                }

                for (int i = 0; i < expiredCorpses.Count; i++)
                    HideCorpse(expiredCorpses[i], now);
            }

            ApplyDueRuntimeRespawns(now);
        }

        private void ApplyDueRuntimeRespawns(DateTime now)
        {
            if (_runtimeRespawnsByEntityId.Count == 0)
                return;

            if (now >= _nextRuntimeRespawnDebugUtc)
            {
                _nextRuntimeRespawnDebugUtc = now.AddSeconds(5.0);
                foreach (var pair in _runtimeRespawnsByEntityId)
                {
                    RuntimeRespawnState pending = pair.Value;
                    if (pending == null)
                        continue;

                    double remaining = Math.Max(0.0, (pending.RespawnUtc - now).TotalSeconds);
                    Console.WriteLine(
                        $"[AO.Server] Runtime mob respawn pending entityId={pending.EntityId} playfield={pending.PlayfieldId} remaining={remaining:0.0}s respawnUtc={pending.RespawnUtc:O} now={now:O}.");
                }
            }

            var respawned = new List<RuntimeRespawnState>();
            foreach (var pair in _runtimeRespawnsByEntityId)
            {
                RuntimeRespawnState state = pair.Value;
                if (state == null)
                    continue;
                if (state.RespawnUtc <= DateTime.MinValue || state.RespawnUtc > now)
                    continue;
                respawned.Add(state);
            }

            for (int i = 0; i < respawned.Count; i++)
            {
                RuntimeRespawnState state = respawned[i];
                _mobCombatByEntityId.Remove(state.EntityId);
                _runtimeRespawnsByEntityId.Remove(state.EntityId);
                _respawnedRuntimeEntityIds.Add(state.EntityId);
                _recentRuntimeRespawnUpsertsByEntityId[state.EntityId] = now.AddSeconds(8.0);
                bool exists = _playfieldRuntime.TryGetEntity(state.PlayfieldId, state.EntityId, out RuntimeEntity entity) && entity != null;
                Console.WriteLine($"[AO.Server] Runtime mob respawned entityId={state.EntityId} playfield={state.PlayfieldId} sourceExists={exists}.");
            }
        }

        private void HideCorpse(string entityId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return;

            _corpseLootByEntityId.Remove(entityId);
            if (!_mobCombatByEntityId.TryGetValue(entityId, out MobCombatState state) || state == null)
                return;

            state.IsHidden = true;
            state.IsInCombat = false;
            state.CombatState = "dead";
            state.TargetSessionId = Guid.Empty;
            state.HiddenUntilUtc = now.AddSeconds(1.0);
            if (state.RespawnUtc <= DateTime.MinValue)
            {
                state.RespawnUtc = now.AddSeconds(1.0);
                _runtimeRespawnsByEntityId[entityId] = new RuntimeRespawnState(
                    entityId,
                    state.PlayfieldId,
                    state.RespawnUtc);
            }
        }

        private static void MarkCorpseEmptied(CorpseLootState corpse, DateTime now)
        {
            if (corpse == null || corpse.IsLooted)
                return;

            corpse.IsLooted = true;
            int delaySeconds = Math.Max(0, corpse.EmptyCorpseSeconds);
            corpse.AutoDespawnUtc = now.AddSeconds(delaySeconds);
        }

        private static bool IsCorpseExpired(CorpseLootState corpse, DateTime now)
        {
            if (corpse == null)
                return true;

            if (corpse.IsLooted)
                return corpse.AutoDespawnUtc.HasValue && corpse.AutoDespawnUtc.Value <= now;

            return corpse.ExpiresUtc <= now;
        }

        private static int GetRemainingSeconds(DateTime expiresUtc, DateTime now)
        {
            if (expiresUtc <= now)
                return 0;

            return Math.Max(0, (int)Math.Ceiling((expiresUtc - now).TotalSeconds));
        }

        private static string AppendTemporaryDescription(string description, int remainingSeconds)
        {
            string temporary = $"Temporary: {FormatRemainingTime(remainingSeconds)}";
            if (string.IsNullOrWhiteSpace(description))
                return temporary;

            string trimmed = description.Trim();
            if (trimmed.StartsWith("Temporary:", StringComparison.OrdinalIgnoreCase))
                return temporary;

            return $"{temporary}\n{trimmed}";
        }

        private static string FormatRemainingTime(int totalSeconds)
        {
            int seconds = Math.Max(0, totalSeconds);
            int minutes = seconds / 60;
            int remainder = seconds % 60;
            return $"{minutes}:{remainder:00}";
        }

        private static long ToUnixMilliseconds(DateTime utc)
        {
            DateTime normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return new DateTimeOffset(normalized).ToUnixTimeMilliseconds();
        }

        private static int GrantCredits(ConnectedPlayer player, int credits)
        {
            if (player?.Profile?.StatsContainer == null || credits <= 0)
                return 0;

            int current = player.Profile.StatsContainer.GetBaseStat(CreditsStatId);
            player.Profile.StatsContainer.SetBaseStat(CreditsStatId, current + credits);
            return credits;
        }

        private static int RollPlayerDamage(ConnectedPlayer player)
        {
            int level = Math.Max(1, player?.Profile?.Level?.Level ?? 1);
            int baseDamage = Math.Max(2, level * 2);
            return baseDamage + Random.Shared.Next(1, 7);
        }

        private static double ResolvePlayerAttackWindupSeconds(ConnectedPlayer player)
        {
            return DefaultPlayerAttackWindupSeconds;
        }

        private static double ResolvePlayerAttackRechargeSeconds(ConnectedPlayer player)
        {
            // Until the server has full weapon timing extraction wired to equipped item stats,
            // keep the authoritative cadence sane and prevent client message spam from becoming DPS.
            return DefaultPlayerAttackRechargeSeconds;
        }

        private static void ClearPendingRuntimeAttack(ConnectedPlayer player)
        {
            if (player == null)
                return;

            player.PendingRuntimeAttackEntityId = string.Empty;
            player.PendingRuntimeAttackImpactUtc = DateTime.MinValue;
            player.PendingRuntimeAttackRechargeSeconds = 0.0;
        }

        private MobCombatState GetOrCreateMobCombatState(RuntimeEntity entity)
        {
            if (_mobCombatByEntityId.TryGetValue(entity.EntityId, out MobCombatState existing) && existing != null)
                return existing;

            int level = Math.Max(1, entity.Level);
            int damageMin = entity.DamageMin > 0 ? entity.DamageMin : Math.Max(1, level);
            int damageMax = entity.DamageMax > 0 ? entity.DamageMax : Math.Max(damageMin, level * 2);
            var created = new MobCombatState
            {
                EntityId = entity.EntityId,
                PlayfieldId = entity.PlayfieldId,
                X = entity.X,
                Y = entity.Y,
                Z = entity.Z,
                SpawnY = entity.Y,
                YawDegrees = entity.YawDegrees,
                Health = Math.Max(1, entity.Health > 0 ? entity.Health : entity.MaxHealth),
                MaxHealth = Math.Max(1, entity.MaxHealth),
                AttackRange = entity.AttackRange > 0f ? entity.AttackRange : 2.25f,
                AttackRechargeSeconds = entity.AttackRechargeSeconds > 0f ? entity.AttackRechargeSeconds : 2.0f,
                DamageMin = damageMin,
                DamageMax = damageMax,
                WalkSpeed = 4.0f,
                CombatState = "idle"
            };
            _mobCombatByEntityId[entity.EntityId] = created;
            return created;
        }

        private static bool IsCombatRuntimeEntity(RuntimeEntity entity)
        {
            if (entity == null)
                return false;

            string haystack = string.Join(" ", entity.ObjectType ?? string.Empty, entity.DisplayName ?? string.Empty, entity.Description ?? string.Empty);
            return haystack.IndexOf("monster", StringComparison.OrdinalIgnoreCase) >= 0
                   || haystack.IndexOf("mob", StringComparison.OrdinalIgnoreCase) >= 0
                   || entity.MaxHealth > 1;
        }

        private static int RollMobDamage(MobCombatState state)
        {
            int min = Math.Max(0, state?.DamageMin ?? 0);
            int max = Math.Max(min, state?.DamageMax ?? min);
            if (max <= min)
                return min;

            return Random.Shared.Next(min, max + 1);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }

    public sealed class ConnectedPlayer
    {
        public Guid SessionId { get; }
        public AO.Core.Characters.Character Profile { get; }
        public AO.Core.Entities.Character WorldCharacter { get; }
        public int CurrentPlayfieldId { get; set; }
        public PlayfieldInstanceKey CurrentInstanceKey { get; set; }
        public bool AllowDebugCharacterEditing { get; }
        public int CurrentHealth { get; set; }
        public string TeamKey { get; set; } = string.Empty;
        public int CharacterSexCode { get; set; }
        public PendingZoneTransitionState? PendingZoneTransition { get; set; }
        public ReturnContextTicket? ReturnContextTicket { get; set; }
        public DateTime AdminTeleportLockUntilUtc { get; set; } = DateTime.MinValue;
        public DateTime NextRuntimeAttackUtc { get; set; } = DateTime.MinValue;
        public string PendingRuntimeAttackEntityId { get; set; } = string.Empty;
        public DateTime PendingRuntimeAttackImpactUtc { get; set; } = DateTime.MinValue;
        public double PendingRuntimeAttackRechargeSeconds { get; set; }

        public ConnectedPlayer(
            Guid sessionId,
            AO.Core.Characters.Character profile,
            AO.Core.Entities.Character worldCharacter,
            int currentPlayfieldId,
            bool allowDebugCharacterEditing)
        {
            SessionId = sessionId;
            Profile = profile;
            WorldCharacter = worldCharacter;
            CurrentPlayfieldId = currentPlayfieldId;
            CurrentInstanceKey = new PlayfieldInstanceKey(currentPlayfieldId, "default", 1);
            AllowDebugCharacterEditing = allowDebugCharacterEditing;
            CharacterSexCode = profile.BreedId == 4 ? 2 : 0;
        }
    }

    public readonly record struct ZoneTransitionDecision(
        string ZoneLinkId,
        int PlayfieldId,
        float SpawnAoX,
        float SpawnAoY,
        float SpawnAoZ,
        float TargetYaw,
        int TemplatePlayfieldId,
        string RouteKey,
        int ShardId,
        bool UsedReturnContext);

    public readonly record struct ServerActionResult(bool Success, string Message)
    {
        public static ServerActionResult Ok(string message) => new(true, message);

        public static ServerActionResult Fail(string message) => new(false, message);
    }

    public sealed class MobCombatState
    {
        public string EntityId { get; set; } = string.Empty;
        public int PlayfieldId { get; set; }
        public Guid TargetSessionId { get; set; }
        public bool IsInCombat { get; set; }
        public string CombatState { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float SpawnY { get; set; }
        public float YawDegrees { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public float AttackRange { get; set; }
        public float AttackRechargeSeconds { get; set; }
        public float WalkSpeed { get; set; }
        public int DamageMin { get; set; }
        public int DamageMax { get; set; }
        public int LastDamage { get; set; }
        public int LastAttackTick { get; set; }
        public DateTime NextAttackUtc { get; set; } = DateTime.MinValue;
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;
        public DateTime DeathUtc { get; set; } = DateTime.MinValue;
        public DateTime RespawnUtc { get; set; } = DateTime.MinValue;
        public DateTime HiddenUntilUtc { get; set; } = DateTime.MinValue;
        public bool IsHidden { get; set; }
        public bool HasTargetPosition { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float TargetZ { get; set; }
    }

    public sealed class CorpseLootState
    {
        public string EntityId { get; set; } = string.Empty;
        public string SourceEntityId { get; set; } = string.Empty;
        public int PlayfieldId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public Guid KillerSessionId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime? AutoDespawnUtc { get; set; }
        public int EmptyCorpseSeconds { get; set; } = 3;
        public bool IsLooted { get; set; }
        public bool CreditsLooted { get; set; }
        public int Credits { get; set; }
        public List<RolledLootItem> Items { get; set; } = new();
        public string ObjectType { get; set; } = "mob";
        public int? TemplateId { get; set; }
        public int? MeshId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float YawDegrees { get; set; }
        public string ImportKey { get; set; } = string.Empty;
        public string MeshName { get; set; } = string.Empty;
        public int MaxHealth { get; set; } = 1;
        public int MaxNano { get; set; }
        public int Level { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public sealed class RuntimeRespawnState
    {
        public RuntimeRespawnState(string entityId, int playfieldId, DateTime respawnUtc)
        {
            EntityId = entityId ?? string.Empty;
            PlayfieldId = playfieldId;
            RespawnUtc = respawnUtc;
        }

        public string EntityId { get; }
        public int PlayfieldId { get; }
        public DateTime RespawnUtc { get; }
    }

    public readonly record struct RuntimeInteractionResult(
        bool Success,
        string EntityId,
        string Message,
        ZoneTransitionDecision? ZoneDecision,
        string InteractionType,
        string InteractionId,
        string DisplayName,
        int UseCount,
        float CooldownRemainingSeconds,
        bool IsEnabled,
        int LootCredits,
        int LootTakenAoid,
        IReadOnlyList<LootItemSnapshot> LootItems)
    {
        public static RuntimeInteractionResult Fail(
            string entityId,
            string message,
            string interactionType = "",
            string interactionId = "",
            string displayName = "",
            int useCount = 0,
            float cooldownRemainingSeconds = 0f,
            bool isEnabled = false)
        {
            return new RuntimeInteractionResult(
                false,
                entityId ?? string.Empty,
                message ?? string.Empty,
                null,
                interactionType ?? string.Empty,
                interactionId ?? string.Empty,
                displayName ?? string.Empty,
                useCount,
                MathF.Max(0f, cooldownRemainingSeconds),
                isEnabled,
                0,
                0,
                Array.Empty<LootItemSnapshot>());
        }

        public static RuntimeInteractionResult Notice(
            string entityId,
            string message,
            string interactionType = "",
            string interactionId = "",
            string displayName = "",
            int useCount = 0,
            float cooldownRemainingSeconds = 0f,
            bool isEnabled = true)
        {
            return new RuntimeInteractionResult(
                true,
                entityId ?? string.Empty,
                message ?? string.Empty,
                null,
                interactionType ?? string.Empty,
                interactionId ?? string.Empty,
                displayName ?? string.Empty,
                useCount,
                MathF.Max(0f, cooldownRemainingSeconds),
                isEnabled,
                0,
                0,
                Array.Empty<LootItemSnapshot>());
        }

        public static RuntimeInteractionResult Zone(
            string entityId,
            string message,
            ZoneTransitionDecision decision,
            string interactionType = "",
            string interactionId = "",
            string displayName = "",
            int useCount = 0,
            float cooldownRemainingSeconds = 0f,
            bool isEnabled = true)
        {
            return new RuntimeInteractionResult(
                true,
                entityId ?? string.Empty,
                message ?? string.Empty,
                decision,
                interactionType ?? string.Empty,
                interactionId ?? string.Empty,
                displayName ?? string.Empty,
                useCount,
                MathF.Max(0f, cooldownRemainingSeconds),
                isEnabled,
                0,
                0,
                Array.Empty<LootItemSnapshot>());
        }

        public static RuntimeInteractionResult Loot(
            string entityId,
            string message,
            string displayName,
            int credits,
            IReadOnlyList<LootItemSnapshot> items,
            int takenAoid = 0)
        {
            return new RuntimeInteractionResult(
                true,
                entityId ?? string.Empty,
                message ?? string.Empty,
                null,
                "loot",
                entityId ?? string.Empty,
                displayName ?? string.Empty,
                1,
                0f,
                false,
                Math.Max(0, credits),
                Math.Max(0, takenAoid),
                items ?? Array.Empty<LootItemSnapshot>());
        }
    }

    public sealed class ZoneLinksFile
    {
        public List<ZoneLinkDefinition> Links { get; set; } = new();
    }

    public sealed class ZoneLinkDefinition
    {
        public string Id { get; set; } = string.Empty;
        public int FromPlayfieldId { get; set; }
        public int ToPlayfieldId { get; set; }
        public string RouteKey { get; set; } = string.Empty;
        public bool UseReturnContext { get; set; }
        public bool TrackReturnContext { get; set; } = true;
        public int DestinationTemplatePlayfieldId { get; set; }
        public string DestinationRouteKey { get; set; } = string.Empty;
        public int? MaxPlayersPerShard { get; set; }
        public int FallbackToPlayfieldId { get; set; }
        public int FallbackDestinationTemplatePlayfieldId { get; set; }
        public string FallbackDestinationRouteKey { get; set; } = string.Empty;
        public int SourceStatelId { get; set; }
        public string SourceMeshName { get; set; } = string.Empty;
        public float TriggerRadius { get; set; } = 2.5f;
        public ServerVec3? SourceAOPosition { get; set; }
        public float SourceAORadius { get; set; } = 25f;
        public ServerVec3? TargetAOSpawn { get; set; }
        public float? TargetYaw { get; set; }
        public ServerVec3? FallbackTargetAOSpawn { get; set; }
        public float? FallbackTargetYaw { get; set; }
    }

    public sealed class ServerVec3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public readonly record struct PlayfieldInstanceKey(int TemplatePlayfieldId, string RouteKey, int ShardId);

    public readonly record struct ReturnContextTicket(
        int OriginPlayfieldId,
        PlayfieldInstanceKey OriginInstance,
        string OriginZoneLinkId,
        ServerVec3? OriginAOSpawn,
        DateTime EnteredUtc);

    public sealed class PendingZoneTransitionState
    {
        public ZoneLinkDefinition Link { get; }
        public PlayfieldInstanceKey DestinationInstance { get; }
        public ReturnContextTicket? PendingReturnContext { get; }

        public PendingZoneTransitionState(ZoneLinkDefinition link, PlayfieldInstanceKey destinationInstance, ReturnContextTicket? pendingReturnContext)
        {
            Link = link;
            DestinationInstance = destinationInstance;
            PendingReturnContext = pendingReturnContext;
        }
    }

    internal readonly record struct ZoneResolution(
        bool Success,
        int PlayfieldId,
        float SpawnAoX,
        float SpawnAoY,
        float SpawnAoZ,
        float TargetYaw,
        int TemplatePlayfieldId,
        string RouteKey,
        bool ReturnContextUsed,
        PlayfieldInstanceKey ReturnInstanceKey)
    {
        public static ZoneResolution Fail() => new(false, 0, 0f, 0f, 0f, 0f, 0, string.Empty, false, default);

        public static ZoneResolution SuccessResult(
            int playfieldId,
            float spawnAoX,
            float spawnAoY,
            float spawnAoZ,
            float targetYaw,
            int templatePlayfieldId,
            string routeKey,
            bool returnContextUsed,
            PlayfieldInstanceKey returnInstanceKey)
        {
            return new ZoneResolution(
                true,
                playfieldId,
                spawnAoX,
                spawnAoY,
                spawnAoZ,
                targetYaw,
                templatePlayfieldId,
                routeKey,
                returnContextUsed,
                returnInstanceKey);
        }
    }

    internal sealed class TeleportDefaultsFile
    {
        public List<TeleportDefaultEntry> Defaults { get; set; } = new();
    }

    internal sealed class TeleportDefaultEntry
    {
        public int PlayfieldId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float AoX { get; set; }
        public float AoY { get; set; }
        public float AoZ { get; set; }
        public float? Heading { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}











