using System;
using System.Text;

namespace AndroidRuntime.Core.Dex
{
    /// <summary>
    /// Lee un archivo classes.dex (formato binario "Dalvik Executable") y lo convierte
    /// en un <see cref="DexFile"/> navegable. Implementa únicamente lectura: no genera
    /// dex, no valida bytecode "verificable" como haría el runtime real de Android,
    /// y confía en los offsets que trae el propio header en vez de reconstruir
    /// map_list (que es opcional para simplemente leer los datos que necesitamos).
    /// </summary>
    public static class DexReader
    {
        public static DexFile Parse(byte[] data)
        {
            if (data == null || data.Length < 0x70)
                throw new FormatException("El archivo es demasiado pequeño para ser un DEX válido.");

            if (data[0] != (byte)'d' || data[1] != (byte)'e' || data[2] != (byte)'x' || data[3] != (byte)'\n')
                throw new FormatException("Magic number inválido: no parece un archivo classes.dex.");

            try
            {
            uint stringIdsSize = U32(data, 56);
            uint stringIdsOff = U32(data, 60);
            uint typeIdsSize = U32(data, 64);
            uint typeIdsOff = U32(data, 68);
            uint protoIdsSize = U32(data, 72);
            uint protoIdsOff = U32(data, 76);
            uint fieldIdsSize = U32(data, 80);
            uint fieldIdsOff = U32(data, 84);
            uint methodIdsSize = U32(data, 88);
            uint methodIdsOff = U32(data, 92);
            uint classDefsSize = U32(data, 96);
            uint classDefsOff = U32(data, 100);

            var dex = new DexFile();

            // ---- strings ----
            for (uint i = 0; i < stringIdsSize; i++)
            {
                uint dataOff = U32(data, (int)(stringIdsOff + i * 4));
                int cursor = (int)dataOff;
                ReadUleb128(data, ref cursor); // utf16_size, no lo necesitamos: usamos el NUL final
                dex.Strings.Add(ReadMutf8UntilNul(data, ref cursor));
            }

            // ---- types ----
            for (uint i = 0; i < typeIdsSize; i++)
            {
                uint descIdx = U32(data, (int)(typeIdsOff + i * 4));
                dex.TypeDescriptors.Add(dex.Strings[(int)descIdx]);
            }

            // ---- protos ----
            for (uint i = 0; i < protoIdsSize; i++)
            {
                int baseOff = (int)(protoIdsOff + i * 12);
                uint shortyIdx = U32(data, baseOff);
                uint returnTypeIdx = U32(data, baseOff + 4);
                uint paramsOff = U32(data, baseOff + 8);

                var proto = new DexProto();
                proto.Shorty = dex.Strings[(int)shortyIdx];
                proto.ReturnType = dex.TypeDescriptors[(int)returnTypeIdx];
                if (paramsOff != 0) ReadTypeList(data, paramsOff, dex, proto.ParameterTypes);
                dex.Protos.Add(proto);
            }

            // ---- fields ----
            for (uint i = 0; i < fieldIdsSize; i++)
            {
                int baseOff = (int)(fieldIdsOff + i * 8);
                ushort classIdx = U16(data, baseOff);
                ushort typeIdx = U16(data, baseOff + 2);
                uint nameIdx = U32(data, baseOff + 4);

                var f = new DexFieldRef();
                f.ClassDescriptor = dex.TypeDescriptors[classIdx];
                f.Type = dex.TypeDescriptors[typeIdx];
                f.Name = dex.Strings[(int)nameIdx];
                dex.Fields.Add(f);
            }

            // ---- methods ----
            for (uint i = 0; i < methodIdsSize; i++)
            {
                int baseOff = (int)(methodIdsOff + i * 8);
                ushort classIdx = U16(data, baseOff);
                ushort protoIdx = U16(data, baseOff + 2);
                uint nameIdx = U32(data, baseOff + 4);

                var m = new DexMethodRef();
                m.ClassDescriptor = dex.TypeDescriptors[classIdx];
                m.Proto = dex.Protos[protoIdx];
                m.Name = dex.Strings[(int)nameIdx];
                dex.Methods.Add(m);
            }

            // ---- class defs ----
            for (uint i = 0; i < classDefsSize; i++)
            {
                int baseOff = (int)(classDefsOff + i * 32);
                uint classIdx = U32(data, baseOff);
                uint accessFlags = U32(data, baseOff + 4);
                uint superclassIdx = U32(data, baseOff + 8);
                uint interfacesOff = U32(data, baseOff + 12);
                uint classDataOff = U32(data, baseOff + 24);

                var cls = new DexClass();
                cls.Descriptor = dex.TypeDescriptors[(int)classIdx];
                cls.AccessFlags = accessFlags;
                cls.SuperclassDescriptor = superclassIdx == DexConstants.NO_INDEX
                    ? null
                    : dex.TypeDescriptors[(int)superclassIdx];
                if (interfacesOff != 0)
                    ReadTypeList(data, interfacesOff, dex, cls.Interfaces);

                if (classDataOff != 0)
                {
                    ParseClassData(data, (int)classDataOff, dex, cls);
                }

                dex.Classes.Add(cls);
            }

            dex.BuildIndexes();
            return dex;
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                throw new FormatException("The DEX is truncated or contains an out-of-range table offset.", error);
            }
        }

