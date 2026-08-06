using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using IntentSystem.Cli;

namespace IntentSystem.Cli.Commands;

internal static class AutomationInstalledCliSurfaceProbe
{
    /// <summary>G282: env var operators can use to pin a specific intent-cli
    /// binary (e.g., for version-specific testing) without relying on PATH or
    /// the cwd-local shim.</summary>
    public const string ExplicitInstalledCliPathEnv = "INTENT_CLI_INSTALLED_PATH";

    /// <summary>G282: stable identifiers for how the probe resolved the binary;
    /// surfaced in <see cref="InstalledCliSurfaceReport.BinarySource"/> so the
    /// doctor output can tell global-tool installs apart from cwd-local shims.
    /// </summary>
    public static class BinarySources
    {
        public const string ExplicitOverride = "explicit-override";
        public const string CwdLocalShim = "cwd-local-shim";
        public const string PathGlobalTool = "path-global-tool";
        public const string Missing = "missing";
    }

    public static Func<string, IReadOnlyList<string>, InstalledCliProbeResult>? ProbeRunner
    {
        get => probeRunner.Value;
        set => probeRunner.Value = value;
    }

    /// <summary>G282: test seam for PATH lookup so unit tests can simulate a
    /// global dotnet tool install without writing to the system PATH.
    /// AsyncLocal so xUnit's parallel test runners do not race on the seam.
    /// </summary>
    public static Func<string, string?>? PathResolver
    {
        get => pathResolver.Value;
        set => pathResolver.Value = value;
    }

    /// <summary>G282: test seam for the explicit-override env var. xUnit runs
    /// tests in parallel and process-global env vars race; tests inject a
    /// reader here to exercise the override path without writing to the
    /// process environment. AsyncLocal so the seam is isolated per test
    /// execution context.</summary>
    public static Func<string?>? ExplicitInstalledCliPathReader
    {
        get => explicitInstalledCliPathReader.Value;
        set => explicitInstalledCliPathReader.Value = value;
    }

    private static readonly AsyncLocal<Func<string, IReadOnlyList<string>, InstalledCliProbeResult>?> probeRunner = new();
    private static readonly AsyncLocal<Func<string, string?>?> pathResolver = new();
    private static readonly AsyncLocal<Func<string?>?> explicitInstalledCliPathReader = new();

    public static InstalledCliSurfaceReport Check(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolution = ResolveInstalledCliPath(context.RepoRoot);
        var checks = RequiredSurfaces
            .Select(surface => CheckSurface(resolution.Path, surface))
            .ToArray();

        return new InstalledCliSurfaceReport(
            resolution.Path,
            checks.All(check => check.Available),
            checks,
            resolution.Source,
            HostDataRootResolver.Resolve(context.RepoRoot));
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
            or IOException
            // G443: a persistent process-start failure (e.g. ETXTBSY that
            // survives the bounded retry in RunInstalledCli) degrades the
            // surface to "missing" rather than crashing the whole command
            // (which previously surfaced as a hard exit 1 in release CI).
            or Win32Exception)
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

    /// <summary>
    /// G443: errno for <c>ETXTBSY</c> ("Text file busy") on Linux. The probe
    /// execs an installed binary that may have just been written/copied into
    /// place (e.g. a freshly published global tool, or a cwd-local shim a test
    /// harness wrote a moment earlier). On Linux, exec races against a writer
    /// still holding the inode — including a sibling <see cref="Process.Start"/>
    /// in the same process that transiently inherits the writable fd under
    /// parallel CI — and surfaces as <see cref="Win32Exception"/> with this
    /// native error code. Retrying briefly clears the race deterministically.
    /// </summary>
    private const int TextFileBusyNativeErrorCode = 26;

    private const int InstalledCliStartMaxAttempts = 12;

    private const int InstalledCliStartRetryDelayMilliseconds = 25;

    private static InstalledCliProbeResult RunInstalledCli(string installedCliPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installedCliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
        };
        startInfo.Environment["INTENT_CLI_SURFACE_PROBE"] = "1";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = StartWithTextFileBusyRetry(startInfo, installedCliPath);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new InstalledCliProbeResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// G443: start the installed-CLI probe process, retrying on the
    /// <c>ETXTBSY</c> ("Text file busy") exec race that otherwise made the
    /// release CI test job flaky on Linux runners. Non-ETXTBSY failures and a
    /// persistent ETXTBSY after the bounded retries propagate unchanged.
    /// </summary>
    private static Process StartWithTextFileBusyRetry(ProcessStartInfo startInfo, string installedCliPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"failed to start installed intent-cli at {installedCliPath}");
            }
            catch (Win32Exception exception)
                when (exception.NativeErrorCode == TextFileBusyNativeErrorCode
                    && attempt < InstalledCliStartMaxAttempts)
            {
                // The binary is momentarily busy (still being written/copied or
                // held by a sibling exec). Back off briefly and retry.
                Thread.Sleep(InstalledCliStartRetryDelayMilliseconds);
            }
        }
    }

    private static InstalledCliResolution ResolveInstalledCliPath(string repoRoot)
    {
        var executableName = OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli";
        var cwdLocal = Path.Combine(repoRoot, CliRuntimeContracts.IntentCliDirectoryName, "bin", executableName);

        // G282 explicit override wins. Lets operators pin a version-specific
        // binary for one-off tests without rewriting PATH or the cwd shim.
        var explicitOverride = (ExplicitInstalledCliPathReader ?? DefaultExplicitInstalledCliPathReader)();
        if (!string.IsNullOrWhiteSpace(explicitOverride) && File.Exists(explicitOverride))
        {
            return new InstalledCliResolution(explicitOverride, BinarySources.ExplicitOverride);
        }

        // Cwd-local shim, when present, is preferred (legacy default — pins the
        // exact binary used by automation in this checkout).
        if (File.Exists(cwdLocal))
        {
            return new InstalledCliResolution(cwdLocal, BinarySources.CwdLocalShim);
        }

        // G282: fall back to PATH (global dotnet tool install at e.g.
        // $HOME/.dotnet/tools/intent-cli) so the doctor does not report
        // stale-host-cli solely because the cwd-local shim is absent.
        var pathResolved = (PathResolver ?? ResolveOnPath)(executableName);
        if (!string.IsNullOrWhiteSpace(pathResolved) && File.Exists(pathResolved))
        {
            return new InstalledCliResolution(pathResolved, BinarySources.PathGlobalTool);
        }

        // Nothing found: report the canonical cwd-local path as the expected
        // location and mark source as missing so the doctor stays accurate.
        return new InstalledCliResolution(cwdLocal, BinarySources.Missing);
    }

    private static string? DefaultExplicitInstalledCliPathReader() =>
        Environment.GetEnvironmentVariable(ExplicitInstalledCliPathEnv);

    private static string? ResolveOnPath(string executableName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private sealed record InstalledCliResolution(string Path, string Source);

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
    IReadOnlyList<InstalledCliSurfaceCheck> Checks,
    string BinarySource,
    string HostDataRoot);

internal static class HostDataRootResolver
{
    public static string Resolve(string repoRoot) =>
        Path.Combine(repoRoot, CliRuntimeContracts.IntentCliDirectoryName);
}

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
