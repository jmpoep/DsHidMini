# DualShock 3 / SIXAXIS motion sensors

Research notes for [issue #217](https://github.com/nefarius/DsHidMini/issues/217)
(motion support). This is the foundation for a later driver implementation;
**nothing in here is implemented in DsHidMini yet** beyond the SIXAXIS.SYS
byte-swap described in [What DsHidMini does today](#what-dshidmini-does-today).

Sources of truth used, in order of authority:

1. Six PS3-console-to-pad USB link-layer captures
   ([CircumSpector/Research, "Sony DualShock 3"](https://github.com/CircumSpector/Research/tree/master/Sony%20DualShock%203)):
   three DualShock 3 pads on CECHZC2E-A1, CECHZC2E-B1 and CECHZC2U-A2 consoles
   plus one original SIXAXIS, dissected with a local `tshark` (recipe in
   [`PS3_USB_STARTUP.md`](PS3_USB_STARTUP.md#tshark-recipe)).
2. A clean-room decompilation (Ghidra 12.1.3, headless) of rajkosto's
   closed-source `ds3cal.dll` (x86 and x64 builds are identical) from the
   [rajkosto/ScpToolkit fork](https://github.com/rajkosto/ScpToolkit/tree/master/ScpControl/ds3cal),
   which is the only known re-implementation of Sony's gyro auto-zero logic.
3. Live dumps from an aftermarket ("clone") DS3 bound to WinUSB via Zadig and
   read with a throwaway probe (see [Tooling](#tooling)). Genuine DS3 and
   SIXAXIS live dumps are still pending, see [Open questions](#open-questions).
4. Public references: RPCS3 `ds3_pad_handler.cpp`,
   [psdevwiki DualShock 3](https://www.psdevwiki.com/ps3/DualShock_3),
   [mclab SIXAXIS notes](https://mclab.uunyan.com/lab/sixaxis/sxs004.htm),
   [lewy20041/DS3_Input_And_Report_Inspector](https://github.com/lewy20041/DS3_Input_And_Report_Inspector).

## Raw sensor bytes in the input report

`DS3_RAW_INPUT_REPORT` (`include/DsHidMini/Ds3Types.h`), offsets counted with the
report ID `0x01` at byte 0 (49-byte USB / BTH HID report):

| Bytes | Field | Notes |
| --- | --- | --- |
| 41-42 | `AccelerometerX` | **big-endian** u16, 10-bit payload (0-1023) |
| 43-44 | `AccelerometerY` | big-endian u16 |
| 45-46 | `AccelerometerZ` | big-endian u16 |
| 47-48 | `Gyroscope` | big-endian u16, **yaw only** (rotation about the axis perpendicular to the face of the pad) |

All four values idle near 512. The pad has no pitch/roll gyro; only one axis.

lewy20041's inspector reads these little-endian, which is why it needs an odd
35583 LSB/g constant; his `0xEF` offsets are also shifted by one byte (see
below). Treat that tool's numbers as unverified.

### Observed idle values

From the PS3 captures (pads being handled on a desk, so X/Y are not perfectly
level) and from the clone lying flat:

| Pad | X | Y | Z | Gyro | Reports |
| --- | --- | --- | --- | --- | --- |
| DS3 on CECHZC2E-A1 | 511 | 491 | 398 | 487 | 1853 |
| DS3 on CECHZC2E-A1 (rumble test, 60 s) | 495 | 532 | 398 | 487 | 5978 |
| DS3 on CECHZC2E-B1 | 498 | 563 | 424 | 494 | 1837 |
| DS3 on CECHZC2U-A2 | 505 | 553 | 408 | 386 | 1129 |
| SIXAXIS | 505 | 554 | 469 | 758 (very noisy, 2-997) | 1199 |
| Clone, flat on desk (WinUSB probe) | 513 | 500 | 383 | 500 (constant) | 1001 |

Flat on the desk the pad reads **Z ~ 1 g below the zero-g value**
(398 vs 511 on the A1 pad, 383 vs 512 on the clone), i.e. Sony's Z axis points
*down* through the pad. X and Y sit at their zero-g values when level.
The exact sign of each axis for the other five orientations still has to be
measured (probe `--interactive`).

## Identification: `GET_REPORT Feature 0x01`

64 bytes requested, layout (offsets into the returned buffer, which starts
with `00 01`):

| Offset | Meaning | Values seen |
| --- | --- | --- |
| 2-4 | firmware/board revision bytes; the same three bytes are echoed at offset 2-4 of every `0xEF` answer | A1 `04 00 08`, B1 `04 01 03`, A2 `04 00 0b`, SIXAXIS `02 00 03`, clone `03 00 05` |
| 8-11 | sensor/pad type, four identical bytes | `18 18 18 18` DualShock 3, `17 17 17 17` SIXAXIS, clone `18 18 18 18` |
| 0x25 | number of calibration field IDs that follow | 4 / 3 / 4 / 1 / 2 |
| 0x26.. | calibration field IDs | A1+A2 `00 01 02 07`, B1 `00 01 02`, SIXAXIS `06`, clone `01 02` |

Field ID `0x07` is the important one: pads that list it (and every SIXAXIS,
which lists `0x06` instead) accept a **gyro calibration byte in the output
report** (see [Gyro](#gyroscope)). Pads without it (B1 sample, clone) get no
cal bytes from the PS3 and are only software-zeroed. rajkosto's
`UsbDs3.cs` encodes exactly this decision tree.

## Calibration EEPROM: `Feature 0xEF`, `0xF8`, `0xF7`

### Page select and read

The PS3 reads two 16-byte EEPROM pages right after `0xF2`/`0xF5` and before the
first output report (step 6 of the startup sequence in
[`PS3_USB_STARTUP.md`](PS3_USB_STARTUP.md#sequence)):

```
SET_REPORT Feature 0xEF, 48 bytes:  00 00 00 00 03 01 A0 00 ... 00     ; select page 0xA0
GET_REPORT Feature 0xEF, 64 bytes:  00 EF vv vv vv 03 01 A0 00 00 00 00 00 00 00 00 00 <16 page bytes> 00 ... 05 00 ...
SET_REPORT Feature 0xEF:            00 00 00 00 03 01 B0 00 ... 00     ; select page 0xB0
GET_REPORT Feature 0xEF:            00 EF vv vv vv 03 01 B0 ... <16 page bytes> ...
GET_REPORT Feature 0xF8, 64 bytes
```

- Request bytes 4-6 are `03 01 <page>`; the reply echoes them at offsets 5-7
  and the page payload starts at **offset 0x11 (17)**, 16 bytes long.
  `vv vv vv` are the revision bytes from `Feature 0x01`. Offset 0x30 is `05`
  on genuine pads and `04` on the clone (meaning unknown).
- Page addresses step in units of `0x10`. The clone answers every page from
  `0x00` to `0xF0`; only `0x70`, `0x80`, `0x90`, `0xA0` and `0xB0` are
  non-zero. Whether genuine pads expose more than `0xA0`/`0xB0` is untested.
- Without a preceding page select, the clone returns page `0xA0`. Genuine
  behaviour untested (the PS3 never does a plain read).
- `Feature 0xF8` returns whatever is in the same 64-byte device buffer with
  the header replaced by `00 01 00 00` (A1, B1, SIXAXIS all return the last
  `0xEF` answer). The A2 console/pad and the clone return all zeros. It
  carries no extra information in any capture.
- `Feature 0xF7` (read once after the first output report) varies per pad and
  per plug-in; bytes 2-6 look like live sensor/ADC readings
  (`7f 02 ce 01 f1`, `1e 02 fa 01 01`, `fe 02 f8 01 ef`) followed by `ff 14 33`
  on DS3-class pads, all zero on the A2 sample and mostly zero on the SIXAXIS
  (`0a 01 ea` at 12-14). Purpose unknown; not needed for motion.

### Page `0xA0`: sensor calibration

Eight **big-endian** u16 values, four pairs `(zero, oneG)`:

| Page offset | Buffer offset | Field |
| --- | --- | --- |
| 0-1 / 2-3 | 0x11 / 0x13 | accel X: reading at 0 g / reading at -1 g |
| 4-5 / 6-7 | 0x15 / 0x17 | accel Y: same |
| 8-9 / 10-11 | 0x19 / 0x1B | accel Z: same |
| 12-13 / 14-15 | 0x1D / 0x1F | gyro: raw reading at rest / factory **cal byte** (0-255) |

Observed:

| Pad | X | Y | Z | Gyro | Page `0xB0` (first 2 u16) |
| --- | --- | --- | --- | --- | --- |
| DS3 (A1 capture) | 508 / 397 (111) | 503 / 387 (116) | 511 / 396 (115) | 521 / **0x77** | 620 / 610 |
| DS3 (B1 capture) | 508 / 395 (113) | 507 / 398 (109) | 513 / 402 (111) | 488 / **0** | 624 / 615 |
| DS3 (A2 capture) | 507 / 395 (112) | 510 / 397 (113) | 511 / 400 (111) | 521 / **0x7F** | 620 / 622 |
| SIXAXIS | 521 / 411 (110) | 518 / 404 (114) | 499 / 389 (110) | 513 / **0x75** | 637 / 632 |
| Clone | 512 / 384 (128) | 512 / 384 (128) | 512 / 384 (128) | 512 / 0 | 640 / 640 |

Numbers in parentheses are `zero - oneG`, i.e. counts per g: **~113 on genuine
hardware**, a suspiciously round 128 on the clone (its "EEPROM" is a fixed
template, but its accelerometer really does read 383 flat, so the template is
consistent with the firmware's scaling).

The rest of page `0xB0` is zero on every pad. Its two values (~620-640) are
unexplained; candidates are gyro sensitivity or temperature compensation. The
clone's pages `0x70`/`0x80`/`0x90` contain what look like stick min/max
(`00 5c 03 a4` = 92 / 932 repeated per axis) and other trim data; not
relevant for motion.

Note on lewy20041's parsing: he reads little-endian u16 at buffer offsets 20,
22, ..., 32. Offset 20-21 happens to be the low byte of X.oneG followed by the
high byte of Y.zero, which decodes "correctly" only by accident for the values
seen here, and his "gyro offset" at 32-33 is really the cal byte. Use the
layout above.

## Accelerometer calibration

Sony's pad library (partially recovered by RPCS3 as the commented-out
`polish_value` in `ds3_pad_handler.cpp`) and rajkosto's `UsbDs3.cs` agree on
the shape of the formula:

```c
// raw: big-endian 10-bit value from the input report
// zero, oneG: the two EEPROM u16 for that axis (page 0xA0)
// gain: 113 in ScpToolkit for all axes; RPCS3's notes list -226 (X), 226 (Y), 113 (Z)
cal = gain * ((raw - zero) * 1024 / (zero - oneG)) / 1024 + 512;
cal = clamp(cal, 0, 1023);
```

So the calibrated scale is **113 counts per g centred on 512** (0 g = 512,
+1 g = 625, -1 g = 399). Lying flat, Z therefore reads ~399 on a genuine pad
and on the clone alike, which the probe confirms (`cal Z 399` for raw 383 with
512/384). The `zero - oneG` divisor is the "unknown `dword_0x0`" RPCS3 was
missing. Whether the PS3 really applies 2x gain and a sign flip on X/Y (the
226/-226 from RPCS3's disassembly) or 113 like ScpToolkit is an open question;
the sign flip on X at least matches what `sixaxis.sys` does on Windows (see
below).

For a **DS4-style** output (DS4Windows mode) the natural conversion is
`accel_ds4 = (raw - zero) * 8192 / (zero - oneG)` per axis (DS4: 8192 LSB/g),
with the axis permutation/sign still to be settled by the six-orientation
measurement.

## Gyroscope

### Hardware cal byte

The gyro has a hardware zero-rate trim. The host sends a **cal byte** in the
output report and the pad shifts its raw yaw reading by about **26.4 counts
per cal-byte step** (`0x6999 / 1024`, constant from `ds3cal.dll`; not yet
confirmed on real hardware, see the probe's `--calbyte` experiment):

| Pad class | Cal bytes in the 48-byte EP0 output report (no report ID) | With report ID (interrupt OUT) |
| --- | --- | --- |
| DualShock 3 with field `0x07` | `[5] = 0xFF, [6] = calByte` | bytes 6-7 |
| SIXAXIS (`0x17`) | `[3] = 0xFF, [4] = calByte` | bytes 4-5 (overlaps the big-motor slot, which a SIXAXIS does not have) |
| DS3 without field `0x07` (B1, clone) | none | none |

The PS3 captures show exactly this: `ff 77` (A1 pad, `Gyro.oneG = 0x77`),
`ff 7f` (A2 pad, `0x7F`), `ff 75` at bytes 3-4 for the SIXAXIS (`0x75`), and
no cal bytes for the B1 pad whose EEPROM has `0`. In all four captures the
cal byte equals the factory value from page `0xA0` and never changes during the
session (up to 60 s observed), so the initial cal is simply "send the EEPROM
byte back". This refines the note in
[`PS3_USB_STARTUP.md`](PS3_USB_STARTUP.md#output-byte-semantics-from-the-consoles-own-traffic).

Sending `0x00 0x00` there (what DsHidMini does today) is what the PS3 does for
the B1 class, so it is safe for every pad; it just leaves the gyro on its
untrimmed zero.

### Auto-zero algorithm (`ds3cal.dll`, clean-room)

`ds3cal.dll` exports `GyroCalCreate/Destroy` (196-byte state on the process
heap), `InitialGyroCal(u16 eepromCal, u16 eepromZero, u8* calByte, state)`,
`RuntimeGyroCal(u16 raw, u16* gyroOut, u8* calByte, state)` (returns 0 **when
the cal byte changed**, -1 otherwise) and `GyroCalStore/Load` (`0xDABE` magic
+ state copy, 0xBC bytes). ScpToolkit calls `InitialGyroCal(Gyro.oneG,
Gyro.zero, ...)`, i.e. `eepromCal` is the cal byte and `eepromZero` the raw
reading it produces at rest.

Constants: target 512; step 0x6999 (Q10, 26.4 counts per cal step); settle
32 samples initially and 2 after each later cal change; "moving" if raw > 819
or raw < 204; 16-sample blocks; block is quiet if
`(max-avg)^2 + (avg-min)^2 < 10`; ring of the last 4 quiet block averages,
"at rest" when their range < 4; long-term average over 236 blocks (~38 s at
100 Hz) as a fallback; pending-cal tolerance 100 decaying by 1 per sample.

```c
#define Q10(x) (((x) + (((x) >> 31) & 0x3FF)) >> 10)   // /1024 rounding toward zero

// Decide whether an at-rest average needs a cal-byte change.
bool Retarget(int restAvg, int* zeroRef, int* calByte)
{
    int delta = (512 - restAvg) * 1024;
    if (-0x6999 <= delta && delta <= 0x6999) { *zeroRef = restAvg; return false; }  // < 1 step: software zero only
    int steps = delta / 0x6999;
    *zeroRef = restAvg + Q10(steps * 0x6999);   // predicted raw at rest after the pad applies the new byte
    *calByte += steps;
    return true;
}

InitialGyroCal: calByte = eepromCal; Retarget(eepromZero, &zeroRef, &calByte);
                output = clamp(zeroRef - eepromZero + 512, 0, 1023); settle = 32;

RuntimeGyroCal(raw):
    if (settle--) { lastRaw = raw; return previous output; }
    output = clamp(512 - raw + zeroRef, 0, 1023);        // NOTE the sign flip: 512 = still
    changed = Track(raw);
    if (pending) {                                        // a new cal byte was sent, wait for the pad to apply it
        if (|raw - lastRaw| <= pendingTol) pendingTol -= 1;
        else { apply pending zeroRef/calByte; reset block stats; changed = true; }
    }
    lastRaw = raw;

Track(raw):
    if (raw > 819 || raw < 204) moving = true;
    accumulate 16-sample block (sum/min/max); when full:
        blockAvg = round(sum/16); var = (max-avg)^2 + (avg-min)^2
        every 236 blocks: longAvg = round(sum/236);
            if Retarget(longAvg) needs a step -> store as pending (tolerance 100) else zeroRef = longAvg
        if (moving) { moving = false; return false; }     // discard this block
        if (var < 10) {
            push blockAvg into 4-entry ring; if ring full and range(ring) < 4:
                restAvg = round(ringSum/4); reset long-term;
                changed = Retarget(restAvg, &zeroRef, &calByte);
                if (changed) { reset ring; settle = 2; }
                pending = false; return changed;
            if (pending) { apply pending; reset ring; settle = 2; return true; }
        }
    return false;
```

A full C# re-implementation used to validate this against hardware lives in the
R&D folder (`probe/GyroCal.cs`, see [Tooling](#tooling)). On the clone, whose
gyro reads a constant 500, the tracker converged to `zeroRef = 500` after four
quiet blocks and emitted 512 - exactly as designed.

### Units

The gyro's counts-per-degree-per-second are **not** in the EEPROM and not in
`ds3cal.dll` (which only zeroes). RPCS3 passes the value through with gain 1.
For a DS4-style output (16 LSB per deg/s) a scale factor has to be measured:
integrate `(raw - zeroRef)` over a controlled 90 degree yaw turn recorded with
the probe's CSV, or read it off the mclab/psdevwiki notes if they turn out to
have it. Page `0xB0` (620-640 range) is a candidate carrier of this
information and should be compared across more pads.

## What `sixaxis.sys` / RPCS3 expect

RPCS3's Windows path (`#ifdef _WIN32`) takes the sensor fields from the
`sixaxis.sys` HID report as-is and notes that Sony's Windows driver already
"does the same modification of this value as the PS3", i.e. X arrives with
the PS3 sign convention. Its Linux path (raw hidraw) flips X manually:
`512 - (accel_x - 512)`. Gyro is passed through unchanged.

## What DsHidMini does today

- **SIXAXIS.SYS-compatible mode** (`driver/DsHidMiniDrv.c`, `DSHM_ProcessHidInputReport`):
  `X = 0x3FF - swap16(X)`, `Y = swap16(Y)`, `Z = swap16(Z)`, `G = swap16(G)` -
  endianness fixed, X mirrored, **no calibration**, no cal byte. This matches
  what RPCS3 expects structurally; values are raw (zero ~ EEPROM `zero`, not 512).
- **DS4Windows-compatible mode** (`driver/DsHid.c`, `DS3_RAW_TO_DS4WINDOWS_HID_INPUT_REPORT`):
  gyro/accel bytes are left zero.
- Output report bytes 6-7 (with ID) are always `00 00`; equivalent to the PS3's
  behaviour for pads without calibration field `0x07`.
- `0xEF`/`0xF8`/`0xF7` are never sent (documented in `PS3_USB_STARTUP.md`).

## Sketch for a future implementation (not part of this pass)

1. During USB start (after `0xF2`/`0xF5`, before the first output report,
   mirroring the PS3), do `SET 0xEF page A0` + `GET 0xEF`, parse the four pairs,
   keep them in the device context. Over Bluetooth the same feature reports
   go through the BthPS3 HID control channel (untested).
2. Read `Feature 0x01` offsets 8 and 0x25/0x26.. to decide whether to place a
   cal byte at output bytes 6-7 (DS3) or 4-5 (SIXAXIS); seed it with
   `Gyro.oneG` (that is all the PS3 does).
3. Per input report: accel `cal = 113 * (raw - zero) * 1024 / (zero - oneG) / 1024 + 512`
   for the SIXAXIS.SYS feature report; gyro `512 + zeroRef - raw` with the
   auto-zero tracker above (optional; the plain EEPROM zero is what the PS3 uses
   for the first ~38 s anyway).
4. DS4Windows mode: `accel_ds4 = (raw - zero) * 8192 / (zero - oneG)` with
   axis mapping from the orientation test; gyro yaw scaled by the measured
   deg/s factor into the DS4 gyro-Y slot, pitch/roll zero.
5. Clone handling: if `0xEF` fails or `zero == oneG`, fall back to
   `zero = 512, oneG = 512 - 113` (or 384 for the 128-count clone class) and
   skip the gyro entirely if it is constant.

## Open questions

- Six-orientation sign table for X/Y/Z and yaw direction on genuine DS3 and
  SIXAXIS (probe `--interactive`; needs a Zadig swap per controller).
- Confirm the 26.4 counts/step cal-byte sensitivity on a SIXAXIS and a
  field-`0x07` DS3 (probe `--calbyte`), and whether a DS3 without field `0x07`
  ignores the bytes.
- Gyro deg/s scale; meaning of page `0xB0`; whether genuine pads answer pages
  other than `0xA0`/`0xB0`.
- 113 vs 226 gain on X/Y in Sony's own pad library.
- Does `0xEF` work over Bluetooth (BthPS3 control channel)?
- Do other clone families (Defender BT in DS3 mode, ShanWan) answer `0xEF`
  and with what?

## Tooling

Everything binary or throwaway lives outside the repository in
`D:\FOSS\DsHidMini-motion-rnd\` (not committed):

- `pcap/Extract-HidControl.ps1` - `tshark`-based extractor that reassembles
  every HID class control transfer (setup + data stage) from the USB
  link-layer pcaps; per-capture `.txt` outputs and `resting_sensor_stats.txt`
  next to it.
- `ghidra/` - `ds3cal.dll` x86/x64, headless project, `ExportDecompiled.java`
  post-script, decompiler output (`ds3cal_*.decompiled.c`) and
  `ds3cal_algorithm.md` with the annotated state layout.
  Run: `analyzeHeadless.bat <proj> ds3cal -import ds3cal.dll -postScript ExportDecompiled.java <out.c>`.
- `probe/` - .NET 10 console on `Nefarius.Drivers.WinUSB` for a Zadig/WinUSB-bound
  `054C:0268`: dumps `0x01/0xF2/0xF5/0xF7/0xF8`, every `0xEF` page, enables
  streaming (`0xF4 42 0C`), logs raw / SIXAXIS.SYS-style / calibrated values
  side by side to CSV, runs the gyro tracker live, optional `--interactive`
  orientation prompts and `--calbyte` sensitivity experiment, and shuts down
  like the PS3 (`0xF4 42 0B`). Restore DsHidMini afterwards via Device
  Manager (uninstall the WinUSB device without deleting the driver, rescan).
- `dumps/` - per-controller logs and CSV streams.
