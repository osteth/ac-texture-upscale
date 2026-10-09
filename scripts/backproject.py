"""Back-projection color fix for upscaled textures.

Real-ESRGAN x4plus drifts color (notably darkening terrain). For each texture: shrink the 2x result to the
original size, take (original - shrunk) as a low-frequency color error, enlarge that error back to 2x and add
it. Original colors are restored while the model's fine detail is kept.

Works on raw A8R8G8B8 .bin files (BGRA, 2x size) plus the retail PNGs extracted at 1x.
usage: backproject.py MANIFEST RETAIL_PNG_DIR BIN_DIR OUT_BIN_DIR [iterations]
"""
import os
import sys
import numpy as np
from PIL import Image

manifest, retail_dir, bin_dir, out_dir = sys.argv[1:5]
iters = int(sys.argv[5]) if len(sys.argv) > 5 else 2
os.makedirs(out_dir, exist_ok=True)
rows = [l.split('\t') for l in open(manifest, encoding='utf-8').read().splitlines()[1:]]

def resize(arr, size, method):
    return np.stack([np.asarray(Image.fromarray(arr[..., c]).resize(size, method), dtype=np.float32)
                     for c in range(arr.shape[2])], axis=-1)

before, after = [], []
for r in rows:
    if r[3] != 'PFID_A8R8G8B8':
        continue
    w, h = int(r[4]), int(r[5])
    orig = np.asarray(Image.open(os.path.join(retail_dir, r[0])).convert('RGBA'), dtype=np.float32)
    raw = np.frombuffer(open(os.path.join(bin_dir, r[0][:-4] + '.bin'), 'rb').read(), dtype=np.uint8)
    up = raw.reshape(h * 2, w * 2, 4)[..., [2, 1, 0, 3]].astype(np.float32)  # BGRA -> RGBA
    before.append(up[..., :3].mean((0, 1)) - orig[..., :3].mean((0, 1)))
    for _ in range(iters):
        down = resize(up, (w, h), Image.BOX)
        err = orig - down
        err[..., 3] = 0  # leave alpha alone
        up = np.clip(up + resize(err, (w * 2, h * 2), Image.BICUBIC), 0, 255)
    after.append(up[..., :3].mean((0, 1)) - orig[..., :3].mean((0, 1)))
    out = np.round(up).astype(np.uint8)[..., [2, 1, 0, 3]]  # back to BGRA
    open(os.path.join(out_dir, r[0][:-4] + '.bin'), 'wb').write(out.tobytes())

b, a = np.mean(before, 0), np.mean(after, 0)
print(f'{len(after)} textures; mean color shift before R{b[0]:+.2f} G{b[1]:+.2f} B{b[2]:+.2f}, '
      f'after R{a[0]:+.2f} G{a[1]:+.2f} B{a[2]:+.2f}; worst before {np.abs(before).max():.1f}, after {np.abs(after).max():.2f}')
