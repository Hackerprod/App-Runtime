#nullable enable
// Phase 2 (in progress): the Phase-1 per-command ViewRuntime ABI
// (frame_begin / draw_fill_rect / draw_text / frame_end / measure_text) has
// been REMOVED — this side no longer interprets display lists or measures text
// locally. The Phase-2 bridge ABI (inflate request + resource-query callbacks +
// frame lifecycle + hit-test + API forwarding) is a two-party contract being
// agreed between this session and the ViewRuntime session; this file will be
// rewritten with the agreed P/Invoke surface once that contract is finalized.
// Until then, view operations fail closed via UnavailableAndroidViewBridge.
