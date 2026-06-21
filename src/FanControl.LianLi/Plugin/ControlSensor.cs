using System;
using FanControl.LianLi.Devices;
using FanControl.Plugins;

namespace FanControl.LianLi.Plugin;

/// <summary>
/// Writable control for one channel. <see cref="Set"/> only hands the target to
/// the controller's in-memory state (the worker performs the USB write), and
/// <see cref="Reset"/> releases the channel so the keepalive stops asserting it.
/// Its id is distinct from the matching fan sensor's to avoid a registry collision.
/// </summary>
internal sealed class ControlSensor : IPluginControlSensor {
    private readonly IFanDevice _controller;
    private readonly int _channel;
    private float? _commanded;

    public ControlSensor(IFanDevice controller, int channel) {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _channel = channel;
        ChannelDescriptor descriptor = controller.Describe(channel);
        Id = descriptor.ControlId;
        Name = descriptor.ControlName;
    }

    public string Id { get; }

    public string Name { get; }

    public float? Value { get; private set; }

    public void Update() => Value = _commanded;

    public void Set(float val) {
        _controller.SetTarget(_channel, (int)val);
        _commanded = val;
    }

    public void Reset() {
        _controller.ReleaseChannel(_channel);
        _commanded = null;
    }
}
