using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Process-singleton HTTP client that speaks the OpenAI
/// <c>/v1/chat/completions</c> schema (also supported by Ollama, Azure OpenAI,
/// vLLM, llama.cpp's server, and most local model runners). Streams SSE on
/// the wire so we can start parsing as soon as the first tokens arrive.
/// </summary>
/// <remarks>
/// Surfacing of failure modes: every non-2xx status is mapped to a
/// <see cref="BbHttpException"/> with a typed <see cref="BbHttpException.StatusCode"/>
/// and (when 429/503) a parsed <see cref="BbHttpException.RetryAfter"/> hint.
/// Network-level failures (DNS, TLS, refused) come through as <see cref="HttpRequestException"/>;
/// the wrapper turns those into the "graceful failure" message in the v1
/// connectivity matrix.
///
/// Cancellation: callers pass a token that the cmdlet's
/// <c>StopProcessing</c> tripped — pressing Ctrl-C in PowerShell stops the
/// in-flight request rather than hanging the host.
/// </remarks>
public sealed class OpenAiCompatibleClient : IDisposable
{
    // One HttpClient per process. Disposing on every call would leak sockets
    // by way of TIME_WAIT (Microsoft's documented anti-pattern).
    private static readonly Lazy<HttpClient> s_sharedHttpClient = new(CreateHttpClient);

