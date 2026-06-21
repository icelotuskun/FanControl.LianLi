namespace FanControl.LianLi.Protocol;

/// <summary>
/// One detected Uni Fan TL fan decoded from a handshake reply: which (port, fan-index) it is and
/// its measured RPM. Produced by <see cref="TlFanProtocol.DecodeHandshake"/>.
/// </summary>
internal readonly struct TlFanReading {
    /// <summary>Create a reading for the fan at <paramref name="port"/>/<paramref name="fanIndex"/>.</summary>
    public TlFanReading(int port, int fanIndex, int rpm) {
        Port = port;
        FanIndex = fanIndex;
        Rpm = rpm;
    }

    /// <summary>The controller port (0-3) the fan is on.</summary>
    public int Port { get; }

    /// <summary>The fan's index within its port.</summary>
    public int FanIndex { get; }

    /// <summary>The fan's measured RPM.</summary>
    public int Rpm { get; }
}
