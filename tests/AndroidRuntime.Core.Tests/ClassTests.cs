using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.lang.Class: canonical per-descriptor Class objects (const-class
/// and Object.getClass() share identity), real Java name formatting (dotted,
/// primitive/array contract), assignability reuse, the superclass chain, and the
/// previously-deferred Enum.getDeclaringClass/valueOf(Class,String) call sites.
/// </summary>
public sealed class ClassTests
{
    private const string Probe = "Lc/Probe;";
    private const string ClassClass = "Ljava/lang/Class;";
    private const string ObjectClass = "Ljava/lang/Object;";
    private const string EnumClass = "Ljava/lang/Enum;";
    private const string PackageClass = "Ljava/lang/Package;";

    [Fact]
    public void Const_class_and_get_class_return_the_same_canonical_object()
    {
        var (state, registry, interpreter) = Session();
        var fooInstance = new DexObject("Lc/Foo;");
        var fromGetClass = Invoke(registry, state, ObjectClass, "getClass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, fooInstance);
        var fromConstClass = interpreter.InvokeStaticExact(Probe, "classOfFoo", "()Ljava/lang/Class;");
        Assert.Same(fromGetClass, fromConstClass);
        // Repeated const-class for the same type yields the exact same object.
        Assert.Same(fromConstClass, interpreter.InvokeStaticExact(Probe, "classOfFoo", "()Ljava/lang/Class;"));
    }

    [Fact]
    public void Get_class_is_stable_across_instances_and_distinct_per_type()
    {
        var (state, registry, _) = Session();
        var first = Invoke(registry, state, ObjectClass, "getClass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, new DexObject("Lc/Foo;"));
        var second = Invoke(registry, state, ObjectClass, "getClass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, new DexObject("Lc/Foo;"));
        Assert.Same(first, second);
        Assert.NotSame(first, Invoke(registry, state, ObjectClass, "getClass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, new DexObject("Lc/Color;")));
    }

    [Fact]
    public void Get_name_and_simple_name_format_correctly()
    {
        var (state, registry, interpreter) = Session();
        var fooClass = interpreter.InvokeStaticExact(Probe, "classOfFoo", "()Ljava/lang/Class;");
        Assert.Equal("c.Foo", Invoke(registry, state, ClassClass, "getName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, fooClass));
        Assert.Equal("c.Foo", Invoke(registry, state, ClassClass, "getCanonicalName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, fooClass));
        Assert.Equal("Foo", Invoke(registry, state, ClassClass, "getSimpleName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, fooClass));
        // getPackage: "Lc/Foo;" -> package "c"; primitives have no package (null).
        var packageObject = Invoke(registry, state, ClassClass, "getPackage", "()Ljava/lang/Package;", AndroidInvokeKind.Virtual, fooClass);
        Assert.NotNull(packageObject);
        Assert.Equal("c", Invoke(registry, state, PackageClass, "getName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, packageObject!));
        Assert.Null(Invoke(registry, state, ClassClass, "getPackage", "()Ljava/lang/Package;", AndroidInvokeKind.Virtual, interpreter.InvokeStaticExact(Probe, "classOfInt", "()Ljava/lang/Class;")));
        Assert.Equal("java.lang.Object", Invoke(registry, state, ClassClass, "getName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, state.EnsureClassObject("Ljava/lang/Object;")));
    }

    [Fact]
    public void Primitive_and_array_class_names_follow_the_real_contract()
    {
        var (state, registry, interpreter) = Session();
        var intClass = interpreter.InvokeStaticExact(Probe, "classOfInt", "()Ljava/lang/Class;");
        var fooArrayClass = interpreter.InvokeStaticExact(Probe, "classOfFooArray", "()Ljava/lang/Class;");
        // Real contract: primitive getName is the keyword; arrays keep the
        // descriptor shape with dotted class components.
        Assert.Equal("int", Invoke(registry, state, ClassClass, "getName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, intClass));
        Assert.Equal("[Lc.Foo;", Invoke(registry, state, ClassClass, "getName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, fooArrayClass));
        Assert.Equal("int", Invoke(registry, state, ClassClass, "getSimpleName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, intClass));
        Assert.Equal("Foo[]", Invoke(registry, state, ClassClass, "getSimpleName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, fooArrayClass));
    }

    [Fact]
    public void Is_instance_and_is_assignable_from_reuse_assignability()
    {
        var (state, registry, interpreter) = Session();
        var fooClass = interpreter.InvokeStaticExact(Probe, "classOfFoo", "()Ljava/lang/Class;");
        var objectClass = state.EnsureClassObject("Ljava/lang/Object;");
        Assert.Equal(1, Invoke(registry, state, ClassClass, "isInstance", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, fooClass, new DexObject("Lc/Foo;")));
        Assert.Equal(0, Invoke(registry, state, ClassClass, "isInstance", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, fooClass, new DexObject("Lc/Color;")));
        Assert.Equal(0, Invoke(registry, state, ClassClass, "isInstance", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, fooClass, null));
        Assert.Equal(1, Invoke(registry, state, ClassClass, "isAssignableFrom", "(Ljava/lang/Class;)Z", AndroidInvokeKind.Virtual, objectClass, fooClass));
        Assert.Equal(0, Invoke(registry, state, ClassClass, "isAssignableFrom", "(Ljava/lang/Class;)Z", AndroidInvokeKind.Virtual, fooClass, objectClass));
    }

    [Fact]
    public void Get_superclass_matches_the_guest_chain()
    {
        var (state, registry, interpreter) = Session();
        var fooClass = interpreter.InvokeStaticExact(Probe, "classOfFoo", "()Ljava/lang/Class;");
        var super = Invoke(registry, state, ClassClass, "getSuperclass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, fooClass);
        Assert.Same(state.EnsureClassObject("Ljava/lang/Object;"), super);
        // Real Java: Object has no superclass -> null.
        Assert.Null(Invoke(registry, state, ClassClass, "getSuperclass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, state.EnsureClassObject("Ljava/lang/Object;")));
    }

    [Fact]
    public void To_string_and_identity_contract()
    {
        var (state, registry, interpreter) = Session();
        var fooClass = interpreter.InvokeStaticExact(Probe, "classOfFoo", "()Ljava/lang/Class;");
        Assert.Equal("class c.Foo", Invoke(registry, state, ClassClass, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, fooClass));
        Assert.Equal(1, Invoke(registry, state, ClassClass, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, fooClass, fooClass));
        Assert.Equal(0, Invoke(registry, state, ClassClass, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, fooClass, state.EnsureClassObject("Ljava/lang/Object;")));
        Assert.Equal(fooClass.GetHashCode(), Invoke(registry, state, ClassClass, "hashCode", "()I", AndroidInvokeKind.Virtual, fooClass));
    }

    [Fact]
    public void Enum_get_declaring_class_and_value_of_close_the_deferred_call_sites()
    {
        var (state, registry, interpreter) = Session();
        // Static access within bytecode runs Color.<clinit>, populating RED/BLUE.
        interpreter.InvokeStaticExact(Probe, "touchColor", "()V");
        var colorClass = interpreter.InvokeStaticExact(Probe, "classOfColor", "()Ljava/lang/Class;");

        var red = Invoke(registry, state, EnumClass, "valueOf", "(Ljava/lang/Class;Ljava/lang/String;)Ljava/lang/Enum;", AndroidInvokeKind.Static, colorClass, "RED");
        Assert.NotNull(red);
        Assert.Equal("RED", Invoke(registry, state, EnumClass, "name", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, red!));
        var blue = Invoke(registry, state, EnumClass, "valueOf", "(Ljava/lang/Class;Ljava/lang/String;)Ljava/lang/Enum;", AndroidInvokeKind.Static, colorClass, "BLUE");
        Assert.NotSame(red, blue);
        // getDeclaringClass returns the canonical Class of the enum instance's type.
        Assert.Same(colorClass, Invoke(registry, state, EnumClass, "getDeclaringClass", "()Ljava/lang/Class;", AndroidInvokeKind.Virtual, red!));
        // Missing constant -> IllegalArgumentException (real contract).
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, EnumClass, "valueOf", "(Ljava/lang/Class;Ljava/lang/String;)Ljava/lang/Enum;", AndroidInvokeKind.Static, colorClass, "NOPE"));
        Assert.Equal("Ljava/lang/IllegalArgumentException;", error.Throwable.TypeDescriptor);
    }

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLogSink()).Build();
        var dex = BuildDex();
        var interpreter = new DexInterpreter(dex, registry, gil: state.Gil);
        state.Gil = interpreter.Gil;
        state.AttachInterpreter(interpreter);
        return (state, registry, interpreter);
    }

    private static DexFile BuildDex()
    {
        var dex = new DexFile();
        // Types: 0 Lc/Color;, 1 Lc/Foo;, 2 I, 3 [Lc/Foo;
        dex.TypeDescriptors.Add("Lc/Color;");
        dex.TypeDescriptors.Add("Lc/Foo;");
        dex.TypeDescriptors.Add("I");
        dex.TypeDescriptors.Add("[Lc/Foo;");
        // Strings: 0 RED, 1 BLUE
        dex.Strings.Add("RED");
        dex.Strings.Add("BLUE");
        // Fields: 0 Color.RED, 1 Color.BLUE
        dex.Fields.Add(Field("Lc/Color;", "RED", "Lc/Color;"));
        dex.Fields.Add(Field("Lc/Color;", "BLUE", "Lc/Color;"));

        var probe = new DexClass { Descriptor = Probe, SuperclassDescriptor = "Ljava/lang/Object;" };
        probe.DirectMethods.Add(Method(Probe, "classOfFoo", "()Ljava/lang/Class;", 1, 0, [0x001c, 0x0001, 0x0011]));
        probe.DirectMethods.Add(Method(Probe, "classOfInt", "()Ljava/lang/Class;", 1, 0, [0x001c, 0x0002, 0x0011]));
        probe.DirectMethods.Add(Method(Probe, "classOfFooArray", "()Ljava/lang/Class;", 1, 0, [0x001c, 0x0003, 0x0011]));
        probe.DirectMethods.Add(Method(Probe, "classOfColor", "()Ljava/lang/Class;", 1, 0, [0x001c, 0x0000, 0x0011]));
        // Static field access to Color.RED: the interpreter's class-init trigger
        // fires on static access WITHIN bytecode (InvokeStaticExact does not), so
        // this runs Color.<clinit> and populates the constants.
        probe.DirectMethods.Add(Method(Probe, "touchColor", "()V", 1, 0, [0x0062, 0x0000, 0x000e]));

        var foo = new DexClass { Descriptor = "Lc/Foo;", SuperclassDescriptor = "Ljava/lang/Object;" };

        // Lc/Color; <clinit> populates RED/BLUE as static fields (the real enum
        // shape: new-instance + Enum.<init>(String,I) + sput-object).
        var color = new DexClass { Descriptor = "Lc/Color;", SuperclassDescriptor = "Ljava/lang/Enum;" };
        color.DirectMethods.Add(Method("Lc/Color;", "<clinit>", "()V", 3, 0,
        [
            0x0022, 0x0000,          // new-instance v0, Color (type idx 0)
            0x011a, 0x0000,          // const-string v1, "RED" (string idx 0)
            0x0212,                   // const/4 v2, 0
            0x3070, 0x0004, 0x0210,  // invoke-direct {v0,v1,v2} Enum.<init>(String,I) (idx 4)
            0x0069, 0x0000,          // sput-object v0, Color.RED (field idx 0)
            0x0022, 0x0000,          // new-instance v0, Color
            0x011a, 0x0001,          // const-string v1, "BLUE" (string idx 1)
            0x1212,                   // const/4 v2, 1
            0x3070, 0x0004, 0x0210,  // invoke-direct {v0,v1,v2} Enum.<init>(String,I)
            0x0069, 0x0001,          // sput-object v0, Color.BLUE (field idx 1)
            0x000e
        ]));
        color.DirectMethods.Add(Method("Lc/Color;", "trigger", "()V", 0, 0, [0x000e]));

        dex.Classes.Add(probe);
        dex.Classes.Add(foo);
        dex.Classes.Add(color);

        // Shared method reference pool.
        dex.Methods.Add(Ref(Probe, "classOfFoo", "()Ljava/lang/Class;"));       // 0
        dex.Methods.Add(Ref(Probe, "classOfInt", "()Ljava/lang/Class;"));       // 1
        dex.Methods.Add(Ref(Probe, "classOfFooArray", "()Ljava/lang/Class;"));  // 2
        dex.Methods.Add(Ref(Probe, "classOfColor", "()Ljava/lang/Class;"));     // 3
        dex.Methods.Add(Ref(EnumClass, "<init>", "(Ljava/lang/String;I)V"));    // 4

        dex.BuildIndexes();
        return dex;
    }

    private static DexFieldRef Field(string owner, string name, string type) => new() { ClassDescriptor = owner, Name = name, Type = type };
    private static DexMethodRef Ref(string owner, string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        var parameters = new List<string>();
        for (int index = 1; index < close;)
        {
            int start = index;
            if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++;
            parameters.Add(descriptor[start..index]);
        }
        return new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
    private static DexEncodedMethod Method(string owner, string name, string descriptor, int registers, int ins, ushort[] instructions, bool isStatic = true) => new()
    {
        AccessFlags = isStatic ? DexConstants.ACC_STATIC : 0,
        Method = Ref(owner, name, descriptor),
        Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 0, Instructions = instructions }
    };

    private static object? Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        // Match the interpreter's guest+framework assignability (a fresh context's
        // default only knows framework edges, not guest superclass chains like
        // "Lc/Color; extends Ljava/lang/Enum;").
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
