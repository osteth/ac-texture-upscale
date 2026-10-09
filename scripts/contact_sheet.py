"""Tile a folder of PNGs into one labelled contact sheet over a checkerboard (so alpha is visible)."""
import sys, os
from PIL import Image, ImageDraw

src, out = sys.argv[1], sys.argv[2]
cell, cols = 160, 8
files = sorted(f for f in os.listdir(src) if f.endswith('.png'))
rows = (len(files) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cell, rows * (cell + 14)), (40, 40, 40))
check = Image.new('RGB', (cell, cell))
d = ImageDraw.Draw(check)
for y in range(0, cell, 16):
    for x in range(0, cell, 16):
        d.rectangle([x, y, x + 15, y + 15], fill=(90, 90, 90) if (x + y) // 16 % 2 else (140, 140, 140))
draw = ImageDraw.Draw(sheet)
for i, f in enumerate(files):
    im = Image.open(os.path.join(src, f)).convert('RGBA')
    im.thumbnail((cell, cell))
    x, y = i % cols * cell, i // cols * (cell + 14)
    tile = check.copy()
    tile.paste(im, ((cell - im.width) // 2, (cell - im.height) // 2), im)
    sheet.paste(tile, (x, y))
    draw.text((x + 2, y + cell + 1), f.replace('.png', '')[-24:], fill=(230, 230, 230))
sheet.save(out)
