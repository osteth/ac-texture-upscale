"""Queue extracted textures as upscale batches in the MEGA-synced job folder.

Each batch gets its files and job.json first, then READY last, so a worker never starts a half-synced batch.
Batch numbers continue after the highest existing batch, so queues from different runs never collide.

With --manifest, each batch also gets its slice of the manifest plus the .idx/.pal palette sidecars, and the
job is marked "post": "encode": the worker resizes to --target-scale and returns game-ready .bin files.

usage: queue_jobs.py SRC_DIR UPSCALE_ROOT TAG [--size N] [--manifest TSV] [--target-scale 2]
"""
import argparse
import json
import os
import shutil

ap = argparse.ArgumentParser()
ap.add_argument("src")
ap.add_argument("root")
ap.add_argument("tag")
ap.add_argument("--size", type=int, default=67)
ap.add_argument("--manifest")
ap.add_argument("--model", default="realesrgan-x4plus")
ap.add_argument("--scale", type=int, default=4)
ap.add_argument("--target-scale", type=int, default=2)
a = ap.parse_args()

jobs = os.path.join(a.root, "jobs")
os.makedirs(jobs, exist_ok=True)
existing = [int(b[6:]) for b in os.listdir(jobs) if b.startswith("batch_")]
start = max(existing) + 1 if existing else 0

if a.manifest:
    lines = open(a.manifest, encoding="utf-8").read().splitlines()
    header, rows = lines[0], [l for l in lines[1:] if l.strip()]
    files = [r.split("\t")[0] for r in rows]
else:
    header, rows = None, None
    files = sorted(f for f in os.listdir(a.src) if f.endswith(".png"))

total_sidecars = 0
for n, i in enumerate(range(0, len(files), a.size)):
    chunk = files[i:i + a.size]
    d = os.path.join(jobs, f"batch_{start + n:05d}")
    os.makedirs(d)
    sidecars = 0
    for f in chunk:
        shutil.copy2(os.path.join(a.src, f), d)
        for ext in (".idx", ".pal"):
            side = os.path.join(a.src, f[:-4] + ext)
            if a.manifest and os.path.exists(side):
                shutil.copy2(side, d)
                sidecars += 1
    job = {"tag": a.tag, "model": a.model, "scale": a.scale, "count": len(chunk)}
    if a.manifest:
        with open(os.path.join(d, "manifest.tsv"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join([header] + rows[i:i + a.size]) + "\n")
        job.update({"post": "encode", "target_scale": a.target_scale, "sidecars": sidecars, "keep_full": True})
    with open(os.path.join(d, "job.json"), "w") as fh:
        json.dump(job, fh, indent=1)
    open(os.path.join(d, "READY"), "w").close()
    total_sidecars += sidecars
batches = (len(files) + a.size - 1) // a.size
print(f"queued {len(files)} textures ({total_sidecars} sidecars) in {batches} batches "
      f"batch_{start:05d}..batch_{start + batches - 1:05d} ({a.tag}, {a.model} x{a.scale}"
      + (f" -> encode x{a.target_scale}" if a.manifest else "") + ")")
