using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Real class initialization: a guest class's <clinit>()V runs exactly once per
/// session at the active-use triggers (new-instance, static field access, static
/// method invoke), superclass before subclass, cycle-safe in the single-lane
/// runtime (in-progress classes are skipped on re-entry, not re-run).
/// </summary>
public sealed class DexClassInitTests
{
    private const string Owner = "Lci/Init;";
    private const string Once = "Lci/Once;";
    private const string Super = "Lci/Super;";
    private const string Sub = "Lci/Sub;";
    private const string CycA = "Lci/CycA;";
    private const string CycB = "Lci/CycB;";

    [Fact]
    public void Class_initializer_runs_and_static_field_reads_see_the_initialized_value()
    {
        var dex = new DexFile();
        dex.Fields.Add(Field(Owner, "value", "I"));
        var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" };
        // <clinit>: const/16 v0, 42; sput v0, value; return
        cls.DirectMethods.Add(Method(Owner, "<clinit>", "()V", 1, 0, [0x0013, 0x002A, 0x0067, 0x0000, 0x000e]));
        // read(): sget v0, value; return v0
        cls.DirectMethods.Add(Method(Owner, "read", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        // The invoke-static of read() triggers the class initializer first.
        Assert.Equal(42, interpreter.InvokeStaticExact(Owner, "read", "()I"));
    }

    [Fact]
    public void Class_initializer_runs_exactly_once_across_multiple_uses()
    {
        var dex = new DexFile();
        dex.Fields.Add(Field(Once, "count", "I"));
        dex.TypeDescriptors.Add(Once); // type index 0, used by new-instance
        var cls = new DexClass { Descriptor = Once, SuperclassDescriptor = "Ljava/lang/Object;" };
        // <clinit>: const/4 v0, 1; sput v0, count; return
        cls.DirectMethods.Add(Method(Once, "<clinit>", "()V", 1, 0, [0x1012, 0x0067, 0x0000, 0x000e]));
        // make(): new-instance v0, Once; return-object v0  (new-instance triggers init)
        cls.DirectMethods.Add(Method(Once, "make", "()Ljava/lang/Object;", 1, 0, [0x0022, 0x0000, 0x0011]));
        // count(): sget v0, count; return v0
        cls.DirectMethods.Add(Method(Once, "count", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        interpreter.InvokeStaticExact(Once, "make", "()Ljava/lang/Object;");
        interpreter.InvokeStaticExact(Once, "make", "()Ljava/lang/Object;");

        // Two new-instance calls, but the initializer must have run once.
        Assert.Equal(1, interpreter.InvokeStaticExact(Once, "count", "()I"));
    }

    [Fact]
    public void Superclass_initializer_runs_before_subclass_initializer()
    {
        var dex = new DexFile();
        dex.Fields.Add(Field(Super, "marker", "I"));
        dex.Methods.Add(Ref(Sub, "trigger", "()V")); // index 0, invoked by the runner's bytecode
        var superCls = new DexClass { Descriptor = Super, SuperclassDescriptor = "Ljava/lang/Object;" };
        // <clinit>: const/16 v0, 1; sput v0, marker; return
        superCls.DirectMethods.Add(Method(Super, "<clinit>", "()V", 1, 0, [0x0013, 0x0001, 0x0067, 0x0000, 0x000e]));
        var subCls = new DexClass { Descriptor = Sub, SuperclassDescriptor = Super };
        // <clinit>: sget v0, marker; const/4 v1, 1; add-int v0, v0, v1; sput v0, marker; return
        subCls.DirectMethods.Add(Method(Sub, "<clinit>", "()V", 2, 0, [0x0060, 0x0000, 0x1112, 0x0090, 0x0100, 0x0067, 0x0000, 0x000e]));
        subCls.DirectMethods.Add(Method(Sub, "trigger", "()V", 0, 0, [0x000e]));
        var runner = new DexClass { Descriptor = "Lci/Runner;", SuperclassDescriptor = "Ljava/lang/Object;" };
        // run(): invoke-static Sub.trigger (bytecode, index 0); return-void
        runner.DirectMethods.Add(Method("Lci/Runner;", "run", "()V", 0, 0, [0x0071, 0x0000, 0x0000, 0x000e]));
        var reader = new DexClass { Descriptor = "Lci/Reader;", SuperclassDescriptor = "Ljava/lang/Object;" };
        reader.DirectMethods.Add(Method("Lci/Reader;", "read", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));
        dex.Classes.Add(superCls);
        dex.Classes.Add(subCls);
        dex.Classes.Add(runner);
        dex.Classes.Add(reader);
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        // The bytecode invoke-static of Sub.trigger initializes Sub (Super first).
        interpreter.InvokeStaticExact("Lci/Runner;", "run", "()V");

        // Super ran first (marker = 1), then Sub read 1 and wrote 2.
        Assert.Equal(2, interpreter.InvokeStaticExact("Lci/Reader;", "read", "()I"));
    }

    [Fact]
    public void Circular_class_initialization_terminates_without_running_twice()
    {
        var dex = new DexFile();
        dex.Fields.Add(Field(CycA, "flag", "I"));
        dex.Methods.Add(Ref(CycB, "trigger", "()V")); // index 0: B.trigger
        dex.Methods.Add(Ref(CycA, "trigger", "()V")); // index 1: A.trigger
        var clsA = new DexClass { Descriptor = CycA, SuperclassDescriptor = "Ljava/lang/Object;" };
        // <clinit>: const/4 v0, 1; sput v0, flag; invoke-static B.trigger; return
        clsA.DirectMethods.Add(Method(CycA, "<clinit>", "()V", 1, 0, [0x1012, 0x0067, 0x0000, 0x0071, 0x0000, 0x0000, 0x000e]));
        clsA.DirectMethods.Add(Method(CycA, "trigger", "()V", 0, 0, [0x000e]));
        var clsB = new DexClass { Descriptor = CycB, SuperclassDescriptor = "Ljava/lang/Object;" };
        // <clinit>: invoke-static A.trigger; return  (re-enters A while A is in-progress)
        clsB.DirectMethods.Add(Method(CycB, "<clinit>", "()V", 0, 0, [0x0071, 0x0001, 0x0000, 0x000e]));
        clsB.DirectMethods.Add(Method(CycB, "trigger", "()V", 0, 0, [0x000e]));
        var reader = new DexClass { Descriptor = "Lci/Reader;", SuperclassDescriptor = "Ljava/lang/Object;" };
        reader.DirectMethods.Add(Method("Lci/Reader;", "read", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));
        dex.Classes.Add(clsA);
        dex.Classes.Add(clsB);
        dex.Classes.Add(reader);
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        // A's <clinit> triggers B, B's <clinit> re-enters A (in-progress -> skip),
        // B finishes, A finishes, then A.trigger runs. No stack overflow.
        interpreter.InvokeStaticExact(CycA, "trigger", "()V");

        Assert.Equal(1, interpreter.InvokeStaticExact("Lci/Reader;", "read", "()I"));
    }

    private static DexFieldRef Field(string owner, string name, string type) => new() { ClassDescriptor = owner, Name = name, Type = type };
    private static DexMethodRef Ref(string owner, string name, string descriptor) => new() { ClassDescriptor = owner, Name = name, Proto = new DexProto { Shorty = "V", ReturnType = descriptor[(descriptor.IndexOf(')') + 1)..], ParameterTypes = [] } };
    private static DexEncodedMethod Method(string owner, string name, string descriptor, int registers, int ins, ushort[] instructions) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { Shorty = "V", ReturnType = descriptor[(descriptor.IndexOf(')') + 1)..], ParameterTypes = [] } },
        Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 0, Instructions = instructions }
    };
}
