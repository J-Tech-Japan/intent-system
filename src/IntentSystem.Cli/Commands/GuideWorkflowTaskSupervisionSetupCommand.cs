using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G712 repair: the declared supervision-setup workflow task must be a real,
/// metadata-free guide entry. It renders the already-shipped session-scoped
/// supervision contract; it does not install, register, inspect, or reconcile
/// a supervisor itself.
/// </summary>
internal static class GuideWorkflowTaskSupervisionSetupCommand
{
    internal const string TaskName = "supervision-setup";
    internal const string ContractVersion = "g712-session-scoped-supervision/v1";
    internal const string UsageLine =
        "Usage: intent-cli guide workflow task supervision-setup [--format markdown|json]";

    internal static readonly IReadOnlyList<string> ContractStatements = new[]
    {
        SupervisionGuideText.InstallBoundRule,
        SupervisionGuideText.InstallArtifactRule,
        SupervisionGuideText.InstallEvidenceRule,
        SupervisionGuideText.SessionLifetimeRule,
        SupervisionGuideText.ShrinkRule,
    };

    internal static readonly IReadOnlyList<SupervisionSetupCommand> Commands = new[]
    {
        new SupervisionSetupCommand
        {
            Name = "install",
            Command = $"intent-cli notify supervise install --domain <domain> --team <team> --repo <owner/repo> --owner-role orchestration --bound 900 --interval 300 --startup-bound {NotifySuperviseInstallCommand.DefaultStartupBoundSeconds} --platform macos --write --format json",
            Purpose = "Emit the current-session artifact and require bounded first-cycle proof; install does not execute lifecycle registration.",
        },
        new SupervisionSetupCommand
        {
            Name = "register-current-gui-session",
            Command = "launchctl bootstrap gui/$(id -u) '<artifact-path>'",
            Purpose = "Explicit operator action for the current GUI session only; never a login-auto-loaded registration.",
        },
        new SupervisionSetupCommand
        {
            Name = "verify-first-cycle",
            Command = $"intent-cli notify supervise install --domain <domain> --team <team> --repo <owner/repo> --owner-role orchestration --bound 900 --interval 300 --startup-bound {NotifySuperviseInstallCommand.DefaultStartupBoundSeconds} --platform macos --verify --format json",
            Purpose = "After explicit registration, re-prove the existing artifact without rewriting it; a late qualifying cycle writes durable first-cycle evidence.",
        },
        new SupervisionSetupCommand
        {
            Name = "reconcile",
            Command = "intent-cli notify supervise reconcile --write --format json",
            Purpose = "Report loaded-before/after state, unload managed jobs, remove managed and legacy artifacts, and preserve unrelated jobs.",
        },
        new SupervisionSetupCommand
        {
            Name = "uninstall",
            Command = "intent-cli notify supervise uninstall --write --format json",
            Purpose = "Run the same explicit current-session drift-removal contract when the operator wants supervision removed.",
        },
        new SupervisionSetupCommand
        {
            Name = "shrink",
            Command = "intent-cli notify supervise shrink --domain <domain> --team <team> --write --format json",
            Purpose = "Compact existing stalls and cycles under the append lock, retain every record, resolve readable invariant evidence, and append the shrink audit.",
        },
    };

