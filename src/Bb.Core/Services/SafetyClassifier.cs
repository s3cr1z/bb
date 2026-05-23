using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Compiled-in regex blacklist that <em>upgrades</em> the LLM's risk
/// classification (never downgrades). The set lives here in the C# core, not
/// in the <c>.psm1</c>, so a hostile profile script cannot monkey-patch the
/// safety net at runtime.
/// </summary>
/// <remarks>
/// Contract: <c>Apply(r)</c> returns a response with
/// <c>Risk = max(r.Risk, regexRisk)</c> and
/// <c>RequiresAdmin = r.RequiresAdmin || regexRequiresAdmin</c>.
/// We never <em>lower</em> the LLM's claim. If the LLM says <c>high</c> and
/// the regex says <c>low</c>, the answer is <c>high</c>.
///
/// Coverage: read the <c>tests/Bb.Module.Tests/SafetyClassifier.Tests.ps1</c>
/// red-team corpus for the exact prompts we test against. Adding a new rule
/// here without also updating that corpus is a review-blocker.
/// </remarks>
public static class SafetyClassifier
{
    // Each rule pairs an explanation with a compiled, case-insensitive regex.
    // Compiled once at class-load time for the latency budget.
    private sealed record Rule(RiskLevel MinimumRisk, bool RequiresAdmin, string Reason, Regex Pattern);

    private static readonly Rule[] s_rules = BuildRules();

    /// <summary>Apply the upgrade-only safety filter to a response.</summary>
    public static AiResponse Apply(AiResponse response)
    {
        if (response is null) throw new ArgumentNullException(nameof(response));

        var (regexRisk, regexAdmin, _) = Classify(response.Command);

        return new AiResponse
        {
            Command = response.Command,
            Explanation = response.Explanation,
            Risk = response.Risk > regexRisk ? response.Risk : regexRisk,
            RequiresAdmin = response.RequiresAdmin || regexAdmin,
        };
    }

    /// <summary>
    /// Public for tests + the <c>bb --debug</c> diagnostics path.
    /// Returns the maximum regex risk + admin requirement + the matched rule
    /// description (or <c>null</c> if nothing matched).
    /// </summary>
    public static (RiskLevel Risk, bool RequiresAdmin, string? MatchedRuleReason) Classify(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return (RiskLevel.Low, false, null);
        }

        var maxRisk = RiskLevel.Low;
        var requiresAdmin = false;
        string? reason = null;

        foreach (var rule in s_rules)
        {
            if (rule.Pattern.IsMatch(command))
            {
                if (rule.MinimumRisk > maxRisk)
                {
                    maxRisk = rule.MinimumRisk;
                    reason = rule.Reason;
                }
                if (rule.RequiresAdmin) requiresAdmin = true;
            }
        }

