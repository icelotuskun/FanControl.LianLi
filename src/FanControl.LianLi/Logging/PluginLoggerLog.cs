using System;
using FanControl.Plugins;

namespace FanControl.LianLi.Logging;

/// <summary>
/// Adapts the host-supplied <see cref="IPluginLogger"/> to the internal
/// <see cref="ILog"/> seam so the rest of the plugin depends only on
/// <see cref="ILog"/>. The host logger is treated as optional: a null logger or a
/// throwing one is swallowed, because logging must never disrupt fan control.
/// </summary>
internal sealed class PluginLoggerLog : ILog
{
    private readonly IPluginLogger? _logger;

    public PluginLoggerLog(IPluginLogger? logger)
    {
        _logger = logger;
    }

    public void Write(string message)
    {
        try
        {
            _logger?.Log(message);
        }
#pragma warning disable CA1031 // logging must never throw
        catch (Exception)
        {
            // Intentionally swallowed: the host logger must never disrupt fan control.
        }
#pragma warning restore CA1031
    }
}
