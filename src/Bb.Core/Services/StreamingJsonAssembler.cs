using System;
using System.Text;
using System.Text.Json;

namespace Bb.Core.Services;

/// <summary>
/// Buffers an OpenAI-style server-sent event (SSE) stream — one
/// <c>data: {chunk}</c> line at a time, terminated by <c>data: [DONE]</c> —
/// and reassembles the streamed <c>delta.content</c> fragments into the
/// final raw JSON body that the model intended to emit.
/// </summary>
/// <remarks>
/// We never render partial commands. The action bar and safety classifier only
/// see fully assembled JSON because rendering a mid-flight <c>Remove-Item -Re</c>
/// would be misleading to the user.
/// </remarks>
public sealed class StreamingJsonAssembler
{
    private readonly StringBuilder _assembled = new();

    /// <summary>True once a <c>data: [DONE]</c> sentinel has been observed.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// Feed one raw SSE line (no trailing newline). Empty lines and the
    /// keep-alive comments OpenAI sends (lines beginning with ":") are
    /// ignored without complaint.
    /// </summary>
    public void Append(string sseLine)
    {
        if (sseLine is null) return;
        if (sseLine.Length == 0) return;
        if (sseLine[0] == ':') return; // keep-alive comment

        // SSE field syntax: "field: value". We only care about the data field.
        const string DataPrefix = "data:";
        if (!sseLine.StartsWith(DataPrefix, StringComparison.Ordinal)) return;

        var payload = sseLine.Substring(DataPrefix.Length).TrimStart();

        if (payload == "[DONE]")
        {
            IsComplete = true;
            return;
        }

        if (payload.Length == 0) return;

        // Each non-DONE data line is itself a JSON object representing one
        // streamed chunk. The shape (matching OpenAI chat.completions stream):
        //   { "choices": [{ "delta": { "content": "..." } }] }
        // Some chunks carry only a "role" field (the first one) or only
        // "finish_reason" (the last). We append whatever "content" we find.
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    var fragment = content.GetString();
                    if (!string.IsNullOrEmpty(fragment))
                    {
                        _assembled.Append(fragment);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed chunk — silently skip. The final validator will catch
            // any resulting structural damage in the assembled output.
        }
    }

    /// <summary>
    /// Return the assembled body. Callable before <see cref="IsComplete"/>
    /// (returns whatever has been accumulated so far), but normally only
    /// invoked after a [DONE] sentinel.
    /// </summary>
    public string GetAssembled() => _assembled.ToString();
}
