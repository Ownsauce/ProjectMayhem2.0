namespace AO.Client.World
{
    public enum NearbyEntityKind { Player, Npc }

    public sealed class NearbyEntity
    {
        public NearbyEntity(int identityType, int identityInstance, NearbyEntityKind kind,
            string name, int level, int health, int healthDamage, int playfieldId,
            float x, float y, float z, uint appearance = 0,
            int npcFamily = 0, int npcLosHeight = 0, int npcUnknown = 0,
            uint monsterData = 0, int monsterScale = 0,
            int visualFlags = 0, int visibleTitle = 0)
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

        public NearbyEntity WithPosition(float x, float y, float z)
        {
            return new NearbyEntity(IdentityType, IdentityInstance, Kind, Name, Level,
                Health, HealthDamage, PlayfieldId, x, y, z, Appearance,
                NpcFamily, NpcLosHeight, NpcUnknown, MonsterData, MonsterScale,
                VisualFlags, VisibleTitle);
        }

        public NearbyEntity WithHealth(int maximumHealth, int currentHealth)
        {
            int damage = maximumHealth > currentHealth ? maximumHealth - currentHealth : 0;
            return new NearbyEntity(IdentityType, IdentityInstance, Kind, Name, Level,
                maximumHealth, damage, PlayfieldId, X, Y, Z, Appearance,
                NpcFamily, NpcLosHeight, NpcUnknown, MonsterData, MonsterScale,
                VisualFlags, VisibleTitle);
        }
    }
}
