using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FanControl.LianLi.Logging;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace FanControl.LianLi.Hid;

/// <summary>
/// The only class that performs I/O against a HidSharp <see cref="HidStream"/>.
/// Writes go through the stream. RPM telemetry is read with a GET_REPORT(Input)
/// control transfer (<c>HidD_GetInputReport</c>) on a raw device handle opened
/// alongside the stream.
/// </summary>
// Excluded from coverage: this is the real USB I/O boundary over HidSharp and
// Win32. It is exercised only against physical hardware; the rest of the plugin
// is tested through the IHidTransport fake.
[ExcludeFromCodeCoverage]
internal sealed class HidSharpTransport : IHidTransport {
    // Cap an interrupt-IN read so a 0x0416 device that goes silent (unplugged, asleep)
    // cannot block the worker thread indefinitely. A local USB handshake answers in
    // tens of milliseconds; this is a generous ceiling, not the expected latency.
    private const int InterruptReadTimeoutMilliseconds = 500;

    // Cap a stream write for the same reason: without it a write inherits HidSharp's multi-second
    // default, so a controller that is mid-re-enumeration after a sleep/wake stalls each keepalive
    // write for seconds before failing - and because the worker services controllers sequentially on
    // one thread, that backs up every other controller and presents as a frozen UI. A local USB
    // write completes in milliseconds, so this ceiling only ever bites a device that is not ready.
    private const int StreamWriteTimeoutMilliseconds = 500;

    // HidD_GetInputReport / HidD_SetFeature are synchronous control transfers with no timeout
    // parameter, so on a stale handle (the device re-enumerated across sleep/wake) they block
    // forever - freezing the keepalive thread, which is the hibernate hang. The stream timeouts
    // above cannot reach this path; it runs on the raw input handle, not the HidStream. So these
    // transfers run under a bounded wait, and on timeout the pending I/O is cancelled so the handle
    // is released cleanly rather than pinned (a pinned handle blocks the next Open() after wake).
    private const int ControlTransferTimeoutMilliseconds = 500;

    // L-Connect sleeps writeDelayTime=20ms after every feature write before the next transfer; without
    // it back-to-back writes can be dropped by the controller. Applied after each feature write (the
    // fan-control and primer path) to match the vendor's pacing. Runs on the worker thread, not the host.
    private const int WriteDelayMilliseconds = 20;

    // Fallback feature report length when the descriptor probe fails: the input report length, which
    // exceeds any real feature length, and HidD_SetFeature accepts a buffer longer than the report.
    private const int DefaultFeatureReportLength = 65;

    private readonly HidStream _stream;
    private readonly SafeFileHandle _inputHandle;
    private readonly ILog _log;

    // The device's feature report byte length. HidD_SetFeature requires the buffer to be at least this
    // long, so a short command prefix (set-speed, manual-mode, primer) is padded up to it.
    private readonly int _featureReportLength;

    // Once one control transfer times out the handle is stale and every later transfer on it will
    // hang too. Latch that so subsequent calls fail fast instead of spawning a fresh watchdog every
    // tick; the controller recovers when the plugin reopens the device on its next Initialize.
    private volatile bool _controlTransferFaulted;

