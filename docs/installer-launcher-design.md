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

## Display: default resolution, rotation, fullscreen (planned)

The WindowsHost window should default to a real, standard phone aspect
ratio and resolution — never a square/arbitrary debug window — and report
that exact resolution consistently to guest code everywhere it's queryable
(`Configuration.screenWidthDp`/`screenHeightDp`/`densityDpi`,
`Resources.getDisplayMetrics()` / `DisplayMetrics.widthPixels`/
`heightPixels`/`density`, `WindowManager.getDefaultDisplay().getSize()`,
`View.getWidth()`/`getHeight()` for the root/decor view). One source of
truth for all of these, not independently hardcoded per binding.

**Partially implemented (visual-fidelity unit)**: `WpfActivityWindowFactory`
now opens the default window phone-shaped at **360x732 DIP** — the exact
dp size of the verified reference device (1080x2196 at 3x density, aspect
0.4918) — instead of the previous arbitrary 800x600 landscape debug
window. The captured frame confirms portrait shape and horizontal
centering (`android:gravity="center"` on the root layout).

**Existing baseline to reconcile**: `AndroidApiBindings.cs`'s
`RegisterConfiguration` still hardcodes `screenWidthDp=360`,
`screenHeightDp=640`, `smallestScreenWidthDp=360`, `densityDpi=320`
(xhdpi) — that's a fixed **720x1280px** physical baseline, which does NOT
match the 360x732dp window shape above. When the full Display feature is
built, either update those constants to match the chosen real default
(360x732dp at 3x for the reference device) or make them derive from the
same single display-state source this feature introduces — don't leave
two independently-hardcoded "default resolution" facts in the codebase.

**Default resolution — needs real verification before locking in**: don't
pick a number from memory/guess. Check current Android device
distribution data (Android Studio's device-catalog reference or a current
usage-share source) for the actual most-common resolution/density/aspect
ratio at implementation time — device trends shift, and a value chosen
now could be stale by the time this is built. A safe, broadly-compatible
starting candidate to verify against real data: **1080x1920 (FHD, 16:9,
xxhdpi/480dpi)** — a long-standing common baseline many apps already
render correctly against — but confirm rather than assume, and note
today's typical flagship default may already have shifted toward taller
aspect ratios (~19.5:9-20:9, e.g. 1080x2400). The 360x732 window shape
above already matches the taller modern trend; the config constants are
the remaining reconciliation.

**Settings-configurable** (reuse the already-built `SharedPreferences`
binding's in-memory store, or a small dedicated host-settings store, for
persistence across the session — check which fits better when this is
built):
- Resolution: a picker among common real device presets (mirrors Android
  Studio AVD's device list — e.g. a small curated set: "Compact/HD",
  "Standard/FHD", "Tall/FHD+", "Tablet") rather than only free-form
  width/height entry, so testing against a few real, representative shapes
  is one click away.
- Density/DPI override independent of resolution — real Android exposes
  this as developer options ("Display size"/font scale); useful for
  testing how a guest app's layout responds to different density buckets
  without needing a different physical resolution.
- Rotation: portrait/landscape toggle, changes `Configuration.orientation`
  and swaps width/height consistently everywhere queried above — explicitly
  called out by the user as needed for future game compatibility (many
  games lock `landscape`/`sensorLandscape` in the manifest and must be
  respected/orientable).
- Fullscreen: available in both portrait and landscape, toggle from
  windowed to borderless/exclusive fullscreen without restarting the guest
  session.

## Other host-level features worth planning for (not committed, just captured)

- **Refresh rate**: `Display.getRefreshRate()`-style queries need a stable
  default (60Hz is the safe universal baseline); a future high-refresh-rate
  toggle (90/120Hz) matters once frame-timing-sensitive apps/games are in
  scope, low priority until then.
- **Status bar / navigation bar presence toggle**: some apps behave
  differently around `WindowInsets`/edge-to-edge depending on whether
  system bars are simulated as present — worth a togglable simulated
  system-bar inset rather than always-absent or always-present.
- **Display cutout / safe-area simulation**: real modern phones report a
  notch/punch-hole via `DisplayCutout`; a togglable simulated cutout would
  help testing apps that specifically handle (or mishandle) it — genuinely
  optional, low priority.
- **Window resizing behavior**: decide whether the host window is
  freely resizable by dragging (and if so, whether guest content scales or
  letterboxes) versus fixed-size-per-preset-only (closer to how a real
  phone actually behaves, arguably more honest for compatibility testing).
  Worth deciding deliberately rather than defaulting to "whatever WPF does
  by default."
- **Screenshot capture**: a simple "save the current guest window content
  to a PNG" action is a common, low-cost, high-value emulator/runtime
  feature — worth considering once the display model above is solid.

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
