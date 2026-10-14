# AORebirth Protocol Research

AORebirth is one Project Mayhem backend target, alongside the live service,
Ithaca, and other compatible private servers. This document records only the
AORebirth adapter's research; it does not define the client-wide protocol.

## Status

The code-first AORebirth adapter has been verified against AORebirth Local for
authentication, character listing and selection, login-to-zone handoff, zlib
zone framing, `PlayfieldAnarchyF`, and `SimpleCharFullUpdate`. The latter is
translated into nearby player/NPC identity, name, level, health, playfield, and
coordinates. Continuous character state handles SCFU upserts, CharDCMove and
DropDynel positions, Stat life/health values, and ToClientQuit/Despawn removal.
Most static-dynel payload details and richer combat state are not decoded yet.
VendingMachineFullUpdate is now decoded into neutral object identity, name,
playfield, and either a direct transform or a link to the positioning NPC.
Door and corpse envelopes are recognized by identity; their remaining
capture-dependent payload fields are intentionally not interpreted yet.

## Implemented corpse transfers and vendor purchases

`ContainerAddItem` (0x47537A24) confirms corpse transfers using source page 107
and an unsigned packed handle/slot. Resolve the item from the opened container,
apply the server's concrete main-inventory destination, and publish both changed
snapshots. Foreign, truncated, unknown-source, duplicate, and occupied-slot
confirmations do not create items.

`ShopUpdate` (0x58362220) supplies indexed low/high template pairs and QL.
`Trade` (0x36284F6E) opens the shop, acknowledges stock selections, and completes
or declines it. A purchase sends AddItem followed by Confirm after the selection
acknowledgement. `AddTemplate` (0x052E2F0C) grants the accepted item into the first
free main slot; credit changes arrive through authoritative stats. Shop stock
does not include prices; the server calculates the purchase cost at commit.
These layouts are checked against AORebirth's `InventoryMoveService`,
`TradeService`, `ShopStock`, and messaging contracts in the sibling checkout.
The current UI supports buying; selling and player-to-player trade remain separate work.

AORebirth allocates a runtime vending-machine ID for static placements. Bind the
rendered terminal's original AO position to its `VendingMachineFullUpdate` before
sending Use or accepting stock. NPC-owned machines are opened through their
linked NPC identity; shop/trade replies retain the machine identity. An explicit
zero-HP update is a death even when the initial maximum HP was omitted, and that
state survives movement and appearance updates.

## Information Required

- Endpoint discovery and server version negotiation
- Authentication and session-key exchange
- Login, character list, character selection, and zone handoff
- Packet framing, byte order, compression, encryption, and checksums
- Message/opcode definitions and identity representation
- Coordinate systems and movement updates
- Dynel create, update, and delete messages
- Inventory, equipment, stats, combat, nanos, quests, shops, loot, and chat
- Disconnect, reconnect, timeout, and error behavior

## Implementation Boundary

Raw AORebirth packet structures belong in an AORebirth-specific area such as
`AO.Client/Backends/AORebirth`. Unity should
consume stable client-domain events such as character lists, zone snapshots,
entity deltas, inventory updates, and chat messages. Presentation code should
not parse network packets directly.

Do not infer packet formats from Project Mayhem's existing JSON transport.
