# AO.Core Reference

## Purpose

`AO.Core` is the shared domain layer. It should contain gameplay rules that do not require Unity.

## Owns

- Character progression and IP spending.
- Equipment validation rules.
- Item definitions and item instances.
- Modifier aggregation.
- Base and derived stat calculation.
- Lightweight simulation primitives such as `GameLoop` and `WorldState`.

## Should Not Own

- Unity scene objects or MonoBehaviours.
- Network transport code.
- Persistence infrastructure.
- UI concerns.

## Key Classes

- `AO.Core/Characters/Character.cs`
- `AO.Core/Characters/CharacterEquipment.cs`
- `AO.Core/Characters/EquipmentValidator.cs`
- `AO.Core/Stats/CharacterStats.cs`
- `AO.Core/Stats/Profession.cs`
- `AO.Core/Stats/ProfessionLoader.cs`
- `AO.Core/Modifiers/ModifierAggregator.cs`
- `AO.Core/Items/ItemDefinition.cs`
- `AO.Core/Items/ItemInstance.cs`
- `AO.Core/Simulation/GameLoop.cs`
- `AO.Core/World/WorldState.cs`
- `AO.Core/Entities/Character.cs`

## Important Design Notes

- `Character` is the richer RPG model. It owns stats, inventory, equipment, and progression.
- `AO.Core.Entities.Character` is a lighter movement/simulation entity and is better treated as a world representation than a full RPG profile.
- Several systems currently rely on static delegates for data lookup. That is workable, but long-term those inputs should be set by the authoritative server bootstrap.

## AI Guidance

- Add reusable rules here when both the server and clients may need them.
- Avoid adding Unity types.
- Avoid making this project directly responsible for file-system data loading.
