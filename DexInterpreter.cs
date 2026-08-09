using System;
using System.Collections.Generic;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Dex
{
    /// <summary>
    /// Intérprete de un subconjunto de la Dalvik Executable Format (registros como
    /// máquina virtual de pila de registros, no de pila de operandos). Cubre lo
    /// suficiente para ejecutar métodos con enteros, strings, arrays simples,
    /// campos y llamadas a otros métodos (propios o de la capa de API), que es lo
    /// que se necesita para el MVP descrito en el roadmap: no implementa threads,
    /// excepciones (try/catch), ni los tipos long/float/double con su ancho real
    /// (se tratan como enteros de 32 bits, simplificación deliberada de esta fase).
    ///
    /// Instrucciones no reconocidas lanzan NotImplementedException con el opcode,
    /// en vez de fallar silenciosamente o dar un resultado incorrecto.
    /// </summary>
    public sealed class DexInterpreter
    {
        private const long DefaultMaxStepsPerInvocation = 20_000_000;
        private const int MaxCallDepth = 128;

        private readonly DexFileSet _dexSet;
        private readonly DexFile _primaryDex;
        private readonly AndroidApiRegistry _api;
        private readonly AndroidApiSessionContext _apiSession;
        private readonly long _maxStepsPerInvocation;
        private readonly AndroidGil _gil;        private readonly Dictionary<string, object> _staticFields = new Dictionary<string, object>();
        private const int ClassInitNotStarted = 0;
        private const int ClassInitInProgress = 1;
        private const int ClassInitDone = 2;
        private readonly Dictionary<string, int> _classInitState = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _classInitOwner = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ManualResetEventSlim> _classInitSignals = new(StringComparer.Ordinal);

        private long _stepsExecuted;
        private int _callDepth;
        private readonly record struct WideValue(ulong Bits);
        private sealed record PendingInvokeResult(object Value, string ReturnDescriptor, int NextPc);
        private static readonly object WideHigh = new();

        public DexInterpreter(
            DexFileSet dexSet,
            AndroidApiRegistry api,
            long maxStepsPerInvocation = DefaultMaxStepsPerInvocation,
            AndroidApiSessionContext apiSession = null,
            AndroidGil gil = null)
        {
            _dexSet = dexSet ?? throw new ArgumentNullException(nameof(dexSet));
            _primaryDex = dexSet.Primary;
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _gil = gil ?? new AndroidGil();
            _apiSession = apiSession ?? new AndroidApiSessionContext(
                "standalone-" + Guid.NewGuid().ToString("N"),
                string.Empty,
                string.Empty,
                CancellationToken.None,
                () => true);
            if (maxStepsPerInvocation <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStepsPerInvocation), "Step budget must be positive.");
            _maxStepsPerInvocation = maxStepsPerInvocation;
            _apiSession.IsTypeAssignable = IsTypeAssignable;
        }

        /// <summary>The session GIL this interpreter executes under. Hosts set the
        /// framework state's Gil to this value so bindings release the SAME lock
        /// around blocking operations.</summary>
        internal AndroidGil Gil => _gil;

        /// <summary>Punto de entrada de conveniencia: busca un método por clase+nombre y lo ejecuta.</summary>
        public object InvokeStatic(string classDescriptor, string methodName, params object[] args)
        {
            using (_gil.Acquire())
            {
                var method = _dexSet.FindMethodByName(classDescriptor, methodName);
                if (method == null)
                    throw new InvalidOperationException("No se encontró " + classDescriptor + "->" + methodName + " en el DEX cargado.");
                _stepsExecuted = 0;
                return ExecuteRoot(method, args ?? new object[0]);
            }
        }

        /// <summary>Invokes one static method using its complete DEX descriptor.</summary>
        public object InvokeStaticExact(string classDescriptor, string methodName, string methodDescriptor, params object[] args)
        {
            using (_gil.Acquire())
            {
                var method = _dexSet.FindMethodExact(classDescriptor, methodName, methodDescriptor);
                if (method == null)
                    throw new InvalidOperationException("Method not found in loaded DEX: " + classDescriptor + "->" + methodName + methodDescriptor);
                _stepsExecuted = 0;
                return ExecuteRoot(method, args ?? Array.Empty<object>());
            }
        }

        /// <summary>Allocates a heap object for a class defined by the loaded DEX without running a constructor.</summary>
        public DexObject CreateInstance(string classDescriptor)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(classDescriptor);
            if (_dexSet.FindClass(classDescriptor) == null)
                throw new InvalidOperationException("Class not found in loaded DEX: " + classDescriptor);
            return new DexObject(classDescriptor);
        }

        /// <summary>Allocates a DEX object and runs its exact no-argument constructor as one root invocation.</summary>
        public DexObject ConstructInstance(string classDescriptor)
        {
            var instance = CreateInstance(classDescriptor);
            InvokeInstanceExact(instance, "<init>", "()V");
            return instance;
        }

        /// <summary>Invokes one instance method using the receiver class and complete DEX descriptor.</summary>
        public object InvokeInstanceExact(DexObject instance, string methodName, string methodDescriptor, params object[] args)
        {
            using (_gil.Acquire())
            {
                ArgumentNullException.ThrowIfNull(instance);
                var method = _dexSet.FindMethodExact(instance.TypeDescriptor, methodName, methodDescriptor);
                if (method == null)
                    throw new InvalidOperationException("Method not found in loaded DEX: " + instance.TypeDescriptor + "->" + methodName + methodDescriptor);
                if (method.IsStatic)
                    throw new InvalidOperationException("Instance invocation cannot target a static method: " + method.Method);

                return ExecuteInstanceRoot(method, instance, args);
            }
        }

        /// <summary>Invokes an exact virtual guest method through the receiver's bounded DEX superclass chain.</summary>
        public object InvokeVirtualInstanceExact(DexObject instance, string methodName, string methodDescriptor, params object[] args)
        {
            using (_gil.Acquire())
            {
                var method = FindVirtualGuestMethod(instance, methodName, methodDescriptor);
                return ExecuteInstanceRoot(method, instance, args ?? Array.Empty<object>());
            }
        }

        /// <summary>Invokes an exact public virtual guest method, used by declarative Android event handlers.</summary>
        public object InvokePublicInstanceExact(DexObject instance, string methodName, string methodDescriptor, params object[] args)
        {
            using (_gil.Acquire())
            {
                var method = FindVirtualGuestMethod(instance, methodName, methodDescriptor);
                if ((method.AccessFlags & DexConstants.ACC_PUBLIC) == 0)
                    throw new MissingMethodException("Android XML callback must be public: " + method.Method);
                return ExecuteInstanceRoot(method, instance, args ?? Array.Empty<object>());
            }
        }

        private DexEncodedMethod FindVirtualGuestMethod(DexObject instance, string methodName, string methodDescriptor)
        {
            ArgumentNullException.ThrowIfNull(instance);
            ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
            ArgumentException.ThrowIfNullOrWhiteSpace(methodDescriptor);
            string current = instance.TypeDescriptor;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int depth = 0; depth < MaxCallDepth && visited.Add(current); depth++)
            {
                DexEncodedMethod method = _dexSet.FindMethodExact(current, methodName, methodDescriptor);
                if (method != null)
                {
                    if (method.IsStatic || method.Code == null) throw new MissingMethodException("Guest callback is not an executable instance method: " + method.Method);
                    return method;
                }
                DexClass type = _dexSet.FindClass(current);
                if (type == null || string.IsNullOrEmpty(type.SuperclassDescriptor)) break;
                current = type.SuperclassDescriptor;
            }
            throw new MissingMethodException("Guest virtual method not found: " + instance.TypeDescriptor + "->" + methodName + methodDescriptor);
        }

        /// <summary>Resolves only supported Activity lifecycle callbacks through the bounded DEX superclass chain.</summary>
        public object InvokeActivityLifecycleExact(DexObject instance, string methodName, string methodDescriptor, params object[] args)
        {
            using (_gil.Acquire())
            {
                return InvokeActivityLifecycleExactCore(instance, methodName, methodDescriptor, args);
            }
        }

        private object InvokeActivityLifecycleExactCore(DexObject instance, string methodName, string methodDescriptor, object[] args)
        {
            ArgumentNullException.ThrowIfNull(instance);
            args ??= Array.Empty<object>();
            int expectedArguments = (methodName, methodDescriptor) switch
            {
                ("onCreate", "(Landroid/os/Bundle;)V") => 1,
                ("onStart", "()V") => 0,
                ("onResume", "()V") => 0,
                ("onPause", "()V") => 0,
                ("onStop", "()V") => 0,
                ("onDestroy", "()V") => 0,
                _ => throw new ArgumentException(
                    "Unsupported Activity lifecycle identity: " + methodName + methodDescriptor,
                    nameof(methodName))
            };
            if (args.Length != expectedArguments)
                throw new ArgumentException(
                    "Argument count does not match Activity lifecycle method " + methodName + methodDescriptor,
                    nameof(args));

            string currentDescriptor = instance.TypeDescriptor;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int depth = 0; depth < MaxCallDepth; depth++)
            {
                if (!visited.Add(currentDescriptor))
                    throw new InvalidDataException("DEX superclass cycle while resolving " +
                        instance.TypeDescriptor + "->" + methodName + methodDescriptor);

                var currentClass = _dexSet.FindClass(currentDescriptor);
                if (currentClass != null)
                {
                    var method = _dexSet.FindMethodExact(currentDescriptor, methodName, methodDescriptor);
                    if (method != null)
                    {
                        if (method.IsStatic)
                            throw new MissingMethodException("Activity lifecycle method is static: " + method.Method);
                        return ExecuteInstanceRoot(method, instance, args);
                    }

                    if (string.IsNullOrEmpty(currentClass.SuperclassDescriptor))
                        break;
                    currentDescriptor = currentClass.SuperclassDescriptor;
                    continue;
                }

                if (currentDescriptor == "Landroid/app/Activity;")
                {
                    var requested = new AndroidApiMethodId(instance.TypeDescriptor, methodName, methodDescriptor);
                    var resolved = new AndroidApiMethodId(currentDescriptor, methodName, methodDescriptor);
                    if (!_api.Contains(resolved))
                        throw MissingLifecycleMethod(currentDescriptor, methodName, methodDescriptor);
                    _stepsExecuted = 0;
                    return _api.Invoke(
                        _apiSession,
                        new AndroidApiCallSite(
                            instance.TypeDescriptor + "->" + methodName + methodDescriptor,
                            -1,
                            requested,
                            resolved,
                            AndroidInvokeKind.Virtual),
                        PrependReceiver(instance, args));
                }

                break;
            }

            throw MissingLifecycleMethod(currentDescriptor, methodName, methodDescriptor);
        }

        private object ExecuteInstanceRoot(DexEncodedMethod method, DexObject instance, object[] args)
        {
            args ??= Array.Empty<object>();
            if (args.Length != method.Method.Proto.ParameterTypes.Count)
                throw new ArgumentException("Argument count does not match " + method.Method, nameof(args));
            _stepsExecuted = 0;
            return ExecuteRoot(method, PrependReceiver(instance, args));
        }

        private object ExecuteRoot(DexEncodedMethod method, object[] args)
        {
            try { return UnwrapRootReturn(Execute(method, args), method.Method.Proto.ReturnType); }
            catch (GuestExceptionCarrier carrier) { throw new UncaughtAndroidGuestException(carrier.Throwable, carrier.GuestFrames); }
        }

        /// <summary>
        /// Runs a spawned guest thread's body. Called by the real background thread
        /// with the GIL already held. With a Runnable target, runs the target's
        /// run()V; otherwise walks the receiver's virtual chain for a guest run()V
        /// override, falling back to the bound Thread.run()V (which dispatches the
        /// Runnable or no-ops), matching how invoke-virtual resolves elsewhere.
        /// </summary>
        public void RunGuestThreadBody(DexObject receiver, DexObject runnable)
        {
            if (runnable is not null)
            {
                var run = _dexSet.FindMethodExact(runnable.TypeDescriptor, "run", "()V");
                if (run is { IsStatic: false, Code: not null })
                {
                    Execute(run, PrependReceiver(runnable, Array.Empty<object>()));
                    return;
                }
                var requested = new AndroidApiMethodId(runnable.TypeDescriptor, "run", "()V");
                _api.Invoke(_apiSession, new AndroidApiCallSite(requested.ToString(), -1, requested, requested, AndroidInvokeKind.Virtual), PrependReceiver(runnable, Array.Empty<object>()));
                return;
            }

            string current = receiver.TypeDescriptor;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int depth = 0; depth < MaxCallDepth && visited.Add(current); depth++)
            {
                var method = _dexSet.FindMethodExact(current, "run", "()V");
                if (method is { IsStatic: false, Code: not null })
                {
                    Execute(method, PrependReceiver(receiver, Array.Empty<object>()));
                    return;
                }
                DexClass cls = _dexSet.FindClass(current);
                if (cls is null || string.IsNullOrEmpty(cls.SuperclassDescriptor)) break;
                current = cls.SuperclassDescriptor;
            }
            // No guest override: the bound Thread.run()V dispatches the Runnable or no-ops.
            var threadRun = new AndroidApiMethodId("Ljava/lang/Thread;", "run", "()V");
            _api.Invoke(_apiSession, new AndroidApiCallSite(threadRun.ToString(), -1, threadRun, threadRun, AndroidInvokeKind.Virtual), PrependReceiver(receiver, Array.Empty<object>()));
        }

        private static AndroidApiMethodId ApiMethod(string classDescriptor, string name, string descriptor) => new(classDescriptor, name, descriptor);

        private static object[] PrependReceiver(DexObject instance, object[] args)
        {
            var callArgs = new object[args.Length + 1];
            callArgs[0] = instance;
            Array.Copy(args, 0, callArgs, 1, args.Length);
            return callArgs;
        }

        private static MissingMethodException MissingLifecycleMethod(
            string classDescriptor,
            string methodName,
            string methodDescriptor) =>
            new("Activity lifecycle method not found: " + classDescriptor + "->" + methodName + methodDescriptor);

        private object Execute(DexEncodedMethod method, object[] args)
        {
            if (method.Code == null)
                throw new InvalidOperationException(method.Method + " no tiene bytecode (abstracto/nativo/no soportado).");

            // Bytecode operand indices (string/type/field/method pool indexes) are
            // LOCAL to the DEX file that owns this method's code, never global across
            // a multidex set. Resolve the owning file once per call frame; recursive
            // Execute calls re-resolve for their own method, so each frame reads its
            // own pools. Cross-class resolution goes through the merged _dexSet.
            var dex = method.OwningDex ?? _primaryDex;

            if (++_callDepth > MaxCallDepth)
            {
                _callDepth--;
                throw new InvalidOperationException("Profundidad de llamadas excedida en " + method.Method + " (posible recursión no soportada por este prototipo).");
            }

            try
            {
                var code = method.Code;
                var regs = new object[code.RegistersSize];
                int firstArgReg = code.RegistersSize - code.InsSize;
                LoadArguments(method, args, regs, firstArgReg);

                var insns = code.Instructions;
                int pc = 0;
                PendingInvokeResult lastInvokeResult = null;
                DexObject pendingException = null;
                int pendingHandlerAddress = -1;

                while (true)
                {
                    if ((_stepsExecuted & 0xff) == 0)
                        _apiSession.CancellationToken.ThrowIfCancellationRequested();

                    if (pc < 0 || pc >= insns.Length)
                        throw new InvalidOperationException(method.Method + ": fin de método alcanzado sin instrucción return.");

                    if (++_stepsExecuted > _maxStepsPerInvocation)
                        throw new InvalidOperationException("Límite de pasos de ejecución excedido (posible bucle infinito) en " + method.Method);

                    ushort unit = insns[pc];
                    int op = unit & 0xFF;
                    int hiByte = (unit >> 8) & 0xFF;      // "AA" cuando el formato usa un byte completo
                    int n1 = hiByte & 0xF;                  // nibble bajo del byte alto
                    int n2 = (hiByte >> 4) & 0xF;            // nibble alto del byte alto

                    if (lastInvokeResult != null && (lastInvokeResult.NextPc != pc || op is not (0x0a or 0x0b or 0x0c)))
                        lastInvokeResult = null;

                    try
                    {
                    switch (op)
                    {
                        case 0x00: // nop (10x)
                            pc += 1;
                            break;

                        case 0x01: case 0x07: // move / move-object (12x)
                            regs[n1] = regs[n2];
                            pc += 1;
                            break;

                        case 0x04: // move-wide (12x)
                            SetWide(regs, n1, GetWide(regs, n2));
                            pc += 1;
                            break;

                        case 0x02: case 0x08: // move/from16, move-object/from16 (22x)
                            regs[hiByte] = regs[insns[pc + 1]];
                            pc += 2;
                            break;

                        case 0x05: // move-wide/from16 (22x)
                            SetWide(regs, hiByte, GetWide(regs, insns[pc + 1]));
                            pc += 2;
                            break;

                        case 0x03: // move/16 (32x)
                            regs[insns[pc + 1]] = regs[insns[pc + 2]];
                            pc += 3;
                            break;

                        case 0x06: // move-wide/16 (32x)
                            SetWide(regs, insns[pc + 1], GetWide(regs, insns[pc + 2]));
                            pc += 3;
                            break;

                        case 0x0a: // move-result (11x)
                            if (lastInvokeResult == null || lastInvokeResult.ReturnDescriptor is "V" or "J" or "D" || IsReferenceDescriptor(lastInvokeResult.ReturnDescriptor))
                                throw new InvalidOperationException("move-result without an immediately preceding word result.");
                            regs[hiByte] = lastInvokeResult.Value;
                            lastInvokeResult = null;
                            pc += 1;
                            break;

                        case 0x0b: // move-result-wide
                            if (lastInvokeResult?.Value is not WideValue wideResult || lastInvokeResult.ReturnDescriptor is not ("J" or "D")) throw new InvalidOperationException("move-result-wide without an immediately preceding wide result.");
                            SetWide(regs, hiByte, wideResult.Bits);
                            lastInvokeResult = null;
                            pc += 1;
                            break;

                        case 0x0c: // move-result-object
                            if (lastInvokeResult == null || !IsReferenceDescriptor(lastInvokeResult.ReturnDescriptor))
                                throw new InvalidOperationException("move-result-object without an immediately preceding reference result.");
                            regs[hiByte] = lastInvokeResult.Value;
                            lastInvokeResult = null;
                            pc += 1;
                            break;

                        case 0x0d: // move-exception
                            if (pendingException == null || pendingHandlerAddress != pc) throw new InvalidOperationException("move-exception outside an active handler entry.");
                            regs[hiByte] = pendingException;
                            pendingException = null;
                            pendingHandlerAddress = -1;
                            pc += 1;
                            break;

                        case 0x0e: // return-void (10x)
                            return null;

                        case 0x0f: // return (11x)
                            return regs[hiByte];

                        case 0x11: // return-object
                            if (!IsReferenceDescriptor(method.Method.Proto.ReturnType) || (!IsNullReference(regs[hiByte]) && !IsRegisterAssignable(regs[hiByte], method.Method.Proto.ReturnType)))
                                throw new InvalidOperationException("return-object value is not assignable to " + method.Method.Proto.ReturnType + ".");
                            return IsNullReference(regs[hiByte]) ? null : regs[hiByte];

                        case 0x10: // return-wide
                            return new WideValue(GetWide(regs, hiByte));

                        case 0x12: // const/4 vA, #+B (11n)
                            regs[n1] = SignExtend4(n2);
                            pc += 1;
                            break;

                        case 0x13: // const/16 (21s)
                            regs[hiByte] = (int)(short)insns[pc + 1];
                            pc += 2;
                            break;

                        case 0x16: // const-wide/16
                            SetWide(regs, hiByte, unchecked((ulong)(long)(short)insns[pc + 1]));
                            pc += 2;
                            break;

                        case 0x17: // const-wide/32
                            SetWide(regs, hiByte, unchecked((ulong)(long)(int)(insns[pc + 1] | (insns[pc + 2] << 16))));
                            pc += 3;
                            break;

                        case 0x18: // const-wide
                            SetWide(regs, hiByte, (ulong)insns[pc + 1] | ((ulong)insns[pc + 2] << 16) | ((ulong)insns[pc + 3] << 32) | ((ulong)insns[pc + 4] << 48));
                            pc += 5;
                            break;

                        case 0x19: // const-wide/high16
                            SetWide(regs, hiByte, (ulong)insns[pc + 1] << 48);
                            pc += 2;
                            break;

                        case 0x14: // const vAA, #+BBBBBBBB (31i)
                            regs[hiByte] = insns[pc + 1] | (insns[pc + 2] << 16);
                            pc += 3;
                            break;

                        case 0x15: // const/high16 (21h)
                            regs[hiByte] = ((int)(uint)insns[pc + 1]) << 16;
                            pc += 2;
                            break;

                        case 0x1a: // const-string vAA, string@BBBB (21c)
                            regs[hiByte] = dex.Strings[insns[pc + 1]];
                            pc += 2;
                            break;

                        case 0x1b: // const-string/jumbo vAA, string@BBBBBBBB (31c)
                            regs[hiByte] = dex.Strings[insns[pc + 1] | (insns[pc + 2] << 16)];
                            pc += 3;
                            break;

                        case 0x1c: // const-class vAA, type@BBBB (21c)
                            regs[hiByte] = new DexObject(dex.TypeDescriptors[insns[pc + 1]]);
                            pc += 2;
                            break;

                        case 0x1d: case 0x1e: // monitor-enter / monitor-exit vAA (11x)
                            {
                                // Real per-object monitor semantics via the CLR's own
                                // Monitor, on the guest object reference (DexObject/
                                // string/array): real reentrancy and real blocking for
                                // free, no lock table of our own. monitor-enter can
                                // block, so the GIL is released while acquiring the
                                // monitor and reacquired after (never hold the GIL
                                // while blocked on a monitor — that would deadlock
                                // every other thread). monitor-exit is non-blocking.
                                // Real Dalvik requires a non-null receiver.
                                if (IsNullReference(regs[hiByte]))
                                    throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                if (op == 0x1d)
                                {
                                    using (_gil.BeginBlocking())
                                        Monitor.Enter(regs[hiByte]!);
                                }
                                else
                                {
                                    Monitor.Exit(regs[hiByte]!);
                                }
                                pc += 1;
                            }
                            break;

                        case 0x1f: // check-cast (21c)
                            if (regs[hiByte] != null && !IsRegisterAssignable(regs[hiByte], dex.TypeDescriptors[insns[pc + 1]]))
                                throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/ClassCastException;"));
                            pc += 2;
                            break;

                        case 0x27: // throw vAA
                            if (regs[hiByte] == null || regs[hiByte] is int zero && zero == 0)
                            {
                                _apiSession.RecordGuestThrow(method.Method.ToString(), pc, "Ljava/lang/NullPointerException;");
                                throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;", "throw null")) { TraceRecorded = true };
                            }
                            if (regs[hiByte] is not DexObject throwable || !IsTypeAssignable(throwable.TypeDescriptor, "Ljava/lang/Throwable;"))
                                throw new InvalidOperationException("throw register does not contain a guest Throwable.");
                            _apiSession.RecordGuestThrow(method.Method.ToString(), pc, throwable.TypeDescriptor);
                            throw new GuestExceptionCarrier(throwable) { TraceRecorded = true };

                        case 0x20: // instance-of vA, vB, type@CCCC (22c) - heurística simplificada
                            {
                                string wanted = dex.TypeDescriptors[insns[pc + 1]];
                                object obj = regs[n2];
                                bool isInst = obj != null && (
                                    wanted == "Ljava/lang/Object;" ||
                                    (obj is string && wanted == "Ljava/lang/String;") ||
                                    (obj is DexObject dobj && dobj.TypeDescriptor == wanted));
                                regs[n1] = isInst ? 1 : 0;
                                pc += 2;
                            }
                            break;

                        case 0x21: // array-length vA, vB (12x)
                            {
                                if (regs[n2] == null || regs[n2] is int nullArray && nullArray == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                regs[n1] = regs[n2] switch { DexArray array => array.Length, object[] legacy => legacy.Length, _ => throw new InvalidOperationException("array-length sobre un registro que no es un array.") };
                                pc += 1;
                            }
                            break;

                        case 0x22: // new-instance vAA, type@BBBB (21c)
                            EnsureClassInitialized(dex.TypeDescriptors[insns[pc + 1]]);
                            regs[hiByte] = new DexObject(dex.TypeDescriptors[insns[pc + 1]]);
                            pc += 2;
                            break;

                        case 0x23: // new-array vA, vB, type@CCCC (22c)
                            {
                                int size = ToInt(regs[n2]);
                                if (size < 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NegativeArraySizeException;", size.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                                string arrayType = dex.TypeDescriptors[insns[pc + 1]];
                                regs[n1] = new DexArray(arrayType, size);
                                pc += 2;
                            }
                            break;

                        case 0x24: case 0x25: // filled-new-array / filled-new-array/range (35c/3rc)
                            {
                                // Same physical register-list shape as invoke-*, but the
                                // operand at insns[pc+1] is a TYPE index (not a method
                                // index): allocate an array of that type with length =
                                // argument count, fill it from the listed registers in
                                // order, and publish it as a result for a following
                                // move-result-object (legal to discard too). Wide
                                // component types (long[]/double[]) are rejected by
                                // DexArray.Set itself — no redundant check here.
                                int argCount = op == 0x24 ? (unit >> 12) & 0xF : hiByte;
                                string arrayType = dex.TypeDescriptors[insns[pc + 1]];
                                var array = new DexArray(arrayType, argCount);
                                if (op == 0x24)
                                {
                                    int g = (unit >> 8) & 0xF;
                                    ushort unit2 = insns[pc + 2];
                                    int[] regNums = { unit2 & 0xF, (unit2 >> 4) & 0xF, (unit2 >> 8) & 0xF, (unit2 >> 12) & 0xF, g };
                                    for (int i = 0; i < argCount; i++) array.Set(i, regs[regNums[i]]);
                                }
                                else
                                {
                                    int firstReg = insns[pc + 2];
                                    for (int i = 0; i < argCount; i++) array.Set(i, regs[firstReg + i]);
                                }
                                lastInvokeResult = new(array, arrayType, pc + 3);
                                pc += 3;
                            }
                            break;

                        case 0x26: // fill-array-data vAA, +BBBBBBBB (31t)
                            {
                                if (IsNullReference(regs[hiByte]))
                                    throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                if (regs[hiByte] is not DexArray array)
                                    throw new InvalidOperationException("fill-array-data register does not contain a guest array.");
                                int payloadAddress = pc + unchecked((int)((uint)insns[pc + 1] | (uint)insns[pc + 2] << 16));
                                FillArrayFromPayload(insns, payloadAddress, array);
                                // No result, no branch: falls through past the 3-unit
                                // instruction. Not a move-result producer.
                                pc += 3;
                            }
                            break;

                        case 0x28: // goto +AA (10t)
                            pc += (sbyte)(byte)hiByte;
                            break;

                        case 0x29: // goto/16 +AAAA (20t)
                            pc += (short)insns[pc + 1];
                            break;

                        case 0x2b: case 0x2c: // packed-switch / sparse-switch vAA, +BBBBBBBB (31t)
                            {
                                int key = ToInt(regs[hiByte]);
                                int payloadAddress = pc + unchecked((int)((uint)insns[pc + 1] | (uint)insns[pc + 2] << 16));
                                int? target = op == 0x2b
                                    ? PackedSwitchTarget(insns, payloadAddress, key)
                                    : SparseSwitchTarget(insns, payloadAddress, key);
                                // Payload targets are signed offsets in code units from
                                // the switch instruction's own pc, not from the payload.
                                pc = target.HasValue ? pc + target.Value : pc + 3;
                            }
                            break;

                        case 0x2d: case 0x2e: // cmpl-float / cmpg-float
                            {
                                ushort unit2 = insns[pc + 1];
                                float left = BitConverter.Int32BitsToSingle(ToInt(regs[unit2 & 0xff]));
                                float right = BitConverter.Int32BitsToSingle(ToInt(regs[unit2 >> 8]));
                                regs[hiByte] = float.IsNaN(left) || float.IsNaN(right) ? (op == 0x2d ? -1 : 1) : left.CompareTo(right);
                                pc += 2;
                            }
                            break;

                        case 0x2f: case 0x30: // cmpl-double / cmpg-double
                            {
                                ushort unit2 = insns[pc + 1];
                                double left = BitConverter.Int64BitsToDouble(unchecked((long)GetWide(regs, unit2 & 0xff)));
                                double right = BitConverter.Int64BitsToDouble(unchecked((long)GetWide(regs, unit2 >> 8)));
                                regs[hiByte] = double.IsNaN(left) || double.IsNaN(right) ? (op == 0x2f ? -1 : 1) : left.CompareTo(right);
                                pc += 2;
                            }
                            break;

                        case 0x31: // cmp-long
                            {
                                ushort unit2 = insns[pc + 1];
                                long left = unchecked((long)GetWide(regs, unit2 & 0xff));
                                long right = unchecked((long)GetWide(regs, unit2 >> 8));
                                regs[hiByte] = left < right ? -1 : left > right ? 1 : 0;
                                pc += 2;
                            }
                            break;

                        case 0x32: case 0x33: case 0x34: case 0x35: case 0x36: case 0x37:
                            // if-eq/ne/lt/ge/gt/le vA, vB, +CCCC (22t)
                            {
                                bool taken = CompareBranch(op - 0x32, regs[n1], regs[n2]);
                                pc += taken ? (short)insns[pc + 1] : 2;
                            }
                            break;

                        case 0x38: case 0x39: case 0x3a: case 0x3b: case 0x3c: case 0x3d:
                            // if-eqz/nez/ltz/gez/gtz/lez vAA, +BBBB (21t)
                            {
                                bool taken = CompareZeroBranch(op - 0x38, regs[hiByte]);
                                pc += taken ? (short)insns[pc + 1] : 2;
                            }
                            break;

                        case 0x44: case 0x46: case 0x47: case 0x48: case 0x49: case 0x4a:
                            // aget* vAA, vBB, vCC (23x)
                            {
                                ushort unit2 = insns[pc + 1];
                                int arrReg = unit2 & 0xFF;
                                int idxReg = (unit2 >> 8) & 0xFF;
                                int index = ToInt(regs[idxReg]);
                                regs[hiByte] = GetArrayValue(op, regs[arrReg], index);
                                pc += 2;
                            }
                            break;

                        case 0x45: // aget-wide
                            {
                                ushort unit2 = insns[pc + 1];
                                var arr = RequireGuestDexArray(regs[unit2 & 0xff], wide: true);
                                if (arr.ElementDescriptor is not ("J" or "D")) throw new InvalidOperationException("aget-wide requires a long[] or double[] array.");
                                int index = ToInt(regs[unit2 >> 8]);
                                RequireArrayIndex(arr.Length, index);
                                SetWide(regs, hiByte, arr.GetWide(index));
                                pc += 2;
                            }
                            break;

                        case 0x4b: case 0x4d: case 0x4e: case 0x4f: case 0x50: case 0x51:
                            // aput* vAA, vBB, vCC (23x)
                            {
                                ushort unit2 = insns[pc + 1];
                                int arrReg = unit2 & 0xFF;
                                int idxReg = (unit2 >> 8) & 0xFF;
                                int index = ToInt(regs[idxReg]);
                                SetArrayValue(op, regs[arrReg], index, regs[hiByte]);
                                pc += 2;
                            }
                            break;

                        case 0x4c: // aput-wide
                            {
                                ushort unit2 = insns[pc + 1];
                                var arr = RequireGuestDexArray(regs[unit2 & 0xff], wide: true);
                                if (arr.ElementDescriptor is not ("J" or "D")) throw new InvalidOperationException("aput-wide requires a long[] or double[] array.");
                                int index = ToInt(regs[unit2 >> 8]);
                                RequireArrayIndex(arr.Length, index);
                                arr.SetWide(index, GetWide(regs, hiByte));
                                pc += 2;
                            }
                            break;

                        case 0x52: case 0x54: case 0x55: case 0x56: case 0x57: case 0x58:
                            // iget* vA, vB, field@CCCC (22c)
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                var obj = regs[n2] as DexObject;
                                if (regs[n2] == null || regs[n2] is int nullIget && nullIget == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                if (obj == null) throw new InvalidOperationException("iget sobre un registro que no es una instancia: " + fref);
                                object val;
                                obj.InstanceFields.TryGetValue(FieldKey(fref), out val);
                                regs[n1] = val ?? DefaultFieldValue(fref.Type);
                                pc += 2;
                            }
                            break;

                        case 0x53: // iget-wide
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                if (regs[n2] == null || regs[n2] is int nullIgetWide && nullIgetWide == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                var obj = regs[n2] as DexObject ?? throw new InvalidOperationException("iget-wide requires an instance: " + fref);
                                ulong bits = obj.InstanceFields.TryGetValue(FieldKey(fref), out var val) && val is WideValue wide ? wide.Bits : 0UL;
                                SetWide(regs, n1, bits);
                                pc += 2;
                            }
                            break;

                        case 0x59: case 0x5b: case 0x5c: case 0x5d: case 0x5e: case 0x5f:
                            // iput* vA, vB, field@CCCC (22c)
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                var obj = regs[n2] as DexObject;
                                if (regs[n2] == null || regs[n2] is int nullIput && nullIput == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                if (obj == null) throw new InvalidOperationException("iput sobre un registro que no es una instancia: " + fref);
                                obj.InstanceFields[FieldKey(fref)] = regs[n1];
                                obj.InstanceFields[fref.Name] = regs[n1];
                                pc += 2;
                            }
                            break;

                        case 0x5a: // iput-wide
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                if (regs[n2] == null || regs[n2] is int nullIputWide && nullIputWide == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
                                var obj = regs[n2] as DexObject ?? throw new InvalidOperationException("iput-wide requires an instance: " + fref);
                                var wide = new WideValue(GetWide(regs, n1));
                                obj.InstanceFields[FieldKey(fref)] = wide;
                                if (fref.Type == "D") obj.InstanceFields[fref.Name] = BitConverter.Int64BitsToDouble(unchecked((long)wide.Bits));
                                else obj.InstanceFields[fref.Name] = unchecked((long)wide.Bits);
                                pc += 2;
                            }
                            break;

                        case 0x60: case 0x62: case 0x63: case 0x64: case 0x65: case 0x66:
                            // sget* vAA, field@BBBB (21c)
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                EnsureClassInitialized(fref.ClassDescriptor);
                                object val;
                                _staticFields.TryGetValue(FieldKey(fref), out val);
                                regs[hiByte] = val ?? DefaultFieldValue(fref.Type);
                                pc += 2;
                            }
                            break;

                        case 0x61: // sget-wide
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                EnsureClassInitialized(fref.ClassDescriptor);
                                ulong bits = _staticFields.TryGetValue(FieldKey(fref), out var val) && val is WideValue wide ? wide.Bits : 0UL;
                                SetWide(regs, hiByte, bits);
                                pc += 2;
                            }
                            break;

                        case 0x67: case 0x69: case 0x6a: case 0x6b: case 0x6c:
                            // sput* vAA, field@BBBB (21c)
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                EnsureClassInitialized(fref.ClassDescriptor);
                                _staticFields[FieldKey(fref)] = regs[hiByte];
                                pc += 2;
                            }
                            break;

                        case 0x68: // sput-wide
                            {
                                var fref = dex.Fields[insns[pc + 1]];
                                EnsureClassInitialized(fref.ClassDescriptor);
                                _staticFields[FieldKey(fref)] = new WideValue(GetWide(regs, hiByte));
                                pc += 2;
                            }
                            break;

                        case 0x6e: case 0x6f: case 0x70: case 0x71: case 0x72:
                            // invoke-virtual/super/direct/static/interface {vC..vG}, meth@BBBB (35c)
                            {
                                int argCount = (unit >> 12) & 0xF;
                                int g = (unit >> 8) & 0xF;
                                int methodIdx = insns[pc + 1];
                                ushort unit2 = insns[pc + 2];
                                int c = unit2 & 0xF;
                                int d = (unit2 >> 4) & 0xF;
                                int e = (unit2 >> 8) & 0xF;
                                int f = (unit2 >> 12) & 0xF;
                                int[] regNums = { c, d, e, f, g };
                                var callArgs = DecodeInvokeArguments(dex.Methods[methodIdx], InvokeKindForOpcode(op), regs, regNums, argCount);
                                object result = InvokeResolved(
                                    dex.Methods[methodIdx],
                                    callArgs,
                                    InvokeKindForOpcode(op),
                                    method,
                                    pc);
                                lastInvokeResult = new(result, dex.Methods[methodIdx].Proto.ReturnType, pc + 3);
                                pc += 3;
                            }
                            break;

                        case 0x74: case 0x75: case 0x76: case 0x77: case 0x78:
                            // invoke-*/range {vCCCC..vNNNN}, meth@BBBB (3rc)
                            {
                                int argCount = hiByte;
                                int methodIdx = insns[pc + 1];
                                int firstReg = insns[pc + 2];
                                int[] regNums = Enumerable.Range(firstReg, argCount).ToArray();
                                var callArgs = DecodeInvokeArguments(dex.Methods[methodIdx], InvokeKindForOpcode(op), regs, regNums, argCount);
                                object result = InvokeResolved(
                                    dex.Methods[methodIdx],
                                    callArgs,
                                    InvokeKindForOpcode(op),
                                    method,
                                    pc);
                                lastInvokeResult = new(result, dex.Methods[methodIdx].Proto.ReturnType, pc + 3);
                                pc += 3;
                            }
                            break;

                        case 0x7b: // neg-int vA, vB (12x)
                            regs[n1] = -ToInt(regs[n2]);
                            pc += 1;
                            break;

                        case 0x7c: // not-int vA, vB (12x)
                            regs[n1] = ~ToInt(regs[n2]);
                            pc += 1;
                            break;

                        case 0x7d: // neg-long
                            SetWide(regs, n1, unchecked((ulong)-unchecked((long)GetWide(regs, n2)))); pc += 1; break;
                        case 0x7e: // not-long
                            SetWide(regs, n1, ~GetWide(regs, n2)); pc += 1; break;
                        case 0x7f: // neg-float vA, vB (12x)
                            regs[n1] = BitConverter.SingleToInt32Bits(-BitConverter.Int32BitsToSingle(ToInt(regs[n2])));
                            pc += 1;
                            break;
                        case 0x80: // neg-double
                            SetWide(regs, n1, unchecked((ulong)BitConverter.DoubleToInt64Bits(-BitConverter.Int64BitsToDouble(unchecked((long)GetWide(regs, n2)))))); pc += 1; break;

                        case 0x81: SetWide(regs, n1, unchecked((ulong)(long)ToInt(regs[n2]))); pc++; break;
                        case 0x82: regs[n1] = BitConverter.SingleToInt32Bits(ToInt(regs[n2])); pc++; break;
                        case 0x83: SetWide(regs, n1, unchecked((ulong)BitConverter.DoubleToInt64Bits(ToInt(regs[n2])))); pc++; break;
                        case 0x84: regs[n1] = unchecked((int)GetWide(regs, n2)); pc++; break;
                        case 0x85: regs[n1] = BitConverter.SingleToInt32Bits(unchecked((long)GetWide(regs, n2))); pc++; break;
                        case 0x86: SetWide(regs, n1, unchecked((ulong)BitConverter.DoubleToInt64Bits(unchecked((long)GetWide(regs, n2))))); pc++; break;
                        case 0x87: regs[n1] = SaturatingInt(BitConverter.Int32BitsToSingle(ToInt(regs[n2]))); pc++; break;
                        case 0x88: SetWide(regs, n1, unchecked((ulong)SaturatingLong(BitConverter.Int32BitsToSingle(ToInt(regs[n2]))))); pc++; break;
                        case 0x89: SetWide(regs, n1, unchecked((ulong)BitConverter.DoubleToInt64Bits(BitConverter.Int32BitsToSingle(ToInt(regs[n2]))))); pc++; break;
                        case 0x8a: regs[n1] = SaturatingInt(BitConverter.Int64BitsToDouble(unchecked((long)GetWide(regs, n2)))); pc++; break;
                        case 0x8b: SetWide(regs, n1, unchecked((ulong)SaturatingLong(BitConverter.Int64BitsToDouble(unchecked((long)GetWide(regs, n2)))))); pc++; break;
                        case 0x8c: regs[n1] = BitConverter.SingleToInt32Bits((float)BitConverter.Int64BitsToDouble(unchecked((long)GetWide(regs, n2)))); pc++; break;

                        case 0x8d: // int-to-byte (12x)
                            regs[n1] = (int)(sbyte)ToInt(regs[n2]);
                            pc += 1;
                            break;

                        case 0x8e: // int-to-char (12x)
                            regs[n1] = (int)(char)ToInt(regs[n2]);
                            pc += 1;
                            break;

                        case 0x8f: // int-to-short (12x)
                            regs[n1] = (int)(short)ToInt(regs[n2]);
                            pc += 1;
                            break;

                        case 0x90: case 0x91: case 0x92: case 0x93: case 0x94:
                        case 0x95: case 0x96: case 0x97: case 0x98: case 0x99: case 0x9a:
                            // *-int vAA, vBB, vCC (23x)
                            {
                                ushort unit2 = insns[pc + 1];
                                int rb = unit2 & 0xFF;
                                int rc = (unit2 >> 8) & 0xFF;
                                regs[hiByte] = IntBinOp(op - 0x90, ToInt(regs[rb]), ToInt(regs[rc]));
                                pc += 2;
                            }
                            break;

                        case 0xb0: case 0xb1: case 0xb2: case 0xb3: case 0xb4:
                        case 0xb5: case 0xb6: case 0xb7: case 0xb8: case 0xb9: case 0xba:
                            // *-int/2addr vA, vB (12x)
                            regs[n1] = IntBinOp(op - 0xb0, ToInt(regs[n1]), ToInt(regs[n2]));
                            pc += 1;
                            break;

                        case >= 0x9b and <= 0xa5: // long binary
                            {
                                ushort unit2 = insns[pc + 1];
                                int kind = op - 0x9b;
                                ulong right = kind >= 8 ? unchecked((ulong)(uint)ToInt(regs[unit2 >> 8])) : GetWide(regs, unit2 >> 8);
                                SetWide(regs, hiByte, LongBinOp(kind, GetWide(regs, unit2 & 0xff), right));
                                pc += 2;
                            }
                            break;
                        case >= 0xa6 and <= 0xaa: // float binary
                            {
                                ushort unit2 = insns[pc + 1];
                                regs[hiByte] = FloatBinOp(op - 0xa6, ToInt(regs[unit2 & 0xff]), ToInt(regs[unit2 >> 8]));
                                pc += 2;
                            }
                            break;
                        case >= 0xab and <= 0xaf: // double binary
                            {
                                ushort unit2 = insns[pc + 1];
                                SetWide(regs, hiByte, DoubleBinOp(op - 0xab, GetWide(regs, unit2 & 0xff), GetWide(regs, unit2 >> 8)));
                                pc += 2;
                            }
                            break;
                        case >= 0xbb and <= 0xc5: // long /2addr
                            { int kind = op - 0xbb; ulong right = kind >= 8 ? unchecked((ulong)(uint)ToInt(regs[n2])) : GetWide(regs, n2); SetWide(regs, n1, LongBinOp(kind, GetWide(regs, n1), right)); pc++; break; }
                        case >= 0xc6 and <= 0xca: // float /2addr
                            regs[n1] = FloatBinOp(op - 0xc6, ToInt(regs[n1]), ToInt(regs[n2])); pc++; break;
                        case >= 0xcb and <= 0xcf: // double /2addr
                            SetWide(regs, n1, DoubleBinOp(op - 0xcb, GetWide(regs, n1), GetWide(regs, n2))); pc++; break;

                        case 0xd0: case 0xd1: case 0xd2: case 0xd3:
                        case 0xd4: case 0xd5: case 0xd6: case 0xd7:
                            // *-int/lit16 vA, vB, #+CCCC (22s)
                            regs[n1] = IntBinOpLit(op - 0xd0, ToInt(regs[n2]), (short)insns[pc + 1]);
                            pc += 2;
                            break;

                        case 0xd8: case 0xd9: case 0xda: case 0xdb: case 0xdc:
                        case 0xdd: case 0xde: case 0xdf: case 0xe0: case 0xe1: case 0xe2:
                            // *-int/lit8 vAA, vBB, #+CC (22b)
                            {
                                ushort unit2 = insns[pc + 1];
                                int rb = unit2 & 0xFF;
                                sbyte lit = (sbyte)((unit2 >> 8) & 0xFF);
                                regs[hiByte] = IntBinOpLit(op - 0xd8, ToInt(regs[rb]), lit);
                                pc += 2;
                            }
                            break;

                        default:
                            throw new NotImplementedException(
                                "Opcode Dalvik no soportado por este prototipo: 0x" + op.ToString("X2") +
                                " (" + DexConstants.NameOf(op) + ") en " + method.Method + " pc=" + pc);
                    }
                    }
                    catch (GuestExceptionCarrier carrier)
                    {
                        if (!carrier.TraceRecorded)
                        {
                            _apiSession.RecordGuestThrow(method.Method.ToString(), pc, carrier.Throwable.TypeDescriptor);
                            carrier.TraceRecorded = true;
                        }
                        if (TryEnterHandler(code, pc, carrier.Throwable, out int handlerAddress))
                        {
                            pendingException = carrier.Throwable;
                            pendingHandlerAddress = handlerAddress;
                            pc = handlerAddress;
                        }
                        else
                        {
                            carrier.AddFrame(method, pc);
                            throw;
                        }
                    }
                    catch (AndroidGuestArithmeticException error)
                    {
                        _apiSession.RecordGuestThrow(method.Method.ToString(), pc, "Ljava/lang/ArithmeticException;");
                        var carrier = new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/ArithmeticException;", error.Message));
                        if (TryEnterHandler(code, pc, carrier.Throwable, out int handlerAddress))
                        {
                            pendingException = carrier.Throwable; pendingHandlerAddress = handlerAddress; pc = handlerAddress;
                        }
                        else { carrier.AddFrame(method, pc); throw carrier; }
                    }
                    catch (AndroidApiNullReferenceException error)
                    {
                        _apiSession.RecordGuestThrow(method.Method.ToString(), pc, "Ljava/lang/NullPointerException;");
                        var carrier = new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;", error.Message));
                        if (TryEnterHandler(code, pc, carrier.Throwable, out int handlerAddress))
                        { pendingException = carrier.Throwable; pendingHandlerAddress = handlerAddress; pc = handlerAddress; }
                        else { carrier.AddFrame(method, pc); throw carrier; }
                    }
                    catch (AndroidApiSecurityException error)
                    {
                        _apiSession.RecordGuestThrow(method.Method.ToString(), pc, "Ljava/lang/SecurityException;");
                        var carrier = new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/SecurityException;", error.Message)) { TraceRecorded = true };
                        if (TryEnterHandler(code, pc, carrier.Throwable, out int handlerAddress)) { pendingException = carrier.Throwable; pendingHandlerAddress = handlerAddress; pc = handlerAddress; }
                        else { carrier.AddFrame(method, pc); throw carrier; }
                    }
                    catch (AndroidGuestArrayIndexException error)
                    {
                        _apiSession.RecordGuestThrow(method.Method.ToString(), pc, "Ljava/lang/ArrayIndexOutOfBoundsException;");
                        var carrier = new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/ArrayIndexOutOfBoundsException;", error.Message)) { TraceRecorded = true };
                        if (TryEnterHandler(code, pc, carrier.Throwable, out int handlerAddress)) { pendingException = carrier.Throwable; pendingHandlerAddress = handlerAddress; pc = handlerAddress; }
                        else { carrier.AddFrame(method, pc); throw carrier; }
                    }
                }
            }
            finally
            {
                _callDepth--;
            }
        }

        private object InvokeResolved(
            DexMethodRef methodRef,
            object[] args,
            AndroidInvokeKind invokeKind,
            DexEncodedMethod caller,
            int dexPc)
        {
            string descriptor = methodRef.Proto.Descriptor();
            var requestedApi = new AndroidApiMethodId(methodRef.ClassDescriptor, methodRef.Name, descriptor);

            if (invokeKind is AndroidInvokeKind.Static or AndroidInvokeKind.Direct)
            {
                var exact = _dexSet.FindMethodExact(methodRef.ClassDescriptor, methodRef.Name, descriptor);
                if (exact?.Code != null)
                {
                    if (invokeKind == AndroidInvokeKind.Static && !exact.IsStatic)
                        throw new InvalidOperationException("invoke-static targeted instance method: " + exact.Method);
                    if (invokeKind == AndroidInvokeKind.Direct && exact.IsStatic)
                        throw new InvalidOperationException("invoke-direct targeted static method: " + exact.Method);
                    if (invokeKind == AndroidInvokeKind.Direct) RequireNonNullReceiver(args, requestedApi);
                    if (invokeKind == AndroidInvokeKind.Static)
                        EnsureClassInitialized(exact.Method.ClassDescriptor);
                    return Execute(exact, args);
                }
                return InvokeApi(methodRef, args, invokeKind, caller, dexPc, requestedApi, requestedApi);
            }

            RequireNonNullReceiver(args, requestedApi);
            string current = invokeKind == AndroidInvokeKind.Super
                ? SuperclassOf(caller.Method.ClassDescriptor)
                : DynamicDescriptor(args[0]);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                var dexMethod = _dexSet.FindMethodExact(current, methodRef.Name, descriptor);
                if (dexMethod?.Code != null)
                {
                    if (dexMethod.IsStatic)
                        throw new InvalidOperationException(invokeKind + " targeted static method: " + dexMethod.Method);
                    return Execute(dexMethod, args);
                }
                var candidate = new AndroidApiMethodId(current, methodRef.Name, descriptor);
                if (_api.Contains(candidate))
                    return InvokeApi(methodRef, args, invokeKind, caller, dexPc, requestedApi, candidate);
                current = SuperclassOf(current);
            }

            return InvokeApi(methodRef, args, invokeKind, caller, dexPc, requestedApi, requestedApi);
        }

        private bool TryEnterHandler(DexCodeItem code, int throwingPc, DexObject throwable, out int handlerAddress)
        {
            foreach (DexTryBlock block in code.TryBlocks)
            {
                if (throwingPc < block.StartAddress || throwingPc >= block.StartAddress + block.InstructionCount) continue;
                foreach (DexExceptionHandler handler in block.Handlers)
                {
                    if (handler.IsCatchAll || IsTypeAssignable(throwable.TypeDescriptor, handler.TypeDescriptor))
                    { handlerAddress = handler.TargetAddress; return true; }
                }
                break;
            }
            handlerAddress = -1;
            return false;
        }

        private bool IsRegisterAssignable(object value, string expected) => value switch
        {
            int zero when zero == 0 && (expected.StartsWith("L", StringComparison.Ordinal) || expected.StartsWith("[", StringComparison.Ordinal)) => true,
            string => IsTypeAssignable("Ljava/lang/String;", expected),
            DexObject obj => IsTypeAssignable(obj.TypeDescriptor, expected),
            DexArray array => IsTypeAssignable(array.ArrayDescriptor, expected),
            _ => false
        };

        private object GetArrayValue(int opcode, object value, int index)
        {
            if (value is null || value is int zero && zero == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
            if (value is DexArray array)
            {
                RequireArrayOpcode(opcode, array.ElementDescriptor, put: false);
                RequireArrayIndex(array.Length, index);
                object element = array.Get(index);
                if (opcode == 0x46 && !IsNullReference(element) && !IsRegisterAssignable(element, array.ElementDescriptor)) throw new InvalidOperationException("aget-object element is not assignable to " + array.ElementDescriptor + ".");
                return opcode switch { 0x47 => ToInt(element) == 0 ? 0 : 1, 0x48 => (int)(sbyte)ToInt(element), 0x49 => (int)(char)ToInt(element), 0x4a => (int)(short)ToInt(element), _ => element };
            }
            if (value is object[] legacy && opcode == 0x46) { RequireArrayIndex(legacy.Length, index); return legacy[index]; }
            throw new InvalidOperationException("aget register is not an array.");
        }

        private void SetArrayValue(int opcode, object value, int index, object element)
        {
            if (value is null || value is int zero && zero == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
            if (value is DexArray array)
            {
                RequireArrayOpcode(opcode, array.ElementDescriptor, put: true);
                RequireArrayIndex(array.Length, index);
                if (opcode == 0x4d)
                {
                    if (!IsNullReference(element) && !IsRegisterAssignable(element, array.ElementDescriptor)) throw new InvalidOperationException("aput-object value is not assignable to " + array.ElementDescriptor + ".");
                    array.Set(index, IsNullReference(element) ? null : element); return;
                }
                array.Set(index, opcode switch { 0x4e => ToInt(element) == 0 ? 0 : 1, 0x4f => (int)(sbyte)ToInt(element), 0x50 => (int)(char)ToInt(element), 0x51 => (int)(short)ToInt(element), _ => ToInt(element) }); return;
            }
            if (value is object[] legacy && opcode == 0x4d) { RequireArrayIndex(legacy.Length, index); legacy[index] = IsNullReference(element) ? null : element; return; }
            throw new InvalidOperationException("aput register is not an array.");
        }

        private static void RequireArrayOpcode(int opcode, string elementDescriptor, bool put)
        {
            int normalized = put ? opcode - 7 : opcode;
            bool valid = normalized switch
            {
                0x44 => elementDescriptor is "I" or "F",
                0x46 => IsReferenceDescriptor(elementDescriptor),
                0x47 => elementDescriptor == "Z",
                0x48 => elementDescriptor == "B",
                0x49 => elementDescriptor == "C",
                0x4a => elementDescriptor == "S",
                _ => false
            };
            if (!valid) throw new InvalidOperationException($"Array opcode 0x{opcode:X2} does not match component {elementDescriptor}.");
        }

        private static bool IsReferenceDescriptor(string descriptor) => descriptor.StartsWith("L", StringComparison.Ordinal) || descriptor.StartsWith("[", StringComparison.Ordinal);
        private static bool IsNullReference(object value) => value is null || value is int zero && zero == 0;

        private static void RequireArrayIndex(int length, int index)
        {
            if ((uint)index >= (uint)length) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/ArrayIndexOutOfBoundsException;", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        private object InvokeApi(DexMethodRef methodRef, object[] args, AndroidInvokeKind invokeKind, DexEncodedMethod caller, int dexPc, AndroidApiMethodId requestedApi, AndroidApiMethodId resolvedApi)
        {
            object result = _api.Invoke(
                _apiSession,
                new AndroidApiCallSite(caller.Method.ToString(), dexPc, requestedApi, resolvedApi, invokeKind),
                NormalizeApiArguments(methodRef, args, invokeKind));
            return methodRef.Proto.ReturnType switch
            {
                "J" => new WideValue(unchecked((ulong)(long)result)),
                "D" => new WideValue(unchecked((ulong)BitConverter.DoubleToInt64Bits((double)result))),
                _ => result
            };
        }

        private static object UnwrapRootReturn(object result, string descriptor) => descriptor switch
        {
            "J" when result is WideValue wide => unchecked((long)wide.Bits),
            "D" when result is WideValue wide => BitConverter.Int64BitsToDouble(unchecked((long)wide.Bits)),
            "F" when result is int bits => BitConverter.Int32BitsToSingle(bits),
            _ => result
        };

        private void LoadArguments(DexEncodedMethod method, object[] args, object[] regs, int firstRegister)
        {
            int logical = 0, register = firstRegister;
            if (!method.IsStatic)
            {
                if (args.Length == 0) throw new ArgumentException("Missing DEX receiver.", nameof(args));
                if (!IsNullReference(args[logical]) && !IsRegisterAssignable(args[logical], method.Method.ClassDescriptor)) throw new ArgumentException("DEX receiver is not assignable to " + method.Method.ClassDescriptor + ".", nameof(args));
                regs[register++] = args[logical++];
            }
            foreach (string type in method.Method.Proto.ParameterTypes)
            {
                if (logical >= args.Length) throw new ArgumentException("Missing argument for " + method.Method, nameof(args));
                object value = args[logical++];
                if (IsReferenceDescriptor(type) && !IsNullReference(value) && !IsRegisterAssignable(value, type)) throw new ArgumentException("DEX argument is not assignable to " + type + " for " + method.Method + ".", nameof(args));
                if (type == "J") SetWide(regs, register, unchecked((ulong)Convert.ToInt64(value)));
                else if (type == "D") SetWide(regs, register, unchecked((ulong)BitConverter.DoubleToInt64Bits(Convert.ToDouble(value))));
                else if (type == "F") regs[register] = BitConverter.SingleToInt32Bits(Convert.ToSingle(value));
                else regs[register] = value;
                register += type is "J" or "D" ? 2 : 1;
            }
            if (logical != args.Length || register != firstRegister + method.Code.InsSize)
                throw new ArgumentException("DEX argument words do not match ins_size for " + method.Method, nameof(args));
        }

        private static object[] DecodeInvokeArguments(DexMethodRef method, AndroidInvokeKind kind, object[] regs, int[] registerWords, int encodedWordCount)
        {
            int expectedWords = kind == AndroidInvokeKind.Static ? 0 : 1;
            foreach (string type in method.Proto.ParameterTypes) expectedWords += type is "J" or "D" ? 2 : 1;
            if (encodedWordCount != expectedWords) throw new InvalidOperationException($"Invoke word count for {method} must be {expectedWords}, observed {encodedWordCount}.");
            var result = new List<object>();
            int word = 0;
            if (kind != AndroidInvokeKind.Static) result.Add(regs[registerWords[word++]]);
            foreach (string type in method.Proto.ParameterTypes)
            {
                int register = registerWords[word++];
                if (type is "J" or "D")
                {
                    if (word >= registerWords.Length || registerWords[word++] != register + 1)
                        throw new InvalidOperationException("Wide invoke argument registers must form a consecutive pair: " + method);
                    ulong bits = GetWide(regs, register);
                    if (type == "J") result.Add(unchecked((long)bits));
                    else result.Add(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
                }
                else result.Add(regs[register]);
            }
            return result.ToArray();
        }

        private static void SetWide(object[] regs, int register, ulong bits)
        {
            if ((uint)register >= (uint)(regs.Length - 1)) throw new InvalidOperationException("Wide register pair exceeds the frame.");
            regs[register] = new WideValue(bits);
            regs[register + 1] = WideHigh;
        }

        private static ulong GetWide(object[] regs, int register)
        {
            if ((uint)register >= (uint)(regs.Length - 1) || regs[register] is not WideValue wide || !ReferenceEquals(regs[register + 1], WideHigh))
                throw new InvalidOperationException("Invalid or overlapping wide register pair at v" + register + ".");
            return wide.Bits;
        }

        private static DexArray RequireDexArray(object value, bool wide)
        {
            if (value is not DexArray array) throw new InvalidOperationException("Expected a typed DEX array.");
            bool isWide = array.ElementDescriptor is "J" or "D";
            if (wide != isWide) throw new InvalidOperationException("DEX array element width mismatch.");
            return array;
        }

        private static DexArray RequireGuestDexArray(object value, bool wide)
        {
            if (value is null || value is int zero && zero == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/NullPointerException;"));
            return RequireDexArray(value, wide);
        }

        private static string FieldKey(DexFieldRef field) => field.ClassDescriptor + "->" + field.Name + ":" + field.Type;
        private static object DefaultFieldValue(string descriptor) => descriptor.StartsWith("L", StringComparison.Ordinal) || descriptor.StartsWith("[", StringComparison.Ordinal) ? null : 0;

        private static ulong LongBinOp(int kind, ulong aBits, ulong bBits)
        {
            long a = unchecked((long)aBits), b = unchecked((long)bBits);
            return kind switch
            {
                0 => unchecked((ulong)(a + b)), 1 => unchecked((ulong)(a - b)), 2 => unchecked((ulong)(a * b)),
                3 => b == 0 ? throw new AndroidGuestArithmeticException("divide by zero") : unchecked((ulong)(a == long.MinValue && b == -1 ? long.MinValue : a / b)),
                4 => b == 0 ? throw new AndroidGuestArithmeticException("divide by zero") : unchecked((ulong)(a == long.MinValue && b == -1 ? 0 : a % b)),
                5 => aBits & bBits, 6 => aBits | bBits, 7 => aBits ^ bBits,
                8 => unchecked((ulong)(a << ((int)b & 63))), 9 => unchecked((ulong)(a >> ((int)b & 63))), 10 => aBits >> ((int)b & 63),
                _ => throw new NotImplementedException("Unsupported long operation " + kind)
            };
        }

        private static ulong DoubleBinOp(int kind, ulong aBits, ulong bBits)
        {
            double a = BitConverter.Int64BitsToDouble(unchecked((long)aBits)), b = BitConverter.Int64BitsToDouble(unchecked((long)bBits));
            double value = kind switch { 0 => a + b, 1 => a - b, 2 => a * b, 3 => a / b, 4 => a % b, _ => throw new NotImplementedException() };
            return unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>Float arithmetic on 32-bit raw IEEE 754 bit patterns (float
        /// registers are stored as int-boxed bits, see the conversion opcodes).</summary>
        private static int FloatBinOp(int kind, int aBits, int bBits)
        {
            float a = BitConverter.Int32BitsToSingle(aBits), b = BitConverter.Int32BitsToSingle(bBits);
            float value = kind switch { 0 => a + b, 1 => a - b, 2 => a * b, 3 => a / b, 4 => a % b, _ => throw new NotImplementedException() };
            return BitConverter.SingleToInt32Bits(value);
        }

        private static int SaturatingInt(double value) => double.IsNaN(value) ? 0 : value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
        private static long SaturatingLong(double value) => double.IsNaN(value) ? 0 : value >= long.MaxValue ? long.MaxValue : value <= long.MinValue ? long.MinValue : (long)value;

        private string SuperclassOf(string descriptor)
        {
            return _dexSet.FindClass(descriptor)?.SuperclassDescriptor ?? AndroidFrameworkHierarchy.ParentOf(descriptor);
        }

        /// <summary>
        /// Runs a guest class's <clinit>()V exactly once per session, at the real
        /// Dalvik active-use trigger points (new-instance, static field access,
        /// static method invoke). Framework/API-bound types have no guest
        /// &lt;clinit&gt; — no-op for them. A superclass initializes before its
        /// subclass (recursive, only while guest-defined). Cycle handling under the
        /// GIL: same-thread re-entry into an in-progress class skips (the JVM's
        /// same-thread-reentrant rule); a DIFFERENT real thread re-entering an
        /// in-progress class genuinely blocks until the initializing thread
        /// finishes (releasing the GIL while waiting so the other thread can make
        /// progress), matching real JVM cross-thread blocking semantics.
        /// </summary>
        private void EnsureClassInitialized(string classDescriptor)
        {
            DexClass cls = _dexSet.FindClass(classDescriptor);
            if (cls is null) return;
            if (_classInitState.TryGetValue(classDescriptor, out int state) && state != ClassInitNotStarted)
            {
                if (state == ClassInitInProgress &&
                    _classInitOwner.TryGetValue(classDescriptor, out int owner) &&
                    owner != Environment.CurrentManagedThreadId)
                {
                    // A different real thread is running this class's <clinit>: block
                    // until it finishes. Release the GIL while waiting so the owner can
                    // make progress, then reacquire; the owner's completion signal fires
                    // in its finally block, so this cannot hang.
                    ManualResetEventSlim signal;
                    lock (_classInitSignals) signal = _classInitSignals[classDescriptor];
                    using (_gil.BeginBlocking())
                        signal.Wait();
                    return;
                }
                return; // done, or same-thread reentrancy into an in-progress class
            }

            if (!string.IsNullOrEmpty(cls.SuperclassDescriptor) && _dexSet.FindClass(cls.SuperclassDescriptor) is not null)
                EnsureClassInitialized(cls.SuperclassDescriptor);

            var initSignal = new ManualResetEventSlim(false);
            lock (_classInitSignals) _classInitSignals[classDescriptor] = initSignal;
            _classInitState[classDescriptor] = ClassInitInProgress;
            _classInitOwner[classDescriptor] = Environment.CurrentManagedThreadId;
            try
            {
                DexEncodedMethod clinit = _dexSet.FindMethodExact(classDescriptor, "<clinit>", "()V");
                if (clinit is not null)
                    Execute(clinit, Array.Empty<object>());
            }
            finally
            {
                _classInitState[classDescriptor] = ClassInitDone;
                initSignal.Set();
            }
        }

        private IEnumerable<string> InterfacesOf(string descriptor)
        {
            // Merge guest-defined interfaces with framework-side ones: a class could
            // be guest-defined AND still need framework interfaces (e.g. it extends
            // a framework class that implements one), so this is not either/or.
            var guest = _dexSet.FindClass(descriptor)?.Interfaces;
            if (guest is null || guest.Count == 0)
                return AndroidFrameworkHierarchy.InterfacesOf(descriptor);
            return guest.Concat(AndroidFrameworkHierarchy.InterfacesOf(descriptor));
        }

        private bool IsTypeAssignable(string actual, string expected)
        {
            if (expected == "Landroid/view/View$OnClickListener;" && _dexSet.FindMethodExact(actual, "onClick", "(Landroid/view/View;)V") is { IsStatic: false }) return true;
            return AndroidFrameworkHierarchy.IsAssignable(actual, expected, SuperclassOf, InterfacesOf);
        }

        private static string DynamicDescriptor(object receiver)
        {
            if (receiver is DexObject guest) return guest.TypeDescriptor;
            if (receiver is string) return "Ljava/lang/String;";
            throw new InvalidOperationException("Unsupported invoke receiver type: " + receiver.GetType().Name);
        }

        private static void RequireNonNullReceiver(object[] args, AndroidApiMethodId api)
        {
            if (args.Length == 0 || args[0] == null || (args[0] is int zero && zero == 0))
                throw new AndroidApiNullReferenceException("Instance DEX receiver is null: " + api);
        }

        private static object[] NormalizeApiArguments(DexMethodRef method, object[] arguments, AndroidInvokeKind invokeKind)
        {
            var normalized = (object[])arguments.Clone();
            int offset = invokeKind == AndroidInvokeKind.Static ? 0 : 1;
            if (offset == 1 && normalized.Length > 0 && normalized[0] is int receiverZero && receiverZero == 0)
                normalized[0] = null;
            for (int index = 0; index < method.Proto.ParameterTypes.Count && index + offset < normalized.Length; index++)
            {
                string type = method.Proto.ParameterTypes[index];
                if ((type.StartsWith("L", StringComparison.Ordinal) || type.StartsWith("[", StringComparison.Ordinal)) &&
                    normalized[index + offset] is int zero && zero == 0)
                    normalized[index + offset] = null;
            }
            return normalized;
        }

        private static AndroidInvokeKind InvokeKindForOpcode(int opcode) => opcode switch
        {
            0x6e or 0x74 => AndroidInvokeKind.Virtual,
            0x6f or 0x75 => AndroidInvokeKind.Super,
            0x70 or 0x76 => AndroidInvokeKind.Direct,
            0x71 or 0x77 => AndroidInvokeKind.Static,
            0x72 or 0x78 => AndroidInvokeKind.Interface,
            _ => throw new ArgumentOutOfRangeException(nameof(opcode))
        };

        private static int SignExtend4(int nibble)
        {
            return nibble >= 8 ? nibble - 16 : nibble;
        }

        private static bool CompareIf(int kind, int a, int b)
        {
            switch (kind)
            {
                case 0: return a == b; // eq
                case 1: return a != b; // ne
                case 2: return a < b;  // lt
                case 3: return a >= b; // ge
                case 4: return a > b;  // gt
                case 5: return a <= b; // le
                default: throw new NotImplementedException();
            }
        }

        private static bool CompareBranch(int kind, object a, object b)
        {
            if (kind is 0 or 1 && (IsReferenceValue(a) || IsReferenceValue(b)))
            {
                bool equal = ReferenceEquals(a, b);
                return kind == 0 ? equal : !equal;
            }
            return CompareIf(kind, ToInt(a), ToInt(b));
        }

        private static bool CompareZeroBranch(int kind, object value)
        {
            if (kind is 0 or 1 && IsReferenceValue(value))
            {
                bool equal = value is null;
                return kind == 0 ? equal : !equal;
            }
            return CompareIf(kind, ToInt(value), 0);
        }

        private static bool IsReferenceValue(object value) =>
            value is null || value is string || value is DexObject || value is Array || value is DexArray;

        private static int IntBinOp(int kind, int a, int b)
        {
            switch (kind)
            {
                case 0: return a + b;
                case 1: return a - b;
                case 2: return a * b;
                case 3:
                    if (b == 0) throw new AndroidGuestArithmeticException("divide by zero");
                    return a == int.MinValue && b == -1 ? int.MinValue : a / b;
                case 4:
                    if (b == 0) throw new AndroidGuestArithmeticException("divide by zero");
                    return a % b;
                case 5: return a & b;
                case 6: return a | b;
                case 7: return a ^ b;
                case 8: return a << (b & 0x1F);
                case 9: return a >> (b & 0x1F);
                case 10: return (int)((uint)a >> (b & 0x1F));
                default: throw new NotImplementedException("operación aritmética int no soportada, índice " + kind);
            }
        }

        // Igual que IntBinOp pero para las variantes /lit*, donde "rsub" (índice 1)
        // resta en el orden inverso: literal - registro.
        private static int IntBinOpLit(int kind, int a, int lit)
        {
            if (kind == 1) return lit - a; // rsub-int
            return IntBinOp(kind == 0 ? 0 : kind, a, lit);
        }

        /// <summary>
        /// Fills an existing primitive array from a fill-array-data-payload (kind 3).
        /// Layout (per DEX spec, cross-checked against DexReader.InstructionWidth kind
        /// 3 = 4 + (size*elementWidth+1)/2 units): ident u16 = 0x0300, element_width u16
        /// (bytes per element), size u32 (element count), then raw initializer bytes
        /// packed two per 16-bit unit, little-endian. The payload never allocates — the
        /// array already exists (new-array) — and only targets primitive arrays.
        /// </summary>
        private static void FillArrayFromPayload(ushort[] insns, int payloadAddress, DexArray array)
        {
            if (payloadAddress < 0 || payloadAddress + 4 > insns.Length || insns[payloadAddress] != 0x0300)
                throw new InvalidOperationException("fill-array-data payload is malformed.");
            int elementWidth = insns[payloadAddress + 1];
            uint sizeValue = (uint)(insns[payloadAddress + 2] | insns[payloadAddress + 3] << 16);
            if (sizeValue > int.MaxValue) throw new InvalidOperationException("fill-array-data payload is too large.");
            int size = (int)sizeValue;
            // Spec reading: the array must be large enough to hold the data (size <=
            // Length); real d8 output makes them equal, but filling a larger array
            // partially is legal DEX. size > Length is malformed — fail closed.
            if (size > array.Length) throw new InvalidOperationException("fill-array-data size exceeds the target array length.");
            int expectedWidth = array.ElementDescriptor switch
            {
                "B" or "Z" => 1,
                "S" or "C" => 2,
                "I" or "F" => 4,
                "J" or "D" => 8,
                _ => throw new InvalidOperationException("fill-array-data cannot target a reference array component: " + array.ElementDescriptor)
            };
            if (elementWidth != expectedWidth)
                throw new InvalidOperationException($"fill-array-data element width {elementWidth} does not match component {array.ElementDescriptor}.");
            long dataBytes = (long)size * elementWidth;
            if (payloadAddress + 4 + (dataBytes + 1) / 2 > insns.Length)
                throw new InvalidOperationException("fill-array-data payload exceeds the method.");
            int dataStart = payloadAddress + 4;
            for (int i = 0; i < size; i++)
            {
                ulong raw = ReadPackedBytes(insns, dataStart, i * elementWidth, elementWidth);
                switch (array.ElementDescriptor)
                {
                    case "B": array.Set(i, unchecked((int)(sbyte)(byte)raw)); break;
                    case "Z": array.Set(i, raw != 0 ? 1 : 0); break;
                    case "S": array.Set(i, unchecked((int)(short)(ushort)raw)); break;
                    case "C": array.Set(i, (int)(char)raw); break;
                    case "I": case "F": array.Set(i, unchecked((int)raw)); break;
                    case "J": case "D": array.SetWide(i, raw); break;
                }
            }
        }

        /// <summary>Reads a little-endian element of elementWidth bytes from the payload's
        /// packed data region (two bytes per ushort unit, low byte first).</summary>
        private static ulong ReadPackedBytes(ushort[] insns, int dataStart, int byteOffset, int elementWidth)
        {
            ulong value = 0;
            for (int b = 0; b < elementWidth; b++)
            {
                int offset = byteOffset + b;
                ushort unit = insns[dataStart + offset / 2];
                byte raw = (offset & 1) == 0 ? (byte)(unit & 0xFF) : (byte)(unit >> 8);
                value |= (ulong)raw << (b * 8);
            }
            return value;
        }

        /// <summary>
        /// Resolves the packed-switch-payload target for a key, or null when no case
        /// matches (fall through). Payload layout (per DEX spec, cross-checked against
        /// DexReader.InstructionWidth kind 1 = 4 + size*2 units): ident u16 = 0x0100,
        /// size u16, first_key s32, then size × target s32.
        /// </summary>
        private static int? PackedSwitchTarget(ushort[] insns, int payloadAddress, int key)
        {
            if (payloadAddress < 0 || payloadAddress + 2 > insns.Length || insns[payloadAddress] != 0x0100)
                throw new InvalidOperationException("packed-switch payload is malformed.");
            int size = insns[payloadAddress + 1];
            if (size < 0 || payloadAddress + 4 + checked(size * 2) > insns.Length)
                throw new InvalidOperationException("packed-switch payload exceeds the method.");
            int firstKey = unchecked((int)((uint)insns[payloadAddress + 2] | (uint)insns[payloadAddress + 3] << 16));
            long delta = (long)key - firstKey;
            if (delta < 0 || delta >= size) return null;
            int target = payloadAddress + 4 + checked((int)delta * 2);
            return unchecked((int)((uint)insns[target] | (uint)insns[target + 1] << 16));
        }

        /// <summary>
        /// Resolves the sparse-switch-payload target for a key, or null when no case
        /// matches (fall through). Payload layout (per DEX spec, cross-checked against
        /// DexReader.InstructionWidth kind 2 = 2 + size*4 units): ident u16 = 0x0200,
        /// size u16, then size × key s32 (ascending per spec), then size × target s32.
        /// Keys are verified strictly ascending — unsorted keys are malformed DEX and
        /// fail closed rather than silently resolving the wrong case.
        /// </summary>
        private static int? SparseSwitchTarget(ushort[] insns, int payloadAddress, int key)
        {
            if (payloadAddress < 0 || payloadAddress + 2 > insns.Length || insns[payloadAddress] != 0x0200)
                throw new InvalidOperationException("sparse-switch payload is malformed.");
            int size = insns[payloadAddress + 1];
            if (size < 0 || payloadAddress + 2 + checked(size * 4) > insns.Length)
                throw new InvalidOperationException("sparse-switch payload exceeds the method.");
            int keysStart = payloadAddress + 2;
            int targetsStart = keysStart + checked(size * 2);
            int previous = 0;
            bool hasPrevious = false;
            for (int i = 0; i < size; i++)
            {
                int candidate = unchecked((int)((uint)insns[keysStart + i * 2] | (uint)insns[keysStart + i * 2 + 1] << 16));
                if (hasPrevious && previous >= candidate) throw new InvalidOperationException("sparse-switch keys are not strictly ascending.");
                previous = candidate;
                hasPrevious = true;
                if (candidate == key)
                {
                    int target = targetsStart + i * 2;
                    return unchecked((int)((uint)insns[target] | (uint)insns[target + 1] << 16));
                }
            }
            return null;
        }

        private static int ToInt(object o)
        {
            if (o is int i) return i;
            if (o == null) return 0;
            if (o is bool b) return b ? 1 : 0;
            throw new InvalidOperationException("Se esperaba un entero en un registro y se encontró: " + o.GetType().Name);
        }
    }
}
