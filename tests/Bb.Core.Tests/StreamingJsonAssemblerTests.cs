using Bb.Core.Services;
using Xunit;

namespace Bb.Core.Tests;

public sealed class StreamingJsonAssemblerTests
{
    [Fact]
    public void EmptyAssemblerIsNotComplete()
    {
        var a = new StreamingJsonAssembler();
        Assert.False(a.IsComplete);
        Assert.Equal(string.Empty, a.GetAssembled());
    }

    [Fact]
    public void IgnoresKeepAliveCommentLines()
    {
        var a = new StreamingJsonAssembler();
        a.Append(": OPENROUTER PROCESSING");
        a.Append(":");
        Assert.False(a.IsComplete);
        Assert.Equal(string.Empty, a.GetAssembled());
    }

    [Fact]
    public void IgnoresBlankLines()
    {
        var a = new StreamingJsonAssembler();
        a.Append(string.Empty);
        Assert.False(a.IsComplete);
        Assert.Equal(string.Empty, a.GetAssembled());
    }

    [Fact]
    public void AssemblesContentFragmentsInOrder()
    {
        var a = new StreamingJsonAssembler();
        a.Append("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}");
        a.Append("data: {\"choices\":[{\"delta\":{\"content\":\"{\\\"command\\\":\\\"Get-\"}}]}");
        a.Append("data: {\"choices\":[{\"delta\":{\"content\":\"Process\\\",\\\"risk\\\":\\\"low\\\",\\\"requires_admin\\\":false}\"}}]}");
        a.Append("data: [DONE]");

        Assert.True(a.IsComplete);
        Assert.Equal("{\"command\":\"Get-Process\",\"risk\":\"low\",\"requires_admin\":false}", a.GetAssembled());
    }

    [Fact]
    public void TolerantToWhitespaceAroundDataValue()
    {
        var a = new StreamingJsonAssembler();
        a.Append("data:   {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}");
        a.Append("data:[DONE]");
        Assert.True(a.IsComplete);
        Assert.Equal("ok", a.GetAssembled());
    }

    [Fact]
    public void SilentlySkipsMalformedChunks()
    {
        var a = new StreamingJsonAssembler();
        a.Append("data: {not json}");
        a.Append("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}");
        a.Append("data: [DONE]");
        Assert.Equal("hello", a.GetAssembled());
    }

    [Fact]
    public void IgnoresNonDataFields()
    {
        var a = new StreamingJsonAssembler();
        a.Append("event: ping");
        a.Append("id: 1");
        a.Append("data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}");
        Assert.Equal("x", a.GetAssembled());
    }

    [Fact]
    public void HandlesChunkWithFinishReasonAndNoContent()
    {
        var a = new StreamingJsonAssembler();
        a.Append("data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}");
        a.Append("data: {\"choices\":[{\"finish_reason\":\"stop\"}]}");
        a.Append("data: [DONE]");
        Assert.True(a.IsComplete);
        Assert.Equal("hi", a.GetAssembled());
    }
}
