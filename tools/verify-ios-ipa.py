"""Validate unsigned device IPA structure and Mach-O platform without executing it."""
import hashlib
import json
import plistlib
from pathlib import Path
import struct
import sys
import zipfile

path = Path(sys.argv[1])
with zipfile.ZipFile(path) as archive:
    prefix = 'Payload/DeskBoxMobile.app/'
    info = plistlib.loads(archive.read(prefix + 'Info.plist'))
    assert info['DTPlatformName'] == 'iphoneos', 'Not an iPhoneOS build'
    assert 'iPhoneOS' in info['CFBundleSupportedPlatforms'], 'Simulator platform rejected'
    binary = archive.read(prefix + info['CFBundleExecutable'])
    magic, cpu = struct.unpack_from('<II', binary)
    assert magic == 0xFEEDFACF and cpu == 0x0100000C, 'Expected arm64 Mach-O'
    count = struct.unpack_from('<I', binary, 16)[0]
    offset = 32
    platforms = []
    for _ in range(count):
        command, length = struct.unpack_from('<II', binary, offset)
        assert length >= 8 and offset + length <= len(binary), 'Malformed load command'
        if command == 0x32:
            platforms.append(struct.unpack_from('<I', binary, offset + 8)[0])
        offset += length
    assert 2 in platforms and 7 not in platforms, 'Expected iOS, not iOS Simulator'
    result = {'file': str(path), 'bundleId': info['CFBundleIdentifier'], 'minimumOS': info['MinimumOSVersion'], 'platform': 'iPhoneOS', 'architecture': 'arm64', 'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
print(json.dumps(result, ensure_ascii=True))
