using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Validates a raw JSON document against
/// <c>src/Bb.Core/Models/ai-response.schema.json</c> (embedded as an assembly
/// resource so it cannot drift from the compiled validator). Scaffold stub in
/// PR 2; real implementation lands in PR 3.
/// </summary>
public static class ResponseSchemaValidator
{
    /// <summary>
    /// Try to deserialize and validate a JSON document.
    /// </summary>
    /// <returns>
    /// <c>true</c> and the parsed response on success;
    /// <c>false</c> and a human-readable error otherwise.
    /// </returns>
    public static bool TryValidate(string json, out AiResponse? response, out string? error)
    {
        // PR 3: JsonSchema.Net validator with the embedded schema, then
        // System.Text.Json deserialization. PR 2 stub always fails so callers
        // know to use the InvokeBbAiQueryCommand stub directly.
        _ = json;
        response = null;
        error = "ResponseSchemaValidator.TryValidate is implemented in PR 3 (MVP).";
        return false;
    }
}
