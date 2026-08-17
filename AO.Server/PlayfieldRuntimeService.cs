using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AO.Server.Transport;

namespace AO.Server;

public sealed class PlayfieldRuntimeService
{
    public enum PlayfieldLifecycleState
    {
        Active = 0,
        Warm = 1,
        Sleeping = 2
    }

    private sealed class PlayfieldCacheEntry
    {
        public PlayfieldRuntime Runtime = PlayfieldRuntime.Empty(0);
        public int ActiveRefCount;
        public DateTime LastTouchedUtc = DateTime.UtcNow;
        public DateTime LastBecameWarmUtc = DateTime.UtcNow;
    }

    private readonly ServerDataPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<int, PlayfieldCacheEntry> _playfields = new();
    private readonly List<ZoneLinkDefinition> _zoneLinks;

    public PlayfieldRuntimeService(ServerDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _zoneLinks = LoadZoneLinks();
    }

    public PlayfieldRuntime GetPlayfield(int playfieldId)
    {
        if (playfieldId <= 0)
            return PlayfieldRuntime.Empty(playfieldId);

        PlayfieldCacheEntry entry = GetOrCreateEntry(playfieldId);
        entry.LastTouchedUtc = DateTime.UtcNow;
        return entry.Runtime;
    }

    public void MarkPlayfieldActive(int playfieldId)
    {
        if (playfieldId <= 0)
            return;

        PlayfieldCacheEntry entry = GetOrCreateEntry(playfieldId);
        entry.ActiveRefCount = Math.Max(0, entry.ActiveRefCount) + 1;
        entry.LastTouchedUtc = DateTime.UtcNow;
    }

    public void MarkPlayfieldInactive(int playfieldId)
    {
        if (playfieldId <= 0)
            return;
        if (!_playfields.TryGetValue(playfieldId, out PlayfieldCacheEntry? entry) || entry == null)
            return;

        entry.ActiveRefCount = Math.Max(0, entry.ActiveRefCount - 1);
        entry.LastTouchedUtc = DateTime.UtcNow;
        if (entry.ActiveRefCount == 0)
            entry.LastBecameWarmUtc = entry.LastTouchedUtc;
    }

    public void SweepLifecycle(TimeSpan warmTtl)
    {
        DateTime now = DateTime.UtcNow;
        if (warmTtl <= TimeSpan.Zero)
            warmTtl = TimeSpan.FromMinutes(2);

        List<int> toSleep = new();
        foreach (var kvp in _playfields)
        {
            int playfieldId = kvp.Key;
            PlayfieldCacheEntry? entry = kvp.Value;
            if (entry == null || entry.ActiveRefCount > 0)
                continue;

            if ((now - entry.LastBecameWarmUtc) >= warmTtl)
                toSleep.Add(playfieldId);
        }

        for (int i = 0; i < toSleep.Count; i++)
        {
            int playfieldId = toSleep[i];
            _playfields.Remove(playfieldId);
            Console.WriteLine($"[AO.Server] Playfield {playfieldId} transitioned Warm->Sleeping (TTL expired).");
        }
    }

    public PlayfieldLifecycleState GetLifecycleState(int playfieldId)
    {
        if (playfieldId <= 0 || !_playfields.TryGetValue(playfieldId, out PlayfieldCacheEntry? entry) || entry == null)
            return PlayfieldLifecycleState.Sleeping;

        return entry.ActiveRefCount > 0
            ? PlayfieldLifecycleState.Active
            : PlayfieldLifecycleState.Warm;
    }

    private PlayfieldCacheEntry GetOrCreateEntry(int playfieldId)
    {
        if (_playfields.TryGetValue(playfieldId, out PlayfieldCacheEntry? existing) && existing != null)
            return existing;

        var created = new PlayfieldCacheEntry
        {
            Runtime = LoadPlayfield(playfieldId),
            ActiveRefCount = 0,
            LastTouchedUtc = DateTime.UtcNow,
            LastBecameWarmUtc = DateTime.UtcNow
        };
        _playfields[playfieldId] = created;
        Console.WriteLine($"[AO.Server] Playfield {playfieldId} transitioned Sleeping->Warm (runtime loaded).");
        return created;
    }

