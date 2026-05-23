using Bb.Core.Models;
using Xunit;

namespace Bb.Core.Tests;

/// <summary>
/// The <see cref="RiskLevel"/> enum is the safety contract for the entire CLI:
/// (a) the LLM emits one of <c>low|medium|high</c>, (b) the C# safety classifier
/// can only ever upgrade the level (never downgrade), (c) the action bar relies
/// on the integer ordering to decide whether to default-accept or default-reject.
/// Locking the integer values down here guarantees that PR 3's
/// <c>SafetyClassifier</c> (regex backstop) can do <c>max(llm, regex)</c> safely.
/// </summary>
public sealed class RiskLevelTests
{
    [Fact]
    public void LowIsZero()
    {
        Assert.Equal(0, (int)RiskLevel.Low);
    }

    [Fact]
    public void MediumIsOne()
    {
        Assert.Equal(1, (int)RiskLevel.Medium);
    }

    [Fact]
    public void HighIsTwo()
    {
        Assert.Equal(2, (int)RiskLevel.High);
    }

    [Fact]
    public void OrderingIsLowLessThanMediumLessThanHigh()
    {
        Assert.True(RiskLevel.Low < RiskLevel.Medium);
        Assert.True(RiskLevel.Medium < RiskLevel.High);
    }
}
