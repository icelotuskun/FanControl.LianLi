using System;
using System.Linq;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Hid;
using FanControl.LianLi.Plugin;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.Plugin;

public class LianLiPluginTests
{
    // The ARGB build advertises a distinct plugin name; assert against the variant in play.
#if ENABLE_ARGB
    private const string ExpectedName = "Lian Li Uni (ARGB)";
#else
    private const string ExpectedName = "Lian Li Uni";
#endif

    private static LianLiPlugin NewPlugin(FakeEnumerator enumerator)
        => new LianLiPlugin(enumerator, new DeviceCatalog(), new FakeClock(), new FakeLogger());

    private static HidDeviceInfo Sli(int index)
        => new HidDeviceInfo(0x0CF2, 0xA102, "fake/" + index, null);

    [Fact]
    public void Name_MatchesBuildVariant()
        => Assert.Equal(ExpectedName, NewPlugin(new FakeEnumerator()).Name);

    [Fact]
    public void PublicConstructor_ComposesWithHostLogger()
    {
        // Exercises the production composition root (real enumerator/clock/loggers)
        // without enumerating hardware, since Initialize is not called.
        using var plugin = new LianLiPlugin(new FakePluginLogger());
        Assert.Equal(ExpectedName, plugin.Name);
    }

    [Fact]
    public void InitializeThenLoad_RegistersFourControlAndFourFanSensorsPerController()
    {
        var enumerator = new FakeEnumerator(Sli(0), Sli(1));
        using LianLiPlugin plugin = NewPlugin(enumerator);

        plugin.Initialize();
        var container = new FakeSensorsContainer();
        plugin.Load(container);

        Assert.Equal(8, container.ControlSensors.Count);
        Assert.Equal(8, container.FanSensors.Count);

        var ids = container.ControlSensors.Select(s => s.Id)
            .Concat(container.FanSensors.Select(s => s.Id))
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count()); // every id is unique

        plugin.Close();
    }

    [Fact]
    public void Lifecycle_IsRepeatableAndDisposesTransports()
    {
        var enumerator = new FakeEnumerator(Sli(0));
        var plugin = NewPlugin(enumerator);

        for (int cycle = 0; cycle < 2; cycle++)
        {
            plugin.Initialize();
            plugin.Load(new FakeSensorsContainer());
            plugin.Close();
        }

        Assert.NotEmpty(enumerator.Opened);
        Assert.All(enumerator.Opened, transport => Assert.True(transport.IsDisposed));

        plugin.Dispose();
    }

    [Fact]
    public void Initialize_SkipsDevicesThatFailToOpen()
    {
        var enumerator = new FakeEnumerator(Sli(0)) { FailOpen = true };
        using LianLiPlugin plugin = NewPlugin(enumerator);

        plugin.Initialize(); // the open throws; the device is logged and skipped, not fatal
        var container = new FakeSensorsContainer();
        plugin.Load(container);

        Assert.Empty(container.ControlSensors);
        Assert.Empty(container.FanSensors);
    }

    [Fact]
    public void Close_BeforeInitialize_DoesNotThrow()
    {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator());
        plugin.Close(); // fixes the original unconditional-dispose NRE
    }

    [Fact]
    public void InitializeLoadClose_WithNoDevices_DoesNotThrow()
    {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator());
        var container = new FakeSensorsContainer();

        plugin.Initialize();
        plugin.Load(container);
        plugin.Close();

        Assert.Empty(container.ControlSensors);
        Assert.Empty(container.FanSensors);
    }

    [Fact]
    public void Load_WithNullContainer_Throws()
    {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator());
        plugin.Initialize();
        Assert.Throws<ArgumentNullException>(() => plugin.Load(null!));
    }
}
