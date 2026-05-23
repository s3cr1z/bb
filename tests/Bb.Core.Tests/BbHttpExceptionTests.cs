using System;
using System.Net;
using Bb.Core.Models;
using Xunit;

namespace Bb.Core.Tests;

public sealed class BbHttpExceptionTests
{
    [Fact]
    public void CarriesStatusAndMessage()
    {
        var ex = new BbHttpException(HttpStatusCode.Unauthorized, "bad key");
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("bad key", ex.Message);
        Assert.Null(ex.RetryAfter);
        Assert.Null(ex.ResponseBody);
    }

    [Fact]
    public void CarriesRetryAfterAndBodyWhenProvided()
    {
        var ex = new BbHttpException((HttpStatusCode)429, "rate limit", TimeSpan.FromSeconds(30), "raw");
        Assert.Equal((HttpStatusCode)429, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
        Assert.Equal("raw", ex.ResponseBody);
    }
}
