# Installer + launcher model (planned, not implemented)

Status: design captured for future implementation. Not scheduled — pick up
once the engine's API-binding boundary work is more mature. Current
validation loop (running the WindowsHost directly against a `.apk` path)
stays as-is until this is actually built; do not switch mid-stream.

## Why (user rationale)

Opening a raw `.apk` file directly on every launch is fragile long-term:
some apps write/download data at runtime, and real Android never runs an
app "from the APK" either — it installs (unpacks + registers) first, then
always launches from the installed, writable state. Matching that model:

1. A UI lists installed apps (a minimal package-manager view): open,
   uninstall.
2. Installing a `.apk` processes it once and unpacks it into
   `%LocalAppData%\AndroidRuntime\Apps\<package.name>` — mirrors real
   Android's per-app data-directory isolation (`/data/data/<package>`).
   **Not** `%ProgramFiles%`: that requires UAC elevation for every install,
   which is unnecessary friction for what is otherwise a per-user,
   no-admin-needed operation.
3. The runtime always launches from that installed directory, never
   directly from a `.apk` file — matches real Android's actual model and
   sidesteps whatever fragility comes from re-parsing/re-extracting a zip
   on every single launch.
4. A per-installed-app launcher file is written to the desktop (or
   wherever the user chooses) with a custom extension (`.apkr`),
   associated with the runtime via a shell file-type registration.

## `.apkr` format decision: plain text, not a Windows `.lnk` shortcut

Considered a standard `.lnk` shortcut (`AndroidRuntime.WindowsHost.exe
--launch <package>`) as the simpler option — Explorer already understands
`.lnk` natively (icon, right-click, pin to taskbar) with zero custom
parsing code. Decided against it: the actual goal here is a portable,
**human-readable** pointer carrying custom metadata (installed path, icon
reference, display name) that isn't naturally a `.lnk`'s job, and creating
a `.lnk` from .NET requires COM interop (`IShellLink`) — one more native
dependency this project has otherwise avoided throughout (real, direct DEX
interpretation and hand-written API bindings, no external shell/COM
tooling). A small custom text format is simpler to write, simpler to
debug, and matches this project's transparency-first ethos (every `.apkr`
file is human-inspectable, same spirit as the trace JSONL format already
used for API invocations).

Note: a plain `.lnk` is **also** already relocatable — Windows shortcuts
store an absolute target path and keep working when copied elsewhere. That
specific property doesn't differentiate the two options; the real
differentiators are custom-metadata ownership and avoiding a COM
dependency, both of which favor `.apkr`.

### Proposed `.apkr` content (draft, not final)

Plain UTF-8 text, simple `key=value` lines (easy to hand-write/inspect,
consistent with this project's preference for simple, verifiable formats
over binary ones):

```
AndroidRuntimeLauncher=1
Package=com.skynet.flexgrabber
DisplayName=FlexGrabber
InstalledPath=%LocalAppData%\AndroidRuntime\Apps\com.skynet.flexgrabber
IconPath=%LocalAppData%\AndroidRuntime\Apps\com.skynet.flexgrabber\icon.ico
```

The runtime registers `.apkr` -> `AndroidRuntime.WindowsHost.exe --launch-file "%1"`
(HKCU-scoped registration, no admin needed, same no-elevation principle as
the install location).

## Open questions for whenever this is picked up

- Uninstall UX: removing the installed-apps directory is easy; also
  offering "Apps & Features" visibility needs a separate
  `HKCU\...\Uninstall` registry entry — real Windows convention, not
  automatic from either the install directory or the `.apkr` file.
- Icon extraction: real Android app icons come from the APK's resource
  table (adaptive icon / mipmap entries) — needs a real decode path from
  `AndroidResourceResolver`, not a placeholder.
- Multi-APK/split-APK installs and update-in-place (re-installing over an
  existing `<package>` directory) are unaddressed here — scope for a
  future revision of this doc, not blocking the first cut.