    public bool TryGetEntity(int playfieldId, string entityId, out RuntimeEntity entity)
    {
        entity = null;
        if (string.IsNullOrWhiteSpace(entityId))
            return false;

        entity = GetPlayfield(playfieldId).FindEntity(entityId);
        return entity != null;
    }

    public bool TryResolveInteraction(int playfieldId, string entityId, DateTime utcNow, out PlayfieldInteractionDecision decision)
    {
        decision = PlayfieldInteractionDecision.Fail(entityId, "Invalid interaction request.");
        if (playfieldId <= 0 || string.IsNullOrWhiteSpace(entityId))
            return false;

        var runtime = GetPlayfield(playfieldId);
        if (runtime == null)
        {
            decision = PlayfieldInteractionDecision.Fail(entityId, "Playfield runtime was not available.");
            return false;
        }

        return runtime.TryConsumeInteraction(entityId, utcNow, out decision);
    }

    public List<RuntimeEntitySnapshot> BuildSnapshots(int playfieldId)
    {
        var runtime = GetPlayfield(playfieldId);
        DateTime utcNow = DateTime.UtcNow;

        return runtime
            .Entities
            .Select(e =>
            {
                RuntimeEntityServiceState state = runtime.GetServiceState(e.EntityId);
                float cooldownRemaining = state != null
                    ? state.GetCooldownRemainingSeconds(utcNow)
                    : 0f;

                return new RuntimeEntitySnapshot
                {
                    EntityId = e.EntityId,
                    ObjectType = e.ObjectType,
                    DisplayName = e.DisplayName,
                    PlayfieldId = e.PlayfieldId,
                    IdentityInstance = e.IdentityInstance,
                    TemplateId = e.TemplateId,
                    MeshId = e.MeshId,
                    X = e.X,
                    Y = e.Y,
                    Z = e.Z,
                    YawDegrees = e.YawDegrees,
                    ImportKey = e.ImportKey,
                    MeshName = e.MeshName,
                    InteractionType = e.InteractionType,
                    InteractionId = e.InteractionId,
                    InteractionRadius = e.InteractionRadius,
                    InteractionLabel = e.InteractionLabel,
                    InteractionEnabled = state?.IsEnabled ?? false,
                    InteractionUseCount = state?.UseCount ?? 0,
                    InteractionCooldownRemainingSeconds = cooldownRemaining,
                    LastInteractionUtc = state?.LastUsedUtc.ToString("O") ?? string.Empty,
                    MaxHealth = Math.Max(1, e.MaxHealth),
                    Health = Math.Max(1, e.Health > 0 ? e.Health : e.MaxHealth),
                    MaxNano = Math.Max(0, e.MaxNano),
                    Level = Math.Max(0, e.Level),
                    AttackRange = MathF.Max(0f, e.AttackRange),
                    AttackRechargeSeconds = MathF.Max(0f, e.AttackRechargeSeconds),
                    DamageMin = Math.Max(0, e.DamageMin),
                    DamageMax = Math.Max(0, e.DamageMax),
                    Description = e.Description ?? string.Empty
                };
            })
            .ToList();
    }

    public bool TryResolveHighestPoint(int playfieldId, out float x, out float y, out float z)
    {
        x = 0f;
        y = 0f;
        z = 0f;
        if (playfieldId <= 0)
            return false;

        var runtime = GetPlayfield(playfieldId);
        if (runtime?.Entities == null || runtime.Entities.Count == 0)
            return false;

        RuntimeEntity best = null;
        float bestY = float.MinValue;
        foreach (var entity in runtime.Entities)
        {
            if (entity == null)
                continue;
            if (!float.IsFinite(entity.Y))
                continue;
            if (entity.Y > bestY)
            {
                bestY = entity.Y;
                best = entity;
            }
        }

        if (best == null)
            return false;

        x = best.X;
        y = best.Y;
        z = best.Z;
        return true;
    }

