# AOSharp Subway Surface Material Analysis

Source dump:
- `<local-app-data>\AOSharp\AOPlayfieldExtractorPlugin\Exports\127_Condemned_Subway_(dng)_20260319_174934.json`
- Later pointer-probe dump:
  - `<local-app-data>\AOSharp\AOPlayfieldExtractorPlugin\Exports\127_Condemned_Subway_(dng)_20260319_195038.json`

Scope:
- Playfield `127` (`Condemned Subway (dng)`)
- AOSharp room-surface export
- Focused on `SurfaceMaterial.PostVertexPreviewHex` and related raw mesh metadata

## Current State

What is working:
- Room geometry export is good.
- `SurfaceMeshes` are populated.
- Raw post-vertex diagnostics are being exported.

What is not working yet:
- No real texture/material parsing.
- `TextureId`, `MaterialId`, `OverrideTextureId`, `OverrideMaterialId` are still `null`.
- `UVs` and `Normals` are still empty.

## Confirmed Meanings

From sampled meshes:
- `RawHeaderInts[0]` appears to match triangle count, not texture/material id.
  - Example: `TriangleIndexCount = 72` means `24` triangles, and `RawHeaderInts[0] = 24`
- `RawHeaderInts[1]` appears to match vertex count.
  - Example: `VertexCount = 14`, `RawHeaderInts[1] = 14`
- `TriangleIndexBytes` and `VertexBytes` are consistent with the already working geometry read path.

So the known header fields are currently:
- `RawHeaderInts[0]` = triangle count
- `RawHeaderInts[1]` = vertex count

## Header Candidate Fields

The next two header ints are now the strongest candidates for additional resource linkage:

- `RawHeaderInts[2]`
- `RawHeaderInts[3]`

### `RawHeaderInts[2]`

This field looks pointer-like.

Why:
- values are large and highly varied
- they cluster into address-looking ranges such as:
  - `0x10D8....`
  - `0x13A....`
  - `0x13C....`
  - `0x13FB....`
- they do not behave like small counts, flags, or material ids

Sample values:
- `0x13C6E448`
- `0x13D13508`
- `0x10D8CAC8`
- `0x13A001F0`
- `0x13FB40C0`

Current hypothesis:
- `RawHeaderInts[2]` is very likely an unmanaged pointer or pointer-like reference to another structure.

Latest update from the `195038` dump:
- `HeaderPointerCandidatePreviewHex` for `RawHeaderInts[2]` frequently mirrors triangle and vertex data.
- This weakens the "material table pointer" hypothesis.
- Current best interpretation:
  - `RawHeaderInts[2]` is still pointer-like
  - but it is probably pointing into geometry-related memory, not directly to texture/material binding

### `RawHeaderInts[3]`

This field looks more like a packed value, flags, or typed metadata than a plain pointer.

Why:
- many values have low byte `00`
- upper/lower 16-bit halves show repeated patterns
- the low 16-bit half often lands on aligned step values such as:
  - `9216`
  - `9984`
  - `10496`
  - `11776`
  - `12800`
  - `15360`
  - `16640`
  - `17408`
  - `17664`
- a large number of entries are also exactly `0`

Observed top patterns:
- `H3Lo = 0` is very common
- `H3Hi = 0` is also very common
- some entries have values like:
  - `0x00FFFD00`
  - `0x560D0200`
  - `0x78A70000`
  - `0x5F4C2600`
  - `0x41F00C00`

Current hypothesis:
- `RawHeaderInts[3]` is probably not a direct texture id
- more likely:
  - packed flags
  - subrecord type/offset encoding
  - typed metadata associated with the post-vertex block

### `RawHeaderInts[4+]`

Current state:
- `RawHeaderInts[4]` and `RawHeaderInts[5]` are `0` in the overwhelming majority of exported Subway meshes
- they do not currently look like the immediate source of texture/material binding

Current interpretation:
- if the material binding is in the current header, `RawHeaderInts[2]` and `RawHeaderInts[3]` are the most promising fields
- otherwise the binding may live in a structure referenced by `RawHeaderInts[2]`

## Current Direction

The next safe AOSharp dump should focus on:
- wider header field export around `RawHeaderInts[3]`
- safe pointer previews for multiple neighboring header fields, not just `RawHeaderInts[2]`
- keeping the geometry export path stable while collecting more raw header evidence

