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
2. A clean-room decompilation (Ghidra 12.1.3, headless) of **Sony's own
   `sixaxis.sys`** (x64, 28,424 bytes, validly Authenticode-signed by "Sony
   Computer Entertainment Inc."; SHA-256
   `b040b0a3a519d8d43e21a02fb9f2a52300f40f07226e18c4ba4e61c6fc380a51`). This is
   the authoritative source for everything in this document and supersedes the
   guesswork in the public references. See
   [What `sixaxis.sys` actually does](#what-sixaxissys-actually-does).
3. A clean-room decompilation of rajkosto's closed-source `ds3cal.dll` (x86 and
   x64 builds are identical) from the
   [rajkosto/ScpToolkit fork](https://github.com/rajkosto/ScpToolkit/tree/master/ScpControl/ds3cal).
   Its gyro auto-zero parameter block is **field-for-field identical** to the one
   in `sixaxis.sys`, so the two implement the same algorithm; `ds3cal.dll` is
   effectively a user-mode port of Sony's calibrator.
4. Live dumps from a genuine DualShock 3 (CECHZC2E-A1 pad, firmware bytes
   `04 00 08`), an original SIXAXIS and an aftermarket ("clone") DS3, each bound
   to WinUSB via Zadig and read with a throwaway probe (see
   [Tooling](#tooling)), including a six-orientation accelerometer table for the
   DS3.
5. Public references: RPCS3 `ds3_pad_handler.cpp`,
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
| SIXAXIS (PS3 capture) | 505 | 554 | 469 | 758 (very noisy, 2-997) | 1199 |
| Genuine DS3, flat on desk (WinUSB probe) | 494 | 475 | 396 | 483 | 200 |
| Clone, flat on desk (WinUSB probe) | 513 | 500 | 383 | 500 (constant) | 1001 |

Flat on the desk the pad reads **Z ~ 1 g below the zero-g value**
(398 vs 511 on the A1 pad, 396 vs 496 on the live DS3, 383 vs 512 on the
clone). X and Y sit at their zero-g values when level.

### Orientation / sign table (genuine DS3, probe `--interactive`)

Raw big-endian values averaged over 200 reports per pose, and the same values
after the calibration formula below (this pad's `zero`/`oneG`: X 498/386,
Y 494/383, Z 496/387):

| Pose | raw X | raw Y | raw Z | cal X | cal Y | cal Z | 1 g on |
| --- | --- | --- | --- | --- | --- | --- | --- |
| flat on desk, buttons up | 494 | 475 | **396** | 509 | 493 | **409** | Z = -1 g |
| upside down, buttons on desk | 494 | 469 | **624** | 509 | 487 | **644** | Z = +1 g |
| standing on the left grip | **607** | 511 | 512 | **621** | 529 | 528 | X = +1 g |
| standing on the right grip | **383** | 516 | 515 | **397** | 534 | 531 | X = -1 g |
| front edge down (triggers on desk) | 496 | **385** | 501 | 511 | **402** | 517 | Y = -1 g |
| back edge down (triggers up) | 495 | **603** | 520 | 510 | **622** | 536 | Y = +1 g |

In Sony's convention (512 = 0 g, +113 = +1 g) an axis reads **+1 g when it
points upwards**: X is positive with the left grip down, Y with the trigger edge
up, Z with the pad upside down. That is a right-handed frame of **X towards the
left grip, Y towards the trigger edge, Z down through the buttons**. Raw and
calibrated values share the signs; only `sixaxis.sys`/DsHidMini's SIXAXIS mode
mirror X (`0x3FF - X`). The ~10-count residuals (cal Z 409 instead of 399 when
flat) are this pad's ageing/temperature drift against its factory EEPROM.

## Identification: `GET_REPORT Feature 0x01`

64 bytes requested, layout (offsets into the returned buffer, which starts
with `00 01`):

| Offset | Meaning | Values seen |
| --- | --- | --- |
| 2-4 | firmware/board revision bytes; the same three bytes are echoed at offset 2-4 of every `0xEF` answer | A1 `04 00 08`, B1 `04 01 03`, A2 `04 00 0b`, SIXAXIS `02 00 03`, live DS3 `04 00 08`, clone `03 00 05` |
| 8-11 | sensor/pad type, four identical bytes | `18 18 18 18` DualShock 3, `17 17 17 17` SIXAXIS, clone `18 18 18 18` |
| 0x25 | number of calibration field IDs that follow | 4 / 3 / 4 / 1 / 3 / 2 |
| 0x26.. | calibration field IDs | A1+A2 `00 01 02 07`, B1 `00 01 02`, SIXAXIS `06`, live DS3 `00 01 02`, clone `01 02` |

These offsets are **verified**: `sixaxis.sys` reads this report with
`GET_REPORT(Feature, 0x01)` into a 49-byte buffer and tests exactly bytes 8-11
for `0x18`, byte `0x25` as the field count and `0x26..` as the field list.

Field ID `0x07` is the important one. `sixaxis.sys` derives two flags from this
report and they select the whole motion code path (bit names ours):

| Flag | Set when | Pads |
| --- | --- | --- |
| `DS3_TYPE` (0x08) | bytes 8-11 all `0x18` | every DS3, incl. the clone |
| `PLAIN_ZERO` (0x10) | the field list starts `01 02` at index 0 **or** index 1 | A1, A2, B1, live DS3 (`00 01 02`), clone (`01 02`) |
| `HW_CAL` (0x20) | the field list contains `07` anywhere | A1, A2 |

A SIXAXIS (single field `06`) matches neither, so it gets `PLAIN_ZERO` clear and
`HW_CAL` clear. The three resulting gyro paths are described under
[Gyroscope](#gyroscope). Pads without field `0x07` get no cal byte and their
EEPROM gyro cal-byte slot reads `0`; rajkosto's `UsbDs3.cs` encodes the same
decision tree. Note that the live DS3 shares firmware bytes `04 00 08` with the
A1 capture pad yet lacks field `0x07`, so the field list has to be read per pad
- it cannot be inferred from the revision bytes.

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
- Page addresses step in units of `0x10`; both the genuine DS3 and the clone
  answer every page from `0x00` to `0xF0`. On the genuine pad: `0x00`-`0x60`
  hold a monotonic byte curve (`d2 d3 d4 ... ff`, most likely a lookup table for
  the analogue inputs or the battery gauge), `0x70` and `0x80` trim data
  (`0x80` = `03 ff 00 00 ff 44 44 02 65 02 5a`), `0x90` four u16 pairs that look
  like stick min/max (`00 5f 03 a1`, `00 63 03 9d`, ...), `0xA0` the sensor
  calibration below, `0xB0` two u16 (`02 65 02 5a` = 613 / 602 - the same two
  values that also sit at `0x80` offset 8-11), `0xC0` a lone `06`, and `0xF0`
  what looks like a manufacturing record
  (`20 07 09 03 00 00 22 2f 00 01 00 00 1f ba 95 0b`, plausibly 2007-09-03 plus
  a serial). The clone's image is a fixed template with the same structure
  (`0x90` = `00 5c 03 a4` four times, `0xB0` = `02 80 02 80`).
- Without a preceding page select, the clone returns page `0xA0`; the genuine
  DS3 returned a buffer whose header reads `03 00 10` with content
  (`00 00 01 64 19 01 00 64 00 01 90 00 19 fe 00`) unlike page `0x10` read via
  `03 01 10`, so request byte 5 (`01` in the PS3 payload) selects something else
  - a different bank or a read width. Leave it at `01`; the PS3 never does a
  plain read.
- `Feature 0xF8` returns whatever is in the same 64-byte device buffer with
  the header replaced by `00 01 00 00` (A1, B1, SIXAXIS all return the last
  `0xEF` answer). The A2 console/pad and the clone return all zeros. It
  carries no extra information in any capture.
- The live SIXAXIS answered every page from `0x00` to `0xC0` with the same
  structure as the genuine DS3: `0x00`-`0x60` the monotonic byte curve
  (`c3 c4 c6 ... ff`), `0x70` trim data
  (`01 8b bf cf c3 ca 16 00 bf a9 a6 bc a3 be 10 b0`, unlike any DS3),
  `0x80` = `03 ff 00 00 ff 44 44 02 7d 02 78`, `0x90` four u16 pairs
  (`00 85 03 7d`, `00 8e 03 97`, `00 91 03 84`, `00 65 03 74`), `0xA0` the
  calibration block `0209 019b | 0206 0194 | 01f3 0185 | 0201 0075`, `0xB0` =
  `02 7d 02 78`, `0xC0` all zeros (the DS3 has a lone `06` there). Its
  identification report is `00 01 02 00 03 08 01 02 17 17 17 17 09 0a ...` with
  one calibration field, `06`. `0xF2` =
  `f2 ff ff 00 00 19 c1 63 7e a0 00 03 40 80 18 01 8a`, `0xF7` =
  `01 00 7f 02 d9 01 02 ff 14 23`. Note that the probe's own summary line for
  that run says "no factory calibration on this unit" - that is a **false
  negative**: the pad stopped answering partway through the page sweep, so the
  confirmation re-read of `0xA0` failed and the verdict was computed from the
  failed re-read instead of the good page captured moments earlier. The probe
  now falls back to the swept page (and checks the echoed page number), but any
  earlier log carrying that line has to be read against its own `0xA0` dump.
- `Feature 0xF7` (read once after the first output report) varies per pad and
  per plug-in; bytes 2-6 look like live sensor/ADC readings
  (`7f 02 ce 01 f1`, `1e 02 fa 01 01`, `fe 02 f8 01 ef`, live DS3
  `04 02 da 01 ee`) followed by `ff 14 33` (`ff 10 90` on the live DS3) on
  DS3-class pads, all zero on the A2 sample and mostly zero on the SIXAXIS
  (`0a 01 ea` at 12-14). Purpose unknown; not needed for motion.

### Page `0xA0`: sensor calibration

Eight **big-endian** u16 values, four pairs `(zero, oneG)`:

| Page offset | Buffer offset | Field |
| --- | --- | --- |
| 0-1 / 2-3 | 0x11 / 0x13 | accel X: reading at 0 g / reading at -1 g |
| 4-5 / 6-7 | 0x15 / 0x17 | accel Y: same |
| 8-9 / 10-11 | 0x19 / 0x1B | accel Z: same |
| 12-13 / 14-15 | 0x1D / 0x1F | gyro: raw reading at rest / factory **cal byte** (0-255) |

This layout is **verified** against `sixaxis.sys`, which parses the reply with a
4x2 loop reading big-endian u16 starting at buffer offset `0x11` into eight
`int` slots, then uses slot 6 as the gyro zero and slot 7 as the gyro cal byte
(see [What `sixaxis.sys` actually does](#what-sixaxissys-actually-does)). The
naming is a little unfortunate: `oneG` is the raw reading at **-1 g**, and for
the gyro the second element of the pair is not a reading at all but the cal
byte.

Observed:

| Pad | X | Y | Z | Gyro | Page `0xB0` (first 2 u16) |
| --- | --- | --- | --- | --- | --- |
| DS3 (A1 capture) | 508 / 397 (111) | 503 / 387 (116) | 511 / 396 (115) | 521 / **0x77** | 620 / 610 |
| DS3 (B1 capture) | 508 / 395 (113) | 507 / 398 (109) | 513 / 402 (111) | 488 / **0** | 624 / 615 |
| DS3 (A2 capture) | 507 / 395 (112) | 510 / 397 (113) | 511 / 400 (111) | 521 / **0x7F** | 620 / 622 |
| SIXAXIS (PS3 capture) | 521 / 411 (110) | 518 / 404 (114) | 499 / 389 (110) | 513 / **0x75** | 637 / 632 |
| SIXAXIS (live, WinUSB) | 521 / 411 (110) | 518 / 404 (114) | 499 / 389 (110) | 513 / **0x75** | 637 / 632 |
| Genuine DS3 (live, WinUSB) | 498 / 386 (112) | 494 / 383 (111) | 496 / 387 (109) | 481 / **0** | 613 / 602 |
| Clone | 512 / 384 (128) | 512 / 384 (128) | 512 / 384 (128) | 512 / 0 | 640 / 640 |

Numbers in parentheses are `zero - oneG`, i.e. counts per g: **~113 on genuine
hardware**, a suspiciously round 128 on the clone (its "EEPROM" is a fixed
template, but its accelerometer really does read 383 flat, so the template is
consistent with the firmware's scaling).

The live SIXAXIS row is byte-identical to the SIXAXIS row extracted from the
PS3 capture, down to page `0xB0` - almost certainly because the pad dumped here
is the same physical unit that produced that capture, which makes it a clean
end-to-end check of both the `tshark` extractor and the WinUSB probe.

The rest of page `0xB0` is zero on every pad. Its two values (~600-640) are
unexplained; candidates are gyro sensitivity or temperature compensation. On the
genuine DS3 and the SIXAXIS alike the same two values are repeated at page
`0x80` offset 8-11, which argues for them being a sensor property rather than
gyro-specific. **`sixaxis.sys` never reads page `0xB0` at all** (it issues
exactly one page select, for `0xA0`), so whatever it holds is not needed for
motion.

Note on lewy20041's parsing: he reads little-endian u16 at buffer offsets 20,
22, ..., 32. Offset 20-21 happens to be the low byte of X.oneG followed by the
high byte of Y.zero, which decodes "correctly" only by accident for the values
seen here, and his "gyro offset" at 32-33 is really the cal byte. Use the
layout above.

## Accelerometer calibration

This is now **settled** by `sixaxis.sys`, which calibrates all three axes in one
loop with a single gain of `0x71` = 113:

```c
// per axis i = 0,1,2 over the report's three big-endian u16
raw = bswap16(report[41 + 2*i]);
if (zero[i] != oneG[i])                                  // guard: clone/blank EEPROM
{
    t   = ((raw - zero[i]) * 1024 / (zero[i] - oneG[i])) * 113;
    cal = ((t + ((t >> 31) & 0x3FF)) >> 10) + 512;        // /1024 rounding toward zero
}
else
    cal = raw;                                           // pass the raw value through
if (i == 0) cal = 0x3FF - cal;                           // X is mirrored, AFTER calibration
```

So the calibrated scale is **113 counts per g centred on 512** (0 g = 512,
+1 g = 625, -1 g = 399). Lying flat, Z therefore reads ~399 on a genuine pad
and on the clone alike, which the probe confirms (`cal Z 399` for raw 383 with
512/384). Three details worth having:

- The `zero - oneG` divisor is the "unknown `dword_0x0`" RPCS3 was missing, and
  `zero` is its unknown `dword_0xC`.
- **There is no 2x gain.** RPCS3's commented-out `polish_value` calls pass
  `(226, -226)`, `(226, 226)` and `(113, 113)` as (divisor, gain) - every pair
  reduces to a ratio of +-1, so those calls do nothing but mirror X. They carry
  no information about the real gain, which is 113 against a `zero - oneG`
  divisor. ScpToolkit's 113 is correct.
- `sixaxis.sys` mirrors X as `0x3FF - cal` **after** calibrating, and does
  **not** clamp the accelerometer result to 0-1023 (only the gyro is clamped),
  so a hard enough knock can push it outside 10 bits. RPCS3's sketch clamps.

For a **DS4-style** output (DS4Windows mode) the natural conversion is
`accel_ds4 = (raw - zero) * 8192 / (zero - oneG)` per axis (DS4: 8192 LSB/g).
The DS3 source frame is now known (X towards the left grip, Y towards the
trigger edge, Z down through the buttons - see the orientation table above);
the permutation and signs into the DS4 frame still need a reference DS4
capture to confirm.

## Gyroscope

### Hardware cal byte

The gyro has a hardware zero-rate trim. The host sends a **cal byte** in the
output report and the pad shifts its raw yaw reading by about **26.4 counts
per cal-byte step** (`0x6999` in Q10). That constant is Sony's own - it appears
in `sixaxis.sys` and in `ds3cal.dll` with identical surrounding parameters - but
its physical magnitude has still not been measured on hardware here, see the
probe's `--calbyte` experiment:

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

`sixaxis.sys` places the byte at exactly those offsets, keyed off the two flags
from `Feature 0x01`: `if (!PLAIN_ZERO) { out[3] = 0xFF; out[4] = cal; }` and
`if (HW_CAL) { out[5] = 0xFF; out[6] = cal; }`, then sends the 48-byte buffer as
`SET_REPORT(Output, report ID 1)` over EP0. Unlike the PS3 captures it *does*
update the byte later: whenever the auto-zero tracker asks for a new one, the
driver stores it under a spin lock for its output-report thread to send. The
captures simply never sat still long enough (or those consoles' pads were
already trimmed) to show a change.

Sending `0x00 0x00` there (what DsHidMini does today) is what the PS3 does for
the B1 class, so it is safe for every pad; it just leaves the gyro on its
untrimmed zero.

### Auto-zero algorithm (`sixaxis.sys` / `ds3cal.dll`, clean-room)

`ds3cal.dll` exports `GyroCalCreate/Destroy` (196-byte state on the process
heap), `InitialGyroCal(u16 eepromCal, u16 eepromZero, u8* calByte, state)`,
`RuntimeGyroCal(u16 raw, u16* gyroOut, u8* calByte, state)` (returns 0 **when
the cal byte changed**, -1 otherwise) and `GyroCalStore/Load` (`0xDABE` magic
+ state copy, 0xBC bytes). ScpToolkit calls `InitialGyroCal(Gyro.oneG,
Gyro.zero, ...)`, i.e. `eepromCal` is the cal byte and `eepromZero` the raw
reading it produces at rest.

The same algorithm is in Sony's driver: `sixaxis.sys` builds a 17-field
parameter block that is **identical field-for-field** to the one `ds3cal.dll`'s
`InitialGyroCal` builds (same values, same order, same two `(cal, zero)`
arguments in the same positions), and its per-report code has the same
structure - state copy in, settle counter, `output = clamp(512 - raw + zeroRef)`,
16-sample block tracker, pending-cal jump detector, state copy out. Everything
below is therefore verified against Sony's implementation, not just rajkosto's.

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

Two traps for a re-implementation, both confirmed in the decompilation:

- The cal byte is tracked as a signed `int` and truncated to `u8` on the way
  out, with **no clamping** at either end - `Retarget` just does
  `calByte += steps`. A pad whose rest value is far from 512 can therefore wrap
  the byte. Clamp to 0-255.
- Neither implementation re-zeroes in software while the hardware trim is
  converging; the reported value keeps using the old `zeroRef` until the pad is
  observed to have applied the new byte (the pending/jump detector).

### Units

The gyro's counts-per-degree-per-second are **not** in the EEPROM, not in
`ds3cal.dll` and not in `sixaxis.sys` - none of them scale the gyro, they only
zero and/or mirror it. RPCS3 passes the value through with gain 1.
On the live DS3 (at rest 483) a slow hand-held yaw rotation swung the raw value
between 362 and 596, i.e. roughly +-120 counts for a leisurely turn, so the
sensor is nowhere near saturating its 10 bits at normal speeds. For a DS4-style
output (16 LSB per deg/s) a scale factor still has to be measured: integrate
`(raw - rest)` over a controlled 90 or 180 degree yaw turn and divide by the
angle (the probe prints that integral after the yaw prompt). Page `0xB0`
(600-640 range) is a candidate carrier of this information and should be
compared across more pads.

The direction convention is unsettled: the recorded DS3 turn produced a net
negative integral, but the physical turn direction was not logged. Repeat with
a known clockwise turn (viewed from above) to pin the sign.

## What `sixaxis.sys` actually does

Decompiled from the signed x64 driver. Offsets are into its device context; the
input report is the 49-byte `GET_REPORT(Input, ID 1)` buffer with the sensors at
byte 41 as documented above.

**Start-up, in order** (all over EP0, all failures abort device start - so a pad
that will not answer `0xEF` never comes up under this driver):

1. optional `SET_REPORT(Feature, 0xF5)` with `01 00 ff ff ff ff ff ff 00...`
   (48 bytes) when the `ClearPairingSetting` registry value under
   `HKLM\SYSTEM\CurrentControlSet\Services\sixaxis\Parameters` is 1.
2. `SET_REPORT(Feature, 0xEF)`, 48 bytes, payload
   `00 00 00 00 03 01 a0 00 00 ... 00` - i.e. select page `0xA0`, byte-for-byte
   the payload seen in the PS3 captures. This is the **only** page select the
   driver ever issues.
3. `GET_REPORT(Feature, 0xEF)`, 64 bytes, then parse eight big-endian u16 from
   buffer offset `0x11` into `(zero, oneG)` x4.
4. `GET_REPORT(Feature, 0x01)`, 49 bytes -> the `DS3_TYPE` / `PLAIN_ZERO` /
   `HW_CAL` flags above. If `!PLAIN_ZERO` or `HW_CAL`, initialise the gyro
   auto-zero state with `InitialGyroCal(cal = slot 7, zero = slot 6)`.
5. `SET_REPORT(Output, ID 1)`, 48 bytes, carrying the gyro cal byte at `[3]/[4]`
   or `[5]/[6]` per the flags (and `0xFF` at `[9]`).
6. `SET_REPORT(Feature, 0xF4)` with `42 0c 00 00` - enable the sensor stream.
   (The probe sends the same four bytes.)

**Per input report:** byte-swap the four big-endian sensor u16, calibrate the
three accelerometer axes with gain 113 and mirror X (see
[Accelerometer calibration](#accelerometer-calibration)), then one of three
gyro paths, selected by the flags:

| Flags | Pads | Reported gyro |
| --- | --- | --- |
| `PLAIN_ZERO` set, `HW_CAL` clear | B1, live DS3, clone | `clamp(512 + eepromZero - raw)` - plain software zero against the EEPROM value, no tracker |
| `PLAIN_ZERO` clear | SIXAXIS | `clamp(512 + zeroRef - raw)` with the full auto-zero tracker driving `zeroRef` |
| `HW_CAL` set | A1, A2 | tracker runs (to keep the **hardware** trim converged and re-send cal bytes), but the reported value is just `clamp(0x3FF - raw)` |

All three **invert the gyro's sign relative to the raw value**, and the result is
written back into the report as host-order u16.

RPCS3's Windows path (`#ifdef _WIN32`) takes these fields as-is and notes that
Sony's driver already "does the same modification of this value as the PS3" -
which the above confirms for X. Its Linux path (raw hidraw) only mirrors X, as
`512 - (accel_x - 512)` = `1024 - accel_x`; note `sixaxis.sys` uses
`1023 - accel_x` and mirrors the *calibrated* value, so RPCS3's Linux path is
both off by one and uncalibrated. RPCS3 passes the gyro through unchanged on
both platforms, so on Linux it is not zeroed either.

## What DsHidMini does today

- **SIXAXIS.SYS-compatible mode** (`driver/DsHidMiniDrv.c`, `DSHM_ProcessHidInputReport`):
  `X = 0x3FF - swap16(X)`, `Y = swap16(Y)`, `Z = swap16(Z)`, `G = swap16(G)` -
  endianness fixed, X mirrored, **no calibration**, no cal byte. The sensors are
  exposed only through the emulated `GetFeatureReport`, not the input report.
- **DS4Windows-compatible mode** (`driver/DsHid.c`, `DS3_RAW_TO_DS4WINDOWS_HID_INPUT_REPORT`):
  writes `Output[0..9]` and `Output[30]` only, so the DS4 gyro/accel fields at
  offsets 13-24 stay zero.
- Output report bytes 6-7 (with ID) are always `00 00`; equivalent to the PS3's
  behaviour for pads without calibration field `0x07`.
- `0xEF`/`0xF8`/`0xF7` are never sent (documented in `PS3_USB_STARTUP.md`).

### Discrepancies a driver implementation must resolve

Ordered by how visible they are to an application that expects `sixaxis.sys`:

1. **No calibration at all.** DsHidMini reports raw counts centred on the pad's
   EEPROM `zero` (494/475/396 on the live DS3); `sixaxis.sys` reports 113
   counts/g centred on exactly 512. Every consumer scaled against Sony's driver
   (RPCS3 included) is therefore mis-scaled and off-centre per pad. Fix: read
   page `0xA0` at start-up and apply the gain-113 formula.
2. **Gyro sign is inverted.** All three `sixaxis.sys` paths report
   `512 + zeroRef - raw` or `0x3FF - raw`; DsHidMini reports `swap16(raw)`. The
   sign is backwards *and* the value is not zeroed, so a still pad reads its
   EEPROM zero (~481) instead of 512.
3. **X is mirrored on the wrong value.** `0x3FF - swap16(X)` mirrors the raw
   reading; Sony mirrors the *calibrated* one (`0x3FF - cal`). These differ once
   calibration exists, so the mirror has to move after the formula, not before.
4. **No `zero == oneG` fallback.** Sony's per-axis guard passes the raw value
   through when the EEPROM pair is degenerate. Any implementation needs the same
   guard, plus a fallback for pads that refuse `0xEF` entirely (Sony's driver
   simply fails device start; DsHidMini must not).
5. **No cal byte and no auto-zero.** Sending `00 00` matches the PS3 only for
   `PLAIN_ZERO` pads. A1/A2-class pads (field `0x07`) expect their EEPROM cal
   byte at output `[5]/[6]`, a SIXAXIS at `[3]/[4]`, and Sony re-sends an updated
   byte whenever the tracker converges on a new one.
6. **Per-class behaviour is not just cosmetic.** The three gyro paths are keyed
   off `Feature 0x01`; picking one path for all pads will be wrong for the
   others. In particular a field-`0x07` DS3 must *not* be software-zeroed on top
   of its hardware trim.
7. **DS4Windows mode has no motion at all**, and the DS4 frame mapping is still
   unverified: the DS3 source frame is now known (X to the left grip, Y to the
   trigger edge, Z down through the buttons) but the permutation, signs and the
   gyro deg/s scale into DS4 units are not.
8. **Bluetooth is untested.** `sixaxis.sys` does all of this over EP0; the same
   feature reports would have to travel the BthPS3 HID control channel.

## Sketch for a future implementation (not part of this pass)

1. During USB start (after `0xF2`/`0xF5`, before the first output report,
   mirroring the PS3), do `SET 0xEF page A0` + `GET 0xEF`, parse the four pairs,
   keep them in the device context. Over Bluetooth the same feature reports
   go through the BthPS3 HID control channel (untested).
2. Read `Feature 0x01` offsets 8 and 0x25/0x26.. and derive Sony's three flags
   (`DS3_TYPE`, `PLAIN_ZERO`, `HW_CAL`); place the cal byte at output bytes 6-7
   (field-`0x07` DS3) or 4-5 (SIXAXIS), seeded with `Gyro.oneG`.
3. Per input report: accel
   `cal = ((raw - zero) * 1024 / (zero - oneG)) * 113 / 1024 + 512`, mirroring X
   afterwards, with a `zero == oneG` passthrough guard; gyro per the flag table
   in [What `sixaxis.sys` actually does](#what-sixaxissys-actually-does).
4. DS4Windows mode: `accel_ds4 = (raw - zero) * 8192 / (zero - oneG)` with
   the axis permutation checked against a reference DS4; gyro yaw scaled by the
   measured
   deg/s factor into the DS4 gyro-Y slot, pitch/roll zero.
5. Clone handling: if `0xEF` fails or `zero == oneG`, fall back to
   `zero = 512, oneG = 512 - 113` (or 384 for the 128-count clone class) and
   skip the gyro entirely if it is constant.

## Open questions

- Yaw sign and deg/s scale from a turn of known direction and angle. Nothing in
  Sony's driver scales the gyro, so this has to be measured.
- The 26.4 counts/step cal-byte sensitivity is Sony's constant but its physical
  effect is unmeasured (probe `--calbyte`), as is whether a pad without field
  `0x07` ignores the bytes. Neither the live DS3 nor the clone lists field
  `0x07`, so this needs an A1/A2-class pad; the SIXAXIS would answer the
  `[3]/[4]` placement question.
- Meaning of page `0xB0` (and of the same pair at `0x80` offset 8-11). Not used
  by `sixaxis.sys`, so it is curiosity rather than a blocker.
- Orientation/sign table for the SIXAXIS, to confirm it shares the DS3 axis
  frame. Only the DS3 was posed through the six orientations.
- Does `0xEF` work over Bluetooth (BthPS3 control channel)?
- Do other clone families (Defender BT in DS3 mode, ShanWan) answer `0xEF`
  and with what?
- A SIXAXIS bound to WinUSB stops answering **all** transfers (EP0 and the
  interrupt IN pipe, `ERROR_GEN_FAILURE`) a few seconds after enumeration and
  never recovers - `WinUsb_ResetPipe`, re-selecting the alternate setting and
  disabling autosuspend do not bring it back, and Windows still reports the node
  healthy and in D0. Catching it within that window is the only way to dump it,
  which is what the probe's `--wait` mode does. Whether the pad selectively
  suspends and fails to resume, or something else, was not chased further; it is
  a quirk of the throwaway WinUSB setup, not of DsHidMini.

## Tooling

Everything binary or throwaway lives outside the repository in
`D:\FOSS\DsHidMini-motion-rnd\` (not committed):

- `pcap/Extract-HidControl.ps1` - `tshark`-based extractor that reassembles
  every HID class control transfer (setup + data stage) from the USB
  link-layer pcaps; per-capture `.txt` outputs and `resting_sensor_stats.txt`
  next to it.
- `ghidra/` - `ds3cal.dll` x86/x64 and Sony's `sixaxis.sys`, headless project,
  `ExportDecompiled.java` post-script, decompiler output
  (`ds3cal_*.decompiled.c`, `sixaxis_sys.decompiled.c`) and
  `ds3cal_algorithm.md` with the annotated state layout.
  Run: `analyzeHeadless.bat <abs proj path> <name> -import <abs binary> -scriptPath <abs scripts> -postScript ExportDecompiled.java <abs out.c>`
  (headless Ghidra rejects any path element starting with `.`).
- `probe/` - .NET 10 console driving `winusb.dll` through a thin P/Invoke layer
  (`WinUsbRaw.cs`). `Nefarius.Drivers.WinUSB` was tried first but its
  `USBDevice` constructor eagerly reads string descriptors, which an original
  SIXAXIS answers with a STALL, killing the whole session. For a
  Zadig/WinUSB-bound `054C:0268` it dumps `0x01/0xF2/0xF5/0xF7/0xF8`, every
  `0xEF` page, enables
  streaming (`0xF4 42 0C`), logs raw / SIXAXIS.SYS-style / calibrated values
  side by side to CSV, runs the gyro tracker live, optional `--interactive`
  orientation prompts and `--calbyte` sensitivity experiment, and shuts down
  like the PS3 (`0xF4 42 0B`). `--wait` polls until a pad answers on EP0, which
  is the only way to catch a SIXAXIS (see [Open questions](#open-questions)).
  Two WinUSB gotchas cost a while: the handle is opened `FILE_FLAG_OVERLAPPED`,
  so every transfer needs a real `OVERLAPPED` plus `GetOverlappedResult` (a NULL
  one yields `ERROR_GEN_FAILURE`/`ERROR_NOACCESS`), and WinUSB probes the
  transfer buffer for *write* even on an OUT transfer, so a span backed by a
  read-only static (what a C# collection expression compiles to) fails with
  `ERROR_NOACCESS`. Restore DsHidMini afterwards via Device Manager (uninstall
  the WinUSB device without deleting the driver, rescan).
- `dumps/` - per-controller logs and CSV streams.
