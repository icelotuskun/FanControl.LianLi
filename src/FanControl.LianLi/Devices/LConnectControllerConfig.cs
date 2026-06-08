#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.Devices;

/// <summary>
/// One controller's lighting look as read from L-Connect's saved configuration: the USB
/// instance token used to match it to a located device, the saved per-port looks, and the
/// optional per-group fan quantity. <see cref="SlInfinityLightingEncoder"/> turns these into
/// the HID transfers that reproduce the look on an SL-Infinity controller.
/// </summary>
internal sealed class LConnectControllerConfig
{
    /// <summary>Create a controller look from its parsed L-Connect settings.</summary>
    public LConnectControllerConfig(
        string instanceToken,
        IReadOnlyList<LightingPortState> ports,
        IReadOnlyList<int>? quantity)
    {
        if (string.IsNullOrEmpty(instanceToken))
        {
            throw new ArgumentException("Instance token is required.", nameof(instanceToken));
        }

        InstanceToken = instanceToken;
        Ports = ports ?? throw new ArgumentNullException(nameof(ports));
        Quantity = quantity;
    }

    /// <summary>
    /// The USB instance token (e.g. <c>71d6ab5</c>) from the saved <c>DeviceID</c>. A located
    /// controller matches when this token appears in its OS device path.
    /// </summary>
    public string InstanceToken { get; }

    /// <summary>The saved per-port looks (one per configured port).</summary>
    public IReadOnlyList<LightingPortState> Ports { get; }

    /// <summary>The saved fan quantity per group, or null when L-Connect saved none.</summary>
    public IReadOnlyList<int>? Quantity { get; }
}
#endif
