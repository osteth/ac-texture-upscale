# GPU worker setup

The upscaling and encoding run on Linux machines with GPUs ("workers"), fed through a synced folder that acts
as a job queue. The Windows machine with the game files ("the laptop" in other docs) extracts textures, queues
batches, and later packs the results. Workers never touch dat files.

Tested with two Ubuntu workers (an RTX 3090 and an RTX A4500) and MEGA as the synced folder. Any sync tool works
if both sides see the same folder.

## Queue layout

```
upscale/
  worker/                    upscale_worker.py, setup_worker.sh, bin/texextract (linux-x64, self-contained)
  jobs/batch_NNNNN/          input PNGs (+ .idx/.pal sidecars, manifest.tsv) and job.json; READY written last
  done/<worker>/batch_NNNNN/ results (.bin for "encode" jobs, else PNGs) and result.json; COMPLETE written last
```

- A worker starts a batch only when `READY` exists and the image and sidecar counts match `job.json`, so
  half-synced batches wait.
- Batches are split by number: `--shard i --shards n` takes batches where `NNNNN % n == i`. Workers never write
  to the same folder, so no locking is needed.
- A batch with `done/<worker>/batch_NNNNN/COMPLETE` is skipped, so restarts are safe.
- Work happens in a local temp folder, so the sync tool never uploads half-written files.
- `"post": "encode"` jobs: after Real-ESRGAN, `texextract encode` resizes to `target_scale`, applies
  back-projection color correction against the batch's original PNGs, and re-encodes to the game's formats.
  The full-size model output is kept in `~/ac-upscale/keep/<tag>/` and reused on re-runs (no GPU needed).

## Set up a worker

1. Find where the synced `upscale/` folder lives on that machine; below it's `$UP`.
2. One-time setup (uses sudo for `libvulkan1 libgomp1 vulkan-tools unzip curl`):
   ```bash
   bash "$UP/worker/setup_worker.sh"
   ```
   This installs Real-ESRGAN ncnn-vulkan to `~/ac-upscale/realesrgan/` and runs a self-test. Make sure your
   real GPU is listed, not only `llvmpipe` (CPU fallback). Install the GPU's Vulkan driver if needed (NVIDIA:
   the proprietary driver; AMD: `mesa-vulkan-drivers`).
3. Start it somewhere that survives logout (tmux, or a systemd user service):
   ```bash
   tmux new -d -s acup "python3 $UP/worker/upscale_worker.py --root $UP --name worker-a --shard 0 --shards 2"
   ```
   Give each worker a unique `--name` and shard. Use `--gpu N` to pick a device; for example, on machines where
   device 0 is an integrated GPU, use `--gpu 1`.
4. Each finished batch logs a line like `batch_00000: 100 textures in 21s (0.21s each), encoded in 9s`.

**Restart workers whenever `upscale_worker.py` or `bin/texextract` changes.** A running worker keeps the old
code in memory. Compare md5 hashes on both sides before queueing.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `Real-ESRGAN binary not found` | `setup_worker.sh` didn't finish; rerun it |
| Worker idle while batches exist | `READY` not synced yet, or the wrong `--root` |
| "N/M ... synced so far" forever | The sync client is stuck, or isn't syncing that folder at all |
| `FAILED rc=...` | Vulkan error or out of GPU memory; check the stderr in the log |
| About 30 s per texture or slower | Running on llvmpipe (CPU); fix the Vulkan driver |
| `vkQueueSubmit failed` | Wrong device (often an integrated GPU); pick another with `--gpu` |