    private static HttpClient CreateHttpClient()
    {
        // HttpClientHandler defaults: HTTP/1.1, automatic decompression OFF.
        // OpenAI's edge supports HTTP/2; we ask for it but fall back gracefully.
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            // The per-provider TimeoutSeconds in ProviderConfig is what actually
            // governs request timeouts (via the linked CancellationToken). The
            // HttpClient.Timeout here is a hard backstop above that.
            Timeout = TimeSpan.FromMinutes(2),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("bb-cli", "0.1.0"));
        return client;
    }

    /// <inheritdoc cref="OpenAiCompatibleClient"/>
    public Task<AiResponse> CompleteAsync(
        ProviderConfig provider,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey is required.", nameof(apiKey));
        if (string.IsNullOrEmpty(systemPrompt)) throw new ArgumentException("systemPrompt is required.", nameof(systemPrompt));
        if (string.IsNullOrEmpty(userPrompt)) throw new ArgumentException("userPrompt is required.", nameof(userPrompt));
        if (string.IsNullOrEmpty(provider.Endpoint)) throw new ArgumentException("provider.Endpoint is required.", nameof(provider));
        if (string.IsNullOrEmpty(provider.Model)) throw new ArgumentException("provider.Model is required.", nameof(provider));

        return CompleteAsyncCore(provider, apiKey, systemPrompt, userPrompt, cancellationToken);
    }

    private async Task<AiResponse> CompleteAsyncCore(
        ProviderConfig provider,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var requestBodyJson = BuildRequestBody(provider, systemPrompt, userPrompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint)
        {
            Version = new Version(2, 0),
            Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (provider.Stream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }
        else
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // Linked timeout: per-provider seconds, OR cmdlet StopProcessing.
        using var perRequestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perRequestCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, provider.TimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await s_sharedHttpClient.Value
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, perRequestCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User pressed Ctrl-C — bubble up, the cmdlet host renders that.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Per-request timeout fired.
            throw new BbHttpException(
                (HttpStatusCode)0,
                $"bb: request to '{provider.Endpoint}' timed out after {provider.TimeoutSeconds}s.");
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowMappedHttpExceptionAsync(response).ConfigureAwait(false);
        }

        try
        {
            string raw;
            if (provider.Stream)
            {
                raw = await AssembleStreamingResponseAsync(response, perRequestCts.Token).ConfigureAwait(false);
            }
            else
            {
                raw = await ExtractNonStreamingContentAsync(response).ConfigureAwait(false);
            }

            if (!ResponseSchemaValidator.TryValidate(raw, out var parsed, out var error) || parsed is null)
            {
                throw new InvalidDataException(
                    $"bb: model response did not match the expected schema ({error}). Raw: {Truncate(raw, 512)}");
            }

            return SafetyClassifier.Apply(parsed);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static string BuildRequestBody(ProviderConfig provider, string systemPrompt, string userPrompt)
    {
        // Hand-crafted JSON so we don't ship a Newtonsoft.Json dependency just for
        // this one request. Escaping goes via System.Text.Json's JsonEncodedText.
        var encodedSystem = JsonEncodedText.Encode(systemPrompt);
        var encodedUser = JsonEncodedText.Encode(userPrompt);
        var encodedModel = JsonEncodedText.Encode(provider.Model);

        // response_format=json_object is the OpenAI-specific switch that
        // forces strict JSON. Local-runner endpoints ignore unknown fields,
        // so it's safe to always send it.
        return string.Concat(
            "{",
              "\"model\":\"", encodedModel.ToString(), "\",",
              "\"messages\":[",
                "{\"role\":\"system\",\"content\":\"", encodedSystem.ToString(), "\"},",
                "{\"role\":\"user\",\"content\":\"", encodedUser.ToString(), "\"}",
              "],",
              "\"temperature\":0.0,",
              "\"response_format\":{\"type\":\"json_object\"},",
              "\"stream\":", provider.Stream ? "true" : "false",
            "}");
    }

    private static async Task<string> AssembleStreamingResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var assembler = new StreamingJsonAssembler();

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            token.ThrowIfCancellationRequested();
            assembler.Append(line);
            if (assembler.IsComplete) break;
        }

        return assembler.GetAssembled();
    }

    private static async Task<string> ExtractNonStreamingContentAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        // Non-streaming chat completion shape:
        //   { "choices": [{ "message": { "content": "..." } }] }
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }
        throw new InvalidDataException("bb: non-streaming response did not contain choices[0].message.content.");
    }

    private static async Task ThrowMappedHttpExceptionAsync(HttpResponseMessage response)
    {
        var status = response.StatusCode;
        string? rawBody = null;
        try
        {
            rawBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch { /* swallow — diagnostic only */ }

        TimeSpan? retryAfter = null;
        if (response.Headers.RetryAfter is RetryConditionHeaderValue ra)
        {
            if (ra.Delta is TimeSpan delta) retryAfter = delta;
            else if (ra.Date is DateTimeOffset date)
            {
                var diff = date - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero) retryAfter = diff;
            }
        }

        string hint = status switch
        {
            HttpStatusCode.Unauthorized => "bb: the API key was rejected (401). Run 'Set-BbConfig' to store a new one.",
            HttpStatusCode.Forbidden => "bb: the API key does not have access to this endpoint or model (403).",
            HttpStatusCode.NotFound => "bb: endpoint not found (404). Check the provider's Endpoint URL.",
            // 429 / 422 are not constants on netstandard2.0's HttpStatusCode enum.
            (HttpStatusCode)429 => $"bb: rate-limited (429). Retry after {Format(retryAfter)}.",
            (HttpStatusCode)422 => "bb: the request was malformed (422). This is a bb bug — please open an issue with the raw response.",
            HttpStatusCode.InternalServerError => "bb: provider returned 500. Try again or switch providers.",
            HttpStatusCode.BadGateway => "bb: provider gateway error (502). Try again in a moment.",
            HttpStatusCode.ServiceUnavailable => $"bb: provider unavailable (503). Retry after {Format(retryAfter)}.",
            HttpStatusCode.GatewayTimeout => "bb: provider gateway timeout (504). Try again or lower TimeoutSeconds.",
            _ => $"bb: provider returned HTTP {(int)status} {status}.",
        };

        throw new BbHttpException(status, hint, retryAfter, Truncate(rawBody, 2048));
    }

    private static string Format(TimeSpan? span)
        => span is { } s ? s.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "s" : "the value in the Retry-After header";

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Length <= max ? s : s.Substring(0, max) + "…";
    }

    public void Dispose()
    {
        // Disposing the singleton would break subsequent calls. The instance
        // is process-lifetime; we expose IDisposable only for parity with
        // common HttpClient wrapper patterns.
    }
}
