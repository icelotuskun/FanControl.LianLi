**FanControl.LianLi v{{VERSION}}** - released {{DATE}}

An unofficial FanControl plugin for Lian Li Uni fan controllers. It adds each fan channel as a control you can put on a curve, and reports each fan's RPM.

## Which download?

Install **one**, not several:

### 🌀 Standard - `FanControl.LianLi-v{{VERSION}}.zip`

Fan speed and RPM control. Lighting is left untouched - drive it yourself with OpenRGB if you want.

### 🌈 ARGB - `FanControl.LianLi-Argb-v{{VERSION}}.zip`

Everything Standard does, and also hands the fan lighting to your motherboard's ARGB header at startup, so the motherboard's RGB software drives it. On controllers that do not save lighting to hardware (e.g. UNI FAN SL-Infinity 120 V1) the lighting resets to factory on every boot.

### 🎨 Lighting - `FanControl.LianLi-Lighting-v{{VERSION}}.zip`

Everything Standard does, and also re-applies a look you designed in L-Connect - set it up once in L-Connect, stop L-Connect, and keep the look. Uni SL-Infinity only; with no L-Connect config it behaves exactly like Standard.

Each `.zip` contains a single DLL plus `LICENSE.txt` and `INSTALL.txt` at the root. The matching `.sha256` is that zip's SHA-256 checksum (optional integrity check).

## Install

1. Download the zip you picked above and extract the DLL (it sits at the zip root).
2. **Unblock the DLL** (important): right-click it -> Properties -> tick **Unblock** -> OK. Windows blocks files downloaded from the internet, and FanControl silently ignores a blocked plugin.
3. In FanControl, open the menu, click **Install plugin**, and pick the DLL. It loads immediately - **no restart needed**.

Your Lian Li channels then appear as controls (assign each to a fan curve) and as RPM sensors.

## Do not run L-Connect at the same time

Lian Li's **L-Connect** drives the same USB controller as this plugin and writes to it aggressively, so the two fight each other. Uninstall L-Connect, or fully exit it and stop its background process (`L ConnectSystem.exe` in Task Manager) before using this plugin. Leaving it running is the most common cause of erratic fan speeds or lighting.

## Upgrading

Download the newer zip, unblock the DLL, and install it through FanControl the same way - no restart needed. Your fan-curve bindings are preserved.

## What's changed
