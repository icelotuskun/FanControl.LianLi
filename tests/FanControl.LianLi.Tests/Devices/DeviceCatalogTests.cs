using FanControl.LianLi.Devices;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public class DeviceCatalogTests {
    // The expected family is passed as its name (a public string) because the
    // DeviceFamily enum is internal and cannot appear in a public test signature.
    [Theory]
    [InlineData(0x7750, "Sl")]
    [InlineData(0xA100, "Sl")]
    [InlineData(0xA101, "Al")]
    [InlineData(0xA102, "SlInfinity")]
    [InlineData(0xA103, "SlV2")]
    [InlineData(0xA104, "AlV2")]
    [InlineData(0xA105, "SlV2")]
    [InlineData(0xA106, "Sl")] // Uni SL (Redragon OEM variant)
    public void TryGetProtocol_MapsKnownPidsToFamily(int pid, string expectedFamily) {
        var catalog = new DeviceCatalog();
        Assert.True(catalog.TryGetProtocol(pid, out IFanProtocol? protocol));
        Assert.NotNull(protocol);
        Assert.Equal(expectedFamily, protocol!.Family.ToString());
    }

    [Theory]
    [InlineData(0x1234)] // arbitrary unknown
    [InlineData(0xA200)] // Strimer L Connect - not a fan controller
    [InlineData(0x8050)] // Universal Screen LED - not a fan controller
    [InlineData(0x7373)] // Galahad II Trinity - different VID, not a fan controller
    public void TryGetProtocol_RejectsUnknownAndOutOfScopePids(int pid) {
        var catalog = new DeviceCatalog();
        Assert.False(catalog.TryGetProtocol(pid, out IFanProtocol? protocol));
        Assert.Null(protocol);
    }

    [Fact]
    public void VendorIds_ContainBothFamilies() {
        var catalog = new DeviceCatalog();
        Assert.Equal(new[] { 0x0CF2, 0x0416 }, catalog.VendorIds);
    }

    [Fact]
    public void ProductIds_ContainEverySupportedPid() {
        var catalog = new DeviceCatalog();
        foreach (int pid in new[] { 0x7750, 0xA100, 0xA101, 0xA102, 0xA103, 0xA104, 0xA105, 0xA106 }) {
            Assert.Contains(pid, catalog.ProductIds);
        }

        Assert.DoesNotContain(0xA200, catalog.ProductIds);
        Assert.DoesNotContain(0x8050, catalog.ProductIds);
        // The 0x0416 ids are located separately - they have no IFanProtocol.
        Assert.DoesNotContain(0x7372, catalog.ProductIds);
    }

    [Fact]
    public void CommandPacketProductIds_ContainTheTlAndGalahadPids() {
        var catalog = new DeviceCatalog();
        Assert.Equal(
            new[] { 0x7372, 0x7371, 0x7373, 0x7391, 0x7395, 0x7398, 0x7399, 0x739A },
            catalog.CommandPacketProductIds);
    }

    // The expected kind is passed as its name (a public string) because the DeviceKind enum is
    // internal and cannot appear in a public test signature.
    [Theory]
    [InlineData(0x0CF2, 0xA102, "UniFan")]   // SL-Infinity fan controller
    [InlineData(0x0CF2, 0xA106, "UniFan")]   // Redragon OEM SL
    [InlineData(0x0CF2, 0xA200, "LightingOnly")] // Strimer Plus
    [InlineData(0x0416, 0x7372, "TlFan")]    // Uni Fan TL
    [InlineData(0x0416, 0x7371, "Galahad2")] // Galahad II Trinity (performance)
    [InlineData(0x0416, 0x7373, "Galahad2")] // Galahad II Trinity (regular)
    [InlineData(0x0416, 0x7391, "Galahad2")] // Galahad II Vision LCD
    [InlineData(0x0416, 0x7395, "Galahad2")] // Galahad II Vision LCD (alternate pid)
    [InlineData(0x0416, 0x7398, "Galahad2")] // HydroShift LCD
    [InlineData(0x0416, 0x739A, "Galahad2")] // HydroShift LCD (alternate pid)
    [InlineData(0x0CF2, 0x9999, "Unknown")]  // unknown Uni-vendor pid
    [InlineData(0x0416, 0x9999, "Unknown")]  // unknown 0x0416 pid
    [InlineData(0x1234, 0xA102, "Unknown")]  // right pid, wrong vendor
    public void Classify_MapsVendorAndPidToKind(int vendorId, int productId, string expectedKind) {
        var catalog = new DeviceCatalog();
        Assert.Equal(expectedKind, catalog.Classify(vendorId, productId).ToString());
    }
}
