#!/usr/bin/env bash
set -euo pipefail
if (( $# < 1 || $# > 4 )); then
  echo 'Usage: tools/export-native-pf-visuals.sh <AO-install> [PF-id=127] [output-directory] [room-index]' >&2
  exit 2
fi
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
install_path="$1"
playfield_id="${2:-127}"
output_dir="${3:-$repo_root/exports/native/pf$playfield_id}"
helper_dir="$(mktemp -d /tmp/pm-native-visual.XXXXXX)"
trap 'rm -rf -- "$helper_dir"' EXIT
i686-w64-mingw32-g++ -O2 -static "$repo_root/tools/AOIndoorVisualExtractor/AOIndoorVisualExtractor.cpp" -ladvapi32 -o "$helper_dir/AOIndoorVisualExtractor.exe"
export_args=("$install_path" "$playfield_id" "$output_dir" "$helper_dir/AOIndoorVisualExtractor.exe")
if (( $# == 4 )); then export_args+=("$4"); fi
dotnet run --project "$repo_root/tools/WorldGen.NativeVisualExport" -- "${export_args[@]}"
python3 "$repo_root/tools/WorldGen.NativeVisualExport/verify-package.py" "$output_dir"
