# FanControl.LianLi

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![Latest release](https://img.shields.io/github/v/release/lewisgibson/FanControl.LianLi?sort=semver)](https://github.com/lewisgibson/FanControl.LianLi/releases/latest)

A plugin for [FanControl](https://getfancontrol.com/) that controls Lian Li Uni fan controllers. It adds each fan channel as a control you can put on a curve, and reports each fan's RPM.

This is an unofficial, community plugin. It is not affiliated with, authorized by, or endorsed by Lian Li or by the FanControl project.

## Supported controllers

| Controller      | USB product id(s) | Set fan speed | Read RPM |
| --------------- | ----------------- | ------------- | -------- |
| Uni Hub / SL    | `7750`, `A100`    | yes           | yes      |
| Uni AL          | `A101`            | yes           | yes      |
| Uni SL-Infinity | `A102`            | yes           | yes      |
| Uni SL v2       | `A103`, `A105`    | yes           | yes      |
| Uni AL v2       | `A104`            | yes           | yes      |

## Two builds: standard and ARGB

There are two builds. Install **one, not both**:

- **Standard** (`FanControl.LianLi.dll`) -- controls fan speed and reads RPM, and never touches lighting. This is what most people want.
- **ARGB** (`FanControl.LianLi.Argb.dll`) -- everything the standard build does, and also syncs the fans' lighting to the motherboard's ARGB header at startup (so your motherboard's RGB software drives the fan LEDs). Read [Standard vs ARGB](#standard-vs-argb) before picking this one.

Both expose the same controls and RPM sensors, so you can switch between them later without losing your fan-curve bindings: close FanControl, delete the DLL you have (it is locked while FanControl is running), then reopen and install the other. Never leave both in the Plugins folder.

## Do not run L-Connect at the same time

Lian Li's own **L-Connect** software drives the same USB controller as this plugin and writes to it aggressively, so the two fight each other. Before using this plugin, **uninstall L-Connect**, or at least fully exit it and stop its background process:

1. Open Task Manager (Ctrl+Shift+Esc).
2. End any task named **L-Connect** or **L ConnectSystem** -- the exact name varies by version, so end anything from Lian Li.

Leaving L-Connect running is the most common cause of erratic fan speeds or lighting with this plugin.

## Install

1. **Download** the zip for the build you want from the [latest release](https://github.com/lewisgibson/FanControl.LianLi/releases/latest) (`FanControl.LianLi-vX.Y.Z.zip` for standard, or `FanControl.LianLi-argb-vX.Y.Z.zip` for ARGB) and extract the `.dll`.

2. **Unblock the DLL.** Right-click the extracted `.dll`, choose **Properties**, tick **Unblock** at the bottom, then **OK**. Windows marks files downloaded from the internet as blocked, and FanControl silently ignores a blocked plugin.

3. **Install it in FanControl.** Open the menu and click **Install plugin**, then pick the `.dll`. It loads immediately -- no restart needed.

That single DLL is all you need -- HidSharp ships with FanControl already. Your Lian Li channels now appear as controls (assign each to a fan curve) and as RPM sensors.

**Upgrading later:** download the newer zip, unblock the `.dll`, and install it through FanControl the same way. Your fan-curve bindings are preserved.

## Standard vs ARGB

Pick **standard** unless you specifically want this plugin to drive ARGB sync.

The ARGB build asserts LED ARGB-header sync at startup, handing the fans' lighting to the motherboard's ARGB header. Controllers that store their lighting in their own memory handle this fine. But some controllers do **not** persist lighting to hardware (for example the **UNI FAN SL-Infinity 120 V1**); on those, asserting this at startup makes the lighting **revert to factory defaults every time the plugin starts**. If your lighting keeps resetting, switch to the standard build.

### Troubleshooting

- **The controls do not show up.** Make sure you unblocked the file (step 2) before installing it. Make sure **L-Connect is not running** (see above) -- it is the most common conflict; OpenRGB and other tools that open the same controller can clash too.
- **Lighting resets to factory on every boot.** You are on the ARGB build with a controller that does not persist lighting; use the standard build.
- **Submitting a bug?** Include your controller's Name, VID, and PID from Windows Device Manager. The bug-report template walks you through it.

## Build from source

You need the .NET 9 SDK on Windows. The plugin targets `netstandard2.0`.

```
dotnet build -c Release
```

The built DLL is at `src/FanControl.LianLi/bin/Release/netstandard2.0/FanControl.LianLi.dll`. To build the ARGB variant instead, add `-p:EnableArgb=true -p:AssemblyName=FanControl.LianLi.Argb`, which produces `FanControl.LianLi.Argb.dll`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full build/test loop and [docs/](docs/) for the architecture, the device protocol, and deployment notes.

## Contributing

Contributions are welcome -- see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT -- see [LICENSE](LICENSE). The plugin uses [HidSharp](https://www.nuget.org/packages/HidSharp/) (Apache-2.0) at runtime, provided by the FanControl host; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Acknowledgements

The Lian Li Uni wire protocol was learned from prior open-source work. This is a clean-room reimplementation that reuses only the protocol facts (report ids, byte offsets, duty formulas), not any third-party source code:

- [uni-sync](https://github.com/EightB1ts/uni-sync) by Cameron Halter -- the Rust sync tool the protocol facts were taken from (MIT).
- [FanControl.LianLi](https://github.com/EightB1ts/FanControl.LianLi) by Cameron Halter -- the original FanControl plugin that inspired this one (LGPL-2.1).
- [liquidctl](https://github.com/liquidctl/liquidctl) -- used as a protocol reference (GPL-3.0-or-later).
