using System;

namespace FanControl.LianLi.Devices;

/// <summary>
/// The stable identity of one device channel as FanControl sees it: the writable
/// control's id/name and the read-only RPM sensor's id/name. A device owns the naming
/// (rather than the sensor) so each family's scheme stays in one place and the ids stay
/// byte-stable across restarts - changing one orphans the user's saved curve bindings.
/// </summary>
internal readonly struct ChannelDescriptor {
    /// <summary>Create the identity for one channel. All four strings are required.</summary>
    public ChannelDescriptor(string controlId, string controlName, string rpmId, string rpmName) {
        ControlId = controlId ?? throw new ArgumentNullException(nameof(controlId));
        ControlName = controlName ?? throw new ArgumentNullException(nameof(controlName));
        RpmId = rpmId ?? throw new ArgumentNullException(nameof(rpmId));
        RpmName = rpmName ?? throw new ArgumentNullException(nameof(rpmName));
    }

    /// <summary>The writable control sensor's id (distinct from <see cref="RpmId"/>).</summary>
    public string ControlId { get; }

    /// <summary>The writable control sensor's display name.</summary>
    public string ControlName { get; }

    /// <summary>The read-only RPM sensor's id (distinct from <see cref="ControlId"/>).</summary>
    public string RpmId { get; }

    /// <summary>The read-only RPM sensor's display name.</summary>
    public string RpmName { get; }
}
