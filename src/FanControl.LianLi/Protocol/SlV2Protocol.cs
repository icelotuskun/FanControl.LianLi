namespace FanControl.LianLi.Protocol;

/// <summary>
/// Uni SL v2 (0xA103, 0xA105). L-Connect floors the duty at 10 and sends 1 for off, like the
/// SL-Infinity. Uni AL v2 derives from this and differs only by <see cref="DeviceFamily"/>.
/// </summary>
internal class SlV2Protocol : FanProtocolBase {
    /// <inheritdoc />
    public override DeviceFamily Family => DeviceFamily.SlV2;

    /// <inheritdoc />
    public override int RpmReportOffset => 2;

    /// <inheritdoc />
    protected override byte ManualModeRegister => 98;

    /// <inheritdoc />
    protected override byte ArgbRegister => 97;

    /// <inheritdoc />
    protected override byte DutyByte(int dutyPercent) => FlooredDutyByte(dutyPercent);
}
