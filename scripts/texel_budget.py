"""Texel budget: put low-value upscales back to retail size and spend the space on 4x where it shows.

Each upscaled portal texture is scored by how much the HD version differs from a plain Lanczos resize of retail
(mean absolute luma difference, alpha-weighted, 0-255). Below --trim-below the upscale adds about what texture
filtering would, so the texture goes back to the retail bytes. At or above --upscale-from, textures used on the chosen
weenie categories go to 4x from the workers' kept 4x output, unless they have a Turbine highres version (Very High
draws that instead) or 4x would pass 2048 px.

Only textures in the `fullmanifest` list are touched; it already leaves out everything the terrain blender reads on the
CPU (ground textures and blend masks), which must keep the size the pack gives them.

Inputs (texextract):
    fullmanifest <retail dats> retail_manifest.tsv
    fullmanifest <hd dats>     hd_manifest.tsv
    extract <retail dats> retail_manifest.tsv <retail png dir>
    extract <hd dats>     retail_manifest.tsv <hd png dir>      (same file names, HD pixels)
    whereused <retail dats> retail_manifest.tsv used.tsv
    twins <retail dats> twins.txt
Outputs in --out:
    scores.tsv   every scored texture
    build.tsv    compact manifest: "retail" rows (copy back from retail) and 4x rows with exact sizes
    job4x/       encode job for `texextract encode job4x <kept 4x dir> bins4x 4`
Then: texextract compact <hd dats> build.tsv bins4x <out dats> 2 <retail dats>

usage: texel_budget.py --retail-manifest M --hd-manifest M --retail-png DIR --hd-png DIR --used TSV --twins TXT
                       --weenies DIR --kept4x DIR --out DIR [--trim-below 3] [--upscale-from 5]
"""
import argparse
import collections
import csv
import os
import re
import shutil
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from PIL import Image, ImageFilter

# Weenie folders (ACE world db, 9 WeenieDefaults) whose models get 4x.
SPEND = {'MeleeWeapon', 'MissileLauncher', 'Caster', 'Missile', 'Ammunition', 'Creature', 'CombatPet', 'Clothing',
         'Generic/Armor', 'Armor'}
BYTES_PER_TEXEL = {'PFID_DXT1': 0.5, 'PFID_DXT3': 1, 'PFID_DXT5': 1, 'PFID_INDEX16': 2, 'PFID_R8G8B8': 3,
                   'PFID_A8R8G8B8': 4}
LUMA = np.array([0.2126, 0.7152, 0.0722], np.float32)


def read_manifest(path):
    return {r['id']: r for r in csv.DictReader(open(path, encoding='utf-8'), delimiter='\t') if r['dat'] == 'portal'}


def weenie_categories(root):
    setup, clothing = collections.defaultdict(set), collections.defaultdict(set)
    for d, _, files in os.walk(root):
        rel = os.path.relpath(d, root).split(os.sep)
        cat = rel[0] if rel[0] not in ('Generic', 'Misc') else f'{rel[0]}/{rel[1] if len(rel) > 1 else ""}'
        for f in files:
            if not f.endswith('.sql'):
                continue
            text = open(os.path.join(d, f), encoding='utf-8', errors='replace').read()
            for typ, val in re.findall(r"\(\d+,\s+(\d+), (0x[0-9A-Fa-f]{8})\)", text):
                if typ == '1': setup[int(val, 16)].add(cat)     # Setup
                if typ == '7': clothing[int(val, 16)].add(cat)  # ClothingTable
    return setup, clothing


def score(args):
    retail_png, hd_png = args
    r = Image.open(retail_png).convert('RGBA')
    h = Image.open(hd_png).convert('RGBA')
    a = np.asarray(h, np.float32)
    b = np.asarray(r.resize(h.size, Image.LANCZOS), np.float32)
    w = a[..., 3] / 255
    if w.sum() == 0:
        w = np.ones_like(w)
    return float((np.abs(a[..., :3] @ LUMA - b[..., :3] @ LUMA) * w).sum() / w.sum())