    private PlayfieldRuntime LoadPlayfield(int playfieldId)
    {
        string path = ResolveServerPlayfieldPath($"{playfieldId}_runtime_world_objects.json");
        try
        {
            RuntimeWorldObjectsFile file = File.Exists(path) ? JsonSerializer.Deserialize<RuntimeWorldObjectsFile>(File.ReadAllText(path), _jsonOptions) : null;
            List<RuntimeWorldObjectData> runtimeObjects = GetRuntimeObjects(file);
            var entities = runtimeObjects?
                .Where(ShouldIncludeRuntimeObject)
                .Select((o, index) =>
                {
                    RuntimeVector3 position = ResolveRuntimeObjectPosition(o);
                    if (o == null || position == null)
                        return null;

                    RuntimeInteraction interaction = ResolveInteraction(playfieldId, o);
                    string normalizedObjectType = NormalizeRuntimeObjectType(o);
                    return new RuntimeEntity(
                        $"pf{playfieldId}:{(!string.IsNullOrWhiteSpace(o.ImportKey) ? o.ImportKey : o.ObjectType ?? "object")}:{o.IdentityInstance}:{index}",
                        playfieldId,
                        normalizedObjectType,
                        string.IsNullOrWhiteSpace(o.DisplayName) ? (o.TemplateName ?? string.Empty) : o.DisplayName,
                        o.IdentityInstance,
                        o.TemplateId,
                        o.MeshId ?? o.Mesh,
                        position.X,
                        position.Y,
                        position.Z,
                        ResolveRuntimeObjectYawDegrees(o),
                        o.ImportKey ?? string.Empty,
                        ResolveRuntimeMeshNameHint(o),
                        interaction.Type,
                        interaction.InteractionId,
                        interaction.Radius,
                        interaction.Label,
                        interaction.CooldownSeconds,
                        Math.Max(1, o.MaxHealth ?? 120),
                        Math.Max(1, o.Health > 0 ? o.Health : o.MaxHealth ?? 120),
                        Math.Max(0, o.MaxNano ?? 0),
                        Math.Max(0, o.Level ?? 0),
                        MathF.Max(0f, ResolveAttackRange(o)),
                        MathF.Max(0f, ResolveAttackRechargeSeconds(o)),
                        Math.Max(0, ResolveDamageMin(o)),
                        Math.Max(0, ResolveDamageMax(o)),
                        o.Description ?? string.Empty);
                })
                .Where(e => e != null)
                .ToList() ?? new List<RuntimeEntity>();

            List<RuntimeEntity> npcSpawnEntities = LoadNpcSpawnGroupEntities(playfieldId);
            if (npcSpawnEntities.Count > 0)
            {
                Console.WriteLine($"[AO.Server] Loaded {npcSpawnEntities.Count} NPC spawn entities for PF {playfieldId}.");
                entities.AddRange(npcSpawnEntities);
            }

            return new PlayfieldRuntime(playfieldId, file?.PlayfieldName ?? string.Empty, entities);
        }
        catch
        {
            return PlayfieldRuntime.Empty(playfieldId);
        }
    }

    private static List<RuntimeWorldObjectData> GetRuntimeObjects(RuntimeWorldObjectsFile file)
    {
        if (file == null)
            return new List<RuntimeWorldObjectData>();

        if (file.RuntimeWorldObjects != null && file.RuntimeWorldObjects.Count > 0)
            return file.RuntimeWorldObjects;

        if (file.Dynels == null || file.Dynels.Count == 0)
            return new List<RuntimeWorldObjectData>();

        for (int i = 0; i < file.Dynels.Count; i++)
        {
            RuntimeWorldObjectData dynel = file.Dynels[i];
            if (dynel == null)
                continue;

            if (string.IsNullOrWhiteSpace(dynel.ObjectType))
            {
                if (dynel.IsNpc == true)
                    dynel.ObjectType = "NPC";
                else if (dynel.IsPlayer == true)
                    dynel.ObjectType = "Player";
                else if (dynel.IsPet == true)
                    dynel.ObjectType = "Pet";
                else if (!string.IsNullOrWhiteSpace(dynel.IdentityType))
                    dynel.ObjectType = dynel.IdentityType;
                else
                    dynel.ObjectType = "Dynel";
            }

            if (string.IsNullOrWhiteSpace(dynel.DisplayName))
                dynel.DisplayName = dynel.Name;
        }

        return file.Dynels;
    }

