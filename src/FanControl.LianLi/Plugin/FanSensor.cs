using System;
using FanControl.LianLi.Devices;
using FanControl.Plugins;

namespace FanControl.LianLi.Plugin;

/// <summary>
/// Read-only RPM sensor for one channel. <see cref="Update"/> publishes the
/// cached RPM the worker last measured; it performs no I/O.
/// </summary>
internal sealed class FanSensor : IPluginSensor {
    private readonly FanController _controller;
    private readonly int _channel;

    public FanSensor(FanController controller, int controllerIndex, int channel) {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _channel = channel;
        Id = $"LianLi/{controllerIndex}/ch{channel}/fan";
        Name = $"Lian Li Uni #{controllerIndex + 1} Ch {channel + 1} RPM";
    }

    public string Id { get; }

    public string Name { get; }

    public float? Value { get; private set; }

    public void Update() => Value = _controller.GetRpm(_channel);
}
