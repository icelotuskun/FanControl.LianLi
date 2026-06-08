#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Hid;
using FanControl.LianLi.Plugin;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.Plugin;

// End-to-end coverage of the Lighting build's host wiring: reading L-Connect's config from a
// fixture directory, matching it to a located controller by instance token, the family gate,
// the encode+apply path, and the guarantee that a lighting fault never drops fan control.
public sealed class LianLiPluginLightingTests : IDisposable
{
    // A realistic SL-Infinity device path; the instance token (9c2f7a3) is what the saved
    // config is matched against.
    private const string DevicePath = @"\\?\hid#vid_0cf2&pid_a102&mi_01#7&9c2f7a3&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private readonly string _configDir;

    public LianLiPluginLightingTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "lianli-plugin-lighting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_configDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; a locked file must not fail the test run.
        }
    }

    [Fact]
    public void Initialize_AppliesSavedLook_ToMatchingSlInfinityController()
    {
        WriteSavedLook();
        var enumerator = new FakeEnumerator(Device(0xA102, DevicePath));
        using LianLiPlugin plugin = NewPlugin(enumerator);

        plugin.Initialize();

        IReadOnlyList<LightingTransfer> expected = SlInfinityLightingEncoder.Encode(
            new[] { new LightingPortState(0, 26, 0, 0, 0, new[] { new RgbColor(255, 0, 0) }) },
            new[] { 4, 4, 4, 4 });

        FakeHidTransport transport = Assert.Single(enumerator.Opened);
        // Lighting is applied before fan setup, so the look is the prefix of the transfer log.
        Assert.True(transport.Transfers.Count >= expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].IsFeature, transport.Transfers[i].Key);
            Assert.Equal(expected[i].Report, transport.Transfers[i].Value);
        }
    }

    [Fact]
    public void Initialize_SkipsLighting_ForUnsupportedFamily()
    {
        WriteSavedLook();
        // An AL controller (0xA101) matches the saved look by token but is not SL-Infinity.
        var enumerator = new FakeEnumerator(Device(0xA101, DevicePath));
        using LianLiPlugin plugin = NewPlugin(enumerator);

        plugin.Initialize();

        FakeHidTransport transport = Assert.Single(enumerator.Opened);
        Assert.Empty(transport.Features); // no lighting feature reports were sent

        // The controller is still registered: fan control is unaffected by the lighting skip.
        var container = new FakeSensorsContainer();
        plugin.Load(container);
        Assert.Equal(4, container.ControlSensors.Count);
    }

    [Fact]
    public void Initialize_DrivesNoLighting_WhenNoSavedLookMatches()
    {
        // Config directory is empty: nothing to apply.
        var enumerator = new FakeEnumerator(Device(0xA102, DevicePath));
        using LianLiPlugin plugin = NewPlugin(enumerator);

        plugin.Initialize();

        FakeHidTransport transport = Assert.Single(enumerator.Opened);
        Assert.Empty(transport.Features);
    }

    [Fact]
    public void Initialize_LightingWriteFault_DisablesLightingButKeepsFanControl()
    {
        WriteSavedLook();
        // The device rejects feature reports (lighting), but output writes (fan setup) still work.
        var enumerator = new FakeEnumerator(Device(0xA102, DevicePath)) { FailFeatures = true };
        using LianLiPlugin plugin = NewPlugin(enumerator);

        plugin.Initialize();

        // The lighting fault is isolated: the controller is still registered with its sensors.
        var container = new FakeSensorsContainer();
        plugin.Load(container);
        Assert.Equal(4, container.ControlSensors.Count);
        Assert.Equal(4, container.FanSensors.Count);
    }

    private LianLiPlugin NewPlugin(FakeEnumerator enumerator)
        => new LianLiPlugin(enumerator, new DeviceCatalog(), new FakeClock(), new FakeLogger(), _configDir);

    private static HidDeviceInfo Device(int productId, string devicePath)
        => new HidDeviceInfo(0x0CF2, productId, devicePath, null);

    // Write one controller's saved look (a StaticColor port plus a fan quantity) as L-Connect
    // stores it: gzipped JSON setting files under a per-device folder.
    private void WriteSavedLook()
    {
        string folder = Path.Combine(_configDir, "controller");
        Directory.CreateDirectory(folder);
        WriteGzip(
            folder,
            "port0",
            Setting("LightingPort0", "{\"Port\":0,\"Mode\":26,\"Speed\":0,\"Direction\":0,\"Brightness\":0,\"Colors\":[{\"R\":255,\"G\":0,\"B\":0}]}"));
        WriteGzip(folder, "quantity", Setting("FanQuantity", "[4,4,4,4]"));
    }

    private static string Setting(string type, string dataJson)
        => "{\"DeviceID\":\"" + DevicePath.Replace("\\", "\\\\") + "\",\"Type\":\"" + type + "\",\"Data\":" + dataJson + "}";

    private static void WriteGzip(string folder, string name, string json)
    {
        using FileStream file = File.Create(Path.Combine(folder, name + ".0"));
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        gzip.Write(bytes, 0, bytes.Length);
    }
}
#endif
