using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using AO.Core.Characters;
using AO.Core.Stats;
using AO.Core.Items;
using AO.Client.World;
using AO.Data.Unity;
using AO.Unity.Quests;
using AO.Unity.World;
using AO.Unity.AOStyle;
using AO.Unity.Assets;
using AO.Assets.ResourceDatabase;
using UnityEngine;
using DataItemInstance = AO.Data.Core.ItemInstance;

namespace AO.Unity.Prototype
{
    public partial class PrototypeUiContext
    {
        private const int DefaultSlotStatId = 88;
        private const int CanFlagStatId = 30;
        private const int UseCanBit = 8;
        private const int StackableCanBit = 512;
        private const int StackCountStatId = 177;
        private const int ChargeCountStatId = 212;
        private const int WaitStateStatId = 430;
        private const int SittingWaitStateValue = 2;
        private const int RightHandSlotId = 6;
        private const int LeftHandSlotId = 8;
        private const int BeltSlotsStatId = 45;
        private const int CreditsStatId = 61;
        private const int DefaultStartingCredits = 1000;
        private const int DeckSlotMinLocal = 1009;
        private const int DeckSlotMaxLocal = 1014;
        private const int WeaponNonHandSlotOffset = 1000;
        private const int ArmorHandSlotOffset = 3000;
        private const int SocialSlotOffset = 2000;
        private const string DefaultHeadMeshKey = "head_solitusmale_philip_ross";
        private const string TooltipLabelColor = "#56D364";
        private const string TooltipValueColor = "#8AA48A";

        [Serializable]
        private sealed class SlotLookupFile
        {
            public Dictionary<string, string> namesById = new();
            public Dictionary<string, string> namesByBit = new();
        }

        [Serializable]
        private sealed class SlotEntry
        {
            public int Id;
            public string Name;
        }

        public sealed class ItemTooltipInfo
        {
            public string Title;
            public Color TitleColor;
            public string Body;
        }

        public sealed class UploadedNanoProgram
        {
            public int NanoId;
            public int RawNanoAoid;
            public string Name;
            public string Description;
            public int IconId;
            public long SourceCrystalInstanceId;
            public int SourceCrystalDefinitionId;
            public string SchoolTab;
            public int FallbackNcuCost;
            public int FallbackDurationSeconds;
        }

        public sealed class ActiveNanoProgram
        {
            public int ActiveId;
            public int NanoId;
            public int RawNanoAoid;
            public string Name;
            public string Description;
            public int IconId;
            public int NcuCost;
            public int NanoCost;
            public float StartedAt;
            public float DurationSeconds;
            public int? VisualProfessionOverrideId;
            public Dictionary<int, int> StatModifiers = new();
            public List<PeriodicNanoEffect> PeriodicEffects = new();
        }

        public sealed class PeriodicNanoEffect
        {
            public int StatId;
            public int Amount;
            public int MinValue;
            public int MaxValue;
            public int RemainingTicks;
            public float TickIntervalSeconds;
            public float NextTickAt;
        }

        public sealed class PendingNanoCastRequest
        {
            public int NanoId;
            public string Name;
            public float AttackSeconds;
            public float RechargeSeconds;
            public int NanoCost;
        }

        public enum SlotZone
        {
            Inventory,
            Backpack
        }

        private static readonly Dictionary<int, long> EmptyEquipped = new();
        private static readonly Dictionary<int, int> EmptyStats = new();
        private const int DefaultMaxNcuCapacity = 8;
        private const int DurationStatId = 8;
        private const int MaxNcuStatId = 181;
        private const int NcuCostStatId = 54; // Legacy nano records store cost in Level.
        private const int NcuCostStatIdPrimary = 1004;
        private const int NanoCostStatId = 407;
        private const int NanoSchoolStatId = 405;
        private const int AttackDelayStatId = 294;
        private const int RechargeDelayStatId = 210;
        private const int VisualProfessionStatId = 368;
        private const int HealDeltaStatId = 343;
        private const int NanoDeltaStatId = 364;
        private const int MaxHealthStatId = 1;
        private const int MaxNanoPoolStatId = 3;
        private const int AddAllOffStatId = 276;
        private const int MaxBeneficialSkillStatId = 538;
        private const int MinDamageStatId = 286;
        private const int MaxDamageStatId = 285;
        private const int CritBonusDamageStatId = 284;
        private const int MartialArtsFallbackStatId = 100;
        private const int MartialArtistProfessionId = 2;
        private const int ShadeProfessionId = 15;
        private const int WeaponDamageTypeStatId = 72;
        private const int DamageTypeProjectile = 1;
        private const int DamageTypeMelee = 2;
        private const int DamageTypeEnergy = 3;
        private const int DamageTypeChemical = 4;
        private const int DamageTypeRadiation = 5;
        private const int DamageTypeCold = 6;
        private const int DamageTypePoison = 7;
        private const int DamageTypeFire = 8;
        private static readonly int[] WeaponAttackSkillStatIds =
        {
            102, 103, 104, 105, 106, 107,
            109, 110, 111, 112, 114, 115, 116, 121, 133
        };

        private readonly int _maxVisibleBrowserItems;
        private readonly List<DataItemInstance> _allItems = new();
        private readonly List<UploadedNanoProgram> _uploadedPrograms = new();
        private readonly List<ActiveNanoProgram> _activePrograms = new();
        private readonly QuestAuthoringService _questAuthoring = new();
        private readonly QuestRuntimeService _questRuntime = new();
        private readonly QuestTargetFamilyService _questTargetFamilies = new();
        private readonly HashSet<int> _uploadedProgramIds = new();
        private readonly Dictionary<int, Sprite> _iconCache = new();
        private readonly HashSet<int> _missingIconIds = new();
        private readonly Dictionary<int, string> _slotNameById = new();
        private readonly Dictionary<int, string> _weaponSlotNameById = new();
        private readonly Dictionary<int, string> _armorSlotNameById = new();
        private readonly Dictionary<int, string> _implantSlotNameById = new();
        private readonly Dictionary<int, string> _socialSlotNameById = new();
        private readonly Dictionary<ulong, string> _weaponSlotNameByBit = new();
        private readonly Dictionary<ulong, string> _armorSlotNameByBit = new();
        private readonly Dictionary<ulong, string> _implantSlotNameByBit = new();
        private readonly Dictionary<ulong, string> _canNameByBit = new();
        private readonly Dictionary<int, float> _itemSkillLockExpiresAt = new();
        private readonly Dictionary<int, float> _itemSkillLockDurations = new();
        private readonly Dictionary<int, long> _socialEquipped = new();
        private static readonly HashSet<int> IgnoredModifierStatIds = new()
        {
            0,   // None
            2,   // Mass
            7,   // State
            12,  // Mesh
            23,  // StaticInstance
            30,  // Can
            54,  // Level (quality level source, not a modifier)
            74,  // Value
            76,  // ItemClass
            79,  // Icon
            88,  // DefaultSlot
            209, // WeaponMesh
            210, // RechargeDelay
            211, // EquipDelay
            212, // MaxEnergy
            284, // Crit bonus (displayed under damage)
            285, // Max damage
            286, // Min damage
            298  // Equip slot bitmap
        };
        private bool _slotNamesLoaded;
        private CharacterRuntimeBridge _runtimeBridge;
        private AuthoritativeNetworkClient _networkClient;
        private CharacterRuntimeBridge _selectedTarget;
        private int _nextActiveProgramId = 1;
        private PendingNanoCastRequest _pendingNanoCastRequest;
        private int _localHealthCurrent;
        private int _localHealthMax;
        private int _localNanoCurrent;
        private int _localNanoMax;
        private float _nextRegenTickAt;
        private float _currentRegenIntervalSeconds = -1f;
        private float _healthRegenCarry;
        private float _nextAutoAttackAt;
        private float _pendingAutoAttackImpactAt = -1f;
        private float _currentAutoAttackRechargeEndAt = -1f;
        private int _lastConsumedAttackImpactCounter = -1;
        private float _burstReadyAt = -1f;
        private float _flingReadyAt = -1f;
        private float _nanoRegenCarry;
        private CharacterRuntimeBridge.CharacterSex _characterSex = CharacterRuntimeBridge.CharacterSex.Male;
        private bool _isUpdatingActivePrograms;

        public static PrototypeUiContext Active { get; private set; }

        public Character Character { get; private set; }
        public int SelectedEquipSlot { get; private set; }
        public long OpenBackpackId { get; private set; }
        public int CharacterLevel => Character?.Level?.Level ?? 1;
        public int CharacterBreedId => Character?.BreedId ?? 1;
        public int CharacterProfessionId => Character?.ProfessionId ?? 1;
        public int CharacterVisualProfessionId => GetCurrentVisualProfessionId();
        public CharacterRuntimeBridge.CharacterSex CharacterSex => CharacterBreedId == 4 ? CharacterRuntimeBridge.CharacterSex.Uni : _characterSex;
        public string CharacterHeadMeshKey => _runtimeBridge != null ? _runtimeBridge.DebugHeadMeshKey : string.Empty;
        public CharacterRuntimeBridge SelectedTarget => _selectedTarget;
        public IReadOnlyList<QuestDefinition> Quests => (IReadOnlyList<QuestDefinition>)(_questAuthoring.File?.Quests ?? new List<QuestDefinition>());
        public IReadOnlyDictionary<string, QuestTargetFamily> QuestTargetFamilies => _questTargetFamilies.FamiliesById;

        public string GetSelfDisplayName()
        {
            if (_runtimeBridge != null && !string.IsNullOrWhiteSpace(_runtimeBridge.DisplayNameOverride))
                return _runtimeBridge.DisplayNameOverride.Trim();

            if (!string.IsNullOrWhiteSpace(Character?.Name))
                return Character.Name.Trim();

            return "PrototypeCharacter";
        }

        public event Action StateChanged;
        public event Action<string> StatusChanged;
        private bool _stateChangedPending;
        private float _nextStateChangedEmitAt;
        private const float StateChangedEmitIntervalSeconds = 0.05f;

        public PrototypeUiContext(int maxVisibleBrowserItems, int defaultEquipSlot)
        {
            _maxVisibleBrowserItems = maxVisibleBrowserItems;
            SelectedEquipSlot = defaultEquipSlot;
            Active = this;
        }

        public void Initialize(Character existingCharacter = null, CharacterRuntimeBridge runtimeBridge = null)
        {
            AODataManager.EnsureInstance();
            _runtimeBridge = runtimeBridge;
            _networkClient = _runtimeBridge != null ? _runtimeBridge.GetComponent<AuthoritativeNetworkClient>() : null;
            if (_networkClient != null)
            {
                _networkClient.CharacterSettingsApplied += HandleAuthoritativeCharacterSettingsApplied;
                _networkClient.StatIncreaseApplied += HandleAuthoritativeStatIncreaseApplied;
                _networkClient.AuthoritativeStatsApplied += HandleAuthoritativeStatsApplied;
                _networkClient.EquipApplied += HandleAuthoritativeEquipApplied;
                _networkClient.UnequipApplied += HandleAuthoritativeUnequipApplied;
                _networkClient.AdminItemGranted += HandleAuthoritativeAdminItemGranted;
                _networkClient.ServerNoticeReceived += HandleServerNoticeReceived;
            }

            Character = existingCharacter ?? new Character(
                "PrototypeCharacter",
                new Profession { Name = "Prototype" },
                0,
                breedId: 1,
                professionId: 1);

            if (_runtimeBridge != null)
                _characterSex = _runtimeBridge.Sex;
            else if (Character.BreedId == 4)
                _characterSex = CharacterRuntimeBridge.CharacterSex.Uni;

            if (_runtimeBridge != null && string.IsNullOrWhiteSpace(_runtimeBridge.DebugHeadMeshKey))
                _runtimeBridge.DebugHeadMeshKey = DefaultHeadMeshKey;

            ApplyDefaultNonAbilityBaseStats();
            ApplyAbilityBaseStatsForCurrentBreed();
            ApplyVisualProfessionBaseline(Character.ProfessionId);
            Character.StatsContainer.Recalculate();
            _localHealthMax = ResolveMaxHealthForUi();
            _localHealthCurrent = ResolveCurrentHealthForUi(_localHealthMax);
            _localNanoMax = ResolveMaxNanoForUi();
            _localNanoCurrent = ResolveCurrentNanoForUi(_localNanoMax);

            foreach (var item in AODataManager.Instance.ItemInstances)
            {
                if (item?.Definition == null || string.IsNullOrWhiteSpace(item.Definition.Name))
                    continue;
                _allItems.Add(item);
            }

            _allItems.Sort((a, b) => string.CompareOrdinal(a.Definition.Name, b.Definition.Name));

            if (!Character.Inventory.Main.Slots.Any(slot => slot != null))
                SeedInitialInventory();

            string questLoad = _questAuthoring.Load();
            string familyLoad = _questTargetFamilies.Load();

            NotifyStateChanged();
            SetStatus($"Loaded {_allItems.Count} items. {questLoad} {familyLoad}");
        }

        public void RequestCharacterSettings(int level, int breedId, int professionId, CharacterRuntimeBridge.CharacterSex requestedSex, long xpToAdd)
        {
            if (_networkClient != null && _networkClient.IsConnected)
            {
                _networkClient.RequestCharacterSettings(level, breedId, professionId, (int)requestedSex, xpToAdd);
                SetStatus("Sent character settings request to AO.Server.");
                return;
            }

            ApplyCharacterSettings(level, breedId, professionId, requestedSex);
            if (xpToAdd > 0)
                AddExperience(xpToAdd);
        }

        public void RequestSkillIncreases(IReadOnlyDictionary<int, int> pendingByStatId)
        {
            if (pendingByStatId == null || pendingByStatId.Count == 0)
            {
                SetStatus("No pending skill changes.");
                return;
            }

            if (_networkClient != null && _networkClient.IsConnected)
            {
                int requests = 0;
                foreach (var pair in pendingByStatId)
                {
                    if (pair.Value <= 0)
                        continue;

                    string statName = GetStatName(pair.Key);
                    if (string.IsNullOrWhiteSpace(statName))
                        continue;

                    _networkClient.RequestStatIncrease(statName, pair.Value);
                    requests++;
                }

                SetStatus(requests > 0
                    ? $"Sent {requests} skill change request(s) to AO.Server."
                    : "No valid skill changes to send.");
                return;
            }

            int before = GetAvailableIp();
            foreach (var pair in pendingByStatId)
            {
                if (pair.Value <= 0)
                    continue;

                string statName = GetStatName(pair.Key);
                if (string.IsNullOrWhiteSpace(statName))
                    continue;

                Character?.TryIncreaseStat(statName, pair.Value);
            }

            int spent = before - GetAvailableIp();
            NotifyStateChanged();
            SetStatus($"Spent {spent} IP.");
        }

        private void SeedInitialInventory()
        {
            // Start clean for easier interaction testing: one backpack in slot 1, rest empty.
            var firstBackpack = _allItems.FirstOrDefault(i =>
                i?.Definition?.Name != null &&
                i.Definition.Name.ToLowerInvariant().Contains("backpack"));

            if (firstBackpack == null)
            {
                SetStatus("No backpack found in loaded data. Add items to inventory from browser.");
                return;
            }

            var core = AODataManager.Instance.GetCoreInstance(firstBackpack.InstanceId);
            if (core == null)
            {
                SetStatus("Backpack core instance missing.");
                return;
            }

            Character.Inventory.TrySetMainSlot(0, core);
            SetStatus($"Starter backpack loaded in inventory: {firstBackpack.Definition.Name}");
        }

        public AOGameServerSession ServerSession { get; set; }
        private bool _hasNativeInventory;
        private bool _hasNativeEquipment;
        private int _nativeMoveVersion;
        private bool _nativeMovePending;
        private bool HasNativeSession => _hasNativeInventory
            || (ServerSession != null && ServerSession.IsAuthenticated);

        public void ResetServerSnapshotHydration()
        {
            _hasNativeInventory = false;
            _hasNativeEquipment = false;
            _nativeMovePending = false;
            ++_nativeMoveVersion;
        }

        private async void SendNativeItemMove(ItemLocation source, ItemLocation destination)
        {
            if (_nativeMovePending) { SetStatus("Waiting for the previous item move confirmation."); return; }
            _nativeMovePending = true;
            int version = ++_nativeMoveVersion;
            SetStatus("Item move requested; waiting for server confirmation.");
            Debug.Log($"[Equipment] Native move requested: {source.Area}[{source.Index}] -> "
                + $"{destination.Area}[{destination.Index}].");
            try
            {
                await ServerSession.MoveItemAsync(source, destination);
                await System.Threading.Tasks.Task.Delay(15000);
                if (_nativeMovePending && version == _nativeMoveVersion)
                {
                    _nativeMovePending = false;
                    SetStatus("No item move confirmation received. Check server messages before trying again.");
                    Debug.LogWarning($"[Equipment] No confirmation for native move: "
                        + $"{source.Area}[{source.Index}] -> {destination.Area}[{destination.Index}].");
                }
            }
            catch (Exception exception)
            {
                if (version != _nativeMoveVersion) return;
                _nativeMovePending = false;
                SetStatus($"Item move failed: {exception.Message}");
            }
        }

        private static ItemLocation NativeWearLocation(int localSlot)
        {
            for (int placement = 1; placement < 0x40; placement++)
            {
                if ((placement & 15) == 0) continue;
                int mapped = placement >= 0x31 ? placement - 0x30 + SocialSlotOffset
                    : NormalizeServerWearSlot(placement);
                if (mapped == localSlot)
                    return new ItemLocation((ItemArea)(placement / 16), (placement & 15) - 1);
            }
            return null;
        }

        private bool TryNativeUnequip(int slot, SlotZone zone, int index, out string reason)
        {
            reason = string.Empty;
            var source = NativeWearLocation(slot);
            if (zone != SlotZone.Inventory || source == null || index < 0
                || index >= Character.Inventory.Main.Capacity || GetSlotItem(zone, index) != null
                || !(slot > SocialSlotOffset ? _socialEquipped.ContainsKey(slot) : GetEquipped().ContainsKey(slot)))
            {
                reason = "Choose an equipped item and an empty main inventory slot; backpack transfers are not connected yet.";
                SetStatus(reason);
                return false;
            }
            SendNativeItemMove(source, new ItemLocation(ItemArea.Inventory, index));
            return true;
        }

        private bool TryNativeEquip(long instanceId, int? preferredSlot, bool strictPreferredSlot)
        {
            var core = AODataManager.Instance.GetCoreInstance(instanceId);
            var data = AODataManager.Instance.GetItemInstance(instanceId);
            int index = -1;
            for (int i = 0; i < Character.Inventory.Main.Capacity; i++)
                if (Character.Inventory.Main.Slots[i]?.InstanceId == instanceId) { index = i; break; }
            if (index < 0 || core?.Definition == null || data?.Definition == null)
            {
                SetStatus("Equip requires an item in your main inventory.");
                return false;
            }
            var candidates = strictPreferredSlot && preferredSlot.HasValue
                ? new List<int> { preferredSlot.Value }
                : ResolveEquipSlotCandidates(core.Definition, data).ToList();
            if (!strictPreferredSlot && preferredSlot.HasValue) candidates.Insert(0, preferredSlot.Value);
            var destination = candidates.Select(NativeWearLocation).FirstOrDefault(location => location != null);
            if (destination == null) { SetStatus("No supported equipment slot found."); return false; }
            SendNativeItemMove(new ItemLocation(ItemArea.Inventory, index), destination);
            return true;
        }

        public void ApplyServerInventory(InventorySnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsMainInventory || Character?.Inventory == null)
                return;

            _hasNativeInventory = true;
            _nativeMovePending = false;
            ++_nativeMoveVersion;
            for (int slot = 0; slot < Character.Inventory.Main.Capacity; slot++)
                Character.Inventory.TryRemoveFromMain(slot, out _);
            Character.Inventory.Items.Clear();