        return (maxRisk, requiresAdmin, reason);
    }

    private static Rule[] BuildRules()
    {
        // RegexOptions.Compiled is intentionally omitted — netstandard2.0's
        // implementation on .NET Framework 4.7.2 has worse startup-cost
        // characteristics for Compiled regexes than the default interpreted
        // engine for tiny patterns like these. We pay no measurable cost.
        const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        var rules = new List<Rule>
        {
            // ---- HIGH RISK: data destruction ----
            new(RiskLevel.High, false, "Remove-Item with -Recurse",
                new Regex(@"\bRemove-Item\b[^|;]*\s-Recurse\b", Opts)),
            new(RiskLevel.High, false, "Remove-Item with -Force",
                new Regex(@"\bRemove-Item\b[^|;]*\s-Force\b", Opts)),
            new(RiskLevel.High, false, "rm -rf (POSIX-style)",
                // Matches -rf, -fr, -Rf, -fR, --recursive, --force in any combo on a single flag.
                new Regex(@"\brm\s+(-[a-z]*r[a-z]*f|-[a-z]*f[a-z]*r|--recursive|--force)\b", Opts)),
            new(RiskLevel.High, false, "del /s or rd /s (cmd-style recursive delete)",
                new Regex(@"\b(del|rd|rmdir)\s+[/-]s\b", Opts)),
            new(RiskLevel.High, false, "Clear-Content on a path",
                new Regex(@"\bClear-Content\b", Opts)),
            new(RiskLevel.High, false, "Format-Volume",
                new Regex(@"\bFormat-Volume\b", Opts)),
            new(RiskLevel.High, false, "Clear-Disk",
                new Regex(@"\bClear-Disk\b", Opts)),
            new(RiskLevel.High, false, "Initialize-Disk",
                new Regex(@"\bInitialize-Disk\b", Opts)),
            new(RiskLevel.High, false, "Reset-ComputerMachinePassword",
                new Regex(@"\bReset-ComputerMachinePassword\b", Opts)),

            // ---- HIGH RISK: service / system mutation (also admin) ----
            new(RiskLevel.High, true, "Stop-Service / Start-Service / Restart-Service / Set-Service",
                new Regex(@"\b(Stop|Start|Restart|Set|Suspend|Resume)-Service\b", Opts)),
            new(RiskLevel.High, true, "sc.exe service control",
                new Regex(@"\bsc(\.exe)?\s+(stop|start|delete|config|create)\b", Opts)),
            new(RiskLevel.High, true, "Set-ExecutionPolicy (any scope)",
                new Regex(@"\bSet-ExecutionPolicy\b", Opts)),
            new(RiskLevel.High, true, "shutdown / Restart-Computer / Stop-Computer",
                new Regex(@"\b(shutdown(\.exe)?|Restart-Computer|Stop-Computer)\b", Opts)),
            new(RiskLevel.High, true, "bcdedit / dism / netsh advfirewall",
                new Regex(@"\b(bcdedit|dism|netsh\s+advfirewall)\b", Opts)),
            new(RiskLevel.High, true, "Disable-NetFirewallProfile / firewall mutations",
                new Regex(@"\b(Disable-NetFirewallProfile|Set-NetFirewallProfile|New-NetFirewallRule|Remove-NetFirewallRule)\b", Opts)),
            new(RiskLevel.High, true, "Scheduled task mutation",
                new Regex(@"\b(Register-ScheduledTask|Unregister-ScheduledTask|Set-ScheduledTask|schtasks)\b", Opts)),

            // ---- HIGH RISK: registry writes under HKLM / HKEY_LOCAL_MACHINE ----
            new(RiskLevel.High, true, "Registry write under HKLM:",
                new Regex(@"\b(New-Item|Set-ItemProperty|Remove-ItemProperty|Remove-Item|New-ItemProperty)\b[^|;]*\s(HKLM:|HKEY_LOCAL_MACHINE)", Opts)),
            new(RiskLevel.High, true, "reg.exe writes",
                new Regex(@"\breg(\.exe)?\s+(add|delete|import|copy)\b", Opts)),

            // ---- HIGH RISK: download-and-execute pattern ----
            new(RiskLevel.High, false, "Invoke-WebRequest piped into Invoke-Expression",
                new Regex(@"\b(Invoke-WebRequest|iwr|curl|wget|Invoke-RestMethod|irm)\b[^|;]*\|\s*(Invoke-Expression|iex)\b", Opts)),
            new(RiskLevel.High, false, "iex on raw downloaded content",
                new Regex(@"\b(Invoke-Expression|iex)\b[^|;]*(\(?\s*(Invoke-WebRequest|iwr|curl|wget|Invoke-RestMethod|irm)\b|FromBase64String)", Opts)),
            new(RiskLevel.High, false, "encoded command (-EncodedCommand)",
                new Regex(@"\b-Encoded(Command|Arguments)\b", Opts)),
            new(RiskLevel.High, false, "powershell -enc / -e shorthand",
                new Regex(@"\b(powershell|pwsh)(\.exe)?\s+(-[eE](nc(odedCommand)?)?)\b", Opts)),

            // ---- HIGH RISK: paths under Windows / Program Files / system32 ----
            new(RiskLevel.High, true, "Write under C:\\Windows or %SystemRoot%",
                new Regex(@"\b(Remove-Item|New-Item|Set-Content|Add-Content|Move-Item|Copy-Item)\b[^|;]*\s(C:\\Windows|%SystemRoot%|\$env:SystemRoot|\$env:windir)", Opts)),
            new(RiskLevel.High, true, "Write under Program Files",
                new Regex(@"\b(Remove-Item|New-Item|Set-Content|Add-Content|Move-Item|Copy-Item)\b[^|;]*\s(C:\\Program Files( \(x86\))?|\$env:ProgramFiles( \(x86\))?)", Opts)),

            // ---- MEDIUM RISK: user-state mutation ----
            new(RiskLevel.Medium, false, "Plain Remove-Item",
                new Regex(@"\bRemove-Item\b", Opts)),
            new(RiskLevel.Medium, false, "Move-Item / Rename-Item",
                new Regex(@"\b(Move-Item|Rename-Item)\b", Opts)),
            new(RiskLevel.Medium, false, "Copy-Item",
                new Regex(@"\bCopy-Item\b", Opts)),
            new(RiskLevel.Medium, false, "Setting an environment variable",
                new Regex(@"\$env:[A-Za-z_][A-Za-z0-9_]*\s*=", Opts)),
            new(RiskLevel.Medium, false, "Set-Location / cd / chdir",
                new Regex(@"\b(Set-Location|cd|chdir|sl)\b", Opts)),
            new(RiskLevel.Medium, false, "New-Item",
                new Regex(@"\bNew-Item\b", Opts)),
            new(RiskLevel.Medium, false, "Out-File / Set-Content / Add-Content / Tee-Object",
                new Regex(@"\b(Out-File|Set-Content|Add-Content|Tee-Object)\b", Opts)),
            new(RiskLevel.Medium, false, "Redirection to file (>, >>)",
                new Regex(@"\s>{1,2}\s*['""]?[^|;]+\.[A-Za-z0-9]{1,8}\b", Opts)),
            new(RiskLevel.Medium, false, "Stop-Process / kill",
                new Regex(@"\b(Stop-Process|kill|spps)\b", Opts)),
            new(RiskLevel.Medium, false, "Install-Module / Update-Module",
                new Regex(@"\b(Install-Module|Update-Module|Install-Script|Save-Module)\b", Opts)),
            new(RiskLevel.Medium, false, "winget / choco install / npm install / pip install",
                new Regex(@"\b(winget|choco|npm|pip|yarn|pnpm|gem|cargo)\s+(install|add|i)\b", Opts)),
            new(RiskLevel.Medium, false, "git push / git reset --hard / git clean",
                new Regex(@"\bgit\s+(push|reset\s+--hard|clean\s+-\w*[fd]\w*)\b", Opts)),
        };

        return rules.ToArray();
    }
}
