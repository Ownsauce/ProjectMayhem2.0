# AO.Client Reference

## Purpose

`AO.Client` is the intended backend-neutral networking and client-state layer.
It should let Unity connect to the live service, Ithaca, AORebirth, or another
supported server without exposing backend packet structures to presentation
code.

## Current Status

- `IGameServerBackend` defines the backend-neutral connection, authentication,
  character-list, world-entry, bootstrap, and nearby-entity lifecycle.
- Shared endpoint, capability, connection-state, authentication, character, and
  world-entry models are present.
- `AORebirthBackend` authenticates, selects a character, follows the zone
  redirect, decompresses the zone stream, reads authoritative playfield state,
  translates nearby character spawns into neutral player/NPC models, and applies
  movement, health, spawn, and removal packets to a persistent world snapshot.
- Static-object discovery translates vending machines and preserves door/corpse
  identities without leaking their AORebirth packet layouts.
- Unity still uses `AuthoritativeNetworkClient` directly during the migration.

## Recommended Direction

- Define stable client intents and domain events.
- Define connection, authentication, character-selection, and world-session interfaces.
- Implement each server protocol in a separate backend adapter.
- Track capabilities and compatibility by backend and server version.
- Keep the existing Project Mayhem TCP/JSON protocol as a mock/test adapter.

## AI Guidance

- Keep AORebirth opcode and flag handling inside its backend adapter.
- Keep Unity presentation independent of live, Ithaca, AORebirth, and Project
  Mayhem wire formats.
