using System;
using System.Threading;
using System.Threading.Tasks;
using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Singleton HTTP client that speaks the OpenAI <c>/v1/chat/completions</c>
/// schema. Streams server-sent events on success, surfaces typed errors for
/// every entry in the v1 connectivity failure matrix. Scaffold stub in PR 2;
/// real implementation lands in PR 3.
/// </summary>
public sealed class OpenAiCompatibleClient
{
    /// <summary>
    /// Send a chat completion request and assemble the streamed response into
    /// a validated <see cref="AiResponse"/>.
    /// </summary>
    public Task<AiResponse> CompleteAsync(
        ProviderConfig provider,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        // PR 3:
        //   1. HttpClient.SendAsync with HTTP/2 negotiation and SSE accept header.
        //   2. Stream-assemble JSON via StreamingJsonAssembler.
        //   3. Validate via ResponseSchemaValidator.
        //   4. Surface typed BbHttpException(StatusCode, RetryAfter?) on non-2xx.
        //   5. Cooperate with cancellationToken from Cmdlet.StopProcessing().
        _ = provider;
        _ = systemPrompt;
        _ = userPrompt;
        _ = cancellationToken;
        throw new NotImplementedException("OpenAiCompatibleClient.CompleteAsync is implemented in PR 3 (MVP).");
    }
}
