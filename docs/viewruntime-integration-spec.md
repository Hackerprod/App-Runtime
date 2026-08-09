# ViewRuntime integration spec — Phase 1 (render backend)

Audience: the ViewRuntime project (native, being built separately). This
spec defines exactly what ViewRuntime needs to expose for App Runtime to
integrate it, get real pixels on screen, and start a visual
feedback/iteration loop. Written from App Runtime's side of the boundary —
App Runtime's orchestrator (Claude Code) will implement the C# side of this
contract once ViewRuntime exposes it.

## Grounding: what App Runtime's UI pipeline actually does today

Read directly from `Ui\AndroidSceneHost.cs` and `Ui\AndroidUiSession.cs`
before assuming anything — summarized here:

- App Runtime already has a real, tested view-tree model
  (`AndroidViewNode` and subclasses: `AndroidLinearLayoutNode`,
  `AndroidTextViewNode`, `AndroidButtonNode`, `AndroidImageViewNode`),
  with real Android box-model measure/layout math (measure specs
  `Unspecified`/`Exactly`/`AtMost`, `MATCH_PARENT`/`WRAP_CONTENT`/exact dp
  sizing, orientation-aware `LinearLayout` measure+layout), real hit-testing
  (`AndroidSceneHost.HitTest`), and real click dispatch back into guest
  bytecode (`AndroidUiSession.PerformClick` → `DexInterpreter.InvokeVirtualInstanceExact`).
