#nullable enable
using System.Collections.ObjectModel;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer;

public readonly record struct AndroidApiMethodId
{
    public AndroidApiMethodId(string classDescriptor, string methodName, string methodDescriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classDescriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodDescriptor);
        ClassDescriptor = classDescriptor;
        MethodName = methodName;
        MethodDescriptor = methodDescriptor;
    }

    public string ClassDescriptor { get; }
    public string MethodName { get; }
    public string MethodDescriptor { get; }
    public override string ToString() => ClassDescriptor + "->" + MethodName + MethodDescriptor;
}

public enum AndroidInvokeKind
{
    Virtual,
    Super,
    Direct,
    Static,
    Interface
}

public enum AndroidApiEventKind
{
    Requested,
    Completed,
    Unimplemented,
    GuestThrew,
    Failed,
    Cancelled
}

public sealed class AndroidApiInvocation
{
    internal AndroidApiInvocation(
        long sequence,
        Guid invocationId,
        AndroidApiCallSite callSite,
        IReadOnlyList<string> argumentSummaries,
        AndroidApiSessionContext session)
    {
        Sequence = sequence;
        InvocationId = invocationId;
        CallerMethod = callSite.CallerMethod;
        DexPc = callSite.DexPc;
        RequestedApi = callSite.RequestedApi;
        ResolvedApi = callSite.ResolvedApi;
        InvokeKind = callSite.InvokeKind;
        ArgumentSummaries = argumentSummaries;
        ManagedThreadId = Environment.CurrentManagedThreadId;
        IsMainLane = session.IsMainLane();
        CancellationToken = session.CancellationToken;
        SessionId = session.SessionId;
        PackageName = session.PackageName;
        ActivityDescriptor = session.ActivityDescriptor;
    }

    public long Sequence { get; }
    public Guid InvocationId { get; }
    public string CallerMethod { get; }
    public int DexPc { get; }
    public AndroidApiMethodId RequestedApi { get; }
    public AndroidApiMethodId ResolvedApi { get; }
    public AndroidInvokeKind InvokeKind { get; }
    public IReadOnlyList<string> ArgumentSummaries { get; }
    public int ManagedThreadId { get; }
    public bool IsMainLane { get; }
    public CancellationToken CancellationToken { get; }
    public string SessionId { get; }
    public string PackageName { get; }
    public string ActivityDescriptor { get; }
}

public sealed record AndroidApiCallSite(
    string CallerMethod,
    int DexPc,
    AndroidApiMethodId RequestedApi,
    AndroidApiMethodId ResolvedApi,
    AndroidInvokeKind InvokeKind);

public sealed record AndroidApiTraceEvent(
    AndroidApiEventKind Kind,
    AndroidApiInvocation Invocation,
    string? ErrorType = null);

public interface IAndroidApiTraceSink
{
    void Record(AndroidApiTraceEvent traceEvent);
}

public sealed class AndroidApiTraceBuffer : IAndroidApiTraceSink
{
    private readonly object _gate = new();
    private readonly Queue<AndroidApiTraceEvent> _events;
    private readonly int _capacity;
    private long _droppedCount;

    public AndroidApiTraceBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must be positive.");
        _capacity = capacity;
        _events = new Queue<AndroidApiTraceEvent>(capacity);
    }

    public long DroppedCount
    {
        get { lock (_gate) return _droppedCount; }
    }

    public void Record(AndroidApiTraceEvent traceEvent)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);
        lock (_gate)
        {
            if (_events.Count == _capacity)
            {
                _events.Dequeue();
                _droppedCount++;
            }
            _events.Enqueue(traceEvent);
        }
    }

    public IReadOnlyList<AndroidApiTraceEvent> Snapshot()
    {
        lock (_gate)
            return _events.ToArray();
    }
}

public sealed class AndroidApiSessionContext
{
    private long _sequence;

