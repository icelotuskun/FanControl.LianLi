using System.Collections.Generic;

namespace FanControl.LianLi.Hid;

/// <summary>
/// Discovers Lian Li controllers and opens transports for them. Implemented
/// once against HidSharp; faked in tests.
/// </summary>
internal interface IHidDeviceEnumerator {
    /// <summary>
    /// Locate every connected HID device whose vendor and product ids both
    /// appear in the supplied allow-lists.
    /// </summary>
    IReadOnlyList<HidDeviceInfo> Locate(
        IReadOnlyList<int> vendorIds,
        IReadOnlyList<int> productIds);

    /// <summary>Open a writable/readable transport for a located device.</summary>
    IHidTransport Open(HidDeviceInfo info);
}
