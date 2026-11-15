import sys
from pathlib import Path
import pymupdf

target = Path(sys.argv[1])
source = pymupdf.open()
page = source.new_page()
page.insert_text((55, 90), '设备验证 测试编号42', fontname='china-s', fontsize=22)
page.insert_text((55, 145), 'ALARM CHECK 42', fontsize=22)
pixels = page.get_pixmap(matrix=pymupdf.Matrix(2, 2))
scanned = pymupdf.open()
scanned.new_page().insert_image(page.rect, stream=pixels.tobytes('png'))
scanned.save(target)
scanned.close()
source.close()
with pymupdf.open(target) as check:
    assert not check[0].get_text().strip(), 'Fixture must contain raster pixels only'
