using System.Diagnostics;
using System.Text.Json.Serialization;
using IntentSystem.Cli;

namespace IntentSystem.Cli.Commands;

internal static class AutomationInstalledCliSurfaceProbe
{
    public static Func<string, IReadOnlyList<string>, InstalledCliProbeResult>? ProbeRunner { get; set; }

    public static InstalledCliSurfaceReport Check(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var installedCliPath = ResolveInstalledCliPath(context.RepoRoot);
        var checks = RequiredSurfaces
            .Select(surface => CheckSurface(installedCliPath, surface))
            .ToArray();

        return new InstalledCliSurfaceReport(
            installedCliPath,
            checks.All(check => check.Available),
            checks);
    }

    private static InstalledCliSurfaceCheck CheckSurface(string installedCliPath, RequiredSurface surface)
    {
        if (!File.Exists(installedCliPath))
        {
            return Missing(surface, "installed intent-cli was not found");
        }

        InstalledCliProbeResult probe;
        try
        {
            probe = ProbeRunner?.Invoke(installedCliPath, surface.Arguments)
                ?? RunInstalledCli(installedCliPath, surface.Arguments);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            return Missing(surface, exception.Message);
        }

        var output = $"{probe.Stdout}\n{probe.Stderr}";

        // A surface is absent only when the router explicitly reports it is not implemented.
        // Accept non-zero exit codes — probes intentionally omit required args so the command
        // reaches intent-cli but fails validation, avoiding --help interception by wrapper layers
        // such as dotnet tool exec.
        if (output.Contains("not yet implemented", StringComparison.OrdinalIgnoreCase))
        {
            return Missing(surface, "installed CLI reports the command surface is not yet implemented");
        }

        if (surface.ExpectedTokens.Count > 0)
        {
            var missing = surface.ExpectedTokens
                .Where(t => !output.Contains(t, StringComparison.Ordinal))
                .ToArray();
            if (missing.Length > 0)
            {
                return Missing(surface,
                    $"installed CLI output did not include required capability tokens: {string.Join(", ", missing)}");
            }
        }

        return new InstalledCliSurfaceCheck(surface.Command, surface.Transition, true, null);
    }

    private static InstalledCliSurfaceCheck Missing(RequiredSurface surface, string reason) =>
        new(
            surface.Command,
            surface.Transition,
            false,
            reason);

    private static InstalledCliProbeResult RunInstalledCli(string installedCliPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installedCliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["INTENT_CLI_SURFACE_PROBE"] = "1";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start installed intent-cli at {installedCliPath}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new InstalledCliProbeResult(process.ExitCode, stdout, stderr);
    }

    private static string ResolveInstalledCliPath(string repoRoot)
    {
        var executableName = OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli";
        return Path.Combine(repoRoot, CliRuntimeContracts.IntentCliDirectoryName, "bin", executableName);
    }

    // Probes intentionally omit --help to avoid wrapper-layer interception (e.g., dotnet tool exec
    // consuming --help before intent-cli sees it). Each probe runs the command with no required args
    // so the router dispatches to intent-cli, which returns a usage/validation error instead of the
    // wrapper's own help. Absence is detected by "not yet implemented" in the output.
    private static readonly IReadOnlyList<RequiredSurface> RequiredSurfaces =
    [
        new(
            "intent-cli automation summary",
            null,
            ["automation", "summary"],
            []),
        new(
            "intent-cli automation host-review-preflight",
            null,
            ["automation", "host-review-preflight"],
            []),
        new(
            "intent-cli automation issue-publish",
            null,
            ["automation", "issue-publish"],
            []),
        new(
            "intent-cli automation pr-transition",
            "review-start",
            ["automation", "pr-transition"],
            ["review-start"]),
        new(
            "intent-cli automation pr-transition",
            "request-update",
            ["automation", "pr-transition"],
            ["request-update"]),
        new(
            "intent-cli automation pr-transition",
            "approved",
            ["automation", "pr-transition"],
            ["approved"]),
    ];

    private sealed record RequiredSurface(
        string Command,
        string? Transition,
        IReadOnlyList<string> Arguments,
        IReadOnlyList<string> ExpectedTokens);
}

internal sealed record InstalledCliSurfaceReport(
    string InstalledCliPath,
    bool Available,
    IReadOnlyList<InstalledCliSurfaceCheck> Checks);

internal sealed record InstalledCliSurfaceCheck(
    [property: JsonPropertyName("command")]
    string Command,
    [property: JsonPropertyName("transition")]
    string? Transition,
    [property: JsonPropertyName("available")]
    bool Available,
    [property: JsonPropertyName("reason")]
    string? Reason);

internal sealed record InstalledCliProbeResult(
    int ExitCode,
    string Stdout,
    string Stderr);
