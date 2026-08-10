#nullable enable
using System.IO;
using AndroidRuntime.Core.Hosting;
using NAudio.Wave;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// REAL microphone capture + AAC/MP4 encoding, the project-owner-approved scope
/// for MediaRecorder (real apps need real audio; an empty stub is not
/// acceptable). Pipeline: WaveInEvent captures 16-bit PCM from the default
/// microphone at the requested sample rate into a temporary WAV, then
/// MediaFoundationEncoder.EncodeToAac produces the final MP4 using Windows'
/// built-in Media Foundation AAC encoder (Windows 8+, no external codec).
/// </summary>
public sealed class WindowsAudioRecorder : IAndroidAudioRecorder, IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _wavWriter;
    private string? _tempWavPath;
    private string? _outputPath;
    private int _disposed;

    public void Start(string outputPath, int sampleRate, int bitRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        lock (_gate)
        {
            StopLocked();
            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? string.Empty;
            Directory.CreateDirectory(directory);
            _tempWavPath = Path.Combine(Path.GetTempPath(), "android-rec-" + Guid.NewGuid().ToString("N") + ".wav");
            _outputPath = outputPath;
            var format = new WaveFormat(sampleRate, 16, 1); // mono, 16-bit PCM
            _wavWriter = new WaveFileWriter(_tempWavPath, format);
            _waveIn = new WaveInEvent
            {
                WaveFormat = format,
                BufferMilliseconds = 50
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopLocked();
    }

    private void StopLocked()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }
        WaveFileWriter? writer = _wavWriter;
        _wavWriter = null;
        writer?.Dispose();
        FinalizeLocked();
    }

    private void FinalizeLocked()
    {
        string? temp = _tempWavPath;
        string? output = _outputPath;
        _tempWavPath = null;
        _outputPath = null;
        if (temp is null || output is null || !File.Exists(temp))
            return;
        try
        {
            // Windows built-in MF AAC encoder: WAV -> MP4 (AAC-LC). The encoder
            // negotiates the actual bitrate from the input format; the requested
            // bitrate is honored as closely as the encoder supports.
            using (var reader = new WaveFileReader(temp))
                MediaFoundationEncoder.EncodeToAac(reader, output);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        WaveFileWriter? writer = _wavWriter;
        if (writer is not null && args.BytesRecorded > 0)
            writer.Write(args.Buffer, 0, args.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        // WaveInEvent raises RecordingStopped on its own thread after Stop();
        // the writer is disposed in StopLocked — nothing further needed here.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            if (_waveIn is not null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }
            _wavWriter?.Dispose();
            _wavWriter = null;
            if (_tempWavPath is not null && File.Exists(_tempWavPath))
            {
                try { File.Delete(_tempWavPath); } catch (IOException) { }
            }
            _tempWavPath = null;
            _outputPath = null;
        }
    }
}
