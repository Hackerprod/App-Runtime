#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for android.media.MediaRecorder — probe-first: ONLY the ctor is
/// bound in this unit; the real trace against RuntimeApiLab tells which method
/// comes next (setAudioSource / setOutputFormat / setOutputFile / prepare /
/// start / stop / release — NOT assumed). The capability gate (Microphone) is
/// decided once the recording start path is reached, matching real Android
/// (RECORD_AUDIO is enforced at start, not construction).
/// </summary>
internal static class AndroidMediaMediaRecorderBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        _ = state;
        builder.Register(Api("Landroid/media/MediaRecorder;", "<init>", "()V"), (_, args) =>
        {
            RequireDex(args[0]); // receiver — the object exists for the chain to configure
            return null!;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);

    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected DEX object.");
}
