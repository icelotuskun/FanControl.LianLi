using System;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using FanControl.LianLi.Worker;
using Xunit;

namespace FanControl.LianLi.Tests.Worker;

public class KeepAliveWorkerTests
{
    private static FanController NewController(int index, FakeHidTransport transport, FakeLogger logger)
        => new FanController(index, transport, new SlProtocol(), new FakeClock(), logger);

    [Fact]
    public void Tick_IsolatesAndLogsAPerControllerFault()
    {
        var logger = new FakeLogger();
        var badTransport = new FakeHidTransport { FailReads = true };
        var goodTransport = new FakeHidTransport();
        var goodBuffer = new byte[65];
        goodBuffer[1] = 0x05; // ch0 high
        goodBuffer[2] = 0xDC; // ch0 low -> 1500
        goodTransport.InputReport = goodBuffer;

        FanController bad = NewController(0, badTransport, logger);
        FanController good = NewController(1, goodTransport, logger);
        using var worker = new KeepAliveWorker(new[] { bad, good }, logger);

        worker.Tick();

        // The faulting controller's poll was caught and logged...
        Assert.Contains(logger.Messages, m => m.Contains("poll err C0"));
        // ...and the healthy controller was still polled despite the earlier fault.
        Assert.Equal(1500f, good.GetRpm(0));
    }

    [Fact]
    public void Start_RunsBackgroundLoopUntilDisposed()
    {
        var logger = new FakeLogger();
        var transport = new FakeHidTransport();
        var buffer = new byte[65];
        buffer[1] = 0x05; // ch0 high
        buffer[2] = 0xDC; // ch0 low -> 1500
        transport.InputReport = buffer;
        FanController controller = NewController(0, transport, logger);
        var worker = new KeepAliveWorker(new[] { controller }, logger);

        worker.Start();
        // The loop ticks immediately; wait until it has polled at least once.
        SpinWait.SpinUntil(() => controller.GetRpm(0) == 1500f, TimeSpan.FromSeconds(2));
        worker.Dispose();

        Assert.Equal(1500f, controller.GetRpm(0));
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void Dispose_DisposesEveryController()
    {
        var t0 = new FakeHidTransport();
        var t1 = new FakeHidTransport();
        FanController c0 = NewController(0, t0, new FakeLogger());
        FanController c1 = NewController(1, t1, new FakeLogger());
        var worker = new KeepAliveWorker(new[] { c0, c1 }, new FakeLogger());

        worker.Dispose();

        Assert.True(t0.IsDisposed);
        Assert.True(t1.IsDisposed);
    }
}
