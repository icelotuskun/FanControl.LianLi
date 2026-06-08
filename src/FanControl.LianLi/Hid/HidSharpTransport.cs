using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
internal sealed class HidSharpTransport : IHidTransport
{
    private readonly HidStream _stream;
    private readonly SafeFileHandle _inputHandle;

    public HidSharpTransport(HidStream stream, string devicePath)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(devicePath))
        {
            throw new ArgumentException("Device path is required.", nameof(devicePath));
        }

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
        if (_inputHandle.IsInvalid)
        {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "Failed to open HID input handle at {0} (error {1}).",
                devicePath,
                Marshal.GetLastWin32Error()));
        }
    }

    public bool CanWrite => _stream.CanWrite;

    public void Write(byte[] report)
    {
        if (!_stream.CanWrite)
        {
            return;
        }

        _stream.Write(report);
    }

    public void SetFeature(byte[] report)
    {
        if (!_stream.CanWrite)
        {
            return;
        }

        // SET_REPORT(Feature) on the same raw handle used for input reports. The
        // lighting effect/quantity/frame commands are feature reports; byte 0 is the
        // report id (0xE0), matching the controller's layout.
        if (!NativeMethods.HidD_SetFeature(_inputHandle, report, report.Length))
        {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "HidD_SetFeature failed (error {0}).",
                Marshal.GetLastWin32Error()));
        }
    }

    public byte[] GetInputReport(byte reportId, int length)
    {
        // GET_REPORT(Input): byte 0 selects the report id, the device fills the rest.
        // The decoder skips the leading id via its per-family RPM offset. The device
        // answers this pull on demand even though it never streams input reports.
        byte[] buffer = new byte[length];
        buffer[0] = reportId;
        if (!NativeMethods.HidD_GetInputReport(_inputHandle, buffer, buffer.Length))
        {
            throw new IOException(string.Format(
                CultureInfo.InvariantCulture,
                "HidD_GetInputReport failed for report 0x{0:X2} (error {1}).",
                reportId,
                Marshal.GetLastWin32Error()));
        }

        return buffer;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _inputHandle.Dispose();
    }

    private static class NativeMethods
    {
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
