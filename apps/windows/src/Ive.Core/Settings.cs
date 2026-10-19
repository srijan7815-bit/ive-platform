using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ive.Core;

public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string cipher);
}

public sealed class IveSettings
{
    public string ApiBaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = "llama-3.3-70b-versatile";
    public string TriggerKey { get; set; } = "RightCtrl";
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public bool RequireApproval { get; set; } = true;
    public string WorkspaceDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public bool LaunchAtLogin { get; set; }

    // Voice input (Groq Whisper by default). Blank base URL / key = reuse the main ones.
    public string SttModel { get; set; } = "whisper-large-v3-turbo";
    public string SttBaseUrl { get; set; } = "";
    public string VoiceHotkey { get; set; } = "Ctrl+Alt+V";
    public bool VoiceAutoSend { get; set; } = true;

    // Music: the user chooses which app IVE controls.
    public string MusicApp { get; set; } = "";     // substring of the media source app id, e.g. "Spotify"
    public string MusicLaunch { get; set; } = "";  // what "Open music app" starts: exe path, URI (spotify:) or shell command

    /// <summary>Never written to disk in clear text — see <see cref="SettingsStore"/>.</summary>
    [JsonIgnore] public string ApiKey { get; set; } = "";

    /// <summary>Optional separate key for speech-to-text; blank = use <see cref="ApiKey"/>.</summary>
    [JsonIgnore] public string SttApiKey { get; set; } = "";

    public IveSettings Clone() => (IveSettings)MemberwiseClone();
}

/// <summary>Validated settings persisted to JSON; the API key is encrypted by the injected protector (DPAPI on Windows).</summary>
public sealed class SettingsStore
{
    public static readonly string[] TriggerKeys = { "RightCtrl", "LeftCtrl", "RightAlt", "LeftAlt", "RightShift", "CapsLock" };

    private sealed class Persisted
    {
        public IveSettings Settings { get; set; } = new();
        public string ApiKeyProtected { get; set; }
        public string SttApiKeyProtected { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _file;
    private readonly ISecretProtector _protector;

    public IveSettings Current { get; private set; } = new();
    public event Action Changed;

    public SettingsStore(string file, ISecretProtector protector)
    {
        _file = file;
        _protector = protector;
        var env = Environment.GetEnvironmentVariable("IVE_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) Current.ApiKey = env;
        try
        {
            if (File.Exists(file))
            {
                var p = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(file), Json);
                if (p?.Settings != null)
                {
                    var key = Current.ApiKey;
                    Current = p.Settings;
                    Current.ApiKey = key;
                    if (!string.IsNullOrEmpty(p.ApiKeyProtected) && string.IsNullOrEmpty(key))
                        Current.ApiKey = _protector.Unprotect(p.ApiKeyProtected);
                    if (!string.IsNullOrEmpty(p.SttApiKeyProtected))
                        Current.SttApiKey = _protector.Unprotect(p.SttApiKeyProtected);
                }
            }
        }
        catch
        {
            // Unreadable or corrupt settings: continue with defaults rather than refusing to start.
        }
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(Current.ApiKey);

    public string KeyHint => HasKey && Current.ApiKey.Length >= 4 ? "••••" + Current.ApiKey[^4..] : "";

    public void Update(Action<IveSettings> edit)
    {
        var next = Current.Clone();
        edit(next);
        Validate(next);
        Current = next;
        Save();
        Changed?.Invoke();
    }

    public static void Validate(IveSettings s)
    {
        if (!Uri.TryCreate((s.ApiBaseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("API base URL is not a valid URL.");
        var local = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
            throw new ArgumentException("API base URL must use https:// (http:// is only allowed for localhost).");
        s.ApiBaseUrl = uri.ToString().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(s.Model) || s.Model.Length > 100) throw new ArgumentException("Model name is required.");
        s.Model = s.Model.Trim();
        if (Array.IndexOf(TriggerKeys, s.TriggerKey) < 0) throw new ArgumentException("Unsupported trigger key.");
        if (string.IsNullOrWhiteSpace(s.Hotkey)) throw new ArgumentException("Shortcut is required.");
        if (!Directory.Exists(s.WorkspaceDir)) throw new ArgumentException("Workspace folder does not exist.");
        s.ApiKey = (s.ApiKey ?? "").Trim();
        s.SttApiKey = (s.SttApiKey ?? "").Trim();

        s.SttBaseUrl = (s.SttBaseUrl ?? "").Trim();
        if (s.SttBaseUrl.Length > 0)
        {
            if (!Uri.TryCreate(s.SttBaseUrl, UriKind.Absolute, out var stt) || (stt.Scheme != Uri.UriSchemeHttps && !(stt.Scheme == Uri.UriSchemeHttp && stt.IsLoopback)))
                throw new ArgumentException("Voice base URL must be a valid https:// URL (or blank to reuse the main one).");
            s.SttBaseUrl = stt.ToString().TrimEnd('/');
        }
        if (string.IsNullOrWhiteSpace(s.SttModel) || s.SttModel.Length > 100) throw new ArgumentException("Voice model is required.");
        s.SttModel = s.SttModel.Trim();
        if (string.IsNullOrWhiteSpace(s.VoiceHotkey)) throw new ArgumentException("Voice shortcut is required.");
        s.MusicApp = (s.MusicApp ?? "").Trim();
        s.MusicLaunch = (s.MusicLaunch ?? "").Trim();
        if (s.MusicApp.Length > 200 || s.MusicLaunch.Length > 500) throw new ArgumentException("Music app settings are too long.");
    }

    private void Save()
    {
        var p = new Persisted
        {
            Settings = Current,
            ApiKeyProtected = HasKey ? _protector.Protect(Current.ApiKey) : null,
            SttApiKeyProtected = string.IsNullOrWhiteSpace(Current.SttApiKey) ? null : _protector.Protect(Current.SttApiKey),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(p, Json));
        File.Move(tmp, _file, overwrite: true);
    }
}
