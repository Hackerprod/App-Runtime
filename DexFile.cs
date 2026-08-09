using System.Collections.Generic;

namespace AndroidRuntime.Core.Dex
{
    /// <summary>
    /// Representación en memoria de un classes.dex ya parseado: todas las tablas
    /// (strings, tipos, protos, métodos, campos) y todas las clases que define,
    /// junto con índices para resolver llamadas rápidamente durante la ejecución.
    /// </summary>
    public sealed class DexFile
    {
        public List<string> Strings = new List<string>();
        public List<string> TypeDescriptors = new List<string>();
        public List<DexProto> Protos = new List<DexProto>();
        public List<DexFieldRef> Fields = new List<DexFieldRef>();
        public List<DexMethodRef> Methods = new List<DexMethodRef>();
        public List<DexClass> Classes = new List<DexClass>();

        private Dictionary<string, DexClass> _classByDescriptor;
        // clave: descriptor de clase + "->" + nombre + descriptor completo
        private Dictionary<string, DexEncodedMethod> _methodByExactKey;
        // clave: descriptor de clase + "->" + nombre (primer match, para conveniencia)
        private Dictionary<string, DexEncodedMethod> _methodByName;

        public void BuildIndexes()
        {
            _classByDescriptor = new Dictionary<string, DexClass>();
            _methodByExactKey = new Dictionary<string, DexEncodedMethod>();
            _methodByName = new Dictionary<string, DexEncodedMethod>();

            foreach (var c in Classes)
            {
                _classByDescriptor[c.Descriptor] = c;
                foreach (var m in c.AllMethods())
                {
                    string exact = m.Method.ClassDescriptor + "->" + m.Method.Name + m.Method.Proto.Descriptor();
                    _methodByExactKey[exact] = m;

                    string byName = m.Method.ClassDescriptor + "->" + m.Method.Name;
                    if (!_methodByName.ContainsKey(byName))
                        _methodByName[byName] = m;
                }
            }
        }

        public DexClass FindClass(string descriptor)
        {
            DexClass result;
            _classByDescriptor.TryGetValue(descriptor, out result);
            return result;
        }

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
