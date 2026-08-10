using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// App-private file sandbox (Context.getExternalFilesDir / getCacheDir):
/// real directories under an injected sandbox root, returned as java.io.File
/// objects carrying their path in the File instance field, and deliberately
/// UNGATED (real scoped storage: app-specific directories need no runtime
/// permission — no capability audit record is produced).
/// </summary>
public sealed class AndroidFileSandboxTests
{
    private const string Owner = "Lorg/example/runtimeprobe/ServicesProbe;";
    private const string FilePathField = "Ljava/io/File;->path:Ljava/lang/String;";

    [Fact]
    public void GetExternalFilesDir_returns_a_real_sandbox_file_for_the_package()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(),
            capabilityPolicy: AndroidCapabilityPolicy.DenyAll, fileSandbox: sandbox);

        DexObject file = InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, null!);

        Assert.Equal("Ljava/io/File;", file.TypeDescriptor);
        string path = (string)file.InstanceFields[FilePathField];
        Assert.Equal(sandbox.Root, Path.GetDirectoryName(Path.GetDirectoryName(path)));
        Assert.Equal("files", Path.GetFileName(path));
        Assert.Equal("org.example.app", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.True(Directory.Exists(path), "sandbox files dir must exist (real Android pre-creates it)");
    }

    [Fact]
    public void GetExternalFilesDir_with_type_appends_the_type_subdirectory()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(), fileSandbox: sandbox);

        DexObject file = InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, "music");

        string path = (string)file.InstanceFields[FilePathField];
        Assert.Equal("music", Path.GetFileName(path));
        Assert.Equal("files", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void GetCacheDir_returns_the_cache_sandbox_directory()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(), fileSandbox: sandbox);

        DexObject file = InvokeContext(state, "getCacheDir", "()Ljava/io/File;", state.ApplicationContext);

        string path = (string)file.InstanceFields[FilePathField];
        Assert.Equal("cache", Path.GetFileName(path));
        Assert.Equal("org.example.app", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Sandbox_directories_are_ungated_and_never_audited()
    {
        var audit = new CapturingAudit();
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(),
            capabilityPolicy: AndroidCapabilityPolicy.DenyAll, capabilityAudit: audit, fileSandbox: sandbox);

        InvokeContext(state, "getCacheDir", "()Ljava/io/File;", state.ApplicationContext);
        InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, null!);

        // Scoped storage: the app's own directories need no permission, so
        // DenyAll still serves them and NOTHING is written to the audit trail.
        Assert.Empty(audit.Entries);
    }

    private static DexObject InvokeContext(AndroidFrameworkState state, string name, string descriptor, params object[] args)
    {
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);
        var api = new AndroidApiMethodId("Landroid/content/Context;", name, descriptor);
        return (DexObject)registry.Invoke(session, new AndroidApiCallSite(Owner + "->test()V", 0, api, api, AndroidInvokeKind.Virtual), args);
    }

    private sealed class CapturingAudit : IAndroidCapabilityAuditSink
    {
        public List<AndroidCapabilityAuditEntry> Entries { get; } = [];
        public void Record(AndroidCapabilityAuditEntry entry) => Entries.Add(entry);
    }

    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }

    private sealed class TempFileSandbox : IAndroidFileSandbox, IDisposable
    {
        private readonly string _root;
        internal string Root => _root;

        internal TempFileSandbox()
        {
            _root = Path.Combine(Path.GetTempPath(), "android-sandbox-" + Guid.NewGuid().ToString("N"));
        }

        public string GetCacheDirectory(string packageName) => Directory.CreateDirectory(Path.Combine(_root, packageName, "cache")).FullName;
        public string GetFilesDirectory(string packageName, string? type) => Directory.CreateDirectory(Path.Combine(_root, packageName, "files", type ?? string.Empty)).FullName;

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }
}
