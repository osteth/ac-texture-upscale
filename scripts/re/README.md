# Client crash-site tools

Used to turn an `acclient.exe` crash offset into a function name (see "Terrain: the crash chain" in
`docs/LESSONS-LEARNED.md`). Needs `pip install capstone` (any 5.x); put it on `sys.path` via the first argument.

- `disasm.py <capstone_dir> <exe> <hexVA>`: disassemble the function around a virtual address in any client
  build. Use the address from Windows' "exception address" (Event Viewer, .NET Runtime 1026), or image base
  `0x400000` + the "Fault offset".
- `pdbpub.py <pdb> <hexVA>...`: minimal MSF 7.0 PDB reader that maps `S_PUB32` public symbols to addresses and
  names the function containing each VA.
- `dis2013.py <capstone_dir> <this_dir> <symbol>...`: disassemble whole 2013 functions by decorated name, with
  call targets labeled from the PDB.
- `callers.py <capstone_dir> <this_dir> <symbol>...`: list direct callers of a function in the 2013 build.

The live client (2015 build) has no matching PDB. Find the crash bytes in the 2013 build
(`acclient_2013.exe`, which matches the shipped `acclient.pdb`), then name them there. Paths default to the
original workspace; override with the `AC_PDB` and `AC_EXE2013` environment variables.
