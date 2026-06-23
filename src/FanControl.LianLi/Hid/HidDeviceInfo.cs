using HidSharp;

namespace FanControl.LianLi.Hid;

/// <summary>
/// A located HID device: the identifying ids plus an opaque handle the
/// enumerator uses to open a transport. The underlying HidSharp handle is
/// kept internal so no HidSharp type leaks past the <c>Hid/</c> layer.
/// </summary>
internal sealed class HidDeviceInfo {
    public HidDeviceInfo(
        int vendorId,
        int productId,
        string devicePath,
        HidDevice? device,
        string? containerId = null,
        int maxOutputReportLength = 0,
        int? usagePage = null) {
        VendorId = vendorId;
        ProductId = productId;
        DevicePath = devicePath;
        Device = device;
        ContainerId = containerId;
        MaxOutputReportLength = maxOutputReportLength;
        UsagePage = usagePage;
    }

    /// <summary>USB vendor id (0x0CF2 for the Lian Li Uni family).</summary>
    public int VendorId { get; }

    /// <summary>USB product id identifying the controller family.</summary>
    public int ProductId { get; }

    /// <summary>OS device path. Used for logging and to open the raw handle for RPM input reports.</summary>
    public string DevicePath { get; }

    /// <summary>
    /// The Windows ContainerId of the physical device, or null when it could not be resolved. Every
    /// HID interface a single controller exposes shares this GUID and it differs across physical
    /// controllers, so it is the de-duplication key (see <see cref="HidDeviceDeduplicator"/>). The
    /// USB serial is deliberately not used: the Lian Li Uni controllers all report the same
    /// firmware-fixed serial, which would wrongly collapse distinct controllers into one.
    /// </summary>
    public string? ContainerId { get; }

    /// <summary>
    /// Maximum output-report length the interface accepts. When one physical controller exposes
    /// several HID interfaces, the fan-control interface is the one that accepts output reports, so
    /// the largest value is used to pick the interface to keep during de-duplication.
    /// </summary>
    public int MaxOutputReportLength { get; }

    /// <summary>
    /// The interface's top-level HID usage page, or null when it could not be read. The 0x0416
    /// family exposes several interfaces; only its vendor-defined command page (0xFF1B) accepts the
    /// command packets, so this is used to drop the other interfaces during location. Null for the
    /// Uni family (which does not need usage filtering) and for test-constructed instances.
    /// </summary>
    public int? UsagePage { get; }

    /// <summary>
    /// The HidSharp handle the enumerator uses to open a stream. Null only for
    /// test-constructed instances that go through a fake enumerator.
    /// </summary>
    public HidDevice? Device { get; }
}
