import os, sys, struct, bisect
sys.path.insert(0, sys.argv[1]); sys.path.insert(0, sys.argv[2])
import capstone
from pdbpub import read_pdb_publics
pubs = read_pdb_publics(os.environ.get('AC_PDB', r'C:\Users\ostet\ac-decomp\original\acclient.pdb'), 0x400000)
addrs = [a for a, _ in pubs]
def sym(v):
    i = bisect.bisect_right(addrs, v) - 1
    if i < 0: return None
    a, n = pubs[i]; return n if v == a else f'{n}+{v-a:#x}'
d = open(os.environ.get('AC_EXE2013', r'C:\Users\ostet\ac-decomp\original\acclient_2013.exe'), 'rb').read()
pe = struct.unpack_from('<I', d, 0x3c)[0]; osz = struct.unpack_from('<H', d, pe+20)[0]
secs = [struct.unpack_from('<IIII', d, pe+24+osz+i*40+8) for i in range(struct.unpack_from('<H', d, pe+6)[0])]
def off(v):
    for vs, va, rs, raw in secs:
        if va <= v-0x400000 < va+vs: return raw + v-0x400000-va
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
for name in sys.argv[3:]:
    i = [n for _, n in pubs].index(name); start, end = pubs[i][0], pubs[i+1][0]
    print(f'===== {name}  ({start:#x}, {end-start} bytes)')
    for ins in md.disasm(d[off(start):off(end)], start):
        note = ''
        if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'): note = '   ; ' + (sym(int(ins.op_str, 16)) or '')
        elif '[0x' in ins.op_str:
            v = int(ins.op_str.split('[0x')[1].split(']')[0], 16); s = sym(v)
            if s: note = '   ; ' + s
        print(f'{ins.address:#x}: {ins.mnemonic} {ins.op_str}{note}')