That means the current priority order is:
1. `RawHeaderInts[3]`
2. neighboring header fields (`RawHeaderInts[4]` through `RawHeaderInts[7]`)
3. only then any new pointer-following guesses

## Latest Narrowing

From the later dedicated `Header3` / `Header7` dump and the follow-up report:
- [AOSharp_Subway_Header3_Header7_Analysis.md](C:/Other/codeprojects/ProjectMayhem/AO.Unity/Docs/AOSharp_Subway_Header3_Header7_Analysis.md)

Current read:
- `Header2` is still not the material answer.
- `Header3` and `Header7` do expose more interesting data than `Header2`.
- But not every readable pointer is useful:
  - some previews are clearly code-like / engine memory
  - some are all-zero
  - the most promising ones are `table-like`, `mixed-binary`, and some `string-like`

Best target subset now:
- large architectural room meshes only
- especially rooms like:
  - `Subway Station`
  - `Statue Ramp Connector`
  - `Helix Descent`
  - `Ticket Checkpoint`
  - `Grand Dome`

Working hypothesis:
- if real texture/material binding is still present in the exported room-surface path,
  the strongest remaining leads are `Header3 table-like` and `Header7 mixed-binary/string-like`
  on large architectural meshes.

## Bucket Pass

Offline classification of `PostVertexPreviewHex` produced these rough buckets:

| Bucket | Count | Notes |
|---|---:|---|
| `float-or-mixed` | 10880 | Most common. Likely contains mixed metadata, float blocks, and possibly references. |
| `typed-header` | 630 | Often includes a small header pattern like `.. .. 00 88/8C/8E/94`. |
| `string-bearing` | 346 | Contains readable ASCII substrings; likely mixed metadata/event/name blocks. |
| `mostly-zero` | 27 | Little or no useful immediate payload in the preview window. |

Important caution:
- `string-bearing` is only a heuristic bucket.
- Some entries classified as string-bearing may just contain accidental printable bytes.
- Even so, several entries clearly contain meaningful names.

## Strong Clues

The post-vertex payload is not a simple `uv[vertexCount]` array.

Reasons:
- Many payloads contain embedded ASCII names.
- Several payloads begin with small structured headers instead of UV-like float pairs.
- Some payloads look like compact index data.
- Some payloads look like floats or transform-ish data.

This strongly suggests the bytes after the vertex block are a mixed sub-record structure, not a single flat material block.

## Example String-Bearing Payloads

Observed readable strings in payloads:
- `swish_punch`
- `head_athrox16`
- `backpack`
- `attack`
- `attack_start_1`
- `evil123_nano`

Representative examples:

### Example A
- Room: `Ladies' Room`
- `VertexCount = 14`
- `TriangleIndexCount = 72`
- Prefix: `83 8E 4C 7C 00 1D 00 88`
- ASCII:

```text
..L|....swish_punch.........8Zb.v+1:...:s....@.Dstep.af_to_catan
```

### Example B
- Room: `Ladies' Room`
- `VertexCount = 12`
- `TriangleIndexCount = 60`
- Prefix: `6F EE 87 BF 5E 94 74 7E`
- ASCII:

```text
o...^.t~"......r....head_athrox16.T........... ............?...?
```

### Example C
- Room: `Ladies' Room`
- `VertexCount = 12`
- `TriangleIndexCount = 60`
- Prefix: `00 00 00 00 00 97 8A 7E`
- ASCII:

```text
.......~:......r....backpack..X=...>.........h.>...........?...?
```

### Example D
- Room: `Ladies' Room`
- `VertexCount = 14`
- `TriangleIndexCount = 72`
- Prefix: `27 72 EF 7F 00 01 00 88`
- ASCII:

```text
'r......attack...............................@.Dattack..........
```

## Typed-Header Pattern

Many meshes show what looks like a small typed sub-header:

- `00 0E 00 88`
- `00 03 00 8C`
- `00 46 00 8E`
- `00 12 00 94`

These values appear near the start of the post-vertex block, often after 4-8 bytes that do not yet have a known meaning.

Examples:

### Example E
- Room: `Ladies' Room`
- `VertexCount = 10`
- `TriangleIndexCount = 36`
- Prefix: `00 00 00 00 1E 8B 8D 7F`
- ASCII:

