using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Emits an operator-owned scheduler definition for the measured supervisor.
/// This command deliberately has no process-runner dependency: registration,
/// unregistration, and process lifecycle remain explicit operator actions.
/// </summary>
internal static class NotifySuperviseInstallCommand
{
    public const string Operation = "install";
    public const string Usage =
        "Usage: intent-cli notify supervise install --domain <d> --team <t> --repo <owner/repo> "
        + "--owner-role <role> --bound <seconds> --interval <seconds> "
        + "[--event-mode] [--platform macos|windows|linux] [--output <path>] [--routing-root <host-root>] "
        + "[--dry-run|--write] [--format markdown|json]";

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

        string routingRoot;
        string artifactPath;
        try
        {
            routingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
            artifactPath = options.Output is null
                ? ResolveDefaultArtifactPath(context, options)
                : Path.GetFullPath(options.Output, context.RepoRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            EmitFailure(writer, options.Format, $"Could not resolve the routing root or artifact path: {exception.Message}");
            return 1;
        }

        var label = $"intent-cli.supervise.{options.Domain}.{options.Team}";
        var superviseArguments = BuildSuperviseArguments(options, routingRoot);
        var invocation = FormatShellInvocation("intent-cli", superviseArguments);
        var artifact = BuildArtifact(options.Platform, label, superviseArguments);
        var (registrationCommand, unregistrationCommand) = BuildOperatorCommands(options.Platform, label, artifactPath);
        var crossAuthored = !string.Equals(options.Platform, CurrentPlatform(), StringComparison.Ordinal);
        var verificationStatus = options.Platform is Windows or Linux
            ? "emitted-but-unverified"
            : "emission-verified-on-macos";

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
                File.WriteAllText(artifactPath, artifact, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EmitFailure(writer, options.Format, $"Could not write the scheduler artifact: {exception.Message}");
                return 1;
            }
        }

