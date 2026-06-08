using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HidSharp;

namespace FanControl.LianLi.Hid;

/// <summary>
/// The only enumeration code that touches HidSharp. Devices are matched on the
/// strongly-typed <see cref="HidDevice.VendorID"/> / <see cref="HidDevice.ProductID"/>
/// properties rather than by parsing the device-path string, which avoids fragile
/// device-path substring parsing.
/// </summary>
// Excluded from coverage: this enumerates real USB devices via HidSharp and is
// verified against physical hardware; tests drive everything else through the
// IHidDeviceEnumerator fake.
[ExcludeFromCodeCoverage]
internal sealed class HidSharpEnumerator : IHidDeviceEnumerator {
    public IReadOnlyList<HidDeviceInfo> Locate(
        IReadOnlyList<int> vendorIds,
        IReadOnlyList<int> productIds) {
        var located = new List<HidDeviceInfo>();
        foreach (HidDevice device in DeviceList.Local.GetHidDevices()) {
            if (vendorIds.Contains(device.VendorID) && productIds.Contains(device.ProductID)) {
                located.Add(new HidDeviceInfo(
                    device.VendorID,
                    device.ProductID,
                    device.DevicePath,
                    device));
            }
        }

        return located;
    }

    public IHidTransport Open(HidDeviceInfo info) {
        if (info.Device is null) {
            throw new InvalidOperationException("No HID handle for device at " + info.DevicePath);
        }

        if (info.Device.TryOpen(out HidStream stream)) {
            return new HidSharpTransport(stream, info.DevicePath);
        }

        throw new InvalidOperationException("Failed to open HID device at " + info.DevicePath);
    }
}
