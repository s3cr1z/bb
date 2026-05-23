using Bb.Core.Models;
using Xunit;

namespace Bb.Core.Tests;

/// <summary>
/// The <see cref="AiResponse"/> model is what crosses the C#-to-PowerShell
/// boundary. PR 2 only verifies that default values are conservative (no
/// blank-but-claimed-low responses) — the JSON-schema validator and the safety
/// classifier (PR 3) sit on top of these defaults and rely on them to fail
/// closed if the LLM returns a malformed payload.
/// </summary>
public sealed class AiResponseTests
{
    [Fact]
    public void NewInstanceDefaultsToHighRisk()
    {
        // Fail-closed default: if a code path ever forgets to set Risk explicitly,
        // the worst case is the user gets prompted to confirm (High), not auto-execute.
        var r = new AiResponse();
        Assert.Equal(RiskLevel.High, r.Risk);
    }

    [Fact]
    public void NewInstanceHasEmptyCommandAndExplanation()
    {
        var r = new AiResponse();
        Assert.Equal(string.Empty, r.Command);
        Assert.Equal(string.Empty, r.Explanation);
    }

    [Fact]
    public void NewInstanceDoesNotRequireAdminByDefault()
    {
        var r = new AiResponse();
        Assert.False(r.RequiresAdmin);
    }
}
