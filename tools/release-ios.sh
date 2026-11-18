#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERIFY_UI=false
if [ "${1:-}" = '--verify-ui' ]; then VERIFY_UI=true; shift; fi
if [ "$#" -ne 0 ]; then echo 'Usage: bash tools/release-ios.sh [--verify-ui]' >&2; exit 2; fi
if [ "$(uname -s)" != Darwin ] || ! command -v xcodebuild >/dev/null || ! command -v swiftc >/dev/null; then
  echo 'iPhone builds require a Mac with full Xcode. This entry point does not use GitHub Actions.' >&2; exit 1
fi
cd "$ROOT"
VERSION="$(python3 - <<'PY'
import re,pathlib
s=pathlib.Path('mobile/DeskBoxMobile/DeskBoxMobile.xcodeproj/project.pbxproj').read_text()
versions=set(re.findall(r'MARKETING_VERSION = ([0-9.]+);',s));assert len(versions)==1
print(versions.pop())
PY
)"
OUT="$ROOT/dist/local-release/ios-$VERSION-$(date -u +%Y%m%d-%H%M%S)-$$"
mkdir -p "$OUT/review"
swiftc mobile/DeskBoxMobile/DeskBoxMobile/AIEnvironment.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramProtocol.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramPlayback.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramInputGate.swift tools/mobile-audio-check.swift -o "$OUT/audio-check"
"$OUT/audio-check" | tee "$OUT/review/audio-check.log"
grep -E '^PASS [0-9]+ native audio checks' "$OUT/review/audio-check.log"
swiftc mobile/DeskBoxMobile/DeskBoxMobile/Models.swift mobile/DeskBoxMobile/DeskBoxMobile/OfflineStore.swift mobile/DeskBoxMobile/DeskBoxMobile/BriefingRules.swift mobile/DeskBoxMobile/DeskBoxMobile/SyncEndpoint.swift mobile/DeskBoxMobile/DeskBoxMobile/CloudAccount.swift tools/mobile-briefing-check.swift -o "$OUT/offline-check"
"$OUT/offline-check" | tee "$OUT/review/offline-check.log"
grep -E 'PASS [0-9]+$' "$OUT/review/offline-check.log"
swiftc mobile/DeskBoxMobile/DeskBoxMobile/Models.swift mobile/DeskBoxMobile/DeskBoxMobile/OfflineStore.swift mobile/DeskBoxMobile/DeskBoxMobile/AIEnvironment.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramProtocol.swift mobile/DeskBoxMobile/DeskBoxMobile/MobileAIAttachment.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramHistory.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramLocalTools.swift mobile/DeskBoxMobile/DeskBoxMobile/AIToolPresentation.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramDiagnostics.swift tools/mobile-hologram-check.swift -o "$OUT/hologram-check"
"$OUT/hologram-check" | tee "$OUT/review/hologram-check.log"
grep -E '^PASS [0-9]+ mobile hologram checks' "$OUT/review/hologram-check.log"
swiftc mobile/DeskBoxMobile/DeskBoxMobile/AIEnvironment.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramProtocol.swift mobile/DeskBoxMobile/DeskBoxMobile/MobileAIAttachment.swift mobile/DeskBoxMobile/DeskBoxMobile/MobileAttachmentReader.swift tools/mobile-attachment-check.swift -o "$OUT/attachment-check"
swiftc mobile/DeskBoxMobile/DeskBoxMobile/Models.swift mobile/DeskBoxMobile/DeskBoxMobile/OfflineStore.swift mobile/DeskBoxMobile/DeskBoxMobile/AIEnvironment.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramProtocol.swift mobile/DeskBoxMobile/DeskBoxMobile/MobileAIAttachment.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramHistory.swift mobile/DeskBoxMobile/DeskBoxMobile/HologramLocalTools.swift mobile/DeskBoxMobile/DeskBoxMobile/AIToolPresentation.swift mobile/DeskBoxMobile/DeskBoxMobile/MobileDirectAI.swift tools/mobile-independent-ai-check.swift -o "$OUT/independent-ai-check"
"$OUT/independent-ai-check" | tee "$OUT/review/independent-ai-check.log"

"$OUT/attachment-check" | tee "$OUT/review/attachment-check.log"
rm "$OUT/audio-check" "$OUT/offline-check" "$OUT/hologram-check" "$OUT/attachment-check" "$OUT/independent-ai-check"
bash tools/build-ios-unsigned-ipa.sh "$OUT/DeskBoxMobile-$VERSION-unsigned.ipa" > "$OUT/review/device-build.log" 2>&1 || { tail -80 "$OUT/review/device-build.log"; exit 1; }
if "$VERIFY_UI"; then bash tools/ios-hologram-smoke.sh "$OUT/review"; fi
cp "docs/MOBILE-HOLOGRAM-$VERSION.md" "$OUT/RELEASE-NOTES.md"
python3 - "$OUT" "$VERSION" "$VERIFY_UI" <<'PY'
import sys,json,subprocess,pathlib,plistlib,zipfile,hashlib
p=pathlib.Path(sys.argv[1]);ipa=p/('DeskBoxMobile-'+sys.argv[2]+'-unsigned.ipa')
with zipfile.ZipFile(ipa) as z: info=plistlib.loads(z.read('Payload/DeskBoxMobile.app/Info.plist'))
assert info['CFBundleShortVersionString']==sys.argv[2] and info['DTPlatformName']=='iphoneos'
source=subprocess.run(['git','rev-parse','HEAD'],capture_output=True,text=True);dirty=subprocess.run(['git','status','--porcelain'],capture_output=True,text=True)
ui=sys.argv[3]=='true'
if ui:
 routes='home tasks task-editor projects incidents incident-editor ai ai-keyboard ai-large connection briefing conflicts hologram history settings settings-large home-large tasks-large projects-large incidents-large project-detail diagnostics ai-empty'.split()
 for label in ['main','small']:
  for route in routes:
   marker=json.loads((p/'review'/(label+'-'+route+'.json')).read_text())
   assert marker['active'] and marker['route']==route
   assert (p/'review'/(label+'-'+route+'.png')).stat().st_size>1000
manifest=dict(Product='DeskBoxMobile',Version=info['CFBundleShortVersionString'],Build=info['CFBundleVersion'],BundleID=info['CFBundleIdentifier'],SourceSHA=source.stdout.strip() if source.returncode==0 else '',Dirty=bool(dirty.stdout) if dirty.returncode==0 else None,Builder='local-macos',Signed=False,CoreAndAudioVerified=True,SimulatorUIVerified=ui,SHA256=hashlib.sha256(ipa.read_bytes()).hexdigest())
(p/'BUILD-MANIFEST.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
with (p/'SHA256SUMS.txt').open('w') as f:
 for item in sorted(p.iterdir()):
  if item.is_file() and item.name!='SHA256SUMS.txt':f.write(hashlib.sha256(item.read_bytes()).hexdigest()+'  '+item.name+'\n')
PY
if ! "$VERIFY_UI"; then echo 'Simulator UI not checked. Run again with --verify-ui for the complete UI matrix.'; fi
echo "Local iPhone package: $OUT"
echo 'Unsigned IPA: keep the original Apple team and Bundle ID when signing for device installation.'
