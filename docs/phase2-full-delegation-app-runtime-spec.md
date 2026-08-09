# Phase 2: full visual delegation — App Runtime side (remove ALL rendering)

Orchestrator: Claude Code (session `App Runtime`). Executor: opencode.
This supersedes the Phase 1 "render-backend only" model
(`docs\viewruntime-integration-spec.md`) and the incremental visual-fidelity
patches from boundary #64 and its follow-up. Those were the wrong shape:
patching individual visual bugs (colors, margins, gravity, density scale)
one at a time in App Runtime's own hand-rolled, incomplete View system
instead of removing that system and delegating to ViewRuntime's real
AOSP-ported engine. **Explicit project-owner directive: remove every
character of rendering/visual logic from this side. This side handles
functioning (DEX execution, APK/resource format parsing, API bindings) —
zero visual behavior. No fallback colors, no hardcoded defaults, no
partial attribute reading, no local measure/layout math.**

## The architecture split (why this boundary, precisely)

Real AOSP itself separates these concerns into different packages:
`android.content.res` (`Resources`, `AssetManager`, `Theme`,
`TypedArray` — reading/resolving the resource *format*: ARSC tables,
binary XML, style/theme chains as raw data) is a different layer from
`android.view`/`android.widget` (`View`, `ViewGroup`, `LinearLayout`,
`TextView`, measure/layout/draw — what a view IS and how it behaves
visually). This project already has real, working, tested APK/resource
**format** parsing (`Apk\AndroidResourceTable.cs`, `Apk\AndroidResourceResolver.cs`,
the binary AXML reader) — that stays. What must go is everything that
turns resolved resource data into visual behavior: view hierarchy,
measure, layout, paint, style *application* (not style *parsing* — the
raw ARSC bytes are still parsed here, but WHICH attribute wins in a
theme chain and how it renders is ViewRuntime's job).

**App Runtime becomes a resource-and-behavior PROVIDER that ViewRuntime
queries; it stops being a second, competing view-hierarchy implementation.**

## Remove entirely — no legacy left, no compatibility shims

1. **`Ui\AndroidSceneHost.cs`** — the ENTIRE file: `AndroidViewNode` and
   every subclass (`AndroidLinearLayoutNode`, `AndroidTextViewNode`,
   `AndroidButtonNode`, `AndroidImageViewNode`), all measure/layout/Record
   logic, `AndroidSceneHost` itself, `AndroidDisplayList`/`AndroidDrawCommand`/
   `AndroidFillRectCommand`/`AndroidDrawTextCommand`, `IAndroidRenderBackend`/
   `RecordingAndroidRenderBackend`, `IAndroidTextMeasurer`/
   `DeterministicAndroidTextMeasurer`, `AndroidUiContext`, `AndroidMeasureSpec`/
   `AndroidLayoutDimension`/`AndroidLayoutSize`/`AndroidOrientation`/
   `AndroidViewVisibility`/`AndroidGravity`-if-any. All of it — this is the
   entire duplicate view-hierarchy implementation.
2. **`Ui\AndroidLayoutInflater.cs`** — the visual-attribute-to-node mapping
   (background/textColor/textSize/gravity/padding/orientation reading and
   node construction) is removed. What may be REPURPOSED (not necessarily
   deleted, investigate first): the raw AXML element-tree walking itself,
   if it becomes the shape that gets serialized across to ViewRuntime — see
   "New bridge" below.
3. **`AndroidRuntime.WindowsHost\WindowsRetainedRenderer.cs`** — the ENTIRE
   hand-rolled rasterizer: `FillPixels`, `DrawPseudoText`, `RenderDib`'s
   command-interpretation loop, the fallback path, `BitmapInfoHeader`/GDI
   blit machinery gets REPLACED (not necessarily deleted wholesale — the
   actual GDI `StretchDIBits` presentation mechanism for getting
   ViewRuntime's finished pixel buffer onto the Win32 child HWND still needs
   to exist somewhere; what goes away is App Runtime INTERPRETING the
   display list itself — ViewRuntime now owns the entire frame, App Runtime
   just presents the buffer ViewRuntime hands back). Investigate the
   minimal presentation shim needed and keep ONLY that.
4. **`AndroidRuntime.WindowsHost\ViewRuntimeNative.cs`'s Phase-1-shaped
   P/Invoke surface** (`frame_begin`/`draw_fill_rect`/`draw_text`/`frame_end`/
   `measure_text` as individually-called-per-command functions) — replaced
   by whatever new ABI Phase 2 needs (see "New bridge" below); this file
   gets rewritten, not kept alongside the new one.
5. **The fake `TypedArray` facade** in `ApiLayer\AndroidApiBindings.cs`
   (`RegisterTypedArray` — `state.TypedArrayObject`, `getIndexCount()=>1`,
   `hasValue()=>1`, defensive-default reads) — this entire stub. Real
   `obtainStyledAttributes`/`TypedArray` behavior is real style/theme
   resolution, which is now ViewRuntime's job entirely; if a guest app calls
   these APIs, the binding should forward the request to ViewRuntime via
   the new bridge (see below) and return whatever ViewRuntime resolves, not
   a local fake.
6. **`AndroidButtonNode`'s hardcoded `BackgroundColor = new(255,224,224,224)`**,
   **`AndroidTextViewNode`'s hardcoded `TextColor = new(255,32,32,32)`**, and
   every other hardcoded visual default anywhere in this codebase — gone
   along with the classes they're on (point 1), but confirm none survive
   in whatever NEW code replaces them either. No fallback colors. If
   ViewRuntime can't resolve something, that's ViewRuntime's problem to
   solve with real AOSP-equivalent default logic (e.g. real Android's own
   documented framework style defaults), not a number picked here.
7. **`RegisterConfiguration`'s fixed `screenWidthDp=360`/`densityDpi=320`
   etc.** in `AndroidApiBindings.cs` — these must come from the SAME single
   source of truth as whatever ViewRuntime is actually rendering at
   (window size/density), not an independently-hardcoded set of numbers.
   Investigate how `Configuration`/`DisplayMetrics`/`WindowManager` queries
   should now be answered — likely by asking ViewRuntime (or the shared
   display-state the new bridge establishes) rather than a static constant
   table.

## New bridge — App Runtime as a resource/behavior provider (design, then implement)

This is the real design work of this unit — investigate and propose the
concrete shape, don't guess blindly, but land on something and build it
(unlike the smaller "stop and report" briefs, this unit is expected to
require real architectural decisions; make them, document the reasoning,
and implement — check in only if something is genuinely ambiguous enough
that a wrong guess would be expensive to undo).

Needed capabilities, App Runtime → ViewRuntime direction (what ViewRuntime
needs FROM App Runtime):
- **Inflate request**: given a layout resource id, ViewRuntime needs the
  parsed element tree (element names, namespaced attributes with their RAW
  values — references, literals, whatever the AXML actually contains,
  unresolved) to build its own real View objects from. Decide: does App
  Runtime hand across a generic serialized tree (e.g. a flat/nested
  struct-array via the ABI) built from the EXISTING AXML reader (which
  stays — it's format parsing), or does ViewRuntime get raw AXML bytes
  directly and parse the binary format itself? Recommend keeping AXML
  binary-format parsing in App Runtime (it's already real, tested, and is
  format-parsing not view-behavior) and handing across a generic resolved
  tree structure — but investigate what's actually practical across the
  C ABI boundary and decide.
- **Resource resolution queries** (ViewRuntime → App Runtime,
  call-back direction): resolve a resource reference to its raw typed
  value (color/dimension/string/drawable-reference/boolean/integer), walk
  a style's parent chain and return its raw attribute bag, resolve a theme
  attribute (`?attr/...`) through the current theme, fetch raw file bytes
  for a resource path (bitmap/font files) — all backed by the EXISTING
  `AndroidResourceResolver`/`AndroidResourceTable` (stays, becomes a
  query-answering service instead of something only App Runtime's own
  inflater consumes).
- **Frame lifecycle**: App Runtime still needs to know when to ask for a
  new frame (on Activity lifecycle/invalidation events, same triggers as
  today) and get back a finished pixel buffer to present via GDI — but
  ViewRuntime now owns measure/layout/style-resolution/paint entirely
  internally once it has the inflate request + resource-query access.
- **Hit-testing and click dispatch**: ViewRuntime does the hit-test
  (it owns the real view bounds/hierarchy now); the RESULT (which view id
  was hit) comes back to App Runtime, which still owns invoking the real
  guest bytecode click listener via `DexInterpreter` (that's DEX
  execution — functioning, stays here).
- **API binding forwarding**: bindings like `View.setText`/
  `View.setOnClickListener`/`View.getWidth`/`TypedArray.getColor`/etc. that
  today manipulate a local `AndroidViewNode` must now forward to
  ViewRuntime via the bridge and relay the real answer back to guest
  bytecode — investigate the full list of currently-bound View/ViewGroup/
  TypedArray API methods (`ApiLayer\AndroidApiBindings.cs`,
  `Ui\AndroidUiSession.cs`) and re-wire each one.

## What NOT to invent

- Don't design a NEW resource/style format — reuse what
  `AndroidResourceResolver`/`AndroidResourceTable` already parse and expose
  it through the new query surface.
- Don't leave ANY hardcoded visual value (color, size, spacing, font) on
  this side — if you're tempted to add one "just to make something render
  for now," stop and report instead; that's exactly the mistake this unit
  exists to undo.
- Don't keep the old Phase-1 ABI functions "just in case" — if the new
  bridge supersedes them, remove them, don't leave two parallel paths.

## Coordination with the ViewRuntime-side spec

A companion spec (`docs\phase2-full-delegation-viewruntime-spec.md`) is
being handed to the ViewRuntime session directly by the project owner —
it covers what ViewRuntime must implement to receive this bridge's data
and own 100% of view hierarchy/measure/layout/style/paint. **The exact ABI
shape must be agreed between both sides before either implements against
it** — this is a two-party contract, not something either side should
finalize alone. If you reach the point of designing the concrete ABI
functions/structs, treat that as the one thing worth a check-in (report
the proposed shape back before implementing it), since the ViewRuntime
session is designing its own matching half independently and a mismatch
here is expensive to discover late.

## Validation plan

Given the scope, validation is necessarily staged:
1. `dotnet build AndroidRuntime.sln -c Debug` — clean at each meaningful
   checkpoint, not just at the very end (this is a big unit, build
   incrementally).
2. `dotnet test` — the existing UI-layer test suite will largely need to
   be REMOVED/REWRITTEN along with the code it tested (View/ViewGroup/
   TypedArray/gravity/style-resolution tests that assert on the
   now-deleted C# view-hierarchy) — report what was removed and why,
   don't just leave tests red or delete them silently without accounting
   for them in the report.
3. Real-APK regression: SKYNET-FlexGrabber/MelyNails paths unrelated to UI
   must stay unchanged; SKYNET-ApkInstaller's full-lifecycle completion
   must still work once the new bridge is functional end-to-end (it may
   temporarily not render anything meaningful until ViewRuntime's side also
   lands — coordinate timing, report honestly if this unit finishes ahead
   of or behind the ViewRuntime-side work).
4. This is large enough that a mid-unit check-in with a status report
   (what's removed, what the proposed bridge shape is, what's left) is
   expected and welcome BEFORE full completion — don't disappear for the
   whole scope silently.

## Handoff format expected back

Report via `agent_send` to `claude_ac2b1602`, session `App Runtime`,
including mid-unit if the scope is taking a while. Files removed (full
list) + files added/changed + the exact bridge ABI proposed (functions/
structs, both directions) + reasoning for the AXML-tree-vs-raw-bytes
decision + what test coverage was removed and why + current status of
real-APK validation. Do not commit or push until the orchestrator
validates — given the size of this change, expect multiple review rounds,
not a single accept/reject.
