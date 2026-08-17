using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace AO.Unity.World
{
    public sealed class AuthoritativeNetworkClient : MonoBehaviour
    {
        private sealed class RuntimePrimitivePlaceholderTag : MonoBehaviour { }

        private static AuthoritativeNetworkClient _activeInstance;
        public readonly record struct CharacterSettingsApproval(int Level, int BreedId, int ProfessionId, int Sex, long Experience, string Message);
        public readonly record struct StatIncreaseApproval(string StatName, int Amount, int AvailableIp, string Message);
        public readonly record struct EquipApproval(int SlotId, long InstanceId, string Message);
        public readonly record struct UnequipApproval(int SlotId, long InstanceId, string Message);
        public readonly record struct AdminItemGrantedApproval(long InstanceId, string Message);
        public readonly record struct QuestDialogOption(string OptionId, string Label, string QuestId, string NextNodeId, bool IsExit);
        public readonly record struct QuestJournalEntry(string QuestId, string Name, string Description, string CurrentNodeId, string State, long UpdatedUnixMs);
        public readonly record struct QuestRewardGrant(int Type, int TargetId, int Amount);
        public readonly record struct QuestDialogPayload(int NpcId, string Title, string Text, bool CanClose, IReadOnlyList<QuestDialogOption> Options, IReadOnlyList<QuestJournalEntry> Journal, IReadOnlyList<QuestRewardGrant> RewardGrants, string Message);
        public readonly record struct QuestTradeRequirement(int ItemAoid, string ItemName, int RequiredCount, int AvailableCount);
        public readonly record struct QuestTradeOffer(int ItemAoid, int Count);
        public readonly record struct QuestTradePayload(string TradeSessionId, string Title, string Prompt, IReadOnlyList<QuestTradeRequirement> Requirements, IReadOnlyList<QuestTradeOffer> Autofill, string Message);
        public readonly record struct LootItem(int Aoid, string Name, int Quantity, int? Ql);
        public readonly record struct LootPayload(string EntityId, int Credits, int TakenAoid, IReadOnlyList<LootItem> Items, string Message);

        [Serializable]
        private sealed class ClientMessage
        {
            public string Type;
            public string Name;
            public int BreedId = 1;
            public int ProfessionId = 1;
            public int Level = 1;
            public int Sex;
            public long Experience;
            public int PlayfieldId;
            public float X;
            public float Y;
            public float Z;
            public string StatName;
            public int Amount;
            public int SlotId;
            public long InstanceId;
            public string ZoneLinkId;
            public string EntityId;
            public string QuestOptionId;
            public int QuestNpcId;
            public float TargetYaw;
            public float SpawnAoX;
            public float SpawnAoY;
            public float SpawnAoZ;
            public bool UseTeleportDefaultSpawn;
            public int BootstrapVersion;
            public string QuestTradeSessionId;
            public QuestTradeOfferSnapshot[] QuestTradeOffers;
        }

        [Serializable]
        private sealed class ServerMessage
        {
            public string Type;
            public int Tick;
            public string SessionId;
            public string Message;
            public string StatName;
            public int Amount;
            public int AvailableIp;
            public int SlotId;
            public long InstanceId;
            public int BreedId;
            public int ProfessionId;
            public int Level;
            public int Sex;
            public long Experience;
            public int PlayfieldId;
            public string ZoneLinkId;
            public string EntityId;
            public string InteractionType;
            public string InteractionId;
            public int QuestNpcId;
            public string DialogTitle;
            public string DialogText;
            public bool DialogCanClose = true;
            public QuestDialogOptionSnapshot[] QuestDialogOptions;
            public QuestJournalEntrySnapshot[] QuestJournal;
            public QuestRewardGrantSnapshot[] QuestRewardGrants;
            public bool IsAdminAction;
            public float TargetYaw;
            public float SpawnAoX;
            public float SpawnAoY;
            public float SpawnAoZ;
            public bool UseTeleportDefaultSpawn;
            public int BootstrapVersion;
            public PlayerSnapshot[] Players;
            public RuntimeEntitySnapshot[] RuntimeEntities;
            public RuntimeEntityDelta[] RuntimeEntityDeltas;
            public LootItemSnapshot[] LootItems;
            public int LootCredits;
            public int LootTakenAoid;
            public string QuestTradeSessionId;
            public string QuestTradeTitle;
            public string QuestTradePrompt;
            public QuestTradeRequirementSnapshot[] QuestTradeRequirements;
            public QuestTradeOfferSnapshot[] QuestTradeAutofill;
        }

        [Serializable]
        private sealed class PlayerSnapshot
        {
            public string SessionId;
            public string Name;
            public int PlayfieldId;
            public float X;
            public float Y;
            public float Z;
            public int Health;
            public int MaxHealth;
            public int Nano;
            public int MaxNano;
            public long Experience;
        }

        [Serializable]
        private sealed class RuntimeEntitySnapshot
        {
            public string EntityId;
            public string ObjectType;
            public string DisplayName;
            public int PlayfieldId;
            public int IdentityInstance;
            public int? TemplateId;
            public int? MeshId;
            public float X;
            public float Y;
            public float Z;
            public float YawDegrees;
            public string ImportKey;
            public string MeshName;
            public string InteractionType;
            public string InteractionId;
            public float InteractionRadius;
            public string InteractionLabel;
            public bool InteractionEnabled;
            public int InteractionUseCount;
            public float InteractionCooldownRemainingSeconds;
            public string LastInteractionUtc;
            public int MaxHealth;
            public int Health;
            public int MaxNano;
            public int Level;
            public string CombatState;
            public string CombatTargetSessionId;
            public float AttackRange;
            public float AttackRechargeSeconds;
            public int DamageMin;
            public int DamageMax;
            public int LastDamage;
            public int LastAttackTick;
            public int TemporaryRemainingSeconds;
            public long TemporaryExpiresUnixMs;
            public string Description;
        }


        [Serializable]
        private sealed class RuntimeEntityDelta
        {
            public string Operation;
            public RuntimeEntitySnapshot Entity;
        }

        [Serializable]
        private sealed class LootItemSnapshot
        {
            public int Aoid;
            public string Name;
            public int Quantity;
            public int? Ql;
        }

        [Serializable]
        private sealed class QuestDialogOptionSnapshot
        {
            public string OptionId;
            public string Label;
            public string QuestId;
            public string NextNodeId;
            public bool IsExit;
        }

        [Serializable]
        private sealed class QuestJournalEntrySnapshot
        {
            public string QuestId;
            public string Name;
            public string Description;
            public string CurrentNodeId;
            public string State;
            public long UpdatedUnixMs;
        }

        [Serializable]
        private sealed class QuestTradeRequirementSnapshot
        {
            public int ItemAoid;
            public string ItemName;
            public int RequiredCount;
            public int AvailableCount;
        }

        [Serializable]
        private sealed class QuestTradeOfferSnapshot
        {
            public int ItemAoid;
            public int Count;
        }

        [Serializable]
        private sealed class QuestRewardGrantSnapshot
        {
            public int Type;
            public int TargetId;
            public int Amount;
        }
        [SerializeField] private string serverHost = "127.0.0.1";
        [SerializeField] private int serverPort = 4000;
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool authoritativeMovement = true;
        [SerializeField] private float sendIntervalSeconds = 0.05f;
        [SerializeField] private float snapDistance = 6f;
        [SerializeField] private float reconcileLerpSpeed = 16f;
        [SerializeField] private float minorDesyncIgnoreDistance = 3f;
        [SerializeField] private bool logTraffic = true;
        [SerializeField] private bool showDebugOverlay = true;
        [SerializeField] private bool collapseDebugOverlay = false;
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private float reconnectDelaySeconds = 2f;
        [SerializeField] private float repeatedServerErrorLogIntervalSeconds = 1.5f;
        [SerializeField] private int runtimeEntityApplyBatchSizePerFrame = 12;
        [SerializeField] private int maxIncomingMessagesPerFrame = 64;

        private readonly ConcurrentQueue<ServerMessage> _incoming = new();
        private CancellationTokenSource _cts;
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private PrototypeWalkerController _walker;
        private CharacterRuntimeBridge _bridge;
        private PrototypeWorldBootstrap _bootstrap;
        private string _sessionId = string.Empty;
        private float _nextSendTime;
        private Vector3 _lastSentIntent = Vector3.zero;
        private bool _hasSentMovementState;
        private Vector3 _authoritativePosition;
        private bool _hasAuthoritativePosition;
        private bool _hasAuthoritativeStatSnapshot;
        private int _authoritativeHealth;
        private int _authoritativeMaxHealth;
        private int _authoritativeNano;
        private int _authoritativeMaxNano;
        private int _authoritativeHealthOffset;
        private int _authoritativeNanoOffset;
        private long _authoritativeExperience;
        private string _connectionState = "Disconnected";
        private string _lastServerMessage = "None";
        private int _messagesSent;
        private int _messagesReceived;
        private int _lastReceivedTick;
        private bool _isConnecting;
        private float _nextReconnectTime;
        private Transform _runtimeEntitiesRoot;
        private readonly Dictionary<string, GameObject> _runtimeEntityViews = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _runtimeEntityDescriptions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _runtimeEntityTemporaryExpiresUnixMs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _runtimeEntityIdByIdentityInstance = new();
        private readonly Dictionary<string, string> _runtimeEntityIdByFallbackKey = new(StringComparer.OrdinalIgnoreCase);
        private int _runtimeEntitiesPlayfieldId = -1;
        private int _lastBootstrapVersion = -1;
        private int _expectedBootstrapPlayfieldId = -1;
        private float _nextRuntimeSnapshotReconcileAt;
        private float _suppressSnapshotPositionUntilTime;
        private int _lastReportedPlayfieldId = -1;
        private bool _pendingAdminTeleportFallback;
        private int _pendingAdminTeleportPlayfieldId = -1;
        private Vector3 _pendingAdminTeleportAoPosition = Vector3.zero;
        private bool _pendingAdminTeleportUseDefault;
        private float _pendingAdminTeleportYaw;
        private float _pendingAdminTeleportSentAt;
        private float _lastAdminTeleportRequestAt = -999f;
        private float _ignoreNonAdminZoneTransitionsUntilTime;
        private bool _pendingZoneLoadedAck;
        private int _pendingZoneLoadedAckPlayfieldId = -1;
        private bool _pendingZoneLoadedAckHasAo;
        private Vector3 _pendingZoneLoadedAckAo = Vector3.zero;
        private bool _pendingAdminPostSettleCheck;
        private int _pendingAdminPostSettlePlayfieldId = -1;
        private Vector3 _pendingAdminPostSettleAo = Vector3.zero;
        private float _pendingAdminPostSettleAtTime;
        private float _pendingAdminPostSettleExpireAtTime;
        private readonly Dictionary<string, float> _lastServerErrorLogTimeByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
        private Coroutine _runtimeEntityApplyCoroutine;
        private int _runtimeEntityApplyGeneration;
        private string _pendingInteractionEntityId = string.Empty;
        private float _pendingInteractionRequestedAt = -999f;
        private string _lastRuntimeAttackEntityId = string.Empty;
        private float _lastRuntimeAttackRequestAt = -999f;
        private float _nextTemporaryEntityExpiryScanAt;

        public event Action<CharacterSettingsApproval> CharacterSettingsApplied;
        public event Action<StatIncreaseApproval> StatIncreaseApplied;
        public event Action<EquipApproval> EquipApplied;
        public event Action<UnequipApproval> UnequipApplied;
        public event Action<AdminItemGrantedApproval> AdminItemGranted;
        public event Action<QuestDialogPayload> QuestDialogReceived;
        public event Action<QuestTradePayload> QuestTradeReceived;
        public event Action<LootPayload> LootReceived;
        public event Action<string> RuntimeEntityRemoved;
        public event Action<string> ServerNoticeReceived;

        public bool IsServerAvailable => _writer != null && _client != null && _client.Connected;
        public bool IsConnected => IsServerAvailable && !string.IsNullOrWhiteSpace(_sessionId);
        public static AuthoritativeNetworkClient ActiveInstance => _activeInstance;
        public bool IsStrictAuthoritativeServerRequired => _bootstrap != null && _bootstrap.IsStrictAuthoritativeServerRequired;
        public bool IsAdminTeleportInProgress =>
            _pendingAdminTeleportPlayfieldId > 0 &&
            (
                _pendingZoneLoadedAck ||
                (Time.unscaledTime - _pendingAdminTeleportSentAt) <= 8.0f
            );

        public void RemoveRuntimeEntityFromAuthoritativeView(string entityId)
        {
            RemoveRuntimeEntityVisual(entityId);
        }

        public bool TryGetRuntimeEntityDescription(string entityId, out string description)
        {
            description = string.Empty;
            if (string.IsNullOrWhiteSpace(entityId))
                return false;

            if (!_runtimeEntityDescriptions.TryGetValue(entityId, out string cached) || string.IsNullOrWhiteSpace(cached))
                return false;

            description = BuildRuntimeEntityDescription(entityId, cached);
            return true;
        }

        private string BuildRuntimeEntityDescription(string entityId, string cachedDescription)
        {
            string body = StripTemporaryLine(cachedDescription);
            if (!_runtimeEntityTemporaryExpiresUnixMs.TryGetValue(entityId, out long expiresUnixMs) || expiresUnixMs <= 0)
                return body.Trim();

            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int remainingSeconds = Mathf.Max(0, Mathf.CeilToInt((expiresUnixMs - nowUnixMs) / 1000f));
            string temporary = $"Temporary: {FormatRemainingTime(remainingSeconds)}";
            return string.IsNullOrWhiteSpace(body)
                ? temporary
                : $"{temporary}\n{body.Trim()}";
        }

        private static string StripTemporaryLine(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            string trimmed = description.Trim();
            if (!trimmed.StartsWith("Temporary:", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            int newline = trimmed.IndexOf('\n');
            if (newline < 0)
                return string.Empty;

            return trimmed.Substring(newline + 1).Trim();
        }

        private static string FormatRemainingTime(int totalSeconds)
        {
            int seconds = Mathf.Max(0, totalSeconds);
            int minutes = seconds / 60;
            int remainder = seconds % 60;
            return $"{minutes}:{remainder:00}";
        }

        public bool TryGetAuthoritativeStats(out int health, out int maxHealth, out int nano, out int maxNano, out long experience)
        {
            health = Mathf.Clamp(_authoritativeHealth + _authoritativeHealthOffset, 0, Mathf.Max(1, _authoritativeMaxHealth));
            maxHealth = _authoritativeMaxHealth;
            nano = Mathf.Clamp(_authoritativeNano + _authoritativeNanoOffset, 0, Mathf.Max(1, _authoritativeMaxNano));
            maxNano = _authoritativeMaxNano;
            experience = _authoritativeExperience;
            return _hasAuthoritativeStatSnapshot;
        }

        public void ApplyLocalAuthoritativeResourceDelta(int healthDelta, int nanoDelta)
        {
            if (healthDelta != 0)
                _authoritativeHealthOffset = Mathf.Clamp(_authoritativeHealthOffset + healthDelta, -Mathf.Max(1, _authoritativeMaxHealth), Mathf.Max(1, _authoritativeMaxHealth));
            if (nanoDelta != 0)
                _authoritativeNanoOffset = Mathf.Clamp(_authoritativeNanoOffset + nanoDelta, -Mathf.Max(1, _authoritativeMaxNano), Mathf.Max(1, _authoritativeMaxNano));
        }

        public void Configure(string host, int port, bool autoConnect, bool useAuthoritativeMovement)
        {
            serverHost = string.IsNullOrWhiteSpace(host) ? serverHost : host;
            serverPort = port > 0 ? port : serverPort;
            connectOnStart = autoConnect;
            authoritativeMovement = useAuthoritativeMovement;
        }

        public void RequestZoneTransition(string zoneLinkId, int currentPlayfieldId)
        {
            if (string.IsNullOrWhiteSpace(zoneLinkId))
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Zone request blocked: server unavailable ({zoneLinkId}).");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "zone_request",
                ZoneLinkId = zoneLinkId,
                PlayfieldId = currentPlayfieldId
            });
        }

        public void NotifyLocalZoneLoaded(int playfieldId, Vector3 worldPosition)
        {
            if (playfieldId <= 0 || !IsConnected)
                return;

            _ = SendAsync(new ClientMessage
            {
                Type = "zone_loaded",
                PlayfieldId = playfieldId,
                X = worldPosition.x,
                Y = worldPosition.y,
                Z = worldPosition.z
            });
        }

        public void RequestRuntimeInteraction(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Interaction blocked: server unavailable ({entityId}).");
                return;
            }

            _pendingInteractionEntityId = entityId.Trim();
            _pendingInteractionRequestedAt = Time.unscaledTime;

            _ = SendAsync(new ClientMessage
            {
                Type = "interact_runtime",
                EntityId = entityId
            });
        }

        public void RequestTakeLoot(string entityId, int itemAoid, int slotIndex)
        {
            if (string.IsNullOrWhiteSpace(entityId) || itemAoid <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Loot blocked: server unavailable ({entityId}).");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "loot_take",
                EntityId = entityId.Trim(),
                InstanceId = itemAoid,
                SlotId = Math.Max(0, slotIndex)
            });
        }

        public void RequestRuntimeAttack(string entityId, int damageAmount = 0)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Attack blocked: server unavailable ({entityId}).");
                return;
            }

            string trimmedEntityId = entityId.Trim();
            float now = Time.unscaledTime;
            if (damageAmount <= 0
                && string.Equals(_lastRuntimeAttackEntityId, trimmedEntityId, StringComparison.OrdinalIgnoreCase)
                && now < _lastRuntimeAttackRequestAt + 0.2f)
            {
                return;
            }

            _lastRuntimeAttackEntityId = trimmedEntityId;
            _lastRuntimeAttackRequestAt = now;

            Transform controlledTransform = ResolveControlledTransform();
            Vector3 targetAo = controlledTransform != null ? controlledTransform.position : transform.position;
            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap != null)
                targetAo = _bootstrap.ConvertWorldToAo(targetAo);

            _ = SendAsync(new ClientMessage
            {
                Type = "attack_runtime",
                EntityId = trimmedEntityId,
                Amount = Math.Max(0, damageAmount),
                X = targetAo.x,
                Y = targetAo.y,
                Z = targetAo.z
            });
        }

        public void RequestQuestDialogStart(int npcId)
        {
            if (npcId <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Quest dialog blocked: server unavailable (npcId={npcId}).");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "quest_dialog_start",
                QuestNpcId = npcId
            });
        }

        public void RequestQuestDialogChoice(int npcId, string optionId)
        {
            if (npcId <= 0 || string.IsNullOrWhiteSpace(optionId))
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable("Quest dialog choice blocked: server unavailable.");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "quest_dialog_choice",
                QuestNpcId = npcId,
                QuestOptionId = optionId.Trim()
            });
        }

        public void RequestQuestDialogClose(int npcId)
        {
            if (npcId <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable("Quest dialog close blocked: server unavailable.");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "quest_dialog_close",
                QuestNpcId = npcId
            });
        }

        public void RequestQuestTradeSubmit(string tradeSessionId, IReadOnlyList<QuestTradeOffer> offers)
        {
            if (string.IsNullOrWhiteSpace(tradeSessionId))
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable("Quest trade submit blocked: server unavailable.");
                return;
            }

            QuestTradeOfferSnapshot[] serializedOffers = offers == null
                ? Array.Empty<QuestTradeOfferSnapshot>()
                : offers
                    .Where(o => o.Count > 0 && o.ItemAoid > 0)
                    .Select(o => new QuestTradeOfferSnapshot { ItemAoid = o.ItemAoid, Count = o.Count })
                    .ToArray();

            _ = SendAsync(new ClientMessage
            {
                Type = "quest_trade_submit",
                QuestTradeSessionId = tradeSessionId.Trim(),
                QuestTradeOffers = serializedOffers
            });
        }

        public void RequestQuestTradeCancel(string tradeSessionId)
        {
            if (string.IsNullOrWhiteSpace(tradeSessionId))
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable("Quest trade cancel blocked: server unavailable.");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "quest_trade_cancel",
                QuestTradeSessionId = tradeSessionId.Trim()
            });
        }

        public void RequestAdminTeleport(int playfieldId, Vector3 targetAoPosition, float targetYaw = 0f, bool useDefaultSpawn = false)
        {
            if (playfieldId <= 0)
                return;
            if (!IsServerAvailable)
            {
                NotifyStrictServerUnavailable("Admin teleport blocked: server unavailable.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                    "Admin teleport delayed: connected to server transport, waiting for session handshake.");
                return;
            }

            _pendingAdminTeleportPlayfieldId = playfieldId;
            _pendingAdminTeleportSentAt = Time.unscaledTime;
            _lastAdminTeleportRequestAt = Time.unscaledTime;
            _hasAuthoritativePosition = false;
            _ignoreNonAdminZoneTransitionsUntilTime = Mathf.Max(
                _ignoreNonAdminZoneTransitionsUntilTime,
                Time.unscaledTime + 10.0f);
            var zoneManager = FindFirstObjectByType<ZoneTransitionManager>();
            if (zoneManager != null)
                zoneManager.SuppressPortalTransitions(12.0f, "admin teleport request in-flight");

            _ = SendAsync(new ClientMessage
            {
                Type = "admin_teleport",
                PlayfieldId = playfieldId,
                SpawnAoX = targetAoPosition.x,
                SpawnAoY = targetAoPosition.y,
                SpawnAoZ = targetAoPosition.z,
                UseTeleportDefaultSpawn = useDefaultSpawn,
                TargetYaw = targetYaw
            });

            // Server-authoritative mode owns teleport resolution and placement.
            // Disable local admin teleport fallback to avoid client-side conflicts.
            _pendingAdminTeleportFallback = false;
            _pendingAdminTeleportAoPosition = targetAoPosition;
            _pendingAdminTeleportUseDefault = useDefaultSpawn;
            _pendingAdminTeleportYaw = targetYaw;
        }

        public void RequestAdminGrantItem(long instanceId)
        {
            if (instanceId <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable("Admin item grant blocked: server unavailable.");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "admin_grant_item",
                InstanceId = instanceId
            });
        }

        public void RequestCharacterSettings(int level, int breedId, int professionId, int sex, long experience)
        {
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable("Character settings request blocked: server unavailable.");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "set_character",
                Level = Mathf.Max(1, level),
                BreedId = breedId <= 0 ? 1 : breedId,
                ProfessionId = professionId <= 0 ? 1 : professionId,
                Sex = sex,
                Experience = Math.Max(0L, experience)
            });
        }

        public void RequestEquipItem(int slotId, long instanceId)
        {
            if (slotId <= 0 || instanceId <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Equip blocked: server unavailable (slot {slotId}).");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "equip",
                SlotId = slotId,
                InstanceId = instanceId
            });
        }

        public void RequestUnequipSlot(int slotId)
        {
            if (slotId <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Unequip blocked: server unavailable (slot {slotId}).");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "unequip",
                SlotId = slotId
            });
        }

        public void RequestStatIncrease(string statName, int amount)
        {
            if (string.IsNullOrWhiteSpace(statName) || amount <= 0)
                return;
            if (!IsConnected)
            {
                NotifyStrictServerUnavailable($"Stat increase blocked: server unavailable ({statName}).");
                return;
            }

            _ = SendAsync(new ClientMessage
            {
                Type = "increase_stat",
                StatName = statName,
                Amount = amount
            });
        }

        private void Awake()
        {
            if (_activeInstance != null && _activeInstance != this)
            {
                Debug.LogWarning(
                    $"Duplicate AuthoritativeNetworkClient on '{gameObject.name}' detected. Disabling duplicate.");
                enabled = false;
                return;
            }

            _activeInstance = this;
            _walker = GetComponent<PrototypeWalkerController>();
            _bridge = GetComponent<CharacterRuntimeBridge>();
            _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            Debug.Log(
                $"AuthoritativeNetworkClient Awake on '{gameObject.name}' host={serverHost}:{serverPort} " +
                $"connectOnStart={connectOnStart} authoritativeMovement={authoritativeMovement}");
        }

        private void Start()
        {
            if (!connectOnStart)
            {
                _connectionState = "ConnectOnStart disabled";
                Debug.Log("AuthoritativeNetworkClient Start skipped because connectOnStart is disabled.");
                return;
            }

            Debug.Log($"AuthoritativeNetworkClient Start connecting to {serverHost}:{serverPort}");
            _ = ConnectAsync();
        }

        private void Update()
        {
            ProcessIncoming();
            SendMovementIfNeeded();
            ApplyAuthoritativeMovement();
            ReportLocalPlayfieldIfChanged();
            TrySendPendingZoneLoadedAck();
            TryApplyPendingAdminPostSettleCorrection();
            TryApplyPendingAdminTeleportFallback();
            RemoveExpiredTemporaryRuntimeEntities();
            TryReconnectIfNeeded();
        }

        private void OnGUI()
        {
            if (!showDebugOverlay)
                return;

            var expandedRect = new Rect(12f, 12f, 420f, 110f);
            var collapsedRect = new Rect(12f, 12f, 36f, 24f);
            if (collapseDebugOverlay)
            {
                if (GUI.Button(collapsedRect, "▸"))
                    collapseDebugOverlay = false;
                return;
            }

            GUI.Box(
                expandedRect,
                "AO Server\n" +
                $"State: {_connectionState}\n" +
                $"Session: {(string.IsNullOrWhiteSpace(_sessionId) ? "<none>" : _sessionId)}\n" +
                $"Sent: {_messagesSent}  Received: {_messagesReceived}  LastTick: {_lastReceivedTick}\n" +
                $"Last: {_lastServerMessage}");
            if (GUI.Button(new Rect(expandedRect.xMax - 26f, expandedRect.y + 4f, 20f, 18f), "▾"))
                collapseDebugOverlay = true;
        }

        private void OnDestroy()
        {
            if (_activeInstance == this)
                _activeInstance = null;
            Disconnect("Destroyed", keepFailureState: true);
        }

        private async Task ConnectAsync()
        {
            if (_client != null || _isConnecting)
                return;

            _isConnecting = true;
            _connectionState = $"Connecting to {serverHost}:{serverPort}";
            _cts = new CancellationTokenSource();
            _client = new TcpClient();
            try
            {
                await _client.ConnectAsync(serverHost, serverPort);
                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _ = Task.Run(ReceiveLoopAsync);

                _connectionState = $"Connected to {serverHost}:{serverPort}";
                if (logTraffic)
                    Debug.Log($"Connected to AO.Server at {serverHost}:{serverPort}");

                // Keep movement client-predicted while connected; server remains authoritative via reconciliation.
                if (_walker != null)
                    _walker.SetLocalMovementEnabled(true);

                await SendHelloAsync();
            }
            catch (Exception ex)
            {
                string message = $"Connect failed: {ex.Message}";
                _connectionState = message;
                Debug.LogWarning($"AO.Server connect failed: {ex.Message}");
                Disconnect(message, keepFailureState: true);
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private async Task SendHelloAsync()
        {
            if (_writer == null)
                return;

            var character = _bridge != null ? _bridge.Character : null;
            var message = new ClientMessage
            {
                Type = "hello",
                Name = character?.Name ?? gameObject.name,
                BreedId = character?.BreedId ?? 1,
                ProfessionId = character?.ProfessionId ?? 1,
                Level = character?.Level?.Level ?? 1,
                Sex = _bridge != null ? (int)_bridge.Sex : 0,
                PlayfieldId = _bootstrap != null ? _bootstrap.ActivePlayfieldId : 0,
                X = transform.position.x,
                Y = transform.position.y,
                Z = transform.position.z
            };

            await SendAsync(message);
        }

        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (_client != null && _client.Connected && _cts != null && !_cts.IsCancellationRequested)
                {
                    string line = await _reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                        break;

                    _messagesReceived++;
                    if (logTraffic)
                        Debug.Log($"AO.Server -> {line}");

                    var message = JsonConvert.DeserializeObject<ServerMessage>(line);
                    if (message != null)
                        _incoming.Enqueue(message);
                }

                if (_cts != null && !_cts.IsCancellationRequested)
                    Disconnect("Connection closed by server", keepFailureState: true);
            }
            catch (Exception ex)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    string message = $"Receive stopped: {ex.Message}";
                    _connectionState = message;
                    Debug.LogWarning($"AO.Server receive loop stopped: {ex.Message}");
                    Disconnect(message, keepFailureState: true);
                }
            }
        }

        private void ProcessIncoming()
        {
            int processed = 0;
            int maxMessages = Mathf.Max(1, maxIncomingMessagesPerFrame);
            while (processed < maxMessages && _incoming.TryDequeue(out var message))
            {
                processed++;
                if (message == null)
                    continue;

                _lastReceivedTick = message.Tick;
                _lastServerMessage = string.IsNullOrWhiteSpace(message.Message)
                    ? message.Type ?? "<unknown>"
                    : $"{message.Type}: {message.Message}";
                if (string.Equals(message.Type, "welcome", StringComparison.OrdinalIgnoreCase))
                {
                    _sessionId = message.SessionId ?? string.Empty;
                    _connectionState = $"Welcomed tick={message.Tick}";
                    Debug.Log(
                        $"AO.Server welcome session={_sessionId} tick={message.Tick} players={(message.Players?.Length ?? 0)}");

                    // Force an immediate authoritative bootstrap refresh now that session is established.
                    // This closes startup races where initial bootstrap arrived before local state was ready.
                    int pf = _bootstrap != null ? _bootstrap.ActivePlayfieldId : message.PlayfieldId;
                    Vector3 pos = transform.position;
                    _ = SendAsync(new ClientMessage
                    {
                        Type = "zone_loaded",
                        PlayfieldId = pf > 0 ? pf : message.PlayfieldId,
                        X = pos.x,
                        Y = pos.y,
                        Z = pos.z
                    });
                    _lastReportedPlayfieldId = pf > 0 ? pf : message.PlayfieldId;
                }
                else if (string.Equals(message.Type, "world_state", StringComparison.OrdinalIgnoreCase))
                {
                    TryRecoverSessionIdFromSnapshot(message);
                    ApplyRuntimeSnapshotFromWorldStateIfNeeded(message);
                    HandleRuntimeEntityDelta(message);
                    ReconcileRuntimeEntitiesFromWorldState(message);
                    _connectionState = $"Receiving world state tick={message.Tick}";
                }
                else if (string.Equals(message.Type, "zone_bootstrap", StringComparison.OrdinalIgnoreCase))
                {
                    HandleZoneBootstrap(message);
                    continue;
                }
                else if (string.Equals(message.Type, "runtime_entity_delta", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRuntimeEntityDelta(message);
                    continue;
                }
                else if (string.Equals(message.Type, "zone_transition", StringComparison.OrdinalIgnoreCase))
                {
                    HandleZoneTransition(message);
                    continue;
                }
                else if (string.Equals(message.Type, "character_settings_applied", StringComparison.OrdinalIgnoreCase))
                {
                    CharacterSettingsApplied?.Invoke(new CharacterSettingsApproval(
                        message.Level,
                        message.BreedId,
                        message.ProfessionId,
                        message.Sex,
                        message.Experience,
                        message.Message));
                    continue;
                }
                else if (string.Equals(message.Type, "equip_applied", StringComparison.OrdinalIgnoreCase))
                {
                    EquipApplied?.Invoke(new EquipApproval(
                        message.SlotId,
                        message.InstanceId,
                        message.Message));
                    continue;
                }
                else if (string.Equals(message.Type, "unequip_applied", StringComparison.OrdinalIgnoreCase))
                {
                    UnequipApplied?.Invoke(new UnequipApproval(
                        message.SlotId,
                        message.InstanceId,
                        message.Message));
                    continue;
                }
                else if (string.Equals(message.Type, "stat_increase_applied", StringComparison.OrdinalIgnoreCase))
                {
                    StatIncreaseApplied?.Invoke(new StatIncreaseApproval(
                        message.StatName,
                        message.Amount,
                        message.AvailableIp,
                        message.Message));
                    continue;
                }
                else if (string.Equals(message.Type, "interaction_result", StringComparison.OrdinalIgnoreCase))
                {
                    bool matchesPendingClick =
                        !string.IsNullOrWhiteSpace(_pendingInteractionEntityId) &&
                        !string.IsNullOrWhiteSpace(message.EntityId) &&
                        string.Equals(_pendingInteractionEntityId, message.EntityId, StringComparison.OrdinalIgnoreCase) &&
                        (Time.unscaledTime - _pendingInteractionRequestedAt) <= 2.5f;

                    if (string.Equals(message.InteractionType, "talk", StringComparison.OrdinalIgnoreCase))
                    {
                        int npcId = message.QuestNpcId;
                        if (npcId <= 0 && !string.IsNullOrWhiteSpace(message.InteractionId))
                            int.TryParse(message.InteractionId, out npcId);

                        if (npcId > 0)
                        {
                            if (logTraffic)
                            {
                                Debug.Log(
                                    $"AO.Server talk interaction_result entity={message.EntityId} npcId={npcId} " +
                                    $"pendingEntity={_pendingInteractionEntityId} matchesPending={matchesPendingClick}");
                            }
                            RequestQuestDialogStart(npcId);
                        }
                    }

                    if (matchesPendingClick)
                    {
                        _pendingInteractionEntityId = string.Empty;
                        _pendingInteractionRequestedAt = -999f;
                    }

                    if (!string.IsNullOrWhiteSpace(message.Message))
                        ServerNoticeReceived?.Invoke(message.Message);
                    continue;
                }
                else if (string.Equals(message.Type, "quest_dialog", StringComparison.OrdinalIgnoreCase))
                {
                    var options = message.QuestDialogOptions == null
                        ? new List<QuestDialogOption>()
                        : message.QuestDialogOptions
                            .Where(o => o != null)
                            .Select(o => new QuestDialogOption(
                                o.OptionId ?? string.Empty,
                                o.Label ?? string.Empty,
                                o.QuestId ?? string.Empty,
                                o.NextNodeId ?? string.Empty,
                                o.IsExit))
                            .ToList();

                    var journal = message.QuestJournal == null
                        ? new List<QuestJournalEntry>()
                        : message.QuestJournal
                            .Where(j => j != null)
                            .Select(j => new QuestJournalEntry(
                                j.QuestId ?? string.Empty,
                                j.Name ?? string.Empty,
                                j.Description ?? string.Empty,
                                j.CurrentNodeId ?? string.Empty,
                                j.State ?? string.Empty,
                                j.UpdatedUnixMs))
                            .ToList();

                    var rewardGrants = message.QuestRewardGrants == null
                        ? new List<QuestRewardGrant>()
                        : message.QuestRewardGrants
                            .Where(r => r != null && r.Amount > 0)
                            .Select(r => new QuestRewardGrant(r.Type, r.TargetId, r.Amount))
                            .ToList();

                    QuestDialogReceived?.Invoke(new QuestDialogPayload(
                        message.QuestNpcId,
                        message.DialogTitle ?? "Dialogue",
                        message.DialogText ?? string.Empty,
                        message.DialogCanClose,
                        options,
                        journal,
                        rewardGrants,
                        message.Message ?? string.Empty));
                    continue;
                }
                else if (string.Equals(message.Type, "quest_trade", StringComparison.OrdinalIgnoreCase))
                {
                    var requirements = message.QuestTradeRequirements == null
                        ? new List<QuestTradeRequirement>()
                        : message.QuestTradeRequirements
                            .Where(r => r != null)
                            .Select(r => new QuestTradeRequirement(
                                r.ItemAoid,
                                r.ItemName ?? string.Empty,
                                r.RequiredCount,
                                r.AvailableCount))
                            .ToList();

                    var autofill = message.QuestTradeAutofill == null
                        ? new List<QuestTradeOffer>()
                        : message.QuestTradeAutofill
                            .Where(o => o != null)
                            .Select(o => new QuestTradeOffer(o.ItemAoid, o.Count))
                            .ToList();

                    QuestTradeReceived?.Invoke(new QuestTradePayload(
                        message.QuestTradeSessionId ?? string.Empty,
                        message.QuestTradeTitle ?? "Quest Trade",
                        message.QuestTradePrompt ?? string.Empty,
                        requirements,
                        autofill,
                        message.Message ?? string.Empty));
                    continue;
                }
                else if (string.Equals(message.Type, "admin_item_granted", StringComparison.OrdinalIgnoreCase))
                {
                    AdminItemGranted?.Invoke(new AdminItemGrantedApproval(
                        message.InstanceId,
                        message.Message ?? string.Empty));
                    if (!string.IsNullOrWhiteSpace(message.Message))
                        ServerNoticeReceived?.Invoke(message.Message);
                    continue;
                }
                else if (string.Equals(message.Type, "loot_result", StringComparison.OrdinalIgnoreCase))
                {
                    var lootItems = message.LootItems == null
                        ? new List<LootItem>()
                        : message.LootItems
                            .Where(i => i != null && i.Aoid > 0)
                            .Select(i => new LootItem(i.Aoid, i.Name ?? string.Empty, Math.Max(1, i.Quantity), i.Ql))
                            .ToList();

                    LootReceived?.Invoke(new LootPayload(
                        message.EntityId ?? string.Empty,
                        message.LootCredits,
                        message.LootTakenAoid,
                        lootItems,
                        message.Message ?? string.Empty));

                    if (!string.IsNullOrWhiteSpace(message.Message))
                        ServerNoticeReceived?.Invoke(message.Message);
                    continue;
                }
                else if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
                {
                    _connectionState = $"Server error at tick={message.Tick}";
                    if (ShouldLogServerError(message.Message))
                        Debug.LogWarning($"AO.Server error: {message.Message}");
                    if (!string.IsNullOrWhiteSpace(message.Message))
                        ServerNoticeReceived?.Invoke(message.Message);
                    if (!string.IsNullOrWhiteSpace(message.Message)
                        && message.Message.IndexOf("Zone transition", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var zoneManager = FindFirstObjectByType<ZoneTransitionManager>();
                        if (zoneManager != null)
                            zoneManager.NotifyAuthoritativeTransitionFailed();
                    }
                }

                if (!string.Equals(message.Type, "world_state", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(message.Type, "welcome", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(message.Type, "zone_bootstrap", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ApplyLocalPlayerSnapshot(message);
            }

            if (!_incoming.IsEmpty && processed >= maxMessages)
                _connectionState = $"Receiving world state (backlog {_incoming.Count})";
        }

        private bool ShouldLogServerError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return true;

            float throttleSeconds = Mathf.Max(0f, repeatedServerErrorLogIntervalSeconds);
            if (throttleSeconds <= 0f)
                return true;

            string key = NormalizeServerErrorKey(message);
            float now = Time.unscaledTime;
            if (_lastServerErrorLogTimeByKey.TryGetValue(key, out float lastLoggedAt))
            {
                if (now - lastLoggedAt < throttleSeconds)
                    return false;
            }

            _lastServerErrorLogTimeByKey[key] = now;
            return true;
        }


        private static string NormalizeServerErrorKey(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "error:empty";

            if (IsForcefieldCooldownError(message))
                return "error:forcefield_cooldown";

            return message.Trim();
        }

        private static bool IsForcefieldCooldownError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.StartsWith("Forcefield is cooling down", StringComparison.OrdinalIgnoreCase);
        }

        private void HandleRuntimeEntityDelta(ServerMessage message)
        {
            if (message == null || message.PlayfieldId <= 0 || message.RuntimeEntityDeltas == null || message.RuntimeEntityDeltas.Length == 0)
                return;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            if (_bootstrap != null && _bootstrap.ActivePlayfieldId > 0 && _bootstrap.ActivePlayfieldId != message.PlayfieldId)
                return;

            Transform root = GetOrCreateRuntimeEntitiesRoot(message.PlayfieldId);
            _runtimeEntitiesPlayfieldId = message.PlayfieldId;

            for (int i = 0; i < message.RuntimeEntityDeltas.Length; i++)
            {
                var delta = message.RuntimeEntityDeltas[i];
                if (delta == null)
                    continue;

                string op = (delta.Operation ?? string.Empty).Trim().ToLowerInvariant();
                RuntimeEntitySnapshot entity = delta.Entity;
                if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                    continue;

                if (op == "remove" || op == "delete")
                {
                    RemoveRuntimeEntityVisual(entity.EntityId);
                    continue;
                }

                UpsertRuntimeEntityVisual(root, entity);
            }
        }

        private void ApplyRuntimeSnapshotFromWorldStateIfNeeded(ServerMessage message)
        {
            if (message == null || message.PlayfieldId <= 0 || message.RuntimeEntities == null || message.RuntimeEntities.Length == 0)
                return;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            if (_bootstrap != null && _bootstrap.ActivePlayfieldId > 0 && _bootstrap.ActivePlayfieldId != message.PlayfieldId)
                return;

            if (_runtimeEntityApplyCoroutine != null)
                return;

            bool playfieldChanged = _runtimeEntitiesPlayfieldId > 0 && _runtimeEntitiesPlayfieldId != message.PlayfieldId;
            bool needsInitialSnapshot = _runtimeEntitiesPlayfieldId != message.PlayfieldId || _runtimeEntityViews.Count == 0;
            if (!playfieldChanged && !needsInitialSnapshot)
                return;

            ApplyRuntimeEntities(message.PlayfieldId, message.RuntimeEntities);
        }

        private void ReconcileRuntimeEntitiesFromWorldState(ServerMessage message)
        {
            if (message == null || message.PlayfieldId <= 0 || message.RuntimeEntities == null || message.RuntimeEntities.Length == 0)
                return;

            if (Time.unscaledTime < _nextRuntimeSnapshotReconcileAt)
                return;
            _nextRuntimeSnapshotReconcileAt = Time.unscaledTime + 0.5f;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            if (_bootstrap != null && _bootstrap.ActivePlayfieldId > 0 && _bootstrap.ActivePlayfieldId != message.PlayfieldId)
                return;

            Transform root = GetOrCreateRuntimeEntitiesRoot(message.PlayfieldId);
            _runtimeEntitiesPlayfieldId = message.PlayfieldId;

            for (int i = 0; i < message.RuntimeEntities.Length; i++)
            {
                RuntimeEntitySnapshot entity = message.RuntimeEntities[i];
                if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                    continue;

                if (!_runtimeEntityViews.TryGetValue(entity.EntityId, out var existing) || existing == null)
                {
                    UpsertRuntimeEntityVisual(root, entity);
                    continue;
                }

                UpdateRuntimeEntityVisual(existing, entity);
            }
        }

        private void HandleZoneBootstrap(ServerMessage message)
        {
            if (message == null)
                return;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            if (message.BootstrapVersion > 0 && message.BootstrapVersion < _lastBootstrapVersion)
            {
                Debug.LogWarning($"Ignored stale zone_bootstrap version={message.BootstrapVersion}, latest={_lastBootstrapVersion}.");
                return;
            }

            bool activeMatches = _bootstrap != null
                && _bootstrap.ActivePlayfieldId > 0
                && message.PlayfieldId > 0
                && _bootstrap.ActivePlayfieldId == message.PlayfieldId;
            bool expectedMatches = _expectedBootstrapPlayfieldId > 0
                && message.PlayfieldId == _expectedBootstrapPlayfieldId;

            if (!activeMatches && !expectedMatches && message.PlayfieldId > 0)
            {
                if ((_bootstrap?.ActivePlayfieldId ?? -1) <= 0 && _expectedBootstrapPlayfieldId <= 0)
                    return;

                Debug.LogWarning(
                    $"zone_bootstrap playfield mismatch server={message.PlayfieldId} local={_bootstrap?.ActivePlayfieldId ?? -1} expected={_expectedBootstrapPlayfieldId}. Ignoring.");
                return;
            }

            _lastBootstrapVersion = Math.Max(_lastBootstrapVersion, message.BootstrapVersion);
            _connectionState = $"Bootstrap PF {message.PlayfieldId} v{_lastBootstrapVersion}";
            if (expectedMatches)
                _expectedBootstrapPlayfieldId = -1;
            ApplyRuntimeEntities(message.PlayfieldId, message.RuntimeEntities);
            // Do not apply position snapshots from zone_bootstrap. These packets can carry
            // stale pre-transition player coordinates and drag the player off authoritative
            // teleport spawn points (including teleport_defaults).

            _ = SendAsync(new ClientMessage
            {
                Type = "zone_bootstrap_loaded",
                PlayfieldId = message.PlayfieldId,
                BootstrapVersion = message.BootstrapVersion,
                X = transform.position.x,
                Y = transform.position.y,
                Z = transform.position.z
            });
        }

        private void ApplyLocalPlayerSnapshot(ServerMessage message)
        {
            if (message?.Players == null || string.IsNullOrWhiteSpace(_sessionId))
                return;
            if (Time.unscaledTime < _suppressSnapshotPositionUntilTime)
                return;

            int activePlayfieldId = _bootstrap != null ? _bootstrap.ActivePlayfieldId : 0;
            for (int i = 0; i < message.Players.Length; i++)
            {
                var player = message.Players[i];
                if (player == null || !string.Equals(player.SessionId, _sessionId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (activePlayfieldId > 0 && player.PlayfieldId > 0 && player.PlayfieldId != activePlayfieldId)
                    continue;

                _authoritativePosition = new Vector3(player.X, player.Y, player.Z);
                _hasAuthoritativePosition = true;
                _authoritativeHealth = player.Health;
                _authoritativeMaxHealth = player.MaxHealth;
                _authoritativeNano = player.Nano;
                _authoritativeMaxNano = player.MaxNano;
                _authoritativeExperience = player.Experience;
                _hasAuthoritativeStatSnapshot = true;
                break;
            }
        }

        private void TryRecoverSessionIdFromSnapshot(ServerMessage message)
        {
            if (!string.IsNullOrWhiteSpace(_sessionId))
                return;
            if (message?.Players == null || message.Players.Length == 0)
                return;

            string localName = _bridge?.Character?.Name;
            PlayerSnapshot best = null;

            if (!string.IsNullOrWhiteSpace(localName))
            {
                for (int i = 0; i < message.Players.Length; i++)
                {
                    var p = message.Players[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.SessionId))
                        continue;
                    if (string.Equals(p.Name, localName, StringComparison.OrdinalIgnoreCase))
                    {
                        best = p;
                        break;
                    }
                }
            }

            if (best == null && message.Players.Length == 1)
            {
                var only = message.Players[0];
                if (only != null && !string.IsNullOrWhiteSpace(only.SessionId))
                    best = only;
            }

            if (best == null)
                return;

            _sessionId = best.SessionId.Trim();
            _connectionState = $"Recovered session from world_state ({_sessionId})";
            Debug.Log($"AO.Server session recovered from world_state: {_sessionId}");
        }

        private void HandleZoneTransition(ServerMessage message)
        {
            bool recentAdminRequest = (Time.unscaledTime - _lastAdminTeleportRequestAt) <= 12.0f;
            bool nonAdminLockActive = Time.unscaledTime < _ignoreNonAdminZoneTransitionsUntilTime;
            if (!message.IsAdminAction
                && (_pendingAdminTeleportPlayfieldId > 0 || nonAdminLockActive || recentAdminRequest))
            {
                Debug.LogWarning(
                    $"Ignored non-admin zone_transition while waiting for admin teleport response. " +
                    $"requestedPF={_pendingAdminTeleportPlayfieldId}, incomingPF={message.PlayfieldId}, link={message.ZoneLinkId ?? "unknown"}");
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                    $"Ignored non-admin zone transition ({message.ZoneLinkId ?? "unknown"}) while waiting for teleport response.");
                return;
            }

            _pendingAdminTeleportFallback = false;
            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap == null)
            {
                Debug.LogWarning("AO.Server zone transition received but PrototypeWorldBootstrap was not found.");
                return;
            }

            Transform playerTransform = ResolveControlledTransform();
            if (playerTransform == null)
                playerTransform = transform;

            // Always trust finite spawn AO from AO.Server, regardless of whether
            // it originated from manual coords, teleport_defaults, or highest-point fallback.
            bool hasExplicitSpawnAo = IsFinite(message.SpawnAoX)
                && IsFinite(message.SpawnAoY)
                && IsFinite(message.SpawnAoZ);
            Vector3 aoSpawn = hasExplicitSpawnAo
                ? new Vector3(message.SpawnAoX, message.SpawnAoY, message.SpawnAoZ)
                : Vector3.zero;
            _expectedBootstrapPlayfieldId = message.PlayfieldId;
            bool ok = _bootstrap.TransitionToPlayfield(
                message.PlayfieldId,
                playerTransform,
                hasExplicitSpawnAo ? aoSpawn : null,
                message.TargetYaw);

            if (!ok)
            {
                if (_bootstrap.ActivePlayfieldId == message.PlayfieldId)
                {
                    // Fallback: same-playfield authoritative teleport should still reposition player
                    // even if TransitionToPlayfield rejected due loader state.
                    Vector3 fallbackWorld = hasExplicitSpawnAo
                        ? _bootstrap.ConvertAoToWorld(aoSpawn)
                        : playerTransform.position;
                    _walker?.ApplyAuthoritativeTeleportPosition(fallbackWorld);
                    _authoritativePosition = fallbackWorld;
                    _hasAuthoritativePosition = true;
                    _suppressSnapshotPositionUntilTime = Time.unscaledTime + 1.0f;
                    Debug.LogWarning(
                        $"AO.Server-approved same-PF teleport fallback applied for PF {message.PlayfieldId} at {fallbackWorld}.");
                    _ = SendAsync(new ClientMessage
                    {
                        Type = "zone_loaded",
                        PlayfieldId = message.PlayfieldId,
                        X = fallbackWorld.x,
                        Y = fallbackWorld.y,
                        Z = fallbackWorld.z
                    });
                    _expectedBootstrapPlayfieldId = message.PlayfieldId;
                    return;
                }

                Debug.LogWarning($"AO.Server-approved zone transition failed locally for PF {message.PlayfieldId}.");
                var zoneManager = FindFirstObjectByType<ZoneTransitionManager>();
                if (zoneManager != null)
                    zoneManager.NotifyAuthoritativeTransitionFailed();
                return;
            }

            _connectionState = $"Zoned to PF {message.PlayfieldId}";
            Debug.Log($"AO.Server approved zone transition {message.ZoneLinkId} -> PF {message.PlayfieldId}");

            var zoneTransitionManager = FindFirstObjectByType<ZoneTransitionManager>();
            if (zoneTransitionManager != null)
                zoneTransitionManager.NotifyAuthoritativeTransitionComplete(playerTransform);

            // Do not send immediate zone_loaded here. During async transition/load this can
            // report stale coordinates from the old playfield and poison authoritative snapshots.
            // ReportLocalPlayfieldIfChanged will send zone_loaded after PF activation settles.
            _suppressSnapshotPositionUntilTime = Time.unscaledTime + 2.0f;
            _lastReportedPlayfieldId = -1;
            _pendingZoneLoadedAck = true;
            _pendingZoneLoadedAckPlayfieldId = message.PlayfieldId;
            _pendingZoneLoadedAckHasAo = hasExplicitSpawnAo;
            _pendingZoneLoadedAckAo = aoSpawn;
            if (message.IsAdminAction)
            {
                _ignoreNonAdminZoneTransitionsUntilTime = Mathf.Max(
                    _ignoreNonAdminZoneTransitionsUntilTime,
                    Time.unscaledTime + 6.0f);
                _pendingAdminPostSettleCheck = hasExplicitSpawnAo;
                _pendingAdminPostSettlePlayfieldId = message.PlayfieldId;
                _pendingAdminPostSettleAo = aoSpawn;
                _pendingAdminPostSettleAtTime = Time.unscaledTime + 0.75f;
                _pendingAdminPostSettleExpireAtTime = Time.unscaledTime + 20.0f;
            }

            if (!_bootstrap.ActivePlayfieldGlbLoadInProgress)
            {
                _authoritativePosition = playerTransform.position;
                _hasAuthoritativePosition = true;
            }

            if (hasExplicitSpawnAo)
            {
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                    $"Server zone transition applied: link={(string.IsNullOrWhiteSpace(message.ZoneLinkId) ? "n/a" : message.ZoneLinkId)}, PF {message.PlayfieldId}, spawn AO=({aoSpawn.x:0.##}, {aoSpawn.y:0.##}, {aoSpawn.z:0.##}), admin={message.IsAdminAction}.");
            }
            else
            {
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                    $"Server zone transition applied: link={(string.IsNullOrWhiteSpace(message.ZoneLinkId) ? "n/a" : message.ZoneLinkId)}, PF {message.PlayfieldId}, spawn AO=(auto), admin={message.IsAdminAction}.");
            }
        }

        private void SendMovementIfNeeded()
        {
            if (_writer == null || _walker == null || string.IsNullOrWhiteSpace(_sessionId))
                return;

            if (Time.unscaledTime < _nextSendTime)
                return;

            Vector3 intent = _walker.GetMovementIntentWorld();
            bool changed = (intent - _lastSentIntent).sqrMagnitude > 0.0001f;
            bool isMoving = intent.sqrMagnitude > 0.0001f;
            if (_hasSentMovementState && !changed)
                return;

            _nextSendTime = Time.unscaledTime + Mathf.Max(0.01f, sendIntervalSeconds);
            _lastSentIntent = intent;
            _hasSentMovementState = true;

            var message = new ClientMessage
            {
                Type = isMoving ? "move" : "stop",
                X = intent.x,
                Z = intent.z
            };

            _ = SendAsync(message);
        }

        private async Task SendAsync(ClientMessage message)
        {
            if (_writer == null)
                return;
            if (message == null)
                return;

            // Protocol guard: before welcome/session assignment, only "hello" is allowed.
            if (string.IsNullOrWhiteSpace(_sessionId)
                && !string.Equals(message.Type, "hello", StringComparison.OrdinalIgnoreCase))
            {
                if (logTraffic)
                    Debug.LogWarning($"AO.Server send blocked before welcome: type={message.Type}");
                return;
            }

            try
            {
                await _sendSemaphore.WaitAsync();

                if (_writer == null)
                    return;

                PopulateMessageContext(message);
                string payload = JsonConvert.SerializeObject(message);
                _messagesSent++;
                if (logTraffic)
                    Debug.Log($"AO.Server <- {payload}");

                await _writer.WriteLineAsync(payload);
            }
            catch (Exception ex)
            {
                string failure = $"Send failed: {ex.Message}";
                _connectionState = failure;
                Debug.LogWarning($"AO.Server send failed: {ex.Message}");
                Disconnect(failure, keepFailureState: true);
            }
            finally
            {
                if (_sendSemaphore.CurrentCount == 0)
                    _sendSemaphore.Release();
            }
        }

        private void PopulateMessageContext(ClientMessage message)
        {
            if (message == null)
                return;

            var character = _bridge != null ? _bridge.Character : null;

            message.Name = character?.Name ?? gameObject.name;
            bool preserveExplicitPlayfield =
                string.Equals(message.Type, "admin_teleport", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Type, "zone_loaded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Type, "zone_bootstrap_loaded", StringComparison.OrdinalIgnoreCase);
            if (!preserveExplicitPlayfield)
                message.PlayfieldId = _bootstrap != null ? _bootstrap.ActivePlayfieldId : message.PlayfieldId;

            if (string.Equals(message.Type, "set_character", StringComparison.OrdinalIgnoreCase))
                return;

            message.BreedId = character?.BreedId ?? 1;
            message.ProfessionId = character?.ProfessionId ?? 1;
            message.Level = character?.Level?.Level ?? 1;
            message.Sex = _bridge != null ? (int)_bridge.Sex : 0;
            message.Experience = character?.Level?.Experience ?? 0L;
        }

        private void ApplyAuthoritativeMovement()
        {
            if (!_hasAuthoritativePosition || _walker == null || !authoritativeMovement)
                return;

            float distance = Vector3.Distance(transform.position, _authoritativePosition);
            if (distance <= Mathf.Max(0.25f, minorDesyncIgnoreDistance))
                return;

            if (distance > snapDistance)
            {
                _walker.ApplyAuthoritativePosition(_authoritativePosition);
                return;
            }

            Vector3 next = Vector3.Lerp(transform.position, _authoritativePosition, Time.deltaTime * reconcileLerpSpeed);
            _walker.ApplyAuthoritativePosition(next);
        }

        private void TryReconnectIfNeeded()
        {
            if (!autoReconnect || !connectOnStart || _client != null || _isConnecting)
                return;

            if (Time.unscaledTime < _nextReconnectTime)
                return;

            _nextReconnectTime = Time.unscaledTime + Mathf.Max(0.25f, reconnectDelaySeconds);
            _ = ConnectAsync();
        }

        private void ApplyRuntimeEntities(int playfieldId, RuntimeEntitySnapshot[] runtimeEntities)
        {
            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            bool emptyCurrentPlayfieldSnapshot = (runtimeEntities == null || runtimeEntities.Length == 0)
                && _runtimeEntityViews.Count > 0
                && _runtimeEntitiesPlayfieldId == playfieldId;
            if (emptyCurrentPlayfieldSnapshot)
                return;

            _runtimeEntityApplyGeneration++;
            if (_runtimeEntityApplyCoroutine != null)
            {
                StopCoroutine(_runtimeEntityApplyCoroutine);
                _runtimeEntityApplyCoroutine = null;
            }

            ClearRuntimeEntities();
            _runtimeEntitiesPlayfieldId = playfieldId;

            if (runtimeEntities == null || runtimeEntities.Length == 0)
                return;

            _runtimeEntityApplyCoroutine = StartCoroutine(
                ApplyRuntimeEntitiesBatchedCoroutine(
                    playfieldId,
                    runtimeEntities,
                    _runtimeEntityApplyGeneration));
        }

        private void UpsertRuntimeEntitySnapshots(int playfieldId, RuntimeEntitySnapshot[] runtimeEntities)
        {
            if (playfieldId <= 0 || runtimeEntities == null)
                return;
            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap != null && _bootstrap.ActivePlayfieldId > 0 && _bootstrap.ActivePlayfieldId != playfieldId)
                return;

            Transform root = GetOrCreateRuntimeEntitiesRoot(playfieldId);
            _runtimeEntitiesPlayfieldId = playfieldId;
            var incomingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < runtimeEntities.Length; i++)
            {
                var entity = runtimeEntities[i];
                if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                    continue;

                incomingIds.Add(entity.EntityId);
                UpsertRuntimeEntityVisual(root, entity);
            }

            RemoveRuntimeEntitiesMissingFromSnapshot(incomingIds);
        }

        private IEnumerator ApplyRuntimeEntitiesBatchedCoroutine(
            int playfieldId,
            RuntimeEntitySnapshot[] runtimeEntities,
            int generation)
        {
            // Let player control settle for a frame before heavy runtime-entity spawn.
            yield return null;

            Transform root = GetOrCreateRuntimeEntitiesRoot(playfieldId);
            int appliedThisFrame = 0;
            int batchSize = Mathf.Max(1, runtimeEntityApplyBatchSizePerFrame);

            for (int i = 0; i < runtimeEntities.Length; i++)
            {
                if (generation != _runtimeEntityApplyGeneration || _runtimeEntitiesPlayfieldId != playfieldId)
                    yield break;

                var entity = runtimeEntities[i];
                if (entity == null)
                    continue;

                UpsertRuntimeEntityVisual(root, entity);
                appliedThisFrame++;
                if (appliedThisFrame >= batchSize)
                {
                    appliedThisFrame = 0;
                    yield return null;
                }
            }

            if (generation == _runtimeEntityApplyGeneration)
                _runtimeEntityApplyCoroutine = null;
        }

        private Transform GetOrCreateRuntimeEntitiesRoot(int playfieldId)
        {
            if (_runtimeEntitiesRoot != null)
                return _runtimeEntitiesRoot;

            Transform parent = null;
            if (_bootstrap != null && _bootstrap.ActivePlayfieldRoot != null)
                parent = _bootstrap.ActivePlayfieldRoot;
            else if (transform.parent != null)
                parent = transform.parent;

            var rootObject = new GameObject($"RuntimeEntities_{playfieldId}");
            _runtimeEntitiesRoot = rootObject.transform;
            if (parent != null)
                _runtimeEntitiesRoot.SetParent(parent, false);

            return _runtimeEntitiesRoot;
        }

        private void UpsertRuntimeEntityVisual(Transform parent, RuntimeEntitySnapshot entity)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                return;
            if (ShouldSuppressRuntimeEntityAsLocalPlayer(entity))
                return;

            bool incomingIsCorpse = IsRuntimeCorpseEntityId(entity.EntityId);
            if (entity.IdentityInstance > 0
                && _runtimeEntityIdByIdentityInstance.TryGetValue(entity.IdentityInstance, out var mappedEntityId)
                && !string.IsNullOrWhiteSpace(mappedEntityId)
                && !string.Equals(mappedEntityId, entity.EntityId, StringComparison.OrdinalIgnoreCase))
            {
                // Server occasionally emits a new entity id for the same dynel identity.
                // Remove the previous visual to avoid duplicate nameplates/meshes.
                RemoveRuntimeEntityVisual(mappedEntityId);
            }

            string fallbackKey = BuildRuntimeEntityFallbackKey(entity);
            if (!string.IsNullOrWhiteSpace(fallbackKey)
                && _runtimeEntityIdByFallbackKey.TryGetValue(fallbackKey, out var fallbackMappedEntityId)
                && !string.IsNullOrWhiteSpace(fallbackMappedEntityId)
                && !string.Equals(fallbackMappedEntityId, entity.EntityId, StringComparison.OrdinalIgnoreCase))
            {
                bool mappedIsCorpse = IsRuntimeCorpseEntityId(fallbackMappedEntityId);
                if (incomingIsCorpse == mappedIsCorpse)
                    RemoveRuntimeEntityVisual(fallbackMappedEntityId);
            }

            if (TryAdoptRuntimeCorpseVisual(entity, out var adopted) && adopted != null)
            {
                UpdateRuntimeEntityVisual(adopted, entity);
                _runtimeEntityViews[entity.EntityId] = adopted;
                if (!string.IsNullOrWhiteSpace(fallbackKey))
                    _runtimeEntityIdByFallbackKey[fallbackKey] = entity.EntityId;
                return;
            }

            if (_runtimeEntityViews.TryGetValue(entity.EntityId, out var existing) && existing != null)
            {
                UpdateRuntimeEntityVisual(existing, entity);
                if (entity.IdentityInstance > 0)
                    _runtimeEntityIdByIdentityInstance[entity.IdentityInstance] = entity.EntityId;
                if (!string.IsNullOrWhiteSpace(fallbackKey))
                    _runtimeEntityIdByFallbackKey[fallbackKey] = entity.EntityId;
                return;
            }

            RemoveLikelyDuplicateRuntimeEntityVisual(entity);

            GameObject created = CreateRuntimeEntityVisual(parent, entity);
            if (created != null)
            {
                _runtimeEntityViews[entity.EntityId] = created;
                if (entity.IdentityInstance > 0)
                    _runtimeEntityIdByIdentityInstance[entity.IdentityInstance] = entity.EntityId;
                if (!string.IsNullOrWhiteSpace(fallbackKey))
                    _runtimeEntityIdByFallbackKey[fallbackKey] = entity.EntityId;
            }
        }

        private bool TryAdoptRuntimeCorpseVisual(RuntimeEntitySnapshot entity, out GameObject adopted)
        {
            adopted = null;
            if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                return false;

            int marker = entity.EntityId.IndexOf(RuntimeCorpseEntityMarker, StringComparison.OrdinalIgnoreCase);
            if (marker <= 0)
                return false;

            string sourceEntityId = entity.EntityId.Substring(0, marker);
            if (string.IsNullOrWhiteSpace(sourceEntityId))
                return false;
            if (!_runtimeEntityViews.TryGetValue(sourceEntityId, out adopted) || adopted == null)
                return false;

            _runtimeEntityViews.Remove(sourceEntityId);
            _runtimeEntityDescriptions.Remove(sourceEntityId);
            _runtimeEntityTemporaryExpiresUnixMs.Remove(sourceEntityId);

            var identity = adopted.GetComponent<RuntimeDynelQuestIdentity>();
            if (identity != null
                && identity.IdentityInstance > 0
                && _runtimeEntityIdByIdentityInstance.TryGetValue(identity.IdentityInstance, out var mappedEntityId)
                && string.Equals(mappedEntityId, sourceEntityId, StringComparison.OrdinalIgnoreCase))
            {
                _runtimeEntityIdByIdentityInstance.Remove(identity.IdentityInstance);
            }

            RemoveRuntimeEntityFallbackMapping(sourceEntityId);
            RuntimeEntityRemoved?.Invoke(sourceEntityId);
            return true;
        }

        private const string RuntimeCorpseEntityMarker = ":corpse:";

        private static bool IsRuntimeCorpseEntityId(string entityId)
        {
            return !string.IsNullOrWhiteSpace(entityId)
                && entityId.IndexOf(RuntimeCorpseEntityMarker, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private void RemoveRuntimeEntityFallbackMapping(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) || _runtimeEntityIdByFallbackKey.Count == 0)
                return;

            string removeKey = null;
            foreach (var kvp in _runtimeEntityIdByFallbackKey)
            {
                if (string.Equals(kvp.Value, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    removeKey = kvp.Key;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(removeKey))
                _runtimeEntityIdByFallbackKey.Remove(removeKey);
        }

        private void RemoveRuntimeEntityVisual(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return;

            if (_runtimeEntityViews.TryGetValue(entityId, out var visual) && visual != null)
            {
                var identity = visual.GetComponent<RuntimeDynelQuestIdentity>();
                if (identity != null
                    && identity.IdentityInstance > 0
                    && _runtimeEntityIdByIdentityInstance.TryGetValue(identity.IdentityInstance, out var mappedEntityId)
                    && string.Equals(mappedEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    _runtimeEntityIdByIdentityInstance.Remove(identity.IdentityInstance);
                }
                Destroy(visual);
            }

            _runtimeEntityViews.Remove(entityId);
            _runtimeEntityDescriptions.Remove(entityId);
            _runtimeEntityTemporaryExpiresUnixMs.Remove(entityId);
            RuntimeEntityRemoved?.Invoke(entityId);
            RemoveRuntimeEntityFallbackMapping(entityId);
        }

        private void RemoveExpiredTemporaryRuntimeEntities()
        {
            if (_runtimeEntityTemporaryExpiresUnixMs.Count == 0)
                return;

            if (Time.unscaledTime < _nextTemporaryEntityExpiryScanAt)
                return;

            _nextTemporaryEntityExpiryScanAt = Time.unscaledTime + 0.25f;
            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expired = new List<string>();
            foreach (var pair in _runtimeEntityTemporaryExpiresUnixMs)
            {
                if (pair.Value > 0 && pair.Value <= nowUnixMs)
                    expired.Add(pair.Key);
            }

            for (int i = 0; i < expired.Count; i++)
                RemoveRuntimeEntityVisual(expired[i]);
        }

        private void RemoveRuntimeEntitiesMissingFromSnapshot(HashSet<string> incomingIds)
        {
            if (incomingIds == null || _runtimeEntityViews.Count == 0)
                return;

            var stale = new List<string>();
            foreach (var pair in _runtimeEntityViews)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                if (!incomingIds.Contains(pair.Key))
                    stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                RemoveRuntimeEntityVisual(stale[i]);
        }

        private bool ShouldSuppressRuntimeEntityAsLocalPlayer(RuntimeEntitySnapshot entity)
        {
            if (entity == null)
                return false;
            if (_bridge == null)
                _bridge = FindFirstObjectByType<CharacterRuntimeBridge>();
            if (_bridge == null)
                return false;

            string localName = string.IsNullOrWhiteSpace(_bridge.DisplayNameOverride)
                ? string.Empty
                : _bridge.DisplayNameOverride.Trim();
            string entityName = string.IsNullOrWhiteSpace(entity.DisplayName)
                ? string.Empty
                : entity.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(entityName))
                return false;
            if (!string.Equals(localName, entityName, StringComparison.OrdinalIgnoreCase))
                return false;

            Vector3 localPos = transform.position;
            Vector3 entityPos = ConvertAoToWorldPosition(entity.X, entity.Y, entity.Z);
            Vector3 d = localPos - entityPos;
            return d.sqrMagnitude <= (3.0f * 3.0f);
        }

        private static string BuildRuntimeEntityFallbackKey(RuntimeEntitySnapshot entity)
        {
            if (entity == null)
                return string.Empty;
            string name = string.IsNullOrWhiteSpace(entity.DisplayName)
                ? string.Empty
                : entity.DisplayName.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            int gx = Mathf.RoundToInt(entity.X * 2f);
            int gy = Mathf.RoundToInt(entity.Y * 2f);
            int gz = Mathf.RoundToInt(entity.Z * 2f);
            int templateId = entity.TemplateId ?? 0;
            int meshId = entity.MeshId ?? 0;
            return $"{name}|{templateId}|{meshId}|{gx}|{gy}|{gz}";
        }

        private void RemoveLikelyDuplicateRuntimeEntityVisual(RuntimeEntitySnapshot entity)
        {
            if (entity == null)
                return;

            string targetName = string.IsNullOrWhiteSpace(entity.DisplayName)
                ? string.Empty
                : entity.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(targetName))
                return;

            Vector3 targetPos = ConvertAoToWorldPosition(entity.X, entity.Y, entity.Z);
            int targetTemplate = entity.TemplateId ?? 0;
            int targetMesh = entity.MeshId ?? 0;
            const float maxDuplicateHorizontalDistanceSq = 2.5f * 2.5f;

            string duplicateEntityId = null;
            foreach (var kvp in _runtimeEntityViews)
            {
                string existingEntityId = kvp.Key;
                GameObject existing = kvp.Value;
                if (existing == null || string.IsNullOrWhiteSpace(existingEntityId))
                    continue;
                if (string.Equals(existingEntityId, entity.EntityId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsRuntimeCorpseEntityId(existingEntityId) != IsRuntimeCorpseEntityId(entity.EntityId))
                    continue;

                var identity = existing.GetComponent<RuntimeDynelQuestIdentity>();
                string existingName = identity != null
                    ? (identity.DynelName ?? string.Empty).Trim()
                    : string.Empty;
                if (!string.Equals(existingName, targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Vector3 existingPos = existing.transform.position;
                Vector3 d = existingPos - targetPos;
                d.y = 0f;
                if (d.sqrMagnitude > maxDuplicateHorizontalDistanceSq)
                    continue;

                bool sameTemplateOrMesh = true;
                if (identity != null)
                {
                    sameTemplateOrMesh = targetTemplate <= 0
                        || identity.TemplateId <= 0
                        || identity.TemplateId == targetTemplate
                        || targetMesh <= 0;
                }

                if (!sameTemplateOrMesh)
                    continue;

                duplicateEntityId = existingEntityId;
                break;
            }

            if (!string.IsNullOrWhiteSpace(duplicateEntityId))
                RemoveRuntimeEntityVisual(duplicateEntityId);
        }

        private GameObject CreateRuntimeEntityVisual(Transform parent, RuntimeEntitySnapshot entity)
        {
            GameObject visual = null;
            bool createdPrimitive = false;
            if (_bootstrap != null)
            {
                visual = _bootstrap.SpawnAuthoritativeRuntimeEntity(
                    parent,
                    entity.IdentityInstance,
                    entity.TemplateId,
                    entity.MeshId,
                    entity.ObjectType,
                    entity.DisplayName,
                    entity.ImportKey,
                    entity.MeshName,
                    new Vector3(entity.X, entity.Y, entity.Z),
                    entity.YawDegrees);
            }

            if (visual == null)
            {
                PrimitiveType primitiveType = ResolveRuntimePrimitive(entity.ObjectType);
                visual = GameObject.CreatePrimitive(primitiveType);
                visual.name = $"RuntimeEntity_{SanitizeName(string.IsNullOrWhiteSpace(entity.DisplayName) ? entity.ObjectType : entity.DisplayName)}";
                visual.transform.SetParent(parent, false);
                visual.AddComponent<RuntimePrimitivePlaceholderTag>();
                createdPrimitive = true;
            }

            UpdateRuntimeEntityVisual(visual, entity, createdPrimitive);
            return visual;
        }

        private void ReportLocalPlayfieldIfChanged()
        {
            if (!IsConnected)
                return;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap == null)
                return;

            int localPf = _bootstrap.ActivePlayfieldId;
            if (localPf <= 0)
                return;
            if (_bootstrap.ActivePlayfieldGlbLoadInProgress)
                return;
            if (_pendingZoneLoadedAck)
                return;
            if (localPf == _lastReportedPlayfieldId)
                return;

            Vector3 pos = transform.position;
            _ = SendAsync(new ClientMessage
            {
                Type = "zone_loaded",
                PlayfieldId = localPf,
                X = pos.x,
                Y = pos.y,
                Z = pos.z
            });
            _lastReportedPlayfieldId = localPf;
        }

        private void TrySendPendingZoneLoadedAck()
        {
            if (!_pendingZoneLoadedAck || !IsConnected)
                return;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap == null)
                return;
            if (_bootstrap.ActivePlayfieldGlbLoadInProgress)
                return;
            if (_pendingZoneLoadedAckPlayfieldId <= 0 || _bootstrap.ActivePlayfieldId != _pendingZoneLoadedAckPlayfieldId)
                return;

            Transform playerTransform = ResolveControlledTransform();
            if (playerTransform == null)
                playerTransform = transform;

            // Final authoritative settle for any server-provided explicit spawn AO
            // (admin teleports + zone links with TargetAOSpawn).
            bool shouldSnapToAuthoritativeAo = _pendingZoneLoadedAckHasAo;
            if (shouldSnapToAuthoritativeAo)
            {
                Vector3 settled = _bootstrap.ConvertAoToWorld(_pendingZoneLoadedAckAo);
                _walker?.ApplyAuthoritativeTeleportPosition(settled);
                _authoritativePosition = settled;
                _hasAuthoritativePosition = true;
                _suppressSnapshotPositionUntilTime = Time.unscaledTime + 0.75f;
            }

            Vector3 pos = playerTransform.position;
            _ = SendAsync(new ClientMessage
            {
                Type = "zone_loaded",
                PlayfieldId = _pendingZoneLoadedAckPlayfieldId,
                X = pos.x,
                Y = pos.y,
                Z = pos.z
            });

            _lastReportedPlayfieldId = _pendingZoneLoadedAckPlayfieldId;
            _pendingZoneLoadedAck = false;
            _pendingZoneLoadedAckPlayfieldId = -1;
            _pendingZoneLoadedAckHasAo = false;
            _pendingZoneLoadedAckAo = Vector3.zero;
            // Only clear pending admin TP flags if this ack corresponds to that request.
            if (_pendingAdminTeleportPlayfieldId > 0
                && _pendingAdminTeleportPlayfieldId == _lastReportedPlayfieldId)
            {
                _pendingAdminTeleportPlayfieldId = -1;
                _pendingAdminTeleportSentAt = 0f;
            }
            _ignoreNonAdminZoneTransitionsUntilTime = Mathf.Max(
                _ignoreNonAdminZoneTransitionsUntilTime,
                Time.unscaledTime + 1.5f);
        }

        private void TryApplyPendingAdminPostSettleCorrection()
        {
            if (!_pendingAdminPostSettleCheck)
                return;
            if (Time.unscaledTime < _pendingAdminPostSettleAtTime)
                return;
            if (Time.unscaledTime > _pendingAdminPostSettleExpireAtTime)
            {
                _pendingAdminPostSettleCheck = false;
                _pendingAdminPostSettlePlayfieldId = -1;
                _pendingAdminPostSettleAo = Vector3.zero;
                _pendingAdminPostSettleAtTime = 0f;
                _pendingAdminPostSettleExpireAtTime = 0f;
                return;
            }
            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap == null || _bootstrap.ActivePlayfieldId != _pendingAdminPostSettlePlayfieldId)
                return;
            // Intentionally allow correction while playfield content is still streaming/recentering.
            // This keeps AO exact as conversion center evolves during load.
            if (_bootstrap.ActivePlayfieldGlbLoadInProgress)
            {
                _pendingAdminPostSettleExpireAtTime = Mathf.Max(
                    _pendingAdminPostSettleExpireAtTime,
                    Time.unscaledTime + 2.0f);
            }

            Transform playerTransform = ResolveControlledTransform();
            if (playerTransform == null)
                playerTransform = transform;

            Vector3 currentAo = _bootstrap.ConvertWorldToAo(playerTransform.position);
            Vector2 planarDelta = new Vector2(
                _pendingAdminPostSettleAo.x - currentAo.x,
                _pendingAdminPostSettleAo.z - currentAo.z);

            // If drift remains after load/recenter settles, force one final exact AO snap.
            if (planarDelta.sqrMagnitude > (0.5f * 0.5f))
            {
                Vector3 correctedWorld = _bootstrap.ConvertAoToWorld(_pendingAdminPostSettleAo);
                _walker?.ApplyAuthoritativeTeleportPosition(correctedWorld);
                if (playerTransform != null && playerTransform != transform)
                    playerTransform.position = correctedWorld;
                _authoritativePosition = correctedWorld;
                _hasAuthoritativePosition = true;
                _suppressSnapshotPositionUntilTime = Time.unscaledTime + 0.75f;
                Debug.Log(
                    $"Applied post-settle AO correction PF {_pendingAdminPostSettlePlayfieldId}: " +
                    $"targetAO={_pendingAdminPostSettleAo} currentAO={currentAo} world={correctedWorld}");

                // Retry once per short interval until mapping settles or timeout hits.
                _pendingAdminPostSettleAtTime = Time.unscaledTime + 0.2f;
                return;
            }

            _pendingAdminPostSettleCheck = false;
            _pendingAdminPostSettlePlayfieldId = -1;
            _pendingAdminPostSettleAo = Vector3.zero;
            _pendingAdminPostSettleAtTime = 0f;
            _pendingAdminPostSettleExpireAtTime = 0f;
        }

        private void TryApplyPendingAdminTeleportFallback()
        {
            if (!_pendingAdminTeleportFallback)
                return;
            if (Time.unscaledTime - _pendingAdminTeleportSentAt < 1.75f)
                return;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap == null)
                return;

            Transform playerTransform = ResolveControlledTransform();
            if (playerTransform == null)
                playerTransform = transform;

            Vector3? explicitAo = _pendingAdminTeleportUseDefault ? null : _pendingAdminTeleportAoPosition;
            bool ok = _bootstrap.TransitionToPlayfield(
                _pendingAdminTeleportPlayfieldId,
                playerTransform,
                explicitAo,
                _pendingAdminTeleportYaw);

            if (ok)
            {
                Vector3 pos = playerTransform.position;
                _ = SendAsync(new ClientMessage
                {
                    Type = "zone_loaded",
                    PlayfieldId = _pendingAdminTeleportPlayfieldId,
                    X = pos.x,
                    Y = pos.y,
                    Z = pos.z
                });
                Debug.LogWarning($"AO.Server admin teleport fallback applied locally for PF {_pendingAdminTeleportPlayfieldId}.");
            }

            _pendingAdminTeleportFallback = false;
        }

        private void UpdateRuntimeEntityVisual(GameObject visual, RuntimeEntitySnapshot entity)
        {
            UpdateRuntimeEntityVisual(visual, entity, createdPrimitiveVisual: false);
        }

        private void UpdateRuntimeEntityVisual(GameObject visual, RuntimeEntitySnapshot entity, bool createdPrimitiveVisual)
        {
            if (visual == null || entity == null)
                return;

            visual.transform.position = ConvertAoToWorldPosition(entity.X, entity.Y, entity.Z);
            visual.transform.rotation = Quaternion.Euler(0f, entity.YawDegrees, 0f);
            bool isPrimitivePlaceholder = createdPrimitiveVisual || visual.GetComponent<RuntimePrimitivePlaceholderTag>() != null;
            if (isPrimitivePlaceholder)
                visual.transform.localScale = ResolveRuntimeScale(entity.ObjectType);

            var collider = visual.GetComponent<Collider>();
            if (isPrimitivePlaceholder && collider != null && collider is not SphereCollider)
                Destroy(collider);

            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null && createdPrimitiveVisual)
            {
                var shader = ResolveRuntimeShader();
                if (shader != null)
                {
                    var material = new Material(shader);
                    material.color = ResolveRuntimeColor(entity.ObjectType);
                    renderer.sharedMaterial = material;
                }
            }

            if (ShouldAttachRuntimeCombatState(entity.ObjectType))
            {
                var combatState = visual.GetComponent<RuntimeDynelCombatState>();
                bool createdCombatState = combatState == null;
                if (combatState == null)
                    combatState = visual.AddComponent<RuntimeDynelCombatState>();
                int resolvedMaxHealth = Mathf.Max(1, entity.MaxHealth > 0 ? entity.MaxHealth : 120);
                int resolvedMaxNano = Mathf.Max(0, entity.MaxNano);
                bool wasDead = combatState.IsDead;
                combatState.Initialize(resolvedMaxHealth, 0, resolvedMaxNano, forceFullHealth: createdCombatState);
                bool hasAuthoritativeCombatState = !string.IsNullOrWhiteSpace(entity.CombatState)
                    || !string.IsNullOrWhiteSpace(entity.CombatTargetSessionId)
                    || entity.LastAttackTick > 0;
                if (entity.Health > 0)
                    combatState.SetAuthoritativeHealth(entity.Health, resolvedMaxHealth);
                if (hasAuthoritativeCombatState)
                {
                    if (entity.Health <= 0)
                        combatState.SetAuthoritativeHealth(entity.Health, resolvedMaxHealth);
                    combatState.ApplyAuthoritativeCombatState(entity.CombatState);
                }
                combatState.ApplyAuthoritativeAttackTick(entity.LastAttackTick);
                if (wasDead && !combatState.IsDead)
                    RuntimeEntityRemoved?.Invoke(entity.EntityId);
            }

            var entityIdentity = visual.GetComponent<AuthoritativeRuntimeEntityIdentity>();
            if (entityIdentity == null)
                entityIdentity = visual.AddComponent<AuthoritativeRuntimeEntityIdentity>();
            entityIdentity.Initialize(entity.EntityId, ShouldAttachRuntimeCombatState(entity.ObjectType));

            var identity = visual.GetComponent<RuntimeDynelQuestIdentity>();
            if (identity == null)
                identity = visual.AddComponent<RuntimeDynelQuestIdentity>();
            identity.DynelName = string.IsNullOrWhiteSpace(entity.DisplayName) ? visual.name : entity.DisplayName.Trim();
            identity.PlayfieldId = entity.PlayfieldId;
            identity.IdentityInstance = entity.IdentityInstance;
            identity.TemplateId = entity.TemplateId ?? 0;
            identity.Level = Math.Max(0, entity.Level);
            identity.Description = entity.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entity.Description))
                _runtimeEntityDescriptions.Remove(entity.EntityId);
            else
                _runtimeEntityDescriptions[entity.EntityId] = entity.Description.Trim();
            if (entity.TemporaryExpiresUnixMs > 0)
                _runtimeEntityTemporaryExpiresUnixMs[entity.EntityId] = entity.TemporaryExpiresUnixMs;
            else
                _runtimeEntityTemporaryExpiresUnixMs.Remove(entity.EntityId);

            AttachRuntimeInteractionTrigger(visual, entity);
        }

        private static bool ShouldAttachRuntimeCombatState(string objectType)
        {
            string normalized = (objectType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "npc"
                || normalized == "monster"
                || normalized == "mob"
                || normalized == "creature";
        }

        private void AttachRuntimeInteractionTrigger(GameObject visual, RuntimeEntitySnapshot entity)
        {
            if (visual == null || entity == null)
                return;

            bool activeInteraction = entity.InteractionEnabled
                && !string.IsNullOrWhiteSpace(entity.InteractionType)
                && !string.IsNullOrWhiteSpace(entity.EntityId);

            var existingTrigger = visual.GetComponent<AuthoritativeRuntimeInteractionTrigger>();
            var existingSphere = visual.GetComponent<SphereCollider>();
            var existingBody = visual.GetComponent<Rigidbody>();

            if (!activeInteraction)
            {
                if (existingTrigger != null)
                    Destroy(existingTrigger);
                if (existingSphere != null)
                    Destroy(existingSphere);
                if (existingBody != null)
                    Destroy(existingBody);
                return;
            }

            float radius = Mathf.Max(1f, entity.InteractionRadius > 0f ? entity.InteractionRadius : 2f);
            SphereCollider trigger = existingSphere;
            if (trigger == null)
                trigger = visual.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = radius;
            trigger.center = Vector3.zero;

            Rigidbody body = existingBody;
            if (body == null)
                body = visual.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            AuthoritativeRuntimeInteractionTrigger interactionTrigger = existingTrigger;
            if (interactionTrigger == null)
                interactionTrigger = visual.AddComponent<AuthoritativeRuntimeInteractionTrigger>();
            interactionTrigger.Initialize(
                this,
                entity.EntityId,
                string.IsNullOrWhiteSpace(entity.InteractionLabel) ? entity.DisplayName : entity.InteractionLabel,
                entity.InteractionType,
                radius);
        }

        private Vector3 ConvertAoToWorldPosition(float x, float y, float z)
        {
            if (_bootstrap != null)
                return _bootstrap.ConvertAoToWorld(new Vector3(x, y, z));

            return new Vector3(x, y, z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static PrimitiveType ResolveRuntimePrimitive(string objectType)
        {
            switch ((objectType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "door":
                    return PrimitiveType.Cube;
                case "terminal":
                    return PrimitiveType.Capsule;
                case "vendingmachine":
                    return PrimitiveType.Cylinder;
                case "npc":
                    return PrimitiveType.Capsule;
                default:
                    return PrimitiveType.Cube;
            }
        }

        private static Vector3 ResolveRuntimeScale(string objectType)
        {
            switch ((objectType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "door":
                    return new Vector3(1.2f, 2.4f, 0.2f);
                case "terminal":
                    return new Vector3(0.8f, 1.2f, 0.8f);
                case "vendingmachine":
                    return new Vector3(1.1f, 1.7f, 1.1f);
                case "npc":
                    return new Vector3(0.8f, 1.8f, 0.8f);
                default:
                    return new Vector3(0.9f, 0.9f, 0.9f);
            }
        }

        private static Color ResolveRuntimeColor(string objectType)
        {
            switch ((objectType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "door":
                    return new Color(0.55f, 0.72f, 0.88f, 1f);
                case "terminal":
                    return new Color(0.62f, 0.93f, 0.78f, 1f);
                case "vendingmachine":
                    return new Color(0.93f, 0.81f, 0.48f, 1f);
                case "npc":
                    return new Color(0.92f, 0.58f, 0.58f, 1f);
                default:
                    return new Color(0.85f, 0.85f, 0.85f, 1f);
            }
        }

        private static Shader ResolveRuntimeShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Diffuse");
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Object";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');

            return value.Replace(' ', '_');
        }

        private void ClearRuntimeEntities()
        {
            if (_runtimeEntitiesRoot != null)
                Destroy(_runtimeEntitiesRoot.gameObject);

            _runtimeEntitiesRoot = null;
            _runtimeEntityViews.Clear();
            _runtimeEntityDescriptions.Clear();
            _runtimeEntityTemporaryExpiresUnixMs.Clear();
            _runtimeEntityIdByIdentityInstance.Clear();
            _runtimeEntityIdByFallbackKey.Clear();
            _runtimeEntitiesPlayfieldId = -1;
        }

        private void Disconnect(string state, bool keepFailureState)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Dispose();

            _writer = null;
            _reader = null;
            _client = null;
            _sessionId = string.Empty;
            _lastBootstrapVersion = -1;
            _expectedBootstrapPlayfieldId = -1;
            _lastReportedPlayfieldId = -1;
            _hasAuthoritativePosition = false;
            _hasAuthoritativeStatSnapshot = false;
            _hasSentMovementState = false;
            _lastSentIntent = Vector3.zero;
            _pendingZoneLoadedAck = false;
            _pendingZoneLoadedAckPlayfieldId = -1;
            _pendingZoneLoadedAckHasAo = false;
            _pendingZoneLoadedAckAo = Vector3.zero;
            _pendingAdminPostSettleCheck = false;
            _pendingAdminPostSettlePlayfieldId = -1;
            _pendingAdminPostSettleAo = Vector3.zero;
            _pendingAdminPostSettleAtTime = 0f;
            _pendingAdminPostSettleExpireAtTime = 0f;
            _pendingAdminTeleportPlayfieldId = -1;
            _pendingAdminTeleportSentAt = 0f;
            _ignoreNonAdminZoneTransitionsUntilTime = 0f;

            ClearRuntimeEntities();
            if (keepFailureState)
                _connectionState = state;
            else
                _connectionState = "Disconnected";

            _nextReconnectTime = Time.unscaledTime + Mathf.Max(0.25f, reconnectDelaySeconds);

            if (_walker != null)
                _walker.SetLocalMovementEnabled(!IsStrictAuthoritativeServerRequired);

            if (IsStrictAuthoritativeServerRequired)
            {
                string message = $"Strict authoritative mode: AO.Server unavailable ({state}).";
                Debug.LogWarning(message);
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(message);
            }
        }

        private void NotifyStrictServerUnavailable(string message)
        {
            if (!IsStrictAuthoritativeServerRequired)
                return;

            string text = string.IsNullOrWhiteSpace(message)
                ? "Strict authoritative mode: AO.Server unavailable."
                : message;
            Debug.LogWarning(text);
            AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(text);
        }

        private Transform ResolveControlledTransform()
        {
            if (_walker != null)
                return _walker.transform;

            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (_bootstrap != null)
            {
                var t = _bootstrap.ResolvePlayerTransform();
                if (t != null)
                    return t;
            }

            return transform;
        }
    }
}





