using System.Collections.Generic;

namespace AO.Client.World
{
    public enum NearbyEntityKind { Player, Npc }

    public sealed class NearbyEntity
    {
        private readonly bool? _knownDead;
        public NearbyEntity(int identityType, int identityInstance, NearbyEntityKind kind,
            string name, int level, int health, int healthDamage, int playfieldId,
            float x, float y, float z, uint appearance = 0,
            int npcFamily = 0, int npcLosHeight = 0, int npcUnknown = 0,
            uint monsterData = 0, int monsterScale = 0,
            int visualFlags = 0, int visibleTitle = 0, CharacterAppearanceSnapshot equipmentAppearance = null,
            IReadOnlyList<NanoEffectUpdate> activeNanos = null,
            int corpseSourceType = 0, int corpseSourceInstance = 0, bool? knownDead = null)
        {
            IdentityType = identityType;
            IdentityInstance = identityInstance;
            Kind = kind;
            Name = name ?? string.Empty;
            Level = level;
            Health = health;
            HealthDamage = healthDamage;
            PlayfieldId = playfieldId;
            X = x;
            Y = y;
            Z = z;
            Appearance = appearance;
            NpcFamily = npcFamily;
            NpcLosHeight = npcLosHeight;
            NpcUnknown = npcUnknown;
            MonsterData = monsterData;
            MonsterScale = monsterScale;
            VisualFlags = visualFlags;
            VisibleTitle = visibleTitle;
            EquipmentAppearance = equipmentAppearance;
            ActiveNanos = activeNanos;
            CorpseSourceType = corpseSourceType;
            CorpseSourceInstance = corpseSourceInstance;
            _knownDead = knownDead;
        }

        public int IdentityType { get; }
        public int IdentityInstance { get; }
        public NearbyEntityKind Kind { get; }
        public string Name { get; }
        public int Level { get; }
        public int Health { get; }
        public int HealthDamage { get; }
        public int PlayfieldId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public uint Appearance { get; }
        public int NpcFamily { get; }
        public int NpcLosHeight { get; }
        public int NpcUnknown { get; }
        public uint MonsterData { get; }
        public int MonsterScale { get; }
        public int VisualFlags { get; }
        public int VisibleTitle { get; }
        public CharacterAppearanceSnapshot EquipmentAppearance { get; }
        // Null means no full snapshot was supplied; empty means no running buffs.
        public IReadOnlyList<NanoEffectUpdate> ActiveNanos { get; }
        public bool IsCorpse => IdentityType == 51050;
        public bool IsDead => IsCorpse || (_knownDead ?? (Health > 0 && HealthDamage >= Health));
        public int CorpseSourceType { get; }
        public int CorpseSourceInstance { get; }

        public bool IsMorphedPlayer => Kind == NearbyEntityKind.Player && MonsterData > 0;

        public NearbyEntity WithVisualStats(uint monsterData, int monsterScale,
            CharacterAppearanceSnapshot equipmentAppearance)
        {
            return new NearbyEntity(IdentityType, IdentityInstance, Kind, Name, Level,
                Health, HealthDamage, PlayfieldId, X, Y, Z, Appearance,
                NpcFamily, NpcLosHeight, NpcUnknown, monsterData, monsterScale,
                equipmentAppearance?.VisualFlags ?? VisualFlags, VisibleTitle, equipmentAppearance, ActiveNanos, CorpseSourceType, CorpseSourceInstance, _knownDead);
        }

        public NearbyEntity WithEquipmentAppearance(CharacterAppearanceSnapshot value)
        {
            value = value?.WithHead(value.HeadMeshId ?? EquipmentAppearance?.HeadMeshId);
            return new NearbyEntity(IdentityType, IdentityInstance, Kind, Name, Level,
                Health, HealthDamage, PlayfieldId, X, Y, Z, Appearance,
                NpcFamily, NpcLosHeight, NpcUnknown, MonsterData, MonsterScale,
                value?.VisualFlags ?? VisualFlags, VisibleTitle, value, ActiveNanos, CorpseSourceType, CorpseSourceInstance, _knownDead);
        }

        public NearbyEntity WithPosition(float x, float y, float z)
        {
            return new NearbyEntity(IdentityType, IdentityInstance, Kind, Name, Level,
                Health, HealthDamage, PlayfieldId, x, y, z, Appearance,
                NpcFamily, NpcLosHeight, NpcUnknown, MonsterData, MonsterScale,
                VisualFlags, VisibleTitle, EquipmentAppearance, ActiveNanos, CorpseSourceType, CorpseSourceInstance, _knownDead);
        }

        public NearbyEntity WithHealth(int maximumHealth, int currentHealth)
        {
            int damage = maximumHealth > currentHealth ? maximumHealth - currentHealth : 0;
            return new NearbyEntity(IdentityType, IdentityInstance, Kind, Name, Level,
                maximumHealth, damage, PlayfieldId, X, Y, Z, Appearance,
                NpcFamily, NpcLosHeight, NpcUnknown, MonsterData, MonsterScale,
                VisualFlags, VisibleTitle, EquipmentAppearance, ActiveNanos, CorpseSourceType, CorpseSourceInstance, currentHealth <= 0);
        }
    }
}
