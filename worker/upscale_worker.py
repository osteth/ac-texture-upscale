#!/usr/bin/env python3
"""GPU upscale worker for the AC texture pipeline. Stdlib only.

Queue layout under --root (a MEGA-synced folder shared with the laptop):
  jobs/batch_NNNNN/        *.png inputs + job.json, then READY written last by the laptop
                           ("post": "encode" jobs also carry manifest.tsv and .idx/.pal palette sidecars)
  done/<name>/batch_NNNNN/ upscaled *.png (or encoded *.bin for "encode" jobs) + result.json, then COMPLETE

Each worker owns the batches where NNNNN % shards == shard, so two machines never touch the
same batch and never write to the same folder (sync services make file locks unreliable).
"""
import argparse
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time


def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def pending_batches(root, name, shard, shards):
    jobs = os.path.join(root, "jobs")
    if not os.path.isdir(jobs):
        return []
    out = []
    for b in sorted(os.listdir(jobs)):
        if not b.startswith("batch_") or int(b[6:]) % shards != shard:
            continue
        if not os.path.exists(os.path.join(jobs, b, "READY")):
            continue  # laptop hasn't finished writing it (or MEGA hasn't finished syncing it)
        if os.path.exists(os.path.join(root, "done", name, b, "COMPLETE")):
            continue
        out.append(b)
    return out


def run_batch(root, name, batch, binary, gpu):
    src = os.path.join(root, "jobs", batch)
    with open(os.path.join(src, "job.json")) as f:
        job = json.load(f)
    pngs = [p for p in os.listdir(src) if p.endswith(".png")]
    sidecars = [p for p in os.listdir(src) if p.endswith((".idx", ".pal"))]
    if len(pngs) != job["count"] or len(sidecars) != job.get("sidecars", 0):
        log(f"{batch}: {len(pngs)}/{job['count']} images, {len(sidecars)}/{job.get('sidecars', 0)} sidecars synced so far, retrying later")
        return False
    encode = job.get("post") == "encode"
    if encode and not os.path.exists(os.path.join(src, "manifest.tsv")):
        log(f"{batch}: manifest.tsv not synced yet, retrying later")
        return False

    if encode and not os.path.exists(encoder):
        log(f"{batch}: encoder missing at {encoder} (still syncing?), retrying later")
        return False

    # Work on local disk, not inside the synced folder, so MEGA never uploads half-written files.
    with tempfile.TemporaryDirectory(prefix="acup_") as tmp:
        tin, tout = os.path.join(tmp, "in"), os.path.join(tmp, "out")
        os.makedirs(tin)
        os.makedirs(tout)
        for p in pngs:
            shutil.copy2(os.path.join(src, p), tin)
        start = time.time()
        cmd = [binary, "-i", tin, "-o", tout, "-n", job["model"], "-s", str(job["scale"]), "-f", "png"]
        if gpu is not None:
            cmd += ["-g", str(gpu)]
        proc = subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
        elapsed = time.time() - start
        produced = sorted(p for p in os.listdir(tout) if p.endswith(".png"))
        missing = sorted(set(pngs) - set(produced))
        if proc.returncode != 0 or missing:
            log(f"{batch}: FAILED rc={proc.returncode} missing={len(missing)}\n{proc.stderr[-2000:]}")
            return False

        dst = os.path.join(root, "done", name, batch)
        os.makedirs(dst, exist_ok=True)
        encode_seconds = None
        if encode:
            # Resize to the target scale and re-encode to the game's pixel formats; only the small .bin files
            # go back through MEGA. The full-size model output stays on this machine's disk.
            tbin = os.path.join(tmp, "bin")
            os.chmod(encoder, 0o755)  # MEGA does not preserve the executable bit
            t0 = time.time()
            proc = subprocess.run([encoder, "encode", src, tout, tbin, str(job.get("target_scale", 2))],
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            encode_seconds = round(time.time() - t0, 1)
            if proc.returncode != 0:
                log(f"{batch}: ENCODE FAILED rc={proc.returncode}\n{proc.stdout[-500:]}\n{proc.stderr[-2000:]}")
                return False
            for p in os.listdir(tbin):
                shutil.copy2(os.path.join(tbin, p), dst)
            if job.get("keep_full", True):
                keep = os.path.join(keep_root, job.get("tag", "untagged"))
                os.makedirs(keep, exist_ok=True)
                for p in produced:
                    shutil.move(os.path.join(tout, p), os.path.join(keep, p))
        else:
            for p in produced:
                shutil.copy2(os.path.join(tout, p), dst)
    with open(os.path.join(dst, "result.json"), "w") as f:
        json.dump({"worker": name, "host": socket.gethostname(), "count": len(produced),
                   "seconds": round(elapsed, 1), "encode_seconds": encode_seconds,
                   "model": job["model"], "scale": job["scale"], "post": job.get("post")}, f, indent=1)
    with open(os.path.join(dst, "COMPLETE"), "w") as f:
        f.write(time.strftime("%Y-%m-%dT%H:%M:%S"))
    log(f"{batch}: {len(produced)} textures in {elapsed:.0f}s ({elapsed / max(len(produced), 1):.2f}s each)"
        + (f", encoded in {encode_seconds:.0f}s" if encode_seconds is not None else ""))
    return True


encoder = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bin", "texextract")
keep_root = os.path.expanduser("~/ac-upscale/keep")


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.path.dirname(here), help="synced upscale/ folder")
    ap.add_argument("--name", default=socket.gethostname(), help="worker name, used for its done/ folder")
    ap.add_argument("--shard", type=int, required=True, help="this worker's shard index (0-based)")
    ap.add_argument("--shards", type=int, required=True, help="total number of workers")
    ap.add_argument("--binary", default=os.path.expanduser("~/ac-upscale/realesrgan/realesrgan-ncnn-vulkan"))
    ap.add_argument("--gpu", type=int, default=None, help="GPU index for multi-GPU machines")
    ap.add_argument("--poll", type=int, default=30, help="seconds between queue scans")
    ap.add_argument("--once", action="store_true", help="process what's ready, then exit")
    ap.add_argument("--encoder", default=None, help="override path to the texextract encoder")
    a = ap.parse_args()
    global encoder
    if a.encoder:
        encoder = a.encoder

    if not os.access(a.binary, os.X_OK):
        sys.exit(f"Real-ESRGAN binary not found at {a.binary}; run setup_worker.sh first")
    log(f"worker {a.name} shard {a.shard}/{a.shards} watching {a.root}")
    while True:
        for b in pending_batches(a.root, a.name, a.shard, a.shards):
            run_batch(a.root, a.name, b, a.binary, a.gpu)
        if a.once:
            break
        time.sleep(a.poll)


if __name__ == "__main__":
    main()
