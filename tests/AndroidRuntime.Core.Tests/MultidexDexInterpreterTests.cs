using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Focused multidex tests: bytecode operand indices resolve against the DEX file that
/// owns the executing method, while class/method lookup by descriptor merges across all
/// loaded files. A naive global pool merge would fail these tests by resolving the wrong
/// string or dispatching against the wrong file's pools.
/// </summary>
public sealed class MultidexDexInterpreterTests
{
    [Fact]
    public void Bytecode_pool_indices_resolve_against_the_owning_file_not_the_primary()
    {
        // Both files define the identical instruction stream "const-string v0, string@0;
        // return-object v0", but string index 0 means a DIFFERENT string in each file.
        var fileA = DexFileWithStrings("Lexample/A;", ("label", "()Ljava/lang/String;", [0x001a, 0x0000, 0x0011]), "alpha-A");
        var fileB = DexFileWithStrings("Lexample/B;", ("label", "()Ljava/lang/String;", [0x001a, 0x0000, 0x0011]), "beta-B");
        var set = new DexFileSet([fileA, fileB]);
        var interpreter = new DexInterpreter(set, new AndroidApiRegistryBuilder().Build());

        Assert.Equal("alpha-A", interpreter.InvokeStaticExact("Lexample/A;", "label", "()Ljava/lang/String;"));
        Assert.Equal("beta-B", interpreter.InvokeStaticExact("Lexample/B;", "label", "()Ljava/lang/String;"));
    }

    [Fact]
    public void Cross_file_dispatch_resolves_a_method_defined_only_in_a_secondary_dex()
    {
        // File B defines the target. File A only references it through its own method_ids
        // pool (index 0); the invoke operand index is local to file A.
        var fileB = DexFileWithStrings("Lexample/B;", ("make", "()Ljava/lang/String;", [0x001a, 0x0000, 0x0011]), "from-B");
        var refB = Ref("Lexample/B;", "make", "()Ljava/lang/String;");
        var caller = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref("Lexample/A;", "run", "()Ljava/lang/String;"),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 0, OutsSize = 0, Instructions = [0x0071, 0x0000, 0x0000, 0x000c, 0x0011] }
        };
        var fileA = new DexFile();
        fileA.Methods.Add(refB);
        var clsA = new DexClass { Descriptor = "Lexample/A;", SuperclassDescriptor = "Ljava/lang/Object;" };
        clsA.DirectMethods.Add(caller);
        fileA.Classes.Add(clsA);
        fileA.BuildIndexes();

        var set = new DexFileSet([fileA, fileB]);
        var interpreter = new DexInterpreter(set, new AndroidApiRegistryBuilder().Build());

        // Resolution crosses into file B, whose const-string operand must read B's pool.
        Assert.Equal("from-B", interpreter.InvokeStaticExact("Lexample/A;", "run", "()Ljava/lang/String;"));
        // The class defined only in B is discoverable from A through the merged layer.
        Assert.NotNull(set.FindClass("Lexample/B;"));
        Assert.NotNull(set.FindMethodExact("Lexample/B;", "make", "()Ljava/lang/String;"));
        Assert.Null(set.FindClass("Lexample/Missing;"));
    }

    [Fact]
    public void Duplicate_class_descriptor_across_files_is_rejected_fail_closed()
    {
        var fileA = DexFileWithStrings("Lexample/Dup;", ("a", "()I", [0x0012, 0x000f]), "unused");
        var fileB = DexFileWithStrings("Lexample/Dup;", ("b", "()I", [0x0012, 0x000f]), "unused");

        var error = Assert.Throws<InvalidDataException>(() => new DexFileSet([fileA, fileB]));

        Assert.Contains("Lexample/Dup;", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_set_and_null_members_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new DexFileSet(Array.Empty<DexFile>()));
        Assert.Throws<ArgumentNullException>(() => new DexFileSet([null!]));
    }

    private static DexFile DexFileWithStrings(string className, (string Name, string Descriptor, ushort[] Instructions) method, params string[] strings)
    {
        var dex = new DexFile();
        dex.Strings.AddRange(strings);
        var encoded = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref(className, method.Name, method.Descriptor),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 0, OutsSize = 0, Instructions = method.Instructions }
        };
        var cls = new DexClass { Descriptor = className, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.Add(encoded);
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        return dex;
    }

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
}
