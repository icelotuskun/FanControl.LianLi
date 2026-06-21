using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Logging;

namespace FanControl.LianLi.Worker;

/// <summary>
/// Owns the controller set and drives their I/O. Every iteration applies pending
/// targets and polls RPM for each controller, isolating per-controller faults so
/// one failing device cannot stall the rest. Two callers drive it: the background
/// thread calls the blocking <see cref="Tick"/> on a fixed cadence (so the 15s
/// keepalive still fires even if the host stops pumping), while the host's
/// <c>IPlugin2.Update()</c> hook calls the non-blocking <see cref="TryTick"/>, which
/// skips its turn rather than block the host thread when a background tick is already
/// in flight. All ticks are serialized on the tick gate, so the two callers never
/// touch a transport concurrently.
/// </summary>
internal sealed class KeepAliveWorker : IDisposable {
    private const int TickIntervalMs = 1000;
    private const int JoinTimeoutMs = 2000;

    private readonly IReadOnlyList<FanController> _controllers;
    private readonly ILog _log;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
    private readonly object _tickGate = new object();

    private volatile bool _stop;
    private bool _started;

    // int (not bool) so the idempotency guard is a lock-free Interlocked.Exchange: Dispose must
    // NOT take _tickGate before it has signalled stop, or a background tick blocked in a slow
    // post-hibernate HID read (holding _tickGate) would stall Dispose for the whole read.
    private int _disposed;

    public KeepAliveWorker(IReadOnlyList<FanController> controllers, ILog log) {
        _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _thread = new Thread(Loop) { IsBackground = true, Name = "LianLiHidWorker" };
    }

    /// <summary>Start the background keepalive thread (no-op when there are no controllers).</summary>
    public void Start() {
        if (_controllers.Count == 0) {
            return;
        }

        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Apply pending targets and poll RPM for every controller. Blocks until the tick
    /// completes; used by the background keepalive loop.
    /// </summary>
    public void Tick() {
        lock (_tickGate) {
            TickCore();
        }
    }

    /// <summary>
    /// Non-blocking variant of <see cref="Tick"/>: skips the tick if the background
    /// thread currently holds <c>_tickGate</c>. Used by the host <c>Update()</c> hook so
    /// a blocked background tick (e.g. a slow HID read after hibernate) does not stall
    /// the FanControl UI thread.
    /// </summary>
    public void TryTick() {
        if (!Monitor.TryEnter(_tickGate)) {
            return;
        }

        try {
            TickCore();
        } finally {
            Monitor.Exit(_tickGate);
        }
    }

    public void Dispose() {
        // Signal stop BEFORE touching _tickGate. A background tick can be blocked for minutes in a
        // post-hibernate HID read while holding _tickGate; taking the gate here first would make
        // Dispose() (and the host's Update()/Close(), serialized with it) block for that whole read -
        // relocating the very freeze this worker is designed to avoid. The guard is lock-free.
        if (Interlocked.Exchange(ref _disposed, 1) == 1) {
            return;
        }

        _stop = true;
        _stopSignal.Set();

        bool threadExited = !_started || _thread.Join(JoinTimeoutMs);

        // Only dispose controllers (and the stop signal) once the loop thread has confirmed to have
        // exited. A successful join (or a never-started thread) proves no tick is in flight, so the
        // controllers can be disposed without re-taking _tickGate. If the join timed out the thread
        // may still be mid-read holding the gate; leave the controllers and signal to the finalizer
        // rather than race a use-after-dispose against the worker thread.
        if (threadExited) {
            for (int i = 0; i < _controllers.Count; i++) {
                _controllers[i].Dispose();
            }

            _stopSignal.Dispose();
        }
    }

    private void TickCore() {
        for (int i = 0; i < _controllers.Count; i++) {
            FanController controller = _controllers[i];

            try {
                controller.ApplyPending();
            }
#pragma warning disable CA1031 // resilience: a failed transfer on one device must not stall the others
            catch (Exception ex) {
                _log.Write(string.Format(CultureInfo.InvariantCulture, "apply err C{0}: {1}", i, ex.Message));
            }
#pragma warning restore CA1031

            try {
                controller.PollRpm();
            }
#pragma warning disable CA1031 // resilience: see above
            catch (Exception ex) {
                _log.Write(string.Format(CultureInfo.InvariantCulture, "poll err C{0}: {1}", i, ex.Message));
            }
#pragma warning restore CA1031
        }
    }

    private void Loop() {
        while (!_stop) {
            Tick();
            if (_stop) {
                break;
            }

            _stopSignal.Wait(TickIntervalMs);
        }
    }
}
