using System;
using System.Collections.Generic;
using System.IO;
using FanControl.LianLi.Hid;

namespace FanControl.LianLi.Tests.Fakes;

internal sealed class FakeHidTransport : IHidTransport
{
    private readonly object _lock = new object();

    public List<byte[]> Writes { get; } = new List<byte[]>();

    /// <summary>Buffer returned (copied) from <see cref="GetInputReport"/>.</summary>
    public byte[] InputReport { get; set; } = new byte[65];

    /// <summary>When set, <see cref="GetInputReport"/> throws, simulating a device fault.</summary>
    public bool FailReads { get; set; }

    public bool IsDisposed { get; private set; }

    public bool CanWrite => true;

    public void Write(byte[] report)
    {
        lock (_lock)
        {
            Writes.Add((byte[])report.Clone());
        }
    }

    public byte[] GetInputReport(byte reportId, int length)
    {
        if (FailReads)
        {
            throw new IOException("simulated device read failure");
        }

        lock (_lock)
        {
            var buffer = new byte[length];
            Array.Copy(InputReport, buffer, Math.Min(InputReport.Length, length));
            return buffer;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Writes.Clear();
        }
    }

    public void Dispose() => IsDisposed = true;
}
