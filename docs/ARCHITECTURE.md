# Architecture

App Runtime is split into a portable managed runtime, an Android-facing resource and
behavior layer, a native Android UI engine, and a Windows host. The boundary is
deliberate: the managed runtime executes guest code and exposes Android contracts,
while the native UI component owns visual behavior.

## End-to-end flow

```text
APK
 ├─ AndroidManifest.xml / binary AXML
 ├─ resources.arsc and packaged resources
 └─ classes.dex / classesN.dex
        │
        ▼
AndroidRuntime.Core (net10.0)
 ├─ APK, AXML, ARSC, and DEX loading
 ├─ bounded DEX verification and interpretation
 ├─ exact Android/Java/Kotlin API registry and bindings
 └─ per-session lifecycle, execution lane, tracing, and host ports
        │
        ├─ AndroidViewBridge
        │  ├─ serialized AXML nodes with raw typed values
        │  └─ resource callbacks for values, styles, and files
        ▼
ViewRuntime native core (C++20)
 ├─ Android view tree and LayoutParams
 ├─ measure/layout and Android-style widget behavior
 ├─ style/state resolution, hit testing, and retained paint commands
 └─ C ABI exposed under Ui/ViewRuntime/include/
        │
        ▼
AndroidRuntime.WindowsHost (net10.0-windows)
 ├─ WPF/Win32 window and input adapters
 ├─ viewruntime_core.dll bridge
 └─ BGRA frame presentation through a child HWND/GDI path
```

## Ownership boundaries

| Area | Owner | Boundary |
| --- | --- | --- |
| APK formats | App Runtime | Reads ZIP entries, binary manifest/AXML, resources.arsc, and one or more DEX files. |
| Guest execution | App Runtime | Verifies and interprets the bounded DEX surface, resolves guest/framework calls, and runs lifecycle code. |
| Android behavior | App Runtime | Provides lifecycle, API bindings, per-session peers, tracing, cancellation, and capability-gated host ports. |
| Resource data | App Runtime | Supplies raw resource/style/file data through the bridge; it does not decide how visual values are laid out or painted. |
| View system | ViewRuntime | Owns view objects, measure/layout, style application, hit testing, interaction state, and paint recording. |
| Windows integration | Windows host | Owns windows, dispatchers, input translation, native-library loading, frame scheduling, and pixel presentation. |

## Session and execution model

Each hosted launch gets its own execution lane, API registry snapshot, peer stores,
activity/window association, cancellation lifetime, and bounded trace buffer. Guest
threads are represented by CLR threads but guest bytecode is serialized by the
per-session `AndroidGil`. Blocking operations release the GIL while they wait.
This is a compatibility-oriented execution model, not a parallel guest VM.

Host access is injected through ports. The default capability policy denies sensitive
operations; explicit grants and audit records are required for the supported
capability surface. Missing adapters and unsupported operations fail closed.

## UI bridge contract

The current UI boundary is Phase 2:

1. App Runtime parses binary AXML and resource data.
2. App Runtime serializes nodes and preserves raw references and dimension units.
3. ViewRuntime inflates the native view tree and applies Android-side behavior.
4. ViewRuntime calls back for resource/style/file data when it needs them.
5. The Windows bridge forwards guest view mutations and input events by native view
   handle.
6. The host presents the native frame buffer.

There is no local C# view hierarchy or C# layout/paint implementation in the current
Phase-2 path. The native library is consumed through the C headers and P/Invoke
surface documented in [Ui/ViewRuntime/README.md](../Ui/ViewRuntime/README.md).

## Design constraints

- The core remains portable `net10.0`; WPF and Win32 types stay in the Windows host.
- Compatibility is bounded and evidence-driven. An API is added for a verified need,
  with focused tests and an explicit omission when the general contract is not yet
  modeled.
- Security-sensitive process escape and unrestricted host access are not inferred
  from Android method names.
- The repository's current visual path is the ViewRuntime C ABI plus Windows BGRA
  presentation. Compose, OpenGL, Vulkan, Xamarin, and Unity are not part of this
  implementation.
