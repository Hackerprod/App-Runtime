#nullable enable
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core;

/// <summary>Launches the manifest Activity through the bounded APK/DEX lifecycle and host ports.</summary>
public sealed class AndroidAppRuntime
{
    public DexObject Launch(string apkPath)
    {
        using var stream = OpenApkRead(apkPath);
        return Launch(stream);
    }

    public DexObject Launch(Stream apkStream) => LaunchSession(apkStream).Activity;

    public AndroidActivitySession LaunchSession(string apkPath)
    {
        using var stream = OpenApkRead(apkPath);
        return LaunchSession(stream);
    }

    public AndroidActivitySession LaunchSession(Stream apkStream)
    {
        var prepared = PrepareSynchronousSession(apkStream, AndroidRuntimeServices.CreateHeadless());
        if (!AndroidLifecycleCoordinator.RunForward(prepared.Session, prepared.FrameworkState, prepared.Window, CancellationToken.None))
        { prepared.Session.Terminate(); prepared.FrameworkState.MarkDestroyed(); }
        return prepared.Session;
    }

    public AndroidActivitySession CreateSession(string apkPath)
    {
        using var stream = OpenApkRead(apkPath);
        return CreateSession(stream);
    }

    public AndroidActivitySession CreateSession(Stream apkStream) =>
        PrepareSynchronousSession(apkStream, AndroidRuntimeServices.CreateHeadless()).Session;

