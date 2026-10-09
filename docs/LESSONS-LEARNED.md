# Lessons learned: Asheron's Call texture upscale

What we learned taking AC's 3D textures from retail to a full 2x pack that runs on the live server
(2026-10-08 to 10-09). Each item says what happened, why it mattered, and what we do now.

## Result

- 9,465 textures (every texture used on 3D models) upscaled with Real-ESRGAN `realesrgan-x4plus`,
  downsampled 4x to 2x, re-encoded to the game's formats, and packed into rebuilt dat files.
- `client_portal.dat` is 1.54 GB and `client_highres.dat` is 0.47 GB, both under the 2 GB limit.
- Works on the local test server and on prod. It's a client-only change: the server needs nothing.
- Busiest place on prod: `acclient.exe` at about 1.0 GB working set and 1.5 GB virtual, out of the 4 GB
  available to a large-address-aware 32-bit process.

## The game files

**Dat files have a hard 2 GB ceiling.** The full set at 2x overflowed `client_portal.dat` at 2,147,446,784 bytes.
The library uses signed 32-bit offsets, and the 1999 client almost certainly does too. Treat 2 GB per dat as
the budget. This is why a full 4x pack isn't possible: portal alone would be about 7 GB, and palettized
textures by themselves exceed 2 GB at 4x. Full 4x needs client changes (loading textures from outside the dats).

**Writing in place leaves dead space.** Replacing an entry appends the new data and abandons the old blocks,
so an in-place pack holds the retail size plus all the new data. Fix: `texextract compact` builds portal and
highres from scratch, copying every retail entry byte-for-byte with its original B-tree metadata and writing
each upgraded texture once.

**When rebuilding a dat, verify everything, then leave the journal empty.** The library stamps entries with
the current date, so we restore each entry's `RawDate`. `texextract verifydat` checks every header field
and every entry's bytes and metadata against retail. The header's `Transactions` block is a write journal that
holds retail block offsets. Copying it into a rebuilt file would point it at unrelated data, so it's left empty.
A rebuilt-retail control set (no changes) also passed and is kept for diagnosing problems.

**Keep dat iteration numbers unchanged.** The server's only dat check compares iterations ("no update
required"). Unchanged iterations mean the upgrade works on any server with no server changes, and players can
opt in per machine. Caution: ACE's `EnableDATPatching` could overwrite upgraded files if it's ever turned on with
mismatched versions.

**Know which textures are safe to resize.** Only textures referenced through a `SurfaceTexture` sit on 3D
models with normalized UVs, so a bigger texture is safe there. UI art (including about 13,000 icons) draws at
exact pixel size, and terrain textures are blended at fixed resolution with masks we didn't resize. Both
groups are excluded until they get their own approach.

**Palettized (INDEX16) textures have to stay palettized.** The client recolors them by swapping palette
ranges (armor dyes, creature variants). We upscale the full-color version, then map each new pixel only to
palette indices found in the 3x3 neighborhood of the original. That keeps every pixel in its recolor range.
Of 9,465 textures, 5,387 are palettized; they can't be compressed, and they dominate the size budget.

**Uncompressed textures can become DXT.** Converting 1,012 R8G8B8 and A8R8G8B8 textures to DXT1/DXT5 saved
623 MB (752 MB to 129 MB) and is what made the full 2x pack fit. The client accepted the format change.

**The client generates its own mipmaps.** Textures are stored as a single level, which keeps encoding simple.

## Image quality

- `realesrgan-x4plus` ("detailed") beat `realesr-animevideov3` ("fast") on almost everything, and the user
  chose it for all textures. It smooths grainy terrain more than the fast model does.
- x4plus only produces 4x. 4x then down to 2x with Lanczos gives a sharper 2x than a direct 2x upscale.
- Downscale with premultiplied alpha. Otherwise hidden color in transparent pixels bleeds dark fringes into
  foliage and other cutout edges.
- The upscale adds real detail to readable art (maps, signs, rugs, floors) but mostly sharpens edges on
  low-detail and palettized art.

## Compute and the job queue