        private static void ParseClassData(byte[] data, int offset, DexFile dex, DexClass cls)
        {
            int cursor = offset;
            uint staticFieldsSize = ReadUleb128(data, ref cursor);
            uint instanceFieldsSize = ReadUleb128(data, ref cursor);
            uint directMethodsSize = ReadUleb128(data, ref cursor);
            uint virtualMethodsSize = ReadUleb128(data, ref cursor);

            // encoded_field: solo necesitamos avanzar el cursor correctamente; no
            // guardamos valores estáticos iniciales en este prototipo.
            for (uint i = 0; i < staticFieldsSize; i++)
            {
                ReadUleb128(data, ref cursor); // field_idx_diff
                ReadUleb128(data, ref cursor); // access_flags
            }
            for (uint i = 0; i < instanceFieldsSize; i++)
            {
                ReadUleb128(data, ref cursor); // field_idx_diff
                ReadUleb128(data, ref cursor); // access_flags
            }

            uint methodIdx = 0;
            for (uint i = 0; i < directMethodsSize; i++)
            {
                methodIdx += ReadUleb128(data, ref cursor);
                uint access = ReadUleb128(data, ref cursor);
                uint codeOff = ReadUleb128(data, ref cursor);
                cls.DirectMethods.Add(BuildEncodedMethod(data, dex, methodIdx, access, codeOff));
            }

            methodIdx = 0;
            for (uint i = 0; i < virtualMethodsSize; i++)
            {
                methodIdx += ReadUleb128(data, ref cursor);
                uint access = ReadUleb128(data, ref cursor);
                uint codeOff = ReadUleb128(data, ref cursor);
                cls.VirtualMethods.Add(BuildEncodedMethod(data, dex, methodIdx, access, codeOff));
            }
        }

        private static DexEncodedMethod BuildEncodedMethod(byte[] data, DexFile dex, uint methodIdx, uint access, uint codeOff)
        {
            var enc = new DexEncodedMethod();
            enc.Method = dex.Methods[(int)methodIdx];
            enc.AccessFlags = access;
            if (codeOff != 0)
                enc.Code = ParseCodeItem(data, (int)codeOff, dex);
            return enc;
        }

