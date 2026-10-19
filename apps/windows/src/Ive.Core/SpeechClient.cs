using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Ive.Core;

/// <summary>Speech-to-text over the OpenAI-compatible <c>/audio/transcriptions</c> endpoint (Groq Whisper large-v3-turbo by default).</summary>
public sealed class SpeechClient
{
    private readonly HttpClient _http;

    public SpeechClient(HttpClient http = null) => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<string> TranscribeAsync(IveSettings s, byte[] audio, string fileName, string contentType, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(s.SttApiKey) ? s.ApiKey : s.SttApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No API key configured for voice input. Open IVE → Settings and add one.");
        if (audio == null || audio.Length < 1000) throw new InvalidOperationException("The recording was too short.");

        var baseUrl = string.IsNullOrWhiteSpace(s.SttBaseUrl) ? s.ApiBaseUrl : s.SttBaseUrl;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(s.SttModel), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0"), "temperature");

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/audio/transcriptions") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Transcription failed ({(int)res.StatusCode}): {(body.Length > 300 ? body[..300] : body)}");
        return (JsonNode.Parse(body)?["text"]?.ToString() ?? "").Trim();
    }
}
