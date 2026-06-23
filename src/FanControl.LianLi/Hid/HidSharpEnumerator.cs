using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using FanControl.LianLi.Logging;
using HidSharp;
using HidSharp.Reports;

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
    private readonly ILog _log;

    public HidSharpEnumerator(ILog log) {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public IReadOnlyList<HidDeviceInfo> Locate(
        IReadOnlyList<int> vendorIds,
        IReadOnlyList<int> productIds) {
        var located = new List<HidDeviceInfo>();
        foreach (HidDevice device in DeviceList.Local.GetHidDevices()) {
            if (!vendorIds.Contains(device.VendorID) || !productIds.Contains(device.ProductID)) {
                continue;
            }

            // Only the 0x0416 family needs its usage page probed and filtered; the Uni family is
            // located exactly as before (no extra descriptor read).
            int? usagePage = CommandInterfaceFilter.RequiresUsageFilter(device.VendorID)
                ? TryGetUsagePage(device)
                : null;
            if (!CommandInterfaceFilter.Keep(device.VendorID, usagePage)) {
                continue;
            }

            located.Add(new HidDeviceInfo(
                device.VendorID,
                device.ProductID,
                device.DevicePath,
                device,
                TryGetContainerId(device),
                TryGetMaxOutputReportLength(device),
                usagePage));
        }

        return located;
    }

    // Resolve the physical device's ContainerId so the de-duplicator can collapse a controller's
    // several HID interfaces while keeping distinct controllers apart (even ones sharing a serial).
    // An unresolved id is a missing-metadata case the de-duplicator handles safely (it falls back to
    // the per-interface device path, which never collapses), so log a trace and locate the device
    // either way rather than surface it as a fault.
    private string? TryGetContainerId(HidDevice device) {
        string? containerId = ContainerIdResolver.Resolve(device.DevicePath);
        if (containerId is null) {
            _log.Write("  container-id probe unresolved for " + device.DevicePath + ", de-dup falls back to the device path");
        }

        return containerId;
    }

    private int TryGetMaxOutputReportLength(HidDevice device) {
        try {
            return device.GetMaxOutputReportLength();
        } catch (IOException ex) {
            _log.Write("  output-report-length probe refused for " + device.DevicePath + ": " + ex.Message);
            return 0;
        }
    }

    // The top-level usage page identifies which interface of a multi-interface 0x0416 device is the
    // command interface (0xFF1B). Reading the report descriptor is I/O that a device can refuse or
    // return garbled; a failed probe degrades to "unknown" (the interface is kept) and must never
    // abort enumerating the other devices, so the catch is broad and only logs.
    private int? TryGetUsagePage(HidDevice device) {
        try {
            ReportDescriptor descriptor = device.GetReportDescriptor();
            foreach (DeviceItem item in descriptor.DeviceItems) {
                foreach (uint usage in item.Usages.GetAllValues()) {
                    // A 32-bit HID usage packs the usage page in its high 16 bits.
                    return (int)(usage >> 16);
                }
            }

            return null;
        }
#pragma warning disable CA1031 // best-effort probe: a refused/garbled descriptor must not abort enumerating the other devices
        catch (Exception ex) {
            _log.Write("  usage-page probe refused for " + device.DevicePath + ": " + ex.Message);
            return null;
        }
#pragma warning restore CA1031
    }

    public IHidTransport Open(HidDeviceInfo info) {
        if (info.Device is null) {
            throw new InvalidOperationException("No HID handle for device at " + info.DevicePath);
        }

        if (info.Device.TryOpen(out HidStream stream)) {
            return new HidSharpTransport(stream, info.DevicePath, _log);
        }

        throw new InvalidOperationException("Failed to open HID device at " + info.DevicePath);
    }
}
