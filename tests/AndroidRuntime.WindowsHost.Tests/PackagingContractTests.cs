using System.Xml.Linq;
using System.IO;
using System.Diagnostics;

namespace AndroidRuntime.WindowsHost.Tests;

public sealed class PackagingContractTests
{
    [Fact]
    public void Publish_profile_is_explicitly_portable_multi_file_and_untrimmed()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var xml = XDocument.Load(Path.Combine(root, "AndroidRuntime.WindowsHost", "Properties", "PublishProfiles", "win-x64.pubxml"));
        string Value(string name) => xml.Descendants(name).Single().Value;
        Assert.Equal("win-x64", Value("RuntimeIdentifier"));
        Assert.Equal("true", Value("SelfContained"));
        Assert.Equal("false", Value("PublishSingleFile"));
        Assert.Equal("false", Value("PublishTrimmed"));
        Assert.Equal("false", Value("PublishReadyToRun"));
    }

    [Fact]
    public void Packaging_rejects_dangerous_output_paths_before_mutation()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string marker = Path.Combine(root, "packaging-safety-marker.txt");
        byte[] expected = "do-not-delete"u8.ToArray(); File.WriteAllBytes(marker, expected);
        try
        {
            string[] dangerous = [".", root, Directory.GetParent(root)!.FullName, Path.GetPathRoot(root)!, Path.Combine(Path.GetTempPath(), "android-runtime-outside")];
            foreach (string output in dangerous)
            {
                string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(root, "scripts", "package-win-x64.ps1")}\" -OutputDirectory \"{output}\"";
                using var process = Process.Start(new ProcessStartInfo("powershell.exe", arguments) { UseShellExecute = false, CreateNoWindow = true })!;
                Assert.True(process.WaitForExit(15_000)); Assert.NotEqual(0, process.ExitCode); Assert.Equal(expected, File.ReadAllBytes(marker));
            }
        }
        finally { File.Delete(marker); }
    }
}
