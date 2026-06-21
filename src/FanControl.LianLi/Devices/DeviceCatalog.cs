using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.Devices;

/// <summary>
/// Recognises the Lian Li devices this plugin drives and classifies a located one into a
/// <see cref="DeviceKind"/> so the plugin knows what to build. The Uni 0x0CF2 controllers
/// map to a pure <see cref="IFanProtocol"/> via <see cref="TryGetProtocol"/>; the 0x0416
/// command-packet controllers (Uni Fan TL, Galahad II Trinity) have no <c>IFanProtocol</c>
/// and are identified by <see cref="Classify"/> instead. The Strimer Plus (0xA200) is a
/// lighting-only device listed in <see cref="LightingProductIds"/>. Unknown ids classify as
/// <see cref="DeviceKind.Unknown"/> and produce no controller and no writes.
/// </summary>
internal sealed class DeviceCatalog {
    // The two USB vendors. The Uni family is one vendor; the command-packet family another.
    private const int UniVendorId = 0x0CF2;
    private const int CommandPacketVendorId = 0x0416;

    // 0x0416 product ids. The Galahad ships in two SKUs (performance / regular) on distinct pids.
    private const int TlFanProductId = 0x7372;
    private const int Galahad2PerformanceProductId = 0x7371;
    private const int Galahad2RegularProductId = 0x7373;

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

        VendorIds = new[] { UniVendorId, CommandPacketVendorId };
        ProductIds = _byProductId.Keys.ToArray();

        // The 0x0416 fan/pump controllers. They share the transport and enumeration with the Uni
        // family but speak a different wire protocol, so they are located here yet built separately.
        CommandPacketProductIds = new[]
        {
            TlFanProductId, Galahad2PerformanceProductId, Galahad2RegularProductId,
        };

        // Lighting-only products (no fan protocol) the Lighting build still locates to drive RGB.
        LightingProductIds = new[] { 0xA200 }; // Strimer Plus
    }

    /// <summary>The USB vendor ids the plugin scans: the Uni family (0x0CF2) and the 0x0416 family.</summary>
    public IReadOnlyList<int> VendorIds { get; }

    /// <summary>Every Uni fan product id backed by an <see cref="IFanProtocol"/>.</summary>
    public IReadOnlyList<int> ProductIds { get; }

    /// <summary>The 0x0416 fan/pump product ids (Uni Fan TL, Galahad II Trinity) located but built separately.</summary>
    public IReadOnlyList<int> CommandPacketProductIds { get; }

    /// <summary>
    /// Lighting-only product ids (the Strimer Plus) that have no fan protocol but whose RGB the
    /// Lighting build drives. They are located and applied, never registered as fan controllers.
    /// </summary>
    public IReadOnlyList<int> LightingProductIds { get; }

    /// <summary>
    /// Classify a located device by its vendor and product id so the plugin knows what to build.
    /// The vendor disambiguates the two wire families; an id from neither classifies as
    /// <see cref="DeviceKind.Unknown"/>.
    /// </summary>
    public DeviceKind Classify(int vendorId, int productId) {
        if (vendorId == UniVendorId) {
            if (_byProductId.ContainsKey(productId)) {
                return DeviceKind.UniFan;
            }

            return LightingProductIds.Contains(productId) ? DeviceKind.LightingOnly : DeviceKind.Unknown;
        }

        if (vendorId == CommandPacketVendorId) {
            if (productId == TlFanProductId) {
                return DeviceKind.TlFan;
            }

            if (productId == Galahad2PerformanceProductId || productId == Galahad2RegularProductId) {
                return DeviceKind.Galahad2;
            }
        }

        return DeviceKind.Unknown;
    }

    /// <summary>
    /// Look up the protocol for a Uni product id. Returns false (and a null
    /// protocol) for any id without an <see cref="IFanProtocol"/> (including the 0x0416 family).
    /// </summary>
    public bool TryGetProtocol(int productId, [MaybeNullWhen(false)] out IFanProtocol protocol) {
        return _byProductId.TryGetValue(productId, out protocol);
    }
}
