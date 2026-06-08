namespace FanControl.LianLi.Protocol;

/// <summary>
/// Uni SL v2 (0xA103, 0xA105). Duty curve 250-2000 rpm. The 17.5 multiplier
/// must stay a double inside the divide before the truncating byte cast - that
/// truncation is part of the device's duty contract. Uni AL v2 derives from this
/// and differs only by <see cref="DeviceFamily"/>.
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
    protected override byte DutyByte(int dutyPercent) => (byte)((250 + (17.5 * dutyPercent)) / 20);
}
