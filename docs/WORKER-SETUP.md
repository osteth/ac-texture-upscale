# Handoff: GPU upscale workers on Osiris and Dmo-N

## UPDATE 2 (2026-10-08 night): restart both workers on the new script before the full run

The starting-room run (batches 0-7) is collected and its jobs/done folders were cleaned up. Thanks for
NOTES.md; the Dmo-N details (path, `--gpu 1`, systemd) are noted.

**What changed:** jobs can now carry `"post": "encode"`. For those, after Real-ESRGAN the worker runs
`worker/bin/texextract` (a self-contained Linux x64 binary, 68 MB, no .NET install needed). It resizes the 4x
output to 2x and encodes it to the game's pixel formats. Only small `.bin` files go back to `done/`. The full-size
4x PNGs are kept locally in `~/ac-upscale/keep/<tag>/` instead of going through MEGA (about 15 GB for the full set).

**Why a restart matters:** a running worker still has the old script in memory. It would ignore
`"post": "encode"` and push 4x PNGs through MEGA. The laptop will **not** queue the full run until you
confirm both workers have restarted.

Steps on each machine:
1. Wait until `worker/upscale_worker.py` and `worker/bin/texextract` (67,834,539 bytes) have synced.
2. Stop the old worker and start it again with the same arguments as before:
   - Osiris: `tmux kill-session -t acup`, then the same `tmux new -d -s acup "python3 ... --gpu 0"` command.
   - Dmo-N: `systemctl --user stop acup` (the transient unit disappears), then the same `systemd-run --user ...
     --gpu 1` command as before. If convenient, make it a real unit file in `~/.config/systemd/user/acup.service`
     with `loginctl enable-linger $USER`, so it survives reboots.
3. Quick check: run `"$UP/worker/bin/texextract"` with no arguments. After a `chmod +x` (the worker does this
   itself), it should print a usage line. If it errors instead, note it in `done/<name>/NOTES.md`.
4. Make sure there is about 10 GB free on each machine for `~/ac-upscale/keep/` (each keeps roughly half).
5. Write "restarted on new worker" plus the date and time into `done/<name>/NOTES.md`. That's the laptop's
   signal to queue.

**Next queue:** about 95 batches of 100 textures (tag `full-3d-v1`), 9,465 textures in total, every texture used on
3D models. With `"post": "encode"`, a batch has `.png` images, `.idx`/`.pal` sidecars for palettized
textures, and a `manifest.tsv`. The worker waits for all of them to sync (`job.json` has the counts).

---

*(Original handoff below. Still accurate except where Update 2 says otherwise.)*

Written 2026-10-08 from the laptop session. Audience: whoever (person or Claude session) sets up the
GPU workers on **Osiris** (this desktop, Ubuntu) and **Dmo-N** (home server, Ubuntu, reachable from Osiris).

## What this is

We're upscaling Asheron's Call textures for an ACE emulator server (fork: rkroska/ACECustom).
The Windows laptop extracts textures from the game's .dat files and queues them here. These two
Ubuntu machines run Real-ESRGAN on their GPUs. The laptop collects the results, downsizes them,
and packs them back into the dats. The laptop is the only machine that touches game files.

Your job: get one worker running on each machine and leave them running.

## Current queue

8 batches (`jobs/batch_00000` to `batch_00007`), 536 textures, tag `startroom-7F03`
(every texture in the new-character starting dungeon), model `realesrgan-x4plus`, scale 4.
Each batch is about 67 PNGs. Expect a few minutes per batch on a real GPU.

## Folder protocol (this folder, synced by MEGA)

```
upscale/
  HANDOFF.md            this file
  worker/               upscale_worker.py, setup_worker.sh
  jobs/batch_NNNNN/     input PNGs + job.json, then READY (laptop writes READY last)
  done/<worker>/batch_NNNNN/   upscaled PNGs + result.json, then COMPLETE (worker writes COMPLETE last)
```

