"""Downsample model output to the target scale (e.g. x4plus 4x -> 2x) with Lanczos, keeping alpha."""
import sys, os
from PIL import Image

src_dir, up_dir, out_dir, scale = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
os.makedirs(out_dir, exist_ok=True)
for f in sorted(os.listdir(src_dir)):
    if not f.endswith('.png'):
        continue
    w, h = Image.open(os.path.join(src_dir, f)).size
    im = Image.open(os.path.join(up_dir, f)).convert('RGBA')
    if im.size != (w * scale, h * scale):
        im = im.resize((w * scale, h * scale), Image.LANCZOS)
    im.save(os.path.join(out_dir, f))
print('ok', len(os.listdir(out_dir)))
