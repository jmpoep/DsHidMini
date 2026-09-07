using Nefarius.DsHidMini.IPC.Models.Drivers;

using Xunit;

namespace Nefarius.DsHidMini.ControlApp.Tests;

public class IdentificationParseTests
{
    // Feature 0x01 dumps from docs/MOTION.md / research/ds3-motion/dumps

    private const string Ds3A1a =
        "00 01 04 00 08 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 03 00 01 02 00 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string Ds3A1b =
        "00 01 04 00 08 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 04 00 01 02 07 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string Ds3A2 =
        "00 01 04 00 0b 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 04 00 01 02 07 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string Ds3E =
        "00 01 04 00 06 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 03 00 01 02 00 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string B1 =
        "00 01 04 01 03 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 03 00 01 02 00 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string Sixaxis1 =
        "00 01 02 00 03 08 01 02 17 17 17 17 09 0a 00 00 " +
        "00 00 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 01 06 00 00 00 00 00 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string Sixaxis2 =
        "00 01 03 00 04 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 01 06 00 00 00 00 00 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string Obigben =
        "00 01 03 00 05 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 02 01 02 00 64 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private const string FakeDs3 =
        "00 01 03 00 05 0c 01 02 18 18 18 18 09 0a 10 11 " +
        "12 13 00 00 00 00 04 00 02 02 02 02 00 00 00 04 " +
        "04 04 04 00 00 02 01 02 00 64 00 17 00 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    [Theory]
    [InlineData("DS3-A1a", Ds3A1a, 0x00040008, 0x18, DsIdentificationMotionPath.PlainZero, false)]
    [InlineData("DS3-A1b", Ds3A1b, 0x00040008, 0x18, DsIdentificationMotionPath.HwCal, false)]
    [InlineData("DS3-A2", Ds3A2, 0x0004000B, 0x18, DsIdentificationMotionPath.HwCal, false)]
    [InlineData("DS3-E", Ds3E, 0x00040006, 0x18, DsIdentificationMotionPath.PlainZero, false)]
    [InlineData("B1", B1, 0x00040103, 0x18, DsIdentificationMotionPath.PlainZero, false)]
    [InlineData("SIXAXIS-1", Sixaxis1, 0x00020003, 0x17, DsIdentificationMotionPath.Sixaxis, false)]
    [InlineData("SIXAXIS-2", Sixaxis2, 0x00030004, 0x18, DsIdentificationMotionPath.Sixaxis, false)]
    [InlineData("Obigben", Obigben, 0x00030005, 0x18, DsIdentificationMotionPath.PlainZero, true)]
    [InlineData("Fake DS3", FakeDs3, 0x00030005, 0x18, DsIdentificationMotionPath.PlainZero, true)]
    public void TryParse_KnownDumps_MatchMotionPathAndCloneHeuristic(
        string name,
        string hex,
        uint firmware,
        byte padType,
        DsIdentificationMotionPath path,
        bool clone)
    {
        byte[] report = ParseHex(hex);

        Assert.False(string.IsNullOrEmpty(name));
        Assert.True(DsIdentification.TryParse(report, out DsIdentificationInfo? info));
        Assert.NotNull(info);
        Assert.Equal(firmware, info!.Firmware);
        Assert.Equal(padType, info.PadType);
        Assert.Equal(path, info.MotionPath);
        Assert.Equal(clone, info.CloneHeuristic);
    }

    [Fact]
    public void TryParse_HwCalWinsOverPlainZero()
    {
        byte[] report = ParseHex(Ds3A1b);

        Assert.True(DsIdentification.TryParse(report, out DsIdentificationInfo? info));
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x07 }, info!.CalibrationFields);
        Assert.Equal(DsIdentificationMotionPath.HwCal, info.MotionPath);
        Assert.False(info.CloneHeuristic);
    }

    [Fact]
    public void TryParse_Sixaxis2UsesFieldListNotTypeBytes()
    {
        byte[] report = ParseHex(Sixaxis2);

        Assert.True(DsIdentification.TryParse(report, out DsIdentificationInfo? info));
        Assert.Equal(0x18, info!.PadType);
        Assert.Equal(new byte[] { 0x06 }, info.CalibrationFields);
        Assert.Equal(DsIdentificationMotionPath.Sixaxis, info.MotionPath);
    }

    [Fact]
    public void TryParse_RejectsTooShortBuffer()
    {
        Assert.False(DsIdentification.TryParse(new byte[0x20], out DsIdentificationInfo? info));
        Assert.Null(info);
    }

    [Fact]
    public void TryParse_RejectsZeroFieldCount()
    {
        byte[] report = ParseHex(Ds3A1a);
        report[0x25] = 0;

        Assert.False(DsIdentification.TryParse(report, out _));
    }

    [Fact]
    public void FirmwareDisplay_FormatsRevisionBytes()
    {
        byte[] report = ParseHex(Ds3A2);

        Assert.True(DsIdentification.TryParse(report, out DsIdentificationInfo? info));
        Assert.Equal("04 00 0B", info!.FirmwareDisplay);
    }

    private static byte[] ParseHex(string hex)
    {
        string[] parts = hex.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            bytes[i] = Convert.ToByte(parts[i], 16);
        }

        Assert.Equal(DsIdentification.ReportLength, bytes.Length);
        return bytes;
    }
}
