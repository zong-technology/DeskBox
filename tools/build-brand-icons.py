"""Prepare the supplied Arthur.Zhong artwork for native app icon assets."""
import io
import json
import struct
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
source = Image.open(ROOT / "assets/brand-reference.png").convert("RGB")
# Fill only the exterior white corners; keep the enclosed white wordmark.
mask = source.point(lambda value: 255 if value > 100 else 0).convert("RGB")
pixels = mask.load()
for y in range(mask.height):
    for x in range(mask.width):
        pixels[x, y] = (255, 255, 255) if min(pixels[x, y]) == 255 else (0, 0, 0)
for corner in [(0, 0), (mask.width - 1, 0), (0, mask.height - 1), (mask.width - 1, mask.height - 1)]:
    ImageDraw.floodfill(mask, corner, (255, 0, 0))
alpha = Image.new("L", source.size)
alpha_pixels = alpha.load()
for y in range(mask.height):
    for x in range(mask.width):
        alpha_pixels[x, y] = 255 if pixels[x, y] == (255, 0, 0) else 0
alpha = alpha.filter(ImageFilter.MaxFilter(5))
source.paste(source.getpixel((source.width // 2, 12)), mask=alpha)
icon = source.resize((1024, 1024), Image.Resampling.LANCZOS)
icon.save(ROOT / "assets/deskbox.png")
mark = Image.new("RGB", (1024, 1024), icon.getpixel((512, 12)))
symbol = icon.crop((145, 160, 860, 720)).resize((860, 674), Image.Resampling.LANCZOS)
mark.paste(symbol, (82, 175))
sizes = [16, 24, 32, 48, 64, 128, 256]
images = []
for size in sizes:
    buffer = io.BytesIO()
    (mark if size <= 64 else icon).resize((size, size), Image.Resampling.LANCZOS).save(buffer, format="PNG")
    images.append(buffer.getvalue())
offset = 6 + 16 * len(sizes)
directory = bytearray(struct.pack("<HHH", 0, 1, len(sizes)))
for size, data in zip(sizes, images):
    directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
(ROOT / "assets/deskbox.ico").write_bytes(directory + b"".join(images))
assets = ROOT / "mobile/DeskBoxMobile/DeskBoxMobile/Assets.xcassets"
appicon = assets / "AppIcon.appiconset"
icon.save(appicon / "AppIcon.png")
(appicon / "Contents.json").write_text(json.dumps({"images": [{"filename": "AppIcon.png", "idiom": "universal", "platform": "ios", "size": "1024x1024"}], "info": {"author": "xcode", "version": 1}}, indent=2) + "\n", encoding="utf-8")
brand = assets / "BrandMark.imageset"
brand.mkdir(exist_ok=True)
mark.resize((192, 192), Image.Resampling.LANCZOS).save(brand / "BrandMark.png")
(brand / "Contents.json").write_text(json.dumps({"images": [{"filename": "BrandMark.png", "idiom": "universal"}], "info": {"author": "xcode", "version": 1}}, indent=2) + "\n", encoding="utf-8")
print("Prepared opaque iOS 1024px icon, Windows 7-size ICO and in-app brand mark")
