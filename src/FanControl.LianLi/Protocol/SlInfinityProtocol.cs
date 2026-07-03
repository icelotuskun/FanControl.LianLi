namespace FanControl.LianLi.Protocol;

/// <summary>Uni SL-Infinity (0xA102). L-Connect floors the duty at 10 and sends 1 for off.</summary>
internal sealed class SlInfinityProtocol : FanProtocolBase {
    /// <inheritdoc />
    public override DeviceFamily Family => DeviceFamily.SlInfinity;

    /// <inheritdoc />
    public override int RpmReportOffset => 1;

    /// <inheritdoc />
    protected override byte ManualModeRegister => 98;

    /// <inheritdoc />
    protected override byte ArgbRegister => 97;

    /// <inheritdoc />
    protected override byte DutyByte(int dutyPercent, bool startStopEnabled) => FlooredDutyByte(dutyPercent, startStopEnabled);
}
