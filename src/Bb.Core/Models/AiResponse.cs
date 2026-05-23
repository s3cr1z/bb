namespace Bb.Core.Models;

/// <summary>
/// The validated response shape returned by <c>Invoke-BbAiQuery</c>. Mirrors
/// <c>src/Bb.Core/Models/ai-response.schema.json</c>, which is the source of
/// truth for the JSON Schema validator.
/// </summary>
public sealed class AiResponse
{
    /// <summary>
    /// The single PowerShell statement or pipeline the model proposes. Must be
    /// non-empty and shorter than 4096 characters per schema.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable rationale shown to the user above the action bar.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// The LLM's risk classification. The safety classifier may upgrade this
    /// (never downgrade) based on the compiled-in regex blacklist.
    /// </summary>
    public RiskLevel Risk { get; set; } = RiskLevel.High;

    /// <summary>
    /// True if the command requires an elevated PowerShell session to run.
    /// </summary>
    public bool RequiresAdmin { get; set; }
}
