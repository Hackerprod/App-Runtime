#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>Bindings for java.lang framework types. File-per-package convention
/// (see ApiLayer\Bindings\), generalized from AndroidSystemServiceBindings.</summary>
internal static class JavaLangBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        RegisterObject(builder, state);
        RegisterEnum(builder, state);
        RegisterClass(builder, state);
    }

    private static void RegisterObject(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // No-op super constructor for plain DEX classes extending java.lang.Object.
        RegisterVoid(builder, "Ljava/lang/Object;", "<init>", "()V");
        // Real Object.getClass(): canonical Class object for the receiver's RUNTIME
        // type (same identity as const-class via the shared cache).
        builder.Register(Api("Ljava/lang/Object;", "getClass", "()Ljava/lang/Class;"), (_, args) =>
        {
            var receiver = Receiver(args);
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("getClass requires an attached interpreter.");
            return state.EnsureClassObject(interpreter.RuntimeDescriptorOf(receiver));
        });
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
        // Previously deferred until java.lang.Class existed — now bound.
        // getDeclaringClass returns the canonical Class of the enum instance's own
        // type (bounded: real Java returns the innermost declaring class for
        // nested enum constants; this runtime's enums are top-level guest classes,
        // so the instance type is the declaring type).
        builder.Register(Api("Ljava/lang/Enum;", "getDeclaringClass", "()Ljava/lang/Class;"), (_, args) =>
        {
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("getDeclaringClass requires an attached interpreter.");
            return state.EnsureClassObject(interpreter.RuntimeDescriptorOf(Receiver(args)));
        });
        // valueOf(Class,String) enumerates the constants a guest <clinit> stored
        // as static fields of the represented enum class and matches by the
        // EnumPeer name; missing name -> IllegalArgumentException (real contract).
        builder.Register(Api("Ljava/lang/Enum;", "valueOf", "(Ljava/lang/Class;Ljava/lang/String;)Ljava/lang/Enum;"), (_, args) =>
        {
            var enumClass = RequireDex(args[0]);
            string descriptor = state.ClassPeerOf(enumClass).RepresentedDescriptor;
            string name = RequireString(args[1]);
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("valueOf requires an attached interpreter.");
            foreach (object constant in interpreter.EnumConstantsOf(descriptor))
            {
                if (state.Enums.Get((DexObject)constant).Name == name)
                    return constant;
            }
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "No enum constant " + descriptor + "." + name));
        });
    }

    private static void RegisterClass(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)    {
        // Real java.lang.Class contract for the commonly-used surface. Class
        // objects are canonical singletons per descriptor (AndroidFrameworkState
        // cache), so equals/hashCode are identity and come free.
        builder.Register(Api("Ljava/lang/Class;", "getName", "()Ljava/lang/String;"), (_, args) => ClassName(state.ClassPeerOf(Receiver(args)).RepresentedDescriptor));
        builder.Register(Api("Ljava/lang/Class;", "getCanonicalName", "()Ljava/lang/String;"), (_, args) =>
        {
            // Real contract: for top-level classes and primitives/arrays the
            // canonical name equals getName(); inner classes would render
            // "Outer.Inner" (binary '$' -> '.'), anonymous/local classes null.
            // Bounded: this runtime models top-level guest classes, so getName()
            // formatting is the honest answer (documented limitation: inner-class
            // canonicalization is not detected).
            return ClassName(state.ClassPeerOf(Receiver(args)).RepresentedDescriptor);
        });
        builder.Register(Api("Ljava/lang/Class;", "getSimpleName", "()Ljava/lang/String;"), (_, args) => SimpleName(state.ClassPeerOf(Receiver(args)).RepresentedDescriptor));
        builder.Register(Api("Ljava/lang/Class;", "isInstance", "(Ljava/lang/Object;)Z"), (_, args) =>
        {
            if (args[1] is null || args[1] is int zero && zero == 0) return 0; // real: isInstance(null) == false
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("isInstance requires an attached interpreter.");
            return interpreter.IsGuestTypeAssignable(interpreter.RuntimeDescriptorOf(args[1]), state.ClassPeerOf(Receiver(args)).RepresentedDescriptor) ? 1 : 0;
        });
        builder.Register(Api("Ljava/lang/Class;", "isAssignableFrom", "(Ljava/lang/Class;)Z"), (_, args) =>
        {
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("isAssignableFrom requires an attached interpreter.");
            return interpreter.IsGuestTypeAssignable(state.ClassPeerOf(RequireDex(args[1])).RepresentedDescriptor, state.ClassPeerOf(Receiver(args)).RepresentedDescriptor) ? 1 : 0;
        });
        builder.Register(Api("Ljava/lang/Class;", "getSuperclass", "()Ljava/lang/Class;"), (_, args) =>
        {
            string represented = state.ClassPeerOf(Receiver(args)).RepresentedDescriptor;
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("getSuperclass requires an attached interpreter.");
            // Reuse the interpreter's own superclass resolution (guest chain +
            // framework parents). Real Java: Object has no superclass (null);
            // arrays/primitives report Object/null respectively — bounded here to
            // null for anything without a resolvable superclass (no array model).
            string? parent = interpreter.SuperclassDescriptorOf(represented);
            return parent is null ? null! : state.EnsureClassObject(parent);
        });
        builder.Register(Api("Ljava/lang/Class;", "getPackage", "()Ljava/lang/Package;"), (_, args) =>
        {
            // Real contract: the Package of the represented type; null for
            // primitives/arrays and for classes in the default (unnamed) package.
            string represented = state.ClassPeerOf(Receiver(args)).RepresentedDescriptor;
            string? packageName = PackageNameOf(represented);
            return packageName is null ? null! : state.EnsurePackageObject(packageName);
        });
        // Class.forName: real dynamic lookup by dotted binary name across the LOADED
        // guest classes. Found -> triggers <clinit> (real documented side effect)
        // and returns the canonical Class object; not found (including framework
        // types, which have no reverse name index in this runtime) -> checked
        // ClassNotFoundException.
        builder.Register(Api("Ljava/lang/Class;", "forName", "(Ljava/lang/String;)Ljava/lang/Class;"), (_, args) => ForName(state, RequireString(args[0]), initialize: true));
        builder.Register(Api("Ljava/lang/Class;", "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;"), (_, args) => ForName(state, RequireString(args[0]), RequireInt(args[1]) != 0));
        // Bounded reflection for the androidx.lifecycle legacy observer scan (see
        // README boundary #43): declared methods as Method[] (fresh Method objects
        // — real Java does not guarantee Method reference identity across calls),
        // declared interfaces as canonical Class[].
        builder.Register(Api("Ljava/lang/Class;", "getDeclaredMethods", "()[Ljava/lang/reflect/Method;"), (_, args) =>
        {
            string represented = state.ClassPeerOf(Receiver(args)).RepresentedDescriptor;
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("getDeclaredMethods requires an attached interpreter.");
            var methods = interpreter.DeclaredMethodsOf(represented);
            var array = new DexArray("[Ljava/lang/reflect/Method;", methods.Count);
            for (int index = 0; index < methods.Count; index++)
            {
                var methodObject = new DexObject("Ljava/lang/reflect/Method;");
                state.Methods.Add(methodObject, new MethodPeer(represented, methods[index].Method.Name, methods[index].Method.Proto.Descriptor()));
                array.Set(index, methodObject);
            }
            return array;
        });
        builder.Register(Api("Ljava/lang/Class;", "getInterfaces", "()[Ljava/lang/Class;"), (_, args) =>
        {
            string represented = state.ClassPeerOf(Receiver(args)).RepresentedDescriptor;
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("getInterfaces requires an attached interpreter.");
            var interfaces = interpreter.DeclaredInterfacesOf(represented);
            var array = new DexArray("[Ljava/lang/Class;", interfaces.Count);
            for (int index = 0; index < interfaces.Count; index++)
                array.Set(index, state.EnsureClassObject(interfaces[index]));
            return array;
        });
        builder.Register(Api("Ljava/lang/Class;", "equals", "(Ljava/lang/Object;)Z"), (_, args) => ReferenceEquals(Receiver(args), args[1]) ? 1 : 0);
        builder.Register(Api("Ljava/lang/Class;", "hashCode", "()I"), (_, args) => Receiver(args).GetHashCode());
        builder.Register(Api("Ljava/lang/Class;", "toString", "()Ljava/lang/String;"), (_, args) => "class " + ClassName(state.ClassPeerOf(Receiver(args)).RepresentedDescriptor));
        // The minimal Package surface SKYNET actually reaches (getName only).
        builder.Register(Api("Ljava/lang/Package;", "getName", "()Ljava/lang/String;"), (_, args) => state.PackagePeerOf(Receiver(args)).Name);
    }

    // ---------------------------------------------------------------------------
    // Class name formatting (real Java contract)
    // ---------------------------------------------------------------------------

    private static string ClassName(string descriptor)
    {
        if (descriptor.StartsWith("[", StringComparison.Ordinal)) return ArrayClassName(descriptor);
        if (descriptor == "V") return "void";
        string? primitive = PrimitiveName(descriptor);
        if (primitive is not null) return primitive;
        // "Lcom/foo/Bar;" -> "com.foo.Bar" (dotted binary name, real getName()).
        return descriptor.Substring(1, descriptor.Length - 2).Replace('/', '.');
    }

    private static string ArrayClassName(string descriptor)
    {
        // Real Class.getName() for arrays keeps the descriptor form but with class
        // components in dotted form: "[I" stays "[I", "[Ljava/lang/String;" ->
        // "[Ljava.lang.String;".
        int index = 0;
        var builder = new System.Text.StringBuilder();
        while (index < descriptor.Length && descriptor[index] == '[')
        {
            builder.Append('[');
            index++;
        }
        string component = descriptor[index..];
        if (component.StartsWith("L", StringComparison.Ordinal))
        {
            builder.Append('L').Append(component.Substring(1, component.Length - 2).Replace('/', '.')).Append(';');
        }
        else
        {
            builder.Append(component);
        }
        return builder.ToString();
    }

    private static string SimpleName(string descriptor)
    {
        if (descriptor.StartsWith("[", StringComparison.Ordinal))
        {
            // Real getSimpleName for arrays: component simple name + "[]" per rank.
            int rank = 0;
            while (rank < descriptor.Length && descriptor[rank] == '[') rank++;
            return SimpleName(descriptor[rank..]) + new string('[', rank) + new string(']', rank);
        }
        if (descriptor == "V") return "void";
        string? primitive = PrimitiveName(descriptor);
        if (primitive is not null) return primitive;
        string dotted = descriptor.Substring(1, descriptor.Length - 2).Replace('/', '.');
        int cut = Math.Max(dotted.LastIndexOf('.'), dotted.LastIndexOf('$'));
        return cut < 0 ? dotted : dotted[(cut + 1)..];
    }

    private static string? PrimitiveName(string descriptor) => descriptor switch
    {
        "Z" => "boolean",
        "B" => "byte",
        "C" => "char",
        "S" => "short",
        "I" => "int",
        "J" => "long",
        "F" => "float",
        "D" => "double",
        _ => null
    };

    /// <summary>Package name of a type descriptor, or null for the default
    /// (unnamed) package, primitives, and arrays — matching real
    /// Class.getPackage()'s null cases.</summary>
    private static string? PackageNameOf(string descriptor)
    {
        if (descriptor.Length < 3 || descriptor[0] != 'L' || descriptor[^1] != ';') return null;
        string binary = descriptor.Substring(1, descriptor.Length - 2).Replace('/', '.');
        int lastDot = binary.LastIndexOf('.');
        if (lastDot <= 0) return null; // default package or a bare name
        return binary[..lastDot];
    }

    /// <summary>Class.forName core: dotted binary name -> internal descriptor,
    /// lookup across loaded guest classes, initialization side effect, canonical
    /// Class object.</summary>
    private static object ForName(AndroidFrameworkState state, string className, bool initialize)
    {
        var interpreter = state.Interpreter ?? throw new InvalidOperationException("Class.forName requires an attached interpreter.");
        string descriptor = DescriptorFromBinaryName(className);
        // Only guest-defined classes are resolvable by name in this runtime:
        // framework/API-bound types have no reverse name index (honest, correctly
        // scoped limitation — they also throw ClassNotFoundException, real
        // semantics for anything that cannot be loaded).
        if (!interpreter.HasGuestClass(descriptor))
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/ClassNotFoundException;", className));
        if (initialize)
            interpreter.EnsureGuestClassInitialized(descriptor);
        return state.EnsureClassObject(descriptor);
    }

    /// <summary>Reverse of Class.getName(): dotted binary name back to the
    /// internal descriptor. Handles normal classes and (bounded) array names
    /// whose class components are dotted ("[Ljava.lang.String;" -> "[Ljava/lang/String;").</summary>
    private static string DescriptorFromBinaryName(string name)
    {
        if (name.StartsWith("[", StringComparison.Ordinal))
        {
            var builder = new System.Text.StringBuilder();
            int index = 0;
            while (index < name.Length && name[index] == '[')
            {
                builder.Append('[');
                index++;
            }
            string component = name[index..];
            if (component.StartsWith("L", StringComparison.Ordinal) && component.EndsWith(";", StringComparison.Ordinal))
            {
                builder.Append('L').Append(component.Substring(1, component.Length - 2).Replace('.', '/')).Append(';');
            }
            else
            {
                builder.Append(component);
            }
            return builder.ToString();
        }
        return "L" + name.Replace('.', '/') + ";";
    }

    private static void RegisterVoid(AndroidApiRegistryBuilder builder, string owner, string name, string descriptor) =>
        builder.Register(Api(owner, name, descriptor), (_, _) => null!);

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static string RequireString(object? value) => value as string ?? throw new ArgumentException("Expected a string.");
    private static int RequireInt(object? value) => value is int i ? i : throw new ArgumentException("Expected an int.");
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
