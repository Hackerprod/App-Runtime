#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.Arrays — SCOPED surface per the brief: only
/// `Arrays.copyOf(T[], int)` (the Object[] overload) is on the executed
/// SKYNET launch path (crash-path call site: Lokio/Options$Companion.of, the
/// same Options.of chain as the previous three boundaries — it calls copyOf
/// right after toArray). Real contract VERIFIED against the Java SE 17
/// Arrays.copyOf(T[], int) docs: "Copies the specified array, truncating or
/// padding with nulls (if necessary) so the copy has the specified length."
/// Confirmed details:
/// - Returns a NEW array of length newLength (never the original reference).
/// - Elements 0..min(original.length, newLength)-1 are copied from original.
/// - newLength > original.length: trailing slots filled with the component
///   type's default (null for a reference array — the only case reachable here).
/// - newLength < original.length: the array is truncated (extra source
///   elements dropped).
/// - RUNTIME TYPE: the new array mirrors the SOURCE array's own descriptor
///   (real copyOf(T[],int) internally delegates to
///   copyOf(original, newLength, original.getClass())), the same principle as
///   the Collection.toArray fix from the previous unit — never hardcode
///   [Ljava/lang/Object;. Also normalizes both guest-array shapes the
///   interpreter delivers (DexArray and legacy object[]), same as the toArray
///   binding.
/// Probe of SKYNET-FlexGrabber.apk: `Arrays.copyOf([Ljava/lang/Object;I)` is
/// the ONLY Arrays method on the executed path. The enormous remaining Arrays
/// surface (primitive copyOf overloads, copyOfRange, asList, equals, hashCode,
/// toString, fill, sort, binarySearch, deep*, compare, mismatch, stream, etc.)
/// IS method-table-referenced but only from bundled-lib helpers
/// (ArraysKt___*, okhttp, okio, kotlin stdlib, androidx) that do NOT run on
/// this path — NOT built by strict scope, reported as future boundaries.
/// </summary>
internal static class JavaUtilArraysBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Ljava/util/Arrays;", "copyOf", "([Ljava/lang/Object;I)[Ljava/lang/Object;"), (_, args) =>
        {
            var original = args[0];
            if (original is not DexArray && original is not object[])
                throw new ArgumentException("Arrays.copyOf requires an array.");
            int newLength = AndroidApiBindings.RequireInt(args[1]);
            if (newLength < 0)
                throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NegativeArraySizeException;"));

            int originalLength = original is DexArray sourceDex ? sourceDex.Length : ((object[])original!).Length;
            // Runtime type: mirror the SOURCE array's own descriptor (real
            // copyOf(T[],int) -> copyOf(original, newLength, original.getClass())).
            string descriptor = original is DexArray src ? src.ArrayDescriptor : "[Ljava/lang/Object;";
            var result = new DexArray(descriptor, newLength);
            int copied = Math.Min(originalLength, newLength);
            for (int index = 0; index < copied; index++)
            {
                object? element = original is DexArray od ? od.Get(index) : ((object[])original!)[index];
                // Extra trailing slots stay at the reference default (null); no
                // explicit write needed — DexArray initializes to null.
                if (element is not null)
                    result.Set(index, element);
            }
            return result;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
}
