using System;
using System.Collections.Generic;
using System.Linq;

namespace AndroidRuntime.Core.Dex
{
    /// <summary>
    /// Ordered set of parsed DEX files (classes.dex first, then classes2.dex, ...)
    /// providing merged class/method lookup for cross-file resolution.
    ///
    /// Lookup by string descriptor/name is safe to merge across files because those
    /// keys are strings. Raw pool indexing (Strings/TypeDescriptors/Fields/Methods)
    /// is NEVER merged here: bytecode operand indices are local to the DEX file that
    /// owns the executing method, and <see cref="DexInterpreter"/> resolves them
    /// against <see cref="DexEncodedMethod.OwningDex"/> per call frame.
    ///
    /// Real d8/dx output never defines the same class descriptor in two files, so a
    /// duplicate is treated as a hard fail-closed error instead of silently picking
    /// the first match.
    /// </summary>
    public sealed class DexFileSet
    {
        private readonly List<DexFile> _files;
        private readonly Dictionary<string, DexClass> _classByDescriptor;
        private readonly Dictionary<string, DexEncodedMethod> _methodByExactKey;
        private readonly Dictionary<string, DexEncodedMethod> _methodByName;

        public DexFileSet(IEnumerable<DexFile> files)
        {
            ArgumentNullException.ThrowIfNull(files);
            _files = files.ToList();
            if (_files.Count == 0)
                throw new ArgumentException("A DEX set must contain at least one file.", nameof(files));

            _classByDescriptor = new Dictionary<string, DexClass>(StringComparer.Ordinal);
            _methodByExactKey = new Dictionary<string, DexEncodedMethod>(StringComparer.Ordinal);
            _methodByName = new Dictionary<string, DexEncodedMethod>(StringComparer.Ordinal);

            foreach (DexFile file in _files)
            {
                ArgumentNullException.ThrowIfNull(file);
                foreach (DexClass cls in file.Classes)
                {
                    if (!_classByDescriptor.TryAdd(cls.Descriptor, cls))
                        throw new InvalidDataException(
                            "Duplicate class descriptor across DEX files: " + cls.Descriptor +
                            " (multidex sets must not redefine a class; this is not valid d8/dx output).");
                    foreach (DexEncodedMethod method in cls.AllMethods())
                    {
                        method.OwningDex = file;
                        string exact = method.Method.ClassDescriptor + "->" + method.Method.Name + method.Method.Proto.Descriptor();
                        if (!_methodByExactKey.TryAdd(exact, method))
                            throw new InvalidDataException(
                                "Duplicate method definition across DEX files: " + exact +
                                " (duplicate class descriptors are already rejected; a method must be unique).");
                        string byName = method.Method.ClassDescriptor + "->" + method.Method.Name;
                        if (!_methodByName.ContainsKey(byName))
                            _methodByName[byName] = method;
                    }
                }
            }
        }

        /// <summary>The first (primary) DEX file: classes.dex.</summary>
        public DexFile Primary => _files[0];

        /// <summary>Ordered loaded files: classes.dex first, then numeric secondaries.</summary>
        public IReadOnlyList<DexFile> Files => _files;

        /// <summary>Convenience for the single-file case: a set of exactly one DEX.</summary>
        public static DexFileSet Single(DexFile file) => new(new[] { file });

        /// <summary>Convenience for the single-file case: keeps existing single-Dex callers trivial.</summary>
        public static implicit operator DexFileSet(DexFile file) => Single(file);

        /// <summary>Parses every raw DEX payload in order and wraps the parsed files.</summary>
        public static DexFileSet ParseMany(IEnumerable<byte[]> dexFiles)
        {
            ArgumentNullException.ThrowIfNull(dexFiles);
            return new DexFileSet(dexFiles.Select(DexReader.Parse));
        }

        /// <summary>Searches all files in load order; first match wins.</summary>
        public DexClass FindClass(string descriptor)
        {
            DexClass result;
            _classByDescriptor.TryGetValue(descriptor, out result);
            return result;
        }

        /// <summary>
        /// Searches all files in load order; first match wins. The returned method
        /// always belongs to the file that defines its class (duplicate classes are
        /// rejected), so its bytecode pools are the correct ones for execution.
        /// </summary>
        public DexEncodedMethod FindMethodExact(string classDescriptor, string name, string descriptor)
        {
            DexEncodedMethod result;
            _methodByExactKey.TryGetValue(classDescriptor + "->" + name + descriptor, out result);
            return result;
        }

        /// <summary>Busca por clase+nombre sin importar la firma exacta (conveniencia para el CLI).</summary>
        public DexEncodedMethod FindMethodByName(string classDescriptor, string name)
        {
            DexEncodedMethod result;
            _methodByName.TryGetValue(classDescriptor + "->" + name, out result);
            return result;
        }
    }
}
