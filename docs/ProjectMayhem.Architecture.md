# ProjectMayhem Architecture

## Overview

ProjectMayhem is currently a Unity-heavy prototype with a shared C# gameplay core. The target architecture should be:

- `AO.Core`: gameplay rules, stats, items, modifiers, progression, simulation primitives.
- `AO.Server`: Project Mayhem's current authoritative runtime and a future mock,
  test harness, or optional compatible backend.
- `AO.Unity`: client-side presentation, world rendering, input, camera, local UI, optional prediction.
- `AO.Tools`: local experiments and offline tooling.
- `AO.Client`: backend-neutral client state plus adapters for the live service,
  Ithaca, AORebirth, and other supported servers; currently minimal.

## What Exists Today

- `AO.Core` already contains the most reusable gameplay logic.
- `AO.Unity` currently owns the main runtime bootstrap through `AODataManager` and `PrototypeWorldBootstrap`.
- `AO.Unity` loads gameplay data from `Assets/StreamingAssets/AOData`.
- `server.py` is only a lightweight relay prototype and is not authoritative.

## Client Loading Lifecycle (Current and Target)

Current runtime behavior is effectively two-stage:

1. Boot/character phase (client shell):
   - Startup UI and character flow (`PrototypeClientUGUI.CharacterFlow`) are initialized.
   - Character select/create is available before entering a character-specific world state.
2. Enter-world phase:
   - After Play/Create confirmation, the client transitions to the selected character's playfield.
   - `PrototypeWorldBootstrap` handles playfield loading, transition spawn, and loading overlay visibility.

Target MMO-style behavior should keep this two-stage structure explicit:

1. Stage A: account + character list/create readiness only.
2. Stage B: world/playfield loading only after character selection/create confirmation.

This keeps first-paint and character flow responsive while deferring heavy world work until it is actually needed.

## Recommended Client/Server Boundary

Whichever server the user connects to should own:

- Character identity, inventory, equipment, stats, skill spending, XP, and progression.
- Item definitions, item instances, stat maps, breed/profession tuning, slot rules, and derived stat inputs.
- World position, movement acceptance, zone transitions, and playfield placement.
- Combat, nano execution, cooldowns, loot, mission state, and persistence once implemented.
- Zone lifecycle policy (warm/active/sleep), including idle timeout behavior.
- Area/chunk interest management and spawn streaming policy.

Project Mayhem should own:

- Camera, controls, animation, VFX, audio, HUD, drag/drop UX, and scene presentation.
- Sending player intents to the server.
- Rendering authoritative snapshots and deltas from the server.

The Unity client should not be the source of truth for:

- Final movement outcome.
- Equipment validity.
- Stat totals.
- XP/IP gains.
- Inventory contents.

## Current Important Files

- `AO.Core/Characters/Character.cs`
- `AO.Core/Characters/CharacterEquipment.cs`
- `AO.Core/Characters/EquipmentValidator.cs`
- `AO.Core/Stats/CharacterStats.cs`
- `AO.Core/Modifiers/ModifierAggregator.cs`
- `AO.Core/Simulation/GameLoop.cs`
- `AO.Core/World/WorldState.cs`
- `AO.Unity/Assets/Scripts/AO.Data.Unity/AODataManager.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWorldBootstrap.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWalkerController.cs`
- `AO.Server/AuthoritativeGameServer.cs`
- `AO.Server/AuthoritativeGameData.cs`

## Near-Term Migration Guidance

1. Keep rules in `AO.Core`.
2. Define stable intents and client-domain events in `AO.Client`.
3. Add isolated protocol adapters per supported server and version.
4. Treat `AODataManager` as a Unity adapter, not the source of live authority.
5. Replace direct client-side state mutation with intent submission through the
   selected backend adapter.
6. Retain `AO.Server` as the existing Project Mayhem backend and a deterministic
   integration-test target.

## Zone Scalability Direction

For long-term player scale, preferred direction is:

- Keep zone instances active while players are present.
- After last player leaves, keep zone warm for a short TTL, then transition zone to sleep.
- Stream runtime objects/NPCs by area-of-interest chunks instead of loading entire zone content at once.
- Use authoritative interest sets from server; clients render only what is in-scope.
