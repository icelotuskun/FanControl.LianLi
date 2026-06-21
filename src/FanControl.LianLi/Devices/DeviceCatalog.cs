using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.Devices;

/// <summary>
/// Maps the Lian Li Uni product ids to their pure protocol encoders. Unknown
/// product ids return false from <see cref="TryGetProtocol"/> and therefore
/// produce no controller and no writes. Out-of-scope Lian Li products (Strimer
/// L Connect 0xA200, Universal Screen LED 0x8050, Galahad II Trinity) are
/// deliberately absent: they are not fan controllers.
/// </summary>
internal sealed class DeviceCatalog {
    private readonly Dictionary<int, IFanProtocol> _byProductId;

    public DeviceCatalog() {
        // Encoders are pure and stateless, so a single instance can back every
        // product id that shares a family.
        var sl = new SlProtocol();
        var al = new AlProtocol();
        var slInfinity = new SlInfinityProtocol();
        var slV2 = new SlV2Protocol();
        var alV2 = new AlV2Protocol();

        _byProductId = new Dictionary<int, IFanProtocol>
        {
            { 0x7750, sl },         // Uni Hub (legacy)
            { 0xA100, sl },         // Uni SL
            { 0xA101, al },         // Uni AL
            { 0xA102, slInfinity }, // Uni SL-Infinity
            { 0xA103, slV2 },       // Uni SL v2
            { 0xA104, alV2 },       // Uni AL v2
            { 0xA105, slV2 },       // Uni SL v2 (alternate pid)
            { 0xA106, sl },         // Uni SL (Redragon OEM variant) - L-Connect drives it as an SL fan
        };

        VendorIds = new[] { 0x0CF2 };
        ProductIds = _byProductId.Keys.ToArray();
    }

    /// <summary>The single vendor id shared by the whole Uni family (0x0CF2).</summary>
    public IReadOnlyList<int> VendorIds { get; }

    /// <summary>Every product id the catalog recognises.</summary>
    public IReadOnlyList<int> ProductIds { get; }

    /// <summary>
    /// Look up the protocol for a product id. Returns false (and a null
    /// protocol) for any id not in the catalog.
    /// </summary>
    public bool TryGetProtocol(int productId, [MaybeNullWhen(false)] out IFanProtocol protocol) {
        return _byProductId.TryGetValue(productId, out protocol);
    }
}
