using System;
using System.Collections.Generic;
using System.Globalization;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Hid;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Worker;
using FanControl.Plugins;

namespace FanControl.LianLi.Plugin;

/// <summary>
/// FanControl entry point for Lian Li Uni controllers. This is the only public
/// type in the assembly: it composes the HID transport, the per-device protocol
/// encoders, the injectable clock, and the keepalive worker, and exposes the
/// <see cref="IPlugin2"/> surface to the host. The control hot path only mutates
/// in-memory state; all USB I/O is driven from <see cref="Update"/> and the
/// background keepalive thread.
/// </summary>
public sealed class LianLiPlugin : IPlugin2, IDisposable
{
    private readonly IHidDeviceEnumerator _enumerator;
    private readonly DeviceCatalog _catalog;
    private readonly IClock _clock;
    private readonly ILog _log;

    // FanControl calls Initialize/Update/Close on its own threads. This serializes
    // the worker lifecycle so a host Update() cannot tick a worker that a concurrent
    // Close()/Dispose() is tearing down.
    private readonly object _sync = new object();
    private readonly List<FanController> _controllers = new List<FanController>();
    private KeepAliveWorker? _worker;

    /// <summary>
    /// Host-injected constructor. FanControl supplies the logger; the plugin
    /// logs through both it and a local file as a fallback.
    /// </summary>
    public LianLiPlugin(IPluginLogger logger)
        : this(
            new HidSharpEnumerator(),
            new DeviceCatalog(),
            new SystemClock(),
            new CompositeLog(new PluginLoggerLog(logger), new FileLogger()))
    {
    }

    /// <summary>Composition/test constructor that accepts fakes for every dependency.</summary>
    internal LianLiPlugin(
        IHidDeviceEnumerator enumerator,
        DeviceCatalog catalog,
        IClock clock,
        ILog log)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Plugin name shown in the FanControl UI. The ARGB build advertises a
    /// distinct name so a user can tell the two variants apart at a glance.</summary>
#if ENABLE_ARGB
    public string Name => "Lian Li Uni (ARGB)";
#else
    public string Name => "Lian Li Uni";
#endif

    /// <summary>
    /// Scan for controllers, build a <see cref="FanController"/> for each known
    /// device, and start the keepalive worker. Safe to call repeatedly: any
    /// previous state is torn down first.
    /// </summary>
    public void Initialize()
    {
        lock (_sync)
        {
            InitializeLocked();
        }
    }

    private void InitializeLocked()
    {
        TearDownLocked();

        // Compile-time variant tag, logged so a user's log file reveals which DLL is installed.
#if ENABLE_ARGB
        const string buildVariant = "ARGB";
#else
        const string buildVariant = "standard";
#endif

        IReadOnlyList<HidDeviceInfo> located = _enumerator.Locate(_catalog.VendorIds, _catalog.ProductIds);
        _log.Write(string.Format(
            CultureInfo.InvariantCulture,
            "Initialize: {0} build, scan located {1} Lian Li controller(s)",
            buildVariant,
            located.Count));

        foreach (HidDeviceInfo info in located)
        {
            if (!_catalog.TryGetProtocol(info.ProductId, out IFanProtocol? protocol))
            {
                continue;
            }

            try
            {
                IHidTransport transport = _enumerator.Open(info);
                var controller = new FanController(
                    _controllers.Count, transport, protocol, _clock, _log);
                _controllers.Add(controller);
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "  controller pid=0x{0:x4} family={1} path={2}",
                    info.ProductId,
                    protocol.Family,
                    info.DevicePath));
            }
#pragma warning disable CA1031 // resilience: a device that fails to open is skipped, not fatal
            catch (Exception ex)
            {
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "  open failed pid=0x{0:x4}: {1}",
                    info.ProductId,
                    ex.Message));
            }
#pragma warning restore CA1031
        }

        _worker = new KeepAliveWorker(_controllers.ToArray(), _log);
        _worker.Start();
    }

    /// <summary>Register four control sensors and four fan sensors per controller.</summary>
    public void Load(IPluginSensorsContainer container)
    {
        if (container is null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        for (int ci = 0; ci < _controllers.Count; ci++)
        {
            FanController controller = _controllers[ci];
            for (int ch = 0; ch < 4; ch++)
            {
                container.ControlSensors.Add(new ControlSensor(controller, ci, ch));
                container.FanSensors.Add(new FanSensor(controller, ci, ch));
            }
        }
    }

    /// <summary>Host update tick: pump pending writes and RPM polling for every controller.</summary>
    public void Update()
    {
        lock (_sync)
        {
            _worker?.Tick();
        }
    }

    /// <summary>Tear down the worker and controllers. Null-guarded against a never-initialized plugin.</summary>
    public void Close()
    {
        lock (_sync)
        {
            TearDownLocked();
        }
    }

    /// <summary>Dispose the plugin. Equivalent to <see cref="Close"/>; safe to call more than once.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            TearDownLocked();
        }

        GC.SuppressFinalize(this);
    }

    // Caller must hold _sync.
    private void TearDownLocked()
    {
        _worker?.Dispose();
        _worker = null;
        _controllers.Clear();
    }
}
