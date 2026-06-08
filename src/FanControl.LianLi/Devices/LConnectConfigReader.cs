#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.Devices;

/// <summary>
/// Reads L-Connect's own saved lighting configuration directly from its config directory and
/// groups it into a per-controller <see cref="LConnectControllerConfig"/>. There is no
/// intermediary export file: the Lighting plugin reproduces whatever look L-Connect last
/// saved. Each setting is a gzipped JSON file holding <c>{ DeviceID, Type, Data }</c>; the
/// reader keeps the <c>LightingPort*</c> and <c>FanQuantity</c> settings and ignores the rest
/// (fan curves, merge order, motherboard sync). Files that are not device settings are
/// skipped by content; a genuinely unreadable file throws and lets the caller disable
/// lighting rather than apply a partial look.
/// </summary>
internal static class LConnectConfigReader
{
    // DeviceID looks like "...&mi_01#d&71d6ab5&0&0000#{...}". The instance token is the first
    // long hex run inside the mi_01 instance segment.
    private static readonly Regex InstanceSegment = new Regex(@"mi_01#(.*?)#\{", RegexOptions.CultureInvariant);
    private static readonly Regex HexToken = new Regex("[0-9a-fA-F]{5,}", RegexOptions.CultureInvariant);

    // Ceiling on a single setting's decompressed size. Genuine L-Connect settings are a few KB;
    // the cap stops a small crafted gzip from expanding to gigabytes and exhausting the host's
    // memory (it runs as SYSTEM). Exceeding it throws a catchable FormatException.
    private const int MaxDecompressedBytes = 1024 * 1024;

    /// <summary>L-Connect 3's default per-device configuration directory (under ProgramData).</summary>
    public static string DefaultConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Lian-Li",
        "L-Connect 3",
        "device");

    /// <summary>
    /// Read every controller's saved look from <paramref name="directory"/>. Returns an empty
    /// list when the directory is absent (L-Connect not installed). Throws on a corrupt file so
    /// the caller can disable lighting deliberately.
    /// </summary>
    public static IReadOnlyList<LConnectControllerConfig> Read(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<LConnectControllerConfig>();
        }

        var builders = new Dictionary<string, Builder>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in Directory.GetDirectories(directory))
        {
            foreach (string file in Directory.GetFiles(folder, "*.0"))
            {
                JsonValue root = ParseFile(file);
                string? deviceId = root.Member("DeviceID")?.AsString();
                string? type = root.Member("Type")?.AsString();
                if (deviceId is null || type is null)
                {
                    continue;
                }

                string? token = ExtractInstanceToken(deviceId);
                if (token is null)
                {
                    continue;
                }

                if (!builders.TryGetValue(token, out Builder? builder))
                {
                    builder = new Builder(token);
                    builders[token] = builder;
                }

                JsonValue? data = root.Member("Data");
                if (data is null)
                {
                    continue;
                }

                if (type.StartsWith("LightingPort", StringComparison.Ordinal))
                {
                    builder.AddPort(ReadPort(data));
                }
                else if (type == "FanQuantity")
                {
                    builder.SetQuantity(ReadIntArray(data));
                }
            }
        }

        var configs = new List<LConnectControllerConfig>();
        foreach (Builder builder in builders.Values)
        {
            LConnectControllerConfig? config = builder.Build();
            if (config != null)
            {
                configs.Add(config);
            }
        }

        return configs;
    }

    private static JsonValue ParseFile(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var buffer = new MemoryStream();

        byte[] chunk = new byte[8192];
        int read;
        while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxDecompressedBytes)
            {
                throw new FormatException(string.Format(
                    CultureInfo.InvariantCulture,
                    "L-Connect config file {0} decompresses to more than {1} bytes.",
                    Path.GetFileName(path),
                    MaxDecompressedBytes));
            }

            buffer.Write(chunk, 0, read);
        }

        return JsonValue.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static LightingPortState? ReadPort(JsonValue data)
    {
        int? port = data.Member("Port")?.AsInt();
        int? mode = data.Member("Mode")?.AsInt();
        if (port is null || mode is null)
        {
            return null;
        }

        int speed = data.Member("Speed")?.AsInt() ?? 0;
        int direction = data.Member("Direction")?.AsInt() ?? 0;
        int brightness = data.Member("Brightness")?.AsInt() ?? 0;

        var colors = new List<RgbColor>();
        JsonValue? colorArray = data.Member("Colors");
        if (colorArray != null)
        {
            foreach (JsonValue color in colorArray.Elements)
            {
                int r = color.Member("R")?.AsInt() ?? 0;
                int g = color.Member("G")?.AsInt() ?? 0;
                int b = color.Member("B")?.AsInt() ?? 0;
                colors.Add(new RgbColor((byte)r, (byte)g, (byte)b));
            }
        }

        return new LightingPortState(port.Value, mode.Value, speed, direction, brightness, colors);
    }

    private static List<int> ReadIntArray(JsonValue data)
    {
        var values = new List<int>();
        foreach (JsonValue value in data.Elements)
        {
            values.Add(value.AsInt() ?? 0);
        }

        return values;
    }

    private static string? ExtractInstanceToken(string deviceId)
    {
        Match segment = InstanceSegment.Match(deviceId);
        string inner = segment.Success ? segment.Groups[1].Value : deviceId;
        Match token = HexToken.Match(inner);
        return token.Success ? token.Value : null;
    }

    // Accumulates the settings that share one instance token into a single controller look.
    private sealed class Builder
    {
        private readonly string _token;
        private readonly List<LightingPortState> _ports = new List<LightingPortState>();
        private IReadOnlyList<int>? _quantity;

        public Builder(string token)
        {
            _token = token;
        }

        public void AddPort(LightingPortState? port)
        {
            if (port != null)
            {
                _ports.Add(port);
            }
        }

        public void SetQuantity(IReadOnlyList<int> quantity)
        {
            _quantity = quantity;
        }

        public LConnectControllerConfig? Build() =>
            _ports.Count == 0 ? null : new LConnectControllerConfig(_token, _ports, _quantity);
    }
}
#endif
