# AO Project — Core Agents & Systems

This document provides an overview of the primary agents (classes, modules, and systems) in the **AO** project — focused on **items, equipment, stats, and modifiers** — including their responsibilities and interactions.

## 1. AODataManager

**Location:** `AO.Data.Unity/AODataManager`  
**Type:** Singleton / Data Manager

**Responsibilities:**

- Loads raw JSON data (Items, Nanos, Stats) from `StreamingAssets`
- Converts `AO.Data.Core` models into `AO.Core` models (`ItemDefinition`, `ItemInstance`) for runtime use
- Maintains caches for fast lookup:
  - `_coreDefs` → `AO.Core` `ItemDefinition`s
  - `_coreInstances` → `AO.Core` `ItemInstance`s
- Provides delegates for `CharacterEquipment` and `ModifierAggregator` to access data
- Ensures a single instance exists via `EnsureInstance()`

**Key Methods:**

- `LoadItems()`
- `BootstrapDelegates()`
- `GetCoreDefinition(int aoid)`
- `GetCoreInstance(long aoid)`

**Notes:**

- Acts as the bridge between the data layer (JSON / `AO.Data.Core`) and core gameplay logic (`AO.Core`).

## 2. CharacterEquipment

**Location:** `AO.Core.Characters/CharacterEquipment`  
**Type:** Component / Runtime Manager

**Responsibilities:**

- Manages equipped items on a character by slot
- Enforces equipment rules using `EquipmentValidator`
- Provides a mapping of equipped slots → item instances
- Delegates data access to `AODataManager` via:
  - `GetItemInstance`
  - `GetItemDefinition`

**Key Methods:**

- `EquipItem(int slotId, long instanceId)`
- `UnequipSlot(int slotId)`
- `GetAllEquipped()`

**Notes:**

- Works exclusively with Core types (`AO.Core.ItemDefinition` / `AO.Core.ItemInstance`)
- Relies on `ModifierAggregator` to update stats after equipment changes

## 3. ModifierAggregator

**Location:** `AO.Core.Modifiers/ModifierAggregator`  
**Type:** Utility / Service

**Responsibilities:**

- Aggregates all stat modifiers from currently equipped items
- Provides read-only access to the final list of active modifiers
- Uses delegates (`GetItemInstance`, `GetItemDefinition`) to resolve items at runtime

**Key Methods:**

- `Rebuild(IReadOnlyDictionary<int, long> equipped)` — Recalculates modifiers for current equipment

**Notes:**

- Separates modifier aggregation logic from character/equipment concerns
- Works exclusively with Core types to prevent data-layer leakage

## 4. EquipmentValidator

**Location:** `AO.Core.Characters/EquipmentValidator`  
**Type:** Static Utility

**Responsibilities:**

- Checks whether a character can equip a given item in a specific slot
- Validates:
  - Item class vs slot type (Weapon / Armor / Implant)
  - Level requirements
  - Stat requirements (derived via modifiers)

**Key Methods:**

- `CanEquip(Character character, ItemDefinition itemDef, int slotId)`
- `GetItemClass(ItemDefinition item)`

**Notes:**

- Uses only `AO.Core.ItemDefinition` for validation
- Level and other requirements are derived from stat modifiers (no separate properties)

## 5. Character

**Location:** `AO.Core.Characters/Character`  
**Type:** Core Entity

**Responsibilities:**

- Represents a player or NPC in the game
- Holds stats (`StatsContainer`), inventory, and equipment
- Manages IP/level progression
- Applies equipment modifiers via `ModifierAggregator`

**Key Methods:**

- `EquipItem(int slotId, long instanceId)`
- `UnequipSlot(int slotId)`
- `RecalculateDerivedStats()`
- `TryIncreaseStat(string statName, int amount)`

**Notes:**

- Delegates all data access to `AODataManager` via static Core delegates
- Updates derived stats whenever equipment or base stats change

## 6. AO.Data.Core Models

**Location:** `AO.Data.Core`  
**Type:** Data Models

**Classes:**

- `Item` → Raw JSON representation
- `ItemDefinition` → Data-level definition
- `ItemInstance` → Data-level item instance
- `NanoProgram` → Data-level nanos
- `StatMapEntry` → Stat mapping
- `StatModifier` → Data-level stat modifier

**Responsibilities:**

- Represent data exactly as read from JSON
- Used by `AODataManager` to bootstrap Core models
- Not intended for direct use in runtime gameplay logic

## 7. AO.Core.Items Models

**Location:** `AO.Core.Items`  
**Type:** Core Runtime Models

**Classes:**

- `ItemDefinition` → Core runtime definition
- `ItemInstance` → Core runtime instance
- `StatModifier` → Core modifier

**Responsibilities:**

- Represent items and modifiers during gameplay
- Used by `Character`, `CharacterEquipment`, and `ModifierAggregator`
- Fully decoupled from the JSON / data layer

## Diagram of Relationships (Simplified)

```text
AODataManager
├── Loads AO.Data.Core Models (Item, ItemDefinition, ItemInstance)
├── Converts to AO.Core Models (ItemDefinition, ItemInstance)
└── Provides delegates ──→ CharacterEquipment / ModifierAggregator

Character
├── CharacterEquipment
│   └── Uses delegates to resolve items
├── Inventory
├── StatsContainer
└── ModifierAggregator
    └── Aggregates modifiers from equipped items