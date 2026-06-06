# Operational Awareness Rules

Plugin code changes do not exist in isolation. Every change must consider the host it runs inside, the contract it implements, and how it reaches a running FanControl install.

## Deployment Model

There is no deployment pipeline, no server, no container. This plugin is a single `netstandard2.0` DLL that FanControl loads at startup. "Deploying" means building `FanControl.LianLi.dll` in Release and copying that ONE file into FanControl's `Plugins/` directory, then restarting FanControl. Do NOT ship `HidSharp.dll`: FanControl ships HidSharp in its own install folder, it is not strong-named, and the plugin binds to the host's copy at runtime; a second copy in `Plugins/` is redundant. Do NOT ship `FanControl.Plugins.dll` either (host-provided). Crucially, FanControl loads plugins in its background service (`FanControl.Service`, running as LocalSystem), and that service only scans plugins at service start -- so a full restart of FanControl (which restarts the service) is required to load a new or changed plugin; reopening the window is not enough. The plugin therefore runs as SYSTEM, so its file log lands under the SYSTEM profile, not the user's. See `docs/deployment.md`. This is a manual, by-hand step -- never automated, never done by CI.

To verify a change works, build it and load the DLL into a real FanControl instance on Windows -- running the artifact is the verification, not just reading the source.

## Host Contract Awareness

The plugin's entire outward surface is the FanControl plugin contract:

- The only public type is the `IPlugin2` implementation. FanControl reflects over the DLL, finds that type, and calls `Initialize`, `Load`, `Update`, and `Close` on its own schedule and thread. Everything else in the assembly is `internal`.
- `FanControl.Plugins.dll` is the host-provided contract assembly. It is a **reference** -- the host supplies it at runtime; do not ship your own copy and do not take a version dependency that the installed FanControl cannot satisfy.
- The plugin targets `netstandard2.0` specifically so it loads into whatever .NET Framework / .NET runtime the host process uses. Do not raise the target framework, and do not use an API that is unavailable on `netstandard2.0`, even if the local SDK offers it.

A change that compiles here but throws `MissingMethodException` or `TypeLoadException` inside the host is broken. When you touch anything on the host seam, reason about what the host actually loads.

## Co-Changes

When a change alters how the plugin presents itself to the host -- sensor names, control identifiers, the set of exposed fan channels -- treat the user-visible naming and the documentation as part of the same changeset. A control whose identifier changes will orphan the user's existing FanControl curve bindings, so:

- Keep externally-visible identifiers stable unless there is a deliberate, documented reason to change them.
- When an identifier must change, say so explicitly in the change description, because the user will have to re-bind curves.

## Breaking Change Discipline

A "breaking change" here is anything that changes the contract the host or the user depends on:

- A change to the public `IPlugin2` surface or its behavior contract.
- A rename or removal of an exposed sensor/control identifier.
- A change to the keepalive cadence or device protocol that alters observable device behavior.
- Raising the target framework or a host-facing dependency version.

Make these deliberately and document them. There is no expand-contract migration dance here because there is no live fleet -- this is a plugin people install by hand -- but a user's existing FanControl configuration IS live state. Do not silently break the bindings they already have.

## Observability

This plugin runs headless inside the host, so the only observability you control is its file logger. When you add or change a failure mode:

- Make sure it is logged through the file logger with enough context to diagnose the fault after the fact (which device, which operation, the exact exception).
- The file logger is one of the two sanctioned swallow points (see `dotnet-architecture.md`): it must never throw back into the host, but it must record the failure, never discard it silently.
- The per-controller worker resilience catch is the other sanctioned swallow point: a fault on one controller must be logged and isolated so the other controllers keep running.

A failure mode that produces no log line is a failure mode you cannot operate. If you add a code path that can fail, make sure it leaves a trace.
