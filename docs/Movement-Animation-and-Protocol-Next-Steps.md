# Movement Animation and Protocol Next Steps

## Current animation fix

Project Mayhem now drives direct character meshes through AO's `CatAnimPlayer`
instead of expecting a Unity `AnimatorController`. The logical movement mapping is:

| Input | Movement | CAT animation |
|---|---|---|
| W | Forward | `run` or `walk` |
| S | Backward | `run-back` or `walk-back` |
| Z | Strafe left | `walk-left` |
| C | Strafe right | `walk-right` |
| Space | Stationary jump | `jump-stand` then `jump-land-idle` |
| W + Space | Moving jump | `jump-forward` then `jump-land-run` |
| X | Sit/stand | `sit-start`, `idle-sit`, `sit-stop`, then `idle` |
| A / D | Rotate left/right | Rotation only; no separate turn clip is currently resolved |

Backspace toggles walk/run mode. Sit and jump animations are one-shots and must
not be replaced by the regular locomotion update before they finish.

The direct CAT resolver and player are currently synchronized with their Lost
Eden implementations. Remaining animation testing should cover each breed/sex,
walk mode, directional changes, jumping while moving, and rapid sit/stand input.

## Server systems to connect next

Implement these in order, keeping the server authoritative:

1. Equipment state
   - Initial `FullCharacter` synchronization for weapons, armor, implants, and
     social slots is implemented. Server slot entries resolve through the local
     ResourceDatabase, populate the wear windows, refresh worn visuals, and
     rebuild equipment-derived stats.
   - Packet tests cover all four wear-slot families and uploaded nano IDs.
   - Decode incremental equip/unequip updates.
   - Send the correct equip, unequip, and move-item messages.
   - Refresh worn meshes and textures only after server confirmation.

2. Nano programs
   - Populate uploaded programs from `FullCharacter` nano IDs.
   - Resolve nano definitions lazily from the selected AO ResourceDatabase.
   - Implement cast requests, server acknowledgements, recharge, lockouts, and
     error feedback.

3. NCU
   - Decode maximum/used NCU statistics.
   - Decode the initial active nano-effect list and incremental buff changes.
   - Populate the NCU and running-NCU windows with duration, caster, stacking,
     and cancellation state supplied by the server.

4. Inventory, backpacks, loot, and item actions
   - Complete incremental container updates and backpack contents.
   - Connect item movement, split stacks, use, delete, and loot operations.
   - Continue resolving item definitions and icons lazily from the database;
     do not restore the large `items.json` or exported icon folders.

5. Chat
   - Implement vicinity/system messages on the zone connection.
   - Add the separate chat-server authentication and channel connection needed
     for tells, teams, organizations, and public channels.
   - Route incoming messages into the existing chat windows and send typed input
     through the appropriate server transport.

## Cross-server requirement

AORebirth Local is the first test target because its server source and logs are
available. Live/Rubi-Ka and Ithaca/Project Rubi-Ka must use capability-specific
authentication and packet handling where their protocols differ. Shared packet
formats should use the same neutral client snapshots and UI code; server-specific
wire details must remain inside their backend/protocol adapters.

## Verification rule

A window is not considered connected merely because it opens or displays local
placeholder data. Each system is complete only when initial state loads from the
server, user actions are sent to the server, server confirmation updates the UI,
and reconnecting reproduces the authoritative state.
