#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
output="$repo_root/AO.Unity/Assets/StreamingAssets/Tools"
mkdir -p -- "$output"
build_dir="$(mktemp -d /tmp/pm-indoor-helpers.XXXXXX)"
trap 'rm -rf -- "$build_dir"' EXIT
# These binaries contain our reader code. AO DLLs/data are loaded only from the user's install.
i686-w64-mingw32-g++ -O2 -static "$repo_root/tools/AOIndoorVisualExtractor/AOIndoorVisualExtractor.cpp" -ladvapi32 -o "$build_dir/AOIndoorVisualExtractor.exe"
i686-w64-mingw32-g++ -O2 -static "$repo_root/tools/AOIndoorExtractor/AOIndoorExtractor.cpp" -o "$build_dir/AOIndoorExtractor.exe"
install -m 644 "$build_dir/AOIndoorVisualExtractor.exe" "$output/AOIndoorVisualExtractor.exe"
install -m 644 "$build_dir/AOIndoorExtractor.exe" "$output/AOIndoorExtractor.exe"
echo "Built local runtime readers in $output (no AO assets or DLLs included)."
