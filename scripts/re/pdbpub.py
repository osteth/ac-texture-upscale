"""Minimal MSF 7.0 PDB reader: public symbols (S_PUB32) mapped to virtual addresses."""
import struct, sys, bisect

def read_pdb_publics(path, image_base):
    d = open(path, 'rb').read()
    assert d.startswith(b'Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0')
    bs, fpm, nblocks, dirsize, _, dirmapblk = struct.unpack_from('<6I', d, 32)
    blk = lambda i: d[i*bs:(i+1)*bs]
    ndirblocks = (dirsize + bs - 1) // bs
    dirmap = struct.unpack_from(f'<{ndirblocks}I', blk(dirmapblk))
    directory = b''.join(blk(i) for i in dirmap)[:dirsize]
    nstreams = struct.unpack_from('<I', directory)[0]
    sizes = struct.unpack_from(f'<{nstreams}I', directory, 4)
    pos = 4 + 4*nstreams; streams = []
    for s in sizes:
        n = 0 if s == 0xFFFFFFFF else (s + bs - 1)//bs
        bl = struct.unpack_from(f'<{n}I', directory, pos); pos += 4*n
        streams.append(b''.join(blk(i) for i in bl)[:s] if n else b'')
    dbi = streams[3]
    # DBI header (64 bytes)
    (sig, ver, age, gsi, bld, psi, pdbdll, symrec, rbld, modsz, secconsz, secmapsz, srcsz, tsmsz, mfc, dbgsz, ecsz) = struct.unpack_from('<iIIHHHHHHiiiiiIii', dbi, 0)
    dbg_off = 64 + modsz + secconsz + secmapsz + srcsz + tsmsz + ecsz
    dbgstreams = struct.unpack_from('<11H', dbi, dbg_off)
    sechdr = streams[dbgstreams[5]]  # section header stream
    secs = [struct.unpack_from('<8sIIIIIIHHI', sechdr, i) for i in range(0, len(sechdr), 40)]
    virt = [s[2] for s in secs]  # VirtualAddress per section (1-based segment index)
    rec = streams[symrec]; out = []
    p = 0
    while p + 4 <= len(rec):
        rl, rt = struct.unpack_from('<HH', rec, p)
        if rt == 0x110E:  # S_PUB32
            flags, off, seg = struct.unpack_from('<IIH', rec, p+4)
            name = rec[p+14:rec.index(b'\0', p+14)].decode('latin-1')
            if 1 <= seg <= len(virt): out.append((image_base + virt[seg-1] + off, name))
        p += 2 + rl
    out.sort()
    return out

if __name__ == '__main__':
    pubs = read_pdb_publics(sys.argv[1], 0x400000)
    addrs = [a for a, _ in pubs]
    print(f'{len(pubs)} public symbols')
    for q in sys.argv[2:]:
        va = int(q, 16); i = bisect.bisect_right(addrs, va) - 1
        print(f'{va:#x} -> {pubs[i][1]} + {va - pubs[i][0]:#x}  (next: {pubs[i+1][1]} at {pubs[i+1][0]:#x})')
