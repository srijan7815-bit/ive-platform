using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Ive.Core;

/// <summary>OpenAI-compatible chat-completions client with tool calling and bounded retry/backoff.</summary>
public sealed class AiClient
{
    private readonly HttpClient _http;

    public AiClient(HttpClient http = null) => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(100) };

    public async Task<JsonObject> ChatAsync(IveSettings s, JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            throw new InvalidOperationException("No API key configured. Open IVE → Settings and add one.");

        var bodyNode = new JsonObject
        {
            ["model"] = s.Model,
            ["messages"] = JsonNode.Parse(messages.ToJsonString()),
            ["max_tokens"] = 4096,
        };
        if (tools is { Count: > 0 })
        {
            bodyNode["tools"] = JsonNode.Parse(tools.ToJsonString());
            bodyNode["tool_choice"] = "auto";
        }
        var body = bodyNode.ToJsonString();

        Exception last = null;
        for (var attempt = 0; attempt <= 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, s.ApiBaseUrl.TrimEnd('/') + "/chat/completions")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);
                using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                {
                    var code = (int)res.StatusCode;
                    var detail = $"AI request failed ({code}): {Trim(text, 300)}";
                    if (code != 408 && code != 429 && code < 500) throw new InvalidOperationException(detail);
                    last = new HttpRequestException(detail);
                }
                else
                {
                    var msg = JsonNode.Parse(text)?["choices"]?[0]?["message"];
                    if (msg == null) throw new InvalidOperationException("AI response had no choices.");
                    return JsonNode.Parse(msg.ToJsonString()).AsObject();
                }
            }
            catch (HttpRequestException e)
            {
                last = e; // network-level failure: retry with backoff
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                last = new TimeoutException("The AI request timed out.", e);
            }

            if (attempt < 2) await Task.Delay(600 * (1 << attempt), ct).ConfigureAwait(false);
        }
        throw last ?? new InvalidOperationException("AI request failed.");
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n];
}
