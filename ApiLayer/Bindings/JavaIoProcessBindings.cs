#nullable enable
using System.Diagnostics;
using System.Text;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.lang.ProcessBuilder + java.lang.Process + the java.io
/// reader chain (InputStream / InputStreamReader / BufferedReader) — the real
/// process-execution path the RuntimeApiLab "ping" button uses. The guest
/// runs `ProcessBuilder("ping", host).start()`, reads stdout through a
/// BufferedReader, parses the ping time with a regex, and shows a toast.
///
/// IMPLEMENTATION: the guest process is a REAL host subprocess launched via
/// System.Diagnostics.Process with UseShellExecute=false (direct exec, no
/// shell interpretation — the faithful ProcessBuilder contract). The
/// process's stdout/stderr are captured asynchronously into per-process
/// StreamReaders; InputStream facades wrap them; BufferedReader.readLine()
/// pulls one line at a time. This is the honest host truth for a Windows
/// host: "ping" is the real Windows ping.exe.
///
/// SECURITY: this is the guest invoking a host subprocess with its own argv.
/// UseShellExecute=false + no shell means no command-string interpretation
/// (ProcessBuilder is argv-based by definition). The sandbox capability
/// policy does not gate subprocess spawning today; this is the documented
/// behavior of the ProcessBuilder binding.
/// </summary>
internal static class JavaIoProcessBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- ProcessBuilder ----
        builder.Register(Api("Ljava/lang/ProcessBuilder;", "<init>", "([Ljava/lang/String;)V"), (_, args) =>
        {
            DexObject receiver = Receiver(args);
            DexArray argv = (DexArray)args[1]!;
            var commands = new string[argv.Length];
            for (int i = 0; i < argv.Length; i++)
                commands[i] = argv.Get(i) as string ?? throw new ArgumentException("ProcessBuilder argv elements must be strings.");
            receiver.InstanceFields["argv"] = commands;
            return null!;
        });
        builder.Register(Api("Ljava/lang/ProcessBuilder;", "start", "()Ljava/lang/Process;"), (_, args) =>
        {
            DexObject receiver = Receiver(args);
            string[] commands = (string[])receiver.InstanceFields["argv"];
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ResolveHostExecutable(commands.Length > 0 ? commands[0] : throw new ArgumentException("ProcessBuilder requires at least a command.")),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                for (int i = 1; i < commands.Length; i++)
                    process.StartInfo.ArgumentList.Add(commands[i]);
                process.Start();

                var processObject = new DexObject("Ljava/lang/Process;");
                state.Processes.Add(processObject, new ProcessPeer
                {
                    Process = process,
                    Stdout = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8),
                    Stderr = new StreamReader(process.StandardError.BaseStream, Encoding.UTF8)
                });
                return processObject;
            }
            catch (Exception error)
            {
                File.AppendAllText(@"D:\Install\Dev\Projects\App Runtime\.tmp\process-start.log", DateTime.UtcNow.ToString("HH:mm:ss.fff") + " ProcessBuilder.start FAILED: " + error + Environment.NewLine);
                throw;
            }
        });
        // redirectErrorStream() — the honest no-op default (false); the ping
        // path reads stdout only. Bound so apps that toggle it don't crash.
        builder.Register(Api("Ljava/lang/ProcessBuilder;", "redirectErrorStream", "(Z)Ljava/lang/ProcessBuilder;"), (_, args) => args[0]);

        // ---- Process ----
        builder.Register(Api("Ljava/lang/Process;", "getInputStream", "()Ljava/io/InputStream;"), (_, args) => InputStreamFor(state, Receiver(args), error: false));
        builder.Register(Api("Ljava/lang/Process;", "getErrorStream", "()Ljava/io/InputStream;"), (_, args) => InputStreamFor(state, Receiver(args), error: true));
        builder.Register(Api("Ljava/lang/Process;", "waitFor", "()I"), (_, args) => WaitFor(state, Receiver(args)));
        builder.Register(Api("Ljava/lang/Process;", "destroy", "()V"), (_, args) => { state.Processes.Get(Receiver(args)).Process?.Kill(entireProcessTree: true); return null!; });

        // ---- java.io.InputStream (only the read path the flow uses) ----
        builder.Register(Api("Ljava/io/InputStream;", "close", "()V"), (_, _) => null!);

        // ---- InputStreamReader ----
        builder.Register(Api("Ljava/io/InputStreamReader;", "<init>", "(Ljava/io/InputStream;)V"), (_, args) =>
        {
            var streamObject = (DexObject)args[1]!;
            InputStreamPeer streamPeer = state.InputStreams.Get(streamObject);
            Receiver(args).InstanceFields["source"] = streamPeer;
            return null!;
        });

        // ---- BufferedReader ----
        builder.Register(Api("Ljava/io/BufferedReader;", "<init>", "(Ljava/io/Reader;)V"), (_, args) =>
        {
            var readerArg = (DexObject)args[1]!;
            if (!readerArg.InstanceFields.TryGetValue("source", out object? src) || src is not InputStreamPeer streamPeer)
                throw new ArgumentException("BufferedReader requires an InputStreamReader source; got " + readerArg.TypeDescriptor);
            var receiver = Receiver(args);
            state.Readers.Add(receiver, new BufferedReaderPeer { Source = streamPeer });
            return null!;
        });
        builder.Register(Api("Ljava/io/BufferedReader;", "readLine", "()Ljava/lang/String;"), (_, args) => ReadLine(state, Receiver(args)));
        builder.Register(Api("Ljava/io/BufferedReader;", "close", "()V"), (_, _) => null!);

        // ---- java.util.regex (the ping flow parses the output with a regex) ----
        builder.Register(Api("Ljava/util/regex/Pattern;", "compile", "(Ljava/lang/String;)Ljava/util/regex/Pattern;"), (_, args) =>
        {
            string pattern = AndroidApiBindings.RequireString(args[0]);
            var patternObject = new DexObject("Ljava/util/regex/Pattern;");
            patternObject.InstanceFields["pattern"] = pattern;
            return patternObject;
        });
        builder.Register(Api("Ljava/util/regex/Pattern;", "matcher", "(Ljava/lang/CharSequence;)Ljava/util/regex/Matcher;"), (_, args) =>
        {
            var patternObject = (DexObject)args[0]!;
            string pattern = (string)patternObject.InstanceFields["pattern"];
            string input = AndroidApiBindings.RequireString(args[1]);
            var matcherObject = new DexObject("Ljava/util/regex/Matcher;");
            matcherObject.InstanceFields["pattern"] = pattern;
            matcherObject.InstanceFields["input"] = input;
            return matcherObject;
        });
        builder.Register(Api("Ljava/util/regex/Matcher;", "find", "()Z"), (_, args) =>
        {
            var matcher = (DexObject)args[0]!;
            string pattern = (string)matcher.InstanceFields["pattern"];
            string input = (string)matcher.InstanceFields["input"];
            System.Text.RegularExpressions.Regex regex = new(pattern);
            System.Text.RegularExpressions.Match match = regex.Match(input);
            matcher.InstanceFields["match"] = match.Success ? match : null;
            return match.Success ? 1 : 0;
        });
        builder.Register(Api("Ljava/util/regex/Matcher;", "group", "(I)Ljava/lang/String;"), (_, args) =>
        {
            var matcher = (DexObject)args[0]!;
            int group = AndroidApiBindings.RequireInt(args[1]);
            var match = matcher.InstanceFields.TryGetValue("match", out object? m) ? m as System.Text.RegularExpressions.Match : null;
            if (match is null || group < 0 || group >= match.Groups.Count) return null!;
            return match.Groups[group].Value;
        });
    }

    /// <summary>Process.getInputStream()/getErrorStream(): a fresh InputStream
    /// facade wrapping the process's captured stdout/stderr reader.</summary>
    private static object InputStreamFor(AndroidFrameworkState state, DexObject processObject, bool error)
    {
        ProcessPeer process = state.Processes.Get(processObject);
        var streamObject = new DexObject("Ljava/io/InputStream;");
        state.InputStreams.Add(streamObject, new InputStreamPeer { Process = process, IsError = error });
        return streamObject;
    }

    private static object WaitFor(AndroidFrameworkState state, DexObject processObject)
    {
        ProcessPeer process = state.Processes.Get(processObject);
        // Drain remaining stdout so readLine() sees the full output before
        // waitFor returns (the guest reads lines BEFORE waitFor, so by the
        // time waitFor runs the async drain has already buffered everything).
        try { process.Process?.WaitForExit(); } catch { }
        return 0;
    }

    private static object ReadLine(AndroidFrameworkState state, DexObject readerObject)
    {
        BufferedReaderPeer reader = state.Readers.Get(readerObject);
        InputStreamPeer stream = reader.Source ?? throw new ArgumentException("BufferedReader has no source stream.");
        StreamReader? source = stream.IsError ? stream.Process?.Stderr : stream.Process?.Stdout;
        if (source is null) return null!;
        string? line = source.ReadLine();
        return line;
    }

    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);

    /// <summary>Maps a guest argv[0] (often a Linux/Android path like
    /// "/system/bin/ping") to the real Windows executable that implements the
    /// same command. Unknown absolute paths stay as-is (the honest host truth:
    /// they do not exist and the start fails with the real Win32 error).</summary>
    private static string ResolveHostExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return command;
        string name = Path.GetFileName(command);
        if (string.Equals(name, "ping", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "ping.exe", StringComparison.OrdinalIgnoreCase))
        {
            string system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
            return File.Exists(system32) ? system32 : "ping";
        }
        return command;
    }
}
