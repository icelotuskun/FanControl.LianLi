#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public sealed class LConnectConfigReaderTests : IDisposable
{
    // A real SL-Infinity DeviceID: the instance token (71d6ab5) and pid (a102) are parsed out.
    private const string DeviceId = @"\\?\hid#vid_0cf2&pid_a102&mi_01#d&71d6ab5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private readonly string _root;

    public LConnectConfigReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lianli-lconnect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; a locked file must not fail the test run.
        }
    }

    [Fact]
    public void Read_MissingDirectory_ReturnsEmpty()
    {
        Assert.Empty(LConnectConfigReader.Read(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void Read_GroupsPortsAndQuantityByInstanceToken()
    {
        string folder = CreateFolder("controller0");
        WriteGzip(folder, "p2", PortJson(2, mode: 46, speed: 1, direction: 0, brightness: 0, "{\"R\":0,\"G\":215,\"B\":255}", "{\"R\":0,\"G\":8,\"B\":255}"));
        WriteGzip(folder, "p0", PortJson(0, mode: 46, speed: 1, direction: 0, brightness: 0, "{\"R\":255,\"G\":0,\"B\":0}"));
        WriteGzip(folder, "quantity", Setting("FanQuantity", "[4,4,4,4]"));
        WriteGzip(folder, "speed", Setting("FanGroupSpeed1", "{\"MaxSpeed\":2100}")); // unrelated setting, ignored

        IReadOnlyList<LConnectControllerConfig> configs = LConnectConfigReader.Read(_root);

        LConnectControllerConfig config = Assert.Single(configs);
        Assert.Equal("71d6ab5", config.InstanceToken);
        Assert.Equal(new[] { 4, 4, 4, 4 }, config.Quantity);
        Assert.Equal(2, config.Ports.Count);

        LightingPortState port2 = config.Ports.Single(p => p.Port == 2);
        Assert.Equal(46, port2.Mode);
        Assert.Equal(1, port2.Speed);
        Assert.Equal(2, port2.Colors.Count);
        Assert.Equal(0, port2.Colors[0].R);
        Assert.Equal(215, port2.Colors[0].G);
        Assert.Equal(255, port2.Colors[0].B);
    }

    [Fact]
    public void Read_SkipsControllersWithNoLightingPorts()
    {
        string folder = CreateFolder("controller0");
        WriteGzip(folder, "quantity", Setting("FanQuantity", "[4,4,4,4]"));

        Assert.Empty(LConnectConfigReader.Read(_root));
    }

    [Fact]
    public void Read_CorruptFile_Throws()
    {
        string folder = CreateFolder("controller0");
        File.WriteAllBytes(Path.Combine(folder, "bad.0"), new byte[] { 0x01, 0x02, 0x03, 0x04 }); // not gzip

        Assert.ThrowsAny<Exception>(() => LConnectConfigReader.Read(_root));
    }

    [Fact]
    public void Read_DecompressionBomb_ThrowsRatherThanExhaustMemory()
    {
        string folder = CreateFolder("controller0");
        // A tiny gzip on disk that expands well past the decompressed-size cap.
        byte[] huge = Encoding.UTF8.GetBytes(new string('a', 2 * 1024 * 1024));
        using (FileStream file = File.Create(Path.Combine(folder, "bomb.0")))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        {
            gzip.Write(huge, 0, huge.Length);
        }

        Assert.Throws<FormatException>(() => LConnectConfigReader.Read(_root));
    }

    [Fact]
    public void Read_ParsesGalahadFanAndPumpLighting()
    {
        string folder = CreateFolder("galahad0");
        WriteGzip(folder, "fan", Setting("FanLEDLighting",
            "{\"Mode\":3,\"Brightness\":2,\"Speed\":4,\"Colors\":[{\"R\":255,\"G\":0,\"B\":0}],\"Direction\":1,\"SyncToPump\":true,\"NumberOfLED\":24}"));
        WriteGzip(folder, "pump", Setting("PumpLEDLighting",
            "[{\"Scope\":2,\"Mode\":2001,\"Brightness\":2,\"Speed\":3,\"Colors\":[{\"R\":0,\"G\":0,\"B\":255}],\"Direction\":5}]"));

        LConnectControllerConfig config = Assert.Single(LConnectConfigReader.Read(_root));

        Assert.NotNull(config.GalahadFan);
        Assert.Equal(3, config.GalahadFan!.Mode);
        Assert.Equal(24, config.GalahadFan.NumberOfLed);
        Assert.True(config.GalahadFan.SyncToPump);
        Assert.Equal(255, Assert.Single(config.GalahadFan.Colors).R);

        Assert.NotNull(config.GalahadPump);
        Assert.Equal(2, config.GalahadPump!.Scope);  // all
        Assert.Equal(2001, config.GalahadPump.Mode); // raw; encoder applies %1000
        Assert.Equal(255, config.GalahadPump.Colors[0].B);
    }

    [Fact]
    public void Read_ParsesTlPerFanLighting()
    {
        string folder = CreateFolder("tl0");
        // LightingConfigs[port]{ PortType -> GroupElement[] }; "1" is the LED port, one ungrouped
        // group of two fans.
        string collection =
            "{\"IsMerged\":false,\"LightingConfigs\":[{\"1\":[{\"IsGrouping\":false,\"Configs\":["
            + "{\"Mode\":3,\"Speed\":2,\"Direction\":0,\"Brightness\":2,\"Colors\":[{\"R\":255,\"G\":0,\"B\":0}]},"
            + "{\"Mode\":1003,\"Speed\":2,\"Direction\":1,\"Brightness\":2,\"Colors\":[{\"R\":0,\"G\":255,\"B\":0}]}"
            + "]}]}]}";
        WriteGzip(folder, "lighting", Setting("Lighting", collection));

        LConnectControllerConfig config = Assert.Single(LConnectConfigReader.Read(_root));

        Assert.NotNull(config.TlFans);
        Assert.Equal(2, config.TlFans!.Count);
        Assert.Equal((0, 0), (config.TlFans[0].Port, config.TlFans[0].FanIndex));
        Assert.Equal(255, config.TlFans[0].Colors[0].R);
        Assert.Equal((0, 1), (config.TlFans[1].Port, config.TlFans[1].FanIndex));
        Assert.Equal(1003, config.TlFans[1].Mode); // raw mode; encoder applies %1000
        Assert.Equal(255, config.TlFans[1].Colors[0].G);
    }

    [Fact]
    public void Read_TlGroupedOnlyLook_ProducesNothing()
    {
        string folder = CreateFolder("tlgrouped");
        // A grouped element carries a single whole-group look with no per-fan count, so the reader
        // produces no per-fan looks and the controller (having no other look) is skipped.
        string collection =
            "{\"IsMerged\":false,\"LightingConfigs\":[{\"1\":[{\"IsGrouping\":true,\"Configs\":["
            + "{\"Mode\":3,\"Speed\":2,\"Direction\":0,\"Brightness\":2,\"Colors\":[{\"R\":255,\"G\":0,\"B\":0}]}"
            + "]}]}]}";
        WriteGzip(folder, "lighting", Setting("Lighting", collection));

        Assert.Empty(LConnectConfigReader.Read(_root));
    }

    private string CreateFolder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string PortJson(int port, int mode, int speed, int direction, int brightness, params string[] colors)
        => Setting(
            "LightingPort" + port,
            "{\"Port\":" + port + ",\"Mode\":" + mode + ",\"Speed\":" + speed + ",\"Direction\":" + direction
            + ",\"Brightness\":" + brightness + ",\"Colors\":[" + string.Join(",", colors) + "]}");

    private static string Setting(string type, string dataJson)
        => "{\"DeviceID\":\"" + DeviceId.Replace("\\", "\\\\") + "\",\"Type\":\"" + type + "\",\"Data\":" + dataJson + "}";

    private static void WriteGzip(string folder, string name, string json)
    {
        using FileStream file = File.Create(Path.Combine(folder, name + ".0"));
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        gzip.Write(bytes, 0, bytes.Length);
    }
}
#endif
