# ds3cal.dll - clean-room description of the gyro calibration algorithm

Source: Ghidra 12.1.3 headless decompilation of `ScpControl/ds3cal/{x86,x64}/ds3cal.dll`
(rajkosto/ScpToolkit). Both builds contain the same algorithm and constants.
Decompiler output: `ds3cal_x86.decompiled.c`, `ds3cal_x64.decompiled.c`.

**Verified against Sony.** `sixaxis.sys` (see `sixaxis_sys.decompiled.c`,
`FUN_00013d40`) builds a parameter block that is identical field-for-field to
the one `InitialGyroCal` builds here - same 17 fields, same values, same order,
same two `(cal, zero)` arguments in the same slots - and its per-report path has
the same shape. `ds3cal.dll` is a faithful port of Sony's calibrator, so the
description below applies to both.

Everything in this file was re-read against the decompiler output on a second
pass; the notes marked *unverified* are the only remaining assumptions.

## Exports (stdcall)

| Export | Signature (reconstructed) | Notes |
| --- | --- | --- |
| `GyroCalCreate` | `void* ()` | `HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, 0xC4)` - state blob is 196 bytes |
| `GyroCalDestroy` | `void (void* state)` | `HeapFree` |
| `InitialGyroCal` | `int (u16 eepromCal, u16 eepromZero, u8* outCalByte, void* state)` | returns 0 / -1 on null args |
| `RuntimeGyroCal` | `int (u16 rawGyro, u16* outGyro, u8* outCalByte, void* state)` | returns **0 when the cal byte changed**, -1 otherwise |
| `GyroCalStore` | `int (void* state, u8* buf, int* len)` | `len = 0xBC`; buf = `u16 magic 0xDABE` + state copy; returns 0 on success |
| `GyroCalLoad` | `int (void* state, u8* buf, int* outCalByte)` | validates magic, restores state |

ScpToolkit calls `InitialGyroCal(G.val2, G.val1, ...)` where `G.val1/G.val2` are the
4th big-endian u16 pair (offset 0x1D/0x1F) of the `0xEF` page `0xA0` EEPROM read,
i.e. `eepromCal = G.val2` (factory cal byte) and `eepromZero = G.val1` (raw gyro
reading at rest that this cal byte yields).

## Constants (parameter block built by InitialGyroCal)

| Name | Value | Meaning |
| --- | --- | --- |
| `TARGET` | 0x200 = 512 | desired at-rest raw value / output centre |
| `STEP_Q10` | 0x6999 = 27033 | raw counts per cal-byte step, in Q10 (27033 / 1024 = 26.4 counts) |
| `SETTLE_INITIAL` | 0x20 = 32 | samples ignored after the initial cal |
| `SETTLE_AFTER_CAL` | 2 | samples ignored after every later cal-byte change |
| `RAW_MAX` | 0x333 = 819 | raw above this = "moving" |
| `RAW_MIN` | 0xCC = 204 | raw below this = "moving" |
| `BLOCK_N` | 0x10 = 16 | samples per block |
| `BLOCK_VAR_MAX` | 10 | block "stable" if (max-avg)^2 + (avg-min)^2 < 10 |
| `RING_N` | 4 | consecutive stable block averages kept |
| `RING_RANGE_MAX` | 4 | ring counts as "at rest" if max-min < 4 |
| `LONG_N` | 0xEC = 236 | blocks per long-term average (236 * 16 = 3776 samples) |
| `PENDING_TOL` / `PENDING_DECAY` | 100 / 1 | tolerance window for detecting the device applied a new cal byte. Only `100` is passed in twice; the decay is computed as `param[0x10] / param[0x0f]` = 100/100 = 1 |

Two further parameter-block slots exist: one `4` (index 8) that the initialiser
never reads (dead constant), and a `0` (index 14) likewise unused. `mode` is
**not** a parameter - the initialiser hard-codes `state+0x34 = 1`.

Signedness and byte order: both entry points mask their inputs with `& 0xffff`
and everything after that is signed `int32` arithmetic, including the `delta`
comparison and the `/ stepQ10` division. The DLL takes **host-order** values;
the big-endian decode of the input report is the caller's job (ScpToolkit does
it before calling, `sixaxis.sys` byte-swaps in its report handler).

## State (offsets into the 0xC4 blob, all int32 unless noted)