```text
................................................\..B.5.B{4~C\..B
```

### Example F
- Room: `Ladies' Room`
- `VertexCount = 8`
- `TriangleIndexCount = 36`
- Prefix: `00 00 00 00 2F DC CB 7E`
- ASCII:

```text
..../..~..............fC..:C.5.B..fC{4<C.5.B..cC..:C.5.BR.eC{4<C
```

Interpretation:
- the `00 ?? 00 88/8C/8E/94` shape looks more like a sub-record marker than UV data
- the bytes after that marker sometimes resemble small indices or float-like values

## Float-Or-Mixed Pattern

This is the dominant bucket.

Examples often include:
- opaque first 8 bytes
- then what looks like a typed marker
- then mixed floats, ids, or packed values

Representative examples:

### Example G
- Room: `Ladies' Room`
- `VertexCount = 20`
- `TriangleIndexCount = 102`
- Prefix: `42 68 5B 07 65 5D 3C 00`
- ASCII:

```text
Bh[.e]<.................@jR.J]<.h...0{............km.....6......
```

### Example H
- Room: `Ladies' Room`
- `VertexCount = 12`
- `TriangleIndexCount = 60`
- Prefix: `14 56 F5 BF AC 97 66 7F`
- First float view:
- `-1.9167, 3.0651e+38, 0, 8.3056e+30, 0, 0, -1.8565, 0.0225`

This does not look like clean UVs.

## Mostly-Zero Pattern

Only a small number of meshes fall into this bucket.

Interpretation:
- could be meshes with no additional material payload in the preview region
- could be a payload further away than the current preview window
- could be uninitialized or sparse blocks

## Working Hypotheses

Most likely:
1. Geometry read position is correct.
2. Material/texture data does live after the vertex block.
3. The post-vertex region is not one single schema.
4. Some of the post-vertex payload may be:
   - event/audio/effect names
   - animation/state names
   - compact index lists
   - float parameters
   - references into a second structure

Also plausible:
- texture/material ids are not stored directly in the immediate payload
- they may live in another pointed-to structure referenced by the header/prefix bytes

Stronger after header analysis:
- the best single lead is now:
  - `RawHeaderInts[2]` as a likely pointer
  - followed by a referenced block that may contain material or texture binding

Less likely now:
- simple `Vector2[vertexCount]` UVs immediately after vertices
- simple `Vector3[vertexCount]` normals immediately after vertices

## Room Notes

Heaviest `float-or-mixed` rooms in the first pass:
- `Subway Station`
- `Grand Dome`
- `Abmouth Showdown`
- `Slum Cathedral`
- `Entrance Stairs`
- `Shopping Arcade`

These rooms are probably good next targets for deeper manual decode, especially large wall/floor meshes that are more likely to carry meaningful material information.

## Recommended Next Steps

1. Keep live AOSharp export on the stable geometry-plus-diagnostics path.
2. Do not reintroduce heuristic UV/normal parsing in-process yet.
3. Expand offline analysis on a curated sample:
   - large floor/wall meshes
   - repeated room archetypes
   - meshes from `Grand Dome`, `Subway Station`, `Shopping Arcade`
4. Compare:
   - first 16-32 bytes after vertices as `uint16`, `uint32`, `float`
   - whether typed-header values (`88/8C/8E/94`) correlate to mesh category
   - whether string-bearing payloads correspond to props/effects instead of architecture
5. Investigate whether the first 8 bytes in the post-vertex block may contain:
   - pointer-like values
   - resource ids
   - hash values
6. Inspect the original `SurfaceMeshData` unmanaged layout for additional fields beyond:
   - triangle count
   - vertex count
   - triangle array pointer
7. Add a safe AOSharp diagnostic that follows `RawHeaderInts[2]` as a candidate pointer only when:
   - the address is readable
   - the block can be copied safely
   - the result is exported as raw bytes first, not interpreted live

## Practical Takeaway

We now have enough evidence to say:
- the current AOSharp exporter is stable again
- room geometry is correct
- the material/texture problem is real
- the relevant bytes are very likely near the vertex tail
- but the format is mixed enough that it should be decoded offline first, then implemented carefully

This note should be updated as soon as a repeatable typed-header or resource-id pattern is confirmed.

