using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Interface assignability: a concrete class is assignable to an interface it (or
/// any ancestor class) implements, and to an interface that interface extends —
/// walking the guest DEX class_def interfaces_off lists. Malformed/adversarial
/// interface graphs (cycles, absurd depth) fail closed, not hang.
/// </summary>
public sealed class DexInterfaceAssignabilityTests
{
    private const string Owner = "Lx/Owner;";
    private const string Child = "Lx/Child;";
    private const string Impl = "Lx/Impl;";
    private const string Sub = "Lx/Sub;";
    private const string Unrelated = "Lx/Unrelated;";
    private const string Probe = "Lx/Probe;";

    [Fact]
    public void Concrete_class_implementing_an_interface_directly_is_assignable()
    {
        var interpreter = BuildInterpreter(Owner);
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/Owner;)V", new DexObject(Impl)));
    }

    [Fact]
    public void Subclass_inherits_interfaces_through_the_superclass_chain()
    {
        var interpreter = BuildInterpreter(Owner);
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/Owner;)V", new DexObject(Sub)));
    }

    [Fact]
    public void Interface_extending_another_interface_is_transitively_assignable()
    {
        var interpreter = BuildInterpreter(Owner, Child);
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/Owner;)V", new DexObject(Impl)));
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/Child;)V", new DexObject(Impl)));
    }

    [Fact]
    public void Class_without_the_interface_is_rejected()
    {
        var interpreter = BuildInterpreter(Owner);
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Probe, "accept", "(Lx/Owner;)V", new DexObject(Unrelated)));
    }

    [Fact]
    public void Cyclic_interface_graph_terminates_without_hanging()
    {
        var dex = BaseDex();
        dex.Classes.Add(Interface("Lx/CycA;", ["Lx/CycB;"]));
        dex.Classes.Add(Interface("Lx/CycB;", ["Lx/CycA;"]));
        dex.Classes.Add(Concrete("Lx/ImplCyc;", "Ljava/lang/Object;", ["Lx/CycA;"]));
        AddProbe(dex, "Lx/CycA;", "Lx/CycB;");
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/CycA;)V", new DexObject("Lx/ImplCyc;")));
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/CycB;)V", new DexObject("Lx/ImplCyc;")));
        // Non-matching on the same cyclic graph: terminates and rejects, no hang.
        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Probe, "accept", "(Lx/CycA;)V", new DexObject(Unrelated)));
    }

    [Fact]
    public void Absurdly_deep_interface_chain_fails_closed()
    {
        var dex = BaseDex();
        for (int i = 0; i < 140; i++)
            dex.Classes.Add(Interface("Lx/D" + i + ";", i == 139 ? [] : ["Lx/D" + (i + 1) + ";"]));
        dex.Classes.Add(Concrete("Lx/ImplDeep;", "Ljava/lang/Object;", ["Lx/D0;"]));
        AddProbe(dex, "Lx/D50;", "Lx/D139;");
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        // Within the depth cap: resolves.
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Lx/D50;)V", new DexObject("Lx/ImplDeep;")));
        // Past the cap: fails closed, no stack overflow.
        Assert.Throws<InvalidDataException>(() => interpreter.InvokeStaticExact(Probe, "accept", "(Lx/D139;)V", new DexObject("Lx/ImplDeep;")));
    }

    [Fact]
    public void Framework_collection_classes_are_assignable_to_their_interfaces()
    {
        var dex = BaseDex();
        AddProbe(dex, "Ljava/util/Set;", "Ljava/util/Collection;", "Ljava/lang/Iterable;", "Ljava/util/List;", "Ljava/util/Map;");
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        // CopyOnWriteArraySet -> Set, and transitively Collection and Iterable.
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/util/Set;)V", new DexObject("Ljava/util/concurrent/CopyOnWriteArraySet;")));
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/util/Collection;)V", new DexObject("Ljava/util/concurrent/CopyOnWriteArraySet;")));
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/lang/Iterable;)V", new DexObject("Ljava/util/concurrent/CopyOnWriteArraySet;")));
        // ArrayList -> List; HashMap/WeakHashMap -> Map.
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/util/List;)V", new DexObject("Ljava/util/ArrayList;")));
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/util/Map;)V", new DexObject("Ljava/util/HashMap;")));
        Assert.Null(interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/util/Map;)V", new DexObject("Ljava/util/WeakHashMap;")));
    }

    [Fact]
    public void Framework_map_is_not_assignable_to_collection()
    {
        // Map has no super-interface in real Java — it does NOT extend Collection.
        var dex = BaseDex();
        AddProbe(dex, "Ljava/util/Collection;");
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Throws<ArgumentException>(() => interpreter.InvokeStaticExact(Probe, "accept", "(Ljava/util/Collection;)V", new DexObject("Ljava/util/HashMap;")));
    }

    private static DexInterpreter BuildInterpreter(params string[] parameterTypes)
    {
        var dex = BaseDex();
        dex.Classes.Add(Interface(Owner, []));
        dex.Classes.Add(Interface(Child, [Owner]));
        dex.Classes.Add(Concrete(Impl, "Ljava/lang/Object;", [Child]));
        dex.Classes.Add(Concrete(Sub, Impl, []));
        dex.Classes.Add(Concrete(Unrelated, "Ljava/lang/Object;", []));
        AddProbe(dex, parameterTypes);
        dex.BuildIndexes();
        return new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
    }

    private static DexFile BaseDex() => new();

    private static void AddProbe(DexFile dex, params string[] parameterTypes)
    {
        var probe = new DexClass { Descriptor = Probe, SuperclassDescriptor = "Ljava/lang/Object;" };
        foreach (string parameterType in parameterTypes)
        {
            probe.DirectMethods.Add(new DexEncodedMethod
            {
                AccessFlags = DexConstants.ACC_STATIC,
                Method = new DexMethodRef { ClassDescriptor = Probe, Name = "accept", Proto = new DexProto { Shorty = "V", ReturnType = "V", ParameterTypes = [parameterType] } },
                Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 0, Instructions = [0x000e] }
            });
        }
        dex.Classes.Add(probe);
    }

    private static DexClass Interface(string descriptor, params string[] interfaces) =>
        new() { Descriptor = descriptor, SuperclassDescriptor = null, AccessFlags = DexConstants.ACC_INTERFACE, Interfaces = [.. interfaces] };

    private static DexClass Concrete(string descriptor, string superclass, params string[] interfaces) =>
        new() { Descriptor = descriptor, SuperclassDescriptor = superclass, Interfaces = [.. interfaces] };
}