    public AndroidApiSessionContext(
        string sessionId,
        string packageName,
        string activityDescriptor,
        CancellationToken cancellationToken,
        Func<bool> isMainLane,
        IAndroidApiTraceSink? traceSink = null,
        Func<string, string, bool>? isTypeAssignable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(isMainLane);
        SessionId = sessionId;
        PackageName = packageName ?? string.Empty;
        ActivityDescriptor = activityDescriptor ?? string.Empty;
        CancellationToken = cancellationToken;
        IsMainLane = isMainLane;
        TraceSink = traceSink;
        // Framework-interface-aware default: standalone contexts (tests) must see the
        // same framework interface edges (e.g. ThreadPoolExecutor implements
        // ExecutorService) as the interpreter's merged assignability does. Hosted
        // sessions override with the interpreter's guest+framework version.
        IsTypeAssignable = isTypeAssignable ?? ((actual, expected) => AndroidFrameworkHierarchy.IsAssignable(actual, expected, AndroidFrameworkHierarchy.ParentOf, AndroidFrameworkHierarchy.InterfacesOf));
    }

    public string SessionId { get; }
    public string PackageName { get; }
    public string ActivityDescriptor { get; }
    public CancellationToken CancellationToken { get; }
    public Func<bool> IsMainLane { get; }
    internal IAndroidApiTraceSink? TraceSink { get; }
    internal Func<string, string, bool> IsTypeAssignable { get; set; }
    internal long NextSequence() => Interlocked.Increment(ref _sequence);

    internal void RecordGuestThrow(string callerMethod, int dexPc, string typeDescriptor)
    {
        if (TraceSink is null) return;
        var api = new AndroidApiMethodId(typeDescriptor, "<throw>", "()V");
        var invocation = new AndroidApiInvocation(NextSequence(), Guid.NewGuid(), new AndroidApiCallSite(callerMethod, dexPc, api, api, AndroidInvokeKind.Direct), ["guest-exception"], this);
        try { TraceSink.Record(new AndroidApiTraceEvent(AndroidApiEventKind.GuestThrew, invocation, typeDescriptor)); } catch { }
    }

    internal static AndroidApiSessionContext Standalone { get; } =
        new("standalone", string.Empty, string.Empty, CancellationToken.None, () => true);
}

public delegate object AndroidApiBinding(AndroidApiInvocation invocation, object[] arguments);

public sealed class AndroidApiRegistryBuilder
{
    private readonly Dictionary<AndroidApiMethodId, AndroidApiBinding> _bindings = new();

    public AndroidApiRegistryBuilder Register(AndroidApiMethodId api, AndroidApiBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!_bindings.TryAdd(api, binding))
            throw new ArgumentException("Android API binding is already registered: " + api, nameof(api));
        return this;
    }

    public AndroidApiRegistryBuilder Register(
        string classDescriptor,
        string methodName,
        string methodDescriptor,
        AndroidApiBinding binding) =>
        Register(new AndroidApiMethodId(classDescriptor, methodName, methodDescriptor), binding);

    public AndroidApiRegistry Build() => new(_bindings);
}

/// <summary>Immutable exact-method binding snapshot safe for concurrent reads.</summary>
public sealed class AndroidApiRegistry
{
    private readonly IReadOnlyDictionary<AndroidApiMethodId, AndroidApiBinding> _bindings;

    internal AndroidApiRegistry(Dictionary<AndroidApiMethodId, AndroidApiBinding> bindings)
    {
        _bindings = new ReadOnlyDictionary<AndroidApiMethodId, AndroidApiBinding>(
            new Dictionary<AndroidApiMethodId, AndroidApiBinding>(bindings));
    }

    public bool Contains(AndroidApiMethodId api) => _bindings.ContainsKey(api);

    public bool TryGet(AndroidApiMethodId api, out AndroidApiBinding binding) =>
        _bindings.TryGetValue(api, out binding!);

