"""Mean color shift (pack vs retail) per texture format, over visible pixels, after shrinking the pack texture
back to retail size. Usage: drift.py OLD_MANIFEST NEW_MANIFEST RETAIL_PNG_DIR PACK_PNG_DIR"""
import collections
import sys
import numpy as np
from PIL import Image

old_m, new_m, rdir, pdir = sys.argv[1:5]
old = {l.split('\t')[0]: l.split('\t') for l in open(old_m).read().splitlines()[1:]}
new = [l.split('\t') for l in open(new_m).read().splitlines()[1:]]
groups = collections.defaultdict(list)
for r in new:
    a = np.asarray(Image.open(f'{rdir}/{r[0]}').convert('RGBA'), np.float32)
    b = Image.open(f'{pdir}/{r[0]}').convert('RGBA').resize(a.shape[1::-1], Image.BOX)
    b = np.asarray(b, np.float32)
    vis = a[..., 3] > 8
    if not vis.any():
        continue
    k = old[r[0]][3].replace('PFID_', '') + (' -> ' + r[3].replace('PFID_', '') if r[3] != old[r[0]][3] else '')
    groups[k].append(b[..., :3][vis].mean(0) - a[..., :3][vis].mean(0))

def show(label, v):
    v = np.array(v); l = 0.299 * v[:, 0] + 0.587 * v[:, 1] + 0.114 * v[:, 2]
    print(f'{label:26} n={len(v):3}  R{v[:, 0].mean():+5.1f} G{v[:, 1].mean():+5.1f} B{v[:, 2].mean():+5.1f}  '
          f'luma {l.mean():+5.1f}  (worst luma {l.min():+.0f})')

everything = []
for k, v in sorted(groups.items()):
    show(k, v); everything += v
show('ALL', everything)
