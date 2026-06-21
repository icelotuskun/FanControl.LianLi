using System;

namespace FanControl.LianLi.Devices;

/// <summary>
/// One physical controllable device the worker drives: a fan/pump controller with a
/// fixed set of channels. The FanControl-thread surface (<see cref="SetTarget"/>,
/// <see cref="ReleaseChannel"/>, <see cref="GetRpm"/>) only mutates in-memory state;
/// every USB transfer happens on the worker-thread methods (<see cref="ApplyPending"/>,
/// <see cref="PollRpm"/>). This is the seam the worker and the plugin's sensor wiring
/// depend on, so a device family (the Uni 0x0CF2 controllers, the 0x0416 command-packet
/// controllers) is plugged in without either of them knowing the family.
/// </summary>
internal interface IFanDevice : IDisposable {
    /// <summary>How many controllable channels this device exposes.</summary>
    int ChannelCount { get; }

    /// <summary>
    /// The stable sensor identity and display names for <paramref name="channel"/>. The
    /// ids are keyed so a user's saved fan-curve bindings survive a restart, so a device
    /// must return the same strings run to run for the same physical channel.
    /// </summary>
    ChannelDescriptor Describe(int channel);

    /// <summary>Set the commanded duty for a channel. The worker pushes it to hardware.</summary>
    void SetTarget(int channel, int duty);

    /// <summary>Release a channel so the keepalive stops asserting it (used by Reset).</summary>
    void ReleaseChannel(int channel);

    /// <summary>Read the last measured RPM for a channel.</summary>
    float GetRpm(int channel);

    /// <summary>Push any changed-or-stale channel targets to the hardware.</summary>
    void ApplyPending();

    /// <summary>Read every channel's RPM into the cache, ignoring implausible readings.</summary>
    void PollRpm();
}
