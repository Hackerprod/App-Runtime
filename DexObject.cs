namespace AndroidRuntime.Core.Dex;

/// <summary>Minimal heap object used by the current interpreter subset.</summary>
public sealed class DexObject
{
    public DexObject(string typeDescriptor)
    {
        TypeDescriptor = typeDescriptor ?? throw new ArgumentNullException(nameof(typeDescriptor));
    }

    public string TypeDescriptor { get; }
    public Dictionary<string, object> InstanceFields { get; } = new(StringComparer.Ordinal);

    public override string ToString() => TypeDescriptor;
}
