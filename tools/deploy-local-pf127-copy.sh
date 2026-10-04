#!/usr/bin/env bash
set -euo pipefail
subway_install_root=/opt/ao-rebirth/zoneengine-new
subway_publish=/home/cody/Coding/AORebirth/LinuxBuild/artifacts/zoneengine-new/linux-x64/self-contained
subway_previous=$(sudo readlink -f "$subway_install_root/current")
subway_release="$subway_install_root/releases/local-pf127-copy-$(date -u +%Y%m%dT%H%M%SZ)"
[[ -x "$subway_publish/ZoneEngine_New" ]]
[[ -s "$subway_publish/NativeCopies/pf127.collision" ]]
[[ -s "$subway_publish/NativeCopies/pf127.json" ]]
sudo install -d -o root -g aorebirth -m 0750 "$subway_release"
sudo cp -a "$subway_publish/." "$subway_release/"
sudo cp -a "$subway_previous/Config.xml" "$subway_release/Config.xml"
sudo chown -R root:aorebirth "$subway_release"
sudo find "$subway_release" -type d -exec chmod 0750 {} +
sudo find "$subway_release" -type f -exec chmod 0640 {} +
sudo chmod 0750 "$subway_release/ZoneEngine_New"
printf '%s\n' 'Local uncommitted source build; user authorized direct local changes, no cloud/Git push.' 'Native PF 127 seeded copy with original room surface + tile terrain collision; retain Subway entry fixes.' | sudo tee "$subway_release/LOCAL_CHANGE.txt" >/dev/null
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
printf 'release=%s\nprevious=%s\n' "$subway_release" "$subway_previous" > /tmp/pf127-copy-deploy-result.txt
printf 'PASS: local ZoneEngine_New active: %s\n' "$subway_release"
