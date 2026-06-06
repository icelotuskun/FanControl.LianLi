# .NET Consistency Rules

Consistency is non-negotiable. Every file must look like it was written by the same person.

## Before Writing Code

1. Find the closest existing example in the same layer (`Hid/`, `Protocol/`, `Devices/`, `Worker/`, `Logging/`, the plugin root).
2. Replicate its structure exactly -- file naming, type layout, constructor shape, member ordering, doc-comment style, test shape.
3. If no example exists in that layer, follow the rules in `.claude/rules/dotnet-architecture.md` and pick the structure that the rest of the assembly would have used.

"Find the closest existing example and replicate it exactly" is the governing philosophy. New code should be indistinguishable from the code already there. A new encoder looks like the existing encoders; a new test looks like the existing tests; a new worker method matches the existing worker methods. Inventing a second way to do a thing that already has one way is a bug.

## Checklist

Verify before marking work complete:

- [ ] File naming follows `{Concept}{Role}.cs` -- one primary type per file, type name matches file name.
- [ ] The file lives in the correct layer and depends only downward (`Plugin -> Worker -> Devices -> Hid -> Protocol -> Logging`).
- [ ] No HidSharp type appears outside `Hid/` -- the rest of the code knows only `IHidTransport` and byte buffers.
- [ ] Protocol encoders are pure: no I/O, no clock, no mutable state; same input yields the same bytes.
- [ ] Keepalive timing goes through the injected clock, never `DateTime.Now`/`DateTime.UtcNow`/`Stopwatch` read directly.
- [ ] Only the `IPlugin2` type is `public`; everything new is `internal`. Tests reach internals via `InternalsVisibleTo`, never by widening visibility.
- [ ] Nullable reference types are honored -- no `!` null-forgiving operator to silence a real warning; annotate and guard instead.
- [ ] The build is analyzer-clean under `TreatWarningsAsErrors` -- no warnings, no suppressions without an inline justification.
- [ ] Public members carry XML doc comments (`GenerateDocumentationFile` makes a missing one a build error).
- [ ] Constructor arguments are validated (`ArgumentNullException` / `ArgumentException`) and required deps are constructor parameters, not optional setters.
- [ ] `IDisposable` is implemented and disposal is correct wherever a HID handle, stream, or timer is owned; `Dispose` is idempotent and does not throw.
- [ ] No swallowed exceptions except the two sanctioned points (file logger, per-controller worker catch), and both log the failure.
- [ ] Exact protocol byte math is preserved and covered by a test that asserts the encoded buffer byte for byte.
- [ ] Logging goes through the file logger, with enough context to diagnose the fault (device, operation, exception).
- [ ] No discarded exceptions -- no empty `catch {}`; in tests, assert the outcome rather than swallowing.
- [ ] Tests run via the dotnet CLI (`dotnet test -c Release`, `dotnet format`) or `./build.ps1` for the whole gate.
- [ ] No planning artifacts in code -- no phase comments, stream references, or ticket IDs.
- [ ] Existing comments preserved -- only remove if demonstrably stale, wrong, or redundant with a rename.
- [ ] New conventions documented -- ordering, grouping, naming patterns annotated inline.
- [ ] A test file exists for every non-trivial type -- `{Type}.cs` has `{Type}Tests.cs`. New types without tests = incomplete change.
- [ ] No tombstone comments -- never `// X removed`, `// deleted Y`; git is the history.
- [ ] netstandard2.0 constraint honored -- no API unavailable on `netstandard2.0` in the plugin assembly, even though .NET 9 is installed.
