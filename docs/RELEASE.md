# Release v1: AC HD Textures 2x

Shipped 2026-10-09 as `AC-HD-Textures-2x-v1.zip` (819 MB, zip SHA-256
`3265433b11763e7af83106d452034917b5ce1975e19c84967af4161e87271e60`).

**Download:** [GitHub release v1.0](https://github.com/osteth/ac-texture-upscale/releases/tag/v1.0). The player-facing kit (installer, switch
scripts, README, checksums) is in [`release/`](../release); the two dat files aren't in git.

| File | SHA-256 | Size |
|---|---|---|
| `client_portal.dat` | `1509a08871f1ed068dfd61495b839e659d2768bb10e9f4f0302a50f91ff67b9f` | 1,724,492,800 |
| `client_highres.dat` | `a80db1fc5b7533092007920abe1dfa414ae766ad976d361d7f8d89cf742658e0` | 502,809,600 |

Built from end-of-retail dats (portal iteration 2072, highres 497; the iterations are unchanged in the release).

## What's in it

- 9,433 model textures at 2x: realesrgan-x4plus 4x, Lanczos to 2x, re-encoded to the original format. 1,012
  R8G8B8/A8R8G8B8 textures were converted to DXT1/DXT5 to fit the 2 GB limit. Encoded *before* back-projection
  existed, so these carry the model's slight color drift (average -2.5 brightness).
- 32 ground textures at 2x, kept uncompressed `A8R8G8B8` (the CPU terrain blender requires it), color-corrected
  with back-projection.
- 24 terrain blend masks (`LSCAPE_ALPHA`) at 2x (Lanczos).
- `Region 0x13000000` `TexMerge.BaseTexSize` 1024 -> 2048.

## Exact build steps

Run from the workspace with `texextract` built (paths shortened):

```
texextract fullmanifest dats work\full_manifest.tsv                 # 9,465 rows incl. the 32 ground textures*
texextract extract dats work\full_manifest.tsv work\full\src
python scripts\queue_jobs.py work\full\src <MEGA>\upscale full-3d-v1 --size 100 --manifest work\full_manifest.tsv
# workers -> done\*\batch_*\*.bin  ->  work\full\bins
texextract todxt work\full_manifest.tsv work\full\bins work\full\bins_dxt work\full_manifest_dxt.tsv

# terrain: the 32 ground rows in their original A8R8G8B8 form, back-projected against retail
texextract terrainids dats work\terrain_ids.txt
#   (terrain rows of full_manifest.tsv -> work\terrain_manifest.tsv; their bins -> work\full\bins_terrain)
python scripts\backproject.py work\terrain_manifest.tsv <retail PNGs> work\full\bins_terrain work\full\bins_terrain_bp
texextract masks2x dats work\full\bins_masks work\masks_rows.tsv

# final manifest = DXT manifest without the 32 ground rows + the 32 ground rows (A8R8G8B8) + mask rows
#   -> work\full_manifest_terrainB.tsv
texextract compact dats work\full_manifest_terrainB.tsv "work\full\bins_masks;work\full\bins_terrain_bp;work\full\bins_dxt" work\dats_terrainD
texextract setbasetex work\dats_terrainD\client_portal.dat 2048
texextract verifydat dats\client_portal.dat work\dats_terrainD\client_portal.dat work\full_manifest_terrainB.tsv
```

\* `fullmanifest` now skips Region-referenced textures, because they need the special terrain handling above.
v1 was built before that change, so its manifest still listed them and they went through the terrain steps.

## Testing done

- Local ACE server (current ACECustom master) and prod: logins, cities, busiest area, outdoor travel.
- Same-spot daytime comparisons against retail at 17.4N 63.2E and 21.5S 1.7W, plus the starter dungeon.
- Memory: up to about 1.76 GB of the 4 GB address space outdoors (retail clients are large-address-aware).
- Installer kit: full install -> switch -> switch -> uninstall cycle restores the originals byte-for-byte.

Not tested: lower "landscape detail" graphics settings with the 2048 terrain tile.

## v1.0.1 packaging

v1.0 plus one new image, repackaged:
- The character select background (`0x06007576`, a raw JPEG RenderSurface) is replaced with
  [`art/06007576-character-select.jpg`](../art/06007576-character-select.jpg). It is 800x600, baseline 4:4:4 JPEG
  like the original, because clients draw UI images at their pixel size: a bigger image breaks the screen layout
  in any client that doesn't scale it. Every other portal entry is byte-identical to v1.0.
- `client_highres.dat` is the v1.0 file, unchanged.
- The zip now carries all four dats in an `HD DATs` folder, so Vitaeum can use it directly as a DAT folder,
  globally or per launch row. Cell and local are the unchanged end-of-retail files.
- A second download, `AC-Retail-DATs.zip`, holds the four clean end-of-retail dats.
- No installer scripts: players copy the four dats into their game folder, and copy the AC-Retail-DATs set back
  to undo it. Vitaeum users point a DAT folder at `HD DATs` instead.

| File | SHA-256 | Size |
|---|---|---|
| `client_portal.dat` | `f23e5cf1a0e8471aaf1bc3865264929a42b6236cf9fd1fdcc708dbc0a3f7ae6c` | 1,724,441,600 |
| `client_highres.dat` | `a80db1fc5b7533092007920abe1dfa414ae766ad976d361d7f8d89cf742658e0` (same as v1.0) | 502,809,600 |

Build the portal from the v1.0 dats (the manifest has one row, `06007576.png  portal  06007576  500`, and the
bin folder holds the JPEG as `06007576.bin`; scale 1 keeps the stored 0x0 size that JPEG surfaces use):

```
texextract compact <v1.0 dats> work\charsel.tsv work\charsel_bins <out> 1
texextract verifydat <v1.0 dats>\client_portal.dat <out>\client_portal.dat work\charsel.tsv
# then copy the v1.0 client_highres.dat over the rebuilt one (compact rewrites it with a different hash)
```

Build both zips with:

```
python scripts/build_release.py --hd <HD dat dir> --retail <retail dat dir> --version v1.0.1 --out <out dir>
```

The script checks each dat before packing: HD against `release/SHA256SUMS.txt`, retail portal/cell/local against
the end-of-retail hashes, and that the retail highres isn't the HD one. It also writes
`release/retail/SHA256SUMS.txt`.

## Next (v1.1)

Re-encode everything with back-projection (workers reuse their kept 4x output, so no GPU time), rebuild with
the same terrain steps, and ship as v1.1. Expect a slightly brighter, more neutral look on buildings and armor.
