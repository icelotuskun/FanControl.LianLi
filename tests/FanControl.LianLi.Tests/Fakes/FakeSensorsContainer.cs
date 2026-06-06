using System.Collections.Generic;
using FanControl.Plugins;

namespace FanControl.LianLi.Tests.Fakes;

internal sealed class FakeSensorsContainer : IPluginSensorsContainer
{
    public List<IPluginControlSensor> ControlSensors { get; } = new List<IPluginControlSensor>();

    public List<IPluginSensor> FanSensors { get; } = new List<IPluginSensor>();

    public List<IPluginSensor> TempSensors { get; } = new List<IPluginSensor>();
}
