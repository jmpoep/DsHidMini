# Headless Ghidra notes

Binaries are **not** in this repo.

| Binary | Where to get it | SHA-256 of the copy used here |
| --- | --- | --- |
| `ds3cal.dll` x86 + x64 | [rajkosto/ScpToolkit](https://github.com/rajkosto/ScpToolkit) `ScpControl/ds3cal/` | n/a (6 656 bytes each) |
| `sixaxis.sys` x64 | Sony's signed driver (the copy used: 28 424 bytes, 2016-09-28) | `B040B0A3A519D8D43E21A02FB9F2A52300F40F07226E18C4BA4E61C6FC380A51` |

`ds3cal_algorithm.md` is the clean-room write-up. It matches Sony's
`InitialGyroCal` parameter block field-for-field; do not treat the `.decompiled.c`
files (kept only in the private R&D folder) as a source to copy from.

## Reproduce the decompilation

Ghidra 12.1.3 PUBLIC. Headless rejects any path element starting with `.`.

```
analyzeHeadless.bat <abs-proj-dir> <name> -import <abs-binary> -scriptPath <abs-scripts-dir> -postScript ExportDecompiled.java <abs-out.c>
```

`scripts/ExportDecompiled.java` writes every function's C plus exports and defined
data. Interesting `sixaxis.sys` symbols for motion: the Feature `0x01` flag parse,
the `0xEF` page-`0xA0` read, the gain-113 accel loop, and the three gyro paths.
