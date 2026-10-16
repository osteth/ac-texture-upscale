"""Build the player downloads: the AC-HD zip (kit + all four HD dats) and the AC-Retail-DATs zip.

Every dat is checked before it is packed: HD dats against release/SHA256SUMS.txt, retail portal/cell/local against
the end-of-retail hashes below, and the retail highres must not be the HD one. The retail zip is skipped when
--retail isn't given.

usage: build_release.py --hd HD_DAT_DIR [--retail RETAIL_DAT_DIR] --version v1.0.1 --out OUT_DIR
"""
import argparse
import hashlib
import os
import sys
import zipfile

DATS = ["client_portal.dat", "client_highres.dat", "client_cell_1.dat", "client_local_English.dat"]
# End-of-retail (portal iteration 2072). Cell and local are the same files in both packs.
RETAIL_SHA = {
    "client_portal.dat": "dc6e500ba22e6b186db7171e3f3345238b6444c85d798adc85e550973b8d12e4",
    "client_cell_1.dat": "6db0abf00fbceed62c3f1ee842ee7c1f423d732bed77a5b7c102ee89a52ab99e",
    "client_local_English.dat": "e85c820280c88fac7df6c8043f5e24596e9c8774193af4123d756546f78fb2bb",
}
KIT = ["README.txt", "SHA256SUMS.txt"]
here = os.path.dirname(os.path.abspath(__file__))
release = os.path.join(os.path.dirname(here), "release")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def write_zip(path, entries):
    # entries: [(archive name, source path)]. Dats compress to about half; deflate level 6 is the usual tradeoff.
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for name, src in entries:
            print(f"  + {name}")
            z.write(src, name)
    print(f"{os.path.basename(path)}: {os.path.getsize(path) / 2**20:.0f} MB, sha256 {sha256(path)}")


ap = argparse.ArgumentParser()
ap.add_argument("--hd", required=True)
ap.add_argument("--retail")
ap.add_argument("--version", required=True)
ap.add_argument("--out", required=True)
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)

hd_sums = {}
for line in open(os.path.join(release, "SHA256SUMS.txt"), encoding="utf-8"):
    h, name = line.rstrip("\n").split("  ", 1)
    hd_sums[os.path.basename(name)] = h
for d in DATS:
    got = sha256(os.path.join(a.hd, d))
    if got != hd_sums[d]:
        sys.exit(f"HD {d}: sha256 {got} doesn't match release/SHA256SUMS.txt")
print("HD dats match release/SHA256SUMS.txt")
write_zip(os.path.join(a.out, f"AC-HD-Textures-{a.version}.zip"),
          [(k, os.path.join(release, k)) for k in KIT] + [(f"HD DATs/{d}", os.path.join(a.hd, d)) for d in DATS])

if a.retail:
    sums = []
    for d in DATS:
        got = sha256(os.path.join(a.retail, d))
        if d in RETAIL_SHA and got != RETAIL_SHA[d]:
            sys.exit(f"retail {d}: sha256 {got} is not the end-of-retail file")
        if d == "client_highres.dat" and got == hd_sums[d]:
            sys.exit("retail client_highres.dat is the HD version")
        sums.append(f"{got}  Retail DATs/{d}")
    sums_path = os.path.join(release, "retail", "SHA256SUMS.txt")
    with open(sums_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(sums) + "\n")
    print(f"retail dats verified; wrote {sums_path}")
    write_zip(os.path.join(a.out, "AC-Retail-DATs.zip"),
              [("README.txt", os.path.join(release, "retail", "README.txt")), ("SHA256SUMS.txt", sums_path)]
              + [(f"Retail DATs/{d}", os.path.join(a.retail, d)) for d in DATS])