    public object Invoke(AndroidApiSessionContext session, AndroidApiCallSite callSite, object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(callSite);
        arguments ??= Array.Empty<object>();
        ValidateCallShape(callSite, arguments, session);
        var invocation = new AndroidApiInvocation(
            session.NextSequence(),
            Guid.NewGuid(),
            callSite,
            SummarizeArguments(arguments),
            session);
        RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Requested, invocation));

        try
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            if (!_bindings.TryGetValue(callSite.ResolvedApi, out var binding))
            {
                RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Unimplemented, invocation));
                throw new AndroidApiNotImplementedException(callSite.ResolvedApi, invocation);
            }

            object result = binding(invocation, arguments);
            ValidateReturnValue(callSite.ResolvedApi, result, session);
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Completed, invocation));
            return result;
        }
        catch (OperationCanceledException)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Cancelled, invocation));
            throw;
        }
        catch (AndroidApiNotImplementedException)
        {
            throw;
        }
        catch (AndroidApiUnavailableException error)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, error.GetType().Name));
            throw;
        }
        catch (AndroidApiNullReferenceException error)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, error.GetType().Name));
            throw;
        }
        catch (AndroidApiSecurityException error)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, error.GetType().Name));
            throw;
        }
        catch (AndroidGuestArrayIndexException error)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, error.GetType().Name));
            throw;
        }
        catch (GuestExceptionCarrier)
        {
            // Guest exceptions raised inside a binding (e.g. ArrayList bounds) must
            // reach the interpreter's Execute catch un-wrapped so they surface as
            // catchable guest exceptions, not as AndroidApiBindingException.
            throw;
        }
        catch (AndroidPeerQuotaExceededException error)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, error.GetType().Name));
            throw;
        }
        catch (AndroidApiBindingException error)
        {
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, error.GetType().Name));
            throw;
        }
        catch (Exception error)
        {
            var wrapped = new AndroidApiBindingException(callSite.ResolvedApi, invocation, error);
            RecordNoThrow(session.TraceSink, new AndroidApiTraceEvent(AndroidApiEventKind.Failed, invocation, wrapped.GetType().Name));
            throw wrapped;
        }
    }

    public static AndroidApiRegistry CreateActivityLifecycleRegistry()
    {
        var builder = new AndroidApiRegistryBuilder();
        builder.Register("Landroid/app/Activity;", "<init>", "()V", (_, _) => null!);
        builder.Register("Landroid/app/Activity;", "onCreate", "(Landroid/os/Bundle;)V", (_, _) => null!);
        builder.Register("Landroid/app/Activity;", "onStart", "()V", (_, _) => null!);
        builder.Register("Landroid/app/Activity;", "onResume", "()V", (_, _) => null!);
        builder.Register("Landroid/app/Activity;", "onPause", "()V", (_, _) => null!);
        builder.Register("Landroid/app/Activity;", "onStop", "()V", (_, _) => null!);
        builder.Register("Landroid/app/Activity;", "onDestroy", "()V", (_, _) => null!);
        return builder.Build();
    }

    private static IReadOnlyList<string> SummarizeArguments(object[] arguments)
    {
        var summaries = new string[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            object value = arguments[index];
            summaries[index] = value switch
            {
                null => "null",
                string text => "string(len=" + text.Length + ",redacted)",
                DexObject dexObject => "object(" + dexObject.TypeDescriptor + ")",
                Array array => "array(len=" + array.Length + ")",
                _ => value.GetType().Name
            };
        }
        return summaries;
    }

    private static void ValidateCallShape(AndroidApiCallSite callSite, object[] arguments, AndroidApiSessionContext session)
    {
        int parameterCount = CountParameters(callSite.ResolvedApi.MethodDescriptor);
        bool isStatic = callSite.InvokeKind == AndroidInvokeKind.Static;
        bool? expectedStatic = KnownStaticShape(callSite.ResolvedApi);
        if (expectedStatic.HasValue && expectedStatic.Value != isStatic)
            throw new ArgumentException($"Invoke kind {callSite.InvokeKind} does not match {(expectedStatic.Value ? "static" : "instance")} API {callSite.ResolvedApi}.", nameof(callSite));
        int expected = parameterCount + (isStatic ? 0 : 1);
        if (arguments.Length != expected)
            throw new ArgumentException($"Invocation argument count for {callSite.ResolvedApi} must be {expected}, observed {arguments.Length}.", nameof(arguments));
        if (!isStatic && arguments[0] is null)
            throw new AndroidApiNullReferenceException("Instance Android API receiver is null: " + callSite.ResolvedApi);
        int argumentIndex = isStatic ? 0 : 1;
        foreach (string parameter in ParameterDescriptors(callSite.ResolvedApi.MethodDescriptor))
        {
            object value = arguments[argumentIndex++];
            if (value is not null)
            {
                string? actual = ReferenceDescriptor(value);
                if ((parameter.StartsWith("[", StringComparison.Ordinal) || actual?.StartsWith("[", StringComparison.Ordinal) == true) &&
                    (actual is null || !session.IsTypeAssignable(actual, parameter)))
                    throw new ArgumentException($"Invocation argument is not assignable to {parameter} for {callSite.ResolvedApi}.", nameof(arguments));
            }
        }
    }

    private static bool? KnownStaticShape(AndroidApiMethodId api)
    {
        if (api.ClassDescriptor is "Landroid/util/Log;" or "Landroid/text/TextUtils;" or "Landroid/graphics/Color;" or "Landroid/os/SystemClock;" or "Ljava/util/concurrent/Executors;" or "Ljava/util/Collections;") return true;
        if (api.ClassDescriptor == "Landroid/os/Looper;" && api.MethodName is "getMainLooper" or "myLooper" or "prepare" or "loop") return true;
        if (api.ClassDescriptor == "Ljava/lang/String;" && api.MethodName == "valueOf") return true;
        if (api.ClassDescriptor == "Landroid/widget/Toast;" && api.MethodName == "makeText") return true;
        if (api.ClassDescriptor == "Ljava/util/concurrent/TimeUnit;" && api.MethodName == "values") return true;
        if (api.ClassDescriptor == "Ljava/lang/Enum;" && api.MethodName == "valueOf") return true;
        if (api.ClassDescriptor == "Ljava/lang/String;" && api.MethodName == "format") return true;
        // Class.forName is static; the blanket instance default below would otherwise
        // misreport a genuine static call as a shape mismatch instead of the honest
        // unimplemented boundary.
        if (api.ClassDescriptor == "Ljava/lang/Class;" && api.MethodName == "forName") return true;
        if (api.ClassDescriptor is "Ljava/lang/Boolean;" or "Ljava/lang/Integer;" or "Ljava/lang/Long;" or "Ljava/lang/Short;" or "Ljava/lang/Byte;" or "Ljava/lang/Character;" or "Ljava/lang/Double;" or "Ljava/lang/Float;")
        {
            // Static factory/parse/format overloads (valueOf, parse*, and the
            // primitive-parameter toString/hashCode/compare statics — distinguishable
            // by having parameters, unlike the () instance accessors).
            if (api.MethodName is "valueOf" or "parseBoolean" or "parseInt" or "parseLong" or "parseShort" or "parseByte" or "parseDouble" or "parseFloat") return true;
            if (api.MethodName is "toString" or "hashCode" or "compare" && api.MethodDescriptor.Length > 2 && api.MethodDescriptor[1] != ')') return true;
            return false;
        }
        if (api.ClassDescriptor is "Landroid/app/Activity;" or "Landroid/content/Context;" or "Landroid/os/BaseBundle;" or "Landroid/os/Bundle;" or "Landroid/content/Intent;" or "Landroid/widget/Toast;" or "Ljava/lang/String;" or "Ljava/lang/StringBuilder;" or "Ljava/lang/CharSequence;" or "Ljava/util/concurrent/TimeUnit;" or "Ljava/util/concurrent/ThreadPoolExecutor;" or "Ljava/util/concurrent/ExecutorService;" or "Ljava/util/concurrent/Executor;" or "Ljava/util/concurrent/FutureTask;" or "Ljava/util/concurrent/Future;" or "Ljava/util/concurrent/ThreadFactory;" or "Landroid/os/Handler;" or "Landroid/os/Looper;" or "Ljava/lang/Class;" or "Ljava/lang/Enum;" or "Ljava/lang/reflect/Method;" or "Ljava/util/ArrayDeque;" or "Ljava/util/Deque;" or "Ljava/util/Queue;" or "Ljava/util/LinkedHashSet;") return false;
        return null;
    }

    private static void ValidateReturnValue(AndroidApiMethodId api, object result, AndroidApiSessionContext session)
    {
        string descriptor = api.MethodDescriptor;
        string returnType = descriptor[(descriptor.IndexOf(')') + 1)..];
        bool valid = returnType switch
        {
            "V" => result is null,
            "I" or "Z" or "C" => result is int or bool or char,
            "F" => result is float,
            "J" => result is long,
            "D" => result is double,
            _ when returnType.StartsWith("[", StringComparison.Ordinal) => result is null || ReferenceDescriptor(result) is string actual && session.IsTypeAssignable(actual, returnType),
            _ when returnType.StartsWith("L", StringComparison.Ordinal) => IsReferenceAssignable(result, returnType, session),
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException($"Binding {api} returned an unsupported value for {returnType}: {result?.GetType().Name ?? "null"}.");
    }

    private static string? GetArrayDescriptor(object result)
    {
        if (result is DexArray dexArray) return dexArray.ArrayDescriptor;
        return GetClrArrayDescriptor(result.GetType());
    }

    private static string? GetClrArrayDescriptor(Type type)
    {
        if (!type.IsArray || type.GetArrayRank() != 1) return null;
        Type? element = type.GetElementType();
        if (element is null) return null;
        string? component = GetClrComponentDescriptor(element);
        return component is null ? null : "[" + component;
    }

    private static string? GetClrComponentDescriptor(Type type) => type.IsArray ? GetClrArrayDescriptor(type) : type == typeof(bool) ? "Z" : type == typeof(byte) || type == typeof(sbyte) ? "B" : type == typeof(short) ? "S" : type == typeof(char) ? "C" : type == typeof(int) ? "I" : type == typeof(long) ? "J" : type == typeof(float) ? "F" : type == typeof(double) ? "D" : type == typeof(string) ? "Ljava/lang/String;" : type == typeof(object) || type == typeof(DexObject) ? "Ljava/lang/Object;" : type == typeof(DexArray) ? "[Ljava/lang/Object;" : null;

    private static bool IsReferenceAssignable(object result, string expected, AndroidApiSessionContext session)
    {
        if (result is null) return true;
        string? actual = ReferenceDescriptor(result);
        return actual is not null && session.IsTypeAssignable(actual, expected);
    }

    private static string? ReferenceDescriptor(object value) => value switch
    {
        string => "Ljava/lang/String;",
        DexObject dexObject => dexObject.TypeDescriptor,
        DexArray dexArray => dexArray.ArrayDescriptor,
        Array array => GetArrayDescriptor(array),
        _ => null
    };

    private static IEnumerable<string> ParameterDescriptors(string descriptor)
    {
        int close = descriptor.IndexOf(')');
        for (int index = 1; index < close;)
        {
            int start = index;
            while (descriptor[index] == '[') index++;
            if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++;
            yield return descriptor[start..index];
        }
    }

    private static int CountParameters(string descriptor)
    {
        int close = descriptor.IndexOf(')');
        if (descriptor.Length < 3 || descriptor[0] != '(' || close < 0 || close == descriptor.Length - 1)
            throw new ArgumentException("Invalid method descriptor: " + descriptor, nameof(descriptor));
        int count = 0;
        for (int index = 1; index < close; count++)
        {
            while (descriptor[index] == '[') index++;
            if (descriptor[index] == 'L')
            {
                index = descriptor.IndexOf(';', index);
                if (index < 0 || index >= close)
                    throw new ArgumentException("Invalid method descriptor: " + descriptor, nameof(descriptor));
            }
            index++;
        }
        return count;
    }

    private static void RecordNoThrow(IAndroidApiTraceSink? sink, AndroidApiTraceEvent traceEvent)
    {
        try { sink?.Record(traceEvent); }
        catch { }
    }
}

public sealed class AndroidApiNotImplementedException : NotSupportedException
{
    public AndroidApiNotImplementedException(AndroidApiMethodId api, AndroidApiInvocation invocation)
        : base("Android API is not implemented: " + api)
    {
        Api = api;
        Invocation = invocation;
    }

    public AndroidApiMethodId Api { get; }
    public AndroidApiInvocation Invocation { get; }
}

public sealed class AndroidApiUnavailableException : InvalidOperationException
{
    public AndroidApiUnavailableException(AndroidApiMethodId api, string message, Exception? innerException = null)
        : base(message, innerException) => Api = api;
    public AndroidApiMethodId Api { get; }
}

public sealed class AndroidApiBindingException : InvalidOperationException
{
    public AndroidApiBindingException(AndroidApiMethodId api, AndroidApiInvocation invocation, Exception innerException)
        : base("Android API binding failed: " + api, innerException)
    {
        Api = api;
        Invocation = invocation;
    }

    public AndroidApiMethodId Api { get; }
    public AndroidApiInvocation Invocation { get; }
}

public sealed class AndroidApiNullReferenceException : NullReferenceException
{
    public AndroidApiNullReferenceException(string message) : base(message) { }
}
