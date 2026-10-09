import os, sys, struct, bisect
sys.path.insert(0, sys.argv[1]); sys.path.insert(0, sys.argv[2])
from pdbpub import read_pdb_publics
pubs = read_pdb_publics(os.environ.get('AC_PDB', r'C:\Users\ostet\ac-decomp\original\acclient.pdb'), 0x400000)
addrs = [a for a, _ in pubs]; byname = {n: a for a, n in pubs}
def name_at(va):
    i = bisect.bisect_right(addrs, va) - 1; return f'{pubs[i][1]}+{va - pubs[i][0]:#x}'
d = open(os.environ.get('AC_EXE2013', r'C:\Users\ostet\ac-decomp\original\acclient_2013.exe'), 'rb').read()
pe = struct.unpack_from('<I', d, 0x3c)[0]; osz = struct.unpack_from('<H', d, pe+20)[0]
vs, va, rs, raw = struct.unpack_from('<IIII', d, pe+24+osz+8)
for target in sys.argv[3:]:
    t = byname[target]
    print(f'== callers of {target} @ {t:#x}')
    for i in range(raw, raw + rs - 5):
        if d[i] == 0xE8:
            src = 0x400000 + va + (i - raw)
            if (src + 5 + struct.unpack_from('<i', d, i+1)[0]) & 0xFFFFFFFF == t: print(f'   {src:#x}  in {name_at(src)}')
