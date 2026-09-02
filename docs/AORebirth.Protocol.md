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

## Information Required

- Permission and supported policy for custom clients
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
