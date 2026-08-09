using System.Collections.Generic;
using System.Text;

namespace AndroidRuntime.Core.Dex
{
    /// <summary>Firma de un método: tipo de retorno + tipos de parámetros.</summary>
    public sealed class DexProto
    {
        public string Shorty;
        public string ReturnType;
        public List<string> ParameterTypes = new List<string>();

        public string Descriptor()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            foreach (var p in ParameterTypes) sb.Append(p);
            sb.Append(')');
            sb.Append(ReturnType);
            return sb.ToString();
        }
    }

    /// <summary>Referencia a un método (tabla method_ids del DEX).</summary>
    public sealed class DexMethodRef
    {
        public string ClassDescriptor;
        public string Name;
        public DexProto Proto;

        public override string ToString()
        {
            return ClassDescriptor + "->" + Name + (Proto != null ? Proto.Descriptor() : "()V");
        }
    }

    /// <summary>Referencia a un campo (tabla field_ids del DEX).</summary>
    public sealed class DexFieldRef
    {
        public string ClassDescriptor;
        public string Name;
        public string Type;

        public override string ToString()
        {
            return ClassDescriptor + "->" + Name + ":" + Type;
        }
    }

    /// <summary>Bytecode Dalvik ya desempaquetado de un método (code_item).</summary>
    public sealed class DexCodeItem
    {
        public int RegistersSize;
        public int InsSize;
        public int OutsSize;
        public ushort[] Instructions;
        public List<DexTryBlock> TryBlocks = new();
    }

    public sealed class DexTryBlock
    {
        public int StartAddress;
        public int InstructionCount;
        public List<DexExceptionHandler> Handlers = new();
    }

    public sealed class DexExceptionHandler
    {
        public string TypeDescriptor;
        public int TargetAddress;
        public bool IsCatchAll => TypeDescriptor == null;
    }

    /// <summary>Un método concreto dentro de una clase (direct o virtual).</summary>
    public sealed class DexEncodedMethod
    {
        public DexMethodRef Method;
        public uint AccessFlags;
        public DexCodeItem Code; // null => abstracto / nativo / sin cuerpo

        /// <summary>
        /// DEX file whose pools (string_ids/type_ids/field_ids/method_ids) own this
        /// method's bytecode operand indices. Stamped by <see cref="DexFileSet"/>
        /// at construction; operand indices are local to this file, never global.
        /// </summary>
        public DexFile OwningDex;

        public bool IsStatic { get { return (AccessFlags & DexConstants.ACC_STATIC) != 0; } }
    }

    /// <summary>Una clase completa definida dentro de este DEX.</summary>
    public sealed class DexClass
    {
        public string Descriptor;
        public string SuperclassDescriptor;
        public uint AccessFlags;
        public List<DexEncodedMethod> DirectMethods = new List<DexEncodedMethod>();
        public List<DexEncodedMethod> VirtualMethods = new List<DexEncodedMethod>();
        /// <summary>Interfaces this class implements (or, for an interface, extends),
        /// parsed from the class_def interfaces_off type_list.</summary>
        public List<string> Interfaces = new List<string>();

        public IEnumerable<DexEncodedMethod> AllMethods()
        {
            foreach (var m in DirectMethods) yield return m;
            foreach (var m in VirtualMethods) yield return m;
        }
    }
}
