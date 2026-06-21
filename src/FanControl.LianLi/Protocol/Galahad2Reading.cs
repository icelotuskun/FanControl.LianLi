namespace FanControl.LianLi.Protocol;

/// <summary>
/// The fan and pump RPM of a Galahad II AIO, decoded from a handshake reply. Produced by
/// <see cref="Galahad2Protocol.DecodeHandshake"/>.
/// </summary>
internal readonly struct Galahad2Reading {
    /// <summary>Create a reading with the given fan and pump RPM.</summary>
    public Galahad2Reading(int fanRpm, int pumpRpm) {
        FanRpm = fanRpm;
        PumpRpm = pumpRpm;
    }

    /// <summary>The AIO fan's measured RPM.</summary>
    public int FanRpm { get; }

    /// <summary>The pump's measured RPM.</summary>
    public int PumpRpm { get; }
}
