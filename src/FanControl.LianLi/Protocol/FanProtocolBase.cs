using System;

namespace FanControl.LianLi.Protocol;

/// <summary>
/// Shares the report framing common to every Uni family. Concrete families supply only the
/// duty-to-byte mapping (raw on the v1 SL/AL, floored-at-10 with 1 for off on the v2/SL-Infinity)
/// and the family-specific register bytes. The byte layout is the controllers' fixed wire protocol -
/// a hardware fact, not a stylistic choice; do not change it.
/// </summary>
internal abstract class FanProtocolBase : IFanProtocol {
    /// <summary>Report id every Uni report begins with (0xE0).</summary>
    protected const byte ReportId = 224;

    /// <summary>Sub-command byte for the manual-mode and ARGB-sync reports (0x10).</summary>
    protected const byte ConfigCommand = 16;

    /// <summary>Base for the set-speed channel byte: byte 1 is 32 + channel.</summary>
    protected const byte SpeedChannelBase = 32;

    /// <summary>
    /// "Prepare an input report" command (0x50). L-Connect sends the feature report
    /// [ReportId, 0x50, 0x00] before every RPM read for the whole Uni family; the 0x00 sub-arg
    /// selects the RPM report (0x01 would select firmware version).
    /// </summary>
    private const byte PrepareInputReportCommand = 0x50;

    /// <inheritdoc />
    public abstract DeviceFamily Family { get; }

    /// <inheritdoc />
    public int ChannelCount => 4;

    /// <inheritdoc />
    public abstract int RpmReportOffset { get; }

    /// <summary>Family-specific disable-PWM register byte (third byte of the manual-mode report).</summary>
    protected abstract byte ManualModeRegister { get; }

    /// <summary>Family-specific ARGB-sync register byte.</summary>
    protected abstract byte ArgbRegister { get; }

    /// <summary>
    /// Map a FanControl duty percent (0-100) to the family's set-speed byte. L-Connect sends the duty
    /// raw on the v1 SL/AL families and floored-at-10 (1 = off) on the v2/SL-Infinity families; each
    /// family implements the matching one via <see cref="RawDutyByte"/> or <see cref="FlooredDutyByte"/>.
    /// </summary>
    protected abstract byte DutyByte(int dutyPercent);

    /// <summary>
    /// v1 SL/AL/Redragon: L-Connect's controller sends the curve value straight to SetFanSpeed
    /// (Math.Max(speed, 0)), so the duty percent goes out raw, clamped to 0-100, with no spin floor.
    /// </summary>
    protected static byte RawDutyByte(int dutyPercent) => (byte)Clamp(dutyPercent, 0, 100);

    /// <summary>
    /// v2/SL-Infinity: L-Connect's controller floors a running fan at 10 and sends 1 for off
    /// (<c>num == 0 ? 1 : Math.Max(10, percent)</c>). So 0 maps to 1, 1-9 map to 10, and 10-100 pass
    /// through.
    /// </summary>
    protected static byte FlooredDutyByte(int dutyPercent) {
        if (dutyPercent <= 0) {
            return 1;
        }

        // Clamp's lower bound IS the floor of 10, mirroring L-Connect's Math.Max(10, percent).
        return (byte)Clamp(dutyPercent, 10, 100);
    }

    /// <inheritdoc />
    public byte[] EncodeSetSpeed(int channel, int dutyPercent) {
        ValidateChannel(channel);

        // L-Connect's SetFanSpeed: feature report [0xE0, 0x20|channel, 0, dutyByte]. The duty byte is
        // family-specific (see DutyByte). The transport pads this 4-byte prefix up to the device's
        // feature report length before the SET_REPORT(Feature) transfer.
        return new byte[] { ReportId, (byte)(SpeedChannelBase + channel), 0, DutyByte(dutyPercent) };
    }

    /// <inheritdoc />
    public byte[] EncodeManualMode(int channel) {
        ValidateChannel(channel);

        // L-Connect's SetFanMotherboardSync(channel, isSync: false): take the channel off motherboard
        // sync so the host owns its speed. Byte 3 selects the channel in the high nibble
        // (1 << (channel+4): ch0=0x10 .. ch3=0x80) and leaves the sync bit (low nibble) clear = off.
        // Feature report; the transport pads this 6-byte prefix to the feature report length.
        byte channelByte = (byte)(1 << (channel + 4));
        return new byte[] { ReportId, ConfigCommand, ManualModeRegister, channelByte, 0, 0 };
    }

    /// <inheritdoc />
    public byte[] EncodeArgbSync(bool on) {
        return new byte[] { ReportId, ConfigCommand, ArgbRegister, (byte)(on ? 1 : 0), 0, 0, 0 };
    }

    /// <inheritdoc />
    public byte[] EncodeRpmPrimer() {
        // The Uni controllers do not stream their input report: HidD_GetInputReport returns a stale
        // idle buffer until this feature report asks the device to refresh it. L-Connect sends the
        // identical [0xE0, 0x50, 0x00] feature report before every RPM read for every Uni family
        // (SL, AL, SLv2, ALv2, SL-Infinity). Some revisions return live RPM without it (verified
        // harmless on SL-Infinity v1.4 here); others return 0/garbage until primed - priming matches
        // the vendor and is safe either way. The transport pads this prefix to the feature length.
        return new byte[] { ReportId, PrepareInputReportCommand, 0 };
    }

    /// <inheritdoc />
    public float DecodeRpm(byte[] inputReport, int channel) {
        ValidateChannel(channel);
        int offset = RpmReportOffset + (channel * 2);
        return (float)((inputReport[offset] << 8) | inputReport[offset + 1]); // big-endian
    }

    private void ValidateChannel(int channel) {
        if (channel < 0 || channel >= ChannelCount) {
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel, "Channel must be in the range 0-3.");
        }
    }

    // Math.Clamp is unavailable on netstandard2.0, so the clamp is hand-rolled.
    private static int Clamp(int value, int min, int max) {
        if (value < min) {
            return min;
        }

        return value > max ? max : value;
    }
}
