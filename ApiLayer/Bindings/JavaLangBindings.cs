#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>Bindings for java.lang framework types. File-per-package convention
/// (see ApiLayer\Bindings\), generalized from AndroidSystemServiceBindings.</summary>
internal static class JavaLangBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        RegisterObject(builder);
        RegisterEnum(builder, state);
    }

    private static void RegisterObject(AndroidApiRegistryBuilder builder)
    {
        // No-op super constructor for plain DEX classes extending java.lang.Object.
        RegisterVoid(builder, "Ljava/lang/Object;", "<init>", "()V");
    }

    private static void RegisterEnum(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // Real java.lang.Enum contract. Enum constants are guest DexObjects whose
        // name/ordinal live in an EnumPeer (peer store keyed by the receiver, not
        // the guest's InstanceFields). equals is reference identity (constants are
        // singletons); hashCode is the CLR identity hash — DexObject does not
        // override GetHashCode, matching Java's default Object.hashCode identity
        // semantics; toString defaults to name() (guest enum overrides of toString
        // resolve independently); compareTo compares ordinals.
        // getDeclaringClass()/valueOf(Class,String) ARE referenced by the target
        // APK but need java.lang.Class modeling — separate reflection-adjacent
        // feature, not bound here.
        builder.Register(Api("Ljava/lang/Enum;", "<init>", "(Ljava/lang/String;I)V"), (_, args) => { state.Enums.Add(Receiver(args), new EnumPeer(RequireString(args[1]), RequireInt(args[2]))); return null!; });
        builder.Register(Api("Ljava/lang/Enum;", "name", "()Ljava/lang/String;"), (_, args) => state.Enums.Get(Receiver(args)).Name);
        builder.Register(Api("Ljava/lang/Enum;", "ordinal", "()I"), (_, args) => state.Enums.Get(Receiver(args)).Ordinal);
        builder.Register(Api("Ljava/lang/Enum;", "toString", "()Ljava/lang/String;"), (_, args) => state.Enums.Get(Receiver(args)).Name);
        builder.Register(Api("Ljava/lang/Enum;", "equals", "(Ljava/lang/Object;)Z"), (_, args) => ReferenceEquals(Receiver(args), args[1]) ? 1 : 0);
        builder.Register(Api("Ljava/lang/Enum;", "hashCode", "()I"), (_, args) => Receiver(args).GetHashCode());
        builder.Register(Api("Ljava/lang/Enum;", "compareTo", "(Ljava/lang/Enum;)I"), (_, args) =>
        {
            int left = state.Enums.Get(Receiver(args)).Ordinal;
            int right = state.Enums.Get(RequireDex(args[1])).Ordinal;
            return left.CompareTo(right);
        });
    }

    private static void RegisterVoid(AndroidApiRegistryBuilder builder, string owner, string name, string descriptor) =>
        builder.Register(Api(owner, name, descriptor), (_, _) => null!);

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static string RequireString(object? value) => value as string ?? throw new ArgumentException("Expected a string.");
    private static int RequireInt(object? value) => value is int i ? i : throw new ArgumentException("Expected an int.");
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
