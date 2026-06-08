using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Logging;

namespace FanControl.LianLi.Worker;

/// <summary>
/// Owns the controller set and drives their I/O. <see cref="Tick"/> applies
/// pending targets and polls RPM for every controller; it is invoked both by
/// the host's <c>IPlugin2.Update()</c> hook and by a background thread, so the
/// 15s keepalive still fires even if the host stops pumping. All ticks are
/// serialized, so the two callers never touch a transport concurrently. Each
/// per-controller call is isolated so one failing device cannot stall the rest.
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
    private bool _disposed;

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
    /// Apply pending targets and poll RPM for every controller. Safe to call
    /// from the host update hook and the background thread; calls are serialized.
    /// </summary>
    public void Tick() {
        lock (_tickGate) {
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
    }

    public void Dispose() {
        lock (_tickGate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
        }

        _stop = true;
        _stopSignal.Set();

        bool threadExited = !_started || _thread.Join(JoinTimeoutMs);

        // Dispose controllers under the tick gate so a tick that outran the join
        // timeout cannot touch a transport while it is being torn down.
        lock (_tickGate) {
            for (int i = 0; i < _controllers.Count; i++) {
                _controllers[i].Dispose();
            }
        }

        // Only dispose the signal once the loop thread can no longer wait on it. If
        // the join timed out the thread may still reference it, so leave it to the
        // finalizer rather than risk an ObjectDisposedException on the worker thread.
        if (threadExited) {
            _stopSignal.Dispose();
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