            int loaded = 0;
            foreach (InventoryEntrySnapshot entry in snapshot.Entries)
            {
                if (entry == null)
                    continue;
                // AO's main inventory page is wire-addressed as slots 0x40..0x5D.
                int guiSlot = entry.Slot >= 0x40 ? entry.Slot - 0x40 : entry.Slot;
                if (guiSlot < 0 || guiSlot >= Character.Inventory.Main.Capacity)
                    continue;
                int aoid = entry.HighId > 0 ? entry.HighId : entry.LowId;
                // Resolve the exact server-supplied low/high template pair and QL
                // directly from the selected AO client's ResourceDatabase.
                try
                {
                    if (AOItemObjectResolver.TryResolve(
                        entry.LowId, entry.HighId, entry.Quality,
                        out AO.Data.Core.Item resolved))
                    {
                        AODataManager.Instance.RegisterRuntimeItem(
                            resolved, entry.LowId, entry.HighId);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Inventory] RDB item resolution failed for low={entry.LowId} "
                        + $"high={entry.HighId} ql={entry.Quality}: {exception.Message}");
                }
                var definition = AODataManager.Instance.GetCoreDefinition(aoid)
                    ?? AODataManager.Instance.GetCoreDefinition(entry.LowId);
                if (definition == null)
                {
                    Debug.LogWarning($"[Inventory] Unknown server item low={entry.LowId} high={entry.HighId} ql={entry.Quality}.");
                    continue;
                }
                long instanceId = entry.IdentityInstance != 0
                    ? entry.IdentityInstance
                    : ((long)aoid << 16) | (uint)guiSlot;
                int containerCapacity = AODataManager.Instance.GetCoreInstance(definition.AOID)?.ContainerCapacity ?? 0;
                AODataManager.Instance.RegisterRuntimeInstance(
                    definition, instanceId, entry.Quantity, containerCapacity);
                var item = new ItemInstance(definition, entry.Quantity, instanceId, containerCapacity);
                if (Character.Inventory.TrySetMainSlot(guiSlot, item))
                    loaded++;
            }
            OpenBackpackId = 0;
            NotifyStateChanged();
            SetStatus($"Server inventory synchronized: {loaded}/{snapshot.Capacity} slots.");
        }

        public void ApplyServerCharacterState(CharacterStateSnapshot snapshot)
        {
            if (snapshot == null || Character == null)
                return;

            // A stat delta retains the last FullCharacter slots. If initial UI
            // hydration missed the full snapshot, use those retained slots once
            // before treating subsequent packets as stats-only updates.
            if (snapshot.IsStatUpdateOnly && _hasNativeEquipment)
            {
                Character.ApplyAuthoritativeStats(snapshot.Stats,
                    statId => AODataManager.Instance?.GetStatName(statId));
                NotifyStateChanged();
                return;
            }

            var equipped = new Dictionary<int, long>();
            _socialEquipped.Clear();
            int resolvedCount = 0;
            foreach (InventoryEntrySnapshot entry in snapshot.Slots)
            {
                if (entry == null || entry.Slot < 0x01 || entry.Slot > 0x3f)
                    continue;
                int aoid = entry.HighId > 0 ? entry.HighId : entry.LowId;
                try
                {
                    if (AOItemObjectResolver.TryResolve(entry.LowId, entry.HighId,
                        entry.Quality, out AO.Data.Core.Item resolved))
                    {
                        AODataManager.Instance.RegisterRuntimeItem(
                            resolved, entry.LowId, entry.HighId);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Equipment] RDB resolution failed for slot 0x{entry.Slot:X2}: "
                        + exception.Message);
                }
                var definition = AODataManager.Instance.GetCoreDefinition(aoid)
                    ?? AODataManager.Instance.GetCoreDefinition(entry.LowId);
                if (definition == null)
                    continue;
                long instanceId = entry.IdentityInstance != 0
                    ? entry.IdentityInstance
                    : ((long)aoid << 16) | (uint)entry.Slot;
                int containerCapacity = AODataManager.Instance
                    .GetCoreInstance(definition.AOID)?.ContainerCapacity ?? 0;
                AODataManager.Instance.RegisterRuntimeInstance(
                    definition, instanceId, entry.Quantity, containerCapacity);
                if (entry.Slot >= 0x31)
                    _socialEquipped[entry.Slot - 0x30 + SocialSlotOffset] = instanceId;
                else
                    equipped[NormalizeServerWearSlot(entry.Slot)] = instanceId;
                resolvedCount++;
            }
            Character.ApplyAuthoritativeEquipment(equipped);
            _hasNativeEquipment = true;
            Character.ApplyAuthoritativeStats(snapshot.Stats,
                statId => AODataManager.Instance?.GetStatName(statId));
            ApplyServerUploadedPrograms(snapshot.UploadedNanoIds);
            NotifyStateChanged();
            Debug.Log($"[Equipment] Applied authoritative wear snapshot: "
                + $"wire={snapshot.Slots.Count}, resolved={resolvedCount}, gameplay={equipped.Count}, "
                + $"social={_socialEquipped.Count}; slots="
                + string.Join(",", equipped.Select(pair => $"{pair.Key}:{pair.Value}")) + ".");
            SetStatus($"Server character state synchronized: {resolvedCount} worn items, "
                + $"{_uploadedPrograms.Count} uploaded programs, {snapshot.Stats.Count} stats. "
                + $"IP={Character.AvailableIp}, HP={Character.StatsContainer.GetBaseStat(StatIds.Health)}/"
                + $"{Character.StatsContainer.GetBaseStat(StatIds.MaxHealth)}, "
                + $"Nano={Character.StatsContainer.GetBaseStat(StatIds.CurrentNano)}/"
                + $"{Character.StatsContainer.GetBaseStat(StatIds.MaxNanoEnergy)}.");
        }

        private static int NormalizeServerWearSlot(int placement)
        {
            if (placement >= 0x21 && placement <= 0x2f)
                return 100 + (placement - 0x20);
            if (placement >= 0x11 && placement <= 0x1f)
                return MapArmorSlotToLocal(placement - 0x10);
            if (placement >= 0x01 && placement <= 0x0f)
                return MapWeaponSlotToLocal(placement);
            return placement;
        }

        private void ApplyServerUploadedPrograms(IReadOnlyList<int> nanoIds)
        {
            _uploadedPrograms.Clear();
            _uploadedProgramIds.Clear();
            if (nanoIds == null)
                return;
            foreach (int nanoId in nanoIds)
            {
                if (nanoId <= 0 || !_uploadedProgramIds.Add(nanoId))
                    continue;
                UploadedNanoProgram uploaded = BuildUploadedProgram(nanoId, null, null);
                if (uploaded != null)
                    _uploadedPrograms.Add(uploaded);
            }
        }

        public IEnumerable<DataItemInstance> QueryItems(string token, string qlOperator = "=", int? qlValue = null)
        {
            IEnumerable<DataItemInstance> query = _allItems;
            if (!string.IsNullOrWhiteSpace(token))
            {
                token = token.Trim().ToLowerInvariant();
                query = query.Where(i => i.Definition.Name.ToLowerInvariant().Contains(token));
            }

            if (qlValue.HasValue)
            {
                int targetQl = Mathf.Max(0, qlValue.Value);
                query = query.Where(i => CompareByOperator(i?.Definition?.RequiredLevel ?? 0, targetQl, qlOperator));
            }

            return query.Take(_maxVisibleBrowserItems);
        }

        private static bool CompareByOperator(int value, int threshold, string op)
        {
            return op switch
            {
                "<" => value < threshold,
                ">" => value > threshold,
                "<=" => value <= threshold,
                ">=" => value >= threshold,
                _ => value == threshold
            };
        }

        public void SetEquipSlot(int slot)
        {
            SelectedEquipSlot = Mathf.Max(1, slot);
            SetStatus($"Equip slot set to {SelectedEquipSlot}.");
            NotifyStateChanged();
        }

        public void SetCharacterLevel(int level)
        {
            if (Character == null)
            {
                SetStatus("Character is not initialized.");
                return;
            }

            // Level-only updates should preserve already spent IP.
            Character.SetLevelAndRebuildIp(level, preserveSpent: true);
            SetStatus($"Character level set to {Character.Level.Level}.");
            NotifyStateChanged();
        }

        public void ApplyCharacterSettings(int level, int breedId, int professionId, CharacterRuntimeBridge.CharacterSex requestedSex)
        {
            if (Character == null)
            {
                SetStatus("Character is not initialized.");
                return;
            }

            int nextBreedId = breedId <= 0 ? 1 : breedId;
            int nextProfessionId = professionId <= 0 ? 1 : professionId;
            var nextSex = nextBreedId == 4
                ? CharacterRuntimeBridge.CharacterSex.Uni
                : (requestedSex == CharacterRuntimeBridge.CharacterSex.Female
                    ? CharacterRuntimeBridge.CharacterSex.Female
                    : CharacterRuntimeBridge.CharacterSex.Male);
            bool identityChanged = Character.BreedId != nextBreedId || Character.ProfessionId != nextProfessionId;
            bool sexChanged = CharacterSex != nextSex;

            var professionName = AODataManager.Instance.GetProfessionLookup()
                .FirstOrDefault(p => p.Key == nextProfessionId).Value ?? "Prototype";

            Character.SetIdentity(
                nextBreedId,
                nextProfessionId,
                new Profession { Name = professionName });

            ApplyVisualProfessionBaseline(nextProfessionId);

            _characterSex = nextSex;
            if (_runtimeBridge != null)
                _runtimeBridge.Sex = _characterSex;

            if (identityChanged || sexChanged)
            {
                ApplyDefaultNonAbilityBaseStats(forceReset: true);
                ApplyAbilityBaseStatsForCurrentBreed();
            }

            // Identity change (breed/profession/sex) is a full reset of spent IP.
            // Level-only change preserves spent IP.
            Character.SetLevelAndRebuildIp(level, preserveSpent: !(identityChanged || sexChanged));
            Character.StatsContainer.Recalculate();
            _localHealthMax = ResolveMaxHealthForUi();
            _localHealthCurrent = Mathf.Clamp(_localHealthCurrent, 0, _localHealthMax);
            if (_localHealthCurrent <= 0)
                _localHealthCurrent = ResolveCurrentHealthForUi(_localHealthMax);
            _localNanoMax = ResolveMaxNanoForUi();
            _localNanoCurrent = Mathf.Clamp(_localNanoCurrent, 0, _localNanoMax);
            if (_localNanoCurrent <= 0)
                _localNanoCurrent = ResolveCurrentNanoForUi(_localNanoMax);

            SetStatus($"Character updated: L{Character.Level.Level}, Breed={Character.BreedId}, Profession={Character.ProfessionId}, Sex={CharacterSex}.");
            NotifyStateChanged();
        }

        private void HandleAuthoritativeCharacterSettingsApplied(AuthoritativeNetworkClient.CharacterSettingsApproval approval)
        {
            var approvedSex = approval.Sex == 1
                ? CharacterRuntimeBridge.CharacterSex.Female
                : (approval.Sex == 2 ? CharacterRuntimeBridge.CharacterSex.Uni : CharacterRuntimeBridge.CharacterSex.Male);

            ApplyCharacterSettings(approval.Level, approval.BreedId, approval.ProfessionId, approvedSex);
            if (approval.Experience > 0)
                AddExperience(approval.Experience);

            if (!string.IsNullOrWhiteSpace(approval.Message))
                SetStatus(approval.Message);
            NotifyStateChanged();
        }

        private void HandleAuthoritativeStatIncreaseApplied(AuthoritativeNetworkClient.StatIncreaseApproval approval)
        {
            if (Character == null || string.IsNullOrWhiteSpace(approval.StatName) || approval.Amount <= 0)
                return;

            Character.TryIncreaseStat(approval.StatName, approval.Amount);

            if (!string.IsNullOrWhiteSpace(approval.Message))
                SetStatus($"{approval.Message} Remaining IP: {approval.AvailableIp}.");
            else
                SetStatus($"Approved {approval.Amount} point(s) in {approval.StatName}. Remaining IP: {approval.AvailableIp}.");

            NotifyStateChanged();
        }

        private void HandleAuthoritativeEquipApplied(AuthoritativeNetworkClient.EquipApproval approval)
        {
            if (approval.InstanceId <= 0 || approval.SlotId <= 0)
                return;

            int localSlot = approval.SlotId;
            var core = AODataManager.Instance.GetCoreInstance(approval.InstanceId);
            if (core?.Definition != null)
                localSlot = NormalizeEquipSlotForLocalStorage(core.Definition, approval.SlotId);

            ApplyEquipByInstanceIdLocal(approval.InstanceId, localSlot, strictPreferredSlot: true);
            if (!string.IsNullOrWhiteSpace(approval.Message))
                SetStatus(approval.Message);
        }

        private void HandleAuthoritativeUnequipApplied(AuthoritativeNetworkClient.UnequipApproval approval)
        {
            if (approval.SlotId <= 0)
                return;

            ApplyUnequipSlotLocal(ResolveLocalSlotIdFromServer(approval.SlotId));
            if (!string.IsNullOrWhiteSpace(approval.Message))
                SetStatus(approval.Message);
        }

        private void HandleServerNoticeReceived(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            SetStatus(message);
        }

        private void HandleAuthoritativeAdminItemGranted(AuthoritativeNetworkClient.AdminItemGrantedApproval approval)
        {
            if (approval.InstanceId <= 0)
            {
                if (!string.IsNullOrWhiteSpace(approval.Message))
                    SetStatus(approval.Message);
                return;
            }

            var dataItem = AODataManager.Instance?.GetItemInstance(approval.InstanceId);
            if (dataItem == null)
            {
                SetStatus(!string.IsNullOrWhiteSpace(approval.Message)
                    ? approval.Message
                    : $"Server granted item {approval.InstanceId}, but local AO data lookup failed.");
                return;
            }

            AddToInventoryLocal(dataItem);
            if (!string.IsNullOrWhiteSpace(approval.Message))
                SetStatus(approval.Message);
        }

        public void AddToInventory(DataItemInstance dataItem)
        {
            if (_networkClient != null && _networkClient.IsConnected)
            {
                if (dataItem?.Definition == null)
                {
                    SetStatus("Cannot add item: invalid data item.");
                    return;
                }

                _networkClient.RequestAdminGrantItem(dataItem.InstanceId);
                SetStatus($"Sent admin item grant request: {dataItem.Definition.Name}.");
                return;
            }

            AddToInventoryLocal(dataItem);
        }

        private void AddToInventoryLocal(DataItemInstance dataItem)
        {
            if (dataItem?.Definition == null)
            {
                SetStatus("Cannot add item: invalid data item.");
                return;
            }

            var core = CreateRuntimeInventoryItemFromData(dataItem);
            if (core == null)
            {
                SetStatus("Core item missing.");
                return;
            }

            if (!Character.Inventory.TryAddToMain(core))
            {
                SetStatus("Inventory full (30/30).");
                return;
            }

            SetStatus($"Added {dataItem.Definition.Name} to inventory.");
            NotifyStateChanged();
        }

        public void AddToOpenBackpack(DataItemInstance dataItem)
        {
            if (dataItem?.Definition == null)
            {
                SetStatus("Cannot add item to bag: invalid data item.");
                return;
            }

            if (OpenBackpackId == 0)
            {
                SetStatus("Open a backpack first.");
                return;
            }

            var core = CreateRuntimeInventoryItemFromData(dataItem);
            if (core == null)
            {
                SetStatus("Core item missing.");
                return;
            }

            if (core.IsContainer)
            {
                SetStatus("Cannot place a backpack/container inside another backpack.");
                return;
            }

            if (!Character.Inventory.TryGetContainer(OpenBackpackId, out var openContainer))
            {
                SetStatus("Open backpack container is unavailable.");
                return;
            }

            bool hasEmptySlot = openContainer.Slots.Any(slot => slot == null);
            if (!hasEmptySlot)
            {
                SetStatus("Backpack is full.");
                return;
            }

            if (!Character.Inventory.TryAddToContainer(OpenBackpackId, core))
            {
                SetStatus("Could not add item to backpack (full or backpack nesting blocked).");
                return;
            }

            SetStatus($"Added {dataItem.Definition.Name} to backpack.");
            NotifyStateChanged();
        }

        private ItemInstance CreateRuntimeInventoryItemFromData(DataItemInstance dataItem)
        {
            if (dataItem?.Definition == null || AODataManager.Instance == null)
                return null;

            var template = AODataManager.Instance.GetCoreInstance(dataItem.InstanceId);
            if (template?.Definition == null)
                return null;

            var raw = AODataManager.Instance.GetRawItemByAoid(dataItem.DefinitionId);
            int count = ResolveInitialItemCount(raw, template);
            return new ItemInstance(
                template.Definition,
                Mathf.Max(1, count),
                template.InstanceId,
                template.ContainerCapacity);
        }

        private int ResolveInitialItemCount(AO.Data.Core.Item raw, ItemInstance template)
        {
            int stackCount = Mathf.Max(0, GetRawStatValue(raw, StackCountStatId));
            int charges = Mathf.Max(0, GetRawStatValue(raw, ChargeCountStatId));
            int canFlags = Mathf.Max(0, GetRawStatValue(raw, CanFlagStatId));
            bool stackable = (canFlags & StackableCanBit) != 0;
            bool usable = (canFlags & UseCanBit) != 0;

            if (stackable && stackCount > 0)
                return stackCount;
            if (!stackable && usable && charges > 0)
                return charges;

            return Mathf.Max(1, template?.Quantity ?? 1);
        }

        public void SelectInventorySlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Character.Inventory.Main.Capacity)
            {
                SetStatus("Invalid inventory slot.");
                return;
            }

            var item = Character.Inventory.Main.Slots[slotIndex];
            if (item == null)
            {
                SetStatus($"Inventory slot {slotIndex + 1} is empty.");
                return;
            }

            if (item.IsContainer)
            {
                ToggleBackpack(item.InstanceId, item.Definition.Name);
                return;
            }

            if (TryUseItemFromZone(SlotZone.Inventory, slotIndex, item))
                return;

            if (TryUploadNanoProgramFromInstance(item.InstanceId, SlotZone.Inventory, slotIndex))
                return;

            TryEquipByInstanceId(
                item.InstanceId,
                preferredSlot: null,
                strictPreferredSlot: false,
                sourceZone: SlotZone.Inventory,
                sourceIndex: slotIndex);
        }

        public void SelectBackpackSlot(int slotIndex)
        {
            if (!TryGetOpenBackpackContainer(out var container, out _))
            {
                SetStatus("No backpack open.");
                return;
            }

            if (slotIndex < 0 || slotIndex >= container.Capacity)
            {
                SetStatus("Invalid backpack slot.");
                return;
            }

            var item = container.Slots[slotIndex];
            if (item == null)
            {
                SetStatus($"Backpack slot {slotIndex + 1} is empty.");
                return;
            }

            if (TryUseItemFromZone(SlotZone.Backpack, slotIndex, item))
                return;

            if (TryUploadNanoProgramFromInstance(item.InstanceId, SlotZone.Backpack, slotIndex))
                return;

            TryEquipByInstanceId(
                item.InstanceId,
                preferredSlot: null,
                strictPreferredSlot: false,
                sourceZone: SlotZone.Backpack,
                sourceIndex: slotIndex);
        }

        private bool TryUseItemFromZone(SlotZone zone, int slotIndex, ItemInstance item)
        {
            if (item == null || AODataManager.Instance == null)
                return false;

            var raw = AODataManager.Instance.GetRawItemByAoid(item.Definition?.AOID ?? (int)item.InstanceId);
            if (raw == null)
                return false;
            if (IsLikelyNanoUploadItem(raw))
                return false;

            int canFlags = Mathf.Max(0, GetRawStatValue(raw, CanFlagStatId));
            if ((canFlags & UseCanBit) == 0)
                return false;

            if (HasNativeSession) { SetStatus("Server item use is not connected yet."); return true; }

            if (!CanUseItemNow(raw, out string blockReason))
            {
                if (!string.IsNullOrWhiteSpace(blockReason))
                    SetStatus(blockReason);
                return true;
            }

            bool changed = ApplyItemUseEffects(raw, out string effectSummary);
            ApplyItemSkillLocksFromSpells(raw);

            item.Quantity = Mathf.Max(0, item.Quantity - 1);
            if (item.Quantity <= 0)
            {
                if (RemoveFromZone(zone, slotIndex, out var removed) && removed != null)
                    Character.Inventory.Items.Remove(removed);
            }

            NotifyStateChanged();
            string itemName = item.Definition?.Name ?? item.InstanceId.ToString();
            string suffix = changed && !string.IsNullOrWhiteSpace(effectSummary) ? $" ({effectSummary})" : string.Empty;
            SetStatus($"Used {itemName}.{suffix}");
            return true;
        }

        private bool CanUseItemNow(AO.Data.Core.Item raw, out string reason)
        {
            reason = string.Empty;
            if (raw != null && (raw.AOID == 116628 || raw.AOID == 116629))
            {
                // Startup Treatment Laboratory / Startup First-Aid Kit:
                // allow use in prototype authoritative mode even when criteria data is incomplete/inconsistent.
                return true;
            }

            if (RequiresSittingForUse(raw) && !IsCurrentlySitting())
            {
                reason = "Use blocked: you must be sitting.";
                return false;
            }

            if (raw?.ActionData?.Actions == null)
                return true;

            for (int i = 0; i < raw.ActionData.Actions.Count; i++)
            {
                var action = raw.ActionData.Actions[i];
                if (action == null || action.Action != 3)
                    continue;

                if (action.Criteria != null)
                {
                    for (int c = 0; c < action.Criteria.Count; c++)
                    {
                        var criterion = action.Criteria[c];
                        if (criterion == null || criterion.Value1 <= 0)
                            continue;
                        if (IsSkillLocked(criterion.Value1, out float remaining))
                        {
                            string statName = ResolveRequirementStatName(criterion.Value1);
                            if (string.IsNullOrWhiteSpace(statName))
                                statName = $"Stat {criterion.Value1}";
                            reason = $"Use blocked: {statName} is locked for {Mathf.CeilToInt(remaining)}s.";
                            return false;
                        }
                    }
                }

                if (!AreCriteriaMet(action.Criteria))
                {
                    reason = "Use blocked: item criteria not met.";
                    return false;
                }
            }

            return true;
        }

        private bool RequiresSittingForUse(AO.Data.Core.Item raw)
        {
            int waitState = Mathf.Max(0, GetRawStatValue(raw, WaitStateStatId));
            if (waitState >= SittingWaitStateValue)
                return true;

            if (raw?.ActionData?.Actions == null)
                return false;

            for (int i = 0; i < raw.ActionData.Actions.Count; i++)
            {
                var action = raw.ActionData.Actions[i];
                if (action?.Criteria == null)
                    continue;

                for (int c = 0; c < action.Criteria.Count; c++)
                {
                    var criterion = action.Criteria[c];
                    if (criterion == null || criterion.Value1 != WaitStateStatId)
                        continue;

                    // AO startup treatment items commonly use WaitState=2 in criteria.
                    if (criterion.Value2 >= SittingWaitStateValue)
                        return true;
                }
            }

            return false;
        }

        private bool ApplyItemUseEffects(AO.Data.Core.Item raw, out string summary)
        {
            summary = string.Empty;
            if (raw?.SpellData == null)
                return false;

            bool changed = false;
            int healthDeltaApplied = 0;
            int nanoDeltaApplied = 0;
            var parts = new List<string>();
            foreach (var group in raw.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    // Consumables and nanos may encode self-effects as target 2 or 3.
                    if (spell == null || (spell.Target != 2 && spell.Target != 3) || spell.Stat <= 0)
                        continue;
                    if (!AreSpellCriteriaMet(spell))
                        continue;

                    int delta = ResolveSpellAmount(spell);
                    if (delta == 0)
                        continue;

                    if (ApplyResourceDelta(spell.Stat, delta))
                    {
                        changed = true;
                        if (spell.Stat == StatIds.Health || spell.Stat == StatIds.RemainingHealth)
                            healthDeltaApplied += delta;
                        else if (spell.Stat == StatIds.CurrentNano || spell.Stat == StatIds.NanoPool)
                            nanoDeltaApplied += delta;

                        string statName = AODataManager.Instance.GetStatName(spell.Stat);
                        if (string.IsNullOrWhiteSpace(statName))
                            statName = $"Stat {spell.Stat}";
                        parts.Add($"{statName} {delta:+#;-#;0}");
                    }
                }
            }

            if (changed && _networkClient != null && _networkClient.IsConnected)
                _networkClient.ApplyLocalAuthoritativeResourceDelta(healthDeltaApplied, nanoDeltaApplied);

            if (parts.Count > 0)
                summary = string.Join(", ", parts);
            return changed;
        }

        private void ApplyItemSkillLocksFromSpells(AO.Data.Core.Item raw)
        {
            if (raw?.SpellData == null)
                return;

            foreach (var group in raw.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null)
                        continue;

                    string text = spell.SpellDescription ?? spell.SpellFormat ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var match = Regex.Match(text, @"Lock\s+skill\s+([A-Za-z0-9_]+)\s+for\s+(\d+)", RegexOptions.IgnoreCase);
                    if (!match.Success || match.Groups.Count < 3)
                        continue;

                    string skillToken = match.Groups[1].Value?.Trim() ?? string.Empty;
                    if (!int.TryParse(match.Groups[2].Value, out int seconds) || seconds <= 0)
                        continue;

                    int skillId = ResolveStatIdFromToken(skillToken);
                    if (skillId <= 0)
                        continue;

                    _itemSkillLockExpiresAt[skillId] = Time.unscaledTime + seconds;
                    _itemSkillLockDurations[skillId] = Mathf.Max(
                        seconds,
                        _itemSkillLockDurations.TryGetValue(skillId, out float existing) ? existing : 0f);
                }
            }
        }

        private int ResolveStatIdFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return 0;

            string normalized = new string(token
                .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-')
                .Select(char.ToLowerInvariant)
                .ToArray());

            var known = AODataManager.Instance.GetKnownSkillStatIds();
            foreach (int statId in known)
            {
                string name = AODataManager.Instance.GetStatName(statId);
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string nameNorm = new string(name
                    .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-')
                    .Select(char.ToLowerInvariant)
                    .ToArray());
                if (string.Equals(nameNorm, normalized, StringComparison.OrdinalIgnoreCase))
                    return statId;
            }

            return 0;
        }

        private bool IsSkillLocked(int statId, out float remainingSeconds)
        {
            remainingSeconds = 0f;
            if (statId <= 0)
                return false;

            if (!_itemSkillLockExpiresAt.TryGetValue(statId, out float until))
                return false;

            remainingSeconds = until - Time.unscaledTime;
            if (remainingSeconds <= 0f)
            {
                _itemSkillLockExpiresAt.Remove(statId);
                remainingSeconds = 0f;
                return false;
            }

            return true;
        }

        public bool TryGetItemCooldown(long instanceId, out float remainingSeconds, out float totalSeconds)
        {
            remainingSeconds = 0f;
            totalSeconds = 0f;
            if (instanceId == 0 || AODataManager.Instance == null)
                return false;

            var dataItem = AODataManager.Instance.GetItemInstance(instanceId);
            if (dataItem?.Definition == null)
                return false;

            var raw = AODataManager.Instance.GetRawItemByAoid(dataItem.DefinitionId);
            if (raw?.ActionData?.Actions == null)
                return false;

            float bestRemaining = 0f;
            float bestTotal = 0f;
            for (int i = 0; i < raw.ActionData.Actions.Count; i++)
            {
                var action = raw.ActionData.Actions[i];
                if (action?.Criteria == null)
                    continue;

                for (int c = 0; c < action.Criteria.Count; c++)
                {
                    var criterion = action.Criteria[c];
                    if (criterion == null || criterion.Value1 <= 0 || criterion.Operator == 4)
                        continue;
                    if (criterion.Value1 == WaitStateStatId)
                        continue;

                    if (!IsSkillLocked(criterion.Value1, out float remaining) || remaining <= 0f)
                        continue;

                    float total = remaining;
                    if (_itemSkillLockDurations.TryGetValue(criterion.Value1, out float knownTotal) && knownTotal > 0f)
                        total = knownTotal;

                    if (remaining > bestRemaining)
                    {
                        bestRemaining = remaining;
                        bestTotal = Mathf.Max(total, remaining);
                    }
                }
            }

            if (bestRemaining <= 0f)
                return false;

            remainingSeconds = bestRemaining;
            totalSeconds = Mathf.Max(bestTotal, bestRemaining);
            return true;
        }

        public IReadOnlyList<UploadedNanoProgram> GetUploadedPrograms()
        {
            return _uploadedPrograms;
        }

        public IReadOnlyList<ActiveNanoProgram> GetActivePrograms()
        {
            UpdateActiveProgramsInternal();
            return _activePrograms;
        }

        public int GetProgramNcuCost(int nanoId)
        {
            int primary = Mathf.Max(0, GetProgramStatValueAcrossChain(nanoId, NcuCostStatIdPrimary));
            if (primary > 0)
                return primary;

            int legacy = Mathf.Max(0, GetProgramStatValueAcrossChain(nanoId, NcuCostStatId));
            if (legacy > 0)
                return legacy;

            return Mathf.Max(0, _uploadedPrograms.FirstOrDefault(p => p != null && p.NanoId == nanoId)?.FallbackNcuCost ?? 0);
        }

        public int GetProgramNanoCost(int nanoId)
        {
            return Mathf.Max(0, GetProgramStatValueAcrossChain(nanoId, NanoCostStatId));
        }

        public int GetProgramDurationSeconds(int nanoId)
        {
            int durationRaw = Mathf.Max(0, GetProgramStatValueAcrossChain(nanoId, DurationStatId));
            if (durationRaw <= 0)
                return Mathf.Max(0, _uploadedPrograms.FirstOrDefault(p => p != null && p.NanoId == nanoId)?.FallbackDurationSeconds ?? 0);

            // AO durations are encoded in centiseconds.
            return Mathf.Max(1, Mathf.CeilToInt(durationRaw / 100f));
        }

        public float GetProgramAttackSeconds(int nanoId)
        {
            var rawNano = ResolveRawNanoByProgramId(nanoId);
            int rawAttack = GetRawStatValue(rawNano, AttackDelayStatId);
            return Mathf.Clamp(ConvertAttackDelayToSeconds(rawAttack), 0.05f, 10f);
        }

        public float GetProgramRechargeSeconds(int nanoId)
        {
            var rawNano = ResolveRawNanoByProgramId(nanoId);
            int rawRecharge = GetRawStatValue(rawNano, RechargeDelayStatId);
            return Mathf.Clamp(ConvertRechargeDelayToSeconds(rawRecharge), 0.05f, 10f);
        }

        public int GetCurrentNcuUsed()
        {
            UpdateActiveProgramsInternal();
            int used = 0;
            for (int i = 0; i < _activePrograms.Count; i++)
                used += Mathf.Max(0, _activePrograms[i].NcuCost);
            return used;
        }

        public int GetCurrentNcuCapacity()
        {
            int fromStatsFinal = Character?.StatsContainer?.GetFinalStat(MaxNcuStatId) ?? 0;
            int fromStatsModified = Character?.StatsContainer?.GetModifiedStat(MaxNcuStatId) ?? 0;
            int fromEquipped = Mathf.Max(0, GetEquippedItemModifierSum(MaxNcuStatId));
            int fromPrograms = GetActiveProgramNcuCapacityBonus();
            int explicitCapacity = Mathf.Max(0, Mathf.Max(fromStatsFinal, fromStatsModified));
            int fallbackCapacity = DefaultMaxNcuCapacity + fromEquipped;
            int baseCapacity = explicitCapacity > 0 ? Mathf.Max(explicitCapacity, fallbackCapacity) : fallbackCapacity;
            return Mathf.Max(1, baseCapacity + fromPrograms);
        }

        public string GetProgramSchoolTabName(int nanoId)
        {
            var rawNano = ResolveRawNanoByProgramId(nanoId);
            int schoolRaw = Mathf.Max(0, GetRawStatValue(rawNano, NanoSchoolStatId));
            string resolved = ResolveNanoSchoolTabName(schoolRaw);
            if (!string.Equals(resolved, "All", StringComparison.OrdinalIgnoreCase))
                return resolved;

            return _uploadedPrograms.FirstOrDefault(p => p != null && p.NanoId == nanoId)?.SchoolTab ?? "All";
        }

        public int GetCurrentNcuFree()
        {
            return Mathf.Max(0, GetCurrentNcuCapacity() - GetCurrentNcuUsed());
        }

        public int GetCurrentCredits()
        {
            return Mathf.Max(0, Character?.StatsContainer?.GetBaseStat(CreditsStatId) ?? 0);
        }

        private bool CanApplyProgramNcu(int nanoId, out int ncuCost, out int durationSeconds, out string failReason)
        {
            failReason = string.Empty;
            ncuCost = 0;
            durationSeconds = 0;

            ncuCost = GetProgramNcuCost(nanoId);
            durationSeconds = GetProgramDurationSeconds(nanoId);

            // Instant effects are not stored in NCU and should not consume NCU capacity.
            if (durationSeconds <= 0)
                return true;

            int existingIndex = _activePrograms.FindIndex(a => a != null && a.NanoId == nanoId);
            int refundedNcu = existingIndex >= 0 ? Mathf.Max(0, _activePrograms[existingIndex].NcuCost) : 0;
            int availableNcu = GetCurrentNcuFree();
            int effectiveAvailable = availableNcu + refundedNcu;
            if (ncuCost > effectiveAvailable)
            {
                failReason = $"Cast failed: not enough NCU ({GetCurrentNcuUsed()}/{GetCurrentNcuCapacity()} used, needs {ncuCost}).";
                return false;
            }

            return true;
        }

        public int GetRemainingSecondsForActiveProgram(int activeId)
        {
            UpdateActiveProgramsInternal();
            for (int i = 0; i < _activePrograms.Count; i++)
            {
                var active = _activePrograms[i];
                if (active.ActiveId != activeId)
                    continue;

                float remaining = Mathf.Max(0f, (active.StartedAt + Mathf.Max(0f, active.DurationSeconds)) - Time.unscaledTime);
                return Mathf.CeilToInt(remaining);
            }

            return 0;
        }

        public bool TickActivePrograms()
        {
            return UpdateActiveProgramsInternal();
        }

        public bool TrySetSelectedTarget(CharacterRuntimeBridge target)
        {
            return TrySetSelectedTarget(target, evaluateQuestSelection: true, publishStatus: true, notifyState: true);
        }

        public bool TrySetSelectedTarget(CharacterRuntimeBridge target, bool evaluateQuestSelection, bool publishStatus)
        {
            return TrySetSelectedTarget(target, evaluateQuestSelection, publishStatus, notifyState: true);
        }

        public bool TrySetSelectedTarget(CharacterRuntimeBridge target, bool evaluateQuestSelection, bool publishStatus, bool notifyState)
        {
            if (target == null)
            {
                if (_selectedTarget == null)
                    return false;

                _selectedTarget = null;
                if (publishStatus)
                    SetStatus("Target cleared.");
                if (notifyState)
                    NotifyStateChanged();
                return true;
            }

            if (_selectedTarget == target)
                return false;

            _selectedTarget = target;
            if (publishStatus)
                SetStatus($"Target selected: {ResolveTargetName(target)}.");
            if (evaluateQuestSelection)
                EvaluateTargetSelectionForQuestProgress(target);
            if (notifyState)
                NotifyStateChanged();
            return true;
        }

        public bool RequestProgramCast(int nanoId)
        {
            var uploaded = _uploadedPrograms.FirstOrDefault(p => p != null && p.NanoId == nanoId);
            if (uploaded == null)
            {
                SetStatus("Cast failed: program not uploaded.");
                return false;
            }

            if (_selectedTarget == null)
            {
                SetStatus("Cast failed: no target selected.");
                return false;
            }

            if (_runtimeBridge != null && _selectedTarget != _runtimeBridge)
            {
                SetStatus("Cast failed: only self-cast is supported in this prototype.");
                return false;
            }

            var rawNano = ResolveRawNanoByProgramId(nanoId);
            if (!ValidateNanoCastRequirements(rawNano, out string castFailReason, out var castFailures))
            {
                SetStatus(BuildUploadBlockedStatusMessage(castFailReason, castFailures));
                return false;
            }

            int nanoCost = GetProgramNanoCost(nanoId);
            int currentNano = GetCurrentNanoValue();
            if (nanoCost > currentNano)
            {
                SetStatus($"Cast failed: not enough nano ({currentNano}/{GetMaxNanoValue()}, needs {nanoCost}).");
                return false;
            }

            if (!CanApplyProgramNcu(nanoId, out int _, out int _, out string ncuFail))
            {
                SetStatus(ncuFail);
                return false;
            }

            _pendingNanoCastRequest = new PendingNanoCastRequest
            {
                NanoId = nanoId,
                Name = uploaded.Name ?? $"Nano {nanoId}",
                AttackSeconds = GetProgramAttackSeconds(nanoId),
                RechargeSeconds = GetProgramRechargeSeconds(nanoId),
                NanoCost = nanoCost
            };

            NotifyStateChanged();
            return true;
        }

        public bool TryConsumePendingProgramCastRequest(out PendingNanoCastRequest request)
        {
            request = _pendingNanoCastRequest;
            if (request == null)
                return false;

            _pendingNanoCastRequest = null;
            return true;
        }

        public void CancelPendingProgramCastRequest()
        {
            _pendingNanoCastRequest = null;
        }

        public bool TryCastUploadedProgram(int nanoId)
        {
            var uploaded = _uploadedPrograms.FirstOrDefault(p => p != null && p.NanoId == nanoId);
            if (uploaded == null)
            {
                SetStatus("Cast failed: program not uploaded.");
                return false;
            }

            if (_selectedTarget == null)
            {
                SetStatus("Cast failed: no target selected.");
                return false;
            }

            if (_runtimeBridge != null && _selectedTarget != _runtimeBridge)
            {
                SetStatus("Cast failed: only self-cast is supported in this prototype.");
                return false;
            }

            UpdateActiveProgramsInternal();

            var rawNano = ResolveRawNanoByProgramId(uploaded.NanoId);
            if (!ValidateNanoCastRequirements(rawNano, out string castFailReason, out var castFailures))
            {
                SetStatus(BuildUploadBlockedStatusMessage(castFailReason, castFailures));
                return false;
            }
            int ncuCost = GetProgramNcuCost(uploaded.NanoId);
            int durationSeconds = GetProgramDurationSeconds(uploaded.NanoId);
            int existingIndex = _activePrograms.FindIndex(a => a != null && a.NanoId == uploaded.NanoId);
            if (!CanApplyProgramNcu(uploaded.NanoId, out ncuCost, out durationSeconds, out string ncuFail))
            {
                SetStatus(ncuFail);
                return false;
            }

            var nanoChain = ResolveNanoExecutionChain(rawNano);
            var modifiers = new Dictionary<int, int>();
            var periodicEffects = new List<PeriodicNanoEffect>();
            int rawNcuBonus = 0;
            int? visualProfessionOverrideId = null;
            for (int i = 0; i < nanoChain.Count; i++)
            {
                var nanoPart = nanoChain[i];
                MergeStatModifiers(modifiers, ExtractNanoStatModifiers(nanoPart));
                rawNcuBonus += ResolveRawNanoModifierSum(nanoPart, MaxNcuStatId);
                periodicEffects.AddRange(ExtractPeriodicNanoEffects(nanoPart, Time.unscaledTime));
                if (!visualProfessionOverrideId.HasValue)
                    visualProfessionOverrideId = ResolveVisualProfessionOverrideId(nanoPart);
                ApplyInstantNanoEffects(nanoPart, durationSeconds <= 0);
            }

            if (rawNcuBonus != 0 && !modifiers.ContainsKey(MaxNcuStatId))
                modifiers[MaxNcuStatId] = rawNcuBonus;
            if (durationSeconds > 0)
            {
                // Refresh same nano by replacing any existing copy.
                if (existingIndex >= 0)
                    _activePrograms.RemoveAt(existingIndex);
                else
                    _activePrograms.RemoveAll(a => a != null && a.NanoId == uploaded.NanoId);

                _activePrograms.Add(new ActiveNanoProgram
                {
                    ActiveId = _nextActiveProgramId++,
                    NanoId = uploaded.NanoId,
                    RawNanoAoid = rawNano?.AOID ?? uploaded.RawNanoAoid,
                    Name = uploaded.Name ?? $"Nano {uploaded.NanoId}",
                    Description = uploaded.Description ?? string.Empty,
                    IconId = uploaded.IconId,
                    NcuCost = ncuCost,
                    NanoCost = GetProgramNanoCost(uploaded.NanoId),
                    StartedAt = Time.unscaledTime,
                    DurationSeconds = durationSeconds,
                    VisualProfessionOverrideId = visualProfessionOverrideId,
                    StatModifiers = modifiers,
                    PeriodicEffects = periodicEffects
                });
            }

            string castTargetName = ResolveTargetName(_selectedTarget);
            SetStatus($"Cast {uploaded.Name} on {castTargetName}.");
            NotifyStateChanged();
            return true;
        }

        public bool TrySpendNanoForProgram(int nanoId, out int nanoCost)
        {
            nanoCost = Mathf.Max(0, GetProgramNanoCost(nanoId));
            if (nanoCost <= 0)
                return true;

            int current = GetCurrentNanoValue();
            if (current < nanoCost)
            {
                SetStatus($"Cast failed: not enough nano ({current}/{GetMaxNanoValue()}, needs {nanoCost}).");
                return false;
            }

            _localNanoCurrent = Mathf.Clamp(current - nanoCost, 0, GetMaxNanoValue());
            NotifyStateChanged();
            return true;
        }

        public int GetCurrentHealthValue()
        {
            if (TryGetAuthoritativeStats(out int authoritativeHealth, out int authoritativeMaxHealth, out _, out _, out _))
            {
                int max = Mathf.Max(1, authoritativeMaxHealth + GetActiveProgramModifierSum(MaxHealthStatId));
                return Mathf.Clamp(authoritativeHealth, 0, max);
            }

            int serverStat = Character?.StatsContainer?.GetBaseStat(StatIds.Health) ?? 0;
            if (Character?.StatsContainer?.HasBaseStat(StatIds.Health) == true)
                return Mathf.Clamp(serverStat, 0, GetMaxHealthValue());

            SyncLocalResourcePools();
            return Mathf.Clamp(_localHealthCurrent, 0, Mathf.Max(1, _localHealthMax));
        }

        public int GetMaxHealthValue()
        {
            if (TryGetAuthoritativeStats(out _, out int authoritativeMaxHealth, out _, out _, out _))
                return Mathf.Max(1, authoritativeMaxHealth + GetActiveProgramModifierSum(MaxHealthStatId));

            SyncLocalResourcePools();
            return Mathf.Max(1, _localHealthMax);
        }

        public int GetCurrentNanoValue()
        {
            if (TryGetAuthoritativeStats(out _, out _, out int authoritativeNano, out int authoritativeMaxNano, out _))
            {
                int max = Mathf.Max(1, authoritativeMaxNano + GetActiveProgramModifierSum(MaxNanoPoolStatId));
                return Mathf.Clamp(authoritativeNano, 0, max);
            }

            int serverStat = Character?.StatsContainer?.GetBaseStat(StatIds.CurrentNano) ?? 0;
            if (Character?.StatsContainer?.HasBaseStat(StatIds.CurrentNano) == true)
                return Mathf.Clamp(serverStat, 0, GetMaxNanoValue());

            SyncLocalResourcePools();
            return Mathf.Clamp(_localNanoCurrent, 0, Mathf.Max(1, _localNanoMax));
        }

        public int GetMaxNanoValue()
        {
            if (TryGetAuthoritativeStats(out _, out _, out _, out int authoritativeMaxNano, out _))
                return Mathf.Max(1, authoritativeMaxNano + GetActiveProgramModifierSum(MaxNanoPoolStatId));

            SyncLocalResourcePools();
            return Mathf.Max(1, _localNanoMax);
        }

        public bool TickRegeneration(bool isSitting)
        {
            UpdateActiveProgramsInternal();
            if (TryGetAuthoritativeStats(out _, out _, out _, out _, out _))
                return false;

            SyncLocalResourcePools();
            float intervalSeconds = isSitting ? 10f : 20f;
            if (!Mathf.Approximately(_currentRegenIntervalSeconds, intervalSeconds))
            {
                _currentRegenIntervalSeconds = intervalSeconds;
                _nextRegenTickAt = Time.unscaledTime + intervalSeconds;
                return false;
            }

            if (_nextRegenTickAt <= 0f)
            {
                _nextRegenTickAt = Time.unscaledTime + intervalSeconds;
                return false;
            }

            if (Time.unscaledTime < _nextRegenTickAt)
                return false;

            bool changed = false;
            int safety = 0;
            while (Time.unscaledTime >= _nextRegenTickAt && safety < 5)
            {
                if (ApplyRegenTick(isSitting))
                    changed = true;
                _nextRegenTickAt += intervalSeconds;
                safety++;
            }

            if (changed)
                NotifyStateChanged();

            return changed;
        }

        public int GetBaseStatValue(int statId)
        {
            return Character?.StatsContainer?.GetBaseStat(statId) ?? 0;
        }

        public int GetModifiedStatValue(int statId)
        {
            if (statId == HealDeltaStatId || statId == NanoDeltaStatId)
                return ResolveDynamicDeltaValue(statId, IsCurrentlySitting());

            int baseModified = Character?.StatsContainer?.GetModifiedStat(statId) ?? 0;
            return baseModified + GetActiveProgramModifierSum(statId);
        }

        public int GetActiveProgramModifierValue(int statId)
        {
            return GetActiveProgramModifierSum(statId);
        }

        public string GetCurrentVisualProfessionName()
        {
            int visualId = GetCurrentVisualProfessionId();
            string name = AODataManager.Instance.GetProfessionLookup()
                .FirstOrDefault(p => p.Key == visualId).Value;

            if (string.IsNullOrWhiteSpace(name))
                name = visualId.ToString();

            return $"{visualId}: {name}";
        }

        public bool TryCancelActiveProgram(int activeId)
        {
            UpdateActiveProgramsInternal();
            int index = _activePrograms.FindIndex(a => a != null && a.ActiveId == activeId);
            if (index < 0)
                return false;

            string name = _activePrograms[index].Name;
            _activePrograms.RemoveAt(index);
            SetStatus($"Cancelled {name}.");
            NotifyStateChanged();
            return true;
        }

        public void TryOpenBackpackFromEquipped(long instanceId)
        {
            if (instanceId == 0)
                return;

            var item = AODataManager.Instance.GetCoreInstance(instanceId);
            if (item == null || !item.IsContainer)
                return;

            ToggleBackpack(instanceId, item.Definition?.Name);
        }

        public void CloseOpenBackpack()
        {
            if (OpenBackpackId == 0)
                return;

            OpenBackpackId = 0;
            SetStatus("Closed backpack.");
            NotifyStateChanged();
        }

        public ItemInstance GetSlotItem(SlotZone zone, int slotIndex)
        {
            if (Character == null || slotIndex < 0)
                return null;

            if (zone == SlotZone.Inventory)
            {
                if (slotIndex >= Character.Inventory.Main.Capacity) return null;
                return Character.Inventory.Main.Slots[slotIndex];
            }

            if (!TryGetOpenBackpackContainer(out var container, out _))
                return null;
            if (slotIndex >= container.Capacity) return null;
            return container.Slots[slotIndex];
        }

        public bool TryDeleteItemFromZone(SlotZone zone, int slotIndex)
        {
            if (HasNativeSession) { SetStatus("Server item deletion is not connected yet."); return false; }
            if (Character == null || slotIndex < 0)
                return false;
            if (_networkClient != null && _networkClient.IsConnected)
            {
                SetStatus("Delete blocked: AO.Server is authoritative while connected.");
                return false;
            }

            if (!RemoveFromZone(zone, slotIndex, out var removed) || removed == null)
                return false;

            Character.Inventory.Items.Remove(removed);
            string name = AODataManager.Instance.GetItemInstance(removed.InstanceId)?.Definition?.Name
                ?? removed.Definition?.Name
                ?? removed.InstanceId.ToString();
            SetStatus($"Deleted {name}.");
            NotifyStateChanged();
            return true;
        }

        public bool TryMoveItem(SlotZone fromZone, int fromIndex, SlotZone toZone, int toIndex, out string reason)
        {
            if (HasNativeSession)
            {
                reason = string.Empty;
                if (fromZone != SlotZone.Inventory || toZone != SlotZone.Inventory
                    || fromIndex < 0 || toIndex < 0 || fromIndex >= Character.Inventory.Main.Capacity
                    || toIndex >= Character.Inventory.Main.Capacity || fromIndex == toIndex
                    || GetSlotItem(fromZone, fromIndex) == null || GetSlotItem(toZone, toIndex) != null)
                {
                    reason = "Choose an item and an empty main inventory slot; stacking and backpack transfers are not connected yet.";
                    SetStatus(reason);
                    return false;
                }
                SendNativeItemMove(new ItemLocation(ItemArea.Inventory, fromIndex),
                    new ItemLocation(ItemArea.Inventory, toIndex));
                return true;
            }
            reason = string.Empty;

            if (fromZone == toZone && fromIndex == toIndex)
            {
                reason = "Same slot.";
                SetStatus(reason);
                return false;
            }

            var movingItem = GetSlotItem(fromZone, fromIndex);
            if (movingItem == null)
            {
                reason = "Source slot is empty.";
                SetStatus(reason);
                return false;
            }

            var targetItem = GetSlotItem(toZone, toIndex);
            if (targetItem != null)
            {
                if (!CanStackTogether(movingItem, targetItem))
                {
                    reason = "Target slot is occupied (items are not stack-compatible).";
                    SetStatus(reason);
                    return false;
                }
            }

            if (toZone == SlotZone.Backpack && movingItem.IsContainer)
            {
                reason = "Backpacks cannot be placed in backpacks.";
                SetStatus(reason);
                return false;
            }

            if (!RemoveFromZone(fromZone, fromIndex, out var removed))
            {
                reason = "Failed to remove item from source slot.";
                SetStatus(reason);
                return false;
            }

            if (targetItem != null && CanStackTogether(removed, targetItem))
            {
                targetItem.Quantity += Mathf.Max(1, removed.Quantity);
                Character.Inventory.Items.Remove(removed);
                var stackedName = AODataManager.Instance.GetItemInstance(targetItem.InstanceId)?.Definition?.Name ?? targetItem.Definition?.Name ?? targetItem.InstanceId.ToString();
                SetStatus($"Stacked {stackedName} (x{targetItem.Quantity}).");
                NotifyStateChanged();
                return true;
            }

            if (TrySetToZone(toZone, toIndex, removed))
            {
                var name = AODataManager.Instance.GetItemInstance(removed.InstanceId)?.Definition?.Name ?? removed.Definition.Name;
                SetStatus($"Moved {name}.");
                NotifyStateChanged();
                return true;
            }

            // Rollback source on failure
            TrySetToZone(fromZone, fromIndex, removed);
            reason = "Failed to place item in target slot.";
            SetStatus(reason);
            return false;
        }

        public bool TryMoveEquippedItemToZone(int fromEquipSlotId, SlotZone toZone, int toIndex, out string reason)
        {
            if (HasNativeSession) return TryNativeUnequip(fromEquipSlotId, toZone, toIndex, out reason);
            reason = string.Empty;

            if (Character == null)
            {
                reason = "Character is not initialized.";
                SetStatus(reason);
                return false;
            }

            var equipped = GetEquipped();
            if (!equipped.TryGetValue(fromEquipSlotId, out var instanceId))
            {
                reason = $"Equip slot {fromEquipSlotId} is empty.";
                SetStatus(reason);
                return false;
            }

            var item = AODataManager.Instance.GetCoreInstance(instanceId);
            if (item == null)
            {
                reason = "Missing equipped item instance.";
                SetStatus(reason);
                return false;
            }

            if (toZone == SlotZone.Backpack && item.IsContainer)
            {
                reason = "Backpacks cannot be placed in backpacks.";
                SetStatus(reason);
                return false;
            }

            if (GetSlotItem(toZone, toIndex) != null)
            {
                reason = "Target slot is occupied.";
                SetStatus(reason);
                return false;
            }

            if (!Character.UnequipSlot(fromEquipSlotId))
            {
                reason = $"Failed to unequip slot {fromEquipSlotId}.";
                SetStatus(reason);
                return false;
            }

            if (TrySetToZone(toZone, toIndex, item))
            {
                var name = AODataManager.Instance.GetItemInstance(item.InstanceId)?.Definition?.Name ?? item.Definition?.Name ?? item.InstanceId.ToString();
                SetStatus($"Moved {name}.");
                NotifyStateChanged();
                return true;
            }

            // Rollback equip on placement failure.
            Character.EquipItem(fromEquipSlotId, instanceId);
            reason = "Failed to place item in target slot.";
            SetStatus(reason);
            return false;
        }

        public void EquipItem(DataItemInstance dataItem)
        {
            if (dataItem?.Definition == null)
            {
                SetStatus("Equip failed: invalid data item.");
                return;
            }

            TryEquipByInstanceId(dataItem.InstanceId, SelectedEquipSlot, strictPreferredSlot: true);
        }

        public void UnequipSlot(int slotId)
        {
            if (HasNativeSession)
            {
                int empty = -1;
                for (int i = 0; i < Character.Inventory.Main.Capacity; i++)
                    if (Character.Inventory.Main.Slots[i] == null) { empty = i; break; }
                TryNativeUnequip(slotId, SlotZone.Inventory, empty, out _);
                return;
            }
            if (_networkClient != null && _networkClient.IsConnected)
            {
                if (slotId > WeaponNonHandSlotOffset && slotId < WeaponNonHandSlotOffset + 100)
                {
                    SetStatus("Unequip blocked: non-hand weapon slots require AO.Server slot namespace support.");
                    return;
                }

                var equipped = GetEquipped();
                if (!equipped.TryGetValue(slotId, out var instanceId))
                {
                    SetStatus($"Unequip failed: slot {slotId} is empty.");
                    return;
                }

                _networkClient.RequestUnequipSlot(ToServerSlotId(slotId));
                SetStatus($"Sent unequip request for slot {slotId} to AO.Server.");
                return;
            }

            ApplyUnequipSlotLocal(slotId);
        }

        public bool TryGetOpenBackpackContainer(out InventoryContainer container, out ItemInstance containerItem)
        {
            container = null;
            containerItem = null;

            if (Character == null)
                return false;

            if (OpenBackpackId == 0)
                return false;

            if (!Character.Inventory.TryGetContainer(OpenBackpackId, out container))
                return false;

            containerItem = AODataManager.Instance.GetCoreInstance(OpenBackpackId);
            return true;
        }

        public IReadOnlyDictionary<int, long> GetEquipped()
        {
            if (Character == null) return EmptyEquipped;
            return Character.Equipment.GetAllEquipped();
        }

        public IReadOnlyDictionary<int, long> GetSocialEquipped()
        {
            return _socialEquipped;
        }

        public IReadOnlyDictionary<int, int> GetStats()
        {
            if (Character == null) return EmptyStats;
            return Character.StatsContainer.DebugGetAllStats();
        }

        public int GetCurrentAttackRating()
        {
            if (!TryResolveAttackProfile(out var weapon, out var skillSplits, out _, out _))
                return Mathf.Max(0, Character?.StatsContainer?.GetFinalStat(StatIds.AMS) ?? 0);

            float weighted = 0f;
            for (int i = 0; i < skillSplits.Count; i++)
            {
                var pair = skillSplits[i];
                int skillValue = GetModifiedStatValue(pair.StatId);
                weighted += pair.Weight * Mathf.Max(0, skillValue);
            }

            int addAllOff = Mathf.Max(0, GetModifiedStatValue(AddAllOffStatId));
            int rating = Mathf.FloorToInt(weighted) + addAllOff;
            return Mathf.Max(0, rating);
        }

        public bool TryUseBurstSpecial(out int totalDamage)
        {
            totalDamage = 0;
            if (!TryResolveSpecialWeapon(out var weapon, out int mbs, out string failReason))
            {
                SetStatus(failReason);
                return false;
            }

            if (!WeaponHasSpecial(weapon, "Burst"))
            {
                SetStatus("Burst unavailable: equipped weapon has no Burst special.");
                return false;
            }

            float now = Time.unscaledTime;
            if (_burstReadyAt > now)
            {
                SetStatus($"Burst skill ready in {Mathf.CeilToInt(_burstReadyAt - now)} seconds.");
                return false;
            }

            int shot1 = ComputeWeaponShotDamage(weapon, mbs, out _);
            int shot2 = ComputeWeaponShotDamage(weapon, mbs, out _);
            int shot3 = ComputeWeaponShotDamage(weapon, mbs, out _);
            totalDamage = Mathf.Max(0, shot1 + shot2 + shot3);

            if (!TryApplyDamageToSelectedTarget(totalDamage, out string targetName, out int remainingHealth, out bool killed))
            {
                SetStatus("Burst failed: no valid target.");
                return false;
            }

            ChatDamageWindowView.AppendDamageFeed(
                $"You hit {targetName} with Burst for {totalDamage} damage ({shot1}+{shot2}+{shot3}). HP left: {remainingHealth}.");
            if (killed)
                ChatDamageWindowView.AppendDamageFeed($"{targetName} was defeated.");

            int burstSkillId = ResolveStatIdFromToken("Burst");
            int burstSkill = burstSkillId > 0 ? Mathf.Max(0, GetModifiedStatValue(burstSkillId)) : 0;
            float attackSeconds = Mathf.Clamp(ConvertAttackDelayToSeconds(GetRawStatValue(weapon, AttackDelayStatId)), 0.05f, 5f);
            float rechargeSeconds = Mathf.Clamp(ConvertRechargeDelayToSeconds(GetRawStatValue(weapon, RechargeDelayStatId)), 0.05f, 10f);
            int burstDelayStatId = ResolveStatIdFromToken("BurstDelay");
            float burstDelay = burstDelayStatId > 0 ? Mathf.Max(0f, GetRawStatValue(weapon, burstDelayStatId)) : 0f;
            float computed = (rechargeSeconds * 20f) + burstDelay - (burstSkill / 25f);
            float minRecharge = 8f + attackSeconds;
            float finalRecharge = Mathf.Max(minRecharge, computed);
            _burstReadyAt = now + finalRecharge;
            return true;
        }

        public bool IsEquippedBurstWeaponOneHandedRanged()
        {
            if (!TryResolveAttackProfile(out var weapon, out _, out _, out _))
                return false;

            int multiRangedStatId = ResolveStatIdFromToken("MultiRanged");
            if (multiRangedStatId <= 0)
                return false;

            return GetRawStatValue(weapon, multiRangedStatId) > 0;
        }

        public bool TryUseFlingShotSpecial(out int damage)
        {
            damage = 0;
            if (!TryResolveSpecialWeapon(out var weapon, out int mbs, out string failReason))
            {
                SetStatus(failReason);
                return false;
            }

            if (!WeaponHasSpecial(weapon, "FlingShot"))
            {
                SetStatus("Fling Shot unavailable: equipped weapon has no Fling Shot special.");
                return false;
            }

            float now = Time.unscaledTime;
            if (_flingReadyAt > now)
            {
                SetStatus($"Fling Shot ready in {Mathf.CeilToInt(_flingReadyAt - now)} seconds.");
                return false;
            }

            damage = ComputeWeaponShotDamage(weapon, mbs, out bool isCrit);
            if (!TryApplyDamageToSelectedTarget(damage, out string targetName, out int remainingHealth, out bool killed))
            {
                SetStatus("Fling Shot failed: no valid target.");
                return false;
            }

            string critText = isCrit ? " (crit)" : string.Empty;
            ChatDamageWindowView.AppendDamageFeed(
                $"You hit {targetName} with Fling Shot for {damage} damage{critText}. HP left: {remainingHealth}.");
            if (killed)
                ChatDamageWindowView.AppendDamageFeed($"{targetName} was defeated.");

            int flingSkillId = ResolveStatIdFromToken("FlingShot");
            int flingSkill = flingSkillId > 0 ? Mathf.Max(0, GetModifiedStatValue(flingSkillId)) : 0;
            float attackSeconds = Mathf.Clamp(ConvertAttackDelayToSeconds(GetRawStatValue(weapon, AttackDelayStatId)), 0.05f, 5f);
            float computed = (attackSeconds * 15f) - (flingSkill / 100f);
            float minRecharge = 6f + attackSeconds;
            float finalRecharge = Mathf.Max(minRecharge, computed);
            _flingReadyAt = now + finalRecharge;
            return true;
        }

        public void TickAutoAttack(bool attackActive)
        {
            if (!attackActive || Character == null || _selectedTarget == null)
            {
                _pendingAutoAttackImpactAt = -1f;
                _currentAutoAttackRechargeEndAt = -1f;
                _lastConsumedAttackImpactCounter = -1;
                return;
            }

            if (!TryResolveAttackProfile(out var weapon, out _, out int mbs, out _))
            {
                _pendingAutoAttackImpactAt = -1f;
                _currentAutoAttackRechargeEndAt = -1f;
                _nextAutoAttackAt = Time.unscaledTime + 0.4f;
                _lastConsumedAttackImpactCounter = -1;
                return;
            }

            if (TryGetAuthoritativeRuntimeEntityId(_selectedTarget, out string authoritativeEntityId))
            {
                float authoritativeNow = Time.unscaledTime;
                float authoritativeAttackSeconds;
                float authoritativeRechargeSeconds;
                if (weapon == null)
                {
                    var unarmed = ResolveUnarmedMartialArtsStats();
                    authoritativeAttackSeconds = Mathf.Clamp(unarmed.AttackSeconds, 0.05f, 5f);
                    authoritativeRechargeSeconds = Mathf.Clamp(unarmed.RechargeSeconds, 0.05f, 5f);
                }
                else
                {
                    authoritativeAttackSeconds = Mathf.Clamp(ConvertAttackDelayToSeconds(GetRawStatValue(weapon, AttackDelayStatId)), 0.05f, 5f);
                    authoritativeRechargeSeconds = Mathf.Clamp(ConvertRechargeDelayToSeconds(GetRawStatValue(weapon, RechargeDelayStatId)), 0.05f, 5f);
                }

                if (_pendingAutoAttackImpactAt < 0f && authoritativeNow >= _nextAutoAttackAt)
                {
                    _pendingAutoAttackImpactAt = authoritativeNow + authoritativeAttackSeconds;
                    _currentAutoAttackRechargeEndAt = _pendingAutoAttackImpactAt + authoritativeRechargeSeconds;
                    TryStartAuthoritativeRuntimeMobCombat(_selectedTarget);
                }

                if (_pendingAutoAttackImpactAt >= 0f && authoritativeNow >= _pendingAutoAttackImpactAt)
                {
                    int authoritativeDamage = ComputeWeaponShotDamage(weapon, mbs, out bool authoritativeCrit);
                    if (TryApplyAuthoritativeRuntimeMobDamage(_selectedTarget, authoritativeDamage))
                    {
                        string authoritativeTargetName = ResolveTargetName(_selectedTarget);
                        string critText = authoritativeCrit ? " (crit)" : string.Empty;
                        ChatDamageWindowView.AppendDamageFeed($"You hit {authoritativeTargetName} for {authoritativeDamage} damage{critText}.");
                    }

                    _pendingAutoAttackImpactAt = -1f;
                    _nextAutoAttackAt = Mathf.Max(authoritativeNow, _currentAutoAttackRechargeEndAt);
                }

                return;
            }

            var appearance = _runtimeBridge != null ? _runtimeBridge.GetComponent<CharacterAppearanceController>() : null;
            if (appearance != null && appearance.IsAttackToggleActive)
            {
                int impactCounter = appearance.AttackImpactCounter;
                if (_lastConsumedAttackImpactCounter < 0)
                {
                    _lastConsumedAttackImpactCounter = impactCounter;
                    if (TryStartAuthoritativeRuntimeMobCombat(_selectedTarget))
                        return;
                    return;
                }

                if (impactCounter == _lastConsumedAttackImpactCounter)
                    return;

                _lastConsumedAttackImpactCounter = impactCounter;
                if (TryStartAuthoritativeRuntimeMobCombat(_selectedTarget))
                    return;

                int syncedDamage = ComputeWeaponShotDamage(weapon, mbs, out bool syncedCrit);
                if (TryApplyDamageToSelectedTarget(syncedDamage, out string syncedTargetName, out int syncedRemainingHealth, out bool syncedKilled))
                {
                    string critText = syncedCrit ? " (crit)" : string.Empty;
                    ChatDamageWindowView.AppendDamageFeed($"You hit {syncedTargetName} for {syncedDamage} damage{critText}. HP left: {syncedRemainingHealth}.");
                    if (syncedKilled)
                        ChatDamageWindowView.AppendDamageFeed($"{syncedTargetName} was defeated.");
                }
                return;
            }

            float now = Time.unscaledTime;
            float attackSeconds;
            float rechargeSeconds;
            if (weapon == null)
            {
                var unarmed = ResolveUnarmedMartialArtsStats();
                attackSeconds = Mathf.Clamp(unarmed.AttackSeconds, 0.05f, 5f);
                rechargeSeconds = Mathf.Clamp(unarmed.RechargeSeconds, 0.05f, 5f);
            }
            else
            {
                attackSeconds = Mathf.Clamp(ConvertAttackDelayToSeconds(GetRawStatValue(weapon, AttackDelayStatId)), 0.05f, 5f);
                rechargeSeconds = Mathf.Clamp(ConvertRechargeDelayToSeconds(GetRawStatValue(weapon, RechargeDelayStatId)), 0.05f, 5f);
            }

            // Phase 1: start a new swing only when we're out of recharge and not already winding up.
            if (_pendingAutoAttackImpactAt < 0f && now >= _nextAutoAttackAt)
            {
                _pendingAutoAttackImpactAt = now + attackSeconds;
                _currentAutoAttackRechargeEndAt = _pendingAutoAttackImpactAt + rechargeSeconds;
                TryStartAuthoritativeRuntimeMobCombat(_selectedTarget);
            }

            // Phase 2: apply hit exactly when attack windup completes.
            if (_pendingAutoAttackImpactAt < 0f || now < _pendingAutoAttackImpactAt)
                return;

            int finalDamage = ComputeWeaponShotDamage(weapon, mbs, out bool isCrit);

            if (TryApplyDamageToSelectedTarget(finalDamage, out string targetName, out int remainingHealth, out bool killed))
            {
                string critText = isCrit ? " (crit)" : string.Empty;
                ChatDamageWindowView.AppendDamageFeed($"You hit {targetName} for {finalDamage} damage{critText}. HP left: {remainingHealth}.");
                if (killed)
                    ChatDamageWindowView.AppendDamageFeed($"{targetName} was defeated.");
            }

            // Consume this swing and wait through recharge before next attack windup begins.
            _pendingAutoAttackImpactAt = -1f;
            _nextAutoAttackAt = Mathf.Max(now, _currentAutoAttackRechargeEndAt);
        }

        private bool TryResolveSpecialWeapon(out AO.Data.Core.Item weapon, out int mbs, out string failReason)
        {
            weapon = null;
            mbs = 0;
            failReason = string.Empty;

            if (_selectedTarget == null)
            {
                failReason = "Special failed: no target selected.";
                return false;
            }

            if (!TryResolveAttackProfile(out weapon, out _, out mbs, out _))
            {
                failReason = "Special failed: no valid weapon equipped.";
                return false;
            }

            return true;
        }

        private bool WeaponHasSpecial(AO.Data.Core.Item rawWeapon, string specialName)
        {
            if (rawWeapon == null || string.IsNullOrWhiteSpace(specialName))
                return false;

            EnsureSlotNamesLoaded();
            uint mask = unchecked((uint)GetRawStatValue(rawWeapon, CanFlagStatId));
            if (mask == 0 || _canNameByBit.Count == 0)
                return false;

            foreach (var kv in _canNameByBit)
            {
                if (kv.Key == 0 || kv.Key > uint.MaxValue)
                    continue;
                if (!string.Equals(kv.Value, specialName, StringComparison.OrdinalIgnoreCase))
                    continue;

                uint bit = (uint)kv.Key;
                if ((mask & bit) == bit)
                    return true;
            }

            return false;
        }

        private int ComputeWeaponShotDamage(AO.Data.Core.Item weapon, int mbs, out bool isCrit)
        {
            isCrit = false;

            int minDamage;
            int maxDamage;
            int critBonus;
            if (weapon == null)
            {
                var unarmed = ResolveUnarmedMartialArtsStats();
                minDamage = Mathf.Max(1, unarmed.MinDamage);
                maxDamage = Mathf.Max(minDamage, unarmed.MaxDamage);
                critBonus = Mathf.Max(0, unarmed.CritBonus);
            }
            else
            {
                minDamage = Mathf.Max(1, GetRawStatValue(weapon, MinDamageStatId));
                maxDamage = Mathf.Max(minDamage, GetRawStatValue(weapon, MaxDamageStatId));
                critBonus = Mathf.Max(0, GetRawStatValue(weapon, CritBonusDamageStatId));
            }

            int attackRating = GetCurrentAttackRating();
            int cappedAttackRating = mbs > 0 ? Mathf.Min(attackRating, mbs) : attackRating;
            float multiplier = 1f + (cappedAttackRating / 400f);

            int rolled = UnityEngine.Random.Range(minDamage, maxDamage + 1);
            isCrit = UnityEngine.Random.value < 0.03f;
            int baseDamage = isCrit ? (maxDamage + critBonus) : rolled;

            int targetAc = ResolveTargetArmorClassForWeapon(weapon);
            int reduced = Mathf.FloorToInt(baseDamage * multiplier) - Mathf.FloorToInt(targetAc / 10f);
            return Mathf.Max(minDamage, reduced);
        }

        private (int MinDamage, int MaxDamage, int CritBonus, float AttackSeconds, float RechargeSeconds) ResolveUnarmedMartialArtsStats()
        {
            int maStatId = ResolveStatIdFromToken("MartialArts");
            if (maStatId <= 0)
                maStatId = MartialArtsFallbackStatId;
            int maSkill = Mathf.Max(1, GetModifiedStatValue(maStatId));
            int professionId = Character?.ProfessionId ?? 1;

            bool isMA = professionId == MartialArtistProfessionId;
            bool isShade = professionId == ShadeProfessionId;

            int tierStart = maSkill <= 1000 ? 1 : maSkill <= 2000 ? 1001 : 2001;
            int tierEnd = maSkill <= 1000 ? 1000 : maSkill <= 2000 ? 2000 : 3000;
            int clamped = Mathf.Clamp(maSkill, tierStart, tierEnd);
            float t = tierEnd > tierStart ? (clamped - tierStart) / (float)(tierEnd - tierStart) : 0f;

            float startAtk, startRec, endAtk, endRec;
            int startMin, startMax, startCrit, endMin, endMax, endCrit;

            if (isMA)
            {
                if (maSkill <= 1000)
                {
                    startAtk = 1.15f; startRec = 1.15f; endAtk = 1.25f; endRec = 1.25f;
                    startMin = 4; startMax = 8; startCrit = 3; endMin = 125; endMax = 400; endCrit = 500;
                }
                else if (maSkill <= 2000)
                {
                    startAtk = 1.30f; startRec = 1.30f; endAtk = 1.35f; endRec = 1.35f;
                    startMin = 130; startMax = 405; startCrit = 501; endMin = 220; endMax = 830; endCrit = 560;
                }
                else
                {
                    startAtk = 1.45f; startRec = 1.45f; endAtk = 1.50f; endRec = 1.50f;
                    startMin = 225; startMax = 831; startCrit = 561; endMin = 450; endMax = 1300; endCrit = 800;
                }
            }
            else if (isShade)
            {
                if (maSkill <= 1000)
                {
                    startAtk = 1.25f; startRec = 1.25f; endAtk = 1.45f; endRec = 1.45f;
                    startMin = 3; startMax = 5; startCrit = 3; endMin = 55; endMax = 258; endCrit = 250;
                }
                else if (maSkill <= 2000)
                {
                    startAtk = 1.45f; startRec = 1.45f; endAtk = 1.65f; endRec = 1.65f;
                    startMin = 56; startMax = 259; startCrit = 251; endMin = 130; endMax = 682; endCrit = 275;
                }
                else
                {
                    startAtk = 1.65f; startRec = 1.65f; endAtk = 1.85f; endRec = 1.85f;
                    startMin = 131; startMax = 683; startCrit = 276; endMin = 280; endMax = 890; endCrit = 300;
                }
            }
            else
            {
                if (maSkill <= 1000)
                {
                    startAtk = 1.25f; startRec = 1.25f; endAtk = 1.45f; endRec = 1.45f;
                    startMin = 3; startMax = 5; startCrit = 3; endMin = 65; endMax = 280; endCrit = 500;
                }
                else if (maSkill <= 2000)
                {
                    startAtk = 1.45f; startRec = 1.45f; endAtk = 1.65f; endRec = 1.65f;
                    startMin = 66; startMax = 281; startCrit = 501; endMin = 140; endMax = 715; endCrit = 605;
                }
                else
                {
                    startAtk = 1.65f; startRec = 1.65f; endAtk = 1.85f; endRec = 1.85f;
                    startMin = 204; startMax = 831; startCrit = 605; endMin = 300; endMax = 990; endCrit = 630;
                }
            }

            int min = Mathf.RoundToInt(Mathf.Lerp(startMin, endMin, t));
            int max = Mathf.RoundToInt(Mathf.Lerp(startMax, endMax, t));
            int crit = Mathf.RoundToInt(Mathf.Lerp(startCrit, endCrit, t));
            float atk = Mathf.Lerp(startAtk, endAtk, t);
            float rec = Mathf.Lerp(startRec, endRec, t);
            return (min, max, crit, atk, rec);
        }
        public bool TryEquipByInstanceId(
            long instanceId,
            int? preferredSlot = null,
            bool strictPreferredSlot = false,
            SlotZone? sourceZone = null,
            int sourceIndex = -1)
        {
            if (HasNativeSession) return TryNativeEquip(instanceId, preferredSlot, strictPreferredSlot);
            if (_networkClient != null && _networkClient.IsConnected)
            {
                var dataItem = AODataManager.Instance.GetItemInstance(instanceId);
                if (dataItem?.Definition == null)
                {
                    SetStatus("Equip failed: missing data item.");
                    return false;
                }

                var core = AODataManager.Instance.GetCoreInstance(instanceId);
                if (core?.Definition == null)
                {
                    SetStatus($"Equip failed: missing core item for {dataItem.Definition.Name}.");
                    return false;
                }

                var slotCandidates = strictPreferredSlot && preferredSlot.HasValue
                    ? new List<int> { preferredSlot.Value }
                    : ResolveEquipSlotCandidates(core.Definition, dataItem).ToList();

                if (!strictPreferredSlot && preferredSlot.HasValue)
                    slotCandidates.Insert(0, preferredSlot.Value);

                int requestedSlot = slotCandidates.FirstOrDefault(slot => slot > 0);
                if (requestedSlot <= 0)
                {
                    SetStatus($"Equip failed: no valid target slot found for {dataItem.Definition.Name}.");
                    return false;
                }

                if (!IsSlotEnabledByDeckCapacity(requestedSlot))
                {
                    SetStatus($"Equip blocked: {GetSlotName(requestedSlot)} is not enabled by your equipped belt.");
                    return false;
                }

                if (GetItemClass(core.Definition) == ItemClass.Weapon
                    && requestedSlot > WeaponNonHandSlotOffset
                    && requestedSlot < WeaponNonHandSlotOffset + 100)
                {
                    SetStatus("Equip blocked: non-hand weapon slots require AO.Server slot namespace support.");
                    return false;
                }

                _networkClient.RequestEquipItem(ToServerEquipSlotId(core.Definition, requestedSlot), instanceId);
                SetStatus($"Sent equip request for {dataItem.Definition.Name} to AO.Server.");
                return true;
            }

            return ApplyEquipByInstanceIdLocal(instanceId, preferredSlot, strictPreferredSlot, sourceZone, sourceIndex);
        }

        private void ApplyUnequipSlotLocal(int slotId)
        {
            slotId = ResolveLocalSlotIdFromServer(slotId);
            var equipped = GetEquipped();
            if (!equipped.TryGetValue(slotId, out var instanceId))
            {
                SetStatus($"Unequip failed: slot {slotId} is empty.");
                return;
            }

            if (!Character.UnequipSlot(slotId))
            {
                SetStatus($"Unequip failed: slot {slotId}.");
                return;
            }

            var core = AODataManager.Instance.GetCoreInstance(instanceId);
            if (core != null)
                Character.Inventory.TryAddToMain(core);

            SetStatus($"Unequipped slot {slotId}.");
            NotifyStateChanged();
        }

        private bool ApplyEquipByInstanceIdLocal(
            long instanceId,
            int? preferredSlot = null,
            bool strictPreferredSlot = false,
            SlotZone? sourceZone = null,
            int sourceIndex = -1)
        {
            var dataItem = AODataManager.Instance.GetItemInstance(instanceId);
            if (dataItem?.Definition == null)
            {
                SetStatus("Equip failed: missing data item.");
                return false;
            }

            var core = AODataManager.Instance.GetCoreInstance(instanceId);
            if (core?.Definition == null)
            {
                SetStatus($"Equip failed: missing core item for {dataItem.Definition.Name}.");
                return false;
            }

            bool hadExplicitSource = sourceZone.HasValue && sourceIndex >= 0;
            bool wasInInventoryOrContainer = false;
            if (hadExplicitSource)
            {
                var located = GetSlotItem(sourceZone.Value, sourceIndex);
                if (located != null && located.InstanceId == instanceId)
                    wasInInventoryOrContainer = RemoveFromZone(sourceZone.Value, sourceIndex, out _);
            }

            if (!wasInInventoryOrContainer)
            {
                wasInInventoryOrContainer = Character.Inventory.RemoveItem(core);
                if (!wasInInventoryOrContainer && Character?.Inventory?.Items != null)
                {
                    var byInstance = Character.Inventory.Items.FirstOrDefault(i => i != null && i.InstanceId == instanceId);
                    if (byInstance != null)
                        wasInInventoryOrContainer = Character.Inventory.RemoveItem(byInstance);
                }
            }

            var slotCandidates = strictPreferredSlot && preferredSlot.HasValue
                ? new List<int> { preferredSlot.Value }
                : ResolveEquipSlotCandidates(core.Definition, dataItem).ToList();

            if (!strictPreferredSlot && preferredSlot.HasValue)
                slotCandidates.Insert(0, preferredSlot.Value);

            string lastReason = "No valid equip slot found.";
            if (!ValidateEquipRequirements(dataItem, out var reqReason, out var requirementFailures))
            {
                if (wasInInventoryOrContainer)
                {
                    if (!(hadExplicitSource && TrySetToZone(sourceZone.Value, sourceIndex, core)))
                        Character.Inventory.TryAddToMain(core);
                }

                lastReason = reqReason;
                string status = BuildEquipBlockedStatusMessage(lastReason, requirementFailures);
                SetStatus(status);
                Debug.LogWarning($"[AutoEquipBlocked] Item={dataItem.Definition.Name} Reason={lastReason}");
                return false;
            }

            foreach (var slot in slotCandidates.Distinct())
            {
                int normalizedSlot = NormalizeEquipSlotForLocalStorage(core.Definition, slot);
                if (!IsSlotEnabledByDeckCapacity(normalizedSlot))
                {
                    lastReason = $"Deck slot {GetSlotName(normalizedSlot)} is not enabled by your equipped belt.";
                    continue;
                }

                if (!EquipmentValidator.CanEquip(Character, core.Definition, normalizedSlot, out var reason))
                {
                    lastReason = reason;
                    continue;
                }

                var equipped = GetEquipped();
                if (equipped.TryGetValue(normalizedSlot, out var occupyingId) && occupyingId != instanceId)
                {
                    var occupyingCore = AODataManager.Instance.GetCoreInstance(occupyingId);
                    if (occupyingCore == null)
                    {
                        lastReason = $"Could not resolve currently equipped item in slot {GetSlotName(normalizedSlot)}.";
                        continue;
                    }

                    if (!Character.UnequipSlot(normalizedSlot))
                    {
                        lastReason = $"Could not unequip occupying item from slot {normalizedSlot}.";
                        continue;
                    }

                    bool returnedToSource = false;
                    if (hadExplicitSource)
                        returnedToSource = TrySetToZone(sourceZone.Value, sourceIndex, occupyingCore);

                    if (!returnedToSource && !Character.Inventory.TryAddToMain(occupyingCore))
                    {
                        lastReason = "Inventory full while replacing equipped item.";
                        if (!Character.EquipItem(normalizedSlot, occupyingId))
                            Character.Inventory.TryAddToMain(occupyingCore);
                        continue;
                    }
                }

                if (Character.EquipItem(normalizedSlot, instanceId))
                {
                    SetStatus($"Equipped {dataItem.Definition.Name} to slot {normalizedSlot}.");
                    NotifyStateChanged();
                    return true;
                }

                lastReason = $"Character.EquipItem returned false for slot {normalizedSlot}.";
            }

            if (wasInInventoryOrContainer)
            {
                if (!(hadExplicitSource && TrySetToZone(sourceZone.Value, sourceIndex, core)))
                    Character.Inventory.TryAddToMain(core);
            }

            SetStatus(BuildEquipBlockedStatusMessage(lastReason));
            Debug.LogWarning($"[AutoEquipBlocked] Item={dataItem.Definition.Name} Reason={lastReason}");
            return false;
        }

        public DataItemInstance GetDataItem(long instanceId) => AODataManager.Instance.GetItemInstance(instanceId);
        public int GetAvailableIp() => Character?.AvailableIp ?? 0;

        public bool TryGetAuthoritativeStats(out int health, out int maxHealth, out int nano, out int maxNano, out long experience)
        {
            health = 0;
            maxHealth = 0;
            nano = 0;
            maxNano = 0;
            experience = 0;

            return _networkClient != null && _networkClient.TryGetAuthoritativeStats(out health, out maxHealth, out nano, out maxNano, out experience);
        }

        public void AddExperience(long amount)
        {
            if (Character == null)
            {
                SetStatus("Character is not initialized.");
                return;
            }

            if (amount <= 0)
            {
                SetStatus("Experience gain must be positive.");
                return;
            }

            int beforeLevel = Character.Level.Level;
            int gainedLevels = Character.AddExperience(amount);
            int afterLevel = Character.Level.Level;
            long current = Character.Level.Experience;
            long needed = Character.GetExperienceToNextLevel();

            if (gainedLevels > 0 || afterLevel > beforeLevel)
                SetStatus($"Gained {amount} XP. Level {afterLevel} ({current}/{needed}).");
            else
                SetStatus($"Gained {amount} XP ({current}/{needed}).");

            NotifyStateChanged();
        }

        public void PublishStatus(string message)
        {
            SetStatus(message);
        }

        public void RaiseStateChanged()
        {
            NotifyStateChanged();
        }

        public IReadOnlyDictionary<int, string> GetBreedLookup() => AODataManager.Instance.GetBreedLookup();

        public IReadOnlyDictionary<int, string> GetProfessionLookup() => AODataManager.Instance.GetProfessionLookup();

        public float GetRunSpeed()
        {
            var walker = ResolvePlayerWalker();
            return walker != null ? walker.GetRunSpeed() : 8f;
        }

        public float GetJumpHeight()
        {
            var walker = ResolvePlayerWalker();
            return walker != null ? walker.GetJumpHeight() : 1.2f;
        }

        public PrototypeWalkerController.MovementMode GetMovementMode()
        {
            var walker = ResolvePlayerWalker();
            return walker != null ? walker.GetMovementMode() : PrototypeWalkerController.MovementMode.Grounded;
        }

        public void ApplyMovementSettings(
            float runSpeed,
            float jumpHeight,
            PrototypeWalkerController.MovementMode movementMode)
        {
            var walker = ResolvePlayerWalker();
            if (walker == null)
            {
                SetStatus("Movement settings unavailable: player controller not found.");
                return;
            }

            walker.SetRunSpeed(runSpeed);
            walker.SetJumpHeight(jumpHeight);
            walker.SetMovementMode(movementMode);
            NotifyStateChanged();
            SetStatus(
                $"Movement updated: mode {walker.GetMovementMode()}, run speed {walker.GetRunSpeed():0.##}, " +
                $"jump height {walker.GetJumpHeight():0.##}.");
        }

        private PrototypeWalkerController ResolvePlayerWalker()
        {
            static bool IsRuntimeDynelBridge(CharacterRuntimeBridge bridge)
            {
                if (bridge == null || bridge.gameObject == null)
                    return false;
                string name = bridge.gameObject.name ?? string.Empty;
                return name.StartsWith("Runtime_", StringComparison.OrdinalIgnoreCase);
            }

            PrototypeWalkerController TryGetWalker(CharacterRuntimeBridge bridge)
            {
                if (bridge == null || bridge.gameObject == null)
                    return null;
                if (IsRuntimeDynelBridge(bridge))
                    return null;
                return bridge.GetComponent<PrototypeWalkerController>();
            }

            var walker = TryGetWalker(_runtimeBridge);
            if (walker != null)
                return walker;

            var bridges = UnityEngine.Object.FindObjectsByType<CharacterRuntimeBridge>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            if (bridges == null || bridges.Length == 0)
                return null;

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null || IsRuntimeDynelBridge(bridge))
                    continue;
                if (bridge.Character == null)
                    continue;

                walker = bridge.GetComponent<PrototypeWalkerController>();
                if (walker != null)
                {
                    _runtimeBridge = bridge;
                    return walker;
                }
            }

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null || IsRuntimeDynelBridge(bridge))
                    continue;

                walker = bridge.GetComponent<PrototypeWalkerController>();
                if (walker != null)
                {
                    _runtimeBridge = bridge;
                    return walker;
                }
            }

            return null;
        }

        public IReadOnlyDictionary<string, string> GetHeadVisualLookup()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { string.Empty, "None" }
            };

            string breedToken = CharacterBreedId switch
            {
                1 => "solitus",
                2 => "opifex",
                3 => "nanomage",
                4 => "athrox",
                _ => string.Empty
            };
            string sexToken = CharacterSex switch
            {
                CharacterRuntimeBridge.CharacterSex.Female => "female",
                CharacterRuntimeBridge.CharacterSex.Uni => "male",
                _ => "male"
            };

            string root = Path.Combine(Application.streamingAssetsPath, "AOData", "ItemMeshes");
            foreach (var file in Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.glb")
                : Enumerable.Empty<string>())
            {
                string meshKey = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(meshKey))
                    continue;
                if (!meshKey.StartsWith("head_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(breedToken) && meshKey.IndexOf(breedToken, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!string.IsNullOrWhiteSpace(sexToken) && meshKey.IndexOf(sexToken, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (!result.ContainsKey(meshKey))
                    result[meshKey] = meshKey;
            }

            try
            {
                AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
                if (install != null && install.IsValid)
                {
                    string prefix = CharacterBreedId switch
                    {
                        1 => "head_solitus" + sexToken,
                        2 => "head_opifex" + sexToken,
                        3 => "head_nano" + (CharacterSex == CharacterRuntimeBridge.CharacterSex.Female ? "female" : "male"),
                        4 => "head_atrox",
                        _ => string.Empty
                    };
                    using (var database = new AOResourceDatabase(install.RootPath))
                    {
                        AOResourceCatalog catalog = AOResourceCatalog.Load(database);
                        foreach (var pair in catalog.GetResources(AOResourceTypes.Mesh))
                        {
                            string meshKey = Path.GetFileNameWithoutExtension(pair.Value ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(prefix)
                                || !meshKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!result.ContainsKey(meshKey))
                                result[meshKey] = meshKey;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not enumerate AO head meshes: {exception.Message}");
            }

            return result;
        }

        public void SetDebugHeadVisual(string meshKey)
        {
            if (_runtimeBridge == null)
                return;

            _runtimeBridge.DebugHeadMeshKey = meshKey ?? string.Empty;
            NotifyStateChanged();
            SetStatus(!string.IsNullOrWhiteSpace(meshKey)
                ? $"Preview head visual set to {meshKey}."
                : "Preview head visual cleared.");
        }

        public Vector3 GetDebugHeadEuler()
        {
            var appearance = _runtimeBridge != null ? _runtimeBridge.GetComponent<CharacterAppearanceController>() : null;
            return appearance != null ? appearance.GetDebugHeadLocalEuler() : Vector3.zero;
        }

        public void AdjustDebugHeadEuler(Vector3 delta)
        {
            var appearance = _runtimeBridge != null ? _runtimeBridge.GetComponent<CharacterAppearanceController>() : null;
            if (appearance == null)
                return;

            appearance.AdjustDebugHeadLocalEuler(delta);
            NotifyStateChanged();
            var euler = appearance.GetDebugHeadLocalEuler();
            SetStatus($"Head rotation set to X={euler.x:0.#}, Y={euler.y:0.#}, Z={euler.z:0.#}.");
        }

        public Vector3 GetDebugHeadPosition()
        {
            var appearance = _runtimeBridge != null ? _runtimeBridge.GetComponent<CharacterAppearanceController>() : null;
            return appearance != null ? appearance.GetDebugHeadLocalPosition() : Vector3.zero;
        }

        public void AdjustDebugHeadPosition(Vector3 delta)
        {
            var appearance = _runtimeBridge != null ? _runtimeBridge.GetComponent<CharacterAppearanceController>() : null;
            if (appearance == null)
                return;

            appearance.AdjustDebugHeadLocalPosition(delta);
            NotifyStateChanged();
            var position = appearance.GetDebugHeadLocalPosition();
            SetStatus($"Head position set to X={position.x:0.###}, Y={position.y:0.###}, Z={position.z:0.###}.");
        }

        public string GetStatName(int statId) => AODataManager.Instance.GetStatName(statId);

        public IReadOnlyCollection<int> GetKnownSkillStatIds() => AODataManager.Instance.GetKnownSkillStatIds();

        public string GetSkillColorName(int statId)
        {
            if (Character == null)
                return string.Empty;

            return AODataManager.Instance.GetSkillColorName(statId, Character.BreedId, Character.ProfessionId);
        }

        public int GetAbilityImprovementsPerLevel()
        {
            return AODataManager.Instance.GetAbilityImprovementsPerLevel();
        }

        public IReadOnlyList<float> GetSkillTrickleWeights(int statId)
        {
            return AODataManager.Instance.GetSkillTrickleWeights(statId);
        }

        public float? GetSkillCostFactorForCurrentProfession(int statId)
        {
            if (Character == null || statId <= 0)
                return null;

            return AODataManager.Instance.GetSkillCostFactorFor(Character.ProfessionId, statId);
        }

        public int? GetStatCapById(int statId)
        {
            if (Character == null || statId <= 0)
                return null;

            return Character.GetStatCap(statId);
        }

        public Sprite GetIconForData(DataItemInstance dataItem)
        {
            if (dataItem == null) return null;
            int iconId = dataItem.Definition.IconId > 0 ? dataItem.Definition.IconId : dataItem.InstanceId;
            return GetOrLoadIcon(iconId);
        }

        public Sprite GetIconById(int iconId)
        {
            return iconId > 0 ? GetOrLoadIcon(iconId) : null;
        }

        public Sprite GetIconForCore(ItemInstance coreItem)
        {
            if (coreItem == null) return null;
            var data = AODataManager.Instance.GetItemInstance(coreItem.InstanceId)
                ?? AODataManager.Instance.GetItemInstance(coreItem.Definition?.AOID ?? 0);
            return GetIconForData(data);
        }

        public bool TryGetItemTooltip(long instanceId, out ItemTooltipInfo info)
        {
            info = null;
            if (instanceId == 0)
                return false;

            var data = AODataManager.Instance.GetItemInstance(instanceId);
            if (data?.Definition == null && Character?.Inventory?.Items != null)
            {
                ItemInstance runtimeItem = Character.Inventory.Items.FirstOrDefault(
                    item => item != null && item.InstanceId == instanceId);
                data = AODataManager.Instance.GetItemInstance(
                    runtimeItem?.Definition?.AOID ?? 0);
            }
            if (data?.Definition == null)
                return false;

            var core = AODataManager.Instance.GetCoreInstance(instanceId);
            var raw = AODataManager.Instance.Items?.FirstOrDefault(i => i != null && i.AOID == data.DefinitionId);
            bool isStackable = IsStackable(core);
            int itemClass = GetRawStatValue(raw, 76);
            bool isWeapon = itemClass == 1;
            bool isHandWeapon = isWeapon && IsHandWeapon(raw);

            string rarityName = ResolveRarityName(raw?.Rarity ?? 0);
            int qlFromStat = GetRawStatValue(raw, 54);
            string quality = qlFromStat > 0
                ? qlFromStat.ToString()
                : raw != null && raw.Level > 0
                    ? raw.Level.ToString()
                : (data.Definition.RequiredLevel > 0 ? data.Definition.RequiredLevel.ToString() : "Unknown");
            if (isWeapon && !isHandWeapon && qlFromStat <= 1)
                quality = "SPECIAL";

            string requirements = ResolveRequirementText(raw, data, isWeapon);

            string location = ResolveLocationLabel(raw, data, core);
            string modifiers = ResolveModifierText(raw, data);
            string weaponSection = isWeapon
                ? (isHandWeapon ? ResolveWeaponDetailText(raw, location) : ResolveWeaponUtilityDetailText(raw, location))
                : string.Empty;
            string description = string.IsNullOrWhiteSpace(data.Definition.Description)
                ? "None"
                : data.Definition.Description.Trim();
            string countLine = string.Empty;
            int currentCount = Mathf.Max(1, core?.Quantity ?? data.Quantity);
            int maxCharges = Mathf.Max(0, GetRawStatValue(raw, 212));
            if (isStackable)
            {
                if (currentCount > 1)
                    countLine = $"{ColorLabel("Stack")} {ColorValue(currentCount.ToString())}\n";
            }
            else if (currentCount > 1 || maxCharges > 0)
            {
                if (maxCharges > 0)
                    countLine = $"{ColorLabel("Charges")} {ColorValue($"{currentCount}/{maxCharges}")}\n";
                else
                    countLine = $"{ColorLabel("Charges")} {ColorValue(currentCount.ToString())}\n";
            }

            string body =
                $"{ColorLabel("Rarity")} {ColorValue(rarityName)}\n" +
                $"{ColorLabel("Quality Level")} {ColorValue(quality)}\n" +
                countLine +
                $"{ColorLabel("Requirements")}\n{ColorValue(requirements)}\n";

            if (weaponSection.Length > 0)
                body += $"{weaponSection}\n";
            else
                body += $"{ColorLabel("Location")} {ColorValue(location)}\n";

            body +=
                $"{ColorLabel("Modifier")}\n{ColorValue(modifiers)}\n" +
                $"{ColorLabel("Description")}\n{ColorValue(description)}";

            info = new ItemTooltipInfo
            {
                Title = string.IsNullOrWhiteSpace(data.Definition.Name) ? $"Item {instanceId}" : data.Definition.Name,
                TitleColor = new Color(1f, 0.9f, 0.25f, 1f),
                Body = body
            };

            return true;
        }

        private Sprite GetOrLoadIcon(int iconId)
        {
            if (_iconCache.TryGetValue(iconId, out var cached))
                return cached;
            if (_missingIconIds.Contains(iconId))
                return null;

            byte[] bytes = null;
            try
            {
                AOItemIconResolver.TryReadIcon(iconId, out bytes);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Items] Could not read icon {iconId} from the AO database: {exception.Message}");
            }

            // Retain compatibility with existing development exports, but a
            // normal configured AO installation no longer needs this folder.
            if (bytes == null || bytes.Length == 0)
            {
                string path = Path.Combine(
                    Application.streamingAssetsPath, "AOData/Icons", $"{iconId}.png");
                if (File.Exists(path))
                    bytes = File.ReadAllBytes(path);
            }

            if (bytes == null || bytes.Length == 0)
            {
                _missingIconIds.Add(iconId);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                _missingIconIds.Add(iconId);
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            RemoveLikelyGreenScreenBackground(texture);
            texture.filterMode = FilterMode.Point;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            _iconCache[iconId] = sprite;
            return sprite;
        }

        private static void RemoveLikelyGreenScreenBackground(Texture2D texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return;

            var pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
                return;

            int width = texture.width;
            int height = texture.height;
            int[] cornerIndices =
            {
                0,
                width - 1,
                (height - 1) * width,
                (height * width) - 1
            };

            int cornerCount = 0;
            float keyR = 0f;
            float keyG = 0f;
            float keyB = 0f;
            for (int i = 0; i < cornerIndices.Length; i++)
            {
                Color32 c = pixels[cornerIndices[i]];
                if (!LooksLikeChromaGreen(c))
                    continue;

                keyR += c.r;
                keyG += c.g;
                keyB += c.b;
                cornerCount++;
            }

            if (cornerCount < 2)
                return;

            keyR /= cornerCount;
            keyG /= cornerCount;
            keyB /= cornerCount;

            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                if (c.a <= 0)
                    continue;

                float dr = c.r - keyR;
                float dg = c.g - keyG;
                float db = c.b - keyB;
                float distSq = dr * dr + dg * dg + db * db;

                // Hard remove near-key color, soften fringe for anti-aliased icon edges.
                if (distSq <= 42f * 42f)
                {
                    c.a = 0;
                    pixels[i] = c;
                    changed = true;
                }
                else if (distSq <= 70f * 70f)
                {
                    float t = Mathf.InverseLerp(42f * 42f, 70f * 70f, distSq);
                    byte targetAlpha = (byte)Mathf.RoundToInt(c.a * t);
                    if (targetAlpha < c.a)
                    {
                        c.a = targetAlpha;
                        pixels[i] = c;
                        changed = true;
                    }
                }
            }

            if (!changed)
                return;

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private static bool LooksLikeChromaGreen(Color32 c)
        {
            return c.a >= 180
                && c.g >= 80
                && c.g > c.r * 1.25f
                && c.g > c.b * 1.25f;
        }

        private static string ResolveRarityName(int rarity)
        {
            return rarity switch
            {
                <= 0 => "Unknown",
                1 => "Common",
                2 => "Uncommon",
                3 => "Rare",
                4 => "Epic",
                5 => "Legendary",
                _ => $"Tier {rarity}"
            };
        }

        private string ResolveLocationLabel(AO.Data.Core.Item raw, DataItemInstance data, ItemInstance core)
        {
            EnsureSlotNamesLoaded();
            int itemClass = GetRawStatValue(raw, 76);
            int slotBitmap = GetRawStatValue(raw, 298);

            var byBit = itemClass switch
            {
                1 => _weaponSlotNameByBit,
                2 => _armorSlotNameByBit,
                3 => _implantSlotNameByBit,
                _ => null
            };

            if (slotBitmap > 0 && byBit != null && byBit.Count > 0)
            {
                var labels = new List<string>();
                uint mask = unchecked((uint)slotBitmap);
                foreach (var kv in byBit)
                {
                    if (kv.Key == 0 || kv.Key > uint.MaxValue)
                        continue;

                    uint bit = (uint)kv.Key;
                    if ((mask & bit) != bit)
                        continue;

                    labels.Add(string.IsNullOrWhiteSpace(kv.Value) ? $"Bit {bit}" : kv.Value.Trim());
                }

                if (labels.Count > 0)
                    return string.Join(", ", labels.Distinct(StringComparer.OrdinalIgnoreCase));
            }

            var fallback = new List<string>();
            if (core?.Definition != null)
            {
                foreach (var slotId in EquipmentValidator.GetAllowedSlots(core.Definition))
                {
                    if (slotId <= 0)
                        continue;

                    fallback.Add(GetSlotName(slotId));
                }
            }

            if (fallback.Count == 0 && data?.Definition?.SlotType > 0)
                fallback.Add(GetSlotName(data.Definition.SlotType));

            return fallback.Count == 0
                ? "Unknown"
                : string.Join(", ", fallback.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string ResolveRequirementText(AO.Data.Core.Item raw, DataItemInstance data, bool isWeapon)
        {
            var lines = new List<string>();
            var actions = raw?.ActionData?.Actions;
            if (actions != null)
            {
                foreach (var action in actions)
                {
                    if (action == null || !IsEquipAction(action.Action) || action.Criteria == null)
                        continue;

                    foreach (var criterion in action.Criteria)
                    {
                        if (!TryFormatRequirementCriterion(criterion, isWeapon, out var formatted))
                            continue;

                        lines.Add(formatted);
                    }
                }
            }

            if (lines.Count == 0 && data?.Definition?.RequiredLevel > 0)
                lines.Add($"Level >= {data.Definition.RequiredLevel}");

            if (lines.Count == 0)
                lines.Add("Unknown");

            var cleaned = lines
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (cleaned.Count == 0)
                cleaned.Add("Unknown");

            string actionLabel = isWeapon ? "Wield:" : "Wear:";
            for (int i = 0; i < cleaned.Count; i++)
            {
                if (i < cleaned.Count - 1 && !cleaned[i].EndsWith(" and", StringComparison.OrdinalIgnoreCase))
                    cleaned[i] += " and";
            }

            return actionLabel + "\nOn Self:\n  " + string.Join("\n  ", cleaned);
        }

        private bool TryFormatRequirementCriterion(AO.Data.Core.ItemActionCriterion criterion, bool weaponFormat, out string formatted)
        {
            formatted = null;
            if (criterion == null || criterion.Value1 <= 0)
                return false;

            // Operator 4 is used as a separator marker in the source data.
            if (criterion.Operator == 4)
                return false;

            int statId = criterion.Value1;
            int value = criterion.Value2;
            string statName = ResolveRequirementStatName(statId);
            if (string.IsNullOrWhiteSpace(statName))
                statName = $"Stat {statId}";

            if (statId == 60)
            {
                string professionName = AODataManager.Instance.GetProfessionLookup()
                    .FirstOrDefault(p => p.Key == value).Value;
                if (string.IsNullOrWhiteSpace(professionName))
                    professionName = value.ToString();

                if (criterion.Operator == 0)
                {
                    formatted = weaponFormat ? professionName : professionName.ToLowerInvariant();
                    return true;
                }

                if (criterion.Operator == 1)
                {
                    formatted = $"{statName} != {professionName}";
                    return true;
                }

                formatted = $"{statName} {ResolveOperatorText(criterion.Operator, value, out _)} {professionName}";
                return true;
            }

            if (statId == 33 && criterion.Operator == 0)
            {
                string sideName = ResolveSideName(value);
                formatted = string.IsNullOrWhiteSpace(sideName)
                    ? $"{statName} = {value}"
                    : sideName.ToLowerInvariant();
                return true;
            }

            if (statId == 33 && criterion.Operator == 1)
            {
                string sideName = ResolveSideName(value);
                formatted = string.IsNullOrWhiteSpace(sideName)
                    ? $"{statName} != {value}"
                    : $"not {sideName.ToLowerInvariant()}";
                return true;
            }

            string opText = ResolveOperatorText(criterion.Operator, value, out int resolvedValue);
            if (opText == ">=" || opText == "=")
                formatted = $"{statName} from {resolvedValue}";
            else
                formatted = $"{statName} {opText} {resolvedValue}";
            return true;
        }

        private static string ResolveOperatorText(int op, int rawValue, out int resolvedValue)
        {
            resolvedValue = rawValue;
            if (op == 2)
            {
                // AO criteria uses strict '>' semantics; displayed equip threshold is value + 1.
                resolvedValue = rawValue + 1;
                return ">=";
            }

            return op switch
            {
                0 => "=",
                1 => "!=",
                3 => "<",
                5 => ">",
                _ => "="
            };
        }

        private string ResolveRequirementStatName(int statId)
        {
            if (statId == 60)
                return "Profession";
            if (statId == 37)
                return "Title Level";
            if (statId == 54)
                return "Level";
            if (statId == 33)
                return "Side";

            string name = AODataManager.Instance.GetStatName(statId);
            return string.IsNullOrWhiteSpace(name) ? $"Stat {statId}" : name;
        }

        private string ResolveModifierText(AO.Data.Core.Item raw, DataItemInstance data)
        {
            var lines = new List<string>();
            var groups = raw?.SpellData;
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (group?.Items == null)
                        continue;

                    foreach (var spell in group.Items)
                    {
                        if (spell == null)
                            continue;

                        if (spell.Target != 2)
                            continue;
                        if (IgnoredModifierStatIds.Contains(spell.Stat))
                            continue;

                        string line = ResolveModifierLine(spell);
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        lines.Add($"On User: {line}");
                    }
                }
            }

            if (lines.Count == 0 && data?.Definition?.StatModifiers != null)
            {
                foreach (var mod in data.Definition.StatModifiers)
                {
                    if (mod == null || mod.Value == 0)
                        continue;
                    if (IgnoredModifierStatIds.Contains(mod.StatId))
                        continue;

                    string name = AODataManager.Instance.GetStatName(mod.StatId);
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"Stat {mod.StatId}";

                    string sign = mod.Value > 0 ? "+" : string.Empty;
                    lines.Add($"On User: {sign}{mod.Value} {name}");
                    if (lines.Count >= 10)
                        break;
                }
            }

            if (lines.Count == 0)
                return "On User: None";

            var uniq = lines.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return "On User:\n  " + string.Join("\n  ", uniq.Select(TrimOnUserPrefix));
        }

        private bool IsHandWeapon(AO.Data.Core.Item raw)
        {
            int slotBitmap = GetRawStatValue(raw, 298);
            if (slotBitmap <= 0)
                return false;

            const int rightHandBit = 64;
            const int leftHandBit = 256;
            return (slotBitmap & rightHandBit) != 0 || (slotBitmap & leftHandBit) != 0;
        }

        private string ResolveWeaponUtilityDetailText(AO.Data.Core.Item raw, string location)
        {
            var lines = new List<string>();

            double attack = GetRawStatValue(raw, 294) / 100.0;
            double recharge = GetRawStatValue(raw, 210) / 100.0;
            double equipDelay = GetRawStatValue(raw, 211) / 100.0;
            lines.Add($"{ColorLabel("Speed")}");
            lines.Add($"{ColorValue($"Attack {attack:0.00}s")}");
            lines.Add($"{ColorValue($"Recharge {recharge:0.00}s")}");
            lines.Add($"{ColorLabel("Equip delay")} {ColorValue($"{equipDelay:0.00}s")}");

            string loc = (location ?? string.Empty).Replace(",", string.Empty);
            lines.Add($"{ColorLabel("Location")} {ColorValue(loc)}");

            return string.Join("\n", lines);
        }

        private string ResolveWeaponDetailText(AO.Data.Core.Item raw, string location)
        {
            var lines = new List<string>();

            int minDmg = GetRawStatValue(raw, 286);
            int maxDmg = GetRawStatValue(raw, 285);
            int critDmg = GetRawStatValue(raw, 284);
            if (minDmg > 0 || maxDmg > 0 || critDmg > 0)
            {
                lines.Add($"{ColorLabel("Damage")} {ColorValue($"{minDmg}-{maxDmg}({critDmg})")}");

                double attackSec = GetRawStatValue(raw, 294) / 100.0;
                double rechargeSec = GetRawStatValue(raw, 210) / 100.0;
                double cycle = Math.Max(0.01, attackSec + rechargeSec);
                double dpsMin = minDmg / cycle;
                double dpsMax = maxDmg / cycle;
                double dpsCrit = (maxDmg + critDmg) / cycle;
                lines.Add($"{ColorLabel("Dps")} {ColorValue($"{dpsMin:0.0}-{dpsMax:0.0}({dpsCrit:0.0})")}");
            }

            double attack = GetRawStatValue(raw, 294) / 100.0;
            double recharge = GetRawStatValue(raw, 210) / 100.0;
            double equipDelay = GetRawStatValue(raw, 211) / 100.0;
            lines.Add($"{ColorLabel("Speed")}");
            lines.Add($"{ColorValue($"Attack {attack:0.00}s")}");
            lines.Add($"{ColorValue($"Recharge {recharge:0.00}s")}");
            lines.Add($"{ColorLabel("Equip delay")} {ColorValue($"{equipDelay:0.00}s")}");

            int initStatId = GetRawStatValue(raw, 440);
            if (initStatId > 0)
            {
                string initName = AODataManager.Instance.GetStatName(initStatId);
                if (string.IsNullOrWhiteSpace(initName))
                    initName = $"Stat {initStatId}";
                lines.Add($"{ColorLabel("Initiative")} {ColorValue(ToUiStatName(initName))}");
            }

            lines.Add($"{ColorLabel("Location")} {ColorValue(location)}");

            int range = GetRawStatValue(raw, 287);
            if (range > 0)
                lines.Add($"{ColorLabel("Range")} {ColorValue($"{range}m")}");

            string dualWield = ResolveDualWieldText(raw);
            if (!string.IsNullOrWhiteSpace(dualWield))
                lines.Add($"{ColorLabel("Dual Wield")} {ColorValue(dualWield)}");

            string specials = ResolveWeaponSpecials(raw);
            if (!string.IsNullOrWhiteSpace(specials))
                lines.Add($"{ColorLabel("Special")} {ColorValue(specials)}");

            int mbs = GetRawStatValue(raw, 538);
            if (mbs > 0)
                lines.Add($"{ColorLabel("Max beneficial skill")} {ColorValue(mbs.ToString())}");

            string atkSkills = ResolveAttackDefenseSkills(raw?.AttackDefenseData?.Attack);
            if (!string.IsNullOrWhiteSpace(atkSkills))
                lines.Add($"{ColorLabel("Attack skills")} {ColorValue(atkSkills)}");

            string defSkills = ResolveAttackDefenseSkills(raw?.AttackDefenseData?.Defense);
            if (!string.IsNullOrWhiteSpace(defSkills))
                lines.Add($"{ColorLabel("Defence skills")} {ColorValue(defSkills)}");

            int dmgTypeStat = GetRawStatValue(raw, 436);
            if (dmgTypeStat > 0)
                lines.Add($"{ColorLabel("Damage type")} {ColorValue(ResolveDamageTypeName(dmgTypeStat))}");

            return string.Join("\n", lines);
        }

        private string ResolveDualWieldText(AO.Data.Core.Item raw)
        {
            int multiMelee = GetRawStatValue(raw, 101);
            int multiRanged = GetRawStatValue(raw, 134);

            if (multiMelee > 0)
                return $"Multi Melee {multiMelee}";
            if (multiRanged > 0)
                return $"Multi Ranged {multiRanged}";

            return string.Empty;
        }

        private string ResolveWeaponSpecials(AO.Data.Core.Item raw)
        {
            EnsureSlotNamesLoaded();
            uint mask = unchecked((uint)GetRawStatValue(raw, 30));
            if (mask == 0 || _canNameByBit.Count == 0)
                return string.Empty;

            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Burst", "FlingShot", "FullAuto", "AimedShot", "SneakAttack",
                "FastAttack", "Brawl", "Dimach"
            };

            var found = new List<string>();
            foreach (var kv in _canNameByBit.OrderBy(k => k.Key))
            {
                if (kv.Key == 0 || kv.Key > uint.MaxValue)
                    continue;

                uint bit = (uint)kv.Key;
                if ((mask & bit) != bit)
                    continue;
                if (!valid.Contains(kv.Value))
                    continue;

                found.Add(kv.Value);
            }

            return string.Join(", ", found.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string ResolveAttackDefenseSkills(IReadOnlyCollection<AO.Data.Core.RawStatValue> entries)
        {
            if (entries == null || entries.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.Stat <= 0 || entry.RawValue <= 0)
                    continue;

                string stat = AODataManager.Instance.GetStatName(entry.Stat);
                if (string.IsNullOrWhiteSpace(stat))
                    stat = $"Stat {entry.Stat}";

                parts.Add($"{ToUiStatName(stat)} {entry.RawValue}%");
            }

            return string.Join(", ", parts);
        }

        private static string ResolveDamageTypeName(int statId)
        {
            return statId switch
            {
                90 => "Projectile AC",
                91 => "Melee/ma AC",
                92 => "Energy AC",
                93 => "Chemical AC",
                94 => "Radiation AC",
                95 => "Cold AC",
                96 => "Disease AC",
                97 => "Fire AC",
                _ => $"Stat {statId}"
            };
        }

        private static string ToUiStatName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Unknown";

            return raw
                .Replace('_', '/')
                .Replace("EvadeClose", "Evade-ClsC")
                .Replace("MeleeInit", "Melee Weapons Initiative")
                .Replace("RangedInit", "Ranged Weapons Initiative")
                .Replace("PhysicalInit", "Physical Initiative")
                .Replace("MaxNanoEnergy", "Max Nano")
                .Replace("NanoInit", "NanoC. Init.")
                .Replace("SpaceTime", "Time&Space")
                .Replace("PsychologicalModification", "Psycho Modi")
                .Replace("SensoryImprovement", "Sensory Impr")
                .Replace("AimedShot", "Aimed Shot")
                .Replace("FlingShot", "Fling Shot")
                .Replace("FastAttack", "Fast Attack")
                .Trim();
        }

        private bool ValidateEquipRequirements(DataItemInstance dataItem, out string reason)
        {
            return ValidateEquipRequirements(dataItem, out reason, out _);
        }

        private bool ValidateEquipRequirements(DataItemInstance dataItem, out string reason, out List<string> failedReasons)
        {
            reason = string.Empty;
            failedReasons = new List<string>();
            if (Character == null || dataItem?.Definition == null)
            {
                reason = "Character or item data is unavailable.";
                failedReasons.Add(reason);
                return false;
            }

            var raw = AODataManager.Instance.Items?.FirstOrDefault(i => i != null && i.AOID == dataItem.DefinitionId);
            var actions = raw?.ActionData?.Actions;
            if (actions == null || actions.Count == 0)
                return true;

            foreach (var action in actions)
            {
                if (action == null || !IsEquipAction(action.Action) || action.Criteria == null)
                    continue;

                if (!EvaluateCriteriaGroups(action.Criteria, out var actionFailures, useVisualProfessionForProfessionStat: true))
                    failedReasons.AddRange(actionFailures);
            }

            if (failedReasons.Count > 0)
            {
                failedReasons = failedReasons
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                reason = failedReasons[0];
                return false;
            }

            return true;
        }

        public string GetItemCountBadgeText(ItemInstance item)
        {
            if (item == null || item.Quantity <= 1)
                return string.Empty;

            return IsStackable(item)
                ? item.Quantity.ToString()
                : $"{item.Quantity}c";
        }

        public Color GetItemCountBadgeColor(ItemInstance item)
        {
            if (item == null || item.Quantity <= 1)
                return Color.white;

            return IsStackable(item)
                ? Color.white
                : new Color(0.76f, 0.92f, 1f, 1f);
        }

        public bool TryGetProgramTooltip(int nanoId, out ItemTooltipInfo info)
        {
            info = null;
            if (nanoId <= 0)
                return false;

            var rawNano = ResolveRawNanoByProgramId(nanoId);
            if (rawNano == null)
                return false;

            string name = string.IsNullOrWhiteSpace(rawNano.Name) ? $"Nano {nanoId}" : rawNano.Name.Trim();
            string description = string.IsNullOrWhiteSpace(rawNano.Description) ? "No description." : rawNano.Description.Trim();
            string requirements = ResolveNanoRequirementText(rawNano);
            string effects = ResolveNanoEffectText(rawNano);
            string school = GetProgramSchoolTabName(nanoId);
            int nanoCost = GetProgramNanoCost(nanoId);
            int ncuCost = GetProgramNcuCost(nanoId);
            int duration = GetProgramDurationSeconds(nanoId);
            float attack = GetProgramAttackSeconds(nanoId);
            float recharge = GetProgramRechargeSeconds(nanoId);

            string body =
                $"{ColorLabel("School")} {ColorValue(school)}\n" +
                $"{ColorLabel("Nano Cost")} {ColorValue(Mathf.Max(0, nanoCost).ToString())}\n" +
                $"{ColorLabel("NCU Cost")} {ColorValue(Mathf.Max(0, ncuCost).ToString())}\n" +
                $"{ColorLabel("Attack")} {ColorValue($"{attack:0.##}s")}\n" +
                $"{ColorLabel("Recharge")} {ColorValue($"{recharge:0.##}s")}\n" +
                $"{ColorLabel("Duration")} {ColorValue(duration > 0 ? FormatSecondsAsClock(duration) : "Instant")}\n" +
                $"{ColorLabel("Requirements")}\n{ColorValue(requirements)}\n" +
                $"{ColorLabel("Effects")}\n{ColorValue(effects)}\n" +
                $"{ColorLabel("Description")}\n{ColorValue(description)}";

            info = new ItemTooltipInfo
            {
                Title = name,
                TitleColor = new Color(0.76f, 0.92f, 1f, 1f),
                Body = body
            };
            return true;
        }

        public bool TryGetActiveProgramTooltip(int activeId, out ItemTooltipInfo info)
        {
            info = null;
            if (activeId <= 0)
                return false;

            UpdateActiveProgramsInternal();
            var active = _activePrograms.FirstOrDefault(a => a != null && a.ActiveId == activeId);
            if (active == null)
                return false;

            if (!TryGetProgramTooltip(active.NanoId, out info) || info == null)
                return false;

            int remaining = GetRemainingSecondsForActiveProgram(activeId);
            string activeEffects = ResolveActiveProgramEffectText(active);
            info.Body =
                $"{info.Body}\n" +
                $"{ColorLabel("Remaining")} {ColorValue(FormatSecondsAsClock(remaining))}\n" +
                $"{ColorLabel("Running Effects")}\n{ColorValue(activeEffects)}";
            return true;
        }

        private static string BuildEquipBlockedStatusMessage(string fallbackReason, IReadOnlyCollection<string> requirementFailures = null)
        {
            if (requirementFailures != null && requirementFailures.Count > 0)
                return "Equip blocked:\n" + string.Join("\n", requirementFailures);

            if (string.IsNullOrWhiteSpace(fallbackReason))
                return "Equip blocked.";

            return $"Equip blocked: {fallbackReason}";
        }

        private static string BuildUploadBlockedStatusMessage(string fallbackReason, IReadOnlyCollection<string> requirementFailures = null)
        {
            if (requirementFailures != null && requirementFailures.Count > 0)
                return "Upload blocked:\n" + string.Join("\n", requirementFailures);

            if (string.IsNullOrWhiteSpace(fallbackReason))
                return "Upload blocked.";

            return $"Upload blocked: {fallbackReason}";
        }

        private bool TryUploadNanoProgramFromInstance(long instanceId, SlotZone? sourceZone = null, int sourceIndex = -1)
        {
            if (instanceId == 0)
                return false;

            var dataItem = AODataManager.Instance.GetItemInstance(instanceId);
            if (dataItem?.Definition == null)
                return false;

            var rawCrystal = AODataManager.Instance.GetRawItemByAoid(dataItem.DefinitionId);
            if (!TryExtractUploadNanoId(rawCrystal, out int nanoId))
            {
                if (IsLikelyNanoUploadItem(rawCrystal))
                {
                    SetStatus($"Upload failed: could not resolve the program ID for {dataItem.Definition.Name}.");
                    return true;
                }
                return false;
            }

            if (HasNativeSession) { SetStatus("Server nano uploading is not connected yet."); return true; }

            if (_uploadedProgramIds.Contains(nanoId))
            {
                string existingName = ResolveNanoDisplayName(nanoId, rawCrystal, dataItem);
                SetStatus($"Program already uploaded: {existingName}.");
                return true;
            }

            if (!ValidateNanoUploadRequirements(rawCrystal, out string reason, out var failures))
            {
                SetStatus(BuildUploadBlockedStatusMessage(reason, failures));
                return true;
            }

            var rawNano = ResolveRawNanoByProgramId(nanoId);
            if (!ValidateNanoCastRequirements(rawNano, out string castReason, out var castFailures))
            {
                SetStatus(BuildUploadBlockedStatusMessage(castReason, castFailures));
                return true;
            }

            var uploaded = BuildUploadedProgram(nanoId, rawCrystal, dataItem);
            if (uploaded == null)
            {
                SetStatus("Upload failed: nano data unavailable.");
                return true;
            }

            _uploadedPrograms.Add(uploaded);
            _uploadedProgramIds.Add(uploaded.NanoId);

            bool removedFromSource = false;
            if (sourceZone.HasValue && sourceIndex >= 0)
            {
                var located = GetSlotItem(sourceZone.Value, sourceIndex);
                if (located != null && located.InstanceId == instanceId)
                {
                    removedFromSource = RemoveFromZone(sourceZone.Value, sourceIndex, out var removedCore);
                    if (removedFromSource && removedCore != null)
                        Character.Inventory.Items.Remove(removedCore);
                }
            }

            if (!removedFromSource)
            {
                var core = AODataManager.Instance.GetCoreInstance(instanceId);
                if (core != null)
                    Character.Inventory.RemoveItem(core);
            }

            NotifyStateChanged();
            SetStatus($"Uploaded program: {uploaded.Name}.");
            return true;
        }

        private UploadedNanoProgram BuildUploadedProgram(int nanoId, AO.Data.Core.Item rawCrystal, DataItemInstance crystalData)
        {
            var rawNano = ResolveRawNanoByProgramId(nanoId);
            string name = ResolveNanoDisplayName(nanoId, rawCrystal, crystalData);
            string description = !string.IsNullOrWhiteSpace(rawNano?.Description)
                ? rawNano.Description.Trim()
                : (!string.IsNullOrWhiteSpace(crystalData?.Definition?.Description) ? crystalData.Definition.Description.Trim() : string.Empty);

            int iconId = GetRawStatValue(rawNano, 79);
            if (iconId <= 0)
                iconId = crystalData?.Definition?.IconId ?? 0;

            if (string.IsNullOrWhiteSpace(name))
                name = $"Nano Program {nanoId}";

            return new UploadedNanoProgram
            {
                NanoId = nanoId,
                RawNanoAoid = rawNano?.AOID ?? nanoId,
                Name = name,
                Description = description ?? string.Empty,
                IconId = iconId,
                SourceCrystalInstanceId = crystalData?.InstanceId ?? 0,
                SourceCrystalDefinitionId = crystalData?.DefinitionId ?? 0,
                SchoolTab = InferNanoSchoolTab(name, description),
                FallbackNcuCost = ResolveFallbackNanoNcuCost(rawCrystal, name, description),
                FallbackDurationSeconds = ResolveFallbackNanoDuration(name, description)
            };
        }

        private static string InferNanoSchoolTab(string name, string description)
        {
            string text = $"{name} {description}".ToLowerInvariant();
            if (ContainsAny(text, "shield", "reflect", "deflect", "barrier", "armor", "armour", "protection", "absorb"))
                return "Prot";
            if (ContainsAny(text, "heal", "medical", "health", "treatment", "regeneration", "wound", "life"))
                return "Medical";
            if (ContainsAny(text, "grid", "warp", "teleport", "beacon", "summon", "space", "quantum"))
                return "Space";
            if (ContainsAny(text, "damage", "weapon", "combat", "attack", "rage", "fury", "nuke", "projectile"))
                return "Combat";
            return "Psi";
        }

        private static int ResolveFallbackNanoNcuCost(AO.Data.Core.Item rawCrystal, string name, string description)
        {
            if (ResolveFallbackNanoDuration(name, description) <= 0)
                return 0;
            int ql = GetRawStatValue(rawCrystal, 54);
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, ql) / 10f), 1, 200);
        }

        private static int ResolveFallbackNanoDuration(string name, string description)
        {
            string text = $"{name} {description}".ToLowerInvariant();
            bool persistent = ContainsAny(text,
                "shield", "expertise", "buff", "increase", "surrounds", "protection", "armor", "armour",
                "reflect", "deflect", "barrier", "regeneration", "enhance", "boost");
            return persistent ? 3600 : 0;
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(text) || terms == null)
                return false;
            for (int i = 0; i < terms.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(terms[i]) && text.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private string ResolveNanoDisplayName(int nanoId, AO.Data.Core.Item rawCrystal, DataItemInstance crystalData)
        {
            var rawNano = ResolveRawNanoByProgramId(nanoId);
            if (!string.IsNullOrWhiteSpace(rawNano?.Name))
                return rawNano.Name.Trim();

            string crystalName = crystalData?.Definition?.Name ?? rawCrystal?.Name ?? string.Empty;
            const string prefix = "Nano Crystal (";
            if (crystalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && crystalName.EndsWith(")", StringComparison.Ordinal))
            {
                int start = prefix.Length;
                int length = crystalName.Length - start - 1;
                if (length > 0)
                    return crystalName.Substring(start, length).Trim();
            }

            return string.IsNullOrWhiteSpace(crystalName) ? $"Nano Program {nanoId}" : crystalName.Trim();
        }

        private bool ValidateNanoUploadRequirements(AO.Data.Core.Item rawCrystal, out string reason, out List<string> failedReasons)
        {
            reason = string.Empty;
            failedReasons = new List<string>();

            if (Character == null)
            {
                reason = "Character is not initialized.";
                failedReasons.Add(reason);
                return false;
            }

            var actions = rawCrystal?.ActionData?.Actions;
            if (actions == null || actions.Count == 0)
                return true;

            foreach (var action in actions)
            {
                if (action?.Criteria == null)
                    continue;

                if (!EvaluateCriteriaGroups(action.Criteria, out var actionFailures, useVisualProfessionForProfessionStat: true))
                    failedReasons.AddRange(actionFailures);
            }

            if (failedReasons.Count == 0)
                return true;

            failedReasons = failedReasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            reason = failedReasons[0];
            return false;
        }

        private bool ValidateNanoCastRequirements(AO.Data.Core.Item rawNano, out string reason, out List<string> failedReasons)
        {
            reason = string.Empty;
            failedReasons = new List<string>();

            if (Character == null)
            {
                reason = "Character is not initialized.";
                failedReasons.Add(reason);
                return false;
            }

            var actions = rawNano?.ActionData?.Actions;
            if (actions == null || actions.Count == 0)
                return true;

            foreach (var action in actions)
            {
                // Action 3 is cast/use requirement checks on nano programs.
                if (action?.Criteria == null || action.Criteria.Count == 0)
                    continue;

                if (!EvaluateCriteriaGroups(action.Criteria, out var actionFailures, useVisualProfessionForProfessionStat: true))
                    failedReasons.AddRange(actionFailures);
            }

            if (failedReasons.Count == 0)
                return true;

            failedReasons = failedReasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            reason = failedReasons[0];
            return false;
        }

        private bool EvaluateCriteriaGroups(IReadOnlyList<AO.Data.Core.ItemActionCriterion> criteria, out List<string> failures, bool useVisualProfessionForProfessionStat = false)
        {
            failures = new List<string>();
            if (criteria == null || criteria.Count == 0)
                return true;

            var groups = new List<List<AO.Data.Core.ItemActionCriterion>>();
            var currentGroup = new List<AO.Data.Core.ItemActionCriterion>();

            for (int i = 0; i < criteria.Count; i++)
            {
                var criterion = criteria[i];
                if (criterion == null)
                    continue;

                // Operator 4 acts as a separator between alternative requirement groups.
                if (criterion.Operator == 4)
                {
                    if (currentGroup.Count > 0)
                    {
                        groups.Add(currentGroup);
                        currentGroup = new List<AO.Data.Core.ItemActionCriterion>();
                    }
                    continue;
                }

                if (criterion.Value1 <= 0)
                    continue;

                currentGroup.Add(criterion);
            }

            if (currentGroup.Count > 0)
                groups.Add(currentGroup);

            if (groups.Count == 0)
                return true;

            var allFailures = new List<string>();
            foreach (var group in groups)
            {
                bool groupPassed = true;
                var groupFailures = new List<string>();

                for (int i = 0; i < group.Count; i++)
                {
                    var criterion = group[i];
                    if (IsRequirementMet(criterion, out string failReason, useVisualProfessionForProfessionStat))
                        continue;

                    groupPassed = false;
                    if (!string.IsNullOrWhiteSpace(failReason))
                        groupFailures.Add(failReason);
                }

                if (groupPassed)
                    return true;

                allFailures.AddRange(groupFailures);
            }

            failures = allFailures
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return false;
        }

        private bool AreCriteriaMet(IReadOnlyList<AO.Data.Core.ItemActionCriterion> criteria, bool useVisualProfessionForProfessionStat = false)
        {
            return EvaluateCriteriaGroups(criteria, out _, useVisualProfessionForProfessionStat);
        }

        private static bool TryExtractUploadNanoId(AO.Data.Core.Item rawCrystal, out int nanoId)
        {
            nanoId = 0;
            if (rawCrystal?.SpellData == null)
                return false;

            bool likelyNanoUploadItem = IsLikelyNanoUploadItem(rawCrystal);

            foreach (var group in rawCrystal.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null)
                        continue;

                    // 53019 is AO's upload-nano spell. Prefer structural data over
                    // display-name formatting from a particular ResourceDatabase.
                    if (spell.NanoID > 0
                        && (likelyNanoUploadItem || spell.SpellID == 53019))
                    {
                        nanoId = spell.NanoID;
                        return true;
                    }

                    if (TryParseNanoIdFromUploadDescription(spell.SpellDescription, out int parsed))
                    {
                        nanoId = parsed;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryParseNanoIdFromUploadDescription(string description, out int nanoId)
        {
            nanoId = 0;
            if (string.IsNullOrWhiteSpace(description))
                return false;

            Match match = Regex.Match(description, @"Upload\s+(\d+)\.", RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups.Count < 2)
                return false;

            return int.TryParse(match.Groups[1].Value, out nanoId) && nanoId > 0;
        }

        private bool IsRequirementMet(AO.Data.Core.ItemActionCriterion criterion, out string failReason, bool useVisualProfessionForProfessionStat = false)
        {
            failReason = string.Empty;

            int statId = criterion.Value1;
            int current = GetRequirementCurrentValue(statId, useVisualProfessionForProfessionStat);
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
                return true;

            string statName = ResolveRequirementStatName(statId);
            if (string.IsNullOrWhiteSpace(statName))
                statName = $"Stat {statId}";

            if (statId == 60)
            {
                string wantProf = AODataManager.Instance.GetProfessionLookup()
                    .FirstOrDefault(p => p.Key == rawTarget).Value ?? rawTarget.ToString();
                int currentProfessionForRequirement = useVisualProfessionForProfessionStat
                    ? GetCurrentVisualProfessionId()
                    : Character.ProfessionId;
                string haveProf = AODataManager.Instance.GetProfessionLookup()
                    .FirstOrDefault(p => p.Key == currentProfessionForRequirement).Value ?? currentProfessionForRequirement.ToString();
                failReason = criterion.Operator switch
                {
                    0 => $"Requires Profession {wantProf} (current: {haveProf}).",
                    1 => $"Requires Profession not {wantProf} (current: {haveProf}).",
                    _ => $"Requirement failed: {statName}."
                };
                return false;
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

            failReason = $"Requires {statName} {op} {displayTarget} (current: {current}).";
            return false;
        }

        private static string FormatSecondsAsClock(int seconds)
        {
            if (seconds <= 0)
                return "0s";

            int mins = seconds / 60;
            int rem = seconds % 60;
            return mins > 0 ? $"{mins}:{rem:00}" : $"{rem}s";
        }

        private string ResolveNanoRequirementText(AO.Data.Core.Item rawNano)
        {
            if (rawNano?.ActionData?.Actions == null)
                return "None";

            var requirementGroups = new List<string>();
            foreach (var action in rawNano.ActionData.Actions)
            {
                if (action?.Criteria == null || action.Criteria.Count == 0)
                    continue;

                var groups = new List<List<AO.Data.Core.ItemActionCriterion>>();
                var currentGroup = new List<AO.Data.Core.ItemActionCriterion>();
                for (int i = 0; i < action.Criteria.Count; i++)
                {
                    var criterion = action.Criteria[i];
                    if (criterion == null)
                        continue;
                    if (criterion.Operator == 4)
                    {
                        if (currentGroup.Count > 0)
                        {
                            groups.Add(currentGroup);
                            currentGroup = new List<AO.Data.Core.ItemActionCriterion>();
                        }
                        continue;
                    }

                    if (criterion.Value1 <= 0)
                        continue;
                    currentGroup.Add(criterion);
                }

                if (currentGroup.Count > 0)
                    groups.Add(currentGroup);
                if (groups.Count == 0)
                    continue;

                for (int g = 0; g < groups.Count; g++)
                {
                    var group = groups[g];
                    var groupParts = new List<string>();
                    for (int i = 0; i < group.Count; i++)
                    {
                        var criterion = group[i];
                        string statName = ResolveRequirementStatName(criterion.Value1);
                        if (string.IsNullOrWhiteSpace(statName))
                            statName = $"Stat {criterion.Value1}";
                        int displayTarget = criterion.Operator == 2 ? criterion.Value2 + 1 : criterion.Value2;
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
                        groupParts.Add($"{statName} {op} {displayTarget}");
                    }

                    if (groupParts.Count > 0)
                        requirementGroups.Add(string.Join(" and ", groupParts));
                }
            }

            if (requirementGroups.Count == 0)
                return "None";

            return string.Join(" OR ", requirementGroups.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string ResolveNanoEffectText(AO.Data.Core.Item rawNano)
        {
            if (rawNano?.SpellData == null || rawNano.SpellData.Count == 0)
                return "None";

            var effects = new List<string>();
            foreach (var group in rawNano.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null || spell.Stat <= 0)
                        continue;

                    string statName = AODataManager.Instance.GetStatName(spell.Stat);
                    if (string.IsNullOrWhiteSpace(statName))
                        statName = $"Stat {spell.Stat}";

                    string amountText;
                    if (spell.MinValue != 0 || spell.MaxValue != 0)
                    {
                        int low = Mathf.Min(spell.MinValue, spell.MaxValue);
                        int high = Mathf.Max(spell.MinValue, spell.MaxValue);
                        amountText = low == high ? low.ToString() : $"{low}..{high}";
                    }
                    else
                    {
                        int amount = spell.Amount;
                        amountText = amount.ToString();
                    }

                    if (IsPeriodicSpell(spell))
                    {
                        float intervalSeconds = ConvertTickIntervalToSeconds(spell.TickInterval);
                        int tickCount = Mathf.Max(1, spell.TickCount);
                        effects.Add($"{statName} {amountText} every {intervalSeconds:0.##}s ({tickCount} ticks)");
                    }
                    else
                    {
                        effects.Add($"{statName} {amountText}");
                    }
                }
            }

            if (effects.Count == 0)
                return "None";

            return string.Join("\n", effects.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string ResolveActiveProgramEffectText(ActiveNanoProgram active)
        {
            if (active == null)
                return "None";

            var lines = new List<string>();
            if (active.StatModifiers != null)
            {
                foreach (var pair in active.StatModifiers.OrderBy(p => p.Key))
                {
                    string statName = AODataManager.Instance.GetStatName(pair.Key);
                    if (string.IsNullOrWhiteSpace(statName))
                        statName = $"Stat {pair.Key}";
                    lines.Add($"{statName} {pair.Value:+#;-#;0}");
                }
            }

            if (active.PeriodicEffects != null)
            {
                for (int i = 0; i < active.PeriodicEffects.Count; i++)
                {
                    var effect = active.PeriodicEffects[i];
                    if (effect == null)
                        continue;

                    string statName = AODataManager.Instance.GetStatName(effect.StatId);
                    if (string.IsNullOrWhiteSpace(statName))
                        statName = $"Stat {effect.StatId}";
                    string amountText = (effect.MinValue != 0 || effect.MaxValue != 0)
                        ? $"{Mathf.Min(effect.MinValue, effect.MaxValue)}..{Mathf.Max(effect.MinValue, effect.MaxValue)}"
                        : effect.Amount.ToString();
                    lines.Add($"{statName} {amountText} every {Mathf.Max(0.05f, effect.TickIntervalSeconds):0.##}s ({Mathf.Max(0, effect.RemainingTicks)} left)");
                }
            }

            return lines.Count == 0 ? "None" : string.Join("\n", lines);
        }

        private int GetRequirementCurrentValue(int statId, bool useVisualProfessionForProfessionStat = false)
        {
            if (Character == null)
                return 0;

            if (statId == 60)
                return useVisualProfessionForProfessionStat ? GetCurrentVisualProfessionId() : Character.ProfessionId;
            if (statId == 37)
                return Character.GetCurrentTitleLevel();
            if (statId == 54)
                return Mathf.Max(1, Character.Level?.Level ?? 1);
            if (statId == WaitStateStatId)
                return IsCurrentlySitting() ? SittingWaitStateValue : 0;

            // Requirement checks must use buffed/final stats.
            int baseValue = Character.StatsContainer.GetFinalStat(statId);
            return baseValue + GetActiveProgramModifierSum(statId);
        }

        private Dictionary<int, int> ExtractNanoStatModifiers(AO.Data.Core.Item rawNano)
        {
            var result = new Dictionary<int, int>();
            if (rawNano?.SpellData == null)
                return result;

            foreach (var group in rawNano.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null || spell.Stat <= 0)
                        continue;
                    if (!AreSpellCriteriaMet(spell))
                        continue;
                    if (IsPeriodicSpell(spell))
                        continue;

                    int amount = ResolveSpellAmount(spell);
                    if (amount == 0 && spell.Stat == VisualProfessionStatId && TryParseVisualProfessionFromSpell(spell, out int visualProf))
                        amount = visualProf - CharacterProfessionId;

                    if (amount == 0)
                        continue;

                    if (result.TryGetValue(spell.Stat, out int current))
                        result[spell.Stat] = current + amount;
                    else
                        result[spell.Stat] = amount;
                }
            }

            return result;
        }

        private List<PeriodicNanoEffect> ExtractPeriodicNanoEffects(AO.Data.Core.Item rawNano, float startedAt)
        {
            var result = new List<PeriodicNanoEffect>();
            if (rawNano?.SpellData == null)
                return result;

            foreach (var group in rawNano.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (!IsPeriodicSpell(spell) || spell.Stat <= 0)
                        continue;
                    if (!AreSpellCriteriaMet(spell))
                        continue;

                    int tickCount = Mathf.Max(1, spell.TickCount);
                    float intervalSeconds = ConvertTickIntervalToSeconds(spell.TickInterval);
                    result.Add(new PeriodicNanoEffect
                    {
                        StatId = spell.Stat,
                        Amount = spell.Amount,
                        MinValue = spell.MinValue,
                        MaxValue = spell.MaxValue,
                        RemainingTicks = tickCount,
                        TickIntervalSeconds = intervalSeconds,
                        NextTickAt = startedAt
                    });
                }
            }

            return result;
        }

        private bool ApplyInstantNanoEffects(AO.Data.Core.Item rawNano, bool applyNow)
        {
            if (!applyNow || rawNano?.SpellData == null)
                return false;

            bool changed = false;
            foreach (var group in rawNano.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null || spell.Stat <= 0)
                        continue;
                    if (!AreSpellCriteriaMet(spell))
                        continue;

                    int delta = ResolveSpellAmount(spell);
                    if (delta == 0)
                        continue;

                    if (spell.Stat == StatIds.Health || spell.Stat == StatIds.RemainingHealth)
                    {
                        int max = GetMaxHealthValue();
                        int current = GetCurrentHealthValue();
                        int next = Mathf.Clamp(current + delta, 0, max);
                        if (next != current)
                        {
                            _localHealthCurrent = next;
                            changed = true;
                        }
                        continue;
                    }

                    if (spell.Stat == StatIds.CurrentNano || spell.Stat == StatIds.NanoPool)
                    {
                        int max = GetMaxNanoValue();
                        int current = GetCurrentNanoValue();
                        int next = Mathf.Clamp(current + delta, 0, max);
                        if (next != current)
                        {
                            _localNanoCurrent = next;
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        private static int ResolveSpellAmount(AO.Data.Core.ItemSpellData spell)
        {
            if (spell == null)
                return 0;

            if (spell.MinValue != 0 || spell.MaxValue != 0)
            {
                int low = Mathf.Min(spell.MinValue, spell.MaxValue);
                int high = Mathf.Max(spell.MinValue, spell.MaxValue);
                if (low == high)
                    return low;

                return UnityEngine.Random.Range(low, high + 1);
            }

            if (spell.Amount != 0)
                return spell.Amount;

            int a = CoerceToInt(spell.A);
            int b = CoerceToInt(spell.B);
            if (a != 0 || b != 0)
            {
                int low = Mathf.Min(a, b);
                int high = Mathf.Max(a, b);
                if (low == high)
                    return low;

                return UnityEngine.Random.Range(low, high + 1);
            }

            return 0;
        }

        private bool AreSpellCriteriaMet(AO.Data.Core.ItemSpellData spell)
        {
            if (spell?.Criteria == null || spell.Criteria.Count == 0)
                return true;
            return AreCriteriaMet(spell.Criteria);
        }

        private int GetActiveProgramRawModifierSum(int statId)
        {
            if (statId <= 0)
                return 0;

            UpdateActiveProgramsInternal();
            int total = 0;
            for (int i = 0; i < _activePrograms.Count; i++)
            {
                var active = _activePrograms[i];
                if (active == null || active.NanoId <= 0)
                    continue;

                var rawNano = ResolveRawNanoForActive(active);
                if (rawNano?.SpellData == null)
                    continue;

                foreach (var group in rawNano.SpellData)
                {
                    if (group?.Items == null)
                        continue;

                    foreach (var spell in group.Items)
                    {
                        if (spell == null || spell.Stat != statId)
                            continue;
                        if (IsPeriodicSpell(spell))
                            continue;

                        total += ResolveSpellAmountForModifierSnapshot(spell);
                    }
                }
            }

            return total;
        }

        private int GetActiveProgramNcuCapacityBonus()
        {
            UpdateActiveProgramsInternal();
            int total = 0;
            for (int i = 0; i < _activePrograms.Count; i++)
            {
                var active = _activePrograms[i];
                if (active == null)
                    continue;

                // Prefer already-resolved modifier snapshot if present.
                if (active.StatModifiers != null && active.StatModifiers.TryGetValue(MaxNcuStatId, out int resolved))
                    total += resolved;
                else if (active.NanoId > 0 || active.RawNanoAoid > 0)
                {
                    // Fallback to raw nano data path from nanos.json.
                    var rawNano = ResolveRawNanoForActive(active);
                    total += ResolveRawNanoModifierSum(rawNano, MaxNcuStatId);
                }
            }

            return Mathf.Max(0, total);
        }

        private static int ResolveRawNanoModifierSum(AO.Data.Core.Item rawNano, int statId)
        {
            if (rawNano?.SpellData == null || statId <= 0)
                return 0;

            int total = 0;
            foreach (var group in rawNano.SpellData)
            {
                if (group?.Items == null)
                    continue;
                foreach (var spell in group.Items)
                {
                    if (spell == null || spell.Stat != statId)
                        continue;
                    if (spell.TickCount > 1 || spell.TickInterval > 0)
                        continue;
                    total += ResolveSpellAmountForModifierSnapshot(spell);
                }
            }

            return total;
        }

        private static int ResolveSpellAmountForModifierSnapshot(AO.Data.Core.ItemSpellData spell)
        {
            if (spell == null)
                return 0;

            if (spell.Amount != 0)
                return spell.Amount;

            if (spell.MinValue != 0 || spell.MaxValue != 0)
            {
                int low = Mathf.Min(spell.MinValue, spell.MaxValue);
                int high = Mathf.Max(spell.MinValue, spell.MaxValue);
                return low == high ? low : high;
            }

            int a = CoerceToInt(spell.A);
            int b = CoerceToInt(spell.B);
            if (a != 0 || b != 0)
            {
                int low = Mathf.Min(a, b);
                int high = Mathf.Max(a, b);
                return low == high ? low : high;
            }

            return 0;
        }

        private static int ResolvePeriodicAmount(PeriodicNanoEffect effect)
        {
            if (effect == null)
                return 0;

            if (effect.MinValue != 0 || effect.MaxValue != 0)
            {
                int low = Mathf.Min(effect.MinValue, effect.MaxValue);
                int high = Mathf.Max(effect.MinValue, effect.MaxValue);
                if (low == high)
                    return low;
                return UnityEngine.Random.Range(low, high + 1);
            }

            return effect.Amount;
        }

        private static int CoerceToInt(object value)
        {
            if (value == null)
                return 0;

            if (value is int i)
                return i;

            if (value is long l)
                return (int)Mathf.Clamp(l, int.MinValue, int.MaxValue);

            if (value is float f)
                return Mathf.RoundToInt(f);

            if (value is double d)
                return Mathf.RoundToInt((float)d);

            if (value is string s && int.TryParse(s, out int parsed))
                return parsed;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private int ResolveDynamicDeltaValue(int statId, bool isSitting)
        {
            float percent = isSitting ? 0.02f : 0.01f;
            int max = statId == NanoDeltaStatId ? GetMaxNanoValue() : GetMaxHealthValue();
            int baseFromPercent = Mathf.FloorToInt(max * percent);
            return Mathf.Max(0, baseFromPercent + GetModifiedStatValueRaw(statId));
        }

        private int GetModifiedStatValueRaw(int statId)
        {
            return Character?.StatsContainer?.GetModifiedStat(statId) ?? 0;
        }

        private bool ApplyRegenTick(bool isSitting)
        {
            float percent = isSitting ? 0.02f : 0.01f;
            bool changed = false;

            int healthMax = GetMaxHealthValue();
            int nanoMax = GetMaxNanoValue();
            int healthCurrent = GetCurrentHealthValue();
            int nanoCurrent = GetCurrentNanoValue();

            int healFromPercent = Mathf.FloorToInt((healthMax * percent) + _healthRegenCarry);
            _healthRegenCarry = (healthMax * percent + _healthRegenCarry) - healFromPercent;
            int healFromStat = Mathf.Max(0, GetModifiedStatValueRaw(HealDeltaStatId) + GetActiveProgramModifierSum(HealDeltaStatId));
            int healTotal = Mathf.Max(0, healFromPercent + healFromStat);
            if (healTotal > 0 && healthCurrent < healthMax)
            {
                _localHealthCurrent = Mathf.Clamp(healthCurrent + healTotal, 0, healthMax);
                changed = true;
            }

            int nanoFromPercent = Mathf.FloorToInt((nanoMax * percent) + _nanoRegenCarry);
            _nanoRegenCarry = (nanoMax * percent + _nanoRegenCarry) - nanoFromPercent;
            int nanoFromStat = Mathf.Max(0, GetModifiedStatValueRaw(NanoDeltaStatId) + GetActiveProgramModifierSum(NanoDeltaStatId));
            int nanoTotal = Mathf.Max(0, nanoFromPercent + nanoFromStat);
            if (nanoTotal > 0 && nanoCurrent < nanoMax)
            {
                _localNanoCurrent = Mathf.Clamp(nanoCurrent + nanoTotal, 0, nanoMax);
                changed = true;
            }

            return changed;
        }

        private bool IsCurrentlySitting()
        {
            if (_runtimeBridge == null)
                return false;

            var appearance = _runtimeBridge.GetComponent<CharacterAppearanceController>();
            return appearance != null && appearance.IsSitting;
        }

        private static float ConvertAttackDelayToSeconds(int attackDelayRaw)
        {
            if (attackDelayRaw <= 0)
                return 1f;

            return attackDelayRaw / 100f;
        }

        private static float ConvertRechargeDelayToSeconds(int rechargeDelayRaw)
        {
            if (rechargeDelayRaw <= 0)
                return 1f;

            return rechargeDelayRaw / 100f;
        }

        private bool TryResolveAttackProfile(
            out AO.Data.Core.Item rawWeapon,
            out List<(int StatId, float Weight)> skillSplits,
            out int mbs,
            out float cycleSeconds)
        {
            rawWeapon = null;
            skillSplits = new List<(int StatId, float Weight)>();
            mbs = 0;
            cycleSeconds = 1f;

            if (Character?.Equipment == null || AODataManager.Instance == null)
                return false;

            var equipped = Character.Equipment.GetAllEquipped();
            if (equipped == null)
                return false;

            long weaponInstanceId = 0;
            if (equipped.TryGetValue(RightHandSlotId, out var rightId) && rightId > 0)
                weaponInstanceId = rightId;
            else if (equipped.TryGetValue(LeftHandSlotId, out var leftId) && leftId > 0)
                weaponInstanceId = leftId;
            if (weaponInstanceId == 0)
            {
                int maStatId = ResolveStatIdFromToken("MartialArts");
                if (maStatId <= 0)
                    maStatId = MartialArtsFallbackStatId;
                skillSplits.Add((maStatId, 1f));
                mbs = 3000;
                var unarmed = ResolveUnarmedMartialArtsStats();
                cycleSeconds = Mathf.Clamp(unarmed.AttackSeconds + unarmed.RechargeSeconds, 0.2f, 10f);
                return true;
            }

            var core = AODataManager.Instance.GetCoreInstance(weaponInstanceId);
            int aoid = core?.Definition?.AOID ?? (int)weaponInstanceId;
            rawWeapon = AODataManager.Instance.GetRawItemByAoid(aoid);
            if (rawWeapon == null)
                return false;

            if (rawWeapon.AttackDefenseData?.Attack != null && rawWeapon.AttackDefenseData.Attack.Count > 0)
            {
                float sum = 0f;
                for (int i = 0; i < rawWeapon.AttackDefenseData.Attack.Count; i++)
                {
                    var attackEntry = rawWeapon.AttackDefenseData.Attack[i];
                    if (attackEntry == null || attackEntry.Stat <= 0 || attackEntry.RawValue <= 0)
                        continue;
                    if (!WeaponAttackSkillStatIds.Contains(attackEntry.Stat))
                        continue;
                    sum += attackEntry.RawValue;
                }

                if (sum > 0f)
                {
                    for (int i = 0; i < rawWeapon.AttackDefenseData.Attack.Count; i++)
                    {
                        var attackEntry = rawWeapon.AttackDefenseData.Attack[i];
                        if (attackEntry == null || attackEntry.Stat <= 0 || attackEntry.RawValue <= 0)
                            continue;
                        if (!WeaponAttackSkillStatIds.Contains(attackEntry.Stat))
                            continue;
                        skillSplits.Add((attackEntry.Stat, attackEntry.RawValue / sum));
                    }
                }
            }

            if (skillSplits.Count == 0)
            {
                int primary = 0;
                int best = 0;
                if (rawWeapon.StatModifiers != null)
                {
                    for (int i = 0; i < rawWeapon.StatModifiers.Count; i++)
                    {
                        var mod = rawWeapon.StatModifiers[i];
                        if (mod == null || mod.Value <= 0 || !WeaponAttackSkillStatIds.Contains(mod.StatId))
                            continue;
                        if (mod.Value > best)
                        {
                            best = mod.Value;
                            primary = mod.StatId;
                        }
                    }
                }
                if (primary <= 0)
                    primary = 112; // pistol fallback
                skillSplits.Add((primary, 1f));
            }

            mbs = Mathf.Max(0, GetRawStatValue(rawWeapon, MaxBeneficialSkillStatId));
            float attack = ConvertAttackDelayToSeconds(GetRawStatValue(rawWeapon, AttackDelayStatId));
            float recharge = ConvertRechargeDelayToSeconds(GetRawStatValue(rawWeapon, RechargeDelayStatId));
            cycleSeconds = Mathf.Clamp(attack + recharge, 0.2f, 10f);
            return true;
        }

        private int ResolveTargetArmorClassForWeapon(AO.Data.Core.Item rawWeapon)
        {
            int damageType = rawWeapon == null
                ? DamageTypeMelee
                : Mathf.Max(0, GetRawStatValue(rawWeapon, WeaponDamageTypeStatId));
            int acStatId = damageType switch
            {
                DamageTypeProjectile => StatIds.ProjectileAC,
                DamageTypeMelee => StatIds.MeleeAC,
                DamageTypeEnergy => StatIds.EnergyAC,
                DamageTypeChemical => StatIds.ChemicalAC,
                DamageTypeRadiation => StatIds.RadiationAC,
                DamageTypeCold => StatIds.ColdAC,
                DamageTypePoison => StatIds.PoisonAC,
                DamageTypeFire => StatIds.FireAC,
                _ => StatIds.ProjectileAC
            };

            if (_selectedTarget?.Character?.StatsContainer != null)
                return Mathf.Max(0, _selectedTarget.Character.StatsContainer.GetFinalStat(acStatId));

            var runtimeCombat = _selectedTarget != null ? _selectedTarget.GetComponent<RuntimeDynelCombatState>() : null;
            return Mathf.Max(0, runtimeCombat?.ArmorClass ?? 0);
        }

        private bool TryApplyDamageToSelectedTarget(int amount, out string targetName, out int remainingHealth, out bool killed)
        {
            targetName = ResolveTargetName(_selectedTarget);
            remainingHealth = 0;
            killed = false;

            if (_selectedTarget == null || amount <= 0)
                return false;

            if (TryApplyAuthoritativeRuntimeMobDamage(_selectedTarget, amount))
            {
                var authoritativeCombat = _selectedTarget.GetComponent<RuntimeDynelCombatState>();
                remainingHealth = authoritativeCombat != null ? authoritativeCombat.CurrentHealth : 0;
                return true;
            }

            if (_selectedTarget.Character?.StatsContainer != null)
            {
                int current = Mathf.Max(0, _selectedTarget.Character.StatsContainer.GetFinalStat(StatIds.Health));
                if (current <= 0)
                {
                    remainingHealth = 0;
                    return false;
                }
                int max = Mathf.Max(1, _selectedTarget.Character.StatsContainer.GetFinalStat(StatIds.MaxHealth));
                int next = Mathf.Clamp(current - amount, 0, max);

                string healthName = AODataManager.Instance.GetStatName(StatIds.Health);
                if (string.IsNullOrWhiteSpace(healthName))
                    healthName = "Health";
                _selectedTarget.Character.SetBaseStatValue(healthName, next);
                _selectedTarget.Character.StatsContainer.Recalculate();
                _selectedTarget.Character.RecalculateDerivedStats();

                remainingHealth = next;
                killed = current > 0 && next <= 0;
                if (killed)
                {
                    MarkTargetAsRemains(_selectedTarget);
                    var deadCombatState = _selectedTarget.GetComponent<RuntimeDynelCombatState>();
                    if (deadCombatState != null)
                    {
                        // Runtime combat state is the single death-animation authority.
                        // Avoid parallel death drivers that can interrupt/freeze one-shots.
                        deadCombatState.BeginCorpseSettle(2.0f);
                    }
                    else
                    {
                        float deathAnimDuration = TryPlayTargetDeathAnimation(_selectedTarget);
                        DisableTargetMotionAndCombat(_selectedTarget);
                    }
                }
                return true;
            }

            var runtimeCombat = _selectedTarget.GetComponent<RuntimeDynelCombatState>();
            if (runtimeCombat == null)
                return false;

            int before = runtimeCombat.CurrentHealth;
            int after = runtimeCombat.ApplyDamage(amount);
            remainingHealth = after;
            killed = before > 0 && after <= 0;
            if (killed)
            {
                MarkTargetAsRemains(_selectedTarget);
                // Runtime combat state now starts death playback on HP->0 directly.
                // Do not trigger a second death path from UI context.
                runtimeCombat.BeginCorpseSettle(2.0f);
            }
            return true;
        }

        private bool TryStartAuthoritativeRuntimeMobCombat(CharacterRuntimeBridge target)
        {
            if (!TryGetAuthoritativeRuntimeEntityId(target, out string entityId))
                return false;

            var client = _networkClient != null ? _networkClient : AuthoritativeNetworkClient.ActiveInstance;
            if (client == null || !client.IsConnected)
                return false;

            client.RequestRuntimeAttack(entityId);
            return true;
        }

        private bool TryApplyAuthoritativeRuntimeMobDamage(CharacterRuntimeBridge target, int amount)
        {
            if (amount <= 0 || !TryGetAuthoritativeRuntimeEntityId(target, out string entityId))
                return false;

            var client = _networkClient != null ? _networkClient : AuthoritativeNetworkClient.ActiveInstance;
            if (client == null || !client.IsConnected)
                return false;

            client.RequestRuntimeAttack(entityId, amount);
            return true;
        }

        private static bool TryGetAuthoritativeRuntimeEntityId(CharacterRuntimeBridge target, out string entityId)
        {
            entityId = string.Empty;
            if (target == null)
                return false;

            var runtimeIdentity = target.GetComponentInParent<AuthoritativeRuntimeEntityIdentity>();
            if (runtimeIdentity == null || !runtimeIdentity.Attackable || string.IsNullOrWhiteSpace(runtimeIdentity.EntityId))
                return false;

            entityId = runtimeIdentity.EntityId;
            return true;
        }

        private static void MarkTargetAsRemains(CharacterRuntimeBridge target)
        {
            if (target == null)
                return;

            string currentName = ResolveTargetName(target);
            if (string.IsNullOrWhiteSpace(currentName))
                currentName = "Unknown";

            if (currentName.StartsWith("Remains of ", StringComparison.OrdinalIgnoreCase))
                return;

            target.DisplayNameOverride = $"Remains of {currentName.Trim()}";
        }

        private static void DisableTargetMotionAndCombat(CharacterRuntimeBridge target)
        {
            if (target == null)
                return;

            var walker = target.GetComponent<PrototypeWalkerController>();
            if (walker != null && walker.enabled)
                walker.enabled = false;

            // Prevent generic idle/move controller from immediately overriding death one-shots.
            var animationController = target.GetComponent<CharacterAnimationController>();
            if (animationController != null && animationController.enabled)
                animationController.enabled = false;

            // Do not pause the appearance controller immediately on death;
            // allowing it to continue briefly ensures death one-shots can play.
        }

        private static float TryPlayTargetDeathAnimation(CharacterRuntimeBridge target)
        {
            if (target == null)
                return 0f;

            ForceStopIdleLikeAnimations(target.gameObject);

            var appearance = target.GetComponent<CharacterAppearanceController>();

            string lowerName = string.Empty;
            if (!string.IsNullOrWhiteSpace(target.DisplayNameOverride))
                lowerName = target.DisplayNameOverride.Trim().ToLowerInvariant();
            else if (target.gameObject != null && !string.IsNullOrWhiteSpace(target.gameObject.name))
                lowerName = target.gameObject.name.Trim().ToLowerInvariant();

            var candidates = new List<string>();
            if (lowerName.Contains("snake"))
            {
                candidates.Add("sewersnake_die-pain_01_01");
                candidates.Add("giant_snake_die-pain_01_01");
                candidates.Add("giant_snake_diedefault_01_01");
            }
            if (lowerName.Contains("viper") || lowerName.Contains("cobra") || lowerName.Contains("asp"))
            {
                candidates.Add("sewersnake_die-pain_01_01");
                candidates.Add("giant_snake_die-pain_01_01");
                candidates.Add("giant_snake_diedefault_01_01");
            }
            if (lowerName.Contains("leet"))
            {
                candidates.Add("shadowleet_DieDefault_01_01");
                candidates.Add("gingerleet_die");
            }
            if (lowerName.Contains("reet") || lowerName.Contains("bird"))
                candidates.Add("cute-birdy_die-pain_01_01");
            if (lowerName.Contains("lizard") || lowerName.Contains("surf"))
                candidates.Add("lizard-green_die-pain_01_01");
            if (lowerName.Contains("gecko") || lowerName.Contains("iguana") || lowerName.Contains("drake"))
                candidates.Add("lizard-green_die-pain_01_01");

            candidates.Add("default_monster_die-pain_01_01");
            candidates.Add("DieDefault");
            candidates.Add("die-pain");
            candidates.Add("die");
            candidates.Add("male_die-pain_01_01");
            candidates.Add("female_die-pain_01_01");
            candidates.Add("athrox_die-pain_01_01");

            bool played = false;
            float duration = 0f;
            // Prefer direct clip playback first for determinism; this bypasses action-key resolution
            // and state-machine timing quirks.
            played = TryPlayDeathClipDirectly(target.gameObject, candidates, out duration);
            if (!played && appearance != null)
                played = appearance.TryPlayDeathOneShot(candidates, out duration);

            if (!played)
                Debug.LogWarning($"[DeathAnim] Failed to start death animation for '{target.name}'.");

            return duration;
        }

        private static void ForceStopIdleLikeAnimations(GameObject host)
        {
            if (host == null)
                return;

            var legacyAnimations = host.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var anim = legacyAnimations[i];
                if (anim == null)
                    continue;
                anim.Stop();
            }

            var animators = host.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;
                animator.speed = 1f;
                // Keep animator enabled for death playback, but reset transient motion.
                animator.ResetTrigger("Attack");
                animator.ResetTrigger("Hit");
            }
        }

        private static bool TryPlayDeathClipDirectly(GameObject host, IReadOnlyList<string> candidates, out float duration)
        {
            duration = 0f;
            if (host == null || candidates == null || candidates.Count == 0)
                return false;

            static bool NameMatches(string clipName, string desired)
            {
                if (string.IsNullOrWhiteSpace(clipName) || string.IsNullOrWhiteSpace(desired))
                    return false;
                return clipName.Equals(desired, StringComparison.OrdinalIgnoreCase)
                    || clipName.EndsWith(desired, StringComparison.OrdinalIgnoreCase)
                    || clipName.IndexOf(desired, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            var legacyAnimations = host.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var anim = legacyAnimations[i];
                if (anim == null)
                    continue;

                for (int c = 0; c < candidates.Count; c++)
                {
                    string desired = candidates[c];
                    if (string.IsNullOrWhiteSpace(desired))
                        continue;

                    foreach (AnimationState state in anim)
                    {
                        if (state == null || string.IsNullOrWhiteSpace(state.name))
                            continue;
                        if (!NameMatches(state.name, desired))
                            continue;

                        anim.Stop();
                        anim.Play(state.name);
                        duration = Mathf.Max(0.05f, state.length);
                        return true;
                    }
                }
            }

            // Fallback: use any death-like legacy clip name if explicit candidates missed.
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var anim = legacyAnimations[i];
                if (anim == null)
                    continue;

                foreach (AnimationState state in anim)
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.name))
                        continue;
                    string n = state.name;
                    if (n.IndexOf("die", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("death", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("dead", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("collapse", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("fall", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    anim.Stop();
                    anim.Play(state.name);
                    duration = Mathf.Max(0.05f, state.length);
                    return true;
                }
            }

            var animators = host.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;

                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips == null || clips.Length == 0)
                    continue;

                for (int c = 0; c < candidates.Count; c++)
                {
                    string desired = candidates[c];
                    if (string.IsNullOrWhiteSpace(desired))
                        continue;

                    for (int k = 0; k < clips.Length; k++)
                    {
                        var clip = clips[k];
                        if (clip == null || string.IsNullOrWhiteSpace(clip.name))
                            continue;
                        if (!NameMatches(clip.name, desired))
                            continue;

                        if (TryPlayAnimatorClipFallback(host, animator, clip, out duration))
                            return true;
                    }
                }
            }

            // Animator fallback: any death-like clip name.
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;

                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips == null || clips.Length == 0)
                    continue;

                for (int k = 0; k < clips.Length; k++)
                {
                    var clip = clips[k];
                    if (clip == null || string.IsNullOrWhiteSpace(clip.name))
                        continue;

                    string n = clip.name;
                    if (n.IndexOf("die", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("death", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("dead", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("collapse", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("fall", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (TryPlayAnimatorClipFallback(host, animator, clip, out duration))
                        return true;
                }
            }

            return false;
        }

        private static bool IsLikelyNanoUploadItem(AO.Data.Core.Item item)
        {
            if (item == null)
                return false;

            string compactName = Regex.Replace(
                item.Name ?? string.Empty, @"[\s_-]+", string.Empty);
            if (compactName.IndexOf("nanocrystal", StringComparison.OrdinalIgnoreCase) >= 0
                || compactName.IndexOf("instructiondisc", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (item.SpellData == null)
                return false;
            return item.SpellData.Any(group => group?.Items != null
                && group.Items.Any(spell => spell != null && spell.SpellID == 53019));
        }

        private static bool TryPlayAnimatorClipFallback(GameObject host, Animator animator, AnimationClip clip, out float duration)
        {
            duration = 0f;
            if (host == null || animator == null || clip == null)
                return false;

            // Always force one-shot legacy playback for death clips so Animator state names / transitions
            // cannot intermittently block or override death playback.
            var legacyHost = animator.gameObject != null ? animator.gameObject : host;
            var legacy = legacyHost.GetComponent<Animation>();
            if (legacy == null)
                legacy = legacyHost.AddComponent<Animation>();

            clip.legacy = true;
            if (legacy.GetClip(clip.name) == null)
                legacy.AddClip(clip, clip.name);
            legacy.wrapMode = WrapMode.Once;
            var st = legacy[clip.name];
            if (st != null)
                st.wrapMode = WrapMode.Once;
            animator.enabled = false;
            legacy.Play(clip.name);

            duration = Mathf.Max(0.25f, clip.length);
            return true;
        }

        private static string ResolveTargetName(CharacterRuntimeBridge target)
        {
            if (target == null)
                return "Unknown";

            if (!string.IsNullOrWhiteSpace(target.DisplayNameOverride))
                return target.DisplayNameOverride.Trim();

            var character = target.Character;
            if (character != null)
            {
                var prop = character.GetType().GetProperty("Name");
                if (prop != null)
                {
                    var value = prop.GetValue(character) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(target.gameObject?.name) ? "Unknown" : target.gameObject.name;
        }

        private string ResolveModifierLine(AO.Data.Core.ItemSpellData spell)
        {
            if (spell.Stat <= 0 || spell.Amount == 0)
                return null;

            string statName = AODataManager.Instance.GetStatName(spell.Stat);
            if (string.IsNullOrWhiteSpace(statName))
                statName = $"Stat {spell.Stat}";

            return $"Modify {ToUiStatName(statName)} {spell.Amount}";
        }

        private string GetSlotName(int slotId)
        {
            EnsureSlotNamesLoaded();
            if (TryGetCanonicalSocialSlot(slotId, out int socialCanonical))
            {
                if (_socialSlotNameById.TryGetValue(socialCanonical, out var socialName) && !string.IsNullOrWhiteSpace(socialName))
                    return $"Social {socialName}";
            }

            int canonicalWeaponSlot = ToCanonicalWeaponSlotId(slotId);
            if (canonicalWeaponSlot != slotId)
            {
                if (_weaponSlotNameById.TryGetValue(canonicalWeaponSlot, out var weaponName) && !string.IsNullOrWhiteSpace(weaponName))
                    return weaponName;
            }

            int canonicalArmorSlot = ToCanonicalArmorSlotId(slotId);
            if (canonicalArmorSlot != slotId)
            {
                if (_armorSlotNameById.TryGetValue(canonicalArmorSlot, out var armorName) && !string.IsNullOrWhiteSpace(armorName))
                    return armorName;
            }

            if (slotId >= 101 && slotId <= 199)
            {
                int canonicalImplant = slotId - 100;
                if (_implantSlotNameById.TryGetValue(canonicalImplant, out var implantName) && !string.IsNullOrWhiteSpace(implantName))
                    return implantName;
            }

            if (_slotNameById.TryGetValue(slotId, out var name) && !string.IsNullOrWhiteSpace(name))
                return name;

            return $"Slot {slotId}";
        }

        private void EnsureSlotNamesLoaded()
        {
            if (_slotNamesLoaded)
                return;

            _slotNamesLoaded = true;
            LoadSlotNamesFromFile("weapon_slots.json", _weaponSlotNameById, mergeIntoGeneral: true);
            LoadSlotNamesFromFile("armor_slots.json", _armorSlotNameById, mergeIntoGeneral: true);
            LoadSlotNamesFromFile("implant_slots.json", _implantSlotNameById, mergeIntoGeneral: true);
            LoadSlotNamesFromFile("social_slots.json", _socialSlotNameById, mergeIntoGeneral: false);
            LoadSlotBitNamesFromFile("weapon_slots.json", _weaponSlotNameByBit);
            LoadSlotBitNamesFromFile("armor_slots.json", _armorSlotNameByBit);
            LoadSlotBitNamesFromFile("implant_slots.json", _implantSlotNameByBit);
            LoadSlotBitNamesFromFile("can_flag.json", _canNameByBit);
        }

        private void LoadSlotNamesFromFile(string fileName, Dictionary<int, string> perCategoryTarget, bool mergeIntoGeneral)
        {
            perCategoryTarget.Clear();
            string path = Path.Combine(Application.streamingAssetsPath, "AOData", fileName);
            if (!File.Exists(path))
                return;

            string json = File.ReadAllText(path);

            List<SlotEntry> entries = null;
            try
            {
                entries = JsonConvert.DeserializeObject<List<SlotEntry>>(json);
            }
            catch
            {
                // Ignore and try lookup format below.
            }

            if (entries == null || entries.Count == 0)
            {
                try
                {
                    var lookup = JsonConvert.DeserializeObject<SlotLookupFile>(json);
                    if (lookup?.namesById != null)
                    {
                        entries = new List<SlotEntry>();
                        foreach (var pair in lookup.namesById)
                        {
                            if (!int.TryParse(pair.Key, out int id))
                                continue;

                            entries.Add(new SlotEntry { Id = id, Name = pair.Value });
                        }
                    }
                }
                catch
                {
                    // No-op.
                }
            }

            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || entry.Id <= 0 || string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                string label = FormatSlotLabel(entry.Name);
                perCategoryTarget[entry.Id] = label;
                if (mergeIntoGeneral)
                    _slotNameById[entry.Id] = label;
            }
        }

        private void LoadSlotBitNamesFromFile(string fileName, Dictionary<ulong, string> target)
        {
            target.Clear();
            string path = Path.Combine(Application.streamingAssetsPath, "AOData", fileName);
            if (!File.Exists(path))
                return;

            try
            {
                var lookup = JsonConvert.DeserializeObject<SlotLookupFile>(File.ReadAllText(path));
                if (lookup?.namesByBit == null)
                    return;

                foreach (var pair in lookup.namesByBit)
                {
                    if (!ulong.TryParse(pair.Key, out ulong bit))
                        continue;
                    if (bit == 0 || string.IsNullOrWhiteSpace(pair.Value))
                        continue;

                    target[bit] = pair.Value;
                }
            }
            catch
            {
                // No-op.
            }
        }

        private static int GetRawStatValue(AO.Data.Core.Item raw, int statId)
        {
            if (raw?.StatValues == null || statId <= 0)
                return 0;

            foreach (var stat in raw.StatValues)
            {
                if (stat == null || stat.Stat != statId)
                    continue;

                return stat.RawValue;
            }

            return 0;
        }

        private static string TrimOnUserPrefix(string value)
        {
            const string prefix = "On User: ";
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length).Trim()
                : value.Trim();
        }

        private static string ResolveSideName(int value)
        {
            return value switch
            {
                1 => "Clan",
                2 => "Omni",
                3 => "Neutral",
                _ => null
            };
        }

        private static bool IsEquipAction(int actionId)
        {
            return actionId == 6 || actionId == 8;
        }

        private void TryEquipOwnedItem(long instanceId)
        {
            TryEquipByInstanceId(instanceId);
        }

        private static string FormatSlotLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Unknown";

            var chars = new List<char>(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0
                    && ((char.IsUpper(c) && !char.IsUpper(raw[i - 1]))
                        || (char.IsDigit(c) && !char.IsDigit(raw[i - 1]))))
                    chars.Add(' ');
                chars.Add(c);
            }

            return new string(chars.ToArray());
        }

        private static string ColorLabel(string text) => $"<color={TooltipLabelColor}>{text}:</color>";
        private static string ColorValue(string text) => $"<color={TooltipValueColor}>{text}</color>";


        private void ToggleBackpack(long instanceId, string fallbackName)
        {
            if (HasNativeSession) { SetStatus("Server backpack contents are not connected yet."); return; }
            if (OpenBackpackId == instanceId)
            {
                CloseOpenBackpack();
                return;
            }

            OpenBackpackId = instanceId;
            var data = AODataManager.Instance.GetItemInstance(instanceId);
            SetStatus($"Opened container {data?.Definition?.Name ?? fallbackName ?? instanceId.ToString()}.");
            NotifyStateChanged();
        }

        private IEnumerable<int> ResolveEquipSlotCandidates(ItemDefinition coreDef, DataItemInstance dataItem)
        {
            var list = new List<int>();
            var mods = dataItem.Definition.StatModifiers ?? new List<AO.Data.Core.StatModifier>();
            var allowedSlots = EquipmentValidator.GetAllowedSlots(coreDef)?.Distinct().ToList() ?? new List<int>();
            var itemClass = GetItemClass(coreDef);

            int defaultSlot = mods.FirstOrDefault(m => m.StatId == DefaultSlotStatId).Value;
            if (itemClass == ItemClass.Weapon)
                defaultSlot = MapWeaponSlotToLocal(defaultSlot);
            else if (itemClass == ItemClass.Armor)
                defaultSlot = MapArmorSlotToLocal(defaultSlot);

            if (defaultSlot > 0 && IsSlotEnabledByDeckCapacity(defaultSlot))
                list.Add(defaultSlot);

            // For weapons, prefer whichever hand is currently empty to support dual-wield by default.
            bool weaponCanUseBothHands = allowedSlots.Contains(RightHandSlotId) && allowedSlots.Contains(LeftHandSlotId);
            if (weaponCanUseBothHands)
            {
                var equipped = Character?.Equipment?.GetAllEquipped() ?? EmptyEquipped;
                bool rightOccupied = equipped.ContainsKey(RightHandSlotId);
                bool leftOccupied = equipped.ContainsKey(LeftHandSlotId);

                if (rightOccupied && !leftOccupied)
                    list.Insert(0, LeftHandSlotId);
                else if (leftOccupied && !rightOccupied)
                    list.Insert(0, RightHandSlotId);
            }

            bool hasDeckSlots = allowedSlots.Any(IsDeckExtensionSlotLocal);
            if (hasDeckSlots)
            {
                int deckCapacity = GetCurrentDeckCapacity();
                var enabledDeckSlots = allowedSlots
                    .Where(slot => IsDeckExtensionSlotLocal(slot) && IsSlotEnabledByDeckCapacity(slot, deckCapacity))
                    .Distinct()
                    .OrderBy(slot => slot)
                    .ToList();

                var equipped = Character?.Equipment?.GetAllEquipped() ?? EmptyEquipped;
                var freeDeckSlots = enabledDeckSlots.Where(slot => !equipped.ContainsKey(slot)).ToList();
                if (freeDeckSlots.Count > 0)
                {
                    list.AddRange(freeDeckSlots);
                }
                else if (enabledDeckSlots.Count > 0)
                {
                    int fallbackDeckSlot = (defaultSlot > 0 && enabledDeckSlots.Contains(defaultSlot))
                        ? defaultSlot
                        : enabledDeckSlots[0];
                    list.Add(fallbackDeckSlot);
                }
            }

            foreach (var allowed in allowedSlots.Where(slot => IsSlotEnabledByDeckCapacity(slot)))
                list.Add(allowed);

            if (IsSlotEnabledByDeckCapacity(SelectedEquipSlot))
                list.Add(SelectedEquipSlot);

            return list.Distinct();
        }

        private int GetCurrentDeckCapacity()
        {
            int capacity = GetEquippedItemModifierSum(BeltSlotsStatId);
            return Mathf.Clamp(capacity, 0, 6);
        }

        private static bool IsDeckExtensionSlotLocal(int slotId)
        {
            return slotId >= DeckSlotMinLocal && slotId <= DeckSlotMaxLocal;
        }

        private bool IsSlotEnabledByDeckCapacity(int slotId)
        {
            return IsSlotEnabledByDeckCapacity(slotId, GetCurrentDeckCapacity());
        }

        private static bool IsSlotEnabledByDeckCapacity(int slotId, int deckCapacity)
        {
            if (!IsDeckExtensionSlotLocal(slotId))
                return true;

            int index = slotId - DeckSlotMinLocal + 1; // Deck1..Deck6
            return index > 0 && index <= Mathf.Clamp(deckCapacity, 0, 6);
        }

        public bool TryEquipSocialByInstanceId(
            long instanceId,
            int socialSlotId,
            SlotZone? sourceZone = null,
            int sourceIndex = -1)
        {
            if (HasNativeSession) return TryNativeEquip(instanceId, socialSlotId, true);
            if (_networkClient != null && _networkClient.IsConnected)
            {
                SetStatus("Social equip blocked: AO.Server is authoritative while connected.");
                return false;
            }

            if (!TryGetCanonicalSocialSlot(socialSlotId, out int socialCanonicalSlot))
            {
                SetStatus("Social equip failed: invalid social slot.");
                return false;
            }

            var dataItem = AODataManager.Instance.GetItemInstance(instanceId);
            if (dataItem?.Definition == null)
            {
                SetStatus("Social equip failed: missing data item.");
                return false;
            }

            var core = AODataManager.Instance.GetCoreInstance(instanceId);
            if (core?.Definition == null)
            {
                SetStatus($"Social equip failed: missing core item for {dataItem.Definition.Name}.");
                return false;
            }

            if (!IsSocialSlotCompatible(dataItem, core, socialCanonicalSlot, out string reason))
            {
                SetStatus($"Social equip blocked: {reason}");
                return false;
            }

            bool hadExplicitSource = sourceZone.HasValue && sourceIndex >= 0;
            bool removedFromSource = false;
            if (hadExplicitSource)
            {
                var located = GetSlotItem(sourceZone.Value, sourceIndex);
                if (located != null && located.InstanceId == instanceId)
                    removedFromSource = RemoveFromZone(sourceZone.Value, sourceIndex, out _);
            }

            if (!removedFromSource)
                removedFromSource = Character.Inventory.RemoveItem(core);

            if (!removedFromSource)
            {
                SetStatus($"Social equip failed: {dataItem.Definition.Name} is not in inventory.");
                return false;
            }

            if (_socialEquipped.TryGetValue(socialSlotId, out var existingInstanceId) && existingInstanceId > 0 && existingInstanceId != instanceId)
            {
                var existingCore = AODataManager.Instance.GetCoreInstance(existingInstanceId);
                if (existingCore != null)
                {
                    bool returnedToSource = false;
                    if (hadExplicitSource)
                        returnedToSource = TrySetToZone(sourceZone.Value, sourceIndex, existingCore);

                    if (!returnedToSource && !Character.Inventory.TryAddToMain(existingCore))
                    {
                        if (!(hadExplicitSource && TrySetToZone(sourceZone.Value, sourceIndex, core)))
                            Character.Inventory.TryAddToMain(core);
                        SetStatus("Social equip failed: inventory full while swapping social item.");
                        return false;
                    }
                }
            }

            _socialEquipped[socialSlotId] = instanceId;
            SetStatus($"Social-equipped {dataItem.Definition.Name}.");
            NotifyStateChanged();
            return true;
        }

        public void UnequipSocialSlot(int socialSlotId)
        {
            if (HasNativeSession) { UnequipSlot(socialSlotId); return; }
            if (_networkClient != null && _networkClient.IsConnected)
            {
                SetStatus("Social unequip blocked: AO.Server is authoritative while connected.");
                return;
            }

            if (!_socialEquipped.TryGetValue(socialSlotId, out var instanceId) || instanceId <= 0)
            {
                SetStatus($"Social unequip failed: slot {socialSlotId} is empty.");
                return;
            }

            _socialEquipped.Remove(socialSlotId);
            var core = AODataManager.Instance.GetCoreInstance(instanceId);
            if (core != null)
                Character.Inventory.TryAddToMain(core);

            SetStatus($"Unequipped social slot {socialSlotId}.");
            NotifyStateChanged();
        }

        private bool IsSocialSlotCompatible(DataItemInstance dataItem, ItemInstance coreItem, int socialCanonicalSlot, out string reason)
        {
            reason = string.Empty;
            if (dataItem?.Definition == null || coreItem?.Definition == null)
            {
                reason = "Missing item definition.";
                return false;
            }

            var itemClass = GetItemClass(coreItem.Definition);
            var raw = AODataManager.Instance.GetRawItemByAoid(dataItem.DefinitionId);
            int bitmap = GetRawStatValue(raw, 298);

            bool handVisualSlot = socialCanonicalSlot == 13 || socialCanonicalSlot == 15;
            int canonicalSlotToCheck = socialCanonicalSlot == 13
                ? RightHandSlotId
                : (socialCanonicalSlot == 15 ? LeftHandSlotId : socialCanonicalSlot);

            if (handVisualSlot)
            {
                if (itemClass != ItemClass.Weapon)
                {
                    reason = "Right/Left Hand social slots only accept weapons.";
                    return false;
                }
            }
            else if (itemClass != ItemClass.Armor)
            {
                reason = "Social body slots only accept armor/social wear items.";
                return false;
            }

            if (bitmap <= 0)
            {
                reason = "Item has no equip slot bitmap.";
                return false;
            }

            uint bit = canonicalSlotToCheck >= 0 && canonicalSlotToCheck < 31
                ? (1u << canonicalSlotToCheck)
                : 0u;
            if (bit == 0u || ((uint)bitmap & bit) == 0u)
            {
                reason = $"Item cannot be worn in social slot {socialCanonicalSlot}.";
                return false;
            }

            return true;
        }

        private static bool IsWeaponHandSlot(int slotId) => slotId == RightHandSlotId || slotId == LeftHandSlotId;

        private static int MapWeaponSlotToLocal(int canonicalSlotId)
        {
            if (canonicalSlotId <= 0)
                return canonicalSlotId;
            if (IsWeaponHandSlot(canonicalSlotId))
                return canonicalSlotId;

            return WeaponNonHandSlotOffset + canonicalSlotId;
        }

        private static int MapArmorSlotToLocal(int canonicalSlotId)
        {
            if (canonicalSlotId == 6 || canonicalSlotId == 8)
                return ArmorHandSlotOffset + canonicalSlotId;
            return canonicalSlotId;
        }

        private static int ToCanonicalWeaponSlotId(int slotId)
        {
            if (slotId > WeaponNonHandSlotOffset && slotId < WeaponNonHandSlotOffset + 100)
                return slotId - WeaponNonHandSlotOffset;
            return slotId;
        }

        private static int ToCanonicalArmorSlotId(int slotId)
        {
            if (slotId == ArmorHandSlotOffset + 6)
                return 6;
            if (slotId == ArmorHandSlotOffset + 8)
                return 8;
            return slotId;
        }

        private static int ToServerSlotId(int slotId)
        {
            return ToCanonicalArmorSlotId(ToCanonicalWeaponSlotId(slotId));
        }

        private int ToServerEquipSlotId(ItemDefinition def, int slotId)
        {
            var itemClass = GetItemClass(def);
            if (itemClass == ItemClass.Weapon)
                return ToCanonicalWeaponSlotId(slotId);
            if (itemClass == ItemClass.Armor)
                return ToCanonicalArmorSlotId(slotId);
            return slotId;
        }

        private int ResolveLocalSlotIdFromServer(int slotId)
        {
            if (slotId <= 0 || IsWeaponHandSlot(slotId))
                return slotId;

            var equipped = GetEquipped();
            if (equipped.ContainsKey(slotId))
                return slotId;

            int mapped = MapWeaponSlotToLocal(slotId);
            if (equipped.ContainsKey(mapped))
                return mapped;

            int mappedArmor = MapArmorSlotToLocal(slotId);
            if (equipped.ContainsKey(mappedArmor))
                return mappedArmor;

            return slotId;
        }

        private static bool TryGetCanonicalSocialSlot(int socialSlotId, out int canonicalSlotId)
        {
            canonicalSlotId = 0;
            if (socialSlotId <= SocialSlotOffset)
                return false;

            int candidate = socialSlotId - SocialSlotOffset;
            if (candidate <= 0 || candidate > 31)
                return false;

            canonicalSlotId = candidate;
            return true;
        }

        public static int ToSocialSlotId(int canonicalSlotId)
        {
            return canonicalSlotId > 0 ? SocialSlotOffset + canonicalSlotId : 0;
        }

        private int NormalizeEquipSlotForLocalStorage(ItemDefinition def, int slotId)
        {
            var itemClass = GetItemClass(def);
            if (itemClass == ItemClass.Weapon)
            {
                int canonicalWeapon = ToCanonicalWeaponSlotId(slotId);
                return MapWeaponSlotToLocal(canonicalWeapon);
            }

            if (itemClass == ItemClass.Armor)
            {
                int canonicalArmor = ToCanonicalArmorSlotId(slotId);
                return MapArmorSlotToLocal(canonicalArmor);
            }

            return slotId;
        }

        private static ItemClass GetItemClass(ItemDefinition itemDef)
        {
            if (itemDef == null)
                return ItemClass.None;

            int classStat = itemDef.Modifiers.FirstOrDefault(m => m.StatId == 76).Value;
            if (classStat > 0)
                return (ItemClass)classStat;

            return (ItemClass)itemDef.DBType;
        }

        private bool RemoveFromZone(SlotZone zone, int slotIndex, out ItemInstance removed)
        {
            removed = null;

            if (zone == SlotZone.Inventory)
                return Character.Inventory.TryRemoveFromMain(slotIndex, out removed);

            if (!TryGetOpenBackpackContainer(out _, out var bagItem) || bagItem == null)
                return false;

            return Character.Inventory.TryRemoveFromContainer(bagItem.InstanceId, slotIndex, out removed);
        }

        private bool TrySetToZone(SlotZone zone, int slotIndex, ItemInstance item)
        {
            if (zone == SlotZone.Inventory)
                return Character.Inventory.TrySetMainSlot(slotIndex, item);

            if (!TryGetOpenBackpackContainer(out _, out var bagItem) || bagItem == null)
                return false;

            return Character.Inventory.TrySetInContainer(bagItem.InstanceId, slotIndex, item);
        }

        private static bool CanStackTogether(ItemInstance a, ItemInstance b)
        {
            if (!IsStackable(a) || !IsStackable(b))
                return false;

            if (a.Definition == null || b.Definition == null)
                return false;

            return a.Definition.AOID == b.Definition.AOID;
        }

        private static bool IsStackable(ItemInstance item)
        {
            if (item == null || item.IsContainer || item.Definition == null)
                return false;

            int canFlags = item.Definition.Modifiers?.FirstOrDefault(m => m.StatId == CanFlagStatId).Value ?? 0;
            return (canFlags & StackableCanBit) != 0;
        }

        private AO.Data.Core.Item ResolveRawNanoByProgramId(int programId)
        {
            if (programId <= 0 || AODataManager.Instance == null)
                return null;

            var direct = AODataManager.Instance.GetRawNanoByAoid(programId);
            if (direct != null)
                return direct;

            var nanos = AODataManager.Instance.NanoItems;
            if (nanos == null || nanos.Count == 0)
                return null;

            for (int i = 0; i < nanos.Count; i++)
            {
                var nano = nanos[i];
                if (nano?.SpellData == null)
                    continue;

                foreach (var group in nano.SpellData)
                {
                    if (group?.Items == null)
                        continue;

                    foreach (var spell in group.Items)
                    {
                        if (spell == null)
                            continue;

                        if (spell.SpellID == programId || spell.NanoID == programId)
                            return nano;
                    }
                }
            }

            return null;
        }

        private int GetProgramStatValueAcrossChain(int nanoId, int statId)
        {
            var raw = ResolveRawNanoByProgramId(nanoId);
            if (raw == null)
                return 0;

            int best = Mathf.Max(0, GetRawStatValue(raw, statId));
            var chain = ResolveNanoExecutionChain(raw);
            for (int i = 0; i < chain.Count; i++)
            {
                int value = Mathf.Max(0, GetRawStatValue(chain[i], statId));
                if (value > best)
                    best = value;
            }

            return best;
        }

        private List<AO.Data.Core.Item> ResolveNanoExecutionChain(AO.Data.Core.Item root)
        {
            var result = new List<AO.Data.Core.Item>();
            if (root == null)
                return result;

            var queue = new Queue<AO.Data.Core.Item>();
            var visited = new HashSet<int>();
            queue.Enqueue(root);
            if (root.AOID > 0)
                visited.Add(root.AOID);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == null)
                    continue;

                result.Add(current);
                if (current.SpellData == null)
                    continue;

                foreach (var group in current.SpellData)
                {
                    if (group?.Items == null)
                        continue;

                    foreach (var spell in group.Items)
                    {
                        if (spell == null || spell.NanoID <= 0)
                            continue;

                        if (!visited.Add(spell.NanoID))
                            continue;

                        var linked = AODataManager.Instance.GetRawNanoByAoid(spell.NanoID) ?? ResolveRawNanoByProgramId(spell.NanoID);
                        if (linked != null)
                            queue.Enqueue(linked);
                    }
                }
            }

            return result;
        }

        private static void MergeStatModifiers(Dictionary<int, int> destination, Dictionary<int, int> source)
        {
            if (destination == null || source == null || source.Count == 0)
                return;

            foreach (var pair in source)
            {
                if (destination.TryGetValue(pair.Key, out int existing))
                    destination[pair.Key] = existing + pair.Value;
                else
                    destination[pair.Key] = pair.Value;
            }
        }

        private AO.Data.Core.Item ResolveRawNanoForActive(ActiveNanoProgram active)
        {
            if (active == null)
                return null;

            if (active.RawNanoAoid > 0)
            {
                var byAoid = AODataManager.Instance.GetRawNanoByAoid(active.RawNanoAoid);
                if (byAoid != null)
                    return byAoid;
            }

            return ResolveRawNanoByProgramId(active.NanoId);
        }

        public string ReloadQuests()
        {
            string message = _questAuthoring.Load();
            string familyMessage = _questTargetFamilies.Load();
            NotifyStateChanged();
            string combined = $"{message} {familyMessage}";
            SetStatus(combined);
            return combined;
        }

        public bool SaveQuests(out string message)
        {
            bool ok = _questAuthoring.Save(out message);
            SetStatus(message);
            return ok;
        }

        public QuestValidationResult ValidateQuests()
        {
            var validation = QuestValidator.Validate(_questAuthoring.File);
            SetStatus(QuestAuthoringService.BuildValidationSummary(validation));
            return validation;
        }

        public QuestDefinition CreateQuest()
        {
            var quest = _questAuthoring.CreateQuest();
            NotifyStateChanged();
            SetStatus($"Created quest '{quest.QuestId}'.");
            return quest;
        }

        public bool DeleteQuest(string questId)
        {
            bool ok = _questAuthoring.DeleteQuest(questId);
            if (!ok)
            {
                SetStatus($"Delete failed: quest '{questId}' not found.");
                return false;
            }

            NotifyStateChanged();
            SetStatus($"Deleted quest '{questId}'.");
            return true;
        }

        public QuestNode CreateQuestNode(string questId, QuestNodeType type)
        {
            var quest = FindQuest(questId);
            if (quest == null)
            {
                SetStatus($"Node create failed: quest '{questId}' not found.");
                return null;
            }

            var node = _questAuthoring.CreateNode(quest, type);
            NotifyStateChanged();
            SetStatus($"Added {type} node '{node?.NodeId}' to '{quest.QuestId}'.");
            return node;
        }

        public bool DeleteQuestNode(string questId, string nodeId)
        {
            var quest = FindQuest(questId);
            if (quest == null)
            {
                SetStatus($"Delete node failed: quest '{questId}' not found.");
                return false;
            }

            bool ok = _questAuthoring.DeleteNode(quest, nodeId);
            if (!ok)
            {
                SetStatus($"Delete node failed: node '{nodeId}' not found.");
                return false;
            }

            NotifyStateChanged();
            SetStatus($"Deleted node '{nodeId}'.");
            return true;
        }

        public bool StartQuest(string questId)
        {
            var quest = FindQuest(questId);
            if (quest == null)
            {
                SetStatus($"Start quest failed: '{questId}' not found.");
                return false;
            }

            bool ok = _questRuntime.StartQuest(quest, BuildQuestEvaluationContext(), out var message);
            if (ok)
                TryAdvanceQuestPassiveFlowOnStart(quest);
            SetStatus(message);
            NotifyStateChanged();
            return ok;
        }

        private void TryAdvanceQuestPassiveFlowOnStart(QuestDefinition quest)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
                return;

            var progress = _questRuntime.Snapshot()
                .FirstOrDefault(p => p != null
                    && p.State == QuestRuntimeState.Started
                    && string.Equals(p.QuestId, quest.QuestId, StringComparison.OrdinalIgnoreCase));
            if (progress == null)
                return;

            string currentNodeId = string.IsNullOrWhiteSpace(progress.CurrentNodeId)
                ? (quest.StartNodeId ?? string.Empty)
                : progress.CurrentNodeId;
            if (string.IsNullOrWhiteSpace(currentNodeId))
                return;

            bool advancedPassive = AdvanceQuestThroughPassiveNodes(
                quest,
                currentNodeId,
                out string finalNodeId,
                out bool reachedEnd,
                BuildQuestEvaluationContext(),
                out string rewardSummary);

            if (advancedPassive)
                _questRuntime.SetCurrentNode(quest.QuestId, finalNodeId, out _);

            if (reachedEnd)
                _questRuntime.CompleteQuest(quest.QuestId, out _);

            if (!string.IsNullOrWhiteSpace(rewardSummary))
                SetStatus($"Quest rewards: {rewardSummary}.");
        }

        public IReadOnlyList<QuestProgress> GetQuestProgress()
        {
            return _questRuntime.Snapshot();
        }

        public void MarkQuestsDirty(string status = null)
        {
            NotifyStateChanged();
            if (!string.IsNullOrWhiteSpace(status))
                SetStatus(status);
        }

        private QuestDefinition FindQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return null;

            var quests = _questAuthoring.File?.Quests;
            if (quests == null)
                return null;

            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (quest == null)
                    continue;

                if (string.Equals(quest.QuestId, questId, StringComparison.OrdinalIgnoreCase))
                    return quest;
            }

            return null;
        }

        private QuestNode FindQuestNode(QuestDefinition quest, string nodeId)
        {
            if (quest?.Nodes == null || string.IsNullOrWhiteSpace(nodeId))
                return null;

            for (int i = 0; i < quest.Nodes.Count; i++)
            {
                var node = quest.Nodes[i];
                if (node == null)
                    continue;

                if (string.Equals(node.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    return node;
            }

            return null;
        }

        private void EvaluateTargetSelectionForQuestProgress(CharacterRuntimeBridge target)
        {
            if (target == null)
                return;

            if (!TryResolveTargetCandidateIds(target, out int targetPlayfieldId, out string targetName, out var candidateIds))
                return;

            var progressSnapshot = _questRuntime.Snapshot();
            if (progressSnapshot == null || progressSnapshot.Count == 0)
                return;

            bool changedAny = false;
            string latestMessage = null;

            for (int i = 0; i < progressSnapshot.Count; i++)
            {
                var progress = progressSnapshot[i];
                if (progress == null || progress.State != QuestRuntimeState.Started || string.IsNullOrWhiteSpace(progress.QuestId))
                    continue;

                var quest = FindQuest(progress.QuestId);
                if (quest == null)
                    continue;

                string currentNodeId = string.IsNullOrWhiteSpace(progress.CurrentNodeId)
                    ? (quest.StartNodeId ?? string.Empty)
                    : progress.CurrentNodeId;
                if (string.IsNullOrWhiteSpace(currentNodeId))
                    continue;

                var node = FindQuestNode(quest, currentNodeId);
                if (!NodeMatchesTargetObjective(node, targetPlayfieldId, candidateIds))
                    continue;

                string nextNode = node.NextNodeIds?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                if (string.IsNullOrWhiteSpace(nextNode))
                {
                    if (_questRuntime.CompleteQuest(quest.QuestId, out _))
                    {
                        changedAny = true;
                        latestMessage = $"Quest complete: {quest.Name ?? quest.QuestId}.";
                    }

                    continue;
                }

                _questRuntime.SetCurrentNode(quest.QuestId, nextNode, out _);
                changedAny = true;

                bool advancedAfterObjective = AdvanceQuestThroughPassiveNodes(
                    quest,
                    nextNode,
                    out string finalNodeId,
                    out bool reachedEndAfterObjective,
                    BuildQuestEvaluationContext(),
                    out string postObjectiveRewardSummary);
                if (advancedAfterObjective)
                {
                    _questRuntime.SetCurrentNode(quest.QuestId, finalNodeId, out _);
                    if (!string.IsNullOrWhiteSpace(postObjectiveRewardSummary))
                        latestMessage = $"Quest rewards: {postObjectiveRewardSummary}.";
                }

                if (reachedEndAfterObjective)
                {
                    if (_questRuntime.CompleteQuest(quest.QuestId, out _))
                        latestMessage = $"Quest complete: {quest.Name ?? quest.QuestId}.";
                }
                else
                {
                    latestMessage = $"Quest updated: {quest.Name ?? quest.QuestId} ({node.Title ?? node.NodeId}) on {targetName}.";
                }
            }

            if (changedAny)
            {
                if (!string.IsNullOrWhiteSpace(latestMessage))
                    SetStatus(latestMessage);
                NotifyStateChanged();
            }
        }

        public bool TryInteractWithTarget(CharacterRuntimeBridge target, out string title, out string body)
        {
            title = ResolveTargetName(target);
            body = string.Empty;
            if (target == null)
                return false;

            TrySetSelectedTarget(target);

            if (!TryResolveTargetCandidateIds(target, out int targetPlayfieldId, out string targetName, out var candidateIds))
                return false;

            title = string.IsNullOrWhiteSpace(targetName) ? title : targetName;

            var lines = new List<string>();
            bool changedAny = false;

            var quests = _questAuthoring.File?.Quests;
            if (quests != null)
            {
                var snapshot = _questRuntime.Snapshot() ?? new List<QuestProgress>();
                for (int i = 0; i < quests.Count; i++)
                {
                    var quest = quests[i];
                    if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
                        continue;
                    if (quest.StartNpcId <= 0 || !candidateIds.Contains(quest.StartNpcId))
                        continue;

                    bool alreadyStarted = snapshot.Any(p => p != null
                        && string.Equals(p.QuestId, quest.QuestId, StringComparison.OrdinalIgnoreCase)
                        && p.State == QuestRuntimeState.Started);
                    if (alreadyStarted)
                        continue;

                    if (StartQuest(quest.QuestId))
                    {
                        changedAny = true;
                        lines.Add($"Started quest: {quest.Name ?? quest.QuestId}");
                    }
                }
            }

            var progressSnapshot = _questRuntime.Snapshot();
            if (progressSnapshot != null)
            {
                for (int i = 0; i < progressSnapshot.Count; i++)
                {
                    var progress = progressSnapshot[i];
                    if (progress == null || progress.State != QuestRuntimeState.Started)
                        continue;

                    var quest = FindQuest(progress.QuestId);
                    if (quest == null)
                        continue;

                    string currentNodeId = string.IsNullOrWhiteSpace(progress.CurrentNodeId)
                        ? (quest.StartNodeId ?? string.Empty)
                        : progress.CurrentNodeId;
                    if (string.IsNullOrWhiteSpace(currentNodeId))
                        continue;

                    bool advancedPassive = AdvanceQuestThroughPassiveNodes(
                        quest,
                        currentNodeId,
                        out string actionableNodeId,
                        out bool reachedEnd,
                        BuildQuestEvaluationContext(),
                        out string passiveRewardSummary);
                    if (advancedPassive)
                    {
                        _questRuntime.SetCurrentNode(quest.QuestId, actionableNodeId, out _);
                        changedAny = true;
                        if (!string.IsNullOrWhiteSpace(passiveRewardSummary))
                            lines.Add($"Quest rewards: {passiveRewardSummary}");
                    }

                    if (reachedEnd)
                    {
                        if (_questRuntime.CompleteQuest(quest.QuestId, out _))
                        {
                            changedAny = true;
                            lines.Add($"Quest complete: {quest.Name ?? quest.QuestId}");
                        }

                        continue;
                    }

                    var node = FindQuestNode(quest, actionableNodeId);
                    if (node == null)
                        continue;

                    if (node.NodeType == QuestNodeType.Dialogue && !string.IsNullOrWhiteSpace(node.DialogueText))
                        lines.Add(node.DialogueText.Trim());

                    if (!NodeMatchesTalkObjective(node, targetPlayfieldId, candidateIds))
                        continue;

                    string nextNode = node.NextNodeIds?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                    if (string.IsNullOrWhiteSpace(nextNode))
                    {
                        if (_questRuntime.CompleteQuest(quest.QuestId, out _))
                        {
                            changedAny = true;
                            lines.Add($"Quest complete: {quest.Name ?? quest.QuestId}");
                        }

                        continue;
                    }

                    _questRuntime.SetCurrentNode(quest.QuestId, nextNode, out _);
                    changedAny = true;

                    bool advancedAfterObjective = AdvanceQuestThroughPassiveNodes(
                        quest,
                        nextNode,
                        out string finalNodeId,
                        out bool reachedEndAfterObjective,
                        BuildQuestEvaluationContext(),
                        out string postObjectiveRewardSummary);
                    if (advancedAfterObjective)
                    {
                        _questRuntime.SetCurrentNode(quest.QuestId, finalNodeId, out _);
                        if (!string.IsNullOrWhiteSpace(postObjectiveRewardSummary))
                            lines.Add($"Quest rewards: {postObjectiveRewardSummary}");
                    }

                    if (reachedEndAfterObjective)
                    {
                        if (_questRuntime.CompleteQuest(quest.QuestId, out _))
                            lines.Add($"Quest complete: {quest.Name ?? quest.QuestId}");
                    }
                    else
                    {
                        lines.Add($"Quest updated: {quest.Name ?? quest.QuestId}");
                    }
                }
            }

            if (changedAny)
                NotifyStateChanged();

            var unique = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();
            if (unique.Count == 0)
                return false;

            body = string.Join("\n\n", unique);
            SetStatus(unique[0]);
            return true;
        }

        private bool AdvanceQuestThroughPassiveNodes(
            QuestDefinition quest,
            string nodeId,
            out string finalNodeId,
            out bool reachedEnd,
            QuestEvaluationContext evalContext,
            out string rewardSummary)
        {
            reachedEnd = false;
            rewardSummary = string.Empty;
            finalNodeId = nodeId ?? string.Empty;
            if (quest?.Nodes == null || string.IsNullOrWhiteSpace(finalNodeId))
                return false;

            bool advanced = false;
            int guard = 0;
            while (guard++ < 64)
            {
                QuestNode node = null;
                for (int i = 0; i < quest.Nodes.Count; i++)
                {
                    var candidate = quest.Nodes[i];
                    if (candidate == null)
                        continue;
                    if (string.Equals(candidate.NodeId, finalNodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        node = candidate;
                        break;
                    }
                }

                if (node == null)
                    break;

                if (node.NodeType == QuestNodeType.End)
                {
                    reachedEnd = true;
                    break;
                }

                if (node.NodeType == QuestNodeType.Objective)
                    break;

                if (!QuestRuntimeService.EvaluateNodeConditions(node, evalContext))
                    break;

                if (node.NodeType == QuestNodeType.Reward)
                {
                    var rewards = QuestRuntimeService.ResolveRewardsForNode(node, evalContext);
                    if (ApplyQuestRewards(rewards, out string thisRewardSummary)
                        && !string.IsNullOrWhiteSpace(thisRewardSummary))
                    {
                        rewardSummary = string.IsNullOrWhiteSpace(rewardSummary)
                            ? thisRewardSummary
                            : $"{rewardSummary}; {thisRewardSummary}";
                    }
                }

                string next = node.NextNodeIds?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                if (string.IsNullOrWhiteSpace(next))
                {
                    if (node.NodeType == QuestNodeType.Reward)
                        reachedEnd = true;
                    break;
                }

                if (string.Equals(next, finalNodeId, StringComparison.OrdinalIgnoreCase))
                    break;

                finalNodeId = next;
                advanced = true;
            }

            return advanced;
        }

        private bool ApplyQuestRewards(IReadOnlyList<QuestReward> rewards, out string summary)
        {
            summary = string.Empty;
            if (rewards == null || rewards.Count == 0)
                return false;

            bool changed = false;
            var applied = new List<string>();
            for (int i = 0; i < rewards.Count; i++)
            {
                var reward = rewards[i];
                if (reward == null)
                    continue;

                int amount = Mathf.Max(1, reward.Amount);
                switch (reward.Type)
                {
                    case QuestRewardType.Experience:
                        AddExperience(amount);
                        applied.Add($"+{amount} XP");
                        changed = true;
                        break;
                    case QuestRewardType.Credits:
                        TryAddCredits(amount);
                        applied.Add($"+{amount} Credits");
                        changed = true;
                        break;
                    case QuestRewardType.Item:
                        if (TryGrantItemReward(reward.TargetId, amount))
                        {
                            applied.Add($"+Item {reward.TargetId} x{amount}");
                            changed = true;
                        }
                        break;
                    case QuestRewardType.Stat:
                        if (TryGrantStatReward(reward.TargetId, amount))
                        {
                            applied.Add($"+{amount} {GetStatName(reward.TargetId)}");
                            changed = true;
                        }
                        break;
                }
            }

            if (applied.Count > 0)
                summary = string.Join(", ", applied);
            return changed;
        }

        private bool TryGrantItemReward(int itemAoid, int amount = 1)
        {
            if (itemAoid <= 0 || AODataManager.Instance == null)
                return false;

            var dataItem = AODataManager.Instance.GetItemInstance(itemAoid);
            if (dataItem == null)
                return false;

            var core = CreateRuntimeInventoryItemFromData(dataItem);
            if (core == null)
                return false;

            // Quest item rewards can intentionally grant large charge/stack counts.
            // Keep defaults for amount=1, but honor explicit reward amounts.
            if (amount > 1)
                core.Quantity = Mathf.Max(1, amount);

            return Character?.Inventory?.TryAddToMain(core) == true;
        }

        public bool ApplyAuthoritativeLootItem(int itemAoid, int amount = 1)
        {
            bool granted = TryGrantItemReward(itemAoid, Mathf.Max(1, amount));
            if (granted)
            {
                NotifyStateChanged();
                return true;
            }

            SetStatus("Inventory full.");
            NotifyStateChanged();
            return false;
        }

        public void ApplyAuthoritativeLootCredits(int amount)
        {
            if (amount <= 0)
                return;

            TryAddCredits(amount);
            NotifyStateChanged();
        }

        private void HandleAuthoritativeStatsApplied(
            AuthoritativeNetworkClient.AuthoritativeStatsSnapshot snapshot)
        {
            if (Character == null || snapshot.Stats == null)
                return;

            Character.ApplyAuthoritativeStats(snapshot.Stats,
                statId => AODataManager.Instance?.GetStatName(statId));
            _characterSex = snapshot.Sex switch
            {
                1 => CharacterRuntimeBridge.CharacterSex.Uni,
                3 => CharacterRuntimeBridge.CharacterSex.Female,
                _ => CharacterRuntimeBridge.CharacterSex.Male
            };
            if (_runtimeBridge != null)
                _runtimeBridge.Sex = _characterSex;
            NotifyStateChanged();
        }

        public void ApplyAuthoritativeQuestRewardGrants(IReadOnlyList<AuthoritativeNetworkClient.QuestRewardGrant> grants)
        {
            if (grants == null || grants.Count == 0 || Character == null)
                return;

            for (int i = 0; i < grants.Count; i++)
            {
                var grant = grants[i];
                int amount = Mathf.Max(0, grant.Amount);
                if (amount <= 0)
                    continue;

                switch (grant.Type)
                {
                    case 1: // XP
                        AddExperience(amount);
                        break;
                    case 2: // Stat/Credits
                        if (grant.TargetId == CreditsStatId)
                            TryAddCredits(amount);
                        else
                            TryGrantStatReward(grant.TargetId, amount);
                        break;
                    case 3: // Item
                        TryGrantItemReward(grant.TargetId, amount);
                        break;
                }
            }

            Character.StatsContainer.Recalculate();
            Character.RecalculateDerivedStats();
            NotifyStateChanged();
        }

        private bool TryGrantStatReward(int statId, int amount)
        {
            if (Character == null || statId <= 0 || amount <= 0)
                return false;

            string statName = AODataManager.Instance.GetStatName(statId);
            if (string.IsNullOrWhiteSpace(statName))
                return false;

            int current = Character.StatsContainer.GetBaseStat(statId);
            Character.SetBaseStatValue(statName, current + amount);
            return true;
        }

        private void TryAddCredits(int amount)
        {
            if (Character == null || amount <= 0)
                return;

            string creditsName = AODataManager.Instance.GetStatName(CreditsStatId);
            if (string.IsNullOrWhiteSpace(creditsName))
                creditsName = "Credits";

            int current = Character.StatsContainer.GetBaseStat(CreditsStatId);
            Character.SetBaseStatValue(creditsName, current + amount);
        }

        private QuestEvaluationContext BuildQuestEvaluationContext()
        {
            return new QuestEvaluationContext
            {
                CharacterLevel = CharacterLevel,
                FactionId = ResolveFactionIdForQuest(),
                ProfessionId = CharacterProfessionId,
                VisualProfessionId = CharacterVisualProfessionId,
                BreedId = CharacterBreedId,
                GenderId = ResolveQuestGenderId()
            };
        }

        private int ResolveFactionIdForQuest()
        {
            const int sideStatId = 33;
            if (Character == null)
                return 0;

            int finalStat = Character.StatsContainer.GetFinalStat(sideStatId) + GetActiveProgramModifierSum(sideStatId);
            if (finalStat > 0)
                return finalStat;

            return Character.StatsContainer.GetBaseStat(sideStatId);
        }

        private int ResolveQuestGenderId()
        {
            return CharacterSex switch
            {
                CharacterRuntimeBridge.CharacterSex.Uni => 1,
                CharacterRuntimeBridge.CharacterSex.Male => 2,
                CharacterRuntimeBridge.CharacterSex.Female => 3,
                _ => 2
            };
        }

        private bool NodeMatchesTargetObjective(QuestNode node, int targetPlayfieldId, IReadOnlyList<int> candidateIds)
        {
            if (node == null || candidateIds == null || candidateIds.Count == 0)
                return false;

            if (node.NodeType != QuestNodeType.Objective)
                return false;

            if (node.ObjectiveType != QuestObjectiveType.Target && node.ObjectiveType != QuestObjectiveType.Select)
                return false;

            var allowedIds = new HashSet<int>();
            if (node.TargetId > 0)
                allowedIds.Add(node.TargetId);

            if (node.TargetTemplateIds != null)
            {
                for (int i = 0; i < node.TargetTemplateIds.Count; i++)
                {
                    int id = node.TargetTemplateIds[i];
                    if (id > 0)
                        allowedIds.Add(id);
                }
            }

            QuestTargetFamily family = null;
            if (!string.IsNullOrWhiteSpace(node.TargetFamilyId))
                _questTargetFamilies.FamiliesById.TryGetValue(node.TargetFamilyId.Trim(), out family);

            if (family?.TemplateIds != null)
            {
                for (int i = 0; i < family.TemplateIds.Count; i++)
                {
                    int id = family.TemplateIds[i];
                    if (id > 0)
                        allowedIds.Add(id);
                }
            }

            if (allowedIds.Count == 0)
                return false;

            if (family?.PlayfieldId is int familyPf && familyPf > 0 && targetPlayfieldId > 0 && familyPf != targetPlayfieldId)
                return false;

            for (int i = 0; i < candidateIds.Count; i++)
            {
                if (allowedIds.Contains(candidateIds[i]))
                    return true;
            }

            return false;
        }

        private bool NodeMatchesTalkObjective(QuestNode node, int targetPlayfieldId, IReadOnlyList<int> candidateIds)
        {
            if (node == null || candidateIds == null || candidateIds.Count == 0)
                return false;
            if (node.NodeType != QuestNodeType.Objective || node.ObjectiveType != QuestObjectiveType.Talk)
                return false;

            var allowedIds = new HashSet<int>();
            if (node.TargetId > 0)
                allowedIds.Add(node.TargetId);

            if (node.TargetTemplateIds != null)
            {
                for (int i = 0; i < node.TargetTemplateIds.Count; i++)
                {
                    int id = node.TargetTemplateIds[i];
                    if (id > 0)
                        allowedIds.Add(id);
                }
            }

            QuestTargetFamily family = null;
            if (!string.IsNullOrWhiteSpace(node.TargetFamilyId))
                _questTargetFamilies.FamiliesById.TryGetValue(node.TargetFamilyId.Trim(), out family);

            if (family?.TemplateIds != null)
            {
                for (int i = 0; i < family.TemplateIds.Count; i++)
                {
                    int id = family.TemplateIds[i];
                    if (id > 0)
                        allowedIds.Add(id);
                }
            }

            if (family?.PlayfieldId is int familyPf && familyPf > 0 && targetPlayfieldId > 0 && familyPf != targetPlayfieldId)
                return false;

            if (allowedIds.Count == 0)
                return true;

            for (int i = 0; i < candidateIds.Count; i++)
            {
                if (allowedIds.Contains(candidateIds[i]))
                    return true;
            }

            return false;
        }

        private bool TryResolveTargetCandidateIds(CharacterRuntimeBridge target, out int playfieldId, out string targetName, out List<int> candidateIds)
        {
            playfieldId = 0;
            targetName = ResolveTargetName(target);
            candidateIds = new List<int>();

            if (target == null)
                return false;

            var identity = target.GetComponent<RuntimeDynelQuestIdentity>();
            if (identity == null)
                return false;

            playfieldId = identity.PlayfieldId;
            if (!string.IsNullOrWhiteSpace(identity.DynelName))
                targetName = identity.DynelName;

            var fromIdentity = identity.GetCandidateIds();
            if (fromIdentity != null)
            {
                for (int i = 0; i < fromIdentity.Count; i++)
                {
                    int id = fromIdentity[i];
                    if (id <= 0 || candidateIds.Contains(id))
                        continue;
                    candidateIds.Add(id);
                }
            }

            int preferred = identity.PreferredStableId;
            if (preferred > 0 && !candidateIds.Contains(preferred))
                candidateIds.Add(preferred);

            return candidateIds.Count > 0;
        }

        private void NotifyStateChanged()
        {
            _stateChangedPending = true;
            float now = Time.unscaledTime;
            if (now >= _nextStateChangedEmitAt)
            {
                _nextStateChangedEmitAt = now + StateChangedEmitIntervalSeconds;
                _stateChangedPending = false;
                StateChanged?.Invoke();
            }
        }

        public void PumpUiStateNotifications()
        {
            if (!_stateChangedPending)
                return;
            float now = Time.unscaledTime;
            if (now < _nextStateChangedEmitAt)
                return;

            _nextStateChangedEmitAt = now + StateChangedEmitIntervalSeconds;
            _stateChangedPending = false;
            StateChanged?.Invoke();
        }

        private void SetStatus(string message)
        {
            StatusChanged?.Invoke(message);
        }

        private void ApplyAbilityBaseStatsForCurrentBreed()
        {
            if (Character == null)
                return;

            ApplyAbilityBaseStat("Strength", 16);
            ApplyAbilityBaseStat("Agility", 17);
            ApplyAbilityBaseStat("Stamina", 18);
            ApplyAbilityBaseStat("Intelligence", 19);
            ApplyAbilityBaseStat("Sense", 20);
            ApplyAbilityBaseStat("Psychic", 21);
        }

        private void ApplyAbilityBaseStat(string statName, int statId)
        {
            int value = AO.Core.Characters.Character.GetStartingBaseStatForBreed?.Invoke(statId, Character.BreedId) ?? 6;
            Character.SetBaseStatValue(statName, Mathf.Max(0, value));
        }

        private void ApplyDefaultNonAbilityBaseStats(bool forceReset = false)
        {
            if (Character == null)
                return;

            foreach (var statId in AODataManager.Instance.GetKnownSkillStatIds())
            {
                if (statId is >= 16 and <= 21)
                    continue;

                string statName = AODataManager.Instance.GetStatName(statId);
                if (string.IsNullOrWhiteSpace(statName))
                    continue;

                if (forceReset)
                {
                    Character.SetBaseStatValue(statName, 5);
                    continue;
                }

                int current = Character.StatsContainer.GetBaseStat(statId);
                if (current < 5)
                    Character.SetBaseStatValue(statName, 5);
            }

            string creditsName = AODataManager.Instance.GetStatName(CreditsStatId);
            if (string.IsNullOrWhiteSpace(creditsName))
                creditsName = "Credits";

            int currentCredits = Character.StatsContainer.GetBaseStat(CreditsStatId);
            if (forceReset || currentCredits < DefaultStartingCredits)
                Character.SetBaseStatValue(creditsName, DefaultStartingCredits);
        }
    }
}

