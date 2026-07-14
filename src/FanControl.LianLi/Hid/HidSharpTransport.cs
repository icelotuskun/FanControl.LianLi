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

    // A hard watchdog above HidSharp's own stream Read/WriteTimeout. Those timeouts USUALLY fail a
    // stalled call fast, but a HidStream freshly reopened onto a just-woken device has been observed to
    // block a Write straight through its WriteTimeout - never returning - which wedges the single worker
    // thread and freezes the whole plugin (the sleep/wake hang). So each stream call also runs under
    // BoundedHidCall: if it does not return within this ceiling the worker abandons it (closing the
    // stream to unblock the abandoned call) and reopens. Set above the 500 ms stream timeout so the
    // stream's own timeout is the normal fail-fast and this only ever bites a true hang.
    private const int StreamWatchdogTimeoutMilliseconds = 1500;

    // HidD_GetInputReport / HidD_SetFeature are synchronous control transfers with no timeout
    // parameter, so on a stale handle (the device re-enumerated across sleep/wake) they block
    // forever - freezing the keepalive thread, which is the hibernate hang. The stream timeouts
    // above cannot reach this path; it runs on the raw input handle, not the HidStream. So these
    // transfers run under a bounded wait, and on timeout the pending I/O is cancelled so the handle
    // is released cleanly rather than pinned (a pinned handle blocks the next Open() after wake).
    private const int ControlTransferTimeoutMilliseconds = 500;

    // Once the control-transfer handle has latched a fault, the transport tries to reopen it in place
    // so the controller recovers WITHOUT the host reinitializing the plugin - which FanControl does
    // not reliably do after a sleep/wake or after another app (e.g. SignalRGB) re-enumerates the
    // shared device. Backed off so a device that stays absent is retried at this cadence, not on
    // every worker tick.
    private const int RecoveryBackoffMilliseconds = 2000;

    // L-Connect sleeps writeDelayTime=20ms after every feature write before the next transfer; without
    // it back-to-back writes can be dropped by the controller. Applied after each feature write (the
    // fan-control and primer path) to match the vendor's pacing. Runs on the worker thread, not the host.
    private const int WriteDelayMilliseconds = 20;

    // Fallback feature report length when the descriptor probe fails: the input report length, which
    // exceeds any real feature length, and HidD_SetFeature accepts a buffer longer than the report.
    private const int DefaultFeatureReportLength = 65;

    // Not readonly: reopened in place when a stream transfer has latched a fault (self-heal), the same
    // way _inputHandle is for the control-transfer path. All access is on the serialized worker tick.
    private HidStream _stream;
    private readonly ILog _log;

    // The HidDevice the stream was opened from, kept as a fallback for reopening the stream in place
    // after a fault. The reopen prefers a FRESH lookup by device path (the cached instance is bound to
    // the pre-sleep enumeration and a stream reopened from it has been seen to hang); this is used only
    // when that lookup finds nothing.
    private readonly HidDevice _device;

    // The OS device path, kept so the raw input handle can be reopened in place when a control
    // transfer has latched a fault (self-heal), rather than only on the host's next Initialize.
    private readonly string _devicePath;

    // The raw handle the control transfers run on. Not readonly: it is replaced when the handle is
    // reopened to recover from a latched fault. All access is on the serialized worker tick.
    private SafeFileHandle _inputHandle;

    // The device's feature report byte length. HidD_SetFeature requires the buffer to be at least this
    // long, so a short command prefix (set-speed, manual-mode, primer) is padded up to it.
    private readonly int _featureReportLength;

    // Once one control transfer times out the handle is stale and every later transfer on it will
    // hang too. Latch that so subsequent calls fail fast instead of spawning a fresh watchdog every
    // tick. The transport then attempts a backed-off reopen to clear the latch itself; failing that,
    // it also clears when the plugin reopens the device on its next Initialize.
    private volatile bool _controlTransferFaulted;

    // Environment.TickCount of the last reopen attempt, to throttle retries to RecoveryBackoff.
    private int _lastRecoveryTick;

    // Log a failed reopen only once per fault episode, so a long device absence does not spam.
    private bool _recoveryLogged;

    // The HidStream self-heal mirrors the control-transfer one above, for the 0x0416 command-packet
    // family that does its I/O through _stream (Write/Read) rather than the raw input handle. A stream
    // left stale by a sleep/wake either times out on every call forever OR blocks a call straight
    // through its own Read/WriteTimeout (observed on a stream reopened onto a just-woken device), with
    // no recovery either way (the control-transfer latch is not on this path). RunStreamCall bounds
    // every call with a watchdog and latches this fault; the next call reopens the stream in place at
    // the recovery cadence, so the controller heals itself even when FanControl never reinitializes the
    // plugin after a wake.
    private volatile bool _streamFaulted;

    // Environment.TickCount of the last stream reopen attempt, to throttle retries to RecoveryBackoff.
    private int _lastStreamRecoveryTick;

    // Log a failed stream reopen only once per fault episode, mirroring _recoveryLogged.
    private bool _streamRecoveryLogged;

    public HidSharpTransport(HidStream stream, string devicePath, ILog log) {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (string.IsNullOrEmpty(devicePath)) {
            throw new ArgumentException("Device path is required.", nameof(devicePath));
        }

        _devicePath = devicePath;

        // Keep the HidDevice so the stream can be reopened in place on a latched stream fault.
        _device = stream.Device;

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
        _inputHandle = OpenRawHandle(devicePath);
        if (_inputHandle.IsInvalid) {
            int error = Marshal.GetLastWin32Error();
            _inputHandle.Dispose();
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "Failed to open HID input handle at {0} (error {1}).",
                devicePath,
                error));
        }
    }

    // Open a raw read/write handle on the device path for the GET_REPORT/SET_REPORT control transfers.
    // Shared by the constructor and the self-heal reopen; callers check IsInvalid and read GetLastError.
    private static SafeFileHandle OpenRawHandle(string devicePath) {
        return NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
    }

    // False while a stream fault is latched: the stream is being retired for reopen and must not be
    // touched (it may already be disposed), so short-circuit before dereferencing it.
    public bool CanWrite => !_streamFaulted && _stream.CanWrite;

    public void Write(byte[] report) {
        // The writable check is deferred to RunStreamCall, which applies it to the reopened stream; a
        // pre-check here would dereference a faulted (possibly disposed) _stream before the reopen. The
        // call operates on the stream RunStreamCall hands it, not the _stream field, so a concurrent
        // reopen cannot retarget it mid-flight.
        RunStreamCall(stream => stream.Write(report), "HidStream.Write");
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
    // latches the fault so later transfers fail fast; the transport then reopens the handle in place
    // (self-heal) to clear the latch, so the controller recovers on its own even if the host never
    // reinitializes the plugin (which it does not reliably do after a sleep/wake or an external
    // re-enumeration of the shared device).
    private void RunControlTransfer(Func<bool> nativeCall, string operation) {
        if (_controlTransferFaulted && !TryReopenInputHandle(operation)) {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} skipped: HID control-transfer handle faulted; awaiting device recovery.",
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
            // Let the next transfer attempt a reopen immediately rather than wait a full backoff.
            _lastRecoveryTick = unchecked(Environment.TickCount - RecoveryBackoffMilliseconds);
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

    // Try to clear a latched control-transfer fault by reopening the raw handle in place. Backed off
    // to RecoveryBackoff so a device that stays absent is retried at that cadence, not every tick.
    // Returns true when the handle was reopened and the latch cleared (the caller then proceeds with
    // the transfer on the fresh handle); false to keep failing fast until the next attempt is due.
    // The old handle is disposed only here, on the serialized worker tick and long after the timed-out
    // transfer's abandoned thread was cancelled, so nothing is mid-call on it when it is replaced.
    private bool TryReopenInputHandle(string operation) {
        if (unchecked(Environment.TickCount - _lastRecoveryTick) < RecoveryBackoffMilliseconds) {
            return false;
        }

        _lastRecoveryTick = Environment.TickCount;

        SafeFileHandle reopened = OpenRawHandle(_devicePath);
        if (reopened.IsInvalid) {
            int error = Marshal.GetLastWin32Error();
            reopened.Dispose();
            if (!_recoveryLogged) {
                // Log the onset once per fault episode so a persistent absence is visible without spam.
                _recoveryLogged = true;
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: control-transfer handle still faulted, reopen failed (error {1}); retrying every {2} ms",
                    operation,
                    error,
                    RecoveryBackoffMilliseconds));
            }

            return false;
        }

        _inputHandle.Dispose();
        _inputHandle = reopened;
        _controlTransferFaulted = false;
        _recoveryLogged = false;
        _log.Write(operation + ": control-transfer handle reopened; device recovered, resuming");
        return true;
    }

    public byte[] Read(int length) {
        // Interrupt-IN read for the 0x0416 command-packet family: after a command write the
        // device answers on its input endpoint. HidStream.Read blocks up to the stream's
        // ReadTimeout (set in the constructor), so a silent device surfaces as a timeout the
        // worker isolates rather than a hang. byte 0 of the reply is the report id (0x01).
        byte[] buffer = new byte[length];
        int read = 0;
        RunStreamCall(stream => read = stream.Read(buffer, 0, buffer.Length), "HidStream.Read");
        if (read <= 0) {
            throw new IOException("HID interrupt-IN read returned no data.");
        }

        return buffer;
    }

    // Run a stream Write/Read, latching a fault so the next call reopens the stream (self-heal). Two
    // things can go wrong on a stream stale after a sleep/wake: HidSharp's own Read/WriteTimeout fires
    // (a clean fail-fast), OR the call hangs straight through that timeout - a HidStream reopened onto a
    // just-woken device has been seen to block a Write forever, which would wedge the single worker
    // thread and freeze the plugin. So the call runs under BoundedHidCall: it either returns/throws
    // within StreamWatchdogTimeout, or the watchdog abandons it (closing the stream to unblock the
    // stuck call) and the worker moves on. Either failure latches the fault; the next call reopens the
    // stream in place, so the 0x0416 controllers heal themselves even when the host never reinitializes
    // the plugin after a wake. The call runs against the passed-in stream, not the _stream field, so a
    // reopen on a later tick cannot retarget an in-flight call.
    private void RunStreamCall(Action<HidStream> streamCall, string operation) {
        if (_streamFaulted && !TryReopenStream(operation)) {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} skipped: HID stream faulted; awaiting device recovery.",
                operation));
        }

        HidStream target = _stream;
        if (!target.CanWrite) {
            return;
        }

        bool completed;
        try {
            completed = BoundedHidCall.TryRun(
                () => streamCall(target),
                StreamWatchdogTimeoutMilliseconds,
                () => AbandonStream(target, operation));
        } catch (Exception ex) when (ex is TimeoutException || ex is IOException) {
            // HidSharp's own timeout fired (the normal fail-fast). Retire this stream and latch so the
            // next call reopens; the fault is transient - the device is re-enumerating across a wake.
            AbandonStream(target, operation);
            FaultStream();
            throw;
        }

        if (!completed) {
            // The call hung past HidSharp's own timeout. AbandonStream (the watchdog's onTimeout) has
            // already closed the stream to unblock the abandoned call; latch so the next call reopens.
            FaultStream();
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} watchdog fired after {1} ms; stream unresponsive, retired for reopen.",
                operation,
                StreamWatchdogTimeoutMilliseconds));
        }
    }

    // Latch a stream fault so subsequent calls fail fast and the next one attempts a reopen. The
    // recovery tick is backdated a full backoff so that next call reopens immediately rather than
    // waiting; the backoff only throttles REPEATED reopen attempts while a device stays absent.
    private void FaultStream() {
        _streamFaulted = true;
        _lastStreamRecoveryTick = unchecked(Environment.TickCount - RecoveryBackoffMilliseconds);
    }

    // Retire a faulted stream: close it on a throwaway thread so a hung call blocked inside it unwinds
    // (closing the handle aborts the pending I/O) without the worker thread waiting on a Dispose that
    // could itself block. A stream is abandoned exactly once - at the point it faults - so the reopen
    // never has to dispose it. The dispose is best-effort; a failure is logged, never rethrown, because
    // this runs detached from any caller (its own resilience swallow point, like the file logger).
    private void AbandonStream(HidStream stream, string operation) {
        var disposer = new Thread(() => {
            try {
                stream.Dispose();
            }
#pragma warning disable CA1031 // detached best-effort dispose: nothing can act on a failure here, so log and move on
            catch (Exception ex) {
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture, "{0}: retiring faulted stream failed: {1}", operation, ex.Message));
            }
#pragma warning restore CA1031
        }) {
            IsBackground = true,
            Name = "LianLiHidStreamDispose",
        };
        disposer.Start();
    }

    // Try to clear a latched stream fault by reopening the HidStream. Backed off to RecoveryBackoff so a
    // device that stays absent is retried at that cadence, not every tick. Returns true when the stream
    // was reopened and the latch cleared (the caller then runs its transfer on the fresh stream); false
    // to keep failing fast until the next attempt is due. Prefers a FRESH device lookup by path over the
    // cached HidDevice: after a sleep/wake the cached instance is bound to the pre-sleep enumeration and
    // a stream reopened from it can hang - a freshly enumerated device binds to the current one. The old
    // stream is not disposed here; it was already retired via AbandonStream when it faulted.
    private bool TryReopenStream(string operation) {
        if (unchecked(Environment.TickCount - _lastStreamRecoveryTick) < RecoveryBackoffMilliseconds) {
            return false;
        }

        _lastStreamRecoveryTick = Environment.TickCount;

        HidDevice device = FindDeviceByPath(_devicePath) ?? _device;
        if (!device.TryOpen(out HidStream reopened)) {
            if (!_streamRecoveryLogged) {
                // Log the onset once per fault episode so a persistent absence is visible without spam.
                _streamRecoveryLogged = true;
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: stream still faulted, reopen failed; retrying every {1} ms",
                    operation,
                    RecoveryBackoffMilliseconds));
            }

            return false;
        }

        // Re-apply the same bounds the constructor set, so the reopened stream still fails fast.
        reopened.ReadTimeout = InterruptReadTimeoutMilliseconds;
        reopened.WriteTimeout = StreamWriteTimeoutMilliseconds;

        _stream = reopened;
        _streamFaulted = false;
        _streamRecoveryLogged = false;
        _log.Write(operation + ": stream reopened; device recovered, resuming");
        return true;
    }

    // Find the current HidDevice for our device path from a fresh enumeration, so a post-wake reopen
    // binds to the re-enumerated device instance rather than the stale cached one. Returns null if the
    // device is not currently present (asleep / not yet re-enumerated), in which case the caller falls
    // back to the cached instance and retries at the recovery cadence.
    private static HidDevice? FindDeviceByPath(string devicePath) {
        foreach (HidDevice device in DeviceList.Local.GetHidDevices()) {
            if (string.Equals(device.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)) {
                return device;
            }
        }

        return null;
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