- Each frame, this pipeline produces an `AndroidDisplayList` — a flat,
  ordered list of abstract draw commands. **Today there are exactly two
  command types**: `AndroidFillRectCommand(Rect, Color, ViewId)` and
  `AndroidDrawTextCommand(Rect, Text, TextSizePixels, Color, ViewId)`. No
  image/bitmap command exists yet (there's no drawable-decoding pipeline
  behind it — `ImageView` currently renders nothing but its background, by
  design, documented in `AndroidImageViewNode`'s own comment).
- The display list is handed to an `IAndroidRenderBackend`:
  ```csharp
  public interface IAndroidRenderBackend : IDisposable {
      void Resize(int pixelWidth, int pixelHeight, float density);
      void Render(AndroidDisplayList displayList);
  }
  ```
  **The only implementation that exists today is `RecordingAndroidRenderBackend`**
  — it stores frames in a list for test assertions and paints nothing. This
  is the exact seam ViewRuntime plugs into: **ViewRuntime becomes a second,
  real implementation of this interface.**
- Text layout also depends on an `IAndroidTextMeasurer`:
  ```csharp
  public interface IAndroidTextMeasurer { AndroidTextMetrics Measure(string text, float textSizePixels, float maxWidth); }
  ```
  Today's only implementation, `DeterministicAndroidTextMeasurer`, is a
  deliberately fake, non-visual stub (a fixed per-character width heuristic)
  used to keep layout tests reproducible without a real font. **For real
  visual output, layout math must use ViewRuntime's real font metrics too**
  — otherwise text will be measured with fake widths during layout but
  drawn with real glyph widths during paint, causing visible overflow/
  misalignment. ViewRuntime needs to expose real text measurement as well
  as real text drawing, and both must agree.

## Why this is Phase 1, not a full replacement

App Runtime's existing view-tree/measure/layout/hit-test/click-dispatch
code is already real, tested, and correctly matches Android's box model for
what it covers. Replacing that wholesale with ViewRuntime's own view
hierarchy right away would be a much bigger, riskier integration (every
`View`/`ViewGroup`/`TypedArray`/etc. API binding — dozens already built —
would need to be rewired to a completely different object model). The
render-backend + text-measurer seam above is the **smallest possible
integration surface that produces real visual output** for feedback now.
Once ViewRuntime's own view-hierarchy/layout/ConstraintLayout work is
mature enough to fully replace `AndroidSceneHost`, that's a real Phase 2 —
not addressed by this spec, not being decided now.

## What ViewRuntime needs to expose (Phase 1 contract)

A flat C ABI (matching ViewRuntime's existing shared-library shape), called
from C# via P/Invoke. Proposed shape — ViewRuntime's author should treat
this as a starting proposal, not a rigid mandate; flag anything that
doesn't fit ViewRuntime's actual internals and propose an alternative
rather than forcing a bad fit.

```c
// Surface lifecycle
void*  viewruntime_surface_create(void);
void   viewruntime_surface_destroy(void* surface);
void   viewruntime_surface_resize(void* surface, int pixel_width, int pixel_height, float density);

// Frame lifecycle — App Runtime calls begin, then N draw calls in display-list
// order, then end. Commands must be painted in the given order (later
// commands draw over earlier ones — matches the display list's own paint
// order, which is depth-first tree order, backgrounds under children).
void   viewruntime_frame_begin(void* surface);
void   viewruntime_draw_fill_rect(void* surface, float x, float y, float w, float h,
                                   uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
void   viewruntime_draw_text(void* surface, float x, float y, float w, float h,
                              const uint16_t* utf16_text, int32_t text_len,
                              float text_size_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
                              int32_t view_id);
void   viewruntime_frame_end(void* surface);

// Text measurement — MUST use the same font/metrics path as viewruntime_draw_text,
// so layout (measured with this) and paint (drawn with viewruntime_draw_text)
// agree pixel-for-pixel on text size.
void   viewruntime_measure_text(void* surface, const uint16_t* utf16_text, int32_t text_len,
                                 float text_size_px, float max_width_px,
                                 float* out_width_px, float* out_height_px, float* out_baseline_px);

// Pixel access for the Windows-side compositing model (see below) — exact
// shape depends on which compositing option is chosen; propose whichever
// is natural for ViewRuntime's actual internal surface representation.
```

Notes on the proposal:
- **Coordinates**: physical pixels, origin top-left, matching
  `AndroidRect`'s existing convention (`X, Y, Width, Height`, all `float`).
- **Color**: straight (non-premultiplied) ARGB8888, matching
  `AndroidColor(byte A, byte R, byte G, byte B)`'s existing field order —
  confirm this is a natural fit for ViewRuntime's internal color
  representation, or propose the actual native order and App Runtime will
  convert at the P/Invoke boundary.
- **Text encoding**: UTF-16 (`uint16_t*` + explicit length, not
  null-terminated) — matches .NET `string`'s native representation exactly,
  avoids a UTF-8 re-encode on every draw/measure call. If ViewRuntime's
  internal text pipeline is UTF-8-native, say so and App Runtime will
  convert instead — don't silently pick one without confirming which side
  should own the conversion cost.
- **`view_id`**: passed through only for diagnostics/hit-testing
  cross-checks (e.g. visually confirming which real Android view a drawn
  rect corresponds to) — ViewRuntime doesn't need to interpret it.
- **Threading**: all calls happen on App Runtime's single execution-lane
  thread (this runtime has a real per-session GIL — see `AndroidGil` — so
  there is never concurrent access to a given surface). ViewRuntime does
  not need to be thread-safe internally for this integration.

## Windows-side compositing: two options, recommend starting with the simpler one

**Option A (recommended for Phase 1): off-screen buffer, blitted into WPF.**
ViewRuntime renders into a CPU or GPU off-screen buffer; App Runtime reads
the finished frame's pixels (or a shared texture handle) each `Render()`
call and blits them into a WPF `WriteableBitmap`/`Image` element already
hosted in the existing window. Simplest to get working end-to-end quickly,
easiest to debug, no native HWND parenting/message-loop interop needed.

**Option B (later, if Option A's performance isn't good enough): a native
child HWND that ViewRuntime draws into directly.** App Runtime already has
a precedent for this — per `README.md`, Toast text is already composited
"into the same retained child-HWND surface as Android content" — so a
retained child-HWND pattern already exists in this codebase and could be
extended to host ViewRuntime's own rendering surface directly, avoiding a
per-frame CPU blit. More complex (window messages, resize handling, HWND
lifecycle across session teardown) — not the first thing to build.

State a recommendation from ViewRuntime's side too — if ViewRuntime already
has a preferred/existing Windows output path (e.g. it already renders via
Direct2D/GDI to an HWND for its own standalone testing), that may make
Option B the natural starting point instead; this is a joint decision, not
dictated unilaterally here.

## What does NOT change in Phase 1

- The view tree, measure/layout algorithm, hit-testing, and click dispatch
  all stay exactly as they are in `Ui\AndroidSceneHost.cs`/
  `Ui\AndroidUiSession.cs` — untouched.
- Every existing `View`/`ViewGroup`/`TypedArray`/etc. API binding continues
  to work unchanged — they all operate on `AndroidViewNode`, not on
  anything ViewRuntime-specific.
- Input handling is unaffected: hit-testing already happens in C# against
  `AndroidViewNode.Bounds` before any ViewRuntime call — ViewRuntime only
  needs to paint, not receive input, in Phase 1.

## Validation / feedback loop once this is built

App Runtime will implement a `ViewRuntimeRenderBackend : IAndroidRenderBackend`
and `ViewRuntimeTextMeasurer : IAndroidTextMeasurer` (thin P/Invoke
adapters over the C ABI above), wire them into `AndroidUiSession`'s
existing `new AndroidSceneHost(root, textMeasurer, renderBackend, limits)`
construction in place of the current `DeterministicAndroidTextMeasurer`/
`RecordingAndroidRenderBackend`, and run the full real-APK validation loop
already used throughout this project (`SKYNET-ApkInstaller-v1.0-debug.apk`
is the natural first visual target — it already completes its entire
lifecycle with zero errors through the existing native inflater, so it's
the cleanest first real screen to actually SEE rendered). Feedback will be
given on real rendered frames against real app layouts, not synthetic
test cases.

## Open questions for ViewRuntime's author

1. Confirm or propose an alternative for the exported-function shape above
   — does it match ViewRuntime's actual internal architecture, or does a
   different shape fit better (e.g. a single opaque command-buffer submit
   instead of one call per draw command)?
2. Text encoding: UTF-16 (proposed) or UTF-8 (if ViewRuntime's text
   pipeline is already UTF-8-native)?
3. Color channel order and premultiplied-vs-straight alpha — confirm
   ViewRuntime's native representation.
4. Compositing model: Option A (off-screen blit) or Option B (native child
   HWND) — which is more natural given ViewRuntime's current Windows output
   path, if it has one already?
5. Font selection: what font(s) does ViewRuntime actually render with
   today, and does it need a font name/family hint from App Runtime, or is
   a fixed system font acceptable for Phase 1?

## Confirmed answers (ViewRuntime's response, App Runtime's confirmations)

ViewRuntime's response: shape confirmed workable (its `display_list_t`/
`paint_command_t` model already matches a flat call-per-command ABI well —
individual `draw_*` calls preferred over a submitted command-buffer union,
which would need fragile C#-side marshaling); **ViewRuntime currently only
records display lists, it does not rasterize yet** — Phase 1 requires it to
add an actual rasterizer (rect fill + `stb_truetype` glyph blit + clip),
described as feasible and bounded. UTF-16 accepted (converts internally at
the boundary, cost lives in ViewRuntime). Color: internal `color_rgba`
(float 0..1), accepts straight (non-premultiplied) `uint8 A,R,G,B` and
converts at the boundary — matches. Compositing: **Option A confirmed**
(ViewRuntime has no existing Windows output path at all — never targeted
Direct2D/GDI/HWND, display-lists-only until now) — surface exposes an
ARGB8888 buffer + pitch at frame end for App Runtime to blit into WPF.
Font: `android_ui_set_font(path)` already loads one system font via
`stb_truetype` for both measurement and drawing (same measurer for both —
already pixel-perfect-consistent by construction); a fixed system font for
Phase 1 is fine, set once at `viewruntime_surface_create`.

**Question asked back, answered**: does `AndroidDrawTextCommand`'s rect
mean an already-positioned text box, or the view's box needing internal
alignment? **Confirmed by re-reading `Ui\AndroidSceneHost.cs` directly**:
`AndroidTextViewNode.Record` passes its own `Bounds` — the view's full,
already-laid-out rectangle (set by `Layout(x,y,width,height,...)`), not a
tightened-to-text box. **Draw text from the rect's top-left corner,
clipped to the rect — confirmed correct, not just a reasonable fallback.**
This is Android's own real default `TextView` gravity (`Gravity.TOP |
Gravity.START`) — and it's the ONLY correct behavior here regardless,
because **`android:gravity`/text centering is not modeled anywhere in this
codebase yet** (verified: zero references to "gravity" in `Ui\*.cs` or the
API bindings). If a future guest layout sets `android:gravity="center"`
and expects centered text, that will visibly not centre — a known, shared
bounded limitation on the App Runtime side, not something for ViewRuntime
to compensate for.
