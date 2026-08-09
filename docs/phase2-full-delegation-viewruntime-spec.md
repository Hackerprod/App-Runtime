# Phase 2: full visual delegation — ViewRuntime side (own everything visual)

Audience: the ViewRuntime project (native, separate session/agent). This
supersedes Phase 1 (`docs\viewruntime-integration-spec.md`, the
render-backend-only integration where App Runtime kept its own C# view
hierarchy and only handed ViewRuntime a pre-built display list of
fill-rect/draw-text commands to paint). **That model is being replaced.**
A companion spec (`docs\phase2-full-delegation-app-runtime-spec.md`) directs
App Runtime's session to remove its entire C# view-hierarchy/measure/
layout/paint implementation — every hardcoded color, every partial
attribute reader, every bit of visual behavior. **ViewRuntime now owns
100% of Android view/widget behavior: the view hierarchy, measure, layout,
style/theme resolution as applied to views, drawable/color rendering,
paint, and hit-testing.** App Runtime becomes a resource-and-behavior
PROVIDER that this side queries — it does not decide how anything looks.

## Why this split (matches real AOSP's own package boundaries)

Real Android separates `android.content.res` (`Resources`/`AssetManager`/
`Theme` — reading the ARSC/AXML resource *format* as raw structured data)
from `android.view`/`android.widget` (`View`/`ViewGroup`/`LinearLayout`/
`TextView` — what a view IS and how it measures/lays out/paints, including
applying style/theme chains to actual view properties). App Runtime keeps
the former (it already has real, tested ARSC table parsing and binary AXML
parsing — that's genuine format-parsing work, not visual behavior).
**ViewRuntime now owns the latter entirely**, matching the same discipline
already proven on the ImageView port (real AOSP source reverse-engineered,
not approximated) — extend that same rigor to every other view type and
concern (LinearLayout weight, margins, padding, gravity's full value set,
style/theme resolution, real drawable rendering).

## What ViewRuntime needs to receive from App Runtime (the bridge)

The exact ABI is a joint decision — App Runtime's session is proposing a
concrete shape and will check in before finalizing it; treat the following
as the capability list to design against, not a fixed contract yet:

1. **Inflate input**: a layout's parsed element tree — element names,
   namespaced attributes with RAW values (references, literals, whatever
   the AXML actually declared, unresolved). App Runtime's binary-AXML
   parser stays on their side (real format-parsing, already built/tested);
   the working assumption is they hand you a generic serialized tree
   rather than raw AXML bytes, but confirm this once their proposal
   arrives — don't assume.
2. **Resource resolution, on demand (you call back into App Runtime)**:
   resolve a resource reference to its typed raw value (color/dimension/
   string/drawable-reference/boolean/integer/array), walk a style's parent
   chain and get its raw attribute bag, resolve a theme attribute
   (`?attr/colorAccent` etc.) through the app's actual theme, fetch raw
   file bytes for an image/font resource path. **You own deciding what a
   resolved value MEANS for view behavior (which attribute wins in a style
   chain, what a ColorStateList's default state resolves to, how a shape
   drawable's solid fill becomes a background) — App Runtime only hands
   you the raw parsed data, doesn't interpret it for you.**
3. **Frame/session lifecycle**: told when to build/paint a frame (Activity
   lifecycle-driven, same triggers as before); you hand back a finished
   pixel buffer (the existing Phase-1 `viewruntime_surface_pixels`-style
   handoff can likely stay close to as-is for this part — presentation
   plumbing didn't cause any of the bugs found, view/layout/style logic
   did).
4. **Hit-testing**: you own it now (you own the real view bounds/hierarchy)
   — report back which view (by id) a point hit; App Runtime dispatches the
   actual guest bytecode click listener itself (that's DEX execution,
   stays their side).
5. **API-binding queries**: real Android API calls like `View.getWidth()`/
   `TypedArray.getColor()`/`View.setText()`/etc. that guest bytecode makes
   need real answers from your actual view state — expect queries for
   "what did you measure this view's size as," "what's this view's current
   text," etc., not just one-way inflate-and-paint.

## What you must implement or extend for real compatibility

Per the visual-parity investigation findings (validated against
`.tmp\Apk-Installer.jpg`, a real device screenshot, and confirmed as
GENERAL pipeline gaps, not one-app quirks):

1. **Real style/theme chain resolution** — walk `style="..."` and its
   parent chain, resolve `?attr/` theme references, apply resolved
   background/textColor/textAppearance to real view properties. The
   found example (SKYNET's CONNECT button: `Widget.AppCompat.Button.Colored`
   → `Base.Widget.AppCompat.Button.Colored` → `?attr/colorAccent` →
   app's `accent_material_light`/theme override) is representative of the
   real complexity — implement the general mechanism (AOSP's actual
   `Resources.Theme`/style-resolution algorithm), not a per-app shortcut.
2. **Real drawable rendering** — at minimum solid-fill shape drawables and
   ColorStateList resolution (default/first-state color); investigate how
   far to go for full compatibility (gradients, selectors with multiple
   states, corner radius/stroke) based on real APK prevalence, your call
   on how to scope it, but the mechanism should be real AOSP-equivalent
   logic, not hardcoded per-case values.
3. **LinearLayout `layout_weight`** — real proportional space distribution,
   not currently implemented anywhere (confirmed: zero references in the
   prior C# implementation, which is being deleted).
4. **Margins and padding, per-edge, on every view type** — `layout_margin*`
   (start/end/top/bottom/left/right + the shorthand) and `padding*`
   equivalents, real AOSP `ViewGroup.MarginLayoutParams`-equivalent
   handling, not just LinearLayout's single generic `padding` value from
   before.
5. **Full `gravity`/`layout_gravity` value set** — `START`/`END`/`TOP`/
   `BOTTOM`/`CENTER`/`CENTER_HORIZONTAL`/`CENTER_VERTICAL`/`FILL`/
   `FILL_HORIZONTAL`/`FILL_VERTICAL`/`CLIP_HORIZONTAL`/`CLIP_VERTICAL`, and
   the real distinction between a container's `gravity` (affects children)
   vs a child's own `layout_gravity` (affects itself within its parent) —
   only 2 of these bits existed in the deleted C# implementation.
6. **Density applied exactly once** — the deleted C# implementation had a
   confirmed real bug (dp→px conversion happening twice across
   measure/layout and render), making every dimension roughly 2x too
   large. Design your own pipeline so density conversion has one clear,
   single point of application — worth being deliberate about this given
   it was the exact class of bug that caused it last time.
7. **A real resolved background fill, always** — every window/root view
   needs its real resolved background (theme's `windowBackground` or the
   view's own resolved background) painted, not left as whatever the
   surface buffer happens to default to (the prior integration had exactly
   this bug: the native path never painted a background before drawing
   content, so unpainted areas showed as solid black instead of the app's
   real background color).
8. **`ImageView.src`/`imageResource`** — real bitmap resolution and
   drawing (the `draw_image`/`surface_set_image` ABI additions already
   made are the presentation half of this; confirm the full pipeline from
   resource-query → decode → draw is real end-to-end, not just the ABI
   plumbing).

## What NOT to do

- Don't hardcode ANY fallback visual value "just to make something render"
  — if a real resolution path is genuinely unavailable (e.g. an
  unmodeled framework theme chain bottoms out), use real documented AOSP
  framework defaults (actual values from the real framework
  resources.arsc / styles.xml, looked up, not invented) rather than an
  arbitrary placeholder color/size.
- Don't build features speculatively beyond what real APKs on this
  project's validation set actually exercise — same probe-before-build
  discipline App Runtime's engine side has used throughout this project,
  applied here too.

## Coordination

Confirm the bridge ABI shape with App Runtime's session before either side
implements against a final version — this is explicitly a joint contract,
not something to lock in unilaterally. The project owner is relaying both
specs directly; loop back through them if the two sides' assumptions
don't line up on the first pass.
