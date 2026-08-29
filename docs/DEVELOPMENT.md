# Development

This page is the practical path for building, testing, and inspecting the repository.
The repository contains both the managed App Runtime and the C++ ViewRuntime native
core.

## Prerequisites

- .NET 10 SDK.
- A Windows SDK/desktop-capable .NET environment for the Windows host.
- CMake and a C++20 compiler for `Ui/ViewRuntime`.
- An Android SDK with build-tools and a JDK only when regenerating the APK fixtures.
- The Windows host project currently references an `AetherUI` checkout at
  `../../AetherUI` and consumes `viewruntime_core.dll` when that native artifact is
  present. A clean clone therefore needs those host-side dependencies before the
  Windows host project can be built.

## Managed runtime

Restore and run the portable core tests:

```powershell
dotnet restore .\tests\AndroidRuntime.Core.Tests\AndroidRuntime.Core.Tests.csproj
dotnet test .\tests\AndroidRuntime.Core.Tests\AndroidRuntime.Core.Tests.csproj
```

With the sibling AetherUI checkout and a ViewRuntime native build available, build
and test the Windows host:

```powershell
dotnet build .\AndroidRuntime.WindowsHost\AndroidRuntime.WindowsHost.csproj
dotnet test .\tests\AndroidRuntime.WindowsHost.Tests\AndroidRuntime.WindowsHost.Tests.csproj
```

Regenerate the repository's APK fixtures when Android SDK/JDK tooling is available:

```powershell
.\fixtures\RuntimeProbe\build.ps1
.\fixtures\UiProbe\build.ps1
```

## ViewRuntime native core

From `Ui/ViewRuntime`:

```powershell
cmake --preset dev
cmake --build --preset dev
ctest --test-dir build/dev -C Debug --output-on-failure
cmake --install build/dev --config Debug
```

The native README documents the C ABI, install tree, package-consumer test, and
platform-specific build alternatives.

## Windows host usage

Launch an APK directly:

```powershell
dotnet run --project .\AndroidRuntime.WindowsHost\AndroidRuntime.WindowsHost.csproj -- path\to\app.apk --auto-close-ms 5000 --trace artifacts\run.jsonl --capture-frame artifacts\run.bmp
```

The host also supports installer/launcher commands:

```text
--install <apk>
--launch <package>
--launch-file <path.apkr>
--list-installed
--uninstall <package>
--register-file-association
```

The positional APK mode accepts bounded automation and capability options:

```text
--auto-close-ms <100..600000>
--trace <path>
--capture-frame <path.bmp>
--capability-audit <path>
--grant-clipboard-read
--grant-clipboard-write
--grant-network-state
--grant-power
--grant-file-read
--grant-file-write
--grant-bluetooth-scan
--grant-bluetooth-connect
--grant-camera
--grant-network-connect
--grant-location-coarse
--grant-location-fine
--grant-microphone
```

Exit codes are `0` for a successful close, `1` for runtime/host failures, `2` for
invalid usage, and `3` for cancellation. Trace and capability-audit output paths
must not alias the input APK or each other.

The checked Windows smoke harness is:

```powershell
.\scripts\smoke-windows-host.ps1
```

It is intended for bounded host verification and parses the JSON-lines trace. The
host's `--capture-frame` option writes a top-down BGRA32 BMP for visual inspection.

## Repository map

| Path | Responsibility |
| --- | --- |
| `AndroidRuntime.Core.csproj` | Portable managed runtime. |
| `Apk/`, `Dex*.cs` | APK/resource/DEX parsing, verification, and interpretation. |
| `ApiLayer/` | Framework state, API registry, and binding implementations. |
| `Hosting/` | Lifecycle sessions, execution lane/GIL, capability policy, and installer support. |
| `Ui/` | App Runtime's Phase-2 resource/view bridge provider. |
| `Ui/ViewRuntime/` | C++20 Android view/layout/paint engine and C ABI. |
| `AndroidRuntime.WindowsHost/` | WPF/Win32 host, native bridge, input, and presentation. |
| `tests/`, `fixtures` | Focused tests and reproducible APK probes. |
| `docs/` | Specifications, architecture, compatibility, and implementation history. |

## Verification conventions

- Treat source and focused tests as authoritative for current behavior.
- Use real APK fixtures to validate lifecycle and API paths; do not infer broad
  compatibility from a single passing launch.
- Keep unsupported behavior explicit and fail closed.
- Keep visual claims tied to the ViewRuntime test suite or a host frame capture.