    public HidSharpTransport(HidStream stream, string devicePath, ILog log) {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (string.IsNullOrEmpty(devicePath)) {
            throw new ArgumentException("Device path is required.", nameof(devicePath));
        }

        // Only the 0x0416 family reads from the stream; the Uni family never calls Read.
        // Bounding it here is harmless for the non-readers and keeps a stalled telemetry
        // read from freezing FanControl.
        _stream.ReadTimeout = InterruptReadTimeoutMilliseconds;

        // Bound writes too: a controller re-enumerating after sleep/wake otherwise stalls each
        // keepalive write for HidSharp's multi-second default before failing, which the worker
        // isolates per controller but only after the full stall.
        _stream.WriteTimeout = StreamWriteTimeoutMilliseconds;

        // Resolve the feature report length once so feature writes can be padded up to it.
        _featureReportLength = TryGetFeatureReportLength(devicePath);

        // The Lian Li controllers do NOT stream interrupt-IN reports, so HidStream.Read
        // times out on real hardware; and NuGet HidSharp 2.6.2 does not expose
        // GetInputReport. RPM is therefore pulled with a GET_REPORT(Input) control
        // transfer (HidD_GetInputReport) on a second raw handle opened on the same
        // device path. Verified on hardware to coexist with the HidSharp write stream.
        _inputHandle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (_inputHandle.IsInvalid) {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "Failed to open HID input handle at {0} (error {1}).",
                devicePath,
                Marshal.GetLastWin32Error()));
        }
    }

    public bool CanWrite => _stream.CanWrite;

    public void Write(byte[] report) {
        if (!_stream.CanWrite) {
            return;
        }

        _stream.Write(report);
    }

    public void SetFeature(byte[] report) {
        if (!_stream.CanWrite) {
            return;
        }

        // SET_REPORT(Feature) on the same raw handle used for input reports. Set-speed, manual-mode,
        // the RPM primer, and the lighting effect commands are all feature reports; byte 0 is the
        // report id (0xE0). Pad a short command prefix up to the device's feature report length, which
        // HidD_SetFeature requires (it rejects a buffer shorter than the report).
        byte[] buffer = PadToFeatureLength(report);
        RunControlTransfer(
            () => NativeMethods.HidD_SetFeature(_inputHandle, buffer, buffer.Length),
            "HidD_SetFeature");

        // Match L-Connect's 20ms post-write settle so the next transfer is not dropped.
        Thread.Sleep(WriteDelayMilliseconds);
    }

    // Pad a short command prefix up to the device's feature report length (zero-filled). A buffer that
    // already meets or exceeds the length is sent as-is (HidD_SetFeature accepts an over-long buffer).
    private byte[] PadToFeatureLength(byte[] report) {
        if (report.Length >= _featureReportLength) {
            return report;
        }

        var padded = new byte[_featureReportLength];
        Array.Copy(report, padded, report.Length);
        return padded;
    }

    // Probe the device's feature report length. Reading the descriptor is I/O the device can refuse; a
    // refused probe falls back to the input report length, a safe upper bound on any real feature report.
    // Mirrors the sibling output-report-length probe in HidSharpEnumerator: a refused probe is logged,
    // never silently swallowed.
    private int TryGetFeatureReportLength(string devicePath) {
        try {
            int length = _stream.Device.GetMaxFeatureReportLength();
            return length > 0 ? length : DefaultFeatureReportLength;
        } catch (IOException ex) {
            _log.Write("  feature-report-length probe refused for " + devicePath + ": " + ex.Message);
            return DefaultFeatureReportLength;
        }
    }

    public byte[] GetInputReport(byte reportId, int length) {
        // GET_REPORT(Input): byte 0 selects the report id, the device fills the rest.
        // The decoder skips the leading id via its per-family RPM offset. The device
        // answers this pull on demand even though it never streams input reports.
        byte[] buffer = new byte[length];
        buffer[0] = reportId;
        RunControlTransfer(
            () => NativeMethods.HidD_GetInputReport(_inputHandle, buffer, buffer.Length),
            string.Format(CultureInfo.InvariantCulture, "HidD_GetInputReport(0x{0:X2})", reportId));

        return buffer;
    }

    // Run a synchronous HID control transfer under a bounded wait. The native call has no timeout,
    // so on a stale handle it blocks forever; BoundedHidCall runs it on a throwaway thread and, on
    // timeout, cancels the pending I/O via CancelIoEx so the abandoned thread unwinds and the handle
    // is released (rather than pinned, which would block the next Open() after wake). A timeout
    // latches the fault so later transfers fail fast; the worker isolates the throw and the
    // controller recovers when the plugin reopens the device on its next Initialize.
    private void RunControlTransfer(Func<bool> nativeCall, string operation) {
        if (_controlTransferFaulted) {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} skipped: HID control-transfer handle faulted; device needs reinitialization.",
                operation));
        }

        bool succeeded = false;
        int lastError = 0;

        bool completed = BoundedHidCall.TryRun(
            () => {
                succeeded = nativeCall();
                if (!succeeded) {
                    // GetLastError is thread-local, so capture it here, on the P/Invoke thread.
                    lastError = Marshal.GetLastWin32Error();
                }
            },
            ControlTransferTimeoutMilliseconds,
            () => NativeMethods.CancelIoEx(_inputHandle, IntPtr.Zero));

        if (!completed) {
            _controlTransferFaulted = true;
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} timed out after {1} ms; device unresponsive (re-enumerating?).",
                operation,
                ControlTransferTimeoutMilliseconds));
        }

        if (!succeeded) {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture, "{0} failed (error {1}).", operation, lastError));
        }
    }

    public byte[] Read(int length) {
        // Interrupt-IN read for the 0x0416 command-packet family: after a command write the
        // device answers on its input endpoint. HidStream.Read blocks up to the stream's
        // ReadTimeout (set in the constructor), so a silent device surfaces as a timeout the
        // worker isolates rather than a hang. byte 0 of the reply is the report id (0x01).
        byte[] buffer = new byte[length];
        int read = _stream.Read(buffer, 0, buffer.Length);
        if (read <= 0) {
            throw new IOException("HID interrupt-IN read returned no data.");
        }

        return buffer;
    }

    public void Dispose() {
        _stream.Dispose();
        _inputHandle.Dispose();
    }

    private static class NativeMethods {
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint OpenExisting = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeFileHandle CreateFile(
            string path,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        // Cancel pending I/O on the handle (overlapped = NULL cancels all the process queued on it).
        // Used to unblock a control transfer abandoned on timeout so its thread unwinds and the
        // handle is released, instead of the transfer pinning a stale handle across a sleep/wake.
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

        // GET_REPORT(Input) control transfer. buffer[0] must be the report id on entry.
        [DllImport("hid.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetInputReport(SafeFileHandle handle, byte[] buffer, int bufferLength);

        // SET_REPORT(Feature) control transfer. buffer[0] is the report id.
        [DllImport("hid.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int bufferLength);
    }
}
