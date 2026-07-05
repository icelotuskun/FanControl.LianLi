using System;
using System.IO;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Tests.Fakes;
using FanControl.Plugins;
using Xunit;

namespace FanControl.LianLi.Tests.Logging;

public class LoggingTests {
    [Fact]
    public void CompositeLog_FansOutToEverySink() {
        var a = new FakeLogger();
        var b = new FakeLogger();
        var composite = new CompositeLog(a, b);

        composite.Write("hello");

        Assert.Equal(new[] { "hello" }, a.Messages);
        Assert.Equal(new[] { "hello" }, b.Messages);
    }

    [Fact]
    public void CompositeLog_ToleratesNullSink() {
        var composite = new CompositeLog(new ILog[] { null! });
        composite.Write("no throw"); // the null sink is skipped via null-conditional
    }

    [Fact]
    public void PluginLoggerLog_ForwardsToHostLogger() {
        var host = new FakePluginLogger();
        var log = new PluginLoggerLog(host);

        log.Write("msg");

        Assert.Contains("msg", host.Messages);
    }

    [Fact]
    public void PluginLoggerLog_SwallowsHostFailures() {
        var log = new PluginLoggerLog(new ThrowingPluginLogger());
        log.Write("msg"); // host throws; the adapter must swallow it
    }

    [Fact]
    public void FileLogger_WritesWithoutThrowingAndResolvesPath() {
        var logger = new FileLogger();

        Assert.False(string.IsNullOrEmpty(logger.FilePath));
        logger.Write("test line");

        Assert.True(File.Exists(logger.FilePath));
    }

    [Fact]
    public void FileLogger_RollsToBackup_AndKeepsActiveFileUnderCap_WhenCapExceeded() {
        string path = Path.Combine(
            Path.GetTempPath(), "lianli-log-test-" + Guid.NewGuid().ToString("N") + ".log");
        string rolled = path + ".1";
        try {
            // A small cap so a handful of ordinary lines trips the roll without writing megabytes.
            var logger = new FileLogger(path, maxFileBytes: 200);

            for (int i = 0; i < 40; i++) {
                logger.Write("fan speed line " + i);
            }

            Assert.True(File.Exists(rolled));                    // the roll created the single backup
            Assert.True(new FileInfo(path).Length <= 200);       // the active file reset and stays under the cap
        } finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }

            if (File.Exists(rolled)) {
                File.Delete(rolled);
            }
        }
    }
}

internal sealed class ThrowingPluginLogger : IPluginLogger {
    public void Log(string message) => throw new InvalidOperationException("host logger failed");
}
