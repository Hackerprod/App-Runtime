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

    [Fact]
    public void File_parent_child_ctor_combines_real_paths()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(), fileSandbox: sandbox);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);

        DexObject dir = InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, null!);
        var child = new DexObject("Ljava/io/File;");
        var ctor = new AndroidApiMethodId("Ljava/io/File;", "<init>", "(Ljava/io/File;Ljava/lang/String;)V");
        registry.Invoke(session, new AndroidApiCallSite(Owner + "->test()V", 0, ctor, ctor, AndroidInvokeKind.Direct), new object[] { child, dir, "fileImplementation.txt" });

        string expected = Path.Combine((string)dir.InstanceFields[FilePathField], "fileImplementation.txt");
        Assert.Equal(expected, (string)child.InstanceFields[FilePathField]);
        Assert.Equal("fileImplementation.txt", Path.GetFileName((string)child.InstanceFields[FilePathField]));
    }

    [Fact]
    public void File_parent_child_ctor_supports_nested_child_paths()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(), fileSandbox: sandbox);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);

        DexObject cache = InvokeContext(state, "getCacheDir", "()Ljava/io/File;", state.ApplicationContext);
        var recording = new DexObject("Ljava/io/File;");
        var ctor = new AndroidApiMethodId("Ljava/io/File;", "<init>", "(Ljava/io/File;Ljava/lang/String;)V");
        registry.Invoke(session, new AndroidApiCallSite(Owner + "->test()V", 0, ctor, ctor, AndroidInvokeKind.Direct), new object[] { recording, cache, "sub/recording.m4a" });

        // Path.Combine semantics: the child's separators are preserved verbatim
        // after the combining separator (mixed separators are valid for real
        // Windows I/O — no normalization is invented).
        Assert.Equal(Path.Combine((string)cache.InstanceFields[FilePathField], "sub/recording.m4a"), (string)recording.InstanceFields[FilePathField]);
    }

    [Fact]
    public void FileWriter_ctor_write_close_creates_a_real_file_with_content()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(),
            capabilityPolicy: AndroidCapabilityPolicy.DenyAll, fileSandbox: sandbox);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);

        // Context.getExternalFilesDir -> File(dir, name) -> FileWriter(file, false).
        DexObject dir = InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, null!);
        var target = new DexObject("Ljava/io/File;");
        Invoke(registry, session, "Ljava/io/File;", "<init>", "(Ljava/io/File;Ljava/lang/String;)V", AndroidInvokeKind.Direct, target, dir, "fileImplementation.txt");
        var writer = new DexObject("Ljava/io/FileWriter;");
        Invoke(registry, session, "Ljava/io/FileWriter;", "<init>", "(Ljava/io/File;Z)V", AndroidInvokeKind.Direct, writer, target, 0);
        Invoke(registry, session, "Ljava/io/FileWriter;", "write", "(Ljava/lang/String;)V", AndroidInvokeKind.Virtual, writer, "Esta es la prueba de filestoraje.");
        Invoke(registry, session, "Ljava/io/FileWriter;", "close", "()V", AndroidInvokeKind.Virtual, writer);

        string path = (string)target.InstanceFields[FilePathField];
        Assert.True(File.Exists(path), "the file must exist on disk after the write cycle");
        Assert.Equal("Esta es la prueba de filestoraje.", File.ReadAllText(path));
        // DenyAll still serves app-private writes: no capability gate applies.
    }

    [Fact]
    public void FileWriter_append_true_keeps_existing_content()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(), fileSandbox: sandbox);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);

        DexObject dir = InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, null!);
        var target = new DexObject("Ljava/io/File;");
        Invoke(registry, session, "Ljava/io/File;", "<init>", "(Ljava/io/File;Ljava/lang/String;)V", AndroidInvokeKind.Direct, target, dir, "append.txt");
        string path = (string)target.InstanceFields[FilePathField];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "first");

        var writer = new DexObject("Ljava/io/FileWriter;");
        Invoke(registry, session, "Ljava/io/FileWriter;", "<init>", "(Ljava/io/File;Z)V", AndroidInvokeKind.Direct, writer, target, 1);
        Invoke(registry, session, "Ljava/io/FileWriter;", "write", "(Ljava/lang/String;)V", AndroidInvokeKind.Virtual, writer, "second");

        Assert.Equal("firstsecond", File.ReadAllText(path));
    }

    [Fact]
    public void File_getAbsolutePath_returns_the_real_stored_path()
    {
        using var sandbox = new TempFileSandbox();
        using var state = new AndroidFrameworkState("files", "org.example.app", Owner, new ActivityWindowPeers(), fileSandbox: sandbox);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);

        DexObject dir = InvokeContext(state, "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;", state.ApplicationContext, null!);
        string stored = (string)dir.InstanceFields[FilePathField];

        var api = new AndroidApiMethodId("Ljava/io/File;", "getAbsolutePath", "()Ljava/lang/String;");
        object result = registry.Invoke(session, new AndroidApiCallSite(Owner + "->test()V", 0, api, api, AndroidInvokeKind.Virtual), new object[] { dir });

        Assert.Equal(stored, result);
        Assert.True(Path.IsPathRooted((string)result), "the path is already absolute (sandbox root) — no forcing needed");
    }

    private static void Invoke(AndroidApiRegistry registry, AndroidApiSessionContext session, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        registry.Invoke(session, new AndroidApiCallSite(Owner + "->test()V", 0, api, api, kind), args);
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
