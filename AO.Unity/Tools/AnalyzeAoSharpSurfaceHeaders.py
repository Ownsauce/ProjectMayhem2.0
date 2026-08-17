import argparse
import json
import re
from collections import Counter
from pathlib import Path


def classify_preview(hex_string: str) -> str:
    if not hex_string:
        return "none"

    try:
        data = bytes(int(part, 16) for part in hex_string.split())
    except ValueError:
        return "invalid"

    if not data:
        return "none"

    if all(b == 0 for b in data):
        return "all-zero"

    non_zero = sum(1 for b in data if b != 0)
    printable = "".join(chr(b) if 32 <= b < 127 else "." for b in data)

    if re.search(r"[A-Za-z]{4,}", printable):
        return "string-like"

    if data[:2] in (b"\x55\x8B", b"\x8B\xEC") or (b"\xCC" in data[:16] and data[:1] in (b"\x56", b"\x57", b"\x53")):
        return "code-like"

    if non_zero < len(data) * 0.15:
        return "sparse"

    table_markers = sum(1 for i in range(3, len(data), 4) if data[i] in (0xFE, 0xFF, 0x00))
    if len(data) >= 16 and table_markers > (len(data) // 8):
        return "table-like"

    return "mixed-binary"


def iter_meshes(dump: dict):
    for room in dump.get("Rooms", []):
        room_name = room.get("Name") or "Unknown"
        for mesh in room.get("SurfaceMeshes") or []:
            yield room_name, mesh, mesh.get("SurfaceMaterial") or {}


def build_report(dump: dict) -> str:
    h3_counts = Counter()
    h7_counts = Counter()
    architectural_rooms = Counter()
    architectural_samples = []

    for room_name, mesh, surface in iter_meshes(dump):
        vertex_count = mesh.get("VertexCount") or 0
        triangle_index_count = mesh.get("TriangleIndexCount") or 0

        h3_bucket = classify_preview(surface.get("Header3PointerPreviewHex")) if surface.get("Header3PointerReadable") else "unreadable"
        h7_bucket = classify_preview(surface.get("Header7PointerPreviewHex")) if surface.get("Header7PointerReadable") else "unreadable"

        h3_counts[h3_bucket] += 1
        h7_counts[h7_bucket] += 1

        if vertex_count >= 20 or triangle_index_count >= 90:
            architectural_rooms[room_name] += 1
            architectural_samples.append(
                {
                    "room": room_name,
                    "vertex_count": vertex_count,
                    "triangle_index_count": triangle_index_count,
                    "h3_bucket": h3_bucket,
                    "h7_bucket": h7_bucket,
                    "h3_pointer": surface.get("Header3PointerHex"),
                    "h7_pointer": surface.get("Header7PointerHex"),
                }
            )

    lines = []
    lines.append("# AOSharp Subway Header3/Header7 Analysis")
    lines.append("")
    lines.append("## Bucket Counts")
    lines.append("")
    lines.append("### Header3")
    for bucket, count in h3_counts.most_common():
        lines.append(f"- `{bucket}`: {count}")
    lines.append("")
    lines.append("### Header7")
    for bucket, count in h7_counts.most_common():
        lines.append(f"- `{bucket}`: {count}")
    lines.append("")
    lines.append("## Architectural Rooms With Large Surface Meshes")
    for room_name, count in architectural_rooms.most_common(16):
        lines.append(f"- `{room_name}`: {count}")
    lines.append("")
    lines.append("## Sample Architectural Meshes")
    for sample in architectural_samples[:24]:
        lines.append(
            f"- `{sample['room']}` vc={sample['vertex_count']} ti={sample['triangle_index_count']} "
            f"h3={sample['h3_bucket']} ({sample['h3_pointer']}) "
            f"h7={sample['h7_bucket']} ({sample['h7_pointer']})"
        )

    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("dump_path", type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    dump = json.loads(args.dump_path.read_text())
    report = build_report(dump)

    if args.out:
        args.out.write_text(report)
    else:
        print(report)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
