"""Side-by-side crops: original (nearest 2x) | bicubic 2x | animevideov3 2x | x4plus downsampled to 2x.
Last row tiles each terrain result 2x2 to expose edge seams."""
import sys, os
from PIL import Image, ImageDraw

w = sys.argv[1]
picks = ['portal_CUSTOM_LSCAPE_R8G8B8_06003794', 'portal_INDEX16_06005739', 'portal_R8G8B8_0600647C',
         'highres_DXT1_060044D7', 'portal_DXT5_06005DDE', 'highres_INDEX16_06003797', 'portal_A8R8G8B8_06004D5C']
C = 256  # output crop size (= 128 source pixels at 2x)
cols = ['original (nearest)', 'bicubic', 'animevideov3 x2', 'x4plus -> 2x']
sheet = Image.new('RGB', (C * 4, (C + 4) * (len(picks) + 1) + 18), (30, 30, 30))
d = ImageDraw.Draw(sheet)
for i, c in enumerate(cols):
    d.text((i * C + 4, 3), c, fill=(255, 255, 255))

def crop_center(im, size):
    x, y = (im.width - size) // 2, (im.height - size) // 2
    return im.crop((max(x, 0), max(y, 0), max(x, 0) + size, max(y, 0) + size))

def bg(im):
    b = Image.new('RGBA', im.size, (110, 110, 110, 255)); b.alpha_composite(im); return b.convert('RGB')

for r, p in enumerate(picks):
    src = Image.open(f'{w}/sample/{p}.png').convert('RGBA')
    big = (src.width * 2, src.height * 2)
    versions = [src.resize(big, Image.NEAREST), src.resize(big, Image.BICUBIC),
                Image.open(f'{w}/up_anime2/{p}.png').convert('RGBA'),
                Image.open(f'{w}/up_x4plus/{p}.png').convert('RGBA').resize(big, Image.LANCZOS)]
    for c, v in enumerate(versions):
        sheet.paste(bg(crop_center(v, C)), (c * C, 18 + r * (C + 4)))
    d.text((4, 18 + r * (C + 4) + 2), p.split('_', 1)[1], fill=(255, 255, 0))

# seam check: tile terrain 2x2 then crop around the shared corner
p = picks[0]; src = Image.open(f'{w}/sample/{p}.png').convert('RGB')
for c, v in enumerate([src.resize((512, 512), Image.NEAREST), src.resize((512, 512), Image.BICUBIC),
                       Image.open(f'{w}/up_anime2/{p}.png').convert('RGB'),
                       Image.open(f'{w}/up_x4plus/{p}.png').convert('RGB').resize((512, 512), Image.LANCZOS)]):
    t = Image.new('RGB', (1024, 1024))
    for x in (0, 512):
        for y in (0, 512): t.paste(v, (x, y))
    sheet.paste(t.crop((512 - C // 2, 512 - C // 2, 512 + C // 2, 512 + C // 2)), (c * C, 18 + len(picks) * (C + 4)))
d.text((4, 18 + len(picks) * (C + 4) + 2), 'SEAM CHECK: terrain tiled 2x2, centered on the tile corner', fill=(255, 80, 80))
sheet.save(sys.argv[2])
