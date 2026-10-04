#!/usr/bin/env bash
set -euo pipefail
subway_install_root=/opt/ao-rebirth/zoneengine-new
subway_publish=/home/cody/Coding/AORebirth/LinuxBuild/artifacts/zoneengine-new/linux-x64/self-contained
subway_release="$subway_install_root/releases/local-pf127-rooms-$(date -u +%Y%m%dT%H%M%SZ)"
[[ -x "$subway_publish/ZoneEngine_New" ]]
[[ -s "$subway_publish/NativeCopies/pf127.collision" ]]
[[ -s "$subway_publish/NativeCopies/pf127.json" ]]
[[ -s "$subway_publish/NativeCopies/pf127.rooms.json" ]]
[[ -s "$subway_publish/NativeCopies/pf127.rooms.collision" ]]
[[ -s "$subway_publish/NativeCopies/sources.json" ]]
python3 - "$subway_publish/NativeCopies" <<'PYREGISTRY'
from pathlib import Path
import hashlib, json, sys
root = Path(sys.argv[1])
registry = json.loads((root / 'sources.json').read_text())
assert registry['Version'] == 1 and registry['Sources']
assert registry['DefaultSource'] in {source['Id'] for source in registry['Sources']}
for source in registry['Sources']:
    folder = root / 'RoomCatalogs' / source['Id'] / source['ActiveRevision']
    publication = json.loads((folder / 'publication.json').read_text())
    assert publication['SourceId'] == source['Id'] and publication['SourcePlayfield'] == source['Playfield']
    assert hashlib.sha256((folder / 'catalog.json').read_bytes()).hexdigest() == source['ActiveRevision'] == publication['CatalogSha256']
    assert hashlib.sha256((folder / 'collision.bin').read_bytes()).hexdigest() == publication['CollisionSha256']
PYREGISTRY
[[ -s "$subway_publish/NativeCopies/native-client-playfields.json" ]]
subway_acg_style=$(python3 - "$subway_publish/NativeCopies/native-client-playfields.json" <<'PY'
import json, sys
with open(sys.argv[1]) as source:
    style = json.load(source).get('AcgStyle')
if type(style) is not int or style <= 0:
    raise SystemExit('Missing native ACG test style')
print(style)
PY
)
for subway_acg_file in metadata.json Rooms.json Surfaces.dat GNDA.png DCGA.png; do
    [[ -s "$subway_publish/GameData/Playfields/$subway_acg_style/$subway_acg_file" ]]
done
subway_previous=$(sudo readlink -f "$subway_install_root/current")
sudo install -d -o root -g aorebirth -m 0750 "$subway_release"
sudo cp -a "$subway_publish/." "$subway_release/"
sudo cp -a "$subway_previous/Config.xml" "$subway_release/Config.xml"
sudo chown -R root:aorebirth "$subway_release"
sudo find "$subway_release" -type d -exec chmod 0750 {} +
sudo find "$subway_release" -type f -exec chmod 0640 {} +
sudo chmod 0750 "$subway_release/ZoneEngine_New"
printf '%s\n' 'Local uncommitted source build; user authorized direct local changes, no cloud/Git push.' 'Native room generator 1.2.0: registered PF127/PF1931 sources, matched client/server catalog publication, retained recipe revisions, supported spawn/doorway validation and authoring source selection. Original-client extension work remains out of scope. Unity live acceptance of PF1931 pending.' | sudo tee "$subway_release/LOCAL_CHANGE.txt" >/dev/null
sudo chmod 0640 "$subway_release/LOCAL_CHANGE.txt"
sudo chown root:aorebirth "$subway_release/LOCAL_CHANGE.txt"
sudo -u aorebirth env AO_REBIRTH_CONFIG_PATH="$subway_release/Config.xml" "$subway_release/ZoneEngine_New" --validate-startup
sudo systemctl stop ao-rebirth-zoneengine-new.service
sudo ln -sfn "$subway_release" "$subway_install_root/current"
if ! sudo systemctl start ao-rebirth-zoneengine-new.service; then
    sudo ln -sfn "$subway_previous" "$subway_install_root/current"
    sudo systemctl start ao-rebirth-zoneengine-new.service
    printf '%s\n' 'FAILED: restored previous local release' >&2
    exit 1
fi
sleep 3
if ! systemctl is-active --quiet ao-rebirth-zoneengine-new.service; then
    sudo systemctl stop ao-rebirth-zoneengine-new.service
    sudo ln -sfn "$subway_previous" "$subway_install_root/current"
    sudo systemctl start ao-rebirth-zoneengine-new.service
    printf '%s\n' 'FAILED: new release exited; restored previous local release' >&2
    exit 1
fi
printf 'release=%s\nprevious=%s\n' "$subway_release" "$subway_previous" > /tmp/pf127-rooms-deploy-result.txt
printf 'PASS: local ZoneEngine_New active: %s\n' "$subway_release"