        var result = new InstallResult
        {
            Platform = options.Platform,
            CurrentPlatform = CurrentPlatform(),
            CrossAuthored = crossAuthored,
            Label = label,
            ArtifactPath = artifactPath,
            ArtifactWritten = options.Write,
            VerificationStatus = verificationStatus,
            EventMode = options.EventMode,
            SuperviseInvocation = invocation,
            RegistrationCommand = registrationCommand,
            UnregistrationCommand = unregistrationCommand,
            CommandMode = options.Write ? "write" : "dry-run",
            ManagesProcess = false,
            Summary = options.Write
                ? $"Emitted the {options.Platform} supervisor artifact with event mode {(options.EventMode ? "enabled" : "disabled")}. intent-cli did not register, start, stop, or unregister it."
                : $"Previewed the {options.Platform} supervisor artifact path and operator commands without writing or executing anything.",
        };
        Emit(writer, options.Format, result);
        return 0;
    }

    private static string ResolveDefaultArtifactPath(CliContext context, InstallOptions options)
    {
        var label = $"intent-cli.supervise.{options.Domain}.{options.Team}";
        var extension = options.Platform switch
        {
            MacOs => ".plist",
            Windows => ".xml",
            _ => ".service",
        };
        return Path.Combine(
            context.ResolveSupervisionArtifactRootPath(),
            options.Domain,
            options.Team,
            "install",
            label + extension);
    }

    private static IReadOnlyList<string> BuildSuperviseArguments(InstallOptions options, string routingRoot)
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
        arguments.AddRange(["--routing-root", routingRoot, "--write", "--format", "json"]);
        return arguments;
    }

    private static string BuildArtifact(string platform, string label, IReadOnlyList<string> arguments) => platform switch
    {
        MacOs => BuildLaunchdArtifact(label, arguments),
        Windows => BuildTaskSchedulerArtifact(label, arguments),
        _ => BuildSystemdArtifact(label, arguments),
    };

    private static string BuildLaunchdArtifact(string label, IReadOnlyList<string> arguments)
    {
        var programArguments = new StringBuilder()
            .AppendLine("    <string>/usr/bin/env</string>")
            .AppendLine("    <string>intent-cli</string>");
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
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <true/>
  <key>ThrottleInterval</key>
  <integer>30</integer>
</dict>
</plist>
""";
    }

    private static string BuildTaskSchedulerArtifact(string label, IReadOnlyList<string> arguments)
    {
        var commandArguments = string.Join(" ", arguments.Select(QuoteWindowsArgument));
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>{XmlEscape(label)} — emitted-but-unverified on macOS; operator-managed intent-cli supervision.</Description>
    <URI>\{XmlEscape(label)}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RestartOnFailure><Interval>PT1M</Interval><Count>999</Count></RestartOnFailure>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec><Command>intent-cli</Command><Arguments>{XmlEscape(commandArguments)}</Arguments></Exec>
  </Actions>
</Task>
""";
    }

    private static string BuildSystemdArtifact(string label, IReadOnlyList<string> arguments)
    {
        var commandArguments = string.Join(" ", arguments.Select(QuoteSystemdArgument));
        return $"""
# {label}
# verification-status: emitted-but-unverified on macOS
[Unit]
Description={label} operator-managed intent-cli supervision

[Service]
Type=simple
ExecStart=/usr/bin/env intent-cli {commandArguments}
Restart=always
RestartSec=30

[Install]
WantedBy=default.target
""";
    }

    private static (string Registration, string Unregistration) BuildOperatorCommands(
        string platform,
        string label,
        string artifactPath) => platform switch
    {
        MacOs =>
        (
            $"launchctl load {QuoteShellArgument(artifactPath)}",
            $"launchctl unload {QuoteShellArgument(artifactPath)}"
        ),
        Windows =>
        (
            $"schtasks /Create /TN {QuoteWindowsArgument(label)} /XML {QuoteWindowsArgument(artifactPath)} /F",
            $"schtasks /Delete /TN {QuoteWindowsArgument(label)} /F"
        ),
        _ =>
        (
            $"systemctl --user link {QuoteShellArgument(artifactPath)} && systemctl --user enable --now {QuoteShellArgument(label + ".service")}",
            $"systemctl --user disable --now {QuoteShellArgument(label + ".service")}"
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
        int? bound = null;
        int? interval = null;
        var write = false;
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
                case "--bound":
                    if (!ReadPositiveInt(args, ref index, argument, 86_400, out bound, out error)) return Fail(out options);
                    break;
                case "--interval":
                    if (!ReadPositiveInt(args, ref index, argument, NotifySupervisor.MaximumIntervalSeconds, out interval, out error)) return Fail(out options);
                    break;
                case "--write": write = true; break;
                case "--dry-run": write = false; break;
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

        options = new InstallOptions
        {
            Domain = domain!, Team = team!, Repo = repo!, OwnerRole = ownerRole!,
            BoundSeconds = bound.Value, IntervalSeconds = interval.Value,
            RoutingRoot = routingRoot, Output = output, Platform = platform,
            Write = write, Format = format!,
            EventMode = eventMode,
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
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- event mode: {result.EventMode.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- artifact path: `{result.ArtifactPath}` (written: {result.ArtifactWritten.ToString().ToLowerInvariant()})");
        writer.WriteLine($"- supervise invocation: `{result.SuperviseInvocation}`");
        writer.WriteLine($"- registration command (operator action): `{result.RegistrationCommand}`");
        writer.WriteLine($"- unregistration command (operator action): `{result.UnregistrationCommand}`");
        writer.WriteLine("- process management executed by intent-cli: false");
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
        public required string Platform { get; init; }
        public string? RoutingRoot { get; init; }
        public string? Output { get; init; }
        public required bool Write { get; init; }
        public bool EventMode { get; init; }
        public required string Format { get; init; }
    }

    private sealed record InstallResult
    {
        [JsonPropertyName("operation")] public string Operation { get; init; } = "supervise-install";
        [JsonPropertyName("platform")] public required string Platform { get; init; }
        [JsonPropertyName("current_platform")] public required string CurrentPlatform { get; init; }
        [JsonPropertyName("cross_authored")] public required bool CrossAuthored { get; init; }
        [JsonPropertyName("label")] public required string Label { get; init; }
        [JsonPropertyName("artifact_path")] public required string ArtifactPath { get; init; }
        [JsonPropertyName("artifact_written")] public required bool ArtifactWritten { get; init; }
        [JsonPropertyName("verification_status")] public required string VerificationStatus { get; init; }
        [JsonPropertyName("event_mode")] public required bool EventMode { get; init; }
        [JsonPropertyName("supervise_invocation")] public required string SuperviseInvocation { get; init; }
        [JsonPropertyName("registration_command")] public required string RegistrationCommand { get; init; }
        [JsonPropertyName("unregistration_command")] public required string UnregistrationCommand { get; init; }
        [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
        [JsonPropertyName("manages_process")] public required bool ManagesProcess { get; init; }
        [JsonPropertyName("summary")] public required string Summary { get; init; }
    }
}
