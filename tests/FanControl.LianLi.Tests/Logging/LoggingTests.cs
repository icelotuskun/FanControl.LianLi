using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
    [Fact]
    public void QueuedLog_ForwardsLinesToInnerInOrder() {
        var inner = new RecordingLog();
        using var queued = new QueuedLog(inner);

        queued.Write("one");
        queued.Write("two");

        Assert.True(WaitUntil(() => inner.Count == 2), "pump did not forward both lines in time");
        Assert.Equal(new[] { "one", "two" }, inner.Snapshot());
    }

    [Fact]
    public void QueuedLog_WriteReturnsImmediately_WhenInnerIsStalled() {
        using var stall = new ManualResetEventSlim(false);
        var inner = new StallingLog(stall);
        var queued = new QueuedLog(inner);
        try {
            // The pump picks up the first line and wedges inside the sink, exactly like the host
            // logger blocking a write across a wake.
            queued.Write("first");
            Assert.True(WaitUntil(() => inner.StallEntered), "pump never entered the stalled sink");

            var stopwatch = Stopwatch.StartNew();
            queued.Write("second");
            stopwatch.Stop();

            // The whole point of the queue: a wedged sink must not block the caller.
            Assert.True(
                stopwatch.ElapsedMilliseconds < 500,
                $"Write blocked for {stopwatch.ElapsedMilliseconds} ms behind a stalled sink");
        } finally {
            stall.Set(); // release the pump so its background thread exits with the test
            queued.Dispose();
        }
    }

    [Fact]
    public void QueuedLog_DropsAtCap_AndReportsDroppedCount_WhenSinkRecovers() {
        using var stall = new ManualResetEventSlim(false);
        var inner = new StallingLog(stall);
        var queued = new QueuedLog(inner);
        try {
            queued.Write("first");
            Assert.True(WaitUntil(() => inner.StallEntered), "pump never entered the stalled sink");

            // Fill the queue past its 1024-line cap while the sink is wedged; the overflow must be
            // dropped without blocking, and counted.
            for (int i = 0; i < 1024 + 3; i++) {
                queued.Write("line " + i);
            }

            stall.Set();

            Assert.True(
                WaitUntil(() => inner.Snapshot().Exists(line => line.Contains("dropped"))),
                "drop notice never reached the sink after it recovered");
            Assert.Contains(inner.Snapshot(), line => line.Contains("3 line(s) dropped"));
        } finally {
            stall.Set();
            queued.Dispose();
        }
    }

    [Fact]
    public void QueuedLog_DisposeFlushesQueuedLines() {
        var inner = new RecordingLog();
        var queued = new QueuedLog(inner);

        queued.Write("flush me");
        queued.Dispose();

        Assert.Contains("flush me", inner.Snapshot());
    }

    // Poll a condition the background pump satisfies asynchronously, bounded so a broken pump fails
    // the test instead of hanging it.
    private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds = 5000) {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds) {
            if (condition()) {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }
}

internal sealed class ThrowingPluginLogger : IPluginLogger {
    public void Log(string message) => throw new InvalidOperationException("host logger failed");
}

// Thread-safe recorder for lines arriving on the QueuedLog pump thread (the shared FakeLogger is a
// bare List and is only safe for single-threaded sinks).
internal class RecordingLog : ILog {
    private readonly object _gate = new object();
    private readonly List<string> _messages = new List<string>();

    public int Count {
        get {
            lock (_gate) {
                return _messages.Count;
            }
        }
    }

    public List<string> Snapshot() {
        lock (_gate) {
            return new List<string>(_messages);
        }
    }

    public virtual void Write(string message) {
        lock (_gate) {
            _messages.Add(message);
        }
    }
}

// A sink that wedges inside Write until released - the failure mode of the host logger blocking a
// write across a wake from hibernate.
internal sealed class StallingLog : RecordingLog {
    private readonly ManualResetEventSlim _release;

    public StallingLog(ManualResetEventSlim release) {
        _release = release;
    }

    public volatile bool StallEntered;

    public override void Write(string message) {
        StallEntered = true;
        _release.Wait();
        base.Write(message);
    }
}