        private static DexCodeItem ParseCodeItem(byte[] data, int offset, DexFile dex)
        {
            const int MaxTryBlocks = 1024;
            const int MaxHandlers = 4096;
            RequireRange(data, offset, 16, "code_item header");
            var item = new DexCodeItem();
            item.RegistersSize = U16(data, offset);
            item.InsSize = U16(data, offset + 2);
            item.OutsSize = U16(data, offset + 4);
            int triesSize = U16(data, offset + 6);
            if (triesSize > MaxTryBlocks) throw new FormatException("DEX code_item exceeds the try-block quota.");
            uint insnsSizeUnits = U32(data, offset + 12);
            if (insnsSizeUnits > int.MaxValue) throw new FormatException("DEX instruction count is too large.");
            long instructionBytes = checked((long)insnsSizeUnits * 2);
            RequireRange(data, offset + 16, instructionBytes, "code_item instructions");
            var insns = new ushort[insnsSizeUnits];
            int insnsOff = offset + 16;
            for (uint i = 0; i < insnsSizeUnits; i++)
                insns[i] = U16(data, (int)(insnsOff + i * 2));
            item.Instructions = insns;
            if (triesSize == 0) return item;

            HashSet<int> boundaries = DecodeInstructionBoundaries(insns);
            int paddingBytes = ((int)insnsSizeUnits & 1) * 2;
            int paddingOffset = checked(insnsOff + (int)instructionBytes);
            if (paddingBytes != 0)
            {
                RequireRange(data, paddingOffset, 2, "code_item padding");
                if (U16(data, paddingOffset) != 0) throw new FormatException("DEX code_item padding must be zero.");
            }
            int triesOffset = checked(paddingOffset + paddingBytes);
            RequireRange(data, triesOffset, checked((long)triesSize * 8), "try_item array");
            int handlersOffset = checked(triesOffset + triesSize * 8);
            int cursor = handlersOffset;
            uint handlerListSize = ReadUleb128Bounded(data, ref cursor, "encoded_catch_handler_list size");
            if (handlerListSize > MaxHandlers) throw new FormatException("DEX code_item exceeds the exception-handler quota.");
            var handlersByOffset = new Dictionary<int, List<DexExceptionHandler>>();
            int totalHandlers = 0;
            for (uint i = 0; i < handlerListSize; i++)
            {
                int relativeOffset = cursor - handlersOffset;
                int signedSize = ReadSleb128Bounded(data, ref cursor, "encoded_catch_handler size");
                if (signedSize == int.MinValue) throw new FormatException("Invalid encoded_catch_handler size.");
                int typedCount = Math.Abs(signedSize);
                if (totalHandlers > MaxHandlers - typedCount - (signedSize <= 0 ? 1 : 0)) throw new FormatException("DEX code_item exceeds the exception-handler quota.");
                var handlers = new List<DexExceptionHandler>(typedCount + 1);
                for (int h = 0; h < typedCount; h++)
                {
                    uint typeIndex = ReadUleb128Bounded(data, ref cursor, "exception type index");
                    uint address = ReadUleb128Bounded(data, ref cursor, "exception handler address");
                    if (typeIndex >= dex.TypeDescriptors.Count) throw new FormatException("Exception handler type index is out of range.");
                    ValidateHandlerTarget(insns, boundaries, address);
                    handlers.Add(new DexExceptionHandler { TypeDescriptor = dex.TypeDescriptors[(int)typeIndex], TargetAddress = (int)address });
                }
                if (signedSize <= 0)
                {
                    uint address = ReadUleb128Bounded(data, ref cursor, "catch-all handler address");
                    ValidateHandlerTarget(insns, boundaries, address);
                    handlers.Add(new DexExceptionHandler { TypeDescriptor = null, TargetAddress = (int)address });
                }
                totalHandlers += handlers.Count;
                if (!handlersByOffset.TryAdd(relativeOffset, handlers)) throw new FormatException("Duplicate encoded exception-handler offset.");
            }

            int previousEnd = -1;
            for (int i = 0; i < triesSize; i++)
            {
                int tryOffset = triesOffset + i * 8;
                uint start = U32(data, tryOffset);
                int count = U16(data, tryOffset + 4);
                int handlerOffset = U16(data, tryOffset + 6);
                long end = (long)start + count;
                if (count == 0 || end > insns.Length || !boundaries.Contains((int)start) || (end != insns.Length && !boundaries.Contains((int)end)))
                    throw new FormatException("DEX try_item range is invalid or not instruction-aligned.");
                if (start < previousEnd) throw new FormatException("DEX try_item ranges overlap or are out of order.");
                if (!handlersByOffset.TryGetValue(handlerOffset, out var handlers)) throw new FormatException("DEX try_item handler_off does not identify an encoded handler.");
                item.TryBlocks.Add(new DexTryBlock { StartAddress = (int)start, InstructionCount = count, Handlers = new List<DexExceptionHandler>(handlers) });
                previousEnd = (int)end;
            }
            return item;
        }

        private static void ValidateHandlerTarget(ushort[] instructions, HashSet<int> boundaries, uint address)
        {
            if (address >= instructions.Length || !boundaries.Contains((int)address)) throw new FormatException("Exception handler target is out of range or not instruction-aligned.");
            if ((instructions[address] & 0xff) != 0x0d) throw new FormatException("Exception handler must begin with move-exception.");
        }

        private static HashSet<int> DecodeInstructionBoundaries(ushort[] instructions)
        {
            var result = new HashSet<int>();
            for (int pc = 0; pc < instructions.Length;)
            {
                result.Add(pc);
                int width = InstructionWidth(instructions, pc);
                if (width <= 0 || pc > instructions.Length - width) throw new FormatException("DEX instruction stream is truncated or malformed.");
                pc += width;
            }
            return result;
        }

        internal static int InstructionWidth(ushort[] instructions, int pc)
        {
            ushort first = instructions[pc];
            int op = first & 0xff;
            if (!DexOpcodeTable.TryGetFormat(first, out DexInstructionFormat format))
                throw new DexUnsupportedInstructionException(op);
            if (format == DexInstructionFormat.Payload)
            {
                int kind = first >> 8;
                if (kind == 1) { RequireUnits(instructions, pc, 2); return checked(4 + instructions[pc + 1] * 2); }
                if (kind == 2) { RequireUnits(instructions, pc, 2); return checked(2 + instructions[pc + 1] * 4); }
                if (kind == 3) { RequireUnits(instructions, pc, 4); uint size = (uint)(instructions[pc + 2] | instructions[pc + 3] << 16); int elementWidth = instructions[pc + 1]; return checked(4 + (int)((size * elementWidth + 1) / 2)); }
                throw new FormatException("Unknown DEX payload pseudo-instruction.");
            }
            return format switch
            {
                DexInstructionFormat.F10x or DexInstructionFormat.F12x or DexInstructionFormat.F11x or DexInstructionFormat.F11n or DexInstructionFormat.F10t => 1,
                DexInstructionFormat.F21 or DexInstructionFormat.F22x or DexInstructionFormat.F22c or DexInstructionFormat.F22t or DexInstructionFormat.F21t or DexInstructionFormat.F23x or DexInstructionFormat.F20t or DexInstructionFormat.F22s or DexInstructionFormat.F22b => 2,
                DexInstructionFormat.F31 or DexInstructionFormat.F32x or DexInstructionFormat.F30t or DexInstructionFormat.F35c or DexInstructionFormat.F3rc => 3,
                DexInstructionFormat.F51 => 5,
                _ => throw new FormatException("DEX instruction format has no width metadata.")
            };
        }

