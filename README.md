# App Runtime

App Runtime is an experimental, bounded Android compatibility runtime for Windows.
It loads real APKs, parses Android package formats, executes selected DEX bytecode
directly in a portable .NET core, and connects Android-style behavior and resources
to a native Android UI engine. The goal is to run meaningful application slices
without booting a complete Android operating system.

> **Status:** Active research and development. This is not a production-complete
> Android implementation and does not claim CTS compatibility.

## What App Runtime is

App Runtime focuses on the application boundary:

- load an APK and its binary manifest, resources, and DEX files;
- resolve the launcher Activity and run a bounded Android lifecycle;
- interpret guest DEX code through an exact, observable API registry;
- provide per-session Android state, tracing, cancellation, and host capability gates;
- hand Android UI data and behavior to the repository's native ViewRuntime component;
- host the resulting session in a Windows WPF/Win32 environment.

The current repository includes reproducible APK fixtures, focused managed tests,
a C++20 ViewRuntime core, and a Windows host integration.

## Why this is different from a full Android VM

App Runtime is intentionally **not** an Android device image, emulator, or virtual
machine. It does not boot a Linux kernel, Android userspace, Binder graph, or the
complete framework/runtime stack.

| Full Android environment | App Runtime |
| --- | --- |
| Boots a broad Android OS/runtime stack. | Loads an APK into a bounded managed runtime. |
| Provides system services inside the guest OS. | Uses explicit host ports and capability policy. |
| Targets broad application compatibility. | Targets verified application slices and fails closed at gaps. |
| Uses the platform's full rendering/window stack. | Uses the ViewRuntime native UI boundary and a Windows host adapter. |

This trades breadth for inspectability, deterministic tests, and a small, explicit
boundary. It is a compatibility runtime, not a shortcut to complete Android
emulation.

## Architecture at a glance

```text
APK
 │
 ├── Manifest / AXML / resources.arsc
 └── classes.dex (+ secondary DEX files)
          │
          ▼
App Runtime Core (.NET 10)
  DEX interpreter + API bindings + lifecycle/session state
          │
          ├── resource and behavior bridge
          ▼
ViewRuntime (C++20)
  Android view tree + measure/layout + hit testing + retained paint
          │
          ▼
Windows host
  WPF/Win32 window + viewruntime_core.dll + BGRA frame presentation
```

App Runtime is the resource-and-behavior provider on the current Phase-2 UI path.
ViewRuntime owns the native view hierarchy, measure/layout, style/state behavior,
hit testing, and paint recording. See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for
the ownership model and [Ui/ViewRuntime/README.md](Ui/ViewRuntime/README.md) for
the native component's ABI and test scope.

## Capabilities today

The following capabilities are present in the repository and backed by source/tests
within a bounded scope:

- **APK and DEX execution:** binary manifest/AXML and resource-table parsing,
  multidex loading, bounded verification, interpretation, and launcher Activity
  lifecycle execution.
- **Android/Java compatibility surface:** exact registry identity, guest exceptions,
  core language/library bindings, selected Android framework APIs, and Kotlin
  helper bindings. The detailed inventory and omissions are in
  [API_COMPATIBILITY.md](docs/API_COMPATIBILITY.md).
- **Session runtime:** isolated hosted sessions, execution-lane scheduling, GIL
  serialization, cancellation, quotas, structured API tracing, and fail-closed
  host failures.
- **Governed host ports:** selected clipboard, connectivity, power, app-private file,
  and microphone/audio flows with explicit capability policy and audit support.
- **Native Android UI path:** ViewRuntime's C++20 view/layout/paint pipeline, the
  App Runtime resource callbacks, native input/click forwarding, Toast state, and
  Windows frame presentation are wired in the repository. The native component's
  own compatibility scope remains bounded; see its README.
- **Windows packaging flow:** installer/launcher commands and `.apkr` launcher files
  are implemented in the Windows host.

These bullets describe repository-backed behavior, not a claim that arbitrary APKs
will run.

## Compatibility status and limits

Implemented today is intentionally narrower than “Android support.” The runtime has
verified paths through selected Activities and fixtures, but it does not provide:

- a complete Android framework or complete Java/Kotlin standard library;
- full Android permissions, Binder, JNI, app-process isolation, or OS services;
- complete task/back-stack semantics, configuration changes, or app data behavior;
- complete resources, themes, fonts, accessibility, or widget coverage;
- broad graphics API coverage or a mobile GPU backend;
- Compose, OpenGL, Vulkan, Xamarin, or Unity integration.

Unsupported API calls are observable and fail closed. Sensitive host operations are
denied unless explicitly granted, and unavailable adapters are not fabricated.

## Roadmap

### Implemented

- Bounded APK/AXML/ARSC/DEX loading and verification.
- Guest Activity lifecycle execution through the currently supported boundary.
- Exact API registry, selected Java/Android/Kotlin bindings, guest exceptions, and
  structured tracing.
- Per-session isolation, cancellation, capability policy/audit, and Windows host
  lifecycle.
- Phase-2 App Runtime ↔ ViewRuntime bridge and the repository's native Android UI
  measure/layout/paint/input path.
- Reproducible fixture and focused test suites.

### In development

- Extend compatibility from real APK evidence while preserving explicit API
  identity, focused tests, and fail-closed omissions.
- Continue hardening the managed/native bridge and host packaging around clean,
  reproducible builds.
- Expand the set of host adapters only where the security and ownership boundary is
  explicit.

### Planned

- Broader Android framework/resource coverage, including richer lifecycle,
  configuration, persistence, and service behavior.
- Custom resource/font resolution and additional Android UI/widget contracts.
- A more complete capability-backed host surface without weakening default-deny
  behavior.

### Research

- Evaluate an Android framework execution mode based on an AOSP/android-all-derived
  framework converted to DEX. This is a future research direction: the current
  repository contains no converted framework artifact, framework DEX loader, or
  claim of complete framework execution.
- Explore additional presentation backends only after the ViewRuntime ABI boundary
  is stable. The current verified visual path is the ViewRuntime native C ABI and
  Windows BGRA/child-HWND presentation; this does not imply OpenGL or Vulkan support.

## Build and run

See [DEVELOPMENT.md](docs/DEVELOPMENT.md) for prerequisites, managed/native build
commands, fixture generation, host usage, CLI options, and verification.

The shortest managed test path is:

```powershell
dotnet restore .\tests\AndroidRuntime.Core.Tests\AndroidRuntime.Core.Tests.csproj
dotnet test .\tests\AndroidRuntime.Core.Tests\AndroidRuntime.Core.Tests.csproj
```

The Windows host additionally needs the sibling AetherUI checkout referenced by
its project file and a built/installed ViewRuntime native library.

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — ownership boundaries and data flow.
- [API compatibility](docs/API_COMPATIBILITY.md) — detailed methods, descriptors,
  tested scope, and deliberate omissions.
- [Development](docs/DEVELOPMENT.md) — build, test, fixture, CLI, and smoke workflows.
- [Implementation notes](docs/IMPLEMENTATION_NOTES.md) — migrated work-unit history,
  hardening details, and rollback boundaries.
- [ViewRuntime native core](Ui/ViewRuntime/README.md) — C++20 UI engine, C ABI,
  install contract, and native tests.
- [Existing specifications](docs/) — focused design/specification documents for
  ViewRuntime integration, Phase 2 delegation, installer/launcher behavior, click
  dispatch, and capabilities.

## Project status

The repository is deliberately evidence-driven: current source, focused tests, and
real fixture runs define what is supported. If a behavior is not documented as
implemented and verified, treat it as an open compatibility boundary.
