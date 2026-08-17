# AOSharp Subway Header3/Header7 Analysis

## Bucket Counts

### Header3
- `unreadable`: 10648
- `mixed-binary`: 491
- `table-like`: 390
- `all-zero`: 190
- `string-like`: 112
- `sparse`: 50
- `code-like`: 2

### Header7
- `unreadable`: 10626
- `mixed-binary`: 706
- `table-like`: 236
- `all-zero`: 176
- `string-like`: 88
- `sparse`: 44
- `code-like`: 7

## Architectural Rooms With Large Surface Meshes
- `Subway Station`: 100
- `Statue Ramp Connector`: 90
- `Helix Descent`: 57
- `0% Transport Aceess Tunnel`: 47
- `Ticket Checkpoint`: 38
- `Ramp Exit West`: 35
- `Ramp Exit East`: 30
- `Black Windows`: 30
- `Shopping Dead-end`: 28
- `Grand Dome`: 28
- `Escalator West`: 26
- `Escalator East`: 26
- `Slum Cathedral`: 25
- `Cave Bridge`: 22
- `Sandy Stairs`: 19
- `Quintiple Bridge`: 18

## Sample Architectural Meshes
- `Ladies' Room` vc=20 ti=102 h3=unreadable (0x4150CF00) h7=unreadable (0xFFFFFFFFF3C7E5FB)
- `Men's Room` vc=20 ti=102 h3=unreadable (None) h7=unreadable (None)
- `Mini to Shopping` vc=20 ti=102 h3=unreadable (0xFFFFFFFF9CDD1B00) h7=mixed-binary (0x6747797C)
- `Mini to Shopping` vc=20 ti=102 h3=unreadable (0x0A45A100) h7=unreadable (0xFFFFFFFF90856E85)
- `Mini to Shopping` vc=20 ti=90 h3=mixed-binary (0x049D5E00) h7=unreadable (0x354C0B46)
- `Mini to Shopping` vc=20 ti=90 h3=mixed-binary (0x68707900) h7=unreadable (0xFFFFFFFFD6F7408B)
- `Double door Mini` vc=20 ti=90 h3=unreadable (0x0436C400) h7=all-zero (0x04910721)
- `Double door Mini` vc=20 ti=108 h3=unreadable (0x0A5A0C00) h7=unreadable (0x0A6FA823)
- `Double door Mini` vc=20 ti=108 h3=all-zero (0x0B738F00) h7=all-zero (0x0B873002)
- `Double door Mini` vc=20 ti=108 h3=mixed-binary (0x0BEAB100) h7=all-zero (0x0BF63326)
- `Double door Mini` vc=20 ti=108 h3=all-zero (0x0C27A200) h7=all-zero (0x0C3B81B8)
- `Double door Mini` vc=20 ti=108 h3=mixed-binary (0x0C9EDD00) h7=mixed-binary (0x0CA1000B)
- `Double door Mini` vc=20 ti=108 h3=unreadable (0x0D1A3000) h7=unreadable (0x0D3F844C)
- `Double door Mini` vc=20 ti=90 h3=unreadable (0x0E8F3900) h7=mixed-binary (0x0EF48203)
- `Double door Mini` vc=20 ti=108 h3=table-like (0x14646200) h7=string-like (0x14750875)
- `Double door Mini` vc=20 ti=108 h3=mixed-binary (0x153BD900) h7=all-zero (0x155AA894)
- `Double door Mini` vc=20 ti=108 h3=all-zero (0x15DB1C00) h7=all-zero (0x15E96F7A)
- `Double door Mini` vc=20 ti=108 h3=all-zero (0x1607DD00) h7=all-zero (0x16195791)
- `Shopping Dead-end` vc=20 ti=108 h3=unreadable (0xFFFFFFFFFF000000) h7=mixed-binary (0x02000000)
- `Shopping Dead-end` vc=20 ti=108 h3=unreadable (0xFFFFFFFFFF000000) h7=unreadable (0x000000FF)
- `Shopping Dead-end` vc=20 ti=108 h3=unreadable (0xFFFFFFFFFF000000) h7=unreadable (0xFFFFFFFFFF000002)
- `Shopping Dead-end` vc=20 ti=108 h3=unreadable (None) h7=mixed-binary (0x00FF0000)
- `Shopping Dead-end` vc=20 ti=108 h3=mixed-binary (0x00FF0200) h7=unreadable (0x00010000)
- `Shopping Dead-end` vc=26 ti=144 h3=unreadable (None) h7=unreadable (None)


## Early Read On Header3 Table-Like Data

Not all `Header3` table-like previews look like the same thing.

Observed buckets from spot checks:
- `engine/code noise`
  - example: `0x10000000` begins with `MZ`, clearly a PE/DOS header and not useful material data
- `utf16-string table`
  - example: `Mini to Dome` with `H3=0x774D5B00` contains UTF-16 text like `BIOS_LID_NOT_EXIST` and `STATUS_...`
  - useful as evidence of referenced data, but probably not texture binding for Subway floors/walls
- `dense FE-terminated tables`
  - examples: `0x132C7800`, `0x1E68E300`, `0x1337E900`, `0x1CED6300`
  - these contain repeating 4-byte groups ending in `FE`
  - this is the most promising `Header3` subfamily so far
- `small struct/table blocks`
  - example: `0x12063100`
  - looks like compact records, counts, and pointer-like values mixed together
- `sparse/zero-filled blocks`
  - likely not enough information on their own

Current best guess:
- the promising `Header3 table-like` cases are the dense `.. .. .. FE` records, not the DOS-header or UTF-16 string cases
- those dense records may be:
  - signed byte tables
  - packed color/material parameters
  - compact per-surface descriptors
  - or another indexed lookup structure used by the room renderer

So the next narrow step should be:
1. focus only on large architectural meshes with `Header3 = table-like`
2. ignore obvious DOS/PE and UTF-16 status-string cases
3. compare record lengths and repeated 4-byte group patterns across rooms like:
   - `Subway Station`
   - `Statue Ramp Connector`
   - `Helix Descent`
   - `Grand Dome`
