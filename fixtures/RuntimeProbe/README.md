# RuntimeProbe APK fixture

The real `onCreate` bytecode exercises StringBuilder/String/TextUtils, all non-Throwable Log levels, Context and Activity getters, typed Bundle roundtrips (including long values), the three long-returning SystemClock APIs, wide long/double constants, calls, fields, arrays, arithmetic and comparisons, the stable launcher Intent and extras, Color packing, and a text Toast. Each feature writes observable Activity fields; the WPF smoke also observes the Toast overlay visible and subsequently hidden.

This fixture proves the current scope: reading a real binary manifest, resolving its MAIN/LAUNCHER Activity, and interpreting that Activity's constructor plus `onCreate(Bundle)`, `onStart()`, and `onResume()` from a signed APK. All three callbacks call their exact `Activity` base stubs. `onCreate` also calls the exact `Activity.setTitle(CharSequence)` and `Log.i(String,String)` bindings. The observable sequence is `1 → 12 → 123`, with one counter increment per callback.

`src/org/example/runtimeprobe/MainActivity.java` is compiled against `android.jar` with `javac`, converted with `d8`, packaged with `aapt2`, aligned with `zipalign`, and signed/verified with `apksigner`. The build also validates the binary manifest with `aapt2 dump xmltree` and `aapt2 dump badging`.

```powershell
.\fixtures\RuntimeProbe\build.ps1
```

The script discovers the newest installed Android SDK build-tools/platform and requires a JDK through `JAVA_HOME`, `javac.exe` on `PATH`, or the local Codex JDK cache used by this workspace. It writes `RuntimeProbe.apk`, identical signed `WideProbe.apk`, `WideClockProbe.apk`, `ExceptionProbe.apk`, and `ServicesProbe.apk` gate artifacts, a separately linked/signed `ServicesProbeMissingPermission.apk`, plus a reproducible `UnimplementedApiProbe.apk` variant. `ExceptionProbe` contains real typed/catch-all tables, explicit/rethrown/null throws, multi-frame unwinding, arithmetic/null/bounds/cast failures, and a sanitized uncaught path. `ServicesProbe` covers caught default denials, stable managers, unknown services, plain-text clipboard, focus/clear behavior, connectivity capabilities/offline/stale tokens, and the ACCESS_NETWORK_STATE manifest gate. The unimplemented variant changes one bound log call to a deliberately unbound API, proving a real ApkLoader-to-DexReader-to-lifecycle `Unimplemented` failure path.

Honest boundary: this fixture does not render UI, load resources, create a real Android `Bundle` or `Intent`, or execute `onPause`/`onStop`.

Rollback boundary: remove the binary-manifest reader, `AndroidAppRuntime`, instance invocation APIs, Activity stubs, lifecycle tests, and this fixture update together. The earlier APK-to-static-DEX execution slice remains independent.

Hardening rollback boundary: the direct intent-filter hierarchy checks, string-pool/chunk invariants, and `ApkLoadLimits` ZIP quotas form one defensive correction with their focused tests. They can be reverted together without adding UI or changing the lifecycle behavior above.

Lifecycle-extension rollback boundary: remove `AndroidActivitySession`, bounded hierarchical lifecycle lookup, `onStart`/`onResume` stubs and tests, then restore the fixture to its `onCreate`-only state. Manifest parsing, ZIP hardening, constructor execution, and the earlier Created-state slice remain independent.

Production-shaped binding rollback boundary: remove the `setTitle` and `Log.i` calls from `MainActivity` together with their exact bindings and hosted integration assertions. Lifecycle state and manifest behavior remain unchanged.

Verification-hardening rollback boundary: remove the generated `UnimplementedApiProbe.apk` variant and its integration test together; the successfully bound fixture remains unchanged.
