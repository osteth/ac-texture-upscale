import sys, struct
sys.path.insert(0, sys.argv[1])
import capstone
exe, va = sys.argv[2], int(sys.argv[3], 16)
d = open(exe, 'rb').read()
pe = struct.unpack_from('<I', d, 0x3c)[0]
nsec = struct.unpack_from('<H', d, pe + 6)[0]
optsz = struct.unpack_from('<H', d, pe + 20)[0]
base = struct.unpack_from('<I', d, pe + 24 + 28)[0]
secs = []
for i in range(nsec):
    o = pe + 24 + optsz + i * 40
    name = d[o:o+8].rstrip(b'\0').decode()
    vsz, vaddr, rsz, raw = struct.unpack_from('<IIII', d, o + 8)
    secs.append((name, vaddr, vsz, raw, rsz))
rva = va - base
sec = next(s for s in secs if s[1] <= rva < s[1] + s[2])
off = sec[3] + (rva - sec[1])
# walk back to a likely function start: previous run of int3/nop padding (0xCC) before va
start = off
while start > sec[3] and not (d[start-1] in (0xCC, 0x90) and d[start-2] in (0xCC, 0x90) and d[start-3] in (0xCC, 0x90)): start -= 1
print(f'image base {base:#x}, section {sec[0]}, function likely starts at {base + sec[1] + (start - sec[3]):#x} ({va - (base + sec[1] + (start - sec[3]))} bytes before crash)')
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
code = d[start: off + 48]
for ins in md.disasm(code, base + sec[1] + (start - sec[3])):
    mark = '  <== CRASH' if ins.address == va else ''
    print(f'{ins.address:#010x}: {ins.bytes.hex():<20} {ins.mnemonic} {ins.op_str}{mark}')
    if ins.address > va + 24: break
