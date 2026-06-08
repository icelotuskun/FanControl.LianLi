using System;
using System.Collections.Generic;
using System.IO;
using FanControl.LianLi.Hid;

namespace FanControl.LianLi.Tests.Fakes;

internal sealed class FakeHidTransport : IHidTransport {
    private readonly object _lock = new object();

    public List<byte[]> Writes { get; } = new List<byte[]>();

    /// <summary>Feature reports passed to <see cref="SetFeature"/>, in order.</summary>
    public List<byte[]> Features { get; } = new List<byte[]>();

    /// <summary>
    /// Every transfer in order across both report kinds: <c>true</c> = feature
    /// report (<see cref="SetFeature"/>), <c>false</c> = output report
    /// (<see cref="Write"/>). Lets a test assert the exact replay sequence.
    /// </summary>
    public List<KeyValuePair<bool, byte[]>> Transfers { get; } = new List<KeyValuePair<bool, byte[]>>();

    /// <summary>Buffer returned (copied) from <see cref="GetInputReport"/>.</summary>
    public byte[] InputReport { get; set; } = new byte[65];

    /// <summary>When set, <see cref="GetInputReport"/> throws, simulating a device fault.</summary>
    public bool FailReads { get; set; }

    /// <summary>When set, <see cref="Write"/> and <see cref="SetFeature"/> throw, simulating a setup-write fault.</summary>
    public bool FailWrites { get; set; }

    /// <summary>When set, only <see cref="SetFeature"/> throws (a feature-report fault), simulating a lighting write the device rejects while output writes still work.</summary>
    public bool FailFeatures { get; set; }

    public bool IsDisposed { get; private set; }

    public bool CanWrite => true;

    public void Write(byte[] report) {
        if (FailWrites) {
            throw new IOException("simulated device write failure");
        }

        lock (_lock) {
            var copy = (byte[])report.Clone();
            Writes.Add(copy);
            Transfers.Add(new KeyValuePair<bool, byte[]>(false, copy));
        }
    }

    public void SetFeature(byte[] report) {
        if (FailWrites || FailFeatures) {
            throw new IOException("simulated device feature-report failure");
        }

        lock (_lock) {
            var copy = (byte[])report.Clone();
            Features.Add(copy);
            Transfers.Add(new KeyValuePair<bool, byte[]>(true, copy));
        }
    }

    public byte[] GetInputReport(byte reportId, int length) {
        if (FailReads) {
            throw new IOException("simulated device read failure");
        }

        lock (_lock) {
            var buffer = new byte[length];
            Array.Copy(InputReport, buffer, Math.Min(InputReport.Length, length));
            return buffer;
        }
    }

    public void Clear() {
        lock (_lock) {
            Writes.Clear();
            Features.Clear();
            Transfers.Clear();
        }
    }

    public void Dispose() => IsDisposed = true;
}
