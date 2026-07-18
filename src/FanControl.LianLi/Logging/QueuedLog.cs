using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace FanControl.LianLi.Logging;

/// <summary>
/// Decouples callers from a log sink that may block: <see cref="Write"/> enqueues the line and
/// returns immediately, and a dedicated background pump thread forwards queued lines to the wrapped
/// sink in order. Exists because a hang inside a sink is invisible to every other safeguard: the
/// host's logger blocked a write indefinitely across a wake from hibernate, wedging the worker
/// thread INSIDE a log call - a freeze no HID watchdog can reach (it is not a HID call) and no
/// breadcrumb can expose (breadcrumbs are themselves log calls). With the queue in between, a
/// wedged sink strands only the pump thread; callers keep running, the queue caps so memory stays
/// bounded, and lines dropped at the cap are counted and reported once the sink drains again.
/// </summary>
internal sealed class QueuedLog : ILog, IDisposable {
    // Cap the queue so a permanently wedged sink cannot grow memory without bound. At this plugin's
    // logging rate the cap holds many minutes of history; a healthy sink never comes close.
    private const int MaxQueuedLines = 1024;

    // The pump wakes on this cadence even without a signal, so a signal lost in a race (set while
    // the pump is mid-drain) delays a line by at most one idle wait, never forever.
    private const int PumpIdleWaitMilliseconds = 250;

    // Bound the flush wait in Dispose: if the sink is wedged mid-write the pump cannot finish, and
    // the host's Close must not inherit the very stall this class exists to absorb.
    private const int DisposeDrainMilliseconds = 1000;

    private readonly ILog _inner;
    private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);
    private readonly Thread _pump;

    private volatile bool _stop;
    private int _queuedLines;   // approximate queue depth; the cap is a memory guard, not a quota
    private int _droppedLines;  // lines discarded at the cap, reported when the sink drains again

    // int (not bool) so the idempotency guard is a lock-free Interlocked.Exchange, matching the
    // worker's dispose pattern.
    private int _disposed;

    public QueuedLog(ILog inner) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        // A daemon thread: if the wrapped sink wedges forever the pump is stranded inside it, and
        // only process teardown can reclaim a thread stuck in foreign code.
        _pump = new Thread(Pump) { IsBackground = true, Name = "LianLiLogPump" };
        _pump.Start();
    }

    /// <summary>Enqueue the line for the background pump. Never blocks and never throws.</summary>
    public void Write(string message) {
        // Approximate check: a concurrent overshoot of a few lines is harmless; the cap guards
        // memory, not an exact count.
        if (Volatile.Read(ref _queuedLines) >= MaxQueuedLines) {
            Interlocked.Increment(ref _droppedLines);
            return;
        }

        _queue.Enqueue(message);
        Interlocked.Increment(ref _queuedLines);

        try {
            _signal.Set();
        } catch (ObjectDisposedException) {
            // A write racing Dispose: the line stays queued and unpumped. A logger must never throw
            // into its caller, and losing a post-dispose line is the accepted cost.
        }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) {
            return;
        }

        _stop = true;
        _signal.Set();

        // Dispose the signal only when the pump has confirmed exit; a wedged sink means the pump may
        // still be alive inside WaitOne, and disposing under it would throw on the pump thread.
        if (_pump.Join(DisposeDrainMilliseconds)) {
            _signal.Dispose();
        }
    }

    private void Pump() {
        while (true) {
            _signal.WaitOne(PumpIdleWaitMilliseconds);
            Drain();
            if (_stop) {
                // Final drain so lines written just before Dispose still reach the sink.
                Drain();
                return;
            }
        }
    }

    private void Drain() {
        while (_queue.TryDequeue(out string line)) {
            Interlocked.Decrement(ref _queuedLines);
            int dropped = Interlocked.Exchange(ref _droppedLines, 0);
            try {
                if (dropped > 0) {
                    _inner.Write(string.Format(
                        CultureInfo.InvariantCulture,
                        "[log] {0} line(s) dropped while the sink was stalled",
                        dropped));
                }

                _inner.Write(line);
            }
#pragma warning disable CA1031 // sanctioned logging swallow: a sink fault must not kill the pump, and there is no further sink to report it to
            catch (Exception) {
                // Intentionally swallowed: mirrors the file logger's contract - logging must never
                // disrupt fan control, and the pump must survive to forward later lines.
            }
#pragma warning restore CA1031
        }
    }
}
