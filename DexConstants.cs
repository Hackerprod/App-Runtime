using System.Collections.Generic;

namespace AndroidRuntime.Core.Dex
{
    /// <summary>
    /// Constantes del formato DEX (ver documentación pública de la Dalvik Executable
    /// Format, la misma que usan herramientas como dexdump / smali / baksmali).
    /// Este archivo NO contiene código de Android ni assets de Google: es únicamente
    /// la descripción de un formato de archivo binario documentado públicamente,
    /// necesaria para poder leerlo, igual que un parser de ZIP o de ELF.
    /// </summary>
    public static class DexConstants
    {
        public const uint NO_INDEX = 0xFFFFFFFF;

        public const uint ACC_PUBLIC = 0x1;
        public const uint ACC_PRIVATE = 0x2;
        public const uint ACC_PROTECTED = 0x4;
        public const uint ACC_STATIC = 0x8;
        public const uint ACC_FINAL = 0x10;
        public const uint ACC_NATIVE = 0x100;
        public const uint ACC_INTERFACE = 0x200;
        public const uint ACC_ABSTRACT = 0x400;

        /// <summary>
        /// Nombres legibles de los opcodes que el intérprete soporta, solo para
        /// mensajes de error / logs de depuración.
        /// </summary>
        public static readonly Dictionary<int, string> OpcodeNames = new Dictionary<int, string>
        {
            { 0x00, "nop" }, { 0x01, "move" }, { 0x02, "move/from16" }, { 0x03, "move/16" },
            { 0x04, "move-wide" }, { 0x07, "move-object" }, { 0x08, "move-object/from16" },
            { 0x0a, "move-result" }, { 0x0b, "move-result-wide" }, { 0x0c, "move-result-object" },
            { 0x0d, "move-exception" }, { 0x0e, "return-void" }, { 0x0f, "return" },
            { 0x10, "return-wide" }, { 0x11, "return-object" },
            { 0x12, "const/4" }, { 0x13, "const/16" }, { 0x14, "const" }, { 0x15, "const/high16" },
            { 0x16, "const-wide/16" }, { 0x1a, "const-string" }, { 0x1b, "const-string/jumbo" },
            { 0x1c, "const-class" }, { 0x1f, "check-cast" }, { 0x20, "instance-of" },
            { 0x21, "array-length" }, { 0x22, "new-instance" }, { 0x23, "new-array" },
            { 0x28, "goto" }, { 0x29, "goto/16" },
            { 0x32, "if-eq" }, { 0x33, "if-ne" }, { 0x34, "if-lt" }, { 0x35, "if-ge" },
            { 0x36, "if-gt" }, { 0x37, "if-le" },
            { 0x38, "if-eqz" }, { 0x39, "if-nez" }, { 0x3a, "if-ltz" }, { 0x3b, "if-gez" },
            { 0x3c, "if-gtz" }, { 0x3d, "if-lez" },
            { 0x44, "aget" }, { 0x4b, "aput" },
            { 0x52, "iget" }, { 0x59, "iput" }, { 0x60, "sget" }, { 0x67, "sput" },
            { 0x6e, "invoke-virtual" }, { 0x6f, "invoke-super" }, { 0x70, "invoke-direct" },
            { 0x71, "invoke-static" }, { 0x72, "invoke-interface" },
            { 0x74, "invoke-virtual/range" }, { 0x75, "invoke-super/range" },
            { 0x76, "invoke-direct/range" }, { 0x77, "invoke-static/range" },
            { 0x78, "invoke-interface/range" },
            { 0x7b, "neg-int" }, { 0x7c, "not-int" },
            { 0x81, "int-to-long" }, { 0x84, "long-to-int" },
            { 0x8d, "int-to-byte" }, { 0x8e, "int-to-char" }, { 0x8f, "int-to-short" },
            { 0x90, "add-int" }, { 0xb0, "add-int/2addr" },
            { 0xd0, "add-int/lit16" }, { 0xd8, "add-int/lit8" },
        };

        public static string NameOf(int opcode)
        {
            return OpcodeNames.TryGetValue(opcode, out var name) ? name : "opcode_" + opcode.ToString("X2");
        }
    }
}
