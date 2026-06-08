using System;
using System.Globalization;
using System.IO;

namespace FanControl.LianLi.Logging;

/// <summary>
/// Crash-safe file logger. Writes timestamped lines to
/// <c>%LOCALAPPDATA%\FanControl.LianLi\plugin.log</c>, falling back to the
/// system temp directory if that location cannot be created. Every operation
/// is guarded so that logging can never throw into the caller.
/// </summary>
internal sealed class FileLogger : ILog {
    private readonly object _gate = new object();
    private readonly string _filePath;

    public FileLogger() {
        string dir;
        try {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FanControl.LianLi");
            Directory.CreateDirectory(dir);
        }
#pragma warning disable CA1031 // logging must never throw: any failure falls back to TEMP
        catch (Exception) {
            dir = Path.GetTempPath();
        }
#pragma warning restore CA1031
        _filePath = Path.Combine(dir, "plugin.log");
    }

    /// <summary>The resolved path the logger appends to (for diagnostics/tests).</summary>
    public string FilePath => _filePath;

    public void Write(string message) {
        try {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + "  " + message + Environment.NewLine;
            lock (_gate) {
                File.AppendAllText(_filePath, line);
            }
        }
#pragma warning disable CA1031 // logging must never throw
        catch (Exception) {
            // Intentionally swallowed: a logging failure must never disrupt fan control.
        }
#pragma warning restore CA1031
    }
}
