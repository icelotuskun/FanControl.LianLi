#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;

namespace FanControl.LianLi.Protocol;

/// <summary>
/// One Uni Fan TL fan's saved lighting look, addressed by (port, fan-index). Unlike the Uni
/// family's per-port <see cref="LightingPortState"/>, the TL addresses lighting per fan, so this
/// carries the fan index too. <see cref="Mode"/>, <see cref="Speed"/>, <see cref="Direction"/>,
/// and <see cref="Brightness"/> are the raw L-Connect enum values; the encoder translates the mode
/// to its wire byte and writes the colours in R,G,B order.
/// </summary>
internal sealed class TlFanLightingState
{
    /// <summary>Create the saved lighting state for one TL fan.</summary>
    public TlFanLightingState(int port, int fanIndex, int mode, int speed, int direction, int brightness, IReadOnlyList<RgbColor> colors)
    {
        Port = port;
        FanIndex = fanIndex;
        Mode = mode;
        Speed = speed;
        Direction = direction;
        Brightness = brightness;
        Colors = colors ?? throw new ArgumentNullException(nameof(colors));
    }

    /// <summary>The controller port (0-3) the fan is on.</summary>
    public int Port { get; }

    /// <summary>The fan's index within its port.</summary>
    public int FanIndex { get; }

    /// <summary>Raw L-Connect lighting-mode enum value (the encoder takes it modulo 1000 for the wire).</summary>
    public int Mode { get; }

    /// <summary>Raw L-Connect speed value, sent through as the effect speed byte.</summary>
    public int Speed { get; }

    /// <summary>Raw L-Connect direction value (0-5), sent through as the direction byte.</summary>
    public int Direction { get; }

    /// <summary>Raw L-Connect brightness value, sent through as the brightness byte.</summary>
    public int Brightness { get; }

    /// <summary>The saved colours; the encoder writes up to four in R,G,B order.</summary>
    public IReadOnlyList<RgbColor> Colors { get; }
}
#endif
