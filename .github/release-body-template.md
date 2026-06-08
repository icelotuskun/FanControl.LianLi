**FanControl.LianLi v{{VERSION}}** -- released {{DATE}}

An unofficial FanControl plugin for Lian Li Uni fan controllers. It adds each fan channel as a control you can put on a curve, and reports each fan's RPM.

## Which download?

Install **one**, not several:

| File | Pick this if | Lighting |
| --- | --- | --- |
| `FanControl.LianLi-v{{VERSION}}.zip` (standard) | You want fan speed + RPM control (most people). | Left untouched -- manage RGB in your motherboard software, OpenRGB, or L-Connect. |
| `FanControl.LianLi-Argb-v{{VERSION}}.zip` (ARGB) | You want your motherboard's RGB software to drive the fan lighting. | Hands lighting to the motherboard's ARGB header at startup. On controllers that do not save lighting to hardware (e.g. UNI FAN SL-Infinity 120 V1) it resets to factory on every boot. |
| `FanControl.LianLi-Lighting-v{{VERSION}}.zip` (Lighting) | You designed a look in L-Connect and want to keep it without running L-Connect. | Reads L-Connect's own saved config at startup and re-applies the look (Uni SL-Infinity only). With no L-Connect config present it leaves lighting untouched, exactly like the standard build. |

Each `.zip` contains a single DLL plus `LICENSE.txt` and `INSTALL.txt` at the root. The matching `.sha256` is that zip's SHA-256 checksum (optional integrity check).

## Install

1. Download the zip you picked above and extract the DLL (it sits at the zip root).
2. **Unblock the DLL** (important): right-click it -> Properties -> tick **Unblock** -> OK. Windows blocks files downloaded from the internet, and FanControl silently ignores a blocked plugin.
3. In FanControl, open the menu, click **Install plugin**, and pick the DLL. It loads immediately -- **no restart needed**.

Your Lian Li channels then appear as controls (assign each to a fan curve) and as RPM sensors.

## Do not run L-Connect at the same time

Lian Li's **L-Connect** drives the same USB controller as this plugin and writes to it aggressively, so the two fight each other. Uninstall L-Connect, or fully exit it and stop its background process (`L ConnectSystem.exe` in Task Manager) before using this plugin. Leaving it running is the most common cause of erratic fan speeds or lighting.

## Upgrading

Download the newer zip, unblock the DLL, and install it through FanControl the same way -- no restart needed. Your fan-curve bindings are preserved.

## What's changed
