#!/usr/bin/env python3
"""Validate the portable visual package without launching Unity or the AO client."""
import hashlib, json, math, pathlib, struct, sys

def require(condition, message):
    if not condition:
        raise ValueError(message)

def verify(folder):
    folder = pathlib.Path(folder)
    manifests = list(folder.glob('pf*.visual.json'))
    require(len(manifests) == 1, 'Expected exactly one visual manifest')
    manifest = json.loads(manifests[0].read_text())
    require(manifest['SchemaVersion'] == 1, 'Unsupported manifest')
    def checked_file(relative, digest):
        path = (folder / relative).resolve()
        require(path.is_relative_to(folder.resolve()), 'Path escapes package')
        data = path.read_bytes()
        require(hashlib.sha256(data).hexdigest() == digest, f'Hash mismatch: {relative}')
        return data
    geometry = checked_file(manifest['GeometryFile'], manifest['GeometrySha256'])
    require(geometry[:4] == b'AOVR', 'Invalid visual header')
    version, pf, tilemap, count = struct.unpack_from('<4i', geometry, 4)
    require((version, pf, tilemap, count) == (1, manifest['SourcePlayfield'], manifest['TilemapId'], manifest['RoomCount']), 'Visual identity mismatch')
    offset = 20
    total_triangles = 0
    support_at_spawn = False
    for report in manifest['Rooms']:
        room_id, cells = struct.unpack_from('<2i', geometry, offset); offset += 8
        require(room_id == report['Index'] and cells == report['CellCount'], 'Room metadata mismatch')
        room_triangles = 0
        for _ in range(cells):
            x, y, tile_id, rotation, vertices = struct.unpack_from('<5i', geometry, offset); offset += 20
            require(0 < tile_id < 16384 and 0 <= rotation <= 3 and 0 < vertices <= 32768, 'Invalid visual tile')
            source = [struct.unpack_from('<6fI2f', geometry, offset + i * 36) for i in range(vertices)]
            require(all(math.isfinite(v) for vertex in source for v in vertex[:6] + vertex[7:]), 'Nonfinite source attribute')
            offset += vertices * 36
            groups, = struct.unpack_from('<i', geometry, offset); offset += 4
            for _ in range(groups):
                material, indices = struct.unpack_from('<Ii', geometry, offset); offset += 8
                require(indices % 3 == 0 and 0 <= indices <= 196608, 'Invalid triangle buffer')
                tris = struct.unpack_from(f'<{indices}H', geometry, offset); offset += indices * 2
                require(all(i < vertices for i in tris), 'Visual index outside vertex buffer')
                room_triangles += indices // 3
                if pf == 127 and room_id == 2:
                    for i in range(0, indices, 3):
                        a, b, c = [source[tris[i + k]] for k in range(3)]
                        if min(a[4], b[4], c[4]) < .7:
                            continue
                        def edge(p, q):
                            return (q[0] - p[0]) * (252 - p[2]) - (q[2] - p[2]) * (184 - p[0])
                        sides = [edge(a, b), edge(b, c), edge(c, a)]
                        if (min(sides) >= -1e-4 or max(sides) <= 1e-4) and all(abs(v[1] - 107.60483) < .01 for v in (a, b, c)):
                            support_at_spawn = True
        require(room_triangles == report['TriangleCount'], 'Room triangle count mismatch')
        total_triangles += room_triangles
        glb = (folder / report['Glb']).read_bytes()
        magic, version, size, json_size, kind = struct.unpack_from('<5I', glb)
        require((magic, version, size, kind) == (0x46546c67, 2, len(glb), 0x4e4f534a), 'Invalid GLB envelope')
        doc = json.loads(glb[20:20 + json_size])
        primitives = doc['meshes'][0]['primitives']
        require(sum(doc['accessors'][p['indices']]['count'] // 3 for p in primitives) == room_triangles, 'GLB lost source triangles')
        for primitive in primitives:
            attributes = primitive['attributes']
            require('TEXCOORD_0' in attributes and 'NORMAL' in attributes, 'GLB lost original UVs or normals')
            require(len({doc['accessors'][attributes[k]]['count'] for k in ('POSITION', 'NORMAL', 'TEXCOORD_0')}) == 1, 'GLB attribute counts differ')
        require(len(doc['images']) == len(doc['materials']), 'GLB material unresolved')
    require(geometry[offset:offset + 4] == b'AOVD', 'Missing source dependencies'); offset += 4
    dependencies, = struct.unpack_from('<i', geometry, offset); offset += 4
    ids = struct.unpack_from(f'<{dependencies}i', geometry, offset); offset += dependencies * 4
    require(set(ids) == {r['ResourceId'] for r in manifest['Dependencies']}, 'Visual dependency manifest mismatch')
    require(offset == len(geometry), 'Trailing or truncated geometry')
    for texture in manifest['Textures']:
        image = checked_file(texture['Path'], texture['ImageSha256'])
        require(image[:8] == b'\x89PNG\r\n\x1a\n', 'Invalid texture image')
        require(struct.unpack_from('>2I', image, 16) == (texture['Width'], texture['Height']), 'Texture dimensions mismatch')
    if pf == 127 and manifest['CompleteRoomSet']:
        require(count == manifest['SourceRoomCount'] == 46, 'Incomplete PF127')
        require(support_at_spawn, 'Textured floor does not align with accepted PF127 spawn')
    print(f"Validated PF {pf}: {count} rooms, {total_triangles} triangles, {len(manifest['Textures'])} textures, {dependencies} visual dependencies; spawn floor alignment={support_at_spawn}")

if __name__ == '__main__':
    verify(sys.argv[1])
