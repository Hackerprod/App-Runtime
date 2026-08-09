# ViewRuntime native core

This is the product. The implementation is C++20; the only thing a consumer is
allowed to depend on is the C headers at [`include/viewruntime/viewruntime.h`](include/viewruntime/viewruntime.h)
and [`include/viewruntime/android.h`](include/viewruntime/android.h) plus the
installed binary they declare.

ViewRuntime renders **Android application UI**. The legacy HTML+CSS front-end was
removed: the core now owns an Android view model (view tree, `LayoutParams`,
measure specs), Android layout semantics (`LinearLayout`, `FrameLayout`,
`RelativeLayout`, `ScrollView`), and retained paint-command recording. It does
not own windows, an OS event loop, text shaping/rasterization, or a graphics
API — those are host concerns, deliberately left to each binding.

## Pipeline

The core implements the Android two-phase contract with fidelity to AOSP:

```text
view tree (view classes + LayoutParams + attributes, dp/sp units)
    ↓ measure(root, width, height)      EXACTLY / AT_MOST / UNSPECIFIED
    ↓ layout(root, x, y, width, height) absolute pixel bounds for every view
    ↓ record(root)                      retained ViewRuntime paint commands
    ↓ binding renders the display list on its own graphics API
```

Key semantics implemented (each verified against the AOSP source, not inferred):

- `MeasureSpec` modes and the canonical `ViewGroup.getChildMeasureSpec`
  resolution, plus `View.resolveSizeAndState` behavior for `wrap_content`,
  `match_parent`, and exact dimensions.
- `LinearLayout`: orientation, `layout_weight` with the exact AOSP
  distribution (weighted children keep their measured size plus their share of
  the remaining — possibly negative — excess; 0-dimension weighted children
  receive the share alone; shares are subtracted sequentially), the
  skip-measure optimization for 0-dimension weighted children under an
  EXACTLY parent, `weightSum`, container major gravity, per-child and
  container gravity, margins/padding, and baseline alignment grouped by the
  child's vertical-gravity bucket (TOP/BOTTOM adjust, CENTER_VERTICAL does
  not, per AOSP bug #1038483).
- `FrameLayout`: stacked children with gravity placement, including the
  AOSP second-pass re-measure of `MATCH_PARENT` children under a
  non-EXACTLY parent and the exact per-gravity placement formulas.
- `GridLayout`: faithful port of the AOSP linear-constraint solver — row and
  column arcs, topological ordering, a Bellman-Ford variant with culprit
  removal, auto cell assignment (`validateLayoutParams`), explicit cells and
  spans, per-spec alignments (START/END/CENTER/FILL/BASELINE) with
  alignment-group bounds, row/column weights through the integer delta
  binary search, and FILL re-measurement of the first-laid axis.
- `RelativeLayout`: parent alignment rules, centering, and sibling anchors
  (below/above/toLeftOf/toRightOf/align*).
- `ScrollView`: unbounded child measure, scroll offset clamping, scroll
  metrics, and clip + translate recording.
- `ListView`: adapter-list semantics verified against AOSP — items measured
  with UNSPECIFIED height (content, uncapped), stacked with the divider
  between them (`nextTop = child.bottom + dividerHeight`), content height
  counts the divider `(n-1)` times (`measureHeightOfChildren`), and the
  divider rects are emitted per `dispatchDraw` (between enabled items and
  after the last one while it stays above the list bottom), with the Material
  default of 1dp at `colorListDivider` (light theme: black 12%).
- `RecyclerView` (LinearLayoutManager): items measured through the canonical
  `getChildMeasureSpec` on both axes (`wrap` degrades to AT_MOST under a
  bounded parent), margins act as item decoration (`layoutChunk` +
  `layoutDecoratedWithMargins`), vertical and horizontal orientations, no
  default divider.
- Widgets: `TextView`, `Button`, `EditText` (hint), `ImageView`
  (scale types), `CheckBox`, `RadioButton`, `ProgressBar`, generic `View`.
  Views without a text baseline report no baseline, matching
  `View.getBaseline()` returning -1.

The host supplies text measurement and image dimensions through callbacks;
both degrade to deterministic fallbacks when unset.

## Module map

