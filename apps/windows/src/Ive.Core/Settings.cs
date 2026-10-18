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
    public string ApiBaseUrl { get; set; } = "https://codecraftapi.com/v1";
    public string Model { get; set; } = "claude-opus-5-5";
    public string TriggerKey { get; set; } = "RightCtrl";
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public bool RequireApproval { get; set; } = true;
    public string WorkspaceDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public bool LaunchAtLogin { get; set; }

    /// <summary>Never written to disk in clear text — see <see cref="SettingsStore"/>.</summary>
    [JsonIgnore] public string ApiKey { get; set; } = "";

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
    }

    private void Save()
    {
        var p = new Persisted
        {
            Settings = Current,
            ApiKeyProtected = HasKey ? _protector.Protect(Current.ApiKey) : null,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(p, Json));
        File.Move(tmp, _file, overwrite: true);
    }
}
