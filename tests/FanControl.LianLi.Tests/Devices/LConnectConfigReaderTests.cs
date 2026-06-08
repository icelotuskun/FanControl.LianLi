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