def main():
    ap = argparse.ArgumentParser()
    for k in ('retail-manifest', 'hd-manifest', 'retail-png', 'hd-png', 'used', 'twins', 'weenies', 'kept4x', 'out'):
        ap.add_argument(f'--{k}', required=True)
    ap.add_argument('--trim-below', type=float, default=3)
    ap.add_argument('--upscale-from', type=float, default=5)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    retail, hd = read_manifest(a.retail_manifest), read_manifest(a.hd_manifest)
    upscaled = [i for i in retail if i in hd and (hd[i]['width'] != retail[i]['width'] or hd[i]['format'] != retail[i]['format'])]
    twins = set(open(a.twins).read().split())
    setup, clothing = weenie_categories(a.weenies)
    cats = collections.defaultdict(set)
    for line in open(a.used):
        rs, kind, oid = line.rstrip('\n').split('\t')
        cats[rs] |= (setup if kind == 'setup' else clothing).get(int(oid), set())

    names = [retail[i]['file'] for i in upscaled]
    with ProcessPoolExecutor() as ex:
        gains = list(ex.map(score, [(os.path.join(a.retail_png, n), os.path.join(a.hd_png, n)) for n in names], chunksize=16))

    trim, up4 = [], []
    with open(os.path.join(a.out, 'scores.tsv'), 'w', encoding='utf-8') as f:
        f.write('id\tretail_format\thd_format\trw\trh\thw\thh\tgain\ttwin\tcategories\tdecision\n')
        for i, g in zip(upscaled, gains):
            r, h = retail[i], hd[i]
            rw, rh = int(r['width']), int(r['height'])
            decision = ''
            if g < a.trim_below:
                decision = 'retail'; trim.append(i)
            elif g >= a.upscale_from and cats[i] & SPEND and i not in twins and max(rw, rh) * 4 <= 2048:
                decision = '4x'; up4.append(i)
            f.write(f"{i}\t{r['format']}\t{h['format']}\t{rw}\t{rh}\t{h['width']}\t{h['height']}\t{g:.2f}\t"
                    f"{int(i in twins)}\t{','.join(sorted(cats[i]))}\t{decision}\n")

    job = os.path.join(a.out, 'job4x')
    os.makedirs(job, exist_ok=True)
    with open(os.path.join(a.out, 'build.tsv'), 'w', encoding='utf-8') as b, \
            open(os.path.join(job, 'manifest.tsv'), 'w', encoding='utf-8') as j:
        b.write('file\tdat\tid\tformat\tw\th\n')
        j.write('file\tdat\tid\tformat\tw\th\n')
        for i in trim:
            b.write(f"{retail[i]['file']}\tportal\t{i}\tretail\t\t\n")
        for i in up4:
            r, n = retail[i], retail[i]['file']
            if not os.path.exists(os.path.join(a.kept4x, n)):
                raise SystemExit(f'no kept 4x output for {n}')
            b.write(f"{n}\tportal\t{i}\t{hd[i]['format']}\t{int(r['width']) * 4}\t{int(r['height']) * 4}\n")
            # The encoder reads the 4x image from the kept folder and the retail original (+ .idx/.pal) from the job.
            j.write(f"{n}\tportal\t{i}\t{hd[i]['format']}\t{r['width']}\t{r['height']}\n")
            stem = os.path.splitext(n)[0]
            for ext in ('.png', '.idx', '.pal'):
                src = os.path.join(a.retail_png, stem + ext)
                if os.path.exists(src):
                    shutil.copy(src, os.path.join(job, stem + ext))

    saved = sum(int(hd[i]['width']) * int(hd[i]['height']) * BYTES_PER_TEXEL[hd[i]['format']]
                - int(retail[i]['width']) * int(retail[i]['height']) * BYTES_PER_TEXEL[retail[i]['format']]
                for i in trim if hd[i]['format'] in BYTES_PER_TEXEL and retail[i]['format'] in BYTES_PER_TEXEL)
    cost = sum(int(retail[i]['width']) * int(retail[i]['height']) * 12 * BYTES_PER_TEXEL[hd[i]['format']] for i in up4)
    print(f'{len(upscaled)} upscaled portal textures scored; {len(trim)} back to retail (-{saved / 1e6:.0f} MB), '
          f'{len(up4)} to 4x (+{cost / 1e6:.0f} MB)')


if __name__ == '__main__':
    main()