    public async Task<AndroidHostedActivitySession> LaunchSessionAsync(
        string apkPath,
        AndroidRuntimeServices services,
        CancellationToken cancellationToken = default)
    {
        var stream = OpenApkRead(apkPath);
        try
        {
            return await LaunchSessionAsyncCore(stream, services, cancellationToken, stream).ConfigureAwait(false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public Task<AndroidHostedActivitySession> LaunchSessionAsync(
        Stream apkStream,
        AndroidRuntimeServices services,
        CancellationToken cancellationToken = default) =>
        LaunchSessionAsyncCore(apkStream, services, cancellationToken, ownedApkStream: null);

    private async Task<AndroidHostedActivitySession> LaunchSessionAsyncCore(
        Stream apkStream,
        AndroidRuntimeServices services,
        CancellationToken cancellationToken,
        IDisposable? ownedApkStream)
    {
        ValidateApkStream(apkStream);
        ArgumentNullException.ThrowIfNull(services);
        string sessionId = Guid.NewGuid().ToString("N");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lane = new AndroidExecutionLane(sessionId);
        var peers = new ActivityWindowPeers();
        var trace = new AndroidApiTraceBuffer(services.TraceCapacity);
        IActivityWindow? window = null;
        AndroidActivitySession? activitySession = null;
        AndroidFrameworkState? frameworkState = null;

        try
        {
            activitySession = await lane.InvokeAsync(() =>
            {
                var apk = ApkLoader.Load(apkStream);
                var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);
        AndroidResourceResolver? resources = apk.ResourcesArsc is null ? null : AndroidResourceResolver.Create(apk);
        AndroidResourceQueryService? resourceQueries = resources is null ? null : new AndroidResourceQueryService(resources, apk);
                foreach (byte[] dexBytes in apk.ClassesDexFiles) EnsureVerified(dexBytes);
                var dexSet = DexFileSet.ParseMany(apk.ClassesDexFiles);
                IAndroidApiTraceSink traceSink = services.AdditionalTraceSink is null
                    ? trace
                    : new CompositeAndroidApiTraceSink(trace, services.AdditionalTraceSink);
                var apiContext = new AndroidApiSessionContext(
                    sessionId,
                    manifest.PackageName,
                    manifest.LauncherActivityDescriptor,
                    lifetime.Token,
                    () => lane.IsCurrentThread,
                    traceSink);
                frameworkState = new AndroidFrameworkState(
                    sessionId,
                    manifest.PackageName,
                    manifest.LauncherActivityDescriptor,
                    peers,
                    services.MinimumLogPriority,
                    services.ToastLimits,
                    services.PeerLimits,
                    services.Clock,
                    services.WallClock,
                    manifest.UsesPermissions,
                    services.CapabilityPolicy,
                    services.Clipboard,
                    services.Connectivity,
                    services.ServiceAudit,
                    services.ServiceLimits,
                    manifest.TargetSdkVersion,
                    services.Power,
                    resources,
                    resourceQueries,
                    services.ViewBridgeFactory is null || resources is null ? null : services.ViewBridgeFactory(resources, resourceQueries!, manifest.ApplicationThemeStyleId));
                var registry = AndroidApiBindings.CreateBuilder(frameworkState, services.LogSink).Build();
                var interpreter = new DexInterpreter(dexSet, registry, apiSession: apiContext, gil: lane.Gil);
                interpreter.StaticFieldMissDiagnostic = message => Console.Error.WriteLine("[DEX] " + message);
                frameworkState.Gil = interpreter.Gil;
                frameworkState.Lane = lane;
                frameworkState.AttachInterpreter(interpreter);
                JavaLangThreadBindings.InitializeMainGuestThread(frameworkState);
                var activity = interpreter.ConstructInstance(manifest.LauncherActivityDescriptor);
                frameworkState.AttachActivity(activity);
                window = services.WindowFactory.Create(
                    sessionId,
                    manifest.PackageName,
                    manifest.LauncherActivityDescriptor,
                    lifetime.Token);
                peers.Associate(activity, window);
                return new AndroidActivitySession(interpreter, activity);
            }, lifetime.Token).ConfigureAwait(false);
            var hosted = new AndroidHostedActivitySession(activitySession, window!, trace, lane, lifetime, peers, sessionId, ownedApkStream, frameworkState);
            bool completed = await lane.InvokeAsync(() => AndroidLifecycleCoordinator.RunForward(activitySession, frameworkState!, window!, lifetime.Token), lifetime.Token).ConfigureAwait(false);
            if (!completed) await hosted.Termination.ConfigureAwait(false);
            return hosted;
        }
        catch
        {
            lifetime.Cancel();
            if (activitySession is not null)
                peers.Remove(activitySession.Activity);
            if (window is not null)
            {
                try { window.Close(); } catch { }
                try { window.Dispose(); } catch { }
            }
            await lane.DisposeAsync().ConfigureAwait(false);
            frameworkState?.Dispose();
            lifetime.Dispose();
            throw;
        }
    }

    private static PreparedSession PrepareSynchronousSession(Stream apkStream, AndroidRuntimeServices services)
    {
        ValidateApkStream(apkStream);
        var apk = ApkLoader.Load(apkStream);
        var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);
        AndroidResourceResolver? resources = apk.ResourcesArsc is null ? null : AndroidResourceResolver.Create(apk);
        AndroidResourceQueryService? resourceQueries = resources is null ? null : new AndroidResourceQueryService(resources, apk);
        foreach (byte[] dexBytes in apk.ClassesDexFiles) EnsureVerified(dexBytes);
        var dexSet = DexFileSet.ParseMany(apk.ClassesDexFiles);
        string sessionId = Guid.NewGuid().ToString("N");
        var peers = new ActivityWindowPeers();
        var trace = new AndroidApiTraceBuffer(services.TraceCapacity);
        var apiContext = new AndroidApiSessionContext(
            sessionId,
            manifest.PackageName,
            manifest.LauncherActivityDescriptor,
            CancellationToken.None,
            () => true,
            trace);
        var frameworkState = new AndroidFrameworkState(
            sessionId,
            manifest.PackageName,
            manifest.LauncherActivityDescriptor,
            peers,
            services.MinimumLogPriority,
            services.ToastLimits,
            services.PeerLimits,
            services.Clock,
            services.WallClock,
            manifest.UsesPermissions,
            services.CapabilityPolicy,
            services.Clipboard,
            services.Connectivity,
            services.ServiceAudit,
            services.ServiceLimits,
            manifest.TargetSdkVersion,
            services.Power,
            resources,
            resourceQueries,
            services.ViewBridgeFactory is null || resources is null ? null : services.ViewBridgeFactory(resources, resourceQueries!, manifest.ApplicationThemeStyleId));
        var registry = AndroidApiBindings.CreateBuilder(frameworkState, services.LogSink).Build();
        var interpreter = new DexInterpreter(dexSet, registry, apiSession: apiContext);
        interpreter.StaticFieldMissDiagnostic = message => Console.Error.WriteLine("[DEX] " + message);
        frameworkState.Gil = interpreter.Gil;
        frameworkState.AttachInterpreter(interpreter);
        JavaLangThreadBindings.InitializeMainGuestThread(frameworkState);
        var activity = interpreter.ConstructInstance(manifest.LauncherActivityDescriptor);
        frameworkState.AttachActivity(activity);
        var window = services.WindowFactory.Create(
            sessionId,
            manifest.PackageName,
            manifest.LauncherActivityDescriptor,
            CancellationToken.None);
        peers.Associate(activity, window);
        return new PreparedSession(new AndroidActivitySession(interpreter, activity), window, frameworkState);
    }

    private sealed record PreparedSession(AndroidActivitySession Session, IActivityWindow Window, AndroidFrameworkState FrameworkState);

    private static FileStream OpenApkRead(string apkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        return new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void ValidateApkStream(Stream apkStream)
    {
        ArgumentNullException.ThrowIfNull(apkStream);
        if (!apkStream.CanRead || !apkStream.CanSeek)
            throw new ArgumentException("APK stream must be readable and seekable.", nameof(apkStream));
    }

    private static void EnsureVerified(byte[] dex)
    {
        DexVerificationResult result = DexVerifier.Verify(dex);
        if (!result.IsValid) throw new InvalidDataException("DEX structural verification failed: " + result.Diagnostics[0]);
    }
}
