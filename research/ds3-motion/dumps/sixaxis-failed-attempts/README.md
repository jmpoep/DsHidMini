# SIXAXIS runs that did not complete

Original SIXAXIS units go dead on the bus a few seconds after plug-in when bound to
WinUSB (selective suspend, see `docs/MOTION.md`, "What sixaxis.sys actually does" /
tooling notes). These logs document that failure mode; the one complete SIXAXIS dump is
`../sixaxis_20260906-164338.txt`. Five further all-fail logs from the same session
(163518, 163715, 163743, 163925, 164225) were identical to 163329 and were dropped.

- `sixaxis_20260906-163329.txt` - SIXAXIS-1 already suspended: every EP0 request, including
  `GET_DESCRIPTOR(device)`, fails with Win32 error 31 (`ERROR_GEN_FAILURE`).
- `sixaxis_20260906-164049.txt` - SIXAXIS-1 caught with `--wait` right after plug-in: Feature
  0x01/F2/F5/F7/F8 and EEPROM pages 0x00-0x40 read fine, then the pad died mid-sweep.
- `sixaxis_20260906-172427.txt` + `_stream.csv` - SIXAXIS-2 (type bytes `18 18 18 18` but a
  single calibration field `06`). All feature reports and EEPROM pages were read, so it feeds
  the pad matrix; the stream died after 8 reports (raw gyro pinned at 6 while the probe put the
  cal byte at output bytes [5,6], i.e. the DS3 placement - the SIXAXIS path expects [3,4]).

Bluetooth device/host addresses are redacted (`xx`) as in the parent folder.
