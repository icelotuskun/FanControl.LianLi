using System.Collections.Generic;
using FanControl.Plugins;

namespace FanControl.LianLi.Tests.Fakes;

internal sealed class FakePluginLogger : IPluginLogger
{
    public List<string> Messages { get; } = new List<string>();

    public void Log(string message) => Messages.Add(message);
}
