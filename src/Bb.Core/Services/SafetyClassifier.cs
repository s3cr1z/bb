using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Compiled-in regex blacklist that <em>upgrades</em> the LLM's risk
/// classification (never downgrades). Lives in the C# core, not the
/// <c>.psm1</c>, so a malicious profile script cannot monkey-patch the
/// safety net at runtime.
/// </summary>
/// <remarks>
/// Scaffold stub in PR 2 — returns the LLM's classification unchanged. PR 3
/// (Milestone 4) lands the full regex set against a 50-prompt red-team test
/// suite that fails the build on any false negative.
/// </remarks>
public static class SafetyClassifier
{
    /// <summary>
    /// Apply the upgrade-only safety filter to a model response.
    /// </summary>
    /// <returns>
    /// A new <see cref="AiResponse"/> whose <see cref="AiResponse.Risk"/> is
    /// <c>max(model.Risk, regex.Risk)</c> and whose
    /// <see cref="AiResponse.RequiresAdmin"/> is <c>model.RequiresAdmin || regex.RequiresAdmin</c>.
    /// </returns>
    public static AiResponse Apply(AiResponse response)
    {
        // PR 3 will load the regex set from a compiled-in resource and apply
        // it here. PR 2 returns the response unchanged so the wiring is
        // verifiable end-to-end without the classifier doing anything.
        return response;
    }
}
