# Lessons learned: Asheron's Call texture upscale

What we learned taking AC's textures from retail to **release v1** (2026-10-08 to 10-09): every texture on 3D
models plus the outdoor ground at 2x, running on the local test server and on prod. Each item covers what
happened, why it mattered, and what we do now. The build recipe for v1 is in [RELEASE.md](RELEASE.md).

## Result

- 9,465 textures (everything on 3D models, including the 32 ground textures) plus 24 terrain blend masks, all at 2x.
  Upscaled with Real-ESRGAN `realesrgan-x4plus` (4x, then Lanczos down to 2x).
- Outdoor terrain tile raised from 1024 to 2048 px via one value in the Region record.
- `client_portal.dat` 1.65 GB and `client_highres.dat` 0.48 GB, under the 2 GB per-file limit.
- Client-only: works on any ACE server, verified on the local server and on prod. Shipped as an 819 MB zip with
  an installer and an instant HD/retail switch.
- Memory (address space, out of the 4 GB available to the large-address-aware retail client): retail 1.17 GB vs
  pack 1.56 GB at the same outdoor spot; up to about 1.76 GB seen outdoors.

## The game files

**Dat files have a 2 GB ceiling.** A full 2x pack overflowed `client_portal.dat` at 2,147,446,784 bytes. The
library uses signed 32-bit offsets, and the 1999 client almost certainly does too, so treat 2 GB per dat as the
budget. That rules out full 4x: portal alone would be about 7 GB, and palettized textures by themselves exceed
2 GB at 4x. Full 4x needs client changes, such as loading textures from outside the dats.

**Writing in place leaves dead space.** Replacing an entry appends the new data and abandons the old blocks.
`texextract compact` rebuilds portal and highres from scratch instead: every retail entry is copied byte-for-byte
with its original B-tree metadata, and each upgraded texture is written once.

**When rebuilding a dat, verify everything and leave the journal empty.** The library stamps written entries
with the current date, so we restore each entry's `RawDate`. `texextract verifydat` checks every header field and
every entry's bytes and metadata against retail. The header's `Transactions` block is a write journal holding
retail block offsets; it's left empty because in a rebuilt file those offsets would point at unrelated data.
A rebuilt-retail control set (no changes) passed too, and is the first thing to test if a rebuilt dat misbehaves.

**Keep dat iteration numbers unchanged.** The server's only dat check compares iterations. Unchanged iterations
mean the upgrade works on any server and players can opt in per machine. Caution: ACE's `EnableDATPatching`
could push files over upgraded ones if it's ever enabled with mismatched versions.

**Palettized (INDEX16) textures must stay palettized.** The client recolors them by swapping palette ranges
(armor dyes, creature variants). We map each new pixel only to palette indices found in the 3x3 neighborhood of
the original pixel, which keeps every pixel inside its recolor range. These are 5,387 of the textures; they
can't be compressed and they dominate the size budget.

**Compression is fine for GPU textures, but not for CPU-read ones.** Converting 1,012 uncompressed textures to
DXT saved 623 MB, which is what let the 2x pack fit. But the client reads some textures on the CPU and assumes
their stored layout; see terrain below.

**UI art draws at pixel size.** About 13,000 icons and interface images are excluded until we know how the
client sizes UI elements.

## Terrain: the crash chain, and how we found it

The outdoor ground is not drawn like models. `TexMerge::FillTempTexBuffer` composites each ground tile on the
CPU: `CopyAndTile` -> `ImgTex::CopyCSI` -> `ImgTex::TileCSI` copies a terrain texture `TexTiling` x `TexTiling`
times into a shared buffer (`TexMerge::tex_data`, sized `BaseTexSize`^2 x 4 bytes), then `ImgTex::MergeTexture`
blends in corner, side and road masks. The finished tile is what the GPU draws.

Each fix moved the crash one step further, which is how we knew we were on the right track:

1. **Ground textures had been converted to DXT.** `TileCSI` reads 4 bytes per pixel, so it ran off the end of
   the smaller compressed data (crash `0x53F560`, a read). Fix: keep ground textures uncompressed `A8R8G8B8`.
2. **2x ground with retail masks.** The overflow from step 3 corrupted memory, and the crash surfaced in
   `MergeTexture`'s mask read (`0x53F786`). We also upscaled the 24 `LSCAPE_ALPHA` masks to 2x (plain Lanczos;
   they're smooth gradients).
3. **Tile buffer too small.** 1024 px texture x tiling 2 = 2048 px written into a 1024 buffer (crash `0x53F562`,
   the write). Fix: `Region.TerrainInfo.LandSurfaces.TexMerge.BaseTexSize` 1024 -> 2048, a data-only change.
   It also doubles on-screen ground resolution, which is the real visual gain: with a 1024 tile, sharper source
   textures could never show up.

The ground textures also exposed a classification gap. They're referenced through `SurfaceTexture`s, like model
textures, so they slipped into the "3D" set. `texextract terrainids` now lists everything reachable from the
Region, and `fullmanifest` handles those textures separately.