```
0x00 target          0x04 calByte        0x08 zeroRef        0x0C stepQ10
0x10 lastRaw         0x14 output         0x18 settleAfterCal 0x1C settleLeft
0x20 calByte0        0x24 zero0          0x28 rawMax         0x2C rawMin
0x30 moving (u8)     0x34 mode           0x38 blockN         0x3C blockCount
0x40 blockSum        0x44 blockMin       0x48 blockMax       0x4C blockAvg
0x50 lastBlockMin    0x54 lastBlockMax   0x58 lastBlockAvg   0x5C blockVar
0x60 blockVarMax     0x64 ring.buf(ptr)  0x68 ring.cap       0x6C ring.filled
0x70 ring.idx        0x74 ring.sum       0x78 ring.range     0x7C..0x88 ring.data[4]
0x8C restAvg         0x90 ringRange      0x94 ringRangeMax   0x98 longN
0x9C longCount       0xA0 longSum        0xA4 longAvg        0xA8 longAvgCopy
0xAC pendingDecay    0xB0 pendingTol0    0xB4 pendingTol     0xB8 pendingCal
0xBC pendingZero     0xC0 pending (u8)
```

## Algorithm

```c
// signed division by 1024 rounding toward zero
#define Q10(x) (((x) + (((x) >> 31) & 0x3FF)) >> 10)

// Given an at-rest average, decide whether a cal-byte change is needed.
// Returns true and updates calByte/zeroRef when it is.
static bool Retarget(State* s, int restAvg, int* zeroRef, int* calByte)
{
    int delta = (s->target - restAvg) * 1024;        // Q10 counts to move
    if (-s->stepQ10 <= delta && delta <= s->stepQ10) // less than one cal step away
    {
        *zeroRef = restAvg;                          // software re-zero only
        return false;
    }
    int steps = delta / s->stepQ10;                  // whole cal-byte steps
    *zeroRef = restAvg + Q10(steps * s->stepQ10);    // predicted raw at rest after the change
    *calByte = s->calByte + steps;
    return true;
}

int InitialGyroCal(u16 eepromCal, u16 eepromZero, u8* outCalByte, State* s)
{
    memset(s, 0, sizeof *s); s->target = 512; s->stepQ10 = 0x6999; ...constants...
    s->calByte = s->calByte0 = eepromCal;
    s->zero0   = s->lastRaw = eepromZero;
    s->settleLeft = 32; s->settleAfterCal = 2; s->mode = 1;
    Retarget(s, eepromZero, &s->zeroRef, &s->calByte); // uses zero0 as restAvg
    s->output = clamp(s->zeroRef - eepromZero + s->target, 0, 1023);
    *outCalByte = (u8)s->calByte;
    return 0;
}

// Called once per input report with the raw (big-endian decoded) gyro value.
int RuntimeGyroCal(u16 raw, u16* outGyro, u8* outCalByte, State* s)
{
    bool changed = false;

    if (s->settleLeft) { s->settleLeft--; s->lastRaw = raw; *outGyro = s->output; *outCalByte = s->calByte; return -1; }

    s->output = clamp(s->target - raw + s->zeroRef, 0, 1023);   // NOTE: sign inverted, 512 = at rest

    if (s->mode == 1)
    {
        changed = Track(s, raw);                                 // see below

        if (s->pending)                                          // a new cal byte was sent, wait for the device to apply it
        {
            int jump = raw - s->lastRaw;
            if (-s->pendingTol <= jump && jump <= s->pendingTol) { s->pendingTol -= s->pendingDecay; }
            else { ApplyPending(s); changed = true; }
        }
    }

    s->lastRaw = raw;
    *outGyro = s->output; *outCalByte = (u8)s->calByte;
    return changed ? 0 : -1;
}

static bool Track(State* s, int raw)
{
    if (raw > s->rawMax || raw < s->rawMin) s->moving = true;

    // 16-sample block statistics
    s->blockSum += raw; s->blockMax = max(s->blockMax, raw); s->blockMin = min(s->blockMin, raw);
    if (++s->blockCount != s->blockN) return false;
    s->blockCount = 0;
    s->blockAvg = (s->blockSum + s->blockN / 2) / s->blockN; s->blockSum = 0;
    s->lastBlockMin = s->blockMin; s->lastBlockMax = s->blockMax; s->blockMin = INT_MAX; s->blockMax = INT_MIN;
    s->lastBlockAvg = s->blockAvg;
    s->blockVar = sq(s->lastBlockMax - s->blockAvg) + sq(s->blockAvg - s->lastBlockMin);

    // slow long-term average over 236 blocks (~38 s at 100 Hz) as a fallback
    s->longSum += s->blockAvg;
    if (++s->longCount == s->longN)
    {
        s->longCount = 0; s->longAvg = (s->longSum + s->longN / 2) / s->longN; s->longSum = 0;
        int zero, cal;
        if (Retarget(s, s->longAvg, &zero, &cal)) { s->pendingZero = zero; s->pendingCal = cal; s->pending = true; s->pendingTol = s->pendingTol0; }
        else { s->pendingZero = s->zeroRef = s->longAvg; }
    }

    if (s->moving) { s->moving = false; return false; }         // discard this block, re-arm

    if (s->blockVar < s->blockVarMax)                            // quiet block
    {
        if (RingPush(&s->ring, s->blockAvg))                     // ring of the last 4 quiet block averages, full?
        {
            s->ringRange = s->ring.range;
            if (s->ringRange < s->ringRangeMax)                  // 4 quiet blocks agree: controller is at rest
            {
                s->restAvg = (s->ring.sum + s->ring.cap / 2) / s->ring.cap;
                s->longCount = 0; s->longSum = 0;
                bool changed = Retarget(s, s->restAvg, &s->zeroRef, &s->calByte);
                if (changed) { RingReset(&s->ring); s->settleLeft = s->settleAfterCal; }
                s->pending = false;
                return changed;
            }
        }
        if (s->pending) { ApplyPending(s); RingReset(&s->ring); s->settleLeft = s->settleAfterCal; return true; }
    }
    return false;
}

static void ApplyPending(State* s)
{
    s->zeroRef = s->pendingZero; s->calByte = s->pendingCal; s->pending = false;
    RingReset(&s->ring); s->longCount = 0; s->longSum = 0;
    // NOTE: the call site in RuntimeGyroCal additionally clears the 16-sample
    // block accumulator (blockCount/blockSum/blockMin/blockMax); the one in
    // Track does not, because Track only reaches it right after a block ended.
}
```

