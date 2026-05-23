using System;

namespace Bb.Core.Services;

/// <summary>
/// Buffers a stream of OpenAI-style server-sent events (<c>data: {...}</c>
/// lines, terminated by <c>data: [DONE]</c>) and reassembles them into a
/// single complete JSON document. Scaffold stub in PR 2; real implementation
/// lands in PR 3.
/// </summary>
/// <remarks>
/// We never render partial commands — the assembler waits for <c>[DONE]</c>
/// before validating. This avoids the "looks malformed mid-flight" failure
/// mode in the v1 connectivity matrix.
/// </remarks>
public sealed class StreamingJsonAssembler
{
    /// <summary>Append one raw SSE line to the buffer.</summary>
    public void Append(string sseLine)
    {
        _ = sseLine;
        throw new NotImplementedException("StreamingJsonAssembler.Append is implemented in PR 3 (MVP).");
    }

    /// <summary>Whether the stream has terminated with <c>data: [DONE]</c>.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Return the assembled JSON once <see cref="IsComplete"/> is true.</summary>
    public string GetAssembled()
    {
        throw new NotImplementedException("StreamingJsonAssembler.GetAssembled is implemented in PR 3 (MVP).");
    }
}
