# Modular capability system — spec

Orchestrator: Claude Code (session `Android Runtime Workspare`). Executor:
App Runtime side (opencode). Not implemented yet — send when ready.

## Confirmed gap (verified live, not assumed)

Ran `SKYNET-ApkInstaller-v1.0-debug.apk` without `--grant-power` with
`--trace` on. The real capability denial fires exactly as expected
(`SecurityException: Capability denied for power.lookup: PowerRead`), but
the trace file is **0 bytes** — nothing about the attempt is logged
anywhere structured. It only ever surfaces as an uncaught guest exception
on stderr. There is currently no way to answer "what capabilities does
this app try to use" other than reading raw crash text.

## Current state (read directly from source, not guessed)

- `Hosting\AndroidHostPorts.cs`: `AndroidCapability` enum has exactly 4
  values — `ClipboardRead`, `ClipboardWrite`, `NetworkState`, `PowerRead`.
  `IAndroidCapabilityPolicy.IsAllowed(AndroidCapabilityRequest)`,
  `AndroidCapabilityPolicy` (grants set + `DenyAll` default), gated by CLI
  flags (`--grant-clipboard-read`, `--grant-power`, etc.).
- `ApiLayer\AndroidSystemServices.cs`: the only consumer — calls
  `IsAllowed`, throws a guest `SecurityException` via `Denied(...)` on
  refusal. Nothing else touches the capability system.
- **Zero bindings exist for real network I/O, Bluetooth, Camera, or
  filesystem APIs** (verified: no `HttpURLConnection`/`Socket`/
  `BluetoothAdapter`/`Camera`/`FileInputStream`/`FileOutputStream`
  anywhere in `ApiLayer\`). An app attempting real network I/O today hits
  `AndroidApiNotImplementedException`, not a capability gate — the gate
  only covers the 4 narrow things above (clipboard, network *state*
  queries, power).

## Goal

1. A real modular capability taxonomy matching Android's actual
   dangerous-permission groups (not an invented one): Files, Bluetooth,
   Camera, Network (real I/O, distinct from the existing NetworkState
   query), Location, Microphone/Audio, SMS/Telephony — extend, don't
   replace, the existing 4.
2. Each domain is an independently toggleable module — its API bindings
   only register when the module is enabled — reusing the existing
   `IAndroidCapabilityPolicy`/`AndroidCapability` pattern, not inventing a
   parallel one.
3. **Every capability check produces a structured, queryable attempt
   record** (not just the raw `--trace` invocation log) — this is the
   actual gap found above. "App X requested capability Y at time Z,
   ALLOWED/DENIED" should be recorded even when nothing else about that
   invocation is logged, and survive/be inspectable even when the
   subsequent guest exception is uncaught and crashes the run.

## Design

### 1. Capability taxonomy — extend the existing enum

Add to `AndroidCapability` (don't rename/remove the existing 4):
`FileRead`, `FileWrite` (app-private storage first; scoped/shared storage
is real Android complexity, probe real APKs before assuming scope),
`BluetoothScan`, `BluetoothConnect`, `Camera`, `NetworkConnect` (real
socket/HTTP I/O, distinct from the existing `NetworkState`),
`LocationCoarse`, `LocationFine`, `Microphone`. Match real Android's own
permission names where there's a direct equivalent (e.g.
`ACCESS_FINE_LOCATION`) so the mapping is obvious and verifiable, not
invented.

### 2. Module registration — bindings register only when enabled

Define an `IAndroidCapabilityModule` (or similar) that pairs a set of
`AndroidCapability` values with a `Register(AndroidApiRegistryBuilder,
AndroidFrameworkState)` — the SAME registration function shape every
existing `*Bindings.cs` file already uses. A module's bindings are only
added to the registry when at least one of its capabilities could plausibly
be granted (or unconditionally registered but each individual binding
still calls `IsAllowed` before doing real work — investigate which fits
this codebase's existing registration-time-vs-call-time pattern better,
check how the existing 4-capability gate is wired into
`AndroidApiRegistryBuilder` construction before picking one approach).

### 3. Structured capability-attempt log (the actual gap)

Add an `IAndroidCapabilityAuditSink` (or extend the existing trace
sink/buffer if that fits better — investigate `AndroidApiTraceBuffer`/
`IAndroidApiTraceSink` first, this may already be the right extension
point rather than a new parallel system) that records one entry per
`IsAllowed` call: session id, package name, the exact `AndroidCapability`,
the requested operation, the timestamp, and the ALLOWED/DENIED result —
written/flushed independently of whether the subsequent guest exception is
caught, so a crash immediately after a denial doesn't lose the record (the
live test above showed the existing `--trace` file ends up empty on this
exact crash path — the audit sink must not have the same failure mode).
Expose it the same way `--trace` is exposed today (a CLI flag / accessible
buffer), and make sure a real run against `SKYNET-ApkInstaller-v1.0-debug.apk`
without `--grant-power` produces a non-empty audit record for the
`PowerRead` denial — that's the concrete acceptance test for this unit.

## What NOT to do

- Don't invent capability categories Android itself doesn't have — match
  real permission groups.
- Don't build real Bluetooth/Camera/File/Network I/O bindings speculatively
  in this unit — this spec is about the CAPABILITY/AUDIT infrastructure;
  actual API bindings for each domain are separate, future, probe-first
  units (same discipline as every API binding this session).
- Don't lose audit records on a crash — that's the exact bug this spec
  exists to fix, verify it explicitly.

## Validation plan

1. `dotnet build AndroidRuntime.sln -c Debug` — clean, 0 warnings.
2. `dotnet test` — full suite green.
3. `.\scripts\smoke-windows-host.ps1` — still green.
4. Real-APK proof: run `SKYNET-ApkInstaller-v1.0-debug.apk` WITHOUT
   `--grant-power`, with the new audit mechanism enabled — confirm a
   non-empty, structured record of the `PowerRead` denial exists
   afterward (this is the exact scenario verified broken above — must be
   fixed, not just theoretically designed).
5. FlexGrabber/MelyNails regression-unchanged.

## Handoff format expected back

Report via `agent_send` to the orchestrator (session `Android Runtime
Workspare`). Files touched + summary per file, test pass counts, the exact
audit-record output from step 4's real-APK proof, and the taxonomy/module
design decisions made with reasoning. Do not commit or push.