`RingPush` stores the value in a 4-entry circular buffer, keeps a running sum
(subtracting the evicted value once full) and recomputes `range = max - min`;
it returns true only once the buffer holds 4 entries.

## Host-side contract (from ScpToolkit and the PS3 captures)

- The cal byte is delivered to the controller in the output report:
  - DualShock 3 (Feature 0x01 lists calibration field `07`): no-report-ID bytes `[5] = 0xFF, [6] = calByte` (bytes 6/7 with report ID). PS3 captures: `ff 77`, `ff 7f` = `G.val2` of the respective pad.
  - SIXAXIS (no field `07`, `0x17` type bytes): bytes `[3] = 0xFF, [4] = calByte` (4/5 with report ID). PS3 capture: `ff 75` = `G.val2` of that SIXAXIS.
  - Pads whose `0x01` field list is `00 01 02` without `07` (CECHZC2E-B1 sample, `G.val2 == 0`): the PS3 sends no cal bytes at all and the gyro is only software-zeroed (`512 + G.val1 - raw`).
- The gyro value handed to applications is `clamp(512 + zeroRef - raw, 0, 1023)`; positive = one rotation direction, 512 = still. The 0x3FF clamp and the sign flip are exactly what `sixaxis.sys` does for SIXAXIS-class and `PLAIN_ZERO` pads. For pads listing calibration field `07`, Sony's driver runs this tracker only to keep the *hardware* trim converged and reports `clamp(0x3FF - raw)` instead - see `docs/MOTION.md`.

## Traps for a re-implementation

- **The cal byte is never clamped.** It is an `int` in the state, `Retarget` does
  `calByte += steps` with no bounds check, and the exports truncate it with a
  `(char)` cast on the way out. A pad resting far from 512 can wrap it. Clamp to
  0-255.
- **`Retarget` leaves `calByte` untouched when it returns false** (only `zeroRef`
  is written), so callers must not assume it was assigned.
- **The ring is not reset when the at-rest check produced no cal change**, so it
  keeps sliding one quiet block at a time.
- The long-term average path writes `pendingZero` in *both* branches but only
  sets `pending` in the stepping branch; in the other branch it also assigns
  `zeroRef` directly.

## Still unverified

- The physical magnitude of one cal-byte step (`0x6999` Q10 = 26.4 raw counts).
  The constant is Sony's, but no measurement on hardware was taken here.
- The `GyroCalStore`/`GyroCalLoad` blob format beyond the `0xDABE` magic and the
  `0xBC` length; nothing observed actually persists it.
