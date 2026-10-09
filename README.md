# AC texture upscale

Tools for upscaling Asheron's Call's 3D textures with Real-ESRGAN and packing them back into the client's
dat files. The result is a client-only upgrade: it keeps dat iteration numbers unchanged, so it works on any
ACE server with no server changes, and players opt in by swapping two files.

Current output: **9,465 textures at 2x** (every texture on 3D models). `client_portal.dat` is 1.54 GB and
`client_highres.dat` is 0.47 GB, both under the 2 GB per-file limit. It's tested on a local ACE server and on prod.
See [docs/LESSONS-LEARNED.md](docs/LESSONS-LEARNED.md) for what we found along the way, including why full 4x
doesn't fit.

> This repo holds code only. Never commit dat files, extracted textures, or other game data
> (`.gitignore` blocks the common types).

## How it works

```
 laptop (Windows, owns the game files)            GPU workers (Ubuntu: Osiris, Dmo-N)
 ------------------------------------             ------------------------------------
 1. fullmanifest  - list 3D textures
 2. extract       - decode to PNG + palette
                    sidecars (.idx/.pal)
 3. queue_jobs.py - batches into a synced  --->   4. upscale_worker.py
                    folder (READY last)                Real-ESRGAN x4plus (4x)
                                                       texextract encode: Lanczos to 2x,
                                                       re-encode to game formats -> .bin
 6. todxt         - uncompressed -> DXT    <---   5. .bin files back (COMPLETE last);
 7. compact       - rebuild portal/highres            4x PNGs stay on the worker's disk
                    from scratch
 8. verifydat     - check against retail
 9. swap-dats.ps1 - swap into the install
```

The job queue is a MEGA-synced folder: `jobs/batch_NNNNN/` from the laptop, `done/<worker>/batch_NNNNN/` from
each worker. Batches are split by number (even/odd), so the two workers never write to the same place.
Worker setup is in [docs/WORKER-SETUP.md](docs/WORKER-SETUP.md).

## Layout

| Path | What |
|---|---|
| `texextract/` | C# (.NET 8) tool that reads and writes dats via Chorizite.DatReaderWriter |
| `worker/` | `upscale_worker.py` (stdlib only) and `setup_worker.sh` for the Ubuntu GPU machines |
| `scripts/` | `queue_jobs.py` (queue batches), `finalize.py`, `contact_sheet.py`, `compare.py`, `viewer_assets.py` |
| `swap/` | `swap-dats.ps1` plus double-click `.bat` files to swap dev/retail dats in place, verifying retail |
| `docs/` | Lessons learned, worker setup |

## texextract commands

```
texextract fullmanifest <datDir> <out.tsv>                      every 3D texture in an encodable format
texextract collect <datDir> <landblockHex> <ids.txt> <out.tsv>  every texture in one landblock (+ extra setups)
texextract extract <datDir> <manifest.tsv> <outDir>             decode to PNG; INDEX16 also gets .idx/.pal
texextract encode <jobDir> <upscaledDir> <outDir> <scale>       (worker) resize + re-encode to .bin
texextract todxt <manifest.tsv> <binDir> <outBinDir> <out.tsv>  R8G8B8/A8R8G8B8 -> DXT1/DXT5
texextract compact <datDir> <manifest.tsv> <binDir> <outDir> [scale]   rebuild portal/highres compactly
texextract verifydat <retail.dat> <new.dat> [manifest.tsv]      header + every entry vs retail
texextract packbins <datDir> <manifest.tsv> <binDir> <outDir> [scale]  in-place pack (small sets only)
texextract classify | whereused | sample | stats | probe        analysis helpers
```

Build locally: `dotnet build -c Release texextract`. For the workers, publish self-contained:
`dotnet publish texextract -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true`.

## Full run, end to end

```powershell
texextract fullmanifest C:\...\dats work\full_manifest.tsv
texextract extract C:\...\dats work\full_manifest.tsv work\full\src
python scripts\queue_jobs.py work\full\src <MEGA>\upscale full-3d-v1 --size 100 --manifest work\full_manifest.tsv
# ...workers run; copy done\*\batch_*\*.bin into work\full\bins...
texextract todxt work\full_manifest.tsv work\full\bins work\full\bins_dxt work\full_manifest_dxt.tsv
texextract compact C:\...\dats work\full_manifest_dxt.tsv work\full\bins_dxt work\dats_full2x
texextract verifydat C:\...\dats\client_portal.dat work\dats_full2x\client_portal.dat work\full_manifest_dxt.tsv
```

Then copy the two dats into the swap script's dev set and run `swap\Swap To Dev Dats.bat` (game closed).

## Known limits

- **2 GB per dat file:** full 4x would need about 7 GB for portal. 4x is possible only for chosen subsets.
- **Excluded for now:** terrain (blended at fixed size with masks) and UI/icons (drawn at pixel size).
- **Palettized (INDEX16) textures** stay palettized so armor dyes and creature color variants keep working.
- **Hard-coded paths:** some scripts assume the original Windows workspace paths (`C:\Users\ostet\ac-decomp`,
  the MEGA folder) and need adjusting on another machine.

## Third-party

[Chorizite.DatReaderWriter](https://www.nuget.org/packages/Chorizite.DatReaderWriter),
[BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) (MIT),
[StbImageSharp / StbImageWriteSharp](https://github.com/StbSharp) (public domain),
[Real-ESRGAN ncnn-vulkan](https://github.com/xinntao/Real-ESRGAN) (BSD-3-Clause).
Asheron's Call game data is not included and must not be committed.
