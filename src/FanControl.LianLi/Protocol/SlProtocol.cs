namespace FanControl.LianLi.Protocol;

/// <summary>Uni Hub (0x7750) and Uni SL (0xA100). Duty curve 800-1900 rpm.</summary>
internal sealed class SlProtocol : FanProtocolBase
{
    /// <inheritdoc />
    public override DeviceFamily Family => DeviceFamily.Sl;

    /// <inheritdoc />
    public override int RpmReportOffset => 1;

    /// <inheritdoc />
    protected override byte ManualModeRegister => 49;

    /// <inheritdoc />
    protected override byte ArgbRegister => 48;

    /// <inheritdoc />
    protected override byte DutyByte(int dutyPercent) => (byte)((800 + (11 * dutyPercent)) / 19);
}
