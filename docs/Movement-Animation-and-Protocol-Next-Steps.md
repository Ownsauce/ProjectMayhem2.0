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
   - Main-inventory moves, equip/unequip, and wear-slot swaps now send native
     ClientMoveItemToInventory requests and apply ContainerAddItem confirmations.
     Protocol fixtures cover all four wear pages; AORebirth Local play-mode and
     reconnect verification remain pending.
   - Worn meshes and textures refresh through the confirmed character snapshot.
   - Backpack transfers and other item-action packets are the next inventory work.

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

## Native inventory verification (2026-09-06)

The first connected item-action slice covers main inventory and weapons, armor,
implants, and social wear slots. Backpack contents/transfers, stack operations,
item use, deletion, and uploading nanos remain pending; those local inventory
mutations are blocked in native sessions. Only one UI move is requested at a time,
with a 15-second confirmation timeout. A timeout does not change inventory or retry.

Protocol evidence: AORebirth's `InventoryContainerRuntimeService.TryMoveOwnedInventoryItem`
accepts `ClientMoveItemToInventory` (0x5469373F), then sends `ContainerAddItem`
(0x47537A24) with the concrete destination. Equipment destinations may swap an
occupied item into the source slot. Wire identities stay inside the AORebirth
adapter; UI requests use an item area and zero-based index.

Manual check on AORebirth Local:

1. Log in with a character carrying an equippable item and two empty inventory slots.
2. Move an item between empty main inventory slots. Confirm it changes only after
   the server reply, and rapid repeat input does not send overlapping UI requests.
3. Equip and unequip through double-click and drag/drop. Check the wear slot,
   character appearance, and subsequent server stat updates.
4. Swap an occupied wear slot and verify the displaced item returns to the source
   inventory slot. Exercise armor, weapons, implants (with clinic access), and social.
5. Attempt an equip that fails requirements. Inventory and appearance must remain
   unchanged; the confirmation timeout must not apply or retry the action.
6. Disconnect/reconnect and verify item locations, appearance, and stats match the
   server. Repeat on other server targets before claiming their compatibility.

Automated fixtures cover request bytes, confirmed movement/swaps, preservation of
item/nano/stat data, all wear pages, unrelated owners, invalid pages, and truncation.