        private static void RequireUnits(ushort[] instructions, int pc, int count) { if (pc > instructions.Length - count) throw new FormatException("DEX payload is truncated."); }

        private static uint ReadUleb128Bounded(byte[] data, ref int offset, string field)
        {
            uint result = 0;
            for (int i = 0; i < 5; i++)
            {
                if ((uint)offset >= data.Length) throw new FormatException("Truncated " + field + ".");
                byte value = data[offset++];
                if (i == 4 && (value & 0xf0) != 0) throw new FormatException("Overflow in " + field + ".");
                result |= (uint)(value & 0x7f) << (i * 7);
                if ((value & 0x80) == 0) return result;
            }
            throw new FormatException("Overlong " + field + ".");
        }

        private static int ReadSleb128Bounded(byte[] data, ref int offset, string field)
        {
            int result = 0, shift = 0;
            byte value = 0;
            for (int i = 0; i < 5; i++)
            {
                if ((uint)offset >= data.Length) throw new FormatException("Truncated " + field + ".");
                value = data[offset++];
                if (i == 4)
                {
                    int payload = value & 0x7f;
                    bool negative = (payload & 0x08) != 0;
                    if ((!negative && (payload & 0x70) != 0) || (negative && (payload & 0x70) != 0x70) || (value & 0x80) != 0)
                        throw new FormatException("Overflow in " + field + ".");
                }
                result |= (value & 0x7f) << shift;
                shift += 7;
                if ((value & 0x80) == 0)
                {
                    if (shift < 32 && (value & 0x40) != 0) result |= -1 << shift;
                    return result;
                }
            }
            throw new FormatException("Overlong " + field + ".");
        }

        private static void RequireRange(byte[] data, int offset, long length, string field)
        {
            if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset) throw new FormatException("Truncated " + field + ".");
        }

        // ---------------- helpers de bajo nivel ----------------

        /// <summary>Reads a type_list (count u32 + count × type_idx u16) and appends the
        /// resolved descriptors. Same structure used by proto parameter lists and by
        /// class_def interfaces_off.</summary>
        private static void ReadTypeList(byte[] data, uint offset, DexFile dex, List<string> target)
        {
            uint count = U32(data, (int)offset);
            for (uint i = 0; i < count; i++)
            {
                ushort typeIdx = U16(data, (int)(offset + 4 + i * 2));
                target.Add(dex.TypeDescriptors[typeIdx]);
            }
        }

        private static uint U32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static ushort U16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUleb128(byte[] data, ref int offset)
            => ReadUleb128Bounded(data, ref offset, "ULEB128 value");

        /// <summary>
        /// Decodifica MUTF-8 (variante de UTF-8 usada por DEX) hasta el byte NUL
        /// terminador. Cubre secuencias de 1, 2 y 3 bytes (suficiente para texto
        /// típico de nombres de clases, métodos y mensajes de log). No reconstruye
        /// pares subrogados codificados como dos secuencias de 3 bytes (CESU-8),
        /// que son poco frecuentes en manifests/etiquetas de log.
        /// </summary>
        private static string ReadMutf8UntilNul(byte[] data, ref int offset)
        {
            var sb = new StringBuilder();
            while (true)
            {
                byte b0 = data[offset];
                if (b0 == 0) { offset++; break; }

                if ((b0 & 0x80) == 0)
                {
                    sb.Append((char)b0);
                    offset += 1;
                }
                else if ((b0 & 0xE0) == 0xC0)
                {
                    byte b1 = data[offset + 1];
                    int cp = ((b0 & 0x1F) << 6) | (b1 & 0x3F);
                    sb.Append((char)cp);
                    offset += 2;
                }
                else if ((b0 & 0xF0) == 0xE0)
                {
                    byte b1 = data[offset + 1];
                    byte b2 = data[offset + 2];
                    int cp = ((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F);
                    sb.Append((char)cp);
                    offset += 3;
                }
                else
                {
                    // Secuencia no cubierta por este prototipo: se salta un byte
                    // para no colgarse en un bucle infinito con datos raros.
                    offset += 1;
                }
            }
            return sb.ToString();
        }
    }
}
