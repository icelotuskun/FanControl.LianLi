using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FanControl.LianLi.Devices;

/// <summary>
/// Reads L-Connect's per-group start/stop toggle for one controller directly from its saved profile.
/// L-Connect exposes a start/stop switch on the start/stop-capable families; when it is on, the
/// controller sends its stop value at the bottom of the curve, which physically stops 0rpm-capable
/// fans (controllers that cannot stop, such as SL-Infinity, floor instead and ignore it). The plugin
/// honors that switch so a 0% request only stops a fan the user actually enabled it for.
///
/// The toggle lives in <c>...\Lian-Li\L-Connect 3\profile\{md5}</c> - a gzip-compressed JSON profile
/// named MD5(devicePath.ToLowerInvariant()). Each <c>SubProfiles</c> entry is one fan group whose
/// <c>RPMSetting.Mode</c> selects the active profile; that profile's <c>IsStartStop</c> is the switch.
/// This is a sink for the config only - no HID and no device state.
/// </summary>
internal static class StartStopConfigReader {
    /// <summary>L-Connect 3's default per-device profile directory (sibling of the lighting "device" dir).</summary>
    public static string DefaultProfileDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Lian-Li",
        "L-Connect 3",
        "profile");

    /// <summary>
    /// Read the per-channel start/stop flags for the controller at <paramref name="devicePath"/>.
    /// Returns a <paramref name="channelCount"/>-length array, <c>false</c> on every channel with no
    /// saved profile (directory or file absent - e.g. L-Connect not installed). Throws
    /// <see cref="FormatException"/> / <see cref="IOException"/> on a corrupt profile so the caller
    /// can log it and degrade deliberately.
    /// </summary>
    public static bool[] Read(string profileDirectory, string devicePath, int channelCount) {
        var flags = new bool[Math.Max(channelCount, 0)]; // default: start/stop off on every channel
        if (string.IsNullOrEmpty(profileDirectory)
            || string.IsNullOrEmpty(devicePath)
            || channelCount <= 0
            || !Directory.Exists(profileDirectory)) {
            return flags;
        }

        string file = Path.Combine(profileDirectory, ProfileFileName(devicePath));
        if (!File.Exists(file)) {
            return flags;
        }

        JsonValue root = JsonValue.Parse(Decompress(file));
        JsonValue? subProfiles = root.Member("SubProfiles");
        if (subProfiles is null) {
            return flags;
        }

        // Map each group to its physical channel by GroupIndex, so a reordered or partial profile
        // still lands on the right channel rather than shifting the whole set.
        foreach (JsonValue group in subProfiles.Elements) {
            if (group.Member("GroupIndex")?.AsInt() is int groupIndex
                && groupIndex >= 0
                && groupIndex < flags.Length) {
                flags[groupIndex] = ReadGroupStartStop(group);
            }
        }

        return flags;
    }

    /// <summary>
    /// The profile filename L-Connect uses for a device: MD5(devicePath.ToLowerInvariant()) as
    /// lowercase hex. This is a filename lookup that must reproduce L-Connect's own scheme, not a
    /// security context, so MD5 is the correct (and required) algorithm. Exposed so a test can place
    /// a fixture at the exact name this reader looks up.
    /// </summary>
#pragma warning disable CA5351 // MD5 is used to match L-Connect's profile filename, not for security
    internal static string ProfileFileName(string devicePath) {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(devicePath.ToLowerInvariant()));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash) {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
#pragma warning restore CA5351

    private static string Decompress(string file) {
        using FileStream source = File.OpenRead(file);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // A group's RPMSetting.Mode selects the active profile; the active profile is the entry in
    // Profiles whose own Mode matches it, and that profile's IsStartStop is the switch. Matching on
    // Mode (not a hard-coded profile name) keeps this independent of the profile-name ordering.
    private static bool ReadGroupStartStop(JsonValue group) {
        JsonValue? rpmSetting = group.Member("RPMSetting");
        int? activeMode = rpmSetting?.Member("Mode")?.AsInt();
        JsonValue? profiles = rpmSetting?.Member("Profiles");
        if (activeMode is null || profiles is null) {
            return false;
        }

        foreach (string name in profiles.MemberNames) {
            JsonValue? profile = profiles.Member(name);
            if (profile?.Member("Mode")?.AsInt() == activeMode) {
                return profile.Member("IsStartStop")?.AsBool() ?? false;
            }
        }

        return false;
    }
}
