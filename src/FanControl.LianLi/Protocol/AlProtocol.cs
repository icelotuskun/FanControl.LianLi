namespace FanControl.LianLi.Protocol;

/// <summary>Uni AL (0xA101). Same duty curve as SL; different register bytes.</summary>
internal sealed class AlProtocol : FanProtocolBase {
    /// <inheritdoc />
    public override DeviceFamily Family => DeviceFamily.Al;

    /// <inheritdoc />
    public override int RpmReportOffset => 1;

    /// <inheritdoc />
    protected override byte ManualModeRegister => 66;

    /// <inheritdoc />
    protected override byte ArgbRegister => 65;

    /// <inheritdoc />
    protected override byte DutyByte(int dutyPercent) => (byte)((800 + (11 * dutyPercent)) / 19);
}
