using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

public sealed class DexRuntimeHardeningTests
{
    private const string Owner = "Lhardening/Probe;";

    [Fact]
    public void Array_opcodes_require_the_exact_component_category()
    {
        Assert.Throws<InvalidOperationException>(() => Run(ArrayMethod("objectFromInt", "([I)Ljava/lang/Object;", 0x0146, 0x0002, 0x0111), new DexArray("[I", 1)));
        Assert.Throws<InvalidOperationException>(() => Run(ArrayMethod("intFromObject", "([Ljava/lang/Object;)I", 0x0144, 0x0002, 0x010f), new DexArray("[Ljava/lang/Object;", 1)));
        Assert.Throws<InvalidOperationException>(() => Run(ArrayMethod("wideFromInt", "([I)J", 0x0145, 0x0002, 0x0110, registers: 3), new DexArray("[I", 1)));
    }

    [Fact]
    public void Reference_arrays_accept_null_and_subtypes_but_reject_unrelated_values()
    {
        var store = Method("store", "([Lhardening/Base;Lhardening/Base;)V", 3, 2, [0x0012, 0x024d, 0x0001, 0x000e]);
        var dex = File(store);
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Base;", SuperclassDescriptor = "Ljava/lang/Object;" });
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Child;", SuperclassDescriptor = "Lhardening/Base;" });
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Other;", SuperclassDescriptor = "Ljava/lang/Object;" });
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        var array = new DexArray("[Lhardening/Base;", 1);

        interpreter.InvokeStaticExact(Owner, "store", "([Lhardening/Base;Lhardening/Base;)V", array, new DexObject("Lhardening/Child;"));
        interpreter.InvokeStaticExact(Owner, "store", "([Lhardening/Base;Lhardening/Base;)V", array, null!);
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Owner, "store", "([Lhardening/Base;Lhardening/Base;)V", array, new DexObject("Lhardening/Other;")));
    }

    [Fact]
    public void Move_result_requires_immediate_adjacency_and_matching_kind()
    {
        var value = Method("value", "()I", 1, 0, [0x1012, 0x000f]);
        var delayed = Method("delayed", "()I", 1, 0, [0x0071, 0, 0, 0x0000, 0x000a, 0x000f]);
        var objectResult = Method("objectResult", "()Ljava/lang/Object;", 1, 0, [0x0071, 0, 0, 0x000c, 0x0011]);
        var missing = Method("missing", "()I", 1, 0, [0x000a, 0x000f]);
        var dex = File(value, delayed, objectResult, missing);
        dex.Methods.Add(value.Method);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Throws<InvalidOperationException>(() => interpreter.InvokeStaticExact(Owner, "delayed", "()I"));
        Assert.Throws<InvalidOperationException>(() => interpreter.InvokeStaticExact(Owner, "objectResult", "()Ljava/lang/Object;"));
        Assert.Throws<InvalidOperationException>(() => interpreter.InvokeStaticExact(Owner, "missing", "()I"));
    }

    [Fact]
    public void Api_array_returns_require_exact_descriptor()
    {
        var api = new AndroidApiMethodId("Lapi/Arrays;", "values", "()[I");
        var registry = new AndroidApiRegistryBuilder().Register(api, (_, _) => new DexArray("[Ljava/lang/Object;", 0)).Build();
        var session = new AndroidApiSessionContext("s", "p", Owner, default, () => true);

        Assert.IsType<InvalidOperationException>(Assert.Throws<AndroidApiBindingException>(() => registry.Invoke(session, new(Owner + "->call()V", 0, api, api, AndroidInvokeKind.Static), [])).InnerException);
    }

    [Fact]
    public void Reference_array_covariance_is_recursive_and_primitive_arrays_are_invariant()
    {
        var childToBase = Method("childToBase", "([Lhardening/Child;)[Lhardening/Base;", 1, 1, [0x0011]);
        var child2ToBase2 = Method("child2ToBase2", "([[Lhardening/Child;)[[Lhardening/Base;", 1, 1, [0x0011]);
        var baseToChild = Method("baseToChild", "([Lhardening/Base;)[Lhardening/Child;", 1, 1, [0x0011]);
        var acceptBase = Method("acceptBase", "([Lhardening/Base;)[Lhardening/Base;", 1, 1, [0x0011]);
        var acceptChild = Method("acceptChild", "([Lhardening/Child;)[Lhardening/Child;", 1, 1, [0x0011]);
        var ints = Method("ints", "([I)[I", 1, 1, [0x0011]);
        var asObject = Method("asObject", "(Ljava/lang/Object;)Ljava/lang/Object;", 1, 1, [0x0011]);
        var asCloneable = Method("asCloneable", "(Ljava/lang/Cloneable;)Ljava/lang/Cloneable;", 1, 1, [0x0011]);
        var asSerializable = Method("asSerializable", "(Ljava/io/Serializable;)Ljava/io/Serializable;", 1, 1, [0x0011]);
        var mixed = Method("mixed", "([Ljava/lang/Object;)[Ljava/lang/Object;", 1, 1, [0x0011]);
        var framework = Method("framework", "([Landroid/content/Context;)[Landroid/content/Context;", 1, 1, [0x0011]);
        var dex = File(childToBase, child2ToBase2, baseToChild, acceptBase, acceptChild, ints, asObject, asCloneable, asSerializable, mixed, framework);
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Base;", SuperclassDescriptor = "Ljava/lang/Object;" });
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Child;", SuperclassDescriptor = "Lhardening/Base;" });
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        var childArray = new DexArray("[Lhardening/Child;", 0);
        Assert.Same(childArray, interpreter.InvokeStaticExact(Owner, "childToBase", "([Lhardening/Child;)[Lhardening/Base;", childArray));
        Assert.Same(childArray, interpreter.InvokeStaticExact(Owner, "acceptBase", "([Lhardening/Base;)[Lhardening/Base;", childArray));
        Assert.IsType<DexArray>(interpreter.InvokeStaticExact(Owner, "child2ToBase2", "([[Lhardening/Child;)[[Lhardening/Base;", new DexArray("[[Lhardening/Child;", 0)));
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Owner, "acceptChild", "([Lhardening/Child;)[Lhardening/Child;", new DexArray("[Lhardening/Base;", 0)));
        Assert.Throws<InvalidOperationException>(() => interpreter.InvokeStaticExact(Owner, "baseToChild", "([Lhardening/Base;)[Lhardening/Child;", new DexArray("[Lhardening/Base;", 0)));
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Owner, "ints", "([I)[I", new DexArray("[J", 0)));
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Owner, "ints", "([I)[I", new DexArray("[Ljava/lang/Object;", 0)));
        Assert.IsType<DexArray>(interpreter.InvokeStaticExact(Owner, "asObject", "(Ljava/lang/Object;)Ljava/lang/Object;", new DexArray("[I", 0)));
        Assert.IsType<DexArray>(interpreter.InvokeStaticExact(Owner, "asCloneable", "(Ljava/lang/Cloneable;)Ljava/lang/Cloneable;", new DexArray("[I", 0)));
        Assert.IsType<DexArray>(interpreter.InvokeStaticExact(Owner, "asSerializable", "(Ljava/io/Serializable;)Ljava/io/Serializable;", new DexArray("[I", 0)));
        Assert.IsType<DexArray>(interpreter.InvokeStaticExact(Owner, "mixed", "([Ljava/lang/Object;)[Ljava/lang/Object;", new DexArray("[[Lhardening/Child;", 0)));
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Owner, "child2ToBase2", "([[Lhardening/Child;)[[Lhardening/Base;", new DexArray("[Lhardening/Child;", 0)));
        Assert.IsType<DexArray>(interpreter.InvokeStaticExact(Owner, "framework", "([Landroid/content/Context;)[Landroid/content/Context;", new DexArray("[Landroid/app/Activity;", 0)));
    }

    [Fact]
    public void Aput_object_accepts_covariant_nested_array_elements_and_rejects_inverse()
    {
        var storeBase = Method("storeBase", "([[Lhardening/Base;[Lhardening/Base;)V", 3, 2, [0x0012, 0x024d, 0x0001, 0x000e]);
        var storeChild = Method("storeChild", "([[Lhardening/Child;[Lhardening/Child;)V", 3, 2, [0x0012, 0x024d, 0x0001, 0x000e]);
        var dex = File(storeBase, storeChild);
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Base;", SuperclassDescriptor = "Ljava/lang/Object;" });
        dex.Classes.Add(new DexClass { Descriptor = "Lhardening/Child;", SuperclassDescriptor = "Lhardening/Base;" });
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        interpreter.InvokeStaticExact(Owner, "storeBase", "([[Lhardening/Base;[Lhardening/Base;)V", new DexArray("[[Lhardening/Base;", 1), new DexArray("[Lhardening/Child;", 0));
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Owner, "storeChild", "([[Lhardening/Child;[Lhardening/Child;)V", new DexArray("[[Lhardening/Child;", 1), new DexArray("[Lhardening/Base;", 0)));
    }

    [Fact]
    public void Clr_array_descriptors_cover_full_primitive_matrix_jagged_rectangular_and_guest_wrappers()
    {
        var longReturn = new AndroidApiMethodId("Lapi/Arrays;", "longs", "()[[J");
        var doubleReturn = new AndroidApiMethodId("Lapi/Arrays;", "doubles", "()[[D");
        var booleanReturn = new AndroidApiMethodId("Lapi/Arrays;", "booleans", "()[[Z");
        var rectangularReturn = new AndroidApiMethodId("Lapi/Arrays;", "rectangular", "()[[J");
        var nestedLongReturn = new AndroidApiMethodId("Lapi/Arrays;", "nestedLongs", "()[[[J");
        var nestedReferenceReturn = new AndroidApiMethodId("Lapi/Arrays;", "nestedReferences", "()[[Ljava/lang/Object;");
        var objectReturn = new AndroidApiMethodId("Lapi/Arrays;", "object", "()Ljava/lang/Object;");
        var cloneableReturn = new AndroidApiMethodId("Lapi/Arrays;", "cloneable", "()Ljava/lang/Cloneable;");
        var serializableReturn = new AndroidApiMethodId("Lapi/Arrays;", "serializable", "()Ljava/io/Serializable;");
        var dexObjectsReturn = new AndroidApiMethodId("Lapi/Arrays;", "dexObjects", "()[Ljava/lang/Object;");
        var dexArraysReturn = new AndroidApiMethodId("Lapi/Arrays;", "dexArrays", "()[[Ljava/lang/Object;");
        var acceptLongs = new AndroidApiMethodId("Lapi/Arrays;", "acceptLongs", "([[J)I");
        var acceptObject = new AndroidApiMethodId("Lapi/Arrays;", "acceptObject", "(Ljava/lang/Object;)I");
        var acceptCloneable = new AndroidApiMethodId("Lapi/Arrays;", "acceptCloneable", "(Ljava/lang/Cloneable;)I");
        var acceptSerializable = new AndroidApiMethodId("Lapi/Arrays;", "acceptSerializable", "(Ljava/io/Serializable;)I");
        var registry = new AndroidApiRegistryBuilder()
            .Register(longReturn, (_, _) => new long[1][])
            .Register(doubleReturn, (_, _) => new double[1][])
            .Register(booleanReturn, (_, _) => new bool[1][])
            .Register(rectangularReturn, (_, _) => new long[1, 1])
            .Register(nestedLongReturn, (_, _) => new long[1][][])
            .Register(nestedReferenceReturn, (_, _) => new string[1][])
            .Register(objectReturn, (_, _) => new long[1][])
            .Register(cloneableReturn, (_, _) => new long[1][])
            .Register(serializableReturn, (_, _) => new long[1][])
            .Register(dexObjectsReturn, (_, _) => Array.Empty<DexObject>())
            .Register(dexArraysReturn, (_, _) => Array.Empty<DexArray>())
            .Register(acceptLongs, (_, _) => 1)
            .Register(acceptObject, (_, _) => 2)
            .Register(acceptCloneable, (_, _) => 3)
            .Register(acceptSerializable, (_, _) => 4)
            .Build();
        var session = new AndroidApiSessionContext("arrays", "pkg", Owner, default, () => true);

        Assert.IsType<long[][]>(Invoke(registry, session, longReturn));
        Assert.IsType<double[][]>(Invoke(registry, session, doubleReturn));
        Assert.IsType<bool[][]>(Invoke(registry, session, booleanReturn));
        Assert.IsType<InvalidOperationException>(Assert.Throws<AndroidApiBindingException>(() => Invoke(registry, session, rectangularReturn)).InnerException);
        Assert.IsType<long[][][]>(Invoke(registry, session, nestedLongReturn));
        Assert.IsType<string[][]>(Invoke(registry, session, nestedReferenceReturn));
        Assert.IsType<long[][]>(Invoke(registry, session, objectReturn));
        Assert.IsType<long[][]>(Invoke(registry, session, cloneableReturn));
        Assert.IsType<long[][]>(Invoke(registry, session, serializableReturn));
        Assert.IsType<DexObject[]>(Invoke(registry, session, dexObjectsReturn));
        Assert.IsType<DexArray[]>(Invoke(registry, session, dexArraysReturn));
        Assert.Equal(1, Invoke(registry, session, acceptLongs, (object)new long[1][]));
        Assert.Throws<ArgumentException>(() => Invoke(registry, session, acceptLongs, new long[1, 1]));
        Assert.Throws<ArgumentException>(() => Invoke(registry, session, acceptLongs, (object)new double[1][]));
        Assert.Equal(2, Invoke(registry, session, acceptObject, (object)new long[1][]));
        Assert.Equal(3, Invoke(registry, session, acceptCloneable, (object)new long[1][]));
        Assert.Equal(4, Invoke(registry, session, acceptSerializable, (object)new long[1][]));
    }

    [Fact]
    public void Aget_object_reads_a_jagged_primitive_row_as_a_reference_array()
    {
        var row = new DexArray("[J", 1);
        row.SetWide(0, 42);
        var outer = new DexArray("[[J", 1);
        outer.Set(0, row);
        var readRow = ArrayMethod("readRow", "([[J)[J", 0x0146, 0x0002, 0x0111);

        Assert.Same(row, Run(readRow, outer));
    }

    private static object Run(DexEncodedMethod method, params object[] args) => new DexInterpreter(File(method), new AndroidApiRegistryBuilder().Build()).InvokeStaticExact(Owner, method.Method.Name, method.Method.Proto.Descriptor(), args);
    private static object Invoke(AndroidApiRegistry registry, AndroidApiSessionContext session, AndroidApiMethodId api, params object[] args) => registry.Invoke(session, new(Owner + "->test()V", 0, api, api, AndroidInvokeKind.Static), args);
    private static DexEncodedMethod ArrayMethod(string name, string descriptor, ushort arrayOpcode, ushort operands, ushort ret, int registers = 3) => Method(name, descriptor, registers, 1, [0x0012, arrayOpcode, operands, ret]);
    private static DexEncodedMethod Method(string name, string descriptor, int registers, int ins, ushort[] instructions) => new() { AccessFlags = DexConstants.ACC_STATIC, Method = Ref(name, descriptor), Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 2, Instructions = instructions } };
    private static DexFile File(params DexEncodedMethod[] methods) { var dex = new DexFile(); var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" }; cls.DirectMethods.AddRange(methods); dex.Classes.Add(cls); dex.BuildIndexes(); return dex; }
    private static DexMethodRef Ref(string name, string descriptor)
    {
        int close = descriptor.IndexOf(')'); var parameters = new List<string>();
        for (int index = 1; index < close;) { int start = index; while (descriptor[index] == '[') index++; if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++; parameters.Add(descriptor[start..index]); }
        return new DexMethodRef { ClassDescriptor = Owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
}
