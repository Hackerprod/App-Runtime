#nullable enable
using System.IO;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.io.FileWriter — real write I/O into the app-private file
/// sandbox (Context.getExternalFilesDir / getCacheDir targets, never arbitrary
/// filesystem paths). Same scoped-storage criterion established for the sandbox
/// directory bindings: writing inside the app's own directories is UNGATED (no
/// runtime permission on real Android), so no capability gate is added here.
///
/// Lifetime model (documented, deliberate): the FileWriter carries its target
/// path in the File path instance slot; every write() opens a REAL
/// StreamWriter in append mode, writes, flushes, and closes. The observable
/// file content at close() is identical to a buffered real FileWriter, and no
/// stream is ever held open across the guest lifetime (no leaks, no peer store
/// needed). <init>(File, false) truncates/creates immediately — the real
/// FileWriter(File) opens the stream at construction.
/// </summary>
internal static class JavaIoFileWriterBindings
{
    private const string FilePathField = "Ljava/io/File;->path:Ljava/lang/String;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        _ = state; // No gate: app-private sandbox writes are ungated (scoped storage).
        builder.Register(Api("Ljava/io/FileWriter;", "<init>", "(Ljava/io/File;Z)V"), (_, args) =>
        {
            DexObject receiver = RequireDex(args[0]);
            string path = FilePathOf(RequireDex(args[1]));
            bool append = args[2] is int flag && flag != 0;
            receiver.InstanceFields[FilePathField] = path;
            if (path.Length > 0 && !append)
            {
                // Real FileWriter(File) opens immediately, creating/truncating.
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.WriteAllBytes(path, Array.Empty<byte>());
            }
            return null!;
        });
        builder.Register(Api("Ljava/io/FileWriter;", "write", "(Ljava/lang/String;)V"), (_, args) =>
        {
            string path = FilePathOf(RequireDex(args[0]));
            string text = args[1] as string ?? string.Empty;
            if (path.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                using (var writer = new StreamWriter(path, append: true))
                {
                    writer.Write(text);
                    writer.Flush();
                }
            }
            return null!;
        });
        builder.Register(Api("Ljava/io/FileWriter;", "close", "()V"), (_, args) =>
        {
            // Every write already flushed; nothing is held open across the guest
            // lifetime. Accepted per the real contract (no-op is honest here).
            RequireDex(args[0]);
            return null!;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);

    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected DEX object.");

    /// <summary>Reads a java.io.File's path from its instance field (empty when
    /// the File has none). The FileWriter reuses the same slot for its target.</summary>
    private static string FilePathOf(DexObject file) =>
        file.InstanceFields.TryGetValue(FilePathField, out object value) && value is string path ? path : string.Empty;
}
