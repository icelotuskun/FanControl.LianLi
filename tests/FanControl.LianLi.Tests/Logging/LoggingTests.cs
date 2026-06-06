using System;
using System.IO;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Tests.Fakes;
using FanControl.Plugins;
using Xunit;

namespace FanControl.LianLi.Tests.Logging;

public class LoggingTests
{
    [Fact]
    public void CompositeLog_FansOutToEverySink()
    {
        var a = new FakeLogger();
        var b = new FakeLogger();
        var composite = new CompositeLog(a, b);

        composite.Write("hello");

        Assert.Equal(new[] { "hello" }, a.Messages);
        Assert.Equal(new[] { "hello" }, b.Messages);
    }

    [Fact]
    public void CompositeLog_ToleratesNullSink()
    {
        var composite = new CompositeLog(new ILog[] { null! });
        composite.Write("no throw"); // the null sink is skipped via null-conditional
    }

    [Fact]
    public void PluginLoggerLog_ForwardsToHostLogger()
    {
        var host = new FakePluginLogger();
        var log = new PluginLoggerLog(host);

        log.Write("msg");

        Assert.Contains("msg", host.Messages);
    }

    [Fact]
    public void PluginLoggerLog_SwallowsHostFailures()
    {
        var log = new PluginLoggerLog(new ThrowingPluginLogger());
        log.Write("msg"); // host throws; the adapter must swallow it
    }

    [Fact]
    public void FileLogger_WritesWithoutThrowingAndResolvesPath()
    {
        var logger = new FileLogger();

        Assert.False(string.IsNullOrEmpty(logger.FilePath));
        logger.Write("test line");

        Assert.True(File.Exists(logger.FilePath));
    }
}

internal sealed class ThrowingPluginLogger : IPluginLogger
{
    public void Log(string message) => throw new InvalidOperationException("host logger failed");
}
