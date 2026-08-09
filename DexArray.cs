#nullable enable
namespace AndroidRuntime.Core.Dex;

/// <summary>Typed guest array retaining raw wide payload bits.</summary>
public sealed class DexArray
{
    private readonly object?[] _values;
    public DexArray(string arrayDescriptor, int length)
    {
        if (!arrayDescriptor.StartsWith("[", StringComparison.Ordinal)) throw new ArgumentException("DEX array descriptor required.", nameof(arrayDescriptor));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        ArrayDescriptor = arrayDescriptor;
        ElementDescriptor = arrayDescriptor[1..];
        _values = new object?[length];
    }
    public string ArrayDescriptor { get; }
    public string ElementDescriptor { get; }
    public int Length => _values.Length;
    public object? Get(int index) => _values[index] ?? (ElementDescriptor is "J" or "D" ? 0UL : ElementDescriptor.StartsWith("L", StringComparison.Ordinal) || ElementDescriptor.StartsWith("[", StringComparison.Ordinal) ? null : 0);
    public void Set(int index, object? value)
    {
        if (ElementDescriptor is "J" or "D") throw new InvalidOperationException("Wide DEX array elements must be written with SetWide.");
        bool reference = ElementDescriptor.StartsWith("L", StringComparison.Ordinal) || ElementDescriptor.StartsWith("[", StringComparison.Ordinal);
        if (!reference && value is not int) throw new InvalidOperationException("Primitive DEX array elements require a 32-bit word.");
        if (reference && value is not null and not string and not DexObject and not DexArray) throw new InvalidOperationException("Reference DEX array elements require a guest reference or null.");
        _values[index] = value;
    }
    public ulong GetWide(int index) => Get(index) is ulong bits ? bits : throw new InvalidOperationException("Wide DEX array storage is corrupted.");
    public void SetWide(int index, ulong bits)
    {
        if (ElementDescriptor is not ("J" or "D")) throw new InvalidOperationException("SetWide requires a long[] or double[] DEX array.");
        _values[index] = bits;
    }
}