    internal static readonly IReadOnlyList<string> NegativeChecks = new[]
    {
        "This guide route must not read .intent-cli/config.toml, queue-state, packets, or intents; it is executable from a bare directory.",
        "The generated macOS artifact must omit RunAtLoad and must not be emitted under ~/Library/LaunchAgents.",
        "Install must not execute registration or start a process; reconcile/uninstall may unload only managed intent-cli.supervise.* jobs and never auto-kill or mutate unrelated jobs.",
        "A missing supervision-setup task is a route failure, not permission to substitute guide next, source archaeology, or an unbounded process action.",
    };

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseFormat(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var payload = new SupervisionSetupGuide
        {
            Task = TaskName,
            ContractVersion = ContractVersion,
            MetadataFree = true,
            ReadOnly = true,
            Summary = "G712 session-scoped supervision setup: emit an artifact, explicitly bootstrap the current GUI session only when wanted, and use reconcile/uninstall for bounded drift removal.",
            ContractStatements = ContractStatements,
            Commands = Commands,
            ArtifactLocation = "Artifacts remain under `.intent-cli/supervision/<domain>/<team>/install/`; no managed artifact is emitted to `~/Library/LaunchAgents`.",
            AuthorityBoundary = "This route only renders guidance. Install authors and first-cycle-probes; registration is an explicit operator action; reconcile/uninstall is the bounded current-session unload/removal command and does not grant workflow or recovery authority.",
            NegativeChecks = NegativeChecks,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        }
        else
        {
            WriteMarkdown(writer, payload);
        }

        return 0;
    }

    private static bool TryParseFormat(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!string.Equals(argument, "--format", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{argument}'.";
                return false;
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[++index]))
            {
                error = "--format requires markdown or json.";
                return false;
            }

            format = args[index];
            if (format is not FormatJson and not FormatMarkdown)
            {
                error = "--format must be markdown or json.";
                return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workflow task supervision-setup");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Read-only and metadata-free. Renders the G712 session-scoped install, current-GUI registration, reconcile, and uninstall contract; it executes none of them.");
    }

    private static void WriteMarkdown(TextWriter writer, SupervisionSetupGuide payload)
    {
        writer.WriteLine("# intent-cli — supervision setup workflow guide (G712)");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("This is a read-only, metadata-free route: it runs from a bare directory and does not read `.intent-cli/config.toml`, queue-state, packets, or intents.");
        writer.WriteLine();
        writer.WriteLine("## Session-scoped contract");
        writer.WriteLine();
        writer.WriteLine(payload.Summary);
        writer.WriteLine();
        foreach (var statement in payload.ContractStatements)
        {
            writer.WriteLine($"- {statement}");
        }
        writer.WriteLine();
        writer.WriteLine($"- artifact location: {payload.ArtifactLocation}");
        writer.WriteLine($"> **Authority boundary:** {payload.AuthorityBoundary}");
        writer.WriteLine();
        writer.WriteLine("## Commands emitted by the shipped contract");
        writer.WriteLine();
        foreach (var command in payload.Commands)
        {
            writer.WriteLine($"- **{command.Name}:** `{command.Command}` — {command.Purpose}");
        }
        writer.WriteLine();
        writer.WriteLine("## Negative checks");
        writer.WriteLine();
        foreach (var check in payload.NegativeChecks)
        {
            writer.WriteLine($"- {check}");
        }
    }

    internal sealed record SupervisionSetupCommand
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("command")] public required string Command { get; init; }
        [JsonPropertyName("purpose")] public required string Purpose { get; init; }
    }

    private sealed record SupervisionSetupGuide
    {
        [JsonPropertyName("task")] public required string Task { get; init; }
        [JsonPropertyName("contract_version")] public required string ContractVersion { get; init; }
        [JsonPropertyName("metadata_free")] public required bool MetadataFree { get; init; }
        [JsonPropertyName("read_only")] public required bool ReadOnly { get; init; }
        [JsonPropertyName("summary")] public required string Summary { get; init; }
        [JsonPropertyName("contract_statements")] public required IReadOnlyList<string> ContractStatements { get; init; }
        [JsonPropertyName("commands")] public required IReadOnlyList<SupervisionSetupCommand> Commands { get; init; }
        [JsonPropertyName("artifact_location")] public required string ArtifactLocation { get; init; }
        [JsonPropertyName("authority_boundary")] public required string AuthorityBoundary { get; init; }
        [JsonPropertyName("negative_checks")] public required IReadOnlyList<string> NegativeChecks { get; init; }
    }
}
