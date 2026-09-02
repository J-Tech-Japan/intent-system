using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Emits an operator-owned scheduler definition for the measured supervisor.
/// Installation authors an artifact and proves its first cycle; explicit
/// reconcile/uninstall owns lifecycle cleanup for the current GUI session.
/// </summary>
internal static class NotifySuperviseInstallCommand
{
    public const string Operation = "install";
    public const int DefaultStartupBoundSeconds = 120;
    public const int DefaultVerifyStartupBoundSeconds = 1;
    public const int MaximumStartupBoundSeconds = 3600;
    internal static Func<NotifySuperviseFirstCycleRequest, NotifySuperviseFirstCycleResult>? FirstCycleProbeFactory { get; set; }
    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } = () => DateTimeOffset.UtcNow;
    internal static Action<TimeSpan> Delay { get; set; } = Thread.Sleep;
    public const string Usage =
        "Usage: intent-cli notify supervise install --domain <d> --team <t> --repo <owner/repo> "
        + "--owner-role <role> --bound <seconds> --interval <seconds> "
        + "[--startup-bound <seconds>; default 120 for --write, 1 for --verify] "
        + "[--persistence persistent] "
        + "[--event-mode] [--platform macos|windows|linux] [--output <path>] [--routing-root <host-root>] "
        + "[--verify|--dry-run|--write] [--format markdown|json]";

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string MacOs = "macos";
    private const string Windows = "windows";
    private const string Linux = "linux";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var error))
        {
            writer.WriteLine($"invalid-supervision-install: {error}");
            writer.WriteLine(Usage);
            return 1;
        }

        if (options.BoundSeconds < options.IntervalSeconds)
        {
            EmitFailure(
                writer,
                options.Format,
                $"bound-below-interval: declared bound {options.BoundSeconds}s is smaller than interval {options.IntervalSeconds}s; a healthy supervisor is structurally judged absent (supervisor-not-running). Runtime warning remains enabled for legacy records.");
            return 1;
        }

        string routingRoot;
        string artifactRoot;
        string artifactPath;
        try
        {
            routingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
            artifactRoot = options.RoutingRoot is null
                ? context.ResolveSupervisionArtifactRootPath()
                : NotifySuperviseLivenessCommand.ResolveSupervisionArtifactRootPath(context, routingRoot);
            artifactPath = options.Output is null
                ? ResolveDefaultArtifactPath(artifactRoot, options)
                : Path.GetFullPath(options.Output, context.RepoRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            EmitFailure(writer, options.Format, $"Could not resolve the routing root or artifact path: {exception.Message}");
            return 1;
        }

        var lifetimePathError = ValidateSessionArtifactPath(
            options.Platform,
            artifactPath,
            NotifySuperviseArtifactInventory.UserProfileDirectoryFactory());
        if (lifetimePathError is not null)
        {
            EmitFailure(writer, options.Format, lifetimePathError);
            return 1;
        }

        try
        {
            if (TeamModeStore.Resolve(routingRoot, options.Domain, options.Team).IsAuthoringOnly)
            {
                EmitFailure(
                    writer,
                    options.Format,
                    "not-applicable-team-mode: authoring-only teams have no supervision process or scheduler artifact.");
                return 1;
            }
        }
        catch (InvalidOperationException exception)
        {
            EmitFailure(writer, options.Format, $"team-mode-unreadable: {exception.Message}");
            return 1;
        }

        var label = $"intent-cli.supervise.{options.Domain}.{options.Team}";
        var supervisionDirectory = Path.Combine(
            artifactRoot,
            options.Domain,
            options.Team);
        var runtimeDirectory = Path.Combine(supervisionDirectory, "runtime");
        var standardOutPath = Path.Combine(runtimeDirectory, label + ".stdout.log");
        var standardErrorPath = Path.Combine(runtimeDirectory, label + ".stderr.log");
        var runtime = ResolveRuntime(options, routingRoot);
        // Scheduler emission remains usable when the invoking process is a
        // dotnet/dnx tool host and intent-cli is not itself PATH-visible. The
        // unresolved runtime record names the gap while the long-standing
        // bare command remains the operator-resolved artifact entrypoint.
        var intentCliExecutable = runtime.IntentCli.Path ?? runtime.IntentCli.Name;
        var superviseArguments = BuildSuperviseArguments(options, routingRoot, runtime);
        var invocation = FormatShellInvocation(intentCliExecutable, superviseArguments);
        var artifact = BuildArtifact(
            options.Platform,
            label,
            intentCliExecutable,
            superviseArguments,
            runtime.RecordedPath,
            routingRoot,
            standardOutPath,
            standardErrorPath,
            options.PersistenceIntent);
        var (registrationCommand, unregistrationCommand) = BuildOperatorCommands(options.Platform, label, artifactPath);
        var crossAuthored = !string.Equals(options.Platform, CurrentPlatform(), StringComparison.Ordinal);
        var unresolvedBinaries = runtime.Binaries
            .Where(binary => !binary.Resolved)
            .Select(binary => binary.Name)
            .ToArray();
        var legacyArtifactsRemoved = Array.Empty<string>();
        if (options.Write)
        {
            legacyArtifactsRemoved = NotifySuperviseArtifactInventory.RemoveLegacyArtifacts(
                label,
                NotifySuperviseArtifactInventory.UserProfileDirectoryFactory(),
                out var legacyCleanupError).ToArray();
            if (legacyCleanupError is not null)
            {
                EmitFailure(writer, options.Format, $"legacy-artifact-cleanup-failed: {legacyCleanupError}");
                return 1;
            }
        }
        NotifySuperviseFirstCycleResult? firstCycle = null;
        DateTimeOffset? artifactWrittenAt = null;
        IReadOnlyList<NotifySuperviseRuntimeLogSnapshot> runtimeLogs = Array.Empty<NotifySuperviseRuntimeLogSnapshot>();

        if (options.Write)
        {
            try
            {
                var directory = Path.GetDirectoryName(artifactPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    EmitFailure(writer, options.Format, "The artifact path must have a parent directory.");
                    return 1;
                }

                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(runtimeDirectory);
                artifactWrittenAt = UtcNowFactory().ToUniversalTime();
                File.WriteAllText(artifactPath, artifact, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.SetLastWriteTimeUtc(artifactPath, artifactWrittenAt.Value.UtcDateTime);
                artifactWrittenAt = ReadArtifactWrittenAt(artifactPath);
                runtimeLogs = CaptureRuntimeLogs([standardOutPath, standardErrorPath]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EmitFailure(writer, options.Format, $"Could not write the scheduler artifact: {exception.Message}");
                return 1;
            }
        }
        else if (options.Verify)
        {
            if (!File.Exists(artifactPath))
            {
                EmitFailure(
                    writer,
                    options.Format,
                    $"verify-artifact-not-found: no existing supervisor artifact was found at '{artifactPath}'; run install --write to author it before using --verify.");
                return 1;
            }

            try
            {
                artifactWrittenAt = ReadArtifactWrittenAt(artifactPath);
                runtimeLogs = CaptureRuntimeLogsForVerify(
                    [standardOutPath, standardErrorPath],
                    artifactWrittenAt.Value);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EmitFailure(writer, options.Format, $"Could not inspect the existing scheduler artifact: {exception.Message}");
                return 1;
            }
        }

        if (options.Write || options.Verify)
        {
            // A bare verify deliberately remains a one-read re-proof against
            // the artifact timestamp. An explicitly supplied verify bound,
            // however, opens a new proof window from this verification.
            var verificationStartedAt = options.Verify && options.HasExplicitStartupBound
                ? UtcNowFactory().ToUniversalTime()
                : artifactWrittenAt!.Value;
            firstCycle = (FirstCycleProbeFactory ?? NotifySuperviseFirstCycleProbe.Wait)(new NotifySuperviseFirstCycleRequest
            {
                ArtifactRoot = artifactRoot,
                Domain = options.Domain,
                Team = options.Team,
                ArtifactPath = artifactPath,
                CyclePath = NotifySupervisionStore.ResolveCyclePath(
                    artifactRoot,
                    options.Domain,
                    options.Team),
                StartupBoundSeconds = options.StartupBoundSeconds,
                ArtifactWrittenAt = artifactWrittenAt!.Value,
                VerificationStartedAt = verificationStartedAt,
                RuntimeLogs = runtimeLogs,
            });
            if (!firstCycle.Verified)
            {
                if (firstCycle.FailureKind is not null)
                {
                    EmitFirstCycleFailure(
                        writer,
                        options.Format,
                        artifactPath,
                        registrationCommand,
                        options.StartupBoundSeconds,
                        firstCycle);
                    return 1;
                }

                // Test-owned probe factories from prior contracts supply a
                // result only, not runtime-log observations. Keep their
                // established diagnostic shape while the real probe supplies
                // the G781 machine-readable distinction below.
                EmitFailure(
                    writer,
                    options.Format,
                    $"first-cycle-proof-failed: managed supervisor did not write a first cycle within {options.StartupBoundSeconds}s; inspect stdout log '{standardOutPath}' and stderr log '{standardErrorPath}'. {firstCycle.FailureReason}");
                return 1;
            }

            if (firstCycle.Writer is null)
            {
                EmitFirstCycleFailure(
                    writer,
                    options.Format,
                    artifactPath,
                    registrationCommand,
                    options.StartupBoundSeconds,
                    firstCycle with
                    {
                        FailureKind = "post-install-process-missing-writer",
                        FailureReason = "The observed post-install cycle had no writer identity.",
                    });
                return 1;
            }

            var installedRecord = NotifySupervisionStore.RecordInstalledSupervisor(
                artifactRoot,
                new NotifySupervisionInstalledSupervisor
                {
                    Domain = options.Domain,
                    Team = options.Team,
                    Label = label,
                    ArtifactPath = artifactPath,
                    Writer = firstCycle.Writer,
                    StartupBoundSeconds = options.StartupBoundSeconds,
                    RecordedAt = firstCycle.ObservedAt ?? UtcNowFactory().ToUniversalTime(),
                },
                write: true);
            if (installedRecord.Error is not null)
            {
                EmitFailure(
                    writer,
                    options.Format,
                    $"first-cycle-proof-record-failed: first cycle was observed but the installed supervisor identity could not be recorded at '{installedRecord.Path}': {installedRecord.Error}. Inspect stdout log '{standardOutPath}' and stderr log '{standardErrorPath}'.");
                return 1;
            }
        }

        var verificationStatus = !options.Write && !options.Verify
            ? "preview-unverified"
            : firstCycle?.Verified == true
                ? "first-cycle-verified"
                : "not-applicable";

        var result = new InstallResult
        {
            Platform = options.Platform,
            CurrentPlatform = CurrentPlatform(),
            CrossAuthored = crossAuthored,
            Label = label,
            ArtifactPath = artifactPath,
            ArtifactWritten = options.Write,
            ArtifactWrittenAt = artifactWrittenAt,
            Lifetime = options.PersistenceIntent is null
                ? "current GUI session only; no LaunchAgents login auto-load and no reboot persistence"
                : "operator-declared persistent intent; registration remains an explicit operator action and intent-cli executes no OS lifecycle command",
            PersistenceIntent = options.PersistenceIntent,
            LegacyArtifactsRemoved = legacyArtifactsRemoved,
            VerificationStatus = verificationStatus,
            StartupBoundSeconds = options.StartupBoundSeconds,
            RuntimeDirectory = runtimeDirectory,
            StandardOutPath = standardOutPath,
            StandardErrorPath = standardErrorPath,
            FirstCycleStatus = firstCycle?.Status ?? "preview-unverified",
            FirstCycleId = firstCycle?.CycleId,
            FirstCycleWriter = firstCycle?.Writer,
            FirstCycleAttempts = firstCycle?.Attempts,
            InstalledSupervisorPath = NotifySupervisionStore.ResolveInstalledSupervisorPath(
                artifactRoot, options.Domain, options.Team),
            EventMode = options.EventMode,
            SuperviseInvocation = invocation,
            RegistrationCommand = registrationCommand,
            UnregistrationCommand = unregistrationCommand,
            ReconcileCommand = "intent-cli notify supervise reconcile --write",
            CommandMode = options.Verify ? "verify" : options.Write ? "write" : "dry-run",
            ManagesProcess = false,
            RecordedPath = runtime.RecordedPath,
            RuntimeBinaries = runtime.Binaries,
            UnresolvedBinaries = unresolvedBinaries,
            Summary = options.Write
                ? $"Emitted and first-cycle-verified the {options.Platform} supervisor artifact with event mode {(options.EventMode ? "enabled" : "disabled")} for {(options.PersistenceIntent is null ? "the current GUI session only; no LaunchAgents login auto-load or reboot persistence is configured" : "operator-declared persistent intent; the operator still owns explicit registration and intent-cli executes no OS lifecycle command")}. Runtime transport binaries are resolved absolutely when available; unresolved binaries: {(unresolvedBinaries.Length == 0 ? "none" : string.Join(", ", unresolvedBinaries))}; the recorded PATH covers any remaining command name. First-cycle evidence is recorded at '{NotifySupervisionStore.ResolveInstalledSupervisorPath(artifactRoot, options.Domain, options.Team)}'. Legacy login-persistent artifacts removed: {legacyArtifactsRemoved.Length}. Install emitted the artifact but did not execute lifecycle registration; use 'intent-cli notify supervise reconcile --write' for explicit unload/removal."
                : options.Verify
                    ? $"Re-proved the existing {options.Platform} supervisor artifact without rewriting it. First-cycle evidence is recorded at '{NotifySupervisionStore.ResolveInstalledSupervisorPath(artifactRoot, options.Domain, options.Team)}'. Install verification did not execute lifecycle registration; use 'intent-cli notify supervise reconcile --write' for explicit unload/removal."
                : $"Previewed the {options.Platform} supervisor artifact path and operator lifecycle commands without writing, probing, or executing anything. Lifetime is {(options.PersistenceIntent is null ? "current GUI session only; no LaunchAgents login auto-load or reboot persistence is configured" : "operator-declared persistent intent; registration remains an explicit operator action")}. Runtime transport binaries are resolved absolutely when available; unresolved binaries: {(unresolvedBinaries.Length == 0 ? "none" : string.Join(", ", unresolvedBinaries))}; the recorded PATH covers any remaining command name. A write requires bounded first-cycle proof and records failure log paths.",
        };
        Emit(writer, options.Format, result);
        return 0;
    }

    private static DateTimeOffset ReadArtifactWrittenAt(string artifactPath) =>
        new(File.GetLastWriteTimeUtc(artifactPath), TimeSpan.Zero);

    private static IReadOnlyList<NotifySuperviseRuntimeLogSnapshot> CaptureRuntimeLogs(
        IEnumerable<string> paths) =>
        paths.Select(path => CaptureRuntimeLog(path, artifactWrittenAt: null)).ToArray();

    private static IReadOnlyList<NotifySuperviseRuntimeLogSnapshot> CaptureRuntimeLogsForVerify(
        IEnumerable<string> paths,
        DateTimeOffset artifactWrittenAt) =>
        paths.Select(path => CaptureRuntimeLog(path, artifactWrittenAt)).ToArray();

    private static NotifySuperviseRuntimeLogSnapshot CaptureRuntimeLog(
        string path,
        DateTimeOffset? artifactWrittenAt)
    {
        if (!File.Exists(path))
        {
            return new NotifySuperviseRuntimeLogSnapshot
            {
                Path = path,
                ExistedAtBaseline = false,
                LengthAtBaseline = 0,
                LastWriteAtBaseline = null,
            };
        }

        var file = new FileInfo(path);
        var lastWriteAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        return new NotifySuperviseRuntimeLogSnapshot
        {
            Path = path,
            ExistedAtBaseline = artifactWrittenAt is null || lastWriteAt <= artifactWrittenAt.Value,
            LengthAtBaseline = file.Length,
            LastWriteAtBaseline = lastWriteAt,
        };
    }

    private static void EmitFirstCycleFailure(
        TextWriter writer,
        string format,
        string artifactPath,
        string registrationCommand,
        int startupBoundSeconds,
        NotifySuperviseFirstCycleResult firstCycle)
    {
        var failureKind = firstCycle.FailureKind
            ?? "post-install-process-missing-writer";
        var observedLogs = string.Equals(
            failureKind,
            "no-post-install-process",
            StringComparison.Ordinal)
            ? Array.Empty<string>()
            : firstCycle.RuntimeLogPaths;
        var reason = string.IsNullOrWhiteSpace(firstCycle.FailureReason)
            ? string.Empty
            : " " + firstCycle.FailureReason;
        var error = failureKind switch
        {
            "no-post-install-process" =>
                $"first-cycle-proof-failed: no supervisor process started under artifact '{artifactPath}' within {startupBoundSeconds}s. intent-cli does not load scheduler jobs. Run the registration command '{registrationCommand}', then use 'intent-cli notify supervise install --verify' to prove a late start (attempts: {firstCycle.Attempts}).{reason}",
            "post-install-process-wrote-no-cycle" =>
                $"first-cycle-proof-failed: a supervisor process ran and wrote no cycle within {startupBoundSeconds}s. Existing runtime logs: {FormatRuntimeLogPaths(observedLogs)}. Use 'intent-cli notify supervise install --verify' after the process writes a qualifying cycle (attempts: {firstCycle.Attempts}).{reason}",
            "post-install-process-missing-writer" =>
                $"first-cycle-proof-failed: a supervisor process wrote a post-install cycle without the required writer identity. Existing runtime logs: {FormatRuntimeLogPaths(observedLogs)}. Use 'intent-cli notify supervise install --verify' after the process writes a qualifying cycle with writer identity (attempts: {firstCycle.Attempts}).{reason}",
            "first-cycle-state-unreadable" =>
                $"first-cycle-proof-failed: the supervision state could not be read while proving artifact '{artifactPath}' (attempts: {firstCycle.Attempts}).{reason}",
            _ =>
                $"first-cycle-proof-failed: first-cycle proof failed with kind '{failureKind}' (attempts: {firstCycle.Attempts}).{reason}",
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                operation = "supervise-install",
                success = false,
                error,
                manages_process = false,
                first_cycle_failure_kind = failureKind,
                first_cycle_attempts = firstCycle.Attempts,
                runtime_logs_observed = observedLogs,
                artifact_path = artifactPath,
                registration_command = registrationCommand,
                verify_command = "intent-cli notify supervise install --verify",
            }, JsonOptions));
            return;
        }

        writer.WriteLine($"supervise-install-failed: [{failureKind}] {error}");
    }

    private static string FormatRuntimeLogPaths(IReadOnlyList<string> paths) =>
        paths.Count == 0
            ? "none"
            : string.Join(", ", paths.Select(path => $"'{path}'"));

    private static NotifySuperviseRuntimeResolution ResolveRuntime(
        InstallOptions options,
        string routingRoot)
    {
        var recordedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var binaries = new List<NotifySuperviseRuntimeBinary>
        {
            ResolveBinary("intent-cli"),
        };

        var herdrRequired = options.EventMode;
        if (!herdrRequired)
        {
            try
            {
                herdrRequired = SessionLayerModeStore.Resolve(routingRoot, options.Domain, options.Team).IsHerdrOnly;
            }
            catch (InvalidOperationException)
            {
                // Preserve the existing default routing behavior when a
                // session-layer record is unreadable; the running loop will
                // report that read failure through its normal surface.
            }
        }

        var transportName = herdrRequired ? "herdr" : "bash";
        var transportOverride = Environment.GetEnvironmentVariable(
            herdrRequired
                ? NotifyTransportPaths.HerdrExecutableEnvironmentVariable
                : NotifyTransportPaths.BashExecutableEnvironmentVariable);
        binaries.Add(ResolveBinary(transportName, transportOverride));
        return new NotifySuperviseRuntimeResolution(binaries, recordedPath);
    }

    private static NotifySuperviseRuntimeBinary ResolveBinary(string name, string? configured = null)
    {
        var requested = string.IsNullOrWhiteSpace(configured) ? name : configured;
        var path = NotifyTransportPaths.ResolveExecutable(requested);
        return path is not null
            ? new NotifySuperviseRuntimeBinary(name, path, true, null)
            : new NotifySuperviseRuntimeBinary(
                name,
                null,
                false,
                $"'{name}' was not found as an absolute executable through the emission environment PATH.");
    }

    private static string ResolveDefaultArtifactPath(string artifactRoot, InstallOptions options)
    {
        var label = $"intent-cli.supervise.{options.Domain}.{options.Team}";
        var extension = options.Platform switch
        {
            MacOs => ".plist",
            Windows => ".xml",
            _ => ".service",
        };
        return Path.Combine(
            artifactRoot,
            options.Domain,
            options.Team,
            "install",
            label + extension);
    }

    private static string? ValidateSessionArtifactPath(
        string platform,
        string artifactPath,
        string userProfileDirectory)
    {
        if (!string.Equals(platform, MacOs, StringComparison.Ordinal))
        {
            return null;
        }

        var launchAgentsRoot = Path.GetFullPath(
            Path.Combine(userProfileDirectory, "Library", "LaunchAgents"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(artifactPath);
        return candidate.StartsWith(launchAgentsRoot, StringComparison.OrdinalIgnoreCase)
            ? $"login-auto-loaded-path: refusing to emit a macOS supervisor artifact under '{launchAgentsRoot.TrimEnd(Path.DirectorySeparatorChar)}'; use the session-scoped default under .intent-cli/supervision and bootstrap the current GUI session explicitly."
            : null;
    }

    private static IReadOnlyList<string> BuildSuperviseArguments(
        InstallOptions options,
        string routingRoot,
        NotifySuperviseRuntimeResolution runtime)
    {
        var arguments = new List<string>
        {
            "notify", "supervise",
            "--domain", options.Domain,
            "--team", options.Team,
            "--repo", options.Repo,
            "--owner-role", options.OwnerRole,
            "--bound", options.BoundSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--interval", options.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (options.EventMode)
        {
            arguments.Add("--event-mode");
        }
        foreach (var binary in runtime.Binaries.Where(binary => (binary.Name is "herdr" or "bash") && binary.Resolved))
        {
            arguments.Add(binary.Name == "herdr" ? "--herdr-executable" : "--bash-executable");
            arguments.Add(binary.Path!);
        }
        arguments.AddRange(["--routing-root", routingRoot, "--write", "--format", "json"]);
        return arguments;
    }

    private static string BuildArtifact(
        string platform,
        string label,
        string intentCliExecutable,
        IReadOnlyList<string> arguments,
        string recordedPath,
        string routingRoot,
        string standardOutPath,
        string standardErrorPath,
        string? persistenceIntent)
    {
        var artifact = platform switch
        {
            MacOs => BuildLaunchdArtifact(
                label,
                intentCliExecutable,
                arguments,
                recordedPath,
                routingRoot,
                standardOutPath,
                standardErrorPath),
            Windows => BuildTaskSchedulerArtifact(label, intentCliExecutable, arguments, recordedPath),
            _ => BuildSystemdArtifact(label, intentCliExecutable, arguments, recordedPath),
        };

        if (persistenceIntent is null)
        {
            return artifact;
        }

        if (platform == Linux)
        {
            return "# " + NotifySuperviseArtifactInventory.PersistenceMarker + Environment.NewLine + artifact;
        }

        // Keep the XML declaration first so the Windows task and macOS plist
        // remain valid documents while carrying the same declaration marker.
        var firstLineBreak = artifact.IndexOf('\n');
        if (firstLineBreak < 0)
        {
            return "<!-- " + NotifySuperviseArtifactInventory.PersistenceMarker + " -->" + Environment.NewLine + artifact;
        }

        var lineEnding = firstLineBreak > 0 && artifact[firstLineBreak - 1] == '\r' ? "\r\n" : "\n";
        return artifact.Insert(
            firstLineBreak + 1,
            "<!-- " + NotifySuperviseArtifactInventory.PersistenceMarker + " -->" + lineEnding);
    }

    private static string BuildLaunchdArtifact(
        string label,
        string intentCliExecutable,
        IReadOnlyList<string> arguments,
        string recordedPath,
        string routingRoot,
        string standardOutPath,
        string standardErrorPath)
    {
        var programArguments = new StringBuilder()
            .Append("    <string>").Append(XmlEscape(intentCliExecutable)).AppendLine("</string>");
        foreach (var argument in arguments)
        {
            programArguments.Append("    <string>").Append(XmlEscape(argument)).AppendLine("</string>");
        }

        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>{XmlEscape(label)}</string>
  <key>ProgramArguments</key>
  <array>
{programArguments.ToString().TrimEnd()}
  </array>
  <key>KeepAlive</key>
  <true/>
  <key>ThrottleInterval</key>
  <integer>30</integer>
  <key>WorkingDirectory</key>
  <string>{XmlEscape(routingRoot)}</string>
  <key>StandardOutPath</key>
  <string>{XmlEscape(standardOutPath)}</string>
  <key>StandardErrorPath</key>
  <string>{XmlEscape(standardErrorPath)}</string>
  <key>EnvironmentVariables</key>
  <dict>
    <key>PATH</key>
    <string>{XmlEscape(recordedPath)}</string>
  </dict>
</dict>
</plist>
""";
    }

    private static string BuildTaskSchedulerArtifact(
        string label,
        string intentCliExecutable,
        IReadOnlyList<string> arguments,
        string recordedPath)
    {
        var commandArguments = string.Join(" ", arguments.Select(QuoteWindowsArgument));
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>{XmlEscape(label)} — emitted-but-unverified on macOS; operator-managed intent-cli supervision. Recorded PATH: {XmlEscape(recordedPath)}</Description>
    <URI>\{XmlEscape(label)}</URI>
  </RegistrationInfo>
  <Principals>
    <Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <RestartOnFailure><Interval>PT1M</Interval><Count>999</Count></RestartOnFailure>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec><Command>{XmlEscape(intentCliExecutable)}</Command><Arguments>{XmlEscape(commandArguments)}</Arguments></Exec>
  </Actions>
</Task>
""";
    }

    private static string BuildSystemdArtifact(
        string label,
        string intentCliExecutable,
        IReadOnlyList<string> arguments,
        string recordedPath)
    {
        var commandArguments = string.Join(" ", arguments.Select(QuoteSystemdArgument));
        return $"""
# {label}
# verification-status: emitted-but-unverified on macOS
[Unit]
Description={label} operator-managed intent-cli supervision

[Service]
Type=simple
Environment={QuoteSystemdArgument("PATH=" + recordedPath)}
ExecStart={QuoteSystemdArgument(intentCliExecutable)} {commandArguments}
Restart=always
RestartSec=30

""";
    }

    private static (string Registration, string Unregistration) BuildOperatorCommands(
        string platform,
        string label,
        string artifactPath) => platform switch
    {
        MacOs =>
        (
            $"launchctl bootstrap gui/$(id -u) {QuoteShellArgument(artifactPath)}",
            $"launchctl bootout gui/$(id -u)/{label}"
        ),
        Windows =>
        (
            $"schtasks /Create /TN {QuoteWindowsArgument(label)} /XML {QuoteWindowsArgument(artifactPath)} /F",
            $"schtasks /Delete /TN {QuoteWindowsArgument(label)} /F"
        ),
        _ =>
        (
            $"systemctl --user link {QuoteShellArgument(artifactPath)} && systemctl --user start {QuoteShellArgument(label + ".service")}",
            $"systemctl --user stop {QuoteShellArgument(label + ".service")} && systemctl --user unlink {QuoteShellArgument(artifactPath)}"
        ),
    };

    private static string FormatShellInvocation(string executable, IReadOnlyList<string> arguments) =>
        executable + " " + string.Join(" ", arguments.Select(QuoteShellArgument));

    private static string QuoteShellArgument(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string QuoteWindowsArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string QuoteSystemdArgument(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string XmlEscape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string CurrentPlatform()
    {
        if (OperatingSystem.IsMacOS()) return MacOs;
        if (OperatingSystem.IsWindows()) return Windows;
        return Linux;
    }

    private static bool TryParse(string[] args, out InstallOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        string? repo = null;
        string? ownerRole = null;
        string? routingRoot = null;
        string? output = null;
        string? platform = null;
        string? persistenceIntent = null;
        int? bound = null;
        int? interval = null;
        int? startupBound = null;
        var write = false;
        var dryRun = false;
        var verify = false;
        var eventMode = false;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain": if (!ReadValue(args, ref index, argument, out domain, out error)) return Fail(out options); break;
                case "--team": if (!ReadValue(args, ref index, argument, out team, out error)) return Fail(out options); break;
                case "--repo": if (!ReadValue(args, ref index, argument, out repo, out error)) return Fail(out options); break;
                case "--owner-role": if (!ReadValue(args, ref index, argument, out ownerRole, out error)) return Fail(out options); break;
                case "--routing-root": if (!ReadValue(args, ref index, argument, out routingRoot, out error)) return Fail(out options); break;
                case "--output": if (!ReadValue(args, ref index, argument, out output, out error)) return Fail(out options); break;
                case "--platform": if (!ReadValue(args, ref index, argument, out platform, out error)) return Fail(out options); break;
                case "--persistence":
                    if (!ReadValue(args, ref index, argument, out var persistence, out error)) return Fail(out options);
                    if (!string.Equals(persistence, "persistent", StringComparison.Ordinal))
                    {
                        error = "--persistence accepts only 'persistent'; omit it for the legacy session-only behavior.";
                        return Fail(out options);
                    }
                    persistenceIntent = "persistent";
                    break;
                case "--bound":
                    if (!ReadPositiveInt(args, ref index, argument, 86_400, out bound, out error)) return Fail(out options);
                    break;
                case "--interval":
                    if (!ReadPositiveInt(args, ref index, argument, NotifySupervisor.MaximumIntervalSeconds, out interval, out error)) return Fail(out options);
                    break;
                case "--startup-bound":
                    if (!ReadPositiveInt(args, ref index, argument, MaximumStartupBoundSeconds, out startupBound, out error)) return Fail(out options);
                    break;
                case "--write": write = true; break;
                case "--dry-run": dryRun = true; break;
                case "--verify": verify = true; break;
                case "--event-mode": eventMode = true; break;
                case "--format":
                    if (!ReadValue(args, ref index, argument, out format, out error)) return Fail(out options);
                    if (format is not FormatJson and not FormatMarkdown)
                    {
                        error = "--format must be markdown or json.";
                        return Fail(out options);
                    }
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return Fail(out options);
            }
        }

        platform ??= CurrentPlatform();
        if (!IsSafeIdentity(domain) || !IsSafeIdentity(team) || !IsSafeIdentity(ownerRole))
        {
            error = "--domain, --team, and --owner-role are required safe identity values.";
            return Fail(out options);
        }
        if (!NotifyEventWriter.TryValidateTeam(team!, out error)) return Fail(out options);
        if (!IsSafeRepository(repo))
        {
            error = "--repo is required and must be an owner/repo value without path syntax.";
            return Fail(out options);
        }
        if (bound is null || interval is null)
        {
            error = "--bound and --interval are required whole-number seconds values.";
            return Fail(out options);
        }
        if (platform is not MacOs and not Windows and not Linux)
        {
            error = "--platform must be macos, windows, or linux.";
            return Fail(out options);
        }
        if (verify && (write || dryRun))
        {
            error = "--verify is exclusive with --write and --dry-run; it re-proves an existing artifact without rewriting it.";
            return Fail(out options);
        }
        if (write && dryRun)
        {
            error = "--write and --dry-run are mutually exclusive.";
            return Fail(out options);
        }

        options = new InstallOptions
        {
            Domain = domain!,
            Team = team!,
            Repo = repo!,
            OwnerRole = ownerRole!,
            BoundSeconds = bound.Value,
            IntervalSeconds = interval.Value,
            StartupBoundSeconds = startupBound
                ?? (verify ? DefaultVerifyStartupBoundSeconds : DefaultStartupBoundSeconds),
            HasExplicitStartupBound = startupBound is not null,
            RoutingRoot = routingRoot,
            Output = output,
            Platform = platform,
            Write = write,
            Format = format!,
            Verify = verify,
            EventMode = eventMode,
            PersistenceIntent = persistenceIntent,
        };
        return true;
    }

    private static bool ReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }
        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static bool ReadPositiveInt(
        string[] args,
        ref int index,
        string option,
        int maximum,
        out int? value,
        out string error)
    {
        value = null;
        if (!ReadValue(args, ref index, option, out var raw, out error)) return false;
        if (!int.TryParse(raw, out var parsed) || parsed < 1 || parsed > maximum)
        {
            error = $"{option} must be between 1 and {maximum} seconds.";
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool IsSafeIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool IsSafeRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/', StringSplitOptions.None);
        return parts.Length == 2 && IsSafeIdentity(parts[0]) && IsSafeIdentity(parts[1]);
    }

    private static bool Fail(out InstallOptions options)
    {
        options = null!;
        return false;
    }

    private static void EmitFailure(TextWriter writer, string format, string error)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                operation = "supervise-install",
                success = false,
                error,
                manages_process = false,
            }, JsonOptions));
            return;
        }
        writer.WriteLine($"supervise-install-failed: {error}");
    }

    private static void Emit(TextWriter writer, string format, InstallResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify supervise install — {result.Label}");
        writer.WriteLine();
        writer.WriteLine($"- platform: {result.Platform} (current: {result.CurrentPlatform}; cross-authored: {result.CrossAuthored.ToString().ToLowerInvariant()})");
        writer.WriteLine($"- verification status: {result.VerificationStatus}");
        writer.WriteLine($"- startup bound: {result.StartupBoundSeconds}s; first-cycle status: {result.FirstCycleStatus}");
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- lifetime: {result.Lifetime}");
        writer.WriteLine($"- event mode: {result.EventMode.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- artifact path: `{result.ArtifactPath}` (written: {result.ArtifactWritten.ToString().ToLowerInvariant()})");
        if (result.ArtifactWrittenAt is not null)
        {
            writer.WriteLine($"- artifact written at: {result.ArtifactWrittenAt.Value:O}");
        }
        writer.WriteLine($"- supervise runtime directory: `{result.RuntimeDirectory}`");
        writer.WriteLine($"- stdout log path: `{result.StandardOutPath}`");
        writer.WriteLine($"- stderr log path: `{result.StandardErrorPath}`");
        writer.WriteLine($"- installed supervisor identity: `{result.InstalledSupervisorPath}`");
        if (result.FirstCycleId is not null)
        {
            writer.WriteLine($"- first cycle id: `{result.FirstCycleId}`");
        }
        if (result.FirstCycleAttempts is not null)
        {
            writer.WriteLine($"- first cycle proof attempts: {result.FirstCycleAttempts.Value}");
        }
        writer.WriteLine($"- supervise invocation: `{result.SuperviseInvocation}`");
        writer.WriteLine($"- recorded PATH: `{result.RecordedPath}`");
        writer.WriteLine($"- runtime binaries: {string.Join(", ", result.RuntimeBinaries.Select(binary => $"{binary.Name}={(binary.Path ?? "unresolved")}"))}");
        if (result.UnresolvedBinaries.Count > 0)
        {
            writer.WriteLine($"- unresolved binaries: {string.Join(", ", result.UnresolvedBinaries)}");
        }
        writer.WriteLine($"- registration command (operator action): `{result.RegistrationCommand}`");
        writer.WriteLine($"- unregistration command (operator action): `{result.UnregistrationCommand}`");
        writer.WriteLine($"- reconcile/removal command (operator action): `{result.ReconcileCommand}`");
        writer.WriteLine($"- legacy login-persistent artifacts removed: {string.Join(", ", result.LegacyArtifactsRemoved.DefaultIfEmpty("none"))}");
        writer.WriteLine("- install lifecycle command executed by intent-cli: false");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private sealed record InstallOptions
    {
        public required string Domain { get; init; }
        public required string Team { get; init; }
        public required string Repo { get; init; }
        public required string OwnerRole { get; init; }
        public required int BoundSeconds { get; init; }
        public required int IntervalSeconds { get; init; }
        public required int StartupBoundSeconds { get; init; }
        public required bool HasExplicitStartupBound { get; init; }
        public required string Platform { get; init; }
        public string? RoutingRoot { get; init; }
        public string? Output { get; init; }
        public required bool Write { get; init; }
        public required bool Verify { get; init; }
        public bool EventMode { get; init; }
        public string? PersistenceIntent { get; init; }
        public required string Format { get; init; }
    }

    private sealed record NotifySuperviseRuntimeResolution(
        IReadOnlyList<NotifySuperviseRuntimeBinary> Binaries,
        string RecordedPath)
    {
        public NotifySuperviseRuntimeBinary IntentCli =>
            Binaries.Single(binary => string.Equals(binary.Name, "intent-cli", StringComparison.Ordinal));
    }

    private sealed record NotifySuperviseRuntimeBinary(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("resolved")] bool Resolved,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record InstallResult
    {
        [JsonPropertyName("operation")] public string Operation { get; init; } = "supervise-install";
        [JsonPropertyName("platform")] public required string Platform { get; init; }
        [JsonPropertyName("current_platform")] public required string CurrentPlatform { get; init; }
        [JsonPropertyName("cross_authored")] public required bool CrossAuthored { get; init; }
        [JsonPropertyName("label")] public required string Label { get; init; }
        [JsonPropertyName("artifact_path")] public required string ArtifactPath { get; init; }
        [JsonPropertyName("artifact_written")] public required bool ArtifactWritten { get; init; }
        [JsonPropertyName("artifact_written_at")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ArtifactWrittenAt { get; init; }
        [JsonPropertyName("lifetime")] public required string Lifetime { get; init; }
        [JsonPropertyName("persistence_intent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PersistenceIntent { get; init; }
        [JsonPropertyName("legacy_artifacts_removed")] public required IReadOnlyList<string> LegacyArtifactsRemoved { get; init; }
        [JsonPropertyName("verification_status")] public required string VerificationStatus { get; init; }
        [JsonPropertyName("startup_bound_seconds")] public required int StartupBoundSeconds { get; init; }
        [JsonPropertyName("runtime_directory")] public required string RuntimeDirectory { get; init; }
        [JsonPropertyName("stdout_log_path")] public required string StandardOutPath { get; init; }
        [JsonPropertyName("stderr_log_path")] public required string StandardErrorPath { get; init; }
        [JsonPropertyName("first_cycle_status")] public required string FirstCycleStatus { get; init; }
        [JsonPropertyName("first_cycle_id")] public string? FirstCycleId { get; init; }
        [JsonPropertyName("first_cycle_writer")] public NotifySupervisionWriterIdentity? FirstCycleWriter { get; init; }
        [JsonPropertyName("first_cycle_attempts")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FirstCycleAttempts { get; init; }
        [JsonPropertyName("installed_supervisor_path")] public required string InstalledSupervisorPath { get; init; }
        [JsonPropertyName("event_mode")] public required bool EventMode { get; init; }
        [JsonPropertyName("supervise_invocation")] public required string SuperviseInvocation { get; init; }
        [JsonPropertyName("registration_command")] public required string RegistrationCommand { get; init; }
        [JsonPropertyName("unregistration_command")] public required string UnregistrationCommand { get; init; }
        [JsonPropertyName("reconcile_command")] public required string ReconcileCommand { get; init; }
        [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
        [JsonPropertyName("manages_process")] public required bool ManagesProcess { get; init; }
        [JsonPropertyName("recorded_path")] public required string RecordedPath { get; init; }
        [JsonPropertyName("runtime_binaries")] public required IReadOnlyList<NotifySuperviseRuntimeBinary> RuntimeBinaries { get; init; }
        [JsonPropertyName("unresolved_binaries")] public required IReadOnlyList<string> UnresolvedBinaries { get; init; }
        [JsonPropertyName("summary")] public required string Summary { get; init; }
    }
}

internal sealed record NotifySuperviseFirstCycleRequest
{
    public required string ArtifactRoot { get; init; }
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string ArtifactPath { get; init; }
    public required string CyclePath { get; init; }
    public required int StartupBoundSeconds { get; init; }
    public required DateTimeOffset ArtifactWrittenAt { get; init; }
    public required DateTimeOffset VerificationStartedAt { get; init; }
    public IReadOnlyList<NotifySuperviseRuntimeLogSnapshot> RuntimeLogs { get; init; } =
        Array.Empty<NotifySuperviseRuntimeLogSnapshot>();
}

internal sealed record NotifySuperviseRuntimeLogSnapshot
{
    public required string Path { get; init; }
    public required bool ExistedAtBaseline { get; init; }
    public required long LengthAtBaseline { get; init; }
    public DateTimeOffset? LastWriteAtBaseline { get; init; }
}

internal sealed record NotifySuperviseFirstCycleResult
{
    public required bool Verified { get; init; }
    public required string Status { get; init; }
    public string? CycleId { get; init; }
    public NotifySupervisionWriterIdentity? Writer { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public int Attempts { get; init; }
    public string? FailureReason { get; init; }
    public string? FailureKind { get; init; }
    public IReadOnlyList<string> RuntimeLogPaths { get; init; } = Array.Empty<string>();
}

internal static class NotifySuperviseFirstCycleProbe
{
    public static NotifySuperviseFirstCycleResult Wait(NotifySuperviseFirstCycleRequest request)
    {
        var deadline = request.VerificationStartedAt.AddSeconds(request.StartupBoundSeconds);
        var attempts = 0;
        NotifySupervisionCycle? lastCycle = null;
        while (true)
        {
            // Check before reading again. This keeps an N-second proof to at
            // most N full-store reads and prevents the final read from turning
            // the bounded wait into an N+1 busy-poll.
            if (attempts > 0
                && NotifySuperviseInstallCommand.UtcNowFactory().ToUniversalTime() >= deadline)
            {
                return Timeout(request, attempts, lastCycle);
            }

            attempts++;
            var state = NotifySupervisionStore.Read(request.ArtifactRoot, request.Domain, request.Team);
            if (!state.Resolved)
            {
                return new NotifySuperviseFirstCycleResult
                {
                    Verified = false,
                    Status = "first-cycle-state-unreadable",
                    Attempts = attempts,
                    FailureReason = state.Error,
                    FailureKind = "first-cycle-state-unreadable",
                };
            }

            var cycle = state.LastCycle;
            lastCycle = cycle;
            if (cycle is not null
                && cycle.CompletedAt > request.ArtifactWrittenAt
                && cycle.Writer is not null)
            {
                return new NotifySuperviseFirstCycleResult
                {
                    Verified = true,
                    Status = "first-cycle-verified",
                    CycleId = cycle.CycleId,
                    Writer = cycle.Writer,
                    ObservedAt = cycle.CompletedAt,
                    Attempts = attempts,
                };
            }

            var now = NotifySuperviseInstallCommand.UtcNowFactory().ToUniversalTime();
            if (now >= deadline)
            {
                return Timeout(request, attempts, cycle);
            }

            // G781: full-store reads are deliberately paced at one second.
            // The next loop checks the deadline before another read.
            NotifySuperviseInstallCommand.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static NotifySuperviseFirstCycleResult Timeout(
        NotifySuperviseFirstCycleRequest request,
        int attempts,
        NotifySupervisionCycle? cycle)
    {
        var runtime = ObserveRuntimeLogs(request.RuntimeLogs);
        if (cycle is not null
            && cycle.CompletedAt > request.ArtifactWrittenAt
            && cycle.Writer is null)
        {
            return new NotifySuperviseFirstCycleResult
            {
                Verified = false,
                Status = "first-cycle-timeout",
                Attempts = attempts,
                FailureKind = "post-install-process-missing-writer",
                RuntimeLogPaths = runtime.ExistingPaths,
                FailureReason = $"The latest post-install cycle '{cycle.CycleId}' completed at {cycle.CompletedAt:O} without a writer identity.",
            };
        }

        var failureKind = runtime.HasPostArtifactActivity
            ? "post-install-process-wrote-no-cycle"
            : "no-post-install-process";
        return new NotifySuperviseFirstCycleResult
        {
            Verified = false,
            Status = "first-cycle-timeout",
            Attempts = attempts,
            FailureKind = failureKind,
            RuntimeLogPaths = runtime.ExistingPaths,
            FailureReason = cycle is null
                ? $"No cycle was recorded at '{request.CyclePath}'."
                : $"The latest cycle '{cycle.CycleId}' completed at {cycle.CompletedAt:O} before the artifact's recorded written-at or without a post-install writer identity.",
        };
    }

    private static RuntimeLogActivity ObserveRuntimeLogs(
        IReadOnlyList<NotifySuperviseRuntimeLogSnapshot> snapshots)
    {
        var existingPaths = new List<string>();
        var hasPostArtifactActivity = false;
        foreach (var snapshot in snapshots)
        {
            if (!TryReadRuntimeLog(snapshot.Path, out var current))
            {
                continue;
            }

            existingPaths.Add(snapshot.Path);
            hasPostArtifactActivity |= !snapshot.ExistedAtBaseline
                || current.Length > snapshot.LengthAtBaseline
                || (snapshot.LastWriteAtBaseline is not null
                    && current.LastWriteAt > snapshot.LastWriteAtBaseline.Value);
        }

        return new RuntimeLogActivity(existingPaths, hasPostArtifactActivity);
    }

    private static bool TryReadRuntimeLog(
        string path,
        out (long Length, DateTimeOffset LastWriteAt) current)
    {
        current = default;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var file = new FileInfo(path);
            current = (file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record RuntimeLogActivity(
        IReadOnlyList<string> ExistingPaths,
        bool HasPostArtifactActivity);
}
