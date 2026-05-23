namespace Bb.Core.Models;

/// <summary>
/// Three-value risk classification for a generated command. Empirically more
/// consistent than a single boolean SAFE/UNSAFE flag in LLM outputs.
/// </summary>
public enum RiskLevel
{
    /// <summary>Read-only or otherwise harmless. Eligible for auto-execute.</summary>
    Low = 0,

    /// <summary>Mutates user files or environment in non-trivial ways. Requires explicit [E] keypress.</summary>
    Medium = 1,

    /// <summary>Deletes data, mutates system configuration, stops services, or executes downloaded code. Requires typed "yes".</summary>
    High = 2,
}
