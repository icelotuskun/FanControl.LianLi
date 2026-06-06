using System.Collections.Generic;
using FanControl.LianLi.Logging;

namespace FanControl.LianLi.Tests.Fakes;

internal sealed class FakeLogger : ILog
{
    public List<string> Messages { get; } = new List<string>();

    public void Write(string message) => Messages.Add(message);
}
