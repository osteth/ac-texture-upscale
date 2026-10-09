"""Prepare lossless WebP assets for the comparison viewer: original, fast 2x, x4plus downsampled to 2x."""
import sys, os, json
from PIL import Image

w, out = sys.argv[1], sys.argv[2]
os.makedirs(f'{out}/img', exist_ok=True)
rows = [l.rstrip('\n').split('\t') for l in open(f'{w}/sample/manifest.tsv')][1:]
items = []
for file, dat, tid, fmt, wd, ht, pal in rows:
    stem = file[:-4]
    src = Image.open(f'{w}/sample/{file}').convert('RGBA')
    size = (src.width * 2, src.height * 2)
    for key, im in (('orig', src),
                    ('fast', Image.open(f'{w}/up_anime2/{file}').convert('RGBA')),
                    ('x4', Image.open(f'{w}/up_x4plus/{file}').convert('RGBA').resize(size, Image.LANCZOS))):
        im.save(f'{out}/img/{stem}_{key}.webp', 'WEBP', lossless=True, quality=100, method=4)
    alpha = src.getextrema()[3][0] < 255
    items.append({'id': tid, 'stem': stem, 'dat': dat, 'fmt': fmt.replace('PFID_', '').replace('CUSTOM_LSCAPE_', 'LSCAPE_'),
                  'w': int(wd), 'h': int(ht), 'alpha': alpha})
json.dump(items, open(f'{out}/items.json', 'w'))
total = sum(os.path.getsize(f'{out}/img/{f}') for f in os.listdir(f'{out}/img'))
print(len(items), 'items', round(total / 1e6, 1), 'MB')
