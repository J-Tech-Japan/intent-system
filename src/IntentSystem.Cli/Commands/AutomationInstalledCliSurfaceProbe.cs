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
        var hasSurface = probe.ExitCode == 0
            && surface.ExpectedTokens.All(token => output.Contains(token, StringComparison.Ordinal));
        if (hasSurface)
        {
            return new InstalledCliSurfaceCheck(
                surface.Command,
                surface.Transition,
                true,
                null);
        }

        var reason = output.Contains("not yet implemented", StringComparison.OrdinalIgnoreCase)
            ? "installed CLI reports the command surface is not yet implemented"
            : $"installed CLI help did not expose required surface tokens: {string.Join(", ", surface.ExpectedTokens)}";

        return Missing(surface, reason);
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

    private static readonly IReadOnlyList<RequiredSurface> RequiredSurfaces =
    [
        new(
            "intent-cli automation summary",
            null,
            ["automation", "summary", "--help"],
            ["automation summary"]),
        new(
            "intent-cli automation host-review-preflight",
            null,
            ["automation", "host-review-preflight", "--help"],
            ["automation host-review-preflight"]),
        new(
            "intent-cli automation issue-publish",
            null,
            ["automation", "issue-publish", "--help"],
            ["automation issue-publish"]),
        new(
            "intent-cli automation pr-transition",
            "review-start",
            ["automation", "pr-transition", "--help"],
            ["automation pr-transition", "review-start"]),
        new(
            "intent-cli automation pr-transition",
            "request-update",
            ["automation", "pr-transition", "--help"],
            ["automation pr-transition", "request-update"]),
        new(
            "intent-cli automation pr-transition",
            "approved",
            ["automation", "pr-transition", "--help"],
            ["automation pr-transition", "approved"]),
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
