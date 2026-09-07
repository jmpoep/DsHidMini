using System.Diagnostics.CodeAnalysis;

namespace Nefarius.DsHidMini.IPC.Models.Drivers;

/// <summary>
///     Decoded DualShock 3 / SIXAXIS Feature 0x01 identification report.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public sealed class DsIdentificationInfo
{
    public DsIdentificationInfo(
        uint firmware,
        byte padType,
        DsIdentificationMotionPath motionPath,
        bool cloneHeuristic,
        byte[] calibrationFields)
    {
        Firmware = firmware;
        PadType = padType;
        MotionPath = motionPath;
        CloneHeuristic = cloneHeuristic;
        CalibrationFields = calibrationFields;
    }

    /// <summary>
    ///     Firmware/board revision packed as <c>b2&lt;&lt;16 | b3&lt;&lt;8 | b4</c>.
    /// </summary>
    public uint Firmware { get; }

    /// <summary>
    ///     First of the four pad/sensor type bytes at offsets 8-11. Informational only.
    /// </summary>
    public byte PadType { get; }

    /// <summary>
    ///     Motion path derived from the calibration field list. Field <c>0x07</c> wins
    ///     over <c>PLAIN_ZERO</c>.
    /// </summary>
    public DsIdentificationMotionPath MotionPath { get; }

    /// <summary>
    ///     <see langword="true"/> if the field list is exactly <c>01 02</c> and byte
    ///     <c>0x29</c> is <c>0x64</c>. Heuristic, not a verdict.
    /// </summary>
    public bool CloneHeuristic { get; }

    /// <summary>
    ///     Calibration field IDs from offset <c>0x26</c>.
    /// </summary>
    public byte[] CalibrationFields { get; }

    /// <summary>
    ///     Firmware bytes formatted as space-separated hex, e.g. <c>04 00 08</c>.
    /// </summary>
    public string FirmwareDisplay =>
        $"{(Firmware >> 16) & 0xFF:X2} {(Firmware >> 8) & 0xFF:X2} {Firmware & 0xFF:X2}";
}

/// <summary>
///     Parses the 64-byte Feature 0x01 identification blob. Rules match
///     <c>DsIdentification_Parse</c> in the driver and <c>docs/MOTION.md</c>.
/// </summary>
public static class DsIdentification
{
    public const int ReportLength = 64;
    public const int MinParseLength = 0x2A;
    public const int FieldCountOffset = 0x25;
    public const int FieldListOffset = 0x26;
    public const int CloneByteOffset = 0x29;
    public const int MaxFields = 8;

    public static bool TryParse(ReadOnlySpan<byte> report, out DsIdentificationInfo? info)
    {
        info = null;

        if (report.Length < MinParseLength)
        {
            return false;
        }

        byte fieldCount = report[FieldCountOffset];
        if (fieldCount == 0 || fieldCount > MaxFields)
        {
            return false;
        }

        int fieldEnd = FieldListOffset + fieldCount;
        if (fieldEnd > report.Length)
        {
            return false;
        }

        byte[] fields = new byte[fieldCount];
        bool hasHwCal = false;
        for (int i = 0; i < fieldCount; i++)
        {
            fields[i] = report[FieldListOffset + i];
            if (fields[i] == 0x07)
            {
                hasHwCal = true;
            }
        }

        bool plainZero = fieldCount >= 2 && fields[0] == 0x01 && fields[1] == 0x02
                         || fieldCount >= 3 && fields[1] == 0x01 && fields[2] == 0x02;

        DsIdentificationMotionPath path;
        if (hasHwCal)
        {
            path = DsIdentificationMotionPath.HwCal;
        }
        else if (!plainZero)
        {
            path = DsIdentificationMotionPath.Sixaxis;
        }
        else
        {
            path = DsIdentificationMotionPath.PlainZero;
        }

        bool clone = fieldCount == 2
                     && fields[0] == 0x01
                     && fields[1] == 0x02
                     && report[CloneByteOffset] == 0x64;

        info = new DsIdentificationInfo(
            ((uint)report[2] << 16) | ((uint)report[3] << 8) | report[4],
            report[8],
            path,
            clone,
            fields);

        return true;
    }
}