    private static bool ShouldIncludeRuntimeObject(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return false;

        string type = (obj.ObjectType ?? string.Empty).Trim();
        if (type.Equals("player", StringComparison.OrdinalIgnoreCase)
            || type.Equals("pet", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeRuntimeObjectType(RuntimeWorldObjectData obj)
    {
        string raw = (obj?.ObjectType ?? obj?.IdentityType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        if (raw.Equals("simplechar", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("npc", StringComparison.OrdinalIgnoreCase))
            return "npc";

        return raw;
    }

    private RuntimeInteraction ResolveInteraction(int playfieldId, RuntimeWorldObjectData obj)
    {
        RuntimeVector3 position = ResolveRuntimeObjectPosition(obj);
        if (position == null)
            return RuntimeInteraction.None;

        // Zone interactions should only bind to objects that are likely intended
        // to be portals/whom-pahs/grid terminals. Otherwise nearby NPCs can
        // accidentally inherit zone links and trigger random teleports.
        if (IsLikelyZoneObject(obj))
        {
            ZoneLinkDefinition zoneLink = FindNearestZoneLink(playfieldId, obj);
            if (zoneLink != null)
            {
                return new RuntimeInteraction(
                    "zone",
                    zoneLink.Id ?? string.Empty,
                    MathF.Max(1.5f, zoneLink.TriggerRadius > 0f ? zoneLink.TriggerRadius : 4f),
                    "Zone",
                    0.6f);
            }
        }

        string objectType = (obj.ObjectType ?? string.Empty).Trim().ToLowerInvariant();
        return objectType switch
        {
            "vendingmachine" => new RuntimeInteraction("shop", obj.ImportKey ?? string.Empty, 3f, "Open shop", 0.9f),
            "terminal" => new RuntimeInteraction("use", obj.ImportKey ?? string.Empty, 2.5f, "Use terminal", 0.5f),
            "door" => new RuntimeInteraction("use", obj.ImportKey ?? string.Empty, 2.5f, "Use door", 0.5f),
            _ => ResolveNpcTalkInteraction(obj, objectType)
        };
    }

    private static RuntimeInteraction ResolveNpcTalkInteraction(RuntimeWorldObjectData obj, string objectType)
    {
        if (obj == null)
            return RuntimeInteraction.None;

        if (IsMonsterLikeObjectType(objectType, obj))
            return RuntimeInteraction.None;

        if (!IsNpcLikeObjectType(objectType, obj))
            return RuntimeInteraction.None;

        int npcConversationId = obj.MonsterData > 0 ? obj.MonsterData : obj.IdentityInstance;
        if (npcConversationId == 0)
            return RuntimeInteraction.None;

        return new RuntimeInteraction("talk", npcConversationId.ToString(), 3f, "Talk", 0.25f);
    }

    private static bool IsNpcLikeObjectType(string objectType, RuntimeWorldObjectData obj)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return false;

        if (objectType.Equals("npc", StringComparison.OrdinalIgnoreCase))
            return true;

        if (objectType.Equals("simplechar", StringComparison.OrdinalIgnoreCase))
            return true;

        string identityType = (obj?.IdentityType ?? string.Empty).Trim();
        if (identityType.Equals("NPC", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsMonsterLikeObjectType(string objectType, RuntimeWorldObjectData obj)
    {
        string identityType = (obj?.IdentityType ?? string.Empty).Trim();
        string displayName = obj?.DisplayName ?? string.Empty;
        string templateName = obj?.TemplateName ?? string.Empty;
        string haystack = string.Join(" ", objectType ?? string.Empty, identityType, displayName, templateName);

        return haystack.IndexOf("monster", StringComparison.OrdinalIgnoreCase) >= 0
               || haystack.IndexOf("mob", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private ZoneLinkDefinition FindNearestZoneLink(int playfieldId, RuntimeWorldObjectData obj)
    {
        RuntimeVector3 position = ResolveRuntimeObjectPosition(obj);
        if (position == null)
            return null;

        bool likelyZoneObject = IsLikelyZoneObject(obj);
        ZoneLinkDefinition best = null;
        float bestDistanceSquared = float.MaxValue;

        foreach (var link in _zoneLinks)
        {
            if (link == null || link.FromPlayfieldId != playfieldId || link.SourceAOPosition == null)
                continue;

            float dx = link.SourceAOPosition.X - position.X;
            float dz = link.SourceAOPosition.Z - position.Z;
            float distanceSquared = (dx * dx) + (dz * dz);
            float radius = MathF.Max(6f, MathF.Max(link.SourceAORadius, link.TriggerRadius) * 1.5f);
            if (distanceSquared > radius * radius)
                continue;

            if (!likelyZoneObject && distanceSquared > MathF.Max(2f, link.TriggerRadius) * MathF.Max(2f, link.TriggerRadius))
                continue;

            if (distanceSquared < bestDistanceSquared)
            {
                best = link;
                bestDistanceSquared = distanceSquared;
            }
        }

        return best;
    }

    private static RuntimeVector3 ResolveRuntimeObjectPosition(RuntimeWorldObjectData obj)
    {
        return obj?.GlobalPosition ?? obj?.Position;
    }

    private static float ResolveRuntimeObjectYawDegrees(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return 0f;

        if (MathF.Abs(obj.YawDegrees) > 0.0001f)
            return obj.YawDegrees;

        if (obj.Rotation == null)
            return 0f;

        float x = obj.Rotation.X;
        float y = obj.Rotation.Y;
        float z = obj.Rotation.Z;
        float w = obj.Rotation.W;

        float sinyCosp = 2f * ((w * y) + (x * z));
        float cosyCosp = 1f - (2f * ((y * y) + (z * z)));
        if (!float.IsFinite(sinyCosp) || !float.IsFinite(cosyCosp))
            return 0f;

        return MathF.Atan2(sinyCosp, cosyCosp) * (180f / MathF.PI);
    }

    private static bool IsLikelyZoneObject(RuntimeWorldObjectData obj)
    {
        string haystack = string.Join(
            " ",
            obj?.ObjectType ?? string.Empty,
            obj?.DisplayName ?? string.Empty,
            obj?.TemplateName ?? string.Empty,
            obj?.ImportKey ?? string.Empty,
            obj?.MeshName ?? string.Empty);

        return haystack.IndexOf("whom", StringComparison.OrdinalIgnoreCase) >= 0
            || haystack.IndexOf("grid", StringComparison.OrdinalIgnoreCase) >= 0
            || haystack.IndexOf("portal", StringComparison.OrdinalIgnoreCase) >= 0
            || haystack.IndexOf("teleport", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private List<RuntimeEntity> LoadNpcSpawnGroupEntities(int playfieldId)
    {
        string path = ResolveServerPlayfieldPath($"{playfieldId}_npc_spawn_groups.json");
        if (!File.Exists(path))
            return new List<RuntimeEntity>();

        try
        {
            var file = JsonSerializer.Deserialize<NpcSpawnGroupsFile>(File.ReadAllText(path), _jsonOptions);
            if (file?.Groups == null || file.Groups.Count == 0)
                return new List<RuntimeEntity>();

            var entities = new List<RuntimeEntity>();
            int sequence = 0;
            foreach (var group in file.Groups)
            {
                if (group?.SpawnPoints == null || group.SpawnPoints.Count == 0)
                    continue;

                string groupId = string.IsNullOrWhiteSpace(group.GroupId)
                    ? $"group_{sequence}"
                    : group.GroupId.Trim();
                int npcCount = Math.Max(1, group.NpcCount);

                for (int i = 0; i < npcCount; i++)
                {
                    RuntimeVector3 spawn = group.SpawnPoints[i % group.SpawnPoints.Count];
                    if (spawn == null)
                        continue;

                    string baseName = string.IsNullOrWhiteSpace(group.DisplayName)
                        ? "NPC"
                        : group.DisplayName.Trim();
                    string displayName = npcCount > 1 ? $"{baseName} {i + 1}" : baseName;
                    string entityId = $"pf{playfieldId}:npcspawn:{groupId}:{i}";

                    entities.Add(new RuntimeEntity(
                        entityId,
                        playfieldId,
                        "npc",
                        displayName,
                        100000 + sequence,
                        group.TemplateId,
                        group.MeshId,
                        spawn.X,
                        spawn.Y,
                        spawn.Z,
                        group.SpawnYawDegrees,
                        groupId,
                        group.MeshName ?? string.Empty,
                        string.Empty,
                        string.Empty,
                        0f,
                        string.Empty,
                        0f,
                        120,
                        120,
                        0,
                        Math.Max(0, group.Level ?? 0),
                        MathF.Max(0f, group.AttackRange),
                        MathF.Max(0f, group.AttackRechargeSeconds),
                        Math.Max(0, group.DamageMin),
                        Math.Max(0, group.DamageMax),
                        group.Description ?? string.Empty));

                    sequence++;
                }
            }

            return entities;
        }
        catch
        {
            return new List<RuntimeEntity>();
        }
    }
    private List<ZoneLinkDefinition> LoadZoneLinks()
    {
        string path = ResolveServerPlayfieldPath("zone_links.json");
        if (!File.Exists(path))
            return new List<ZoneLinkDefinition>();

        try
        {
            var file = JsonSerializer.Deserialize<ZoneLinksFile>(File.ReadAllText(path), _jsonOptions);
            return file?.Links ?? new List<ZoneLinkDefinition>();
        }
        catch
        {
            return new List<ZoneLinkDefinition>();
        }
    }

    private static string ResolveRuntimeMeshNameHint(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return string.Empty;

        if (!IsIgnorableRuntimeMeshName(obj.MeshName))
            return obj.MeshName;

        if (!IsIgnorableRuntimeMeshName(obj.MeshLookupName))
            return obj.MeshLookupName;

        return string.Empty;
    }

    private static bool IsIgnorableRuntimeMeshName(string meshName)
    {
        if (string.IsNullOrWhiteSpace(meshName))
            return true;

        string trimmed = meshName.Trim();
        return trimmed.Equals("NoName", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveServerPlayfieldPath(string fileName)
    {
        string serverPath = Path.Combine(_paths.ServerPlayfieldsRoot, fileName);
        if (File.Exists(serverPath))
            return serverPath;

        return Path.Combine(_paths.PlayfieldsRoot, fileName);
    }

    private sealed class RuntimeWorldObjectsFile
    {
        public int PlayfieldId { get; set; }
        public string PlayfieldName { get; set; } = string.Empty;
        public List<RuntimeWorldObjectData> RuntimeWorldObjects { get; set; } = new();
        public List<RuntimeWorldObjectData> Dynels { get; set; } = new();
    }

    private sealed class RuntimeWorldObjectData
    {
        public string ObjectType { get; set; } = string.Empty;
        public string IdentityType { get; set; } = string.Empty;
        public bool? IsNpc { get; set; }
        public bool? IsPlayer { get; set; }
        public bool? IsPet { get; set; }
        public int IdentityInstance { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public int? MeshId { get; set; }
        public int? Mesh { get; set; }
        public string MeshName { get; set; } = string.Empty;
        public string MeshLookupName { get; set; } = string.Empty;
        public RuntimeVector3 Position { get; set; }
        public RuntimeVector3 GlobalPosition { get; set; }
        public RuntimeQuaternion Rotation { get; set; }
        public float YawDegrees { get; set; }
        public int MonsterData { get; set; }
        public string ImportKey { get; set; } = string.Empty;
        public int? MaxHealth { get; set; }
        public int Health { get; set; }
        public int? MaxNano { get; set; }
        public int? Level { get; set; }
        public float AttackRange { get; set; }
        public float WeaponRange { get; set; }
        public float AttackRechargeSeconds { get; set; }
        public float AttackSpeed { get; set; }
        public float AttackSpeedStat { get; set; }
        public float RunSpeedStat { get; set; }
        public int DamageMin { get; set; }
        public int DamageMax { get; set; }
        public int MinDamage { get; set; }
        public int MaxDamage { get; set; }
        public string Description { get; set; } = string.Empty;
    }
    private sealed class NpcSpawnGroupsFile
    {
        public int PlayfieldId { get; set; }
        public List<NpcSpawnGroupRow> Groups { get; set; } = new();
    }

    private sealed class NpcSpawnGroupRow
    {
        public string GroupId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int NpcCount { get; set; } = 1;
        public int? TemplateId { get; set; }
        public int? MeshId { get; set; }
        public string MeshName { get; set; } = string.Empty;
        public int? Level { get; set; }
        public float AttackRange { get; set; }
        public float AttackRechargeSeconds { get; set; }
        public int DamageMin { get; set; }
        public int DamageMax { get; set; }
        public string Description { get; set; } = string.Empty;
        public float SpawnYawDegrees { get; set; }
        public List<RuntimeVector3> SpawnPoints { get; set; } = new();
    }
    private sealed class RuntimeVector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    private sealed class RuntimeQuaternion
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }

    private readonly record struct RuntimeInteraction(string Type, string InteractionId, float Radius, string Label, float CooldownSeconds)
    {
        public static RuntimeInteraction None => new(string.Empty, string.Empty, 0f, string.Empty, 0f);
    }

    private static float ResolveAttackRange(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return 0f;

        if (obj.AttackRange > 0f)
            return obj.AttackRange;
        if (obj.WeaponRange > 0f)
            return obj.WeaponRange;
        return 0f;
    }

    private static float ResolveAttackRechargeSeconds(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return 0f;

        if (obj.AttackRechargeSeconds > 0f)
            return obj.AttackRechargeSeconds;
        if (obj.AttackSpeed > 0f && obj.AttackSpeed < 120f)
            return obj.AttackSpeed;
        if (obj.AttackSpeedStat > 0f && obj.AttackSpeedStat < 120f)
            return obj.AttackSpeedStat;
        return 0f;
    }

    private static int ResolveDamageMin(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return 0;

        if (obj.DamageMin > 0)
            return obj.DamageMin;
        if (obj.MinDamage > 0)
            return obj.MinDamage;
        return 0;
    }

    private static int ResolveDamageMax(RuntimeWorldObjectData obj)
    {
        if (obj == null)
            return 0;

        if (obj.DamageMax > 0)
            return obj.DamageMax;
        if (obj.MaxDamage > 0)
            return obj.MaxDamage;
        return 0;
    }
}

public sealed class PlayfieldRuntime
{
    private readonly Dictionary<string, RuntimeEntityServiceState> _serviceStateByEntityId;

    public int PlayfieldId { get; }
    public string Name { get; }
    public IReadOnlyList<RuntimeEntity> Entities { get; }

    public PlayfieldRuntime(int playfieldId, string name, IReadOnlyList<RuntimeEntity> entities)
    {
        PlayfieldId = playfieldId;
        Name = name ?? string.Empty;
        Entities = entities ?? Array.Empty<RuntimeEntity>();
        _serviceStateByEntityId = BuildInitialServiceState(Entities);
    }

    public RuntimeEntity FindEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId) || Entities == null)
            return null;

        for (int i = 0; i < Entities.Count; i++)
        {
            if (string.Equals(Entities[i].EntityId, entityId, StringComparison.OrdinalIgnoreCase))
                return Entities[i];
        }

        return null;
    }

    public RuntimeEntityServiceState GetServiceState(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return null;

        _serviceStateByEntityId.TryGetValue(entityId, out var state);
        return state;
    }

    public bool TryConsumeInteraction(string entityId, DateTime utcNow, out PlayfieldInteractionDecision decision)
    {
        decision = PlayfieldInteractionDecision.Fail(entityId, "Unknown runtime interaction request.");
        RuntimeEntity entity = FindEntity(entityId);
        if (entity == null)
        {
            decision = PlayfieldInteractionDecision.Fail(entityId, "Runtime entity was not found.");
            return true;
        }

        if (!_serviceStateByEntityId.TryGetValue(entity.EntityId, out var state) || state == null)
        {
            decision = PlayfieldInteractionDecision.Fail(entity.EntityId, $"{entity.DisplayName} is not interactable.");
            return true;
        }

        if (!state.IsEnabled)
        {
            decision = PlayfieldInteractionDecision.Fail(entity.EntityId, $"{entity.DisplayName} is currently disabled.");
            return true;
        }

        float remaining = state.GetCooldownRemainingSeconds(utcNow);
        if (remaining > 0.001f)
        {
            decision = PlayfieldInteractionDecision.Fail(
                entity.EntityId,
                $"{entity.DisplayName} is cooling down ({remaining:0.0}s).",
                entity.InteractionType,
                entity.InteractionId,
                entity.DisplayName,
                state.UseCount,
                remaining,
                state.IsEnabled);
            return true;
        }

        state.RecordUse(utcNow);
        decision = PlayfieldInteractionDecision.Accepted(
            entity.EntityId,
            $"Interaction accepted for {entity.DisplayName}.",
            entity.InteractionType,
            entity.InteractionId,
            entity.DisplayName,
            state.UseCount,
            state.GetCooldownRemainingSeconds(utcNow),
            state.IsEnabled);
        return true;
    }

    private static Dictionary<string, RuntimeEntityServiceState> BuildInitialServiceState(IReadOnlyList<RuntimeEntity> entities)
    {
        var map = new Dictionary<string, RuntimeEntityServiceState>(StringComparer.OrdinalIgnoreCase);
        if (entities == null)
            return map;

        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId) || string.IsNullOrWhiteSpace(entity.InteractionType))
                continue;

            map[entity.EntityId] = new RuntimeEntityServiceState(
                isEnabled: true,
                cooldownSeconds: MathF.Max(0f, entity.InteractionCooldownSeconds));
        }

        return map;
    }

    public static PlayfieldRuntime Empty(int playfieldId) => new(playfieldId, string.Empty, Array.Empty<RuntimeEntity>());
}

public sealed class RuntimeEntityServiceState
{
    public bool IsEnabled { get; private set; }
    public int UseCount { get; private set; }
    public DateTime LastUsedUtc { get; private set; }
    public DateTime NextAvailableUtc { get; private set; }
    public float CooldownSeconds { get; private set; }

    public RuntimeEntityServiceState(bool isEnabled, float cooldownSeconds)
    {
        IsEnabled = isEnabled;
        CooldownSeconds = MathF.Max(0f, cooldownSeconds);
        LastUsedUtc = DateTime.MinValue;
        NextAvailableUtc = DateTime.MinValue;
        UseCount = 0;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }

    public void RecordUse(DateTime utcNow)
    {
        LastUsedUtc = utcNow;
        UseCount++;
        NextAvailableUtc = CooldownSeconds > 0f
            ? utcNow.AddSeconds(CooldownSeconds)
            : utcNow;
    }

    public float GetCooldownRemainingSeconds(DateTime utcNow)
    {
        if (NextAvailableUtc <= utcNow)
            return 0f;

        return (float)(NextAvailableUtc - utcNow).TotalSeconds;
    }
}

public readonly record struct PlayfieldInteractionDecision(
    bool Success,
    string EntityId,
    string Message,
    string InteractionType,
    string InteractionId,
    string DisplayName,
    int UseCount,
    float CooldownRemainingSeconds,
    bool IsEnabled)
{
    public static PlayfieldInteractionDecision Fail(
        string entityId,
        string message,
        string interactionType = "",
        string interactionId = "",
        string displayName = "",
        int useCount = 0,
        float cooldownRemainingSeconds = 0f,
        bool isEnabled = false)
    {
        return new PlayfieldInteractionDecision(
            false,
            entityId ?? string.Empty,
            message ?? string.Empty,
            interactionType ?? string.Empty,
            interactionId ?? string.Empty,
            displayName ?? string.Empty,
            useCount,
            MathF.Max(0f, cooldownRemainingSeconds),
            isEnabled);
    }

    public static PlayfieldInteractionDecision Accepted(
        string entityId,
        string message,
        string interactionType,
        string interactionId,
        string displayName,
        int useCount,
        float cooldownRemainingSeconds,
        bool isEnabled)
    {
        return new PlayfieldInteractionDecision(
            true,
            entityId ?? string.Empty,
            message ?? string.Empty,
            interactionType ?? string.Empty,
            interactionId ?? string.Empty,
            displayName ?? string.Empty,
            useCount,
            MathF.Max(0f, cooldownRemainingSeconds),
            isEnabled);
    }
}

public sealed record RuntimeEntity(
    string EntityId,
    int PlayfieldId,
    string ObjectType,
    string DisplayName,
    int IdentityInstance,
    int? TemplateId,
    int? MeshId,
    float X,
    float Y,
    float Z,
    float YawDegrees,
    string ImportKey,
    string MeshName,
    string InteractionType,
    string InteractionId,
    float InteractionRadius,
    string InteractionLabel,
    float InteractionCooldownSeconds,
    int MaxHealth,
    int Health,
    int MaxNano,
    int Level,
    float AttackRange,
    float AttackRechargeSeconds,
    int DamageMin,
    int DamageMax,
    string Description);
