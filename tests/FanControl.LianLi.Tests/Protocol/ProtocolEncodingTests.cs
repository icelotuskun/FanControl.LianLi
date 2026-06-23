using System;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.Protocol;

public class ProtocolEncodingTests {
    // ---- EncodeSetSpeed: exact report prefix per family (transport pads to the feature length) ----
    // Duty byte matches L-Connect: v1 SL/AL send the percent RAW (no floor); v2/SL-Infinity floor a
    // running fan at 10 and send 1 for off (0 -> 1, 1-9 -> 10, 10-100 -> percent).

    [Theory]
    [InlineData(0, 0, new byte[] { 224, 32, 0, 0 })]     // v1 raw: off sends 0
    [InlineData(0, 5, new byte[] { 224, 32, 0, 5 })]     // v1 raw: no floor, 5 stays 5
    [InlineData(0, 50, new byte[] { 224, 32, 0, 50 })]
    [InlineData(0, 100, new byte[] { 224, 32, 0, 100 })]
    [InlineData(3, 100, new byte[] { 224, 35, 0, 100 })] // channel byte 32+3
    public void EncodeSetSpeed_Sl(int channel, int duty, byte[] expected)
        => Assert.Equal(expected, new SlProtocol().EncodeSetSpeed(channel, duty));

    [Theory]
    [InlineData(0, 0, new byte[] { 224, 32, 0, 0 })]     // v1 raw
    [InlineData(0, 5, new byte[] { 224, 32, 0, 5 })]     // v1 raw: no floor
    [InlineData(1, 100, new byte[] { 224, 33, 0, 100 })]
    public void EncodeSetSpeed_Al(int channel, int duty, byte[] expected)
        => Assert.Equal(expected, new AlProtocol().EncodeSetSpeed(channel, duty));

    [Theory]
    [InlineData(0, 0, new byte[] { 224, 32, 0, 1 })]     // off -> 1
    [InlineData(0, 5, new byte[] { 224, 32, 0, 10 })]    // floored at 10
    [InlineData(0, 50, new byte[] { 224, 32, 0, 50 })]
    [InlineData(3, 100, new byte[] { 224, 35, 0, 100 })]
    public void EncodeSetSpeed_SlInfinity(int channel, int duty, byte[] expected)
        => Assert.Equal(expected, new SlInfinityProtocol().EncodeSetSpeed(channel, duty));

    [Theory]
    [InlineData(0, 0, new byte[] { 224, 32, 0, 1 })]     // off -> 1
    [InlineData(0, 5, new byte[] { 224, 32, 0, 10 })]    // floored at 10
    [InlineData(0, 50, new byte[] { 224, 32, 0, 50 })]
    [InlineData(0, 100, new byte[] { 224, 32, 0, 100 })]
    public void EncodeSetSpeed_SlV2(int channel, int duty, byte[] expected)
        => Assert.Equal(expected, new SlV2Protocol().EncodeSetSpeed(channel, duty));

    [Theory]
    [InlineData(0, 0, new byte[] { 224, 32, 0, 1 })]     // off -> 1
    [InlineData(0, 5, new byte[] { 224, 32, 0, 10 })]    // floored at 10
    [InlineData(0, 100, new byte[] { 224, 32, 0, 100 })]
    public void EncodeSetSpeed_AlV2(int channel, int duty, byte[] expected)
        => Assert.Equal(expected, new AlV2Protocol().EncodeSetSpeed(channel, duty));

    [Fact]
    public void EncodeSetSpeed_ClampsDutyToZeroToHundred() {
        var protocol = new SlProtocol();
        Assert.Equal(protocol.EncodeSetSpeed(0, 0), protocol.EncodeSetSpeed(0, -10));
        Assert.Equal(protocol.EncodeSetSpeed(0, 100), protocol.EncodeSetSpeed(0, 150));
    }

    // ---- EncodeManualMode: SetFanMotherboardSync(ch, off) - 6-byte prefix, channel bit 1<<(ch+4) ----

    [Theory]
    [InlineData(0, 0x10)]
    [InlineData(1, 0x20)]
    [InlineData(2, 0x40)]
    [InlineData(3, 0x80)] // regression lock: the upstream (2*ch)*16 bug gave 0x60 here
    public void EncodeManualMode_ChannelByteIsBitShift(int channel, int expectedChannelByte) {
        byte[] report = new SlProtocol().EncodeManualMode(channel);
        Assert.Equal((byte)expectedChannelByte, report[3]);
    }

