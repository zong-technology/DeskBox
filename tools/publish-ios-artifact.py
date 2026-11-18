"""Publish checked iPhone artifacts, or resume an upload without rebuilding them."""
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import struct
import subprocess
import zipfile


def gh(*args):
    return subprocess.check_output(["gh", *args], text=True)


repo = os.environ["GITHUB_REPOSITORY"]
run_id = os.environ["SOURCE_RUN_ID"]
assert re.fullmatch(r"[0-9]+", run_id), "Invalid source run ID"
api = f"repos/{repo}"
run = json.loads(gh("api", f"{api}/actions/runs/{run_id}"))
assert run["path"] == ".github/workflows/ios-unsigned-ipa.yml"
assert run["event"] == "workflow_dispatch" and run["head_repository"]["full_name"] == repo
jobs = json.loads(gh("api", f"{api}/actions/runs/{run_id}/jobs?per_page=100"))["jobs"]
for name in ["unsigned-ipa", "audio-regression"]:
    assert any(j["name"] == name and j["conclusion"] == "success" for j in jobs), f"{name} has not passed"

packages = list(Path("release").glob("DeskBoxMobile-*-unsigned.ipa"))
assert len(packages) == 1, "Expected one iPhone package"
package = packages[0]
with zipfile.ZipFile(package) as archive:
    info = plistlib.loads(archive.read("Payload/DeskBoxMobile.app/Info.plist"))
    binary = archive.read("Payload/DeskBoxMobile.app/DeskBoxMobile")
version = info["CFBundleShortVersionString"]
assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version)
assert package.name == f"DeskBoxMobile-{version}-unsigned.ipa"
assert info["CFBundleIdentifier"] == "com.deskbox.mobile"
assert info["CFBundleSupportedPlatforms"] == ["iPhoneOS"]
assert binary[:4] == bytes.fromhex("cffaedfe") and struct.unpack("<I", binary[4:8])[0] == 0x100000C
checksums = Path("release/SHA256SUMS.txt")
expected, filename = checksums.read_text().strip().split()
assert filename == package.name and hashlib.sha256(package.read_bytes()).hexdigest() == expected

tag = f"v{version}-mobile-hologram"
existing = json.loads(gh("api", f"{api}/releases?per_page=100"))
release = next((r for r in existing if r["tag_name"] == tag), None)
files = [package, checksums, Path("review/main-hologram.png"), Path("review/small-hologram.png")]
digests = {p.name: "sha256:" + hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
if release and release.get("immutable"):
    assets = {a["name"]: a for a in release["assets"]}
    if any(name not in assets for name in digests):
        # An already published immutable release cannot accept missing assets.
        # Keep it intact and publish this exact verified build under a new tag.
        build = info["CFBundleVersion"]
        assert re.fullmatch(r"[0-9]+", build)
        tag += f"-build{build}"
        release = next((r for r in existing if r["tag_name"] == tag), None)
if release:
    # A previous failed upload may already have created the release. Never retag it.
    refs = json.loads(gh("api", f"{api}/git/matching-refs/tags/{tag}"))
    if any(r["ref"] == f"refs/tags/{tag}" for r in refs):
        commit = json.loads(gh("api", f"{api}/commits/{tag}"))
        assert commit["sha"] == run["head_sha"], "Release tag belongs to another source commit"
    else:
        # GitHub may defer creating a draft's tag until it is published.
        assert release["draft"] and release["target_commitish"] == run["head_sha"], "Draft belongs to another source commit"
else:
    refs = json.loads(gh("api", f"{api}/git/matching-refs/tags/{tag}"))
    if any(r["ref"] == f"refs/tags/{tag}" for r in refs):
        commit = json.loads(gh("api", f"{api}/commits/{tag}"))
        assert commit["sha"] == run["head_sha"], "Existing tag belongs to another source commit"
        target = []
    else:
        target = ["-f", f"target_commitish={run['head_sha']}"]
    # Creation returns the draft's ID immediately. The releases list can still
    # contain an older cached response, and by-tag lookups omit draft releases.
    release = json.loads(gh("api", f"{api}/releases", "--method", "POST",
                            "-f", f"tag_name={tag}", *target,
                            "-F", "draft=true", "-F", "prerelease=true",
                            "-f", f"name=DeskBox Mobile {version} - holographic voice preview",
                            "-f", "body=" + Path("release/RELEASE-NOTES.md").read_text()))

assets = {a["name"]: a for a in release["assets"]}
for path in files:
    digest = digests[path.name]
    if path.name in assets:
        assert assets[path.name].get("digest") == digest, f"Existing asset differs: {path.name}"
    else:
        gh("release", "upload", tag, str(path), "--repo", repo)
verified = json.loads(gh("api", f"{api}/releases/{release['id']}"))
assets = {a["name"]: a for a in verified["assets"]}
for path in files:
    assert assets[path.name]["digest"] == digests[path.name]
if verified["draft"]:
    gh("release", "edit", tag, "--repo", repo, "--draft=false")
print(f"PASS published {tag} from {run['head_sha']}; all four asset hashes match")
