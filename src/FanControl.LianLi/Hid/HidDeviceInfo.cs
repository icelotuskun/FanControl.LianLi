using HidSharp;

namespace FanControl.LianLi.Hid;

/// <summary>
/// A located HID device: the identifying ids plus an opaque handle the
/// enumerator uses to open a transport. The underlying HidSharp handle is
/// kept internal so no HidSharp type leaks past the <c>Hid/</c> layer.
/// </summary>
internal sealed class HidDeviceInfo {
    public HidDeviceInfo(int vendorId, int productId, string devicePath, HidDevice? device) {
        VendorId = vendorId;
        ProductId = productId;
        DevicePath = devicePath;
        Device = device;
    }

    /// <summary>USB vendor id (0x0CF2 for the Lian Li Uni family).</summary>
    public int VendorId { get; }

    /// <summary>USB product id identifying the controller family.</summary>
    public int ProductId { get; }

    /// <summary>OS device path. Used for logging and to open the raw handle for RPM input reports.</summary>
    public string DevicePath { get; }

    /// <summary>
    /// The HidSharp handle the enumerator uses to open a stream. Null only for
    /// test-constructed instances that go through a fake enumerator.
    /// </summary>
    public HidDevice? Device { get; }
}
