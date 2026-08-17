using System;
using System.Collections.Generic;

namespace AO.Data.Core
{
    [Serializable]
    public class Item
    {
        public int DBType;
        public int AOID;
        public string Name;
        public string Description;
        public string Type;
        public int Level;
        public int Rarity;
        public List<RawStatValue> StatValues = new();
        public List<StatModifier> StatModifiers = new();
        public ItemAttackDefenseData AttackDefenseData;
        public ItemActionData ActionData;
        public List<ItemSpellGroup> SpellData = new();
    }

    [Serializable]
    public class ItemAttackDefenseData
    {
        public List<RawStatValue> Attack = new();
        public List<RawStatValue> Defense = new();
    }

    [Serializable]
    public class ItemActionData
    {
        public List<ItemAction> Actions = new();
    }

    [Serializable]
    public class ItemAction
    {
        public int Action;
        public List<ItemActionCriterion> Criteria = new();
    }

    [Serializable]
    public class ItemActionCriterion
    {
        public int Value1;
        public int Value2;
        public int Operator;
    }

    [Serializable]
    public class ItemSpellGroup
    {
        public List<ItemSpellData> Items = new();
        public int Event;
    }

    [Serializable]
    public class ItemSpellData
    {
        public int NanoID;
        public int Stat;
        public int Amount;
        public int MinValue;
        public int MaxValue;
        public int ModifierStat;
        public int Texture;
        public int Location;
        public object A;
        public object B;
        public object C;
        public object D;
        public object E;
        public object Version;
        public object Patch;
        public List<ItemActionCriterion> Criteria = new();
        public int Target;
        public int TickCount;
        public int TickInterval;
        public object Unknown2;
        public int SpellID;
        public string SpellFormat;
        public string SpellDescription;
    }

    [Serializable]
    public class ItemDefinition
    {
        public int Id;
        public string Name;
        public string Description;
        public int IconId;
        public int SlotType;
        public Dictionary<int, float> Stats = new();
        public List<int> SpellEffects = new();
        public List<StatModifier> StatModifiers = new();
        public int RequiredLevel;
        public List<string> AllowedProfessions = new();
        public string Type;
    }

    [Serializable]
    public class ItemInstance
    {
        public int InstanceId;
        public int DefinitionId;
        public ItemDefinition Definition;
        public int Quantity;
    }

    [Serializable]
    public class NanoProgram
    {
        public int Id;
        public string Name;
        public string Description;
        public int IconId;
        public int RequiredLevel;
        public Dictionary<int, float> Effects;
    }

    [Serializable]
    public class StatMapEntry
    {
        public int Id;
        public string Name;
    }

    [Serializable]
    public class StatModifier
    {
        public int StatId;
        public int Value;
    }

    [Serializable]
    public class RawStatValue
    {
        public int Stat;
        public int RawValue;
    }
}
