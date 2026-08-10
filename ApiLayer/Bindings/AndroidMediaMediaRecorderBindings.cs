#nullable enable
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for android.media.MediaRecorder — probe-first, one method at a time.
/// Configuration methods store their value on the recorder object's instance
/// fields (same pattern as java.io.File.path) so the capture chain (start/stop)
/// can read them when built. The real trace against RuntimeApiLab drives which
/// method binds next. Scope (owner-approved): REAL audio capture once the chain
/// is built — NOT an honest empty stub.
/// </summary>
internal static class AndroidMediaMediaRecorderBindings
{
    private const string AudioSourceField = "Landroid/media/MediaRecorder;->audioSource:I";
    private const string OutputFormatField = "Landroid/media/MediaRecorder;->outputFormat:I";
    private const string AudioEncoderField = "Landroid/media/MediaRecorder;->audioEncoder:I";
    private const string BitRateField = "Landroid/media/MediaRecorder;->audioEncodingBitRate:I";
    private const string SamplingRateField = "Landroid/media/MediaRecorder;->audioSamplingRate:I";
    private const string OutputFileField = "Landroid/media/MediaRecorder;->outputFile:Ljava/lang/String;";
    private const string FilePathField = "Ljava/io/File;->path:Ljava/lang/String;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        _ = state;
        builder.Register(Api("Landroid/media/MediaRecorder;", "<init>", "()V"), (_, args) =>
        {
            RequireDex(args[0]); // receiver — the object exists for the chain to configure
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "setAudioSource", "(I)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            int source = RequireInt(args[1]);
            recorder.InstanceFields[AudioSourceField] = source;
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "setOutputFormat", "(I)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            int format = RequireInt(args[1]);
            recorder.InstanceFields[OutputFormatField] = format;
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "setAudioEncoder", "(I)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            int encoder = RequireInt(args[1]);
            recorder.InstanceFields[AudioEncoderField] = encoder;
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "setAudioEncodingBitRate", "(I)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            recorder.InstanceFields[BitRateField] = RequireInt(args[1]);
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "setAudioSamplingRate", "(I)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            recorder.InstanceFields[SamplingRateField] = RequireInt(args[1]);
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "setOutputFile", "(Ljava/io/File;)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            DexObject file = RequireDex(args[1]);
            recorder.InstanceFields[OutputFileField] = FilePathOf(file);
            return null!;
        });
        // The real app passes setOutputFile(file.getAbsolutePath()) — the String
        // variant is what the trace actually hits (probe-discovered).
        builder.Register(Api("Landroid/media/MediaRecorder;", "setOutputFile", "(Ljava/lang/String;)V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            recorder.InstanceFields[OutputFileField] = args[1] as string ?? string.Empty;
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "prepare", "()V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            // A real Android MediaRecorder throws IllegalStateException when the
            // config is incomplete. Honest check: the capture chain (start) needs
            // the output file + sample rate; missing config is a guest error.
            if (recorder.InstanceFields.TryGetValue(OutputFileField, out object? file) && file is string path && path.Length > 0)
                return null!;
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalStateException;", "prepare() called without a valid output file"));
        });
        // The capture boundary: start() requires the Microphone capability
        // (real Android checks RECORD_AUDIO at start, not construction) and
        // delegates to the host audio-recorder port — REAL capture, never a
        // fabricated silent file (owner-approved scope).
        builder.Register(Api("Landroid/media/MediaRecorder;", "start", "()V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            if (!state.IsCapabilityAllowed(new(state.SessionId, state.PackageName, AndroidCapability.Microphone, "MediaRecorder.start")))
                throw new AndroidApiSecurityException("Capability denied for MediaRecorder.start: Microphone.");
            string outputPath = recorder.InstanceFields.TryGetValue(OutputFileField, out object? file) && file is string path ? path : string.Empty;
            int sampleRate = recorder.InstanceFields.TryGetValue(SamplingRateField, out object? rate) && rate is int sr ? sr : 44100;
            int bitRate = recorder.InstanceFields.TryGetValue(BitRateField, out object? br) && br is int b ? b : 128000;
            state.AudioRecorder.Start(outputPath, sampleRate, bitRate);
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "stop", "()V"), (_, args) =>
        {
            RequireDex(args[0]);
            state.AudioRecorder.Stop();
            return null!;
        });
        // Teardown surface (probe-discovered): the app's onDestroy calls
        // reset()/release() — without them every session with a MediaRecorder
        // crashes on shutdown (same pattern as Handler.removeCallbacksAndMessages).
        // Both clear configuration state; the capture pipeline (once built) owns
        // any real resources from start() to release().
        builder.Register(Api("Landroid/media/MediaRecorder;", "reset", "()V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            recorder.InstanceFields.Remove(AudioSourceField);
            recorder.InstanceFields.Remove(OutputFormatField);
            recorder.InstanceFields.Remove(AudioEncoderField);
            recorder.InstanceFields.Remove(BitRateField);
            recorder.InstanceFields.Remove(SamplingRateField);
            recorder.InstanceFields.Remove(OutputFileField);
            return null!;
        });
        builder.Register(Api("Landroid/media/MediaRecorder;", "release", "()V"), (_, args) =>
        {
            DexObject recorder = RequireDex(args[0]);
            recorder.InstanceFields.Clear();
            return null!;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);

    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected DEX object.");

    private static int RequireInt(object? value) => value is int result ? result : throw new ArgumentException("Expected int argument.");

    /// <summary>Reads a java.io.File's path from its instance field (empty when
    /// the File has none) — setOutputFile(File) reuses the same slot the
    /// sandbox directory bindings write.</summary>
    private static string FilePathOf(DexObject file) =>
        file.InstanceFields.TryGetValue(FilePathField, out object? value) && value is string path ? path : string.Empty;
}
