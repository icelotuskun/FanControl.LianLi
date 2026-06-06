using System;

namespace FanControl.LianLi.Logging;

/// <summary>
/// Fans a single log line out to several sinks (for example the host
/// <c>IPluginLogger</c> and the local <see cref="FileLogger"/>). A failure in
/// one sink never prevents the others from receiving the message.
/// </summary>
internal sealed class CompositeLog : ILog
{
    private readonly ILog[] _sinks;

    public CompositeLog(params ILog[] sinks)
    {
        _sinks = sinks ?? Array.Empty<ILog>();
    }

    public void Write(string message)
    {
        for (int i = 0; i < _sinks.Length; i++)
        {
            _sinks[i]?.Write(message);
        }
    }
}