**How we found the crash site:** every crash record showed the same `acclient.exe` fault offset. We
disassembled the live 2015 client (Capstone), found the same bytes in the 2013 client, and looked up the
2013 address in Turbine's shipped PDB with a small MSF/PDB reader of our own (`S_PUB32` symbols), which named
`ImgTex::TileCSI`. Tools are in `docs/` and the scratch scripts. The same method works for any future client crash.

## Image quality

- `realesrgan-x4plus` ("detailed") beat `realesr-animevideov3` on almost everything and was chosen for all
  textures.
- **The model shifts color.** Measured against retail: average -2.5 brightness with red dropping most (a cool,
  blue-purple lean), DXT1 building textures -5.2 on average, ground up to -24, and visible seams between the
  upscaler's processing tiles. Under AC's blue-purple night lighting this read as "everything is darker and more
  purple."
- **Fix: back-projection.** Shrink the 2x result to the original size, take the difference from the original,
  enlarge that error and add it back (2 passes). It restores the original colors and removes tile seams while
  keeping the new detail. Drift went from -3.2 to -0.1 and worst from -21 to -1. The worker encoder now does
  this automatically (`texextract encode`).
- v1 ships with back-projection on the 32 ground textures only, which is where the drift was visible. A full
  re-encode of everything with back-projection is ready (workers kept their 4x output) and is the obvious v1.1.
- Downscale with premultiplied alpha, so transparent pixels don't bleed dark fringes into cutouts.

## Compute and the job queue

- **Heavy work belongs on the GPU machines.** The laptop's Vega 8 ran x4plus at about 30 s per texture; the
  RTX 3090 and RTX A4500 ran it at about 0.2 s. The full set took about 22 minutes of GPU time.
- **A synced folder works as a job queue if the protocol is strict.** `jobs/` and `done/` in MEGA, `READY`
  written last by the laptop, `COMPLETE` written last by the worker, file and sidecar counts checked before a
  batch starts. Batches are split by number (even to one worker, odd to the other), so no locks are needed.
- **Restart workers whenever the worker script changes,** because a running worker keeps the old code in
  memory. Confirm with script and binary md5s in each worker's NOTES.md before queueing.
- **Keep big outputs on the workers' disks.** The 4x PNGs (about 15 GB) stay in `~/ac-upscale/keep/<tag>/`, and
  re-encodes reuse them without the GPU. Only encoded `.bin` files come back through MEGA (2.2 GB for the full set).
- **Ship self-contained binaries.** The encoder is a single-file `linux-x64` .NET publish.
- **Check the obvious when sync is slow:** MEGAsync simply wasn't syncing the new folder at first.

## Testing

- **Change one thing at a time, from a validated baseline.** Server and dats changed together once, and we
  couldn't tell which caused the black screen.
- **Test from the default install location.** This setup (Decal, ThwargLauncher) only works with the client in
  `C:\Turbine\Asheron's Call`; swap dat files in place instead of using a client copy. This client also needs
  ThwargLauncher plus Decal with "Use Alternate Injection Method" on. A direct `acclient.exe` launch always
  black-screens here.
- **Test by location, not by object.** Collecting every texture in a landblock gave exact before/after shots
  and real memory numbers.
- **Compare the same spot at the same time of day.** Night lighting tints everything blue-purple. For the
  local server, `start-local-server.ps1 -TimeOffsetMinutes N` shifts the day/night clock (a local-only ACE
  patch; one Dereth day is about 127 real minutes).
- **Measure color, don't eyeball it.** `scripts/drift.py` compares average color per format between retail
  and a pack, independent of in-game lighting.
- **Read crash records before blaming the newest change,** and check whether the address repeats. One crash
  can be noise; four at the same instruction is a bug.

## Infrastructure mistakes (avoid repeating)

- **Never run `New-Item -Force` on an existing registry key.** It recreates the key and wiped Decal's
  per-user options, breaking logins on every server. Export a key before touching it.
- **A stale local server looks like a client bug.** The local ACE build was 228 commits behind prod and
  black-screened clients. Build the test server from the same branch as prod.
- **Never start ACE.Server with stdin closed.** Its console loop spins at 100% CPU.
- **Classify by how the client uses data, not by format.** Ground textures looked like model textures.
- **Read the request carefully.** "Pack a 2x and a 4x" meant the full set, not one room.

## Tooling notes

- SixLabors.ImageSharp 4.x needs a paid license; we use StbImageSharp/StbImageWriteSharp (public domain) and
  BCnEncoder.Net (MIT).
- Chorizite.DatReaderWriter reads and writes dats, including creating new ones (`InitNew` + `SetVersion`).
- Windows PowerShell 5.1 pitfalls: backticks in inline SQL, `Select-Object -First` killing native processes,
  `-h127.0.0.1` parsing, and broad `Remove-Item` patterns being blocked.

## Open items

- v1.1: full back-projection re-encode for color accuracy on everything (no GPU time needed).
- UI and icons: need to learn how the client sizes UI elements first.
- 4x: possible for chosen subsets within the 2 GB budget; full 4x needs client changes.
- Lower "landscape detail" settings haven't been tested with the 2048 terrain tile.
- The local test server lacks prod's custom content (missing custom weenies).
