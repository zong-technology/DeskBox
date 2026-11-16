#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:?Output directory required}"
mkdir -p "$OUT"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/deskbox-simulator.XXXXXX")"
xcodebuild -project "$ROOT/mobile/DeskBoxMobile/DeskBoxMobile.xcodeproj" \
  -scheme DeskBoxMobile -configuration Debug -sdk iphonesimulator \
  -destination 'generic/platform=iOS Simulator' -derivedDataPath "$WORK/derived" \
  CODE_SIGNING_ALLOWED=NO build > "$OUT/simulator-build.log" 2>&1 || { tail -100 "$OUT/simulator-build.log"; exit 1; }
APP="$WORK/derived/Build/Products/Debug-iphonesimulator/DeskBoxMobile.app"
xcrun simctl list runtimes --json > "$WORK/runtimes.json"
xcrun simctl list devicetypes --json > "$WORK/types.json"
python3 - "$WORK" <<'PY'
import json,sys,pathlib
p=pathlib.Path(sys.argv[1]);r=json.loads((p/'runtimes.json').read_text())['runtimes']
r=sorted((x for x in r if x.get('isAvailable') and '.iOS-' in x['identifier']),key=lambda x:tuple(map(int,x['version'].split('.'))))
assert r,'No iOS simulator runtime installed'
t=json.loads((p/'types.json').read_text())['devicetypes']
def device(names):
 for name in names:
  for x in t:
   if x['name']==name:return x['identifier']
 raise RuntimeError('Required iPhone device type unavailable')
(p/'runtime').write_text(r[-1]['identifier'])
(p/'main-type').write_text(device(['iPhone 17 Pro','iPhone 16 Pro','iPhone 15 Pro']))
(p/'small-type').write_text(device(['iPhone SE (3rd generation)','iPhone 16e','iPhone 16']))
PY
DEVICES=()
cleanup() { for device in "${DEVICES[@]}"; do xcrun simctl shutdown "$device" >/dev/null 2>&1 || true; xcrun simctl delete "$device" >/dev/null 2>&1 || true; done; }
trap cleanup EXIT
for label in main small; do
  device="$(xcrun simctl create "DeskBox-QA-$label" "$(cat "$WORK/$label-type")" "$(cat "$WORK/runtime")")"
  DEVICES+=("$device")
  xcrun simctl boot "$device"
  xcrun simctl bootstatus "$device" -b
  xcrun simctl status_bar "$device" override --time '9:41' --dataNetwork wifi --wifiMode active --wifiBars 3 --batteryState charged --batteryLevel 100
  xcrun simctl install "$device" "$APP"
  for route in hologram history settings; do
    xcrun simctl launch --terminate-running-process "$device" com.deskbox.mobile --hologram-preview "$route"
    container="$(xcrun simctl get_app_container "$device" com.deskbox.mobile data)"
    marker="$container/Documents/hologram-qa-$route.json"
    for attempt in {1..40}; do if [ -f "$marker" ]; then break; fi; sleep 0.5; done
    cp "$marker" "$OUT/$label-$route.json"
    xcrun simctl io "$device" screenshot "$OUT/$label-$route.png"
    python3 - "$OUT/$label-$route.json" "$OUT/$label-$route.png" "$route" <<'PY'
import sys,json,struct
m=json.load(open(sys.argv[1]));assert m['route']==sys.argv[3] and m['active'] and m['simulatedVoice']
with open(sys.argv[2],'rb') as f:b=f.read(24)
assert b[:8]==b'\x89PNG\r\n\x1a\n';w,h=struct.unpack('>II',b[16:24])
assert abs(w-m['width']*m['scale'])<2 and abs(h-m['height']*m['scale'])<2,(w,h,m)
print('PASS native simulator route',m['route'],w,h,'pixels; active window',m['width'],m['height'],'points')
PY
  done
  xcrun simctl shutdown "$device"
done
