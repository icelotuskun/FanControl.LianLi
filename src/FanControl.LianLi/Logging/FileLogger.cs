using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FanControl.LianLi.Logging;

/// <summary>
/// Crash-safe file logger. Writes timestamped lines to
/// <c>%LOCALAPPDATA%\FanControl.LianLi\plugin.log</c>, falling back to the
/// system temp directory if that location cannot be created. The file is size
/// capped: once a line would push it past the cap the current file is rolled to
/// <c>plugin.log.1</c> (replacing any previous roll) and a fresh file started, so
/// the log self-clears and at most two files are ever kept. Every operation is
/// guarded so that logging can never throw into the caller.
/// </summary>
internal sealed class FileLogger : ILog {
    // Cap the active file so a plugin that logs routine activity cannot grow without bound (the
    // 100MB+ files seen in the field). At the cap the file rolls to a single backup, so total on-disk
    // history is bounded to roughly two caps' worth - recent enough to diagnose, small enough to keep.
    private const long DefaultMaxFileBytes = 5L * 1024 * 1024;

    private readonly object _gate = new object();
    private readonly string _filePath;
    private readonly string _rolledPath;
    private readonly long _maxFileBytes;

    // Running size of the active file, tracked in-memory so a stat is not needed on every line. Seeded
    // from any existing file at construction, reset to zero on each roll. Guarded by _gate.
    private long _currentBytes;

    public FileLogger()
        : this(ResolveDefaultPath(), DefaultMaxFileBytes) {
    }

    // Test/diagnostic seam: an explicit path and cap so rotation can be exercised without writing
    // megabytes. The public constructor resolves the real %LOCALAPPDATA% path and the production cap.
    internal FileLogger(string filePath, long maxFileBytes) {
        if (string.IsNullOrEmpty(filePath)) {
            throw new ArgumentException("Log file path is required.", nameof(filePath));
        }

        if (maxFileBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "Cap must be positive.");
        }

        _filePath = filePath;
        _rolledPath = filePath + ".1";
        _maxFileBytes = maxFileBytes;
        _currentBytes = FileLength(_filePath);
    }

    private static string ResolveDefaultPath() {
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
        return Path.Combine(dir, "plugin.log");
    }

    /// <summary>The resolved path the logger appends to (for diagnostics/tests).</summary>
    public string FilePath => _filePath;

    public void Write(string message) {
        try {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + "  " + message + Environment.NewLine;
            int lineBytes = Encoding.UTF8.GetByteCount(line);
            lock (_gate) {
                RollIfAtCapacity(lineBytes);
                File.AppendAllText(_filePath, line);
                _currentBytes += lineBytes;
            }
        }
#pragma warning disable CA1031 // logging must never throw
        catch (Exception) {
            // Intentionally swallowed: a logging failure must never disrupt fan control.
        }
#pragma warning restore CA1031
    }

    // Roll the active file to the single backup slot once the next line would exceed the cap, so the
    // active file never grows past the cap and total history stays bounded to two files. A single line
    // larger than the whole cap on an empty file is written rather than looping forever on an empty roll.
    // Caller holds _gate.
    private void RollIfAtCapacity(int nextLineBytes) {
        if (_currentBytes == 0 || _currentBytes + nextLineBytes <= _maxFileBytes) {
            return;
        }

        if (File.Exists(_rolledPath)) {
            File.Delete(_rolledPath);
        }

        if (File.Exists(_filePath)) {
            File.Move(_filePath, _rolledPath);
        }

        _currentBytes = 0;
    }

    private static long FileLength(string path) {
        try {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
#pragma warning disable CA1031 // logging must never throw: a stat failure just starts the counter at 0
        catch (Exception) {
            return 0;
        }
#pragma warning restore CA1031
    }
}
