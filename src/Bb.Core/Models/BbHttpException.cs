using System;
using System.Net;

namespace Bb.Core.Models;

/// <summary>
/// Typed HTTP error surfaced by <see cref="Services.OpenAiCompatibleClient"/>.
/// The PowerShell wrapper inspects <see cref="StatusCode"/> to render a helpful
/// hint (e.g. "401: run 'bb config' to set your API key", "429: retry after
/// X seconds") rather than dumping a raw HTTP status to the console.
/// </summary>
public sealed class BbHttpException : Exception
{
    /// <summary>The HTTP status code that the provider returned.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Server-supplied retry-after hint, if any (only meaningful for 429/503).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Raw response body (truncated to 2048 chars) for diagnostics.</summary>
    public string? ResponseBody { get; }

    public BbHttpException(HttpStatusCode statusCode, string message, TimeSpan? retryAfter = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        ResponseBody = responseBody;
    }
}
