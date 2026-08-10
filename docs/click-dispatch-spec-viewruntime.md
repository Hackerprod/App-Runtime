# Interaction visual feedback — ViewRuntime side (hold — send when ready)

Audience: the ViewRuntime session. **Not sent yet** — this session is
mid-way through the real AOSP measure/layout port; don't interrupt that
for this. Queued for when that work lands. Paired with
`docs\click-dispatch-spec-app-runtime.md` (the App Runtime half, which
needs nothing from this spec and is being implemented independently right
now — real click DISPATCH already works without any ViewRuntime change,
confirmed by investigation).

## Scope: pressed/hover visual state only — not dispatch

Click dispatch itself (identifying and invoking the correct guest
`onClick` method) is entirely an App Runtime-side concern and needs no
ViewRuntime involvement — already confirmed via `HitTest`, which already
works. **This spec is only about the VISUAL feedback a real Android app
shows during interaction** — the pressed-state color/ripple change on a
button while the mouse is down, and (a Windows-native addition beyond
real Android's touch model, but a reasonable one for a desktop host)
hover feedback while the mouse is over a clickable element.

## Real AOSP behavior to port (don't invent, verify against real source)

Real Android buttons/clickable views respond to state changes via
`StateListDrawable`/`RippleDrawable` — the `background` resolves to a
different visual (or animates a ripple) when the view's internal state
includes `android:state_pressed=true`. This project's existing drawable/
`ColorStateList` resolution (`FindDrawableColor` on the App Runtime side,
`resolve_drawable_solid` on this side) ALREADY has the machinery for
reading state-specific `<item>`s from a selector — it currently only ever
resolves the DEFAULT (stateless) entry. This unit extends that: when a
view is in a pressed state, resolve the `android:state_pressed=true` item
instead of the default, if one exists in the same drawable file already
being fetched.

## What's needed (proposed — confirm/adjust once you're back on this)

1. **A way for App Runtime to tell you a view's current interaction
   state** (pressed / hovered / neither) — a new, additive ABI function,
   e.g. `android_view_set_pressed(view, bool)` /
   `android_view_set_hovered(view, bool)` (or a combined state bitmask
   setter if that fits your internal model better — your call, propose
   the shape). App Runtime will call this on mouse-down/up (pressed) and
   mouse-move enter/exit (hovered) for whichever view `HitTest` currently
   reports, and re-request a frame render afterward (same
   `RenderFrame`/`Invalidate` pattern already used for content changes).
2. **Re-resolve background color per state on render**: when building the
   display list / painting a view whose background comes from a
   `ColorStateList`/selector drawable, check the view's current pressed/
   hovered flags and prefer a matching `android:state_pressed`/
   `android:state_hovered` item over the stateless default — reuses the
   existing drawable-parsing code from the Phase 2 bridge unit, just adds
   a state-aware lookup on top of the default-only lookup already there.
3. **Real Android has no `state_hovered` for touch devices** — this is a
   deliberate desktop-host addition. If a drawable file has no
   hover-specific item (the overwhelmingly common case, since real Android
   assets are touch-first), falling back to the default/pressed state is
   fine — don't fabricate a hover color that doesn't exist in the real
   asset.

## What NOT to do

- Don't build a general animated ripple effect (real Android's
  `RippleDrawable` is genuinely animated, expanding-circle visual) — a
  static color swap for the pressed state is a reasonable, honest bounded
  scope; note it as such if built this way.
- Don't touch click dispatch — that's confirmed to be entirely App
  Runtime's side, already being implemented independently.

## Validation plan (once picked up)

1. Full ctest suite green (same `cmake`/`ctest` path used throughout this
   project — see any recent report for the exact invocation).
2. A real interaction test: capture a frame while a button is
   artificially held "pressed" via the new setter, confirm its background
   resolves to the pressed-state color if the real drawable has one.

## Handoff

Report to the orchestrator (session `Android Runtime Workspare`) when
picked up — this file is the brief, no need to wait for a separate
message, but the orchestrator will confirm timing given the measure/layout
port in progress.
