# Real click dispatch — App Runtime side (self-contained, implement now)

Orchestrator: Claude Code (session `Android Runtime Workspare`). Executor:
App Runtime side (opencode). **This unit needs ZERO ViewRuntime ABI
changes** — investigated and confirmed below. Implement and validate fully
now; don't wait on the ViewRuntime-side spec (a separate, parallel unit for
hover/pressed visual feedback only).

## Why this is fully self-contained (verified, not assumed)

Read `AndroidRuntime.WindowsHost\ViewRuntimeAndroidViewBridge.cs` directly:
- `SetOnClickListener` already stores every programmatic
  `view.setOnClickListener(...)` call in a local `Dictionary<nint,
  DexObject?> _listeners` keyed by the native view handle — this is
  EXACTLY what's needed to dispatch a programmatic listener, no ViewRuntime
  query required.
- `PerformClick(DexObject view)` currently does nothing but
  `RequireAvailable(); throw NotWired();` — a placeholder, never
  implemented.
- `HitTest` already works end-to-end (confirmed: `AndroidHwndRenderSurface`
  already calls `session.ViewBridge.HitTest(...)` on mouse down/up and gets
  a real view id back from ViewRuntime).
- The only other real click style, `android:onClick="methodName"` (a
  layout XML attribute naming an Activity method directly, an older but
  still-used Android pattern — no `setOnClickListener` call at all), is
  visible to App Runtime's OWN AXML parsing (`Ui\AndroidInflateSerializer.cs`
  already walks and serializes every raw attribute across the bridge,
  including this one) — it's just not currently extracted/stored for local
  lookup. This is fixable entirely on this side too.

**Conclusion: click dispatch needs zero new ViewRuntime capability.**
`PerformClick`'s job is to look at what THIS side already has (or can
easily capture at inflate time) and invoke the right guest method — real
DEX execution, which has always been App Runtime's job.

## Important framing (per the project owner, don't lose this)

A button's real click handler may do ANYTHING inside guest bytecode once
invoked — open a dialog, switch to a different view/fragment, mutate
internal state, or call a real Android API (vibrate, play a sound, toggle
Bluetooth, make a network request, copy a file). **This unit's job is
ONLY the dispatch mechanism — correctly identifying and invoking whichever
guest method should run on a click.** What that guest method does
afterward is out of scope here entirely; if it calls a framework API this
runtime doesn't have a binding for yet, that surfaces as its own honest
`AndroidApiNotImplementedException` boundary, handled the same probe-first
way every other API gap has been this whole project — don't try to
anticipate or pre-build specific action bindings (Vibrator, MediaPlayer,
Bluetooth toggle, etc.) in this unit.

## What to implement

1. **`PerformClick(DexObject view)`**: look up `_listeners[native]`. If a
   non-null listener is registered, invoke its real `onClick(View)` method
   via the interpreter (reuse the exact same invocation pattern the OLD
   pre-Phase-2 `AndroidUiSession.PerformClick` used —
   `_interpreter.InvokeVirtualInstanceExact(listener, "onClick",
   "(Landroid/view/View;)V", guest)` — check git history/the deleted
   `Ui\AndroidUiSession.cs` for the exact reference implementation via `git
   show 7da58a2~1:Ui/AndroidUiSession.cs` or similar, don't reinvent the
   invocation mechanics). If no programmatic listener is registered, check
   for a captured XML `onClick` method name (see #2) and invoke that
   instead via `InvokePublicInstanceExact` on the Activity (same pattern
   the old code used). Return `true` if a real dispatch happened, `false`
   if nothing was registered — matching the real `View.performClick()`
   boolean-return contract (real Android: `true` means a listener
   consumed the click).
2. **Capture `android:onClick` at inflate time**: extend
   `Ui\AndroidInflateSerializer.cs` (or wherever the per-node attribute
   walk happens) to also extract the `onClick` attribute's string value
   (a plain method name, e.g. `android:onClick="onPlaySoundClick"`) into a
   lookup table keyed by whatever identity is available at that point
   (resource_id, or node index — check what's actually accessible and
   reliably maps to the eventual native view handle once `PerformClick`
   needs to look it up; if resource_id is 0/absent for some views,
   investigate whether node index survives round-trip through the native
   inflate call, or find another correlating key — don't guess, verify by
   testing against a real click).
3. **Null-safety / real Android semantics**: `performClick()` on a
   disabled or invisible view should not dispatch (matches real
   `View.performClick()`'s documented behavior — check the real contract
   rather than assuming; a disabled view intercepting clicks was already
   a pattern this project modeled pre-Phase-2, check the deleted
   `AndroidUiSession.PerformClick` for the exact `Enabled`/`Visibility`
   check it used to have, since that's now unavailable locally — this may
   need `IsEnabled`/`GetVisibility` to work, which currently ALSO throw
   `NotWired()`; if that's a blocker for correctness here, either treat it
   as accepted debt with a comment (dispatch happens even on
   disabled/invisible views for now) or coordinate a minimal ABI need with
   ViewRuntime — investigate and report which, don't silently skip the
   check without noting it).

## What NOT to do

- Don't build ANY specific action binding (Vibrator, MediaPlayer,
  BluetoothAdapter toggle, network I/O, file copy) in this unit — that's
  explicitly the next, separate, probe-first unit once dispatch itself
  works and we can see (via the project owner's own test APK,
  `.tmp\RuntimeApiLab-debug.apk`) which real APIs each button's listener
  actually calls.
- Don't touch mouse hover/pressed visual state — that's the separate
  ViewRuntime-side spec (`docs\click-dispatch-spec-viewruntime.md`),
  waiting on that session; don't start it here.
- Don't add any new P/Invoke declarations or touch
  `Ui\ViewRuntime\include\viewruntime\*.h` — this unit needs none.

## Validation plan

1. `dotnet build AndroidRuntime.sln -c Debug` — clean, 0 warnings.
2. `dotnet test` — full suite green (baseline: 461 Core + 22 WindowsHost).
3. `.\scripts\smoke-windows-host.ps1` — still green.
4. Real click test: run `.tmp\RuntimeApiLab-debug.apk` (the project
   owner's own test APK, built specifically with buttons for real
   actions), interact with it for real (this is a genuinely interactive
   GUI test — describe exactly how you validated, e.g. injected
   click events at known coordinates matching a real button's hit-tested
   bounds, or another concrete mechanism — not just "it builds"). Confirm
   at least one button's guest `onClick` actually fires (observable via
   `--trace` showing the guest method invocation, or the button's own
   internal logic producing an observable side effect like a state change
   reflected in a subsequent capture).
5. `SKYNET-ApkInstaller-v1.0-debug.apk`'s existing click-driven flows
   (if any were working pre-Phase-2) should now work again through the
   real bridge — sanity check if time allows, not blocking.

## Handoff format expected back

Report via `agent_send` to the orchestrator (session `Android Runtime
Workspare`). Files touched + summary, test pass counts, exact evidence a
real click dispatched to a real guest method, and what you found for the
XML-onClick lookup-key question. Do not commit or push.
