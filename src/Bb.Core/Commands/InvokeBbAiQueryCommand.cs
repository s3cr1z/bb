using System;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Threading;
using Bb.Core.Models;
using Bb.Core.Services;

namespace Bb.Core.Commands;

/// <summary>
/// Latency-sensitive entry point invoked by the <c>bb.psm1</c> wrapper.
/// Routes the user's prompt through (a) the configured provider via
/// <see cref="OpenAiCompatibleClient"/>, (b) the schema validator,
/// (c) the upgrade-only <see cref="SafetyClassifier"/> regex backstop, and
/// returns a typed <see cref="AiResponse"/> to PowerShell for in-session
/// execution.
/// </summary>
/// <remarks>
/// The <c>-Stub</c> switch short-circuits the network and safety chain so
/// the contract tests in <c>tests/Bb.Module.Tests/</c> can exercise the
/// PowerShell wrapper deterministically without HTTP. The cmdlet's
/// <see cref="StopProcessing"/> override cancels the in-flight request when
/// the user presses Ctrl-C.
/// </remarks>
[Cmdlet(VerbsLifecycle.Invoke, "BbAiQuery")]
[OutputType(typeof(AiResponse))]
public sealed class InvokeBbAiQueryCommand : PSCmdlet
{
    // Lazily-loaded singleton so the first invocation is the only one paying
    // HttpClient construction cost. Subsequent calls reuse the same instance.
    private static readonly Lazy<OpenAiCompatibleClient> s_client =
        new(() => new OpenAiCompatibleClient());

    private CancellationTokenSource? _cts;

    /// <summary>The user's natural-language prompt.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNullOrEmpty]
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>The current working directory of the caller's session.</summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Short-circuit the network path and return a deterministic echo
    /// response. Used by the Pester contract tests so they don't need a
    /// configured provider.
    /// </summary>
    [Parameter]
    public SwitchParameter Stub { get; set; }

    /// <summary>
    /// Override the active provider for this single invocation.
    /// Defaults to whichever provider is selected in <c>config.json</c>.
    /// </summary>
    [Parameter]
    public string? Provider { get; set; }

    /// <summary>
    /// Override the per-request timeout for this invocation.
    /// Defaults to the provider's configured TimeoutSeconds.
    /// </summary>
    [Parameter]
    [ValidateRange(1, 600)]
    public int? TimeoutSeconds { get; set; }

    protected override void BeginProcessing()
    {
        _cts = new CancellationTokenSource();
    }

    protected override void ProcessRecord()
    {
        if (Stub.IsPresent)
        {
            WriteObject(BuildStubResponse(UserPrompt));
            return;
        }

        var response = ExecuteRealQuery();
        WriteObject(response);
    }

    protected override void StopProcessing()
    {
        // Ctrl-C: tear down the in-flight HTTP request. The client respects
        // this via the linked CancellationTokenSource in CompleteAsync.
        _cts?.Cancel();
    }

    protected override void EndProcessing()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private AiResponse ExecuteRealQuery()
    {
        var config = BbConfigStore.Load();
        var providerName = Provider ?? config.ActiveProvider;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException(
                "bb: no active provider is configured. Run 'Set-BbConfig -Provider openai -ApiKey sk-...' first, " +
                "or pass -Provider to this cmdlet.");
        }

        if (!config.Providers.TryGetValue(providerName!, out var providerConfig) || providerConfig is null)
        {
            throw new InvalidOperationException(
                $"bb: provider '{providerName}' is not configured. Run 'Set-BbConfig -Provider {providerName} ...' to add it.");
        }

        if (TimeoutSeconds is int seconds)
        {
            providerConfig.TimeoutSeconds = seconds;
        }

        var apiKey = CredentialStore.GetSecret(providerName!);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                $"bb: no API key is stored for provider '{providerName}'. Run 'Set-BbConfig -Provider {providerName} -ApiKey ...'.");
        }

        var systemPrompt = LoadSystemPrompt();
        var enrichedUserPrompt = string.IsNullOrEmpty(WorkingDirectory)
            ? UserPrompt
            : $"[cwd: {WorkingDirectory}]\n{UserPrompt}";

        // Run the async call synchronously inside the cmdlet's ProcessRecord.
        // GetAwaiter().GetResult() is the documented pattern for binding async
        // code into the cmdlet pipeline without deadlocks (PowerShell's host
        // does not install a sync context that would cause one).
        try
        {
            return s_client.Value
                .CompleteAsync(providerConfig, apiKey!, systemPrompt, enrichedUserPrompt, _cts!.Token)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C — translate to a typed terminating error so the host
            // doesn't print a confusing stack trace.
            ThrowTerminatingError(new ErrorRecord(
                new PipelineStoppedException("bb: query cancelled by user."),
                "BbCancelled",
                ErrorCategory.OperationStopped,
                UserPrompt));
            throw; // unreachable
        }
    }

    private static AiResponse BuildStubResponse(string prompt)
    {
        return new AiResponse
        {
            Command = "Write-Output 'bb scaffold: " + EscapeForSingleQuoted(prompt) + "'",
            Explanation = "Scaffold stub. Use without -Stub to call the real provider.",
            Risk = RiskLevel.Low,
            RequiresAdmin = false,
        };
    }

    private static string LoadSystemPrompt()
    {
        // The system prompt is embedded as Resources/system-prompt.md. We
        // read it once on first call; the JIT-static field would be ideal
        // but this is simple enough.
        var asm = typeof(InvokeBbAiQueryCommand).Assembly;
        var resourceName = "Bb.Core.Resources.system-prompt.md";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"bb: embedded resource '{resourceName}' not found in {asm.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string EscapeForSingleQuoted(string s) => s.Replace("'", "''");
}
