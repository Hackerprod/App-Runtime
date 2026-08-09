#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for the bounded java.lang.reflect surface this runtime proves it
/// needs (see README boundary #43 — scoped from a real caller-chain and
/// full-APK annotation scan). Only the Method identity surface used by
/// androidx.lifecycle's legacy observer scan: getName (trivial) and
/// getAnnotation (ALWAYS null — this runtime does not model annotation metadata
/// at all; DEX annotation parsing is not implemented anywhere, so "not present"
/// is the only honest answer, confirmed correct for the observed real path where
/// no class declares @OnLifecycleEvent). Deliberately NOT bound: Method.invoke
/// (provably unreachable on this path), getModifiers/Modifier, getClassLoader,
/// getMethods (public/inherited), getDeclaredFields, any real Annotation model.
/// </summary>
internal static class JavaLangReflectBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Ljava/lang/reflect/Method;", "getName", "()Ljava/lang/String;"), (_, args) => state.Methods.Get(Receiver(args)).Name);
        builder.Register(Api("Ljava/lang/reflect/Method;", "getAnnotation", "(Ljava/lang/Class;)Ljava/lang/annotation/Annotation;"), (_, _) => null!);
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
}
