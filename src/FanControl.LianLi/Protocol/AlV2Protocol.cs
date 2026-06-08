namespace FanControl.LianLi.Protocol;

/// <summary>
/// Uni AL v2 (0xA104). Identical to <see cref="SlV2Protocol"/> in duty curve,
/// register bytes, and RPM offset; only the product id and reported family differ.
/// </summary>
internal sealed class AlV2Protocol : SlV2Protocol {
    /// <inheritdoc />
    public override DeviceFamily Family => DeviceFamily.AlV2;
}
