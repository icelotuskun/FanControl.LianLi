#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;

namespace FanControl.LianLi.Protocol;

/// <summary>
/// Pure encoder that turns a Uni Fan TL controller's saved per-fan lighting into the 0xA3
/// per-fan light command packets L-Connect sends to reproduce it. No I/O and no state: same input
/// always yields the same bytes. Each fan is one output report (the 64-byte
/// <see cref="CommandPacket"/>); there is no commit/latch, every write self-applies. The TL speaks
/// the 0x0416 protocol over output reports, so every transfer is <see cref="LightingTransfer.IsFeature"/>
/// false.
/// </summary>
internal static class TlFanLightingEncoder
{
    // SetFanLight is command 0xA3 with a fixed 20-byte payload (LConnectCore.Products.TLFan.TLFanDevice).
    private const byte SetFanLightCommand = 0xA3;
    private const int PayloadLength = 20;
    private const int MaxColors = 4;
    private const int ColorOffset = 5; // payload[5..16] holds up to four R,G,B triples.

    /// <summary>
    /// Encode one 0xA3 per-fan light command for each <paramref name="fans"/> entry, in order.
    /// Returns an empty list when there are no fans.
    /// </summary>
    public static IReadOnlyList<LightingTransfer> Encode(IReadOnlyList<TlFanLightingState> fans)
    {
        if (fans is null)
        {
            throw new ArgumentNullException(nameof(fans));
        }

        var transfers = new List<LightingTransfer>(fans.Count);
        foreach (TlFanLightingState fan in fans)
        {
            transfers.Add(EncodeFan(fan));
        }

        return transfers;
    }

    private static LightingTransfer EncodeFan(TlFanLightingState fan)
    {
        byte[] payload = new byte[PayloadLength];

        // payload[0]: sync bit (0 when the plugin drives the colours) in the low nibble | port.
        payload[0] = (byte)((fan.Port & 0x0F) << 4);
        // payload[1]: port in the high nibble, fan index in the low nibble.
        payload[1] = (byte)(((fan.Port & 0x0F) << 4) | (fan.FanIndex & 0x0F));
        // The wire mode byte is the saved mode modulo 1000 - the thousands band selects the
        // merge/side variants, which collapse to the base mode on the wire.
        payload[2] = (byte)(fan.Mode % 1000);
        payload[3] = (byte)fan.Brightness;
        payload[4] = (byte)fan.Speed;

        int count = fan.Colors.Count < MaxColors ? fan.Colors.Count : MaxColors;
        for (int i = 0; i < count; i++)
        {
            int offset = ColorOffset + (i * 3);
            payload[offset] = fan.Colors[i].R;
            payload[offset + 1] = fan.Colors[i].G;
            payload[offset + 2] = fan.Colors[i].B;
        }

        payload[17] = (byte)fan.Direction;
        payload[18] = 0;            // not disabled
        payload[19] = (byte)count;  // colour count

        return new LightingTransfer(isFeature: false, CommandPacket.Build(SetFanLightCommand, payload));
    }
}
#endif