    [Theory]
    [InlineData(typeof(SlProtocol), 49)]
    [InlineData(typeof(AlProtocol), 66)]
    [InlineData(typeof(SlInfinityProtocol), 98)]
    [InlineData(typeof(SlV2Protocol), 98)]
    [InlineData(typeof(AlV2Protocol), 98)]
    public void EncodeManualMode_RegisterPerFamily(Type protocolType, int expectedRegister) {
        var protocol = (IFanProtocol)Activator.CreateInstance(protocolType)!;
        byte[] report = protocol.EncodeManualMode(0);
        // L-Connect's SetFanMotherboardSync(0, false): {224, 16, register, 0x10, 0, 0}.
        Assert.Equal(new byte[] { 224, 16, (byte)expectedRegister, 0x10, 0, 0 }, report);
    }

    // ---- EncodeArgbSync: {224,16,reg,on,0,0,0} per family ----

    [Theory]
    [InlineData(typeof(SlProtocol), 48)]
    [InlineData(typeof(AlProtocol), 65)]
    [InlineData(typeof(SlInfinityProtocol), 97)]
    [InlineData(typeof(SlV2Protocol), 97)]
    [InlineData(typeof(AlV2Protocol), 97)]
    public void EncodeArgbSync_PerFamily(Type protocolType, int expectedRegister) {
        var protocol = (IFanProtocol)Activator.CreateInstance(protocolType)!;
        Assert.Equal(new byte[] { 224, 16, (byte)expectedRegister, 1, 0, 0, 0 }, protocol.EncodeArgbSync(true));
        Assert.Equal(new byte[] { 224, 16, (byte)expectedRegister, 0, 0, 0, 0 }, protocol.EncodeArgbSync(false));
    }

    // ---- DecodeRpm: big-endian pair at the family offset ----

    [Fact]
    public void DecodeRpm_Offset1_BigEndian() {
        var buffer = new byte[65];
        buffer[1] = 0x0A; // ch0 high
        buffer[2] = 0x28; // ch0 low  -> (10<<8)|40 = 2600
        buffer[7] = 0x01; // ch3 high (offset 1 + 3*2 = 7)
        buffer[8] = 0x2C; // ch3 low  -> (1<<8)|44 = 300

        var protocol = new SlProtocol();
        Assert.Equal(2600f, protocol.DecodeRpm(buffer, 0));
        Assert.Equal(300f, protocol.DecodeRpm(buffer, 3));
    }

    [Fact]
    public void DecodeRpm_Offset2_ForV2() {
        var buffer = new byte[65];
        buffer[2] = 0x0A; // ch0 high (offset 2)
        buffer[3] = 0x28; // ch0 low -> 2600

        Assert.Equal(2600f, new SlV2Protocol().DecodeRpm(buffer, 0));
        Assert.Equal(2600f, new AlV2Protocol().DecodeRpm(buffer, 0));
    }

    [Theory]
    [InlineData(typeof(SlProtocol), 1)]
    [InlineData(typeof(AlProtocol), 1)]
    [InlineData(typeof(SlInfinityProtocol), 1)]
    [InlineData(typeof(SlV2Protocol), 2)]
    [InlineData(typeof(AlV2Protocol), 2)]
    public void RpmReportOffset_PerFamily(Type protocolType, int expectedOffset) {
        var protocol = (IFanProtocol)Activator.CreateInstance(protocolType)!;
        Assert.Equal(expectedOffset, protocol.RpmReportOffset);
    }

    [Theory]
    [InlineData(typeof(SlProtocol))]
    [InlineData(typeof(AlProtocol))]
    [InlineData(typeof(SlInfinityProtocol))]
    [InlineData(typeof(SlV2Protocol))]
    [InlineData(typeof(AlV2Protocol))]
    public void EncodeRpmPrimer_IsE05000_ForEveryUniFamily(Type protocolType) {
        var protocol = (IFanProtocol)Activator.CreateInstance(protocolType)!;

        // L-Connect's "prepare input report" feature report prefix; the transport pads it to the
        // device's feature report length before the transfer.
        Assert.Equal(new byte[] { 0xE0, 0x50, 0x00 }, protocol.EncodeRpmPrimer());
    }

    [Fact]
    public void ChannelCount_IsFour()
        => Assert.Equal(4, new SlProtocol().ChannelCount);

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void EncodeSetSpeed_ThrowsForChannelOutOfRange(int channel)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SlProtocol().EncodeSetSpeed(channel, 50));
}
