using Bb.Core.Models;
using Bb.Core.Services;
using Xunit;

namespace Bb.Core.Tests;

/// <summary>
/// Locks down the contract between the LLM and the action bar: every field
/// in ai-response.schema.json must be enforced exactly.
/// </summary>
public sealed class ResponseSchemaValidatorTests
{
    [Fact]
    public void RejectsEmpty()
    {
        var ok = ResponseSchemaValidator.TryValidate(string.Empty, out var r, out var err);
        Assert.False(ok);
        Assert.Null(r);
        Assert.Contains("empty", err);
    }

    [Fact]
    public void RejectsNonObjectRoot()
    {
        var ok = ResponseSchemaValidator.TryValidate("[]", out var r, out var err);
        Assert.False(ok);
        Assert.Null(r);
        Assert.Contains("array", err!);
    }

    [Fact]
    public void RejectsMissingCommand()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"risk\":\"low\",\"requires_admin\":false}", out _, out var err);
        Assert.False(ok);
        Assert.Contains("command", err!);
    }

    [Fact]
    public void RejectsEmptyCommand()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"command\":\"\",\"risk\":\"low\",\"requires_admin\":false}", out _, out var err);
        Assert.False(ok);
        Assert.Contains("at least 1", err!);
    }

    [Fact]
    public void RejectsCommandTooLong()
    {
        var huge = new string('a', 5000);
        var json = "{\"command\":\"" + huge + "\",\"risk\":\"low\",\"requires_admin\":false}";
        var ok = ResponseSchemaValidator.TryValidate(json, out _, out var err);
        Assert.False(ok);
        Assert.Contains("4096", err!);
    }

    [Fact]
    public void RejectsMissingRisk()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"command\":\"Get-Process\",\"requires_admin\":false}", out _, out var err);
        Assert.False(ok);
        Assert.Contains("risk", err!);
    }

    [Fact]
    public void RejectsInvalidRiskEnum()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"command\":\"Get-Process\",\"risk\":\"critical\",\"requires_admin\":false}", out _, out var err);
        Assert.False(ok);
        Assert.Contains("critical", err!);
    }

    [Fact]
    public void RejectsMissingRequiresAdmin()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"command\":\"Get-Process\",\"risk\":\"low\"}", out _, out var err);
        Assert.False(ok);
        Assert.Contains("requires_admin", err!);
    }

    [Fact]
    public void RejectsNonBoolRequiresAdmin()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"command\":\"Get-Process\",\"risk\":\"low\",\"requires_admin\":\"false\"}", out _, out var err);
        Assert.False(ok);
        Assert.Contains("boolean", err!);
    }

    [Fact]
    public void RejectsMalformedJson()
    {
        var ok = ResponseSchemaValidator.TryValidate("{not json", out _, out var err);
        Assert.False(ok);
        Assert.Contains("valid JSON", err!);
    }

    [Theory]
    [InlineData("low", RiskLevel.Low)]
    [InlineData("medium", RiskLevel.Medium)]
    [InlineData("high", RiskLevel.High)]
    public void AcceptsValidRiskValues(string risk, RiskLevel expected)
    {
        var json = "{\"command\":\"Get-Process\",\"risk\":\"" + risk + "\",\"requires_admin\":false}";
        var ok = ResponseSchemaValidator.TryValidate(json, out var r, out var err);
        Assert.True(ok, err);
        Assert.NotNull(r);
        Assert.Equal(expected, r!.Risk);
    }

    [Fact]
    public void AcceptsExplanationOptionalAndMissing()
    {
        var ok = ResponseSchemaValidator.TryValidate("{\"command\":\"Get-Process\",\"risk\":\"low\",\"requires_admin\":false}", out var r, out _);
        Assert.True(ok);
        Assert.Equal(string.Empty, r!.Explanation);
    }

    [Fact]
    public void AcceptsExplanationWhenPresent()
    {
        var ok = ResponseSchemaValidator.TryValidate(
            "{\"command\":\"Get-Process\",\"explanation\":\"lists running processes\",\"risk\":\"low\",\"requires_admin\":false}",
            out var r, out _);
        Assert.True(ok);
        Assert.Equal("lists running processes", r!.Explanation);
    }

    [Fact]
    public void RejectsExplanationTooLong()
    {
        var huge = new string('a', 1500);
        var json = "{\"command\":\"Get-Process\",\"explanation\":\"" + huge + "\",\"risk\":\"low\",\"requires_admin\":false}";
        var ok = ResponseSchemaValidator.TryValidate(json, out _, out var err);
        Assert.False(ok);
        Assert.Contains("1024", err!);
    }

    [Fact]
    public void RoundTripsCanonicalResponse()
    {
        var json = "{\"command\":\"Remove-Item C:\\\\tmp\\\\foo.txt\",\"explanation\":\"deletes one file\",\"risk\":\"medium\",\"requires_admin\":false}";
        var ok = ResponseSchemaValidator.TryValidate(json, out var r, out var err);
        Assert.True(ok, err);
        Assert.NotNull(r);
        Assert.Equal("Remove-Item C:\\tmp\\foo.txt", r!.Command);
        Assert.Equal("deletes one file", r.Explanation);
        Assert.Equal(RiskLevel.Medium, r.Risk);
        Assert.False(r.RequiresAdmin);
    }
}
