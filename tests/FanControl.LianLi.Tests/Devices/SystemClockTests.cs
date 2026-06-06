using System;
using FanControl.LianLi.Devices;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        DateTime before = DateTime.UtcNow;
        DateTime value = new SystemClock().UtcNow;
        DateTime after = DateTime.UtcNow;

        Assert.InRange(value, before, after);
    }
}
