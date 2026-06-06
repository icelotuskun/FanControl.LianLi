# Deployment

How the built plugin actually reaches a running FanControl, and the non-obvious
things that bite if you skip them.

## What you ship: one DLL (per variant)

The deployable artifact is a single file: `FanControl.LianLi.dll` (standard) or
`FanControl.LianLi.Argb.dll` (the ARGB variant -- see [protocol.md](protocol.md)).
A user installs one or the other, never both. You do NOT ship anything else:

- `HidSharp.dll` -- FanControl already ships HidSharp in its own install folder
  (it uses HidSharp for its built-in HID controllers). HidSharp is not
  strong-named, so the plugin binds by simple name to whatever 2.6.x the host has
  loaded. Shipping a second copy is pointless (the host's already-loaded one
  wins) and just clutters the Plugins folder.
- `FanControl.Plugins.dll` -- the host contract assembly, always provided by the
  host. Never ship it.

Install: use FanControl's **Install plugin** button, or copy the DLL into
`<FanControl>\Plugins\`.

## Plugins are loaded by the FanControl service

Modern FanControl (4.x) splits hardware access into a background Windows service,
`FanControl.Service` (running as `LocalSystem`). **Plugins are loaded and run by
that service, not by `FanControl.exe`.** Consequences:

- The service scans `Plugins\*.dll` (files whose name starts with `FanControl`).
  The normal way to get a plugin loaded is FanControl's **Install plugin** button
  (it copies the DLL into place and loads it with no restart), or to drop the DLL
  in the `Plugins` folder and **restart FanControl**, which reloads plugins.
  (Note: the `FanControl.Service` process itself keeps running across an app
  restart -- the reload is driven by the app, not by the service process
  bouncing -- so do not assume the service must be manually restarted.)
- The plugin runs as `LocalSystem`. Anything path-relative (for example the file
  logger, which writes to `%LOCALAPPDATA%\FanControl.LianLi\plugin.log`) resolves
  under the SYSTEM profile
  (`C:\Windows\System32\config\systemprofile\AppData\Local\...`), not your user
  profile. Look there when diagnosing.
- FanControl's own error log (`<FanControl>\service_log.txt`) is written by the
  service (SYSTEM), so it captures plugin load/Initialize failures. The main
  app's `log.txt` lives in the same Program Files folder and is not writable by a
  non-elevated `FanControl.exe`, so do not rely on it.

## Updating the plugin file

The service holds the plugin DLL loaded (and therefore locked) while it runs, so
overwriting it while FanControl is up can fail. Close FanControl, replace the
file, then start FanControl again so the service reloads it. If the copy still
fails because the file is locked, stop `FanControl.Service` (elevated) first. The
DLL name is stable across versions, so an upgrade is a same-name swap; switching
between the standard and ARGB builds is the same swap, and because the sensor
identifiers are identical the user's existing curve bindings survive it.

## Verifying a deployment

After reloading the plugin, `plugin.log` under the SYSTEM profile should show
`Initialize: standard build, scan located N Lian Li controller(s)` (or `ARGB
build`) followed by `Set C{i}:{ch} = N%` lines (FanControl driving the curves)
and, every 15s, the same lines tagged `(refresh)` -- that is the keepalive
re-assert. No `poll err` lines means RPM telemetry is reading cleanly. Make sure
Lian Li L-Connect is not running while you test: it drives the same controller
and will fight the plugin.
