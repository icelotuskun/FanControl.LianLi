using System;
using FanControl.LianLi.Devices;
using FanControl.Plugins;

namespace FanControl.LianLi.Plugin;

/// <summary>
/// Read-only RPM sensor for one channel. <see cref="Update"/> publishes the
/// cached RPM the worker last measured; it performs no I/O.
/// </summary>
internal sealed class FanSensor : IPluginSensor {
    private readonly IFanDevice _controller;
    private readonly int _channel;

    public FanSensor(IFanDevice controller, int channel) {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _channel = channel;
        ChannelDescriptor descriptor = controller.Describe(channel);
        Id = descriptor.RpmId;
        Name = descriptor.RpmName;
    }

    public string Id { get; }

    public string Name { get; }

    public float? Value { get; private set; }

    public void Update() => Value = _controller.GetRpm(_channel);
}