- A worker only starts a batch when `READY` exists **and** the PNG count matches `job.json` `count`.
  If MEGA is still syncing, it logs "N/M files synced so far" and retries. That's normal.
- Ownership is fixed by batch number: Osiris takes even batches (`--shard 0`), Dmo-N takes odd
  (`--shard 1`), with `--shards 2`. The two machines never write to the same folder, so there is no
  locking and no sync conflicts. Don't change the shard numbers or add a third worker without updating both.
- A worker skips any batch that already has `done/<its name>/batch_NNNNN/COMPLETE`, so restarting is safe.
- Work happens in a local temp dir, not in the synced folder, so MEGA never uploads half-written files.

## Setup (do this on each machine)

1. Find the local MEGA sync path for this folder. Below it's written as `$UP`, for example
   `UP=~/MEGA/ac-decomp/upscale`. Confirm that `$UP/jobs/batch_00000/READY` exists on each machine
   before going on.
2. Run the one-time setup. It uses sudo for apt: `libvulkan1 libgomp1 vulkan-tools unzip curl`.
   ```bash
   bash "$UP/worker/setup_worker.sh"
   ```
   It installs the Real-ESRGAN ncnn-vulkan binary to `~/ac-upscale/realesrgan/` (outside MEGA on
   purpose), lists Vulkan devices, and upscales a sample image. **Check that the real GPU is listed**,
   not only `llvmpipe` (CPU fallback, very slow). If only llvmpipe shows, install the GPU's Vulkan driver:
   - NVIDIA: the proprietary driver (`ubuntu-drivers autoinstall`, or `nvidia-driver-XXX`) includes Vulkan.
   - AMD: `mesa-vulkan-drivers`.
3. Start the worker in something that survives logout, such as tmux:
   ```bash
   # Osiris
   tmux new -d -s acup "python3 $UP/worker/upscale_worker.py --root $UP --name osiris --shard 0 --shards 2"
   # Dmo-N
   tmux new -d -s acup "python3 $UP/worker/upscale_worker.py --root $UP --name dmo-n --shard 1 --shards 2"
   ```
   Names must be exactly `osiris` and `dmo-n`; the laptop collects from `done/osiris` and `done/dmo-n`.
   Multi-GPU machine: add `--gpu N` to pick one.
4. Watch it with `tmux attach -t acup`. Each finished batch logs a line like
   `batch_00000: 67 textures in 95s (1.42s each)`.

The worker uses only the Python 3 standard library, so there's nothing to pip install.

## Done looks like

- `done/osiris/` has batch_00000, 00002, 00004, 00006 and `done/dmo-n/` has 00001, 00003, 00005, 00007,
  each with 67 PNGs, `result.json` and `COMPLETE`.
- Output PNGs are 4x the input size. Don't resize them; the laptop does the 4x to 2x step.
- Leave both workers running. More batches will be queued later (the full set is about 23,000 textures).

## Please don't

- Don't modify or delete anything in `jobs/`. The laptop cleans up after it has collected results.
- Don't change `upscale_worker.py` in place; it's shared by both machines through MEGA. If it needs a
  fix, describe it in `done/<name>/NOTES.md` and the laptop session will apply it.
- Don't copy game .dat files here or try to pack anything. That happens on the laptop only.

## If something goes wrong

| Symptom | Likely cause |
|---|---|
| `Real-ESRGAN binary not found` | setup_worker.sh didn't finish; rerun it |
| Worker idle, batches present | `READY` not synced yet, or the wrong `--root` path |
| "N/M files synced so far" forever | MEGA stuck; check the MEGAsync client on that machine |
| `FAILED rc=...` | Usually Vulkan or out of GPU memory. Check the stderr in the log; `-t 256` tiling may be needed (worker change, see "Please don't") |
| Very slow, ~30 s/texture or worse | Running on llvmpipe (CPU); fix the Vulkan driver |

Report anything unusual in `done/<name>/NOTES.md`. It syncs back to the laptop.
