using AndroidRuntime.WindowsHost;
using System.IO;
using System.Security.Cryptography;

namespace AndroidRuntime.WindowsHost.Tests;

[Collection("WPF adapter")]
public sealed class CapabilityAuditHostTests
{
    [Fact]
    public void Capability_audit_path_aliasing_apk_is_rejected_and_apk_preserved()
    {
        string directory = CreateTemporaryDirectory();
        string apk = Path.Combine(directory, "runtimeprobe.apk");
        File.Copy(FixturePath(), apk);
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(apk));
        try
        {
            int exitCode = Program.Main([apk, "--capability-audit", apk.ToUpperInvariant()]);

            Assert.NotEqual(0, exitCode);
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(apk)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Capability_audit_file_is_created_by_the_host_flag()
    {
        string directory = CreateTemporaryDirectory();
        string auditPath = Path.Combine(directory, "audit.jsonl");
        try
        {
            int exitCode = Program.Main([FixturePath(), "--auto-close-ms", "300", "--capability-audit", auditPath]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(auditPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
