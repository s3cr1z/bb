using System;
using System.Text.Json;
using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Validates that a raw JSON document conforms to
/// <c>src/Bb.Core/Models/ai-response.schema.json</c> and parses it into an
/// <see cref="AiResponse"/>.
/// </summary>
/// <remarks>
/// This validator is intentionally hand-rolled rather than reaching for a
/// schema library. The shape is fixed (4 fields), the validation rules are
/// simple, and shipping a schema library would push us across the netstandard
/// dependency wall. Every field is checked explicitly with a precise error
/// message so the LLM operator (or a contributor changing the prompt) gets
/// useful diagnostics, not "validation failed at path /risk".
/// </remarks>
public static class ResponseSchemaValidator
{
    // Mirrors enum in ai-response.schema.json. Listed lowercase exactly as
    // emitted by the LLM (the system prompt enforces lowercase).
    private const string LowRisk = "low";
    private const string MediumRisk = "medium";
    private const string HighRisk = "high";

    private const int MaxCommandLength = 4096;
    private const int MaxExplanationLength = 1024;

    /// <summary>
    /// Try to parse and validate <paramref name="json"/> against the schema.
    /// </summary>
    /// <returns>
    /// <c>true</c> with a populated <paramref name="response"/> on success;
    /// <c>false</c> with a human-readable <paramref name="error"/> otherwise.
    /// </returns>
    public static bool TryValidate(string json, out AiResponse? response, out string? error)
    {
        response = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "response was empty";
            return false;
        }

        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = "response is not valid JSON: " + ex.Message;
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "expected a JSON object at the root, got " + root.ValueKind.ToString().ToLowerInvariant();
                return false;
            }

            // ---- command (required, string, 1..4096) ----
            if (!root.TryGetProperty("command", out var commandEl))
            {
                error = "missing required property 'command'";
                return false;
            }
            if (commandEl.ValueKind != JsonValueKind.String)
            {
                error = "'command' must be a string";
                return false;
            }
            var command = commandEl.GetString() ?? string.Empty;
            if (command.Length == 0)
            {
                error = "'command' must be at least 1 character";
                return false;
            }
            if (command.Length > MaxCommandLength)
            {
                error = $"'command' exceeds {MaxCommandLength} characters (was {command.Length})";
                return false;
            }

            // ---- explanation (optional, string, <= 1024) ----
            string explanation = string.Empty;
            if (root.TryGetProperty("explanation", out var explanationEl) && explanationEl.ValueKind != JsonValueKind.Null)
            {
                if (explanationEl.ValueKind != JsonValueKind.String)
                {
                    error = "'explanation' must be a string when present";
                    return false;
                }
                explanation = explanationEl.GetString() ?? string.Empty;
                if (explanation.Length > MaxExplanationLength)
                {
                    error = $"'explanation' exceeds {MaxExplanationLength} characters (was {explanation.Length})";
                    return false;
                }
            }

            // ---- risk (required, enum low|medium|high) ----
            if (!root.TryGetProperty("risk", out var riskEl))
            {
                error = "missing required property 'risk'";
                return false;
            }
            if (riskEl.ValueKind != JsonValueKind.String)
            {
                error = "'risk' must be a string";
                return false;
            }
            var riskString = riskEl.GetString();
            RiskLevel risk;
            switch (riskString)
            {
                case LowRisk: risk = RiskLevel.Low; break;
                case MediumRisk: risk = RiskLevel.Medium; break;
                case HighRisk: risk = RiskLevel.High; break;
                default:
                    error = $"'risk' must be one of [low, medium, high], got '{riskString}'";
                    return false;
            }

            // ---- requires_admin (required, bool) ----
            if (!root.TryGetProperty("requires_admin", out var adminEl))
            {
                error = "missing required property 'requires_admin'";
                return false;
            }
            if (adminEl.ValueKind != JsonValueKind.True && adminEl.ValueKind != JsonValueKind.False)
            {
                error = "'requires_admin' must be a boolean";
                return false;
            }

            response = new AiResponse
            {
                Command = command,
                Explanation = explanation,
                Risk = risk,
                RequiresAdmin = adminEl.GetBoolean(),
            };
            return true;
        }
    }
}
