#nullable enable
namespace AndroidRuntime.Core.Dex;

public sealed class AndroidGuestArithmeticException : ArithmeticException
{
    public AndroidGuestArithmeticException(string message) : base(message) { }
}

internal sealed class GuestExceptionCarrier : Exception
{
    public GuestExceptionCarrier(DexObject throwable) => Throwable = throwable;
    public DexObject Throwable { get; }
    public bool TraceRecorded { get; set; }
    public List<string> GuestFrames { get; } = new();
    public void AddFrame(DexEncodedMethod method, int pc)
    {
        if (GuestFrames.Count < 64) GuestFrames.Add(method.Method + " pc=" + pc);
    }
}

public sealed class UncaughtAndroidGuestException : Exception
{
    internal UncaughtAndroidGuestException(DexObject throwable, IReadOnlyList<string> frames)
        : base(BuildMessage(throwable))
    {
        GuestThrowable = throwable;
        TypeDescriptor = throwable.TypeDescriptor;
        GuestMessage = GuestThrowableMetadata.Message(throwable);
        GuestFrames = frames.ToArray();
    }

    public DexObject GuestThrowable { get; }
    public string TypeDescriptor { get; }
    public string? GuestMessage { get; }
    public IReadOnlyList<string> GuestFrames { get; }
    private static string BuildMessage(DexObject throwable) => "Uncaught Android guest exception: " + throwable.TypeDescriptor + (GuestThrowableMetadata.Message(throwable) is string message ? ": " + message : string.Empty);
    public override string ToString() => Message + (GuestFrames.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, GuestFrames.Select(frame => "  at " + frame)));
}

internal static class GuestThrowableMetadata
{
    private const int MaxMessageLength = 4096;
    private const string MessageKey = "__guestThrowableMessage";
    private const string CauseKey = "__guestThrowableCause";
    internal static DexObject Create(string descriptor, string? message = null, DexObject? cause = null)
    {
        var value = new DexObject(descriptor);
        Set(value, message, cause);
        return value;
    }
    internal static void Set(DexObject value, string? message, DexObject? cause)
    {
        if (message?.Length > MaxMessageLength) throw new AndroidGuestExceptionQuotaExceededException("Guest Throwable message exceeds 4096 UTF-16 code units.");
        value.InstanceFields[MessageKey] = message!;
        value.InstanceFields[CauseKey] = cause!;
    }
    internal static string? Message(DexObject value) => value.InstanceFields.TryGetValue(MessageKey, out object? message) ? message as string : null;
    internal static DexObject? Cause(DexObject value) => value.InstanceFields.TryGetValue(CauseKey, out object? cause) ? cause as DexObject : null;
}

public sealed class AndroidGuestExceptionQuotaExceededException : InvalidOperationException
{
    public AndroidGuestExceptionQuotaExceededException(string message) : base(message) { }
}
public sealed class AndroidApiSecurityException : Exception { public AndroidApiSecurityException(string message) : base(message) { } }
public sealed class AndroidGuestArrayIndexException : Exception { public AndroidGuestArrayIndexException(string message) : base(message) { } }
