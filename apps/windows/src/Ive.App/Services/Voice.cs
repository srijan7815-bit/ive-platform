using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ive.Core;
using NAudio.Wave;

namespace Ive.App.Services;

/// <summary>Microphone capture (16 kHz mono PCM) encoded to a WAV in memory.</summary>
public sealed class VoiceRecorder
{
    private const int Rate = 16000;
    private const int MaxBytes = Rate * 2 * 120; // 2 minutes

    private WaveInEvent _in;
    private MemoryStream _pcm;
    private TaskCompletionSource<byte[]> _done;

    public event Action<double> Level;

    public bool IsRecording => _in != null;

    public void Start()
    {
        if (_in != null) return;
        var pcm = new MemoryStream();
        _pcm = pcm;
        var wave = new WaveInEvent { WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 50 };
        wave.DataAvailable += (_, e) =>
        {
            lock (pcm) { if (pcm.Length < MaxBytes) pcm.Write(e.Buffer, 0, e.BytesRecorded); }
            Level?.Invoke(Rms(e.Buffer, e.BytesRecorded));
        };
        wave.RecordingStopped += (_, _) =>
        {
            wave.Dispose();
            byte[] data;
            lock (pcm) data = pcm.ToArray();
            _done?.TrySetResult(ToWav(data));
        };
        wave.StartRecording();
        _in = wave;
    }

    public Task<byte[]> StopAsync()
    {
        var wave = _in;
        if (wave == null) return Task.FromResult<byte[]>(null);
        _in = null;
        _done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        wave.StopRecording();
        return _done.Task;
    }

    private static double Rms(byte[] buf, int count)
    {
        double sum = 0;
        var n = count / 2;
        if (n == 0) return 0;
        for (var i = 0; i < n; i++)
        {
            double v = BitConverter.ToInt16(buf, i * 2) / 32768.0;
            sum += v * v;
        }
        return Math.Min(1, Math.Sqrt(sum / n) * 5);
    }

    private static byte[] ToWav(byte[] pcm)
    {
        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms, Encoding.ASCII, true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }
}

public enum VoiceState { Idle, Recording, Transcribing }

/// <summary>Record → transcribe (Groq Whisper large-v3-turbo by default). One shared instance: only one mic user at a time.</summary>
public sealed class VoiceSession
{
    private readonly SettingsStore _settings;
    private readonly SpeechClient _speech = new();
    private readonly VoiceRecorder _rec = new();

    public VoiceState State { get; private set; } = VoiceState.Idle;
    public event Action<VoiceState> StateChanged;
    public event Action<double> Level;

    public VoiceSession(SettingsStore settings)
    {
        _settings = settings;
        _rec.Level += l => Level?.Invoke(l);
    }

    private void Set(VoiceState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    public void Start()
    {
        if (State != VoiceState.Idle) return;
        var c = _settings.Current;
        if (!_settings.HasKey && string.IsNullOrWhiteSpace(c.SttApiKey))
            throw new InvalidOperationException("Add your API key in Settings to use voice input.");
        _rec.Start();
        Set(VoiceState.Recording);
    }

    public async Task<string> StopAsync()
    {
        if (State != VoiceState.Recording) return null;
        Set(VoiceState.Transcribing);
        try
        {
            var wav = await _rec.StopAsync();
            return await _speech.TranscribeAsync(_settings.Current, wav, "voice.wav", "audio/wav", CancellationToken.None);
        }
        finally
        {
            Set(VoiceState.Idle);
        }
    }

    /// <summary>Discard the current recording without transcribing.</summary>
    public void Cancel()
    {
        if (State != VoiceState.Recording) return;
        _ = _rec.StopAsync();
        Set(VoiceState.Idle);
    }
}
