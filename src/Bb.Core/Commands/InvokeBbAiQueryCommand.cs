using System.Management.Automation;
using System.Threading;
using Bb.Core.Models;

namespace Bb.Core.Commands;

/// <summary>
/// Latency-sensitive entry point invoked by the <c>bb.psm1</c> wrapper. In PR 2
/// (scaffold) this is a deterministic stub that returns an echo response so
/// the contract tests can verify the wiring end-to-end without a network
/// dependency. PR 3 (MVP) replaces the body with the real
/// <see cref="Bb.Core.Services.OpenAiCompatibleClient"/> call, JSON-schema
/// validation, and safety-classifier chain.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "BbAiQuery")]
[OutputType(typeof(AiResponse))]
public sealed class InvokeBbAiQueryCommand : PSCmdlet
{
    private CancellationTokenSource? _cts;

    /// <summary>The user's natural-language prompt.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNullOrEmpty]
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>The current working directory of the caller's session.</summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// When set, return a deterministic echo response without touching the
    /// network. PR 2 always behaves as if this is set; PR 3 will toggle it
    /// based on whether an <see cref="Models.ProviderConfig"/> is configured.
    /// </summary>
    [Parameter]
    public SwitchParameter Stub { get; set; }

    protected override void BeginProcessing()
    {
        _cts = new CancellationTokenSource();
    }

    protected override void ProcessRecord()
    {
        // PR 2 stub behavior: deterministic round-trip so the wrapper and
        // contract test can run end-to-end without HTTP. Note we intentionally
        // do NOT echo arbitrary user input into the Command field beyond a
        // safe template — the safety classifier in PR 3 will sit on top of
        // this and the contract test stubs it out via mocks regardless.
        var response = new AiResponse
        {
            Command = "Write-Output 'bb scaffold: " + EscapeForSingleQuoted(UserPrompt) + "'",
            Explanation = "Scaffold stub. PR 3 replaces this with a real AI call.",
            Risk = RiskLevel.Low,
            RequiresAdmin = false,
        };

        WriteObject(response);
    }

    protected override void StopProcessing()
    {
        // Real implementation in PR 3 will cancel an in-flight HttpClient
        // call via this token. Stub does nothing because it doesn't do I/O.
        _cts?.Cancel();
    }

    protected override void EndProcessing()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private static string EscapeForSingleQuoted(string s) => s.Replace("'", "''");
}
