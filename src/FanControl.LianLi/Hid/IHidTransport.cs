using System;

namespace FanControl.LianLi.Hid;

/// <summary>
/// The single seam through which the plugin performs USB HID I/O. Everything
/// downstream of <c>Hid/</c> depends only on this interface, never on HidSharp,
/// which makes the protocol and worker layers trivially fakeable in tests.
/// </summary>
internal interface IHidTransport : IDisposable
{
    /// <summary>True when the underlying stream accepts writes.</summary>
    bool CanWrite { get; }

    /// <summary>Write a raw output report. No-ops if the stream is not writable.</summary>
    void Write(byte[] report);

    /// <summary>
    /// Send a raw HID feature report (SET_REPORT(Feature) / <c>HidD_SetFeature</c>).
    /// The controllers take their lighting effect, fan-quantity, and frame-latch
    /// commands as feature reports (colour data goes through <see cref="Write"/> as
    /// an output report). No-ops if the stream is not writable.
    /// </summary>
    void SetFeature(byte[] report);

    /// <summary>
    /// Read an input report of <paramref name="length"/> bytes for the given
    /// <paramref name="reportId"/>. The returned buffer has byte 0 set to the
    /// report id, matching the controller's report layout.
    /// </summary>
    byte[] GetInputReport(byte reportId, int length);
}
