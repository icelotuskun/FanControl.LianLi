using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FanControl.LianLi.Devices;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public sealed class StartStopConfigReaderTests : IDisposable {
    private const string DevicePath = @"\\?\hid#vid_0cf2&pid_a102&mi_01#d&71d6ab5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private readonly string _dir;

    public StartStopConfigReaderTests() {
        _dir = Path.Combine(Path.GetTempPath(), "lianli-startstop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() {
        if (Directory.Exists(_dir)) {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private void WriteProfile(string json) {
        // Place the gzipped JSON at the exact filename the reader looks up (reusing its own hasher,
        // so the test never reimplements L-Connect's MD5 naming).
        using FileStream file = File.Create(Path.Combine(_dir, StartStopConfigReader.ProfileFileName(DevicePath)));
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        gzip.Write(payload, 0, payload.Length);
    }

    [Fact]
    public void Read_ReturnsPerGroupStartStop_FromTheActiveProfile() {
        // Group 0's active profile (Mode 0 = Quiet) has start/stop on; group 1's active profile
        // (Mode 1 = StandardSpeed) has it off. The reader must select by Mode, not by position.
        WriteProfile(@"{""SubProfiles"":[
            {""GroupIndex"":0,""RPMSetting"":{""Mode"":0,""Profiles"":{
                ""Quiet"":{""Mode"":0,""IsStartStop"":true},
                ""StandardSpeed"":{""Mode"":1,""IsStartStop"":false}}}},
            {""GroupIndex"":1,""RPMSetting"":{""Mode"":1,""Profiles"":{
                ""Quiet"":{""Mode"":0,""IsStartStop"":true},
                ""StandardSpeed"":{""Mode"":1,""IsStartStop"":false}}}}
        ]}");

        bool[] flags = StartStopConfigReader.Read(_dir, DevicePath, 4);

        Assert.Equal(new[] { true, false, false, false }, flags);
    }

    [Fact]
    public void Read_MapsByGroupIndex_NotArrayPosition() {
        // A single group carrying GroupIndex 2 must set channel 2, not channel 0.
        WriteProfile(@"{""SubProfiles"":[
            {""GroupIndex"":2,""RPMSetting"":{""Mode"":0,""Profiles"":{
                ""Quiet"":{""Mode"":0,""IsStartStop"":true}}}}
        ]}");

        bool[] flags = StartStopConfigReader.Read(_dir, DevicePath, 4);

        Assert.Equal(new[] { false, false, true, false }, flags);
    }

    [Fact]
    public void Read_MissingDirectory_ReturnsAllOff() {
        bool[] flags = StartStopConfigReader.Read(
            Path.Combine(_dir, "does-not-exist"), DevicePath, 4);

        Assert.Equal(new[] { false, false, false, false }, flags);
    }

    [Fact]
    public void Read_MissingProfileFile_ReturnsAllOff() {
        // Directory exists but holds no file for this device path.
        bool[] flags = StartStopConfigReader.Read(_dir, DevicePath, 4);

        Assert.Equal(new[] { false, false, false, false }, flags);
    }

    [Fact]
    public void Read_CorruptProfile_Throws() {
        WriteProfile("{ this is not valid json");

        Assert.ThrowsAny<Exception>(() => StartStopConfigReader.Read(_dir, DevicePath, 4));
    }
}
