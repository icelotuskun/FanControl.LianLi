using System;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using FanControl.LianLi.Worker;
using Xunit;

namespace FanControl.LianLi.Tests.Worker;

public class KeepAliveWorkerTests {
    private static FanController NewController(int index, FakeHidTransport transport, FakeLogger logger)
        => new FanController(index, transport, new SlProtocol(), new bool[4], new FakeClock(), logger);

    [Fact]
    public void Tick_IsolatesAndLogsAPerControllerFault() {
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
    public void Start_RunsBackgroundLoopUntilDisposed() {
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
    public void Dispose_DisposesEveryController() {
        var t0 = new FakeHidTransport();
        var t1 = new FakeHidTransport();
        FanController c0 = NewController(0, t0, new FakeLogger());
        FanController c1 = NewController(1, t1, new FakeLogger());
        var worker = new KeepAliveWorker(new[] { c0, c1 }, new FakeLogger());

        worker.Dispose();

        Assert.True(t0.IsDisposed);
        Assert.True(t1.IsDisposed);
    }

    [Fact]
    public void TryTick_WhenBackgroundTickHoldsGate_SkipsWithoutBlocking() {
        var logger = new FakeLogger();
        using var gate = new ManualResetEventSlim(false);
        var transport = new FakeHidTransport { BlockReadsUntil = gate };
        FanController controller = NewController(0, transport, logger);
        using var worker = new KeepAliveWorker(new[] { controller }, logger);

        // A background tick blocks inside the HID read while holding the tick gate, mimicking the
        // slow post-hibernate read.
        var background = new Thread(worker.Tick) { IsBackground = true };
        background.Start();
        Assert.True(
            SpinWait.SpinUntil(() => transport.ReadCount >= 1, TimeSpan.FromSeconds(2)),
            "background tick never reached the blocking read");
        int readsWhileBlocked = transport.ReadCount;

        // The host path (Update -> TryTick) must skip immediately instead of blocking on the gate -
        // this is the freeze fix. Run it on its own thread bounded by Join so a regression (reverting
        // TryTick to a blocking lock) fails the assert promptly instead of hanging the suite.
        var tryTickThread = new Thread(worker.TryTick) { IsBackground = true };
        tryTickThread.Start();
        Assert.True(
            tryTickThread.Join(TimeSpan.FromSeconds(1)),
            "TryTick blocked on the held gate instead of skipping");
        Assert.Equal(readsWhileBlocked, transport.ReadCount); // skipped: did no work while busy

        // Once the gate frees, a TryTick performs the work again (proving it was a skip, not a no-op).
        gate.Set();
        Assert.True(background.Join(TimeSpan.FromSeconds(2)), "background tick did not complete");
        worker.TryTick();
        Assert.True(transport.ReadCount > readsWhileBlocked);
    }

    [Fact]
    public void Dispose_WhenBackgroundReadBlocked_ReturnsPromptly() {
        var logger = new FakeLogger();
        using var gate = new ManualResetEventSlim(false);
        var transport = new FakeHidTransport { BlockReadsUntil = gate };
        FanController controller = NewController(0, transport, logger);
        var worker = new KeepAliveWorker(new[] { controller }, logger);

        worker.Start();
        Assert.True(
            SpinWait.SpinUntil(() => transport.ReadCount >= 1, TimeSpan.FromSeconds(2)),
            "background loop never reached the blocking read");

        // Dispose must not block on the stuck read; it is bounded by the join timeout. Run it on a
        // separate thread guarded by a timeout so a regression (taking the tick gate before
        // signalling stop) fails the assert instead of hanging the whole suite.
        var disposeThread = new Thread(worker.Dispose) { IsBackground = true };
        disposeThread.Start();
        bool returned = disposeThread.Join(TimeSpan.FromSeconds(5));
        gate.Set(); // always release so the blocked thread can exit, even if Dispose regressed
        Assert.True(returned, "Dispose blocked on the stuck HID read instead of returning within the join timeout");

        // The join timed out (the thread is still mid-read holding the gate), so the controller is
        // intentionally left undisposed rather than racing a use-after-dispose against the worker.
        Assert.False(transport.IsDisposed);

        Assert.True(disposeThread.Join(TimeSpan.FromSeconds(2)));
    }
}