**Do heavy work on the GPU machines.** The laptop's Vega 8 ran x4plus at about 30 s per texture. The RTX 3090
and RTX A4500 ran it at about 0.2 s, roughly 150x faster. The full set took about 22 minutes of GPU time. DXT
encoding also runs on the workers; the laptop only extracts, queues and packs.

**A synced folder makes a good job queue if the protocol is strict.** `jobs/` and `done/` in MEGA, with
`READY` written last by the laptop and `COMPLETE` written last by the worker. Workers also check file and
sidecar counts before starting. Batches are split by number (even to Osiris, odd to Dmo-N), so no locks are
needed and sync conflicts can't happen.

**Restart workers whenever the worker script changes.** A running worker keeps the old code in memory. We
held the full queue until both workers confirmed the restart and their script and encoder hashes matched.

**Keep large outputs off the synced folder.** The 4x PNGs (about 15 GB) stay on the workers' disks. Only
encoded `.bin` files come back. Even those were 2.2 GB for the full set, so expect sync time to dominate.

**Ship workers self-contained binaries.** The encoder is a single-file `linux-x64` .NET publish, so workers
need no .NET install.

**Check the obvious when sync seems slow.** MEGAsync wasn't syncing the new folder on the desktop at all.

## Testing

**Change one thing at a time, and validate the baseline first.** We changed the server and the dats together,
got a black screen, and couldn't tell which was at fault. The working order is: known-good client on the
server, then one change.

**Test from the default install location.** A copy of the client in another folder isn't a valid test with
this setup (Decal, ThwargLauncher). The swap script moves dat files in and out of
`C:\Turbine\Asheron's Call` instead, and verifies the retail fingerprints when restoring.

**This client needs ThwargLauncher plus Decal, with "Use Alternate Injection Method" on.** A direct
`acclient.exe` launch always black-screens on this machine, so it isn't a useful baseline.

**Test by location, not by object.** With about 9,000 textures, hunting individual objects doesn't scale.
Collecting every texture in one landblock (the starter dungeon) gave an exact before-and-after from the same
camera spot, plus a real memory number.

**Read crash records carefully before blaming the newest change.** The 0xe0434352 entry named
VirindiViewService, but the real fault was an access violation in `acclient.exe`. Retail dats had produced
similar crashes days earlier, and the server error the user saw (a missing custom item) was logged two seconds
*after* the client died. Prod then ran the same pack fine.

## Infrastructure mistakes (avoid repeating)

- **Never run `New-Item -Force` on an existing registry key.** It deletes and recreates the key. This wiped
  Decal's per-user options (`HKCU\SOFTWARE\Decal\Agent`), including alternate injection, and broke logins on
  every server. Export a key before touching it, and prefer not changing the player's launcher setup at all.
- **A stale local server looks like a client bug.** The local ACE build was 228 commits (about 11 months)
  behind prod and black-screened clients after login. Build the test server from the same branch as prod.
- **Never start ACE.Server with stdin closed.** Its console loop spins at 100% CPU and writes a log of
  hundreds of MB.
- **Read the request carefully.** "Pack a 2x and a 4x" meant the full set, not one room.

## Tooling notes

- SixLabors.ImageSharp 4.x needs a paid commercial license. We use StbImageSharp/StbImageWriteSharp (public
  domain) and BCnEncoder.Net (MIT) instead.
- Chorizite.DatReaderWriter reads and writes dats, including creating new ones (`InitNew` + `SetVersion`).
- Windows PowerShell 5.1 quirks: backticks in SQL break inline `-e` strings (pipe a here-string instead),
  `Select-Object -First` on a native command kills that process, and `-h127.0.0.1` parses badly (use
  `--host=`).

## Open items

- Terrain (LSCAPE textures plus blend masks) and UI/icons: each needs its own approach.
- 4x: possible for chosen subsets within the 2 GB budget. Full 4x needs client changes.
- A retail memory baseline in the same busy spot, to put a precise number on the upgrade's cost.
- The single local-server crash while loading Shoushi hasn't been reproduced.
- The local test server lacks prod's custom content (missing custom weenies such as 64454601).
- Dmo-N's worker runs as a transient systemd unit and won't survive a reboot.
- Distribution to players: an installer or patch that swaps the two dats, with a restore option.
