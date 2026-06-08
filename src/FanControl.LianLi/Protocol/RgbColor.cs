#if ENABLE_LIGHTING
namespace FanControl.LianLi.Protocol;

/// <summary>
/// An 8-bit-per-channel RGB colour, as stored per fan in an L-Connect lighting
/// configuration. The wire byte order (R, B, G) is applied by the encoder, not here.
/// </summary>
internal readonly struct RgbColor
{
    /// <summary>Create a colour from its red, green, and blue components.</summary>
    public RgbColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Red channel (0-255).</summary>
    public byte R { get; }

    /// <summary>Green channel (0-255).</summary>
    public byte G { get; }

    /// <summary>Blue channel (0-255).</summary>
    public byte B { get; }
}
#endif
