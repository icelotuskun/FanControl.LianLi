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
    public void VendorIds_ContainsOnlyTheUniVendor() {
        var catalog = new DeviceCatalog();
        Assert.Equal(new[] { 0x0CF2 }, catalog.VendorIds);
    }

    [Fact]
    public void ProductIds_ContainEverySupportedPid() {
        var catalog = new DeviceCatalog();
        foreach (int pid in new[] { 0x7750, 0xA100, 0xA101, 0xA102, 0xA103, 0xA104, 0xA105 }) {
            Assert.Contains(pid, catalog.ProductIds);
        }

        Assert.DoesNotContain(0xA200, catalog.ProductIds);
        Assert.DoesNotContain(0x8050, catalog.ProductIds);
    }
}