```text
src/
  android/         Android view model: view tree, LayoutParams, measure/layout
                     engine, display-list recorder, density scaling
  rendering/       DisplayList + command storage/ownership, render-plan policy,
                     transform matrix utilities
  css/             Shared paint value types (CssLength, ColorRgba) — value
                     types only, no cascade, no selectors, no HTML
  primitives/      RectF/PointF/SizeF geometry shared across every module
  abi/             abi_version()/abi_capabilities()/status and
                     the paint value list copy/equal/free helpers
```

## ABI contract

The current ABI is **0.31.0**: flat C functions, opaque handles, fixed-width
value types, integer status codes, and plain C callbacks. No C++ classes, STL
containers, or exceptions cross the boundary — every public function is a
`noexcept`-equivalent that converts any internal exception into a status code
before returning.

Every consumer must inspect the loaded library before creating a single handle:

```c
#include <viewruntime/viewruntime.h>

uint32_t version = abi_version();
if (ABI_VERSION_GET_MAJOR(version) != ABI_VERSION_MAJOR) {
    /* refuse to load: major-version mismatch is a breaking-change signal */
}

capabilities_t capabilities = abi_capabilities();
size_t paint_command_size = paint_command_size();
```

`CAPABILITY_ANDROID_UI` advertises the tested Android pipeline.
`paint_command_size()` exists so a binding never hard-codes
`sizeof(paint_command_t)`. The display-list command union grows as
paint features are added; allocate command buffers from this call, not from a
literal.

**Ownership.** The Android UI session owns every view it creates
(`android_ui_destroy`/`android_ui_clear` release them). Views are
*borrowed* once created — they are valid only while their session lives. A
display list returned by `android_ui_record` owns its commands; release
it with `display_list_destroy`.

**Threading.** Handle access is serialized: no two threads may operate on the
same UI session, view, or display list concurrently. Callbacks (text
measurement, image dimensions) run synchronously on the calling thread during
the native call that triggered them, must remain valid for as long as they're
registered, and must not let an exception, panic, or longjmp unwind through
native stack frames — catch everything at the callback boundary and return a
safe fallback value instead.

## Build, test, and install

The install tree — not the build tree — is the consumption contract. Never
point a consumer at `build/` or a private CMake cache directory.

**Windows (MSVC):**

```powershell
cmake --preset dev
cmake --build --preset dev
ctest --test-dir build/dev -C Debug --output-on-failure
cmake --install build/dev --config Debug
```

**Linux / macOS:**

```sh
cmake -S . -B build/native -DCMAKE_BUILD_TYPE=Release -DVIEWRUNTIME_BUILD_TESTS=ON
cmake --build build/native --parallel
ctest --test-dir build/native --output-on-failure
cmake --install build/native
```

Two test binaries run under CTest:

- `viewruntime.smoke` (`tests/test_smoke.cpp`) — end-to-end Android pipeline: tree,
  measure, layout, hit testing, record, display-list reads.
- `viewruntime.abi-c` (`tests/test_abi_c.c`) — a **C11** consumer, compiled and
  linked with no C++ in its translation unit, proving the headers are
  genuinely C-clean and not "C++ wearing an `extern "C"` hat."

The Android tests (`viewruntime.android-measure`, `viewruntime.android-layout`,
`viewruntime.android-display-list`, `viewruntime.android-api`) pin the layout semantics
against AOSP behavior: spec resolution, weights, gravity, baseline alignment,
relative anchors, scroll, hit testing, and command recording.

`tests/package_consumer/` is a separate, standalone CMake project (its own
`CMakeLists.txt`, not part of the main build) that runs
`find_package(ViewRuntime CONFIG REQUIRED)` against an *installed* tree and links
`ViewRuntime::Core` — the regression test for the install/export step itself.

## Installed package

CMake consumers use the exported imported target:

```cmake
find_package(ViewRuntime CONFIG REQUIRED)
target_link_libraries(my_app PRIVATE ViewRuntime::Core)
```

pkg-config consumers (non-CMake C/C++ builds, most cgo setups) use:

```sh
pkg-config --cflags --libs viewruntime-core
```

Both are generated from `cmake/ViewRuntimeConfig.cmake.in` and
`cmake/viewruntime-core.pc.in` at configure time and installed under
`<prefix>/lib/cmake/ViewRuntime/` and `<prefix>/lib/pkgconfig/` respectively —
private headers and build-tree binaries are never a distribution interface,
only `<prefix>/include/viewruntime/` and the installed library are.
