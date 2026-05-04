using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G244: <c>intent-cli packet draft</c> command. Scaffolds and validates
/// the canonical packet directory for a child execution unit
/// (<c>.intent-cli/issues/&lt;id&gt;/</c>). Writes only skeleton files
/// (<c>packet.yaml</c>, <c>implementation.md</c>, <c>review-context.md</c>,
/// <c>github-body.md</c>) and never overwrites existing content. Produces
/// deterministic output, supports a read-only <c>--dry-run</c> preview,
/// and never launches an AI provider.
/// </summary>
internal static class PacketDraftCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string FileCreated = "created";
    private const string FileSkipped = "skipped";
    private const string FilePlanned = "planned";

    private const string UsageLine =
        "Usage: intent-cli packet draft --execution-unit <id> [--domain <name>] [--target-repo <owner/repo>] [--dry-run] [--format markdown|json]";

    private static readonly Regex ExecutionUnitPattern = new(
        @"^[A-Za-z][A-Za-z0-9-]*$",
        RegexOptions.Compiled);

    internal static readonly IReadOnlyList<string> RequiredContractSections =
        new[]
        {
            "Goal",
            "Why This Slice Exists Now",
            "Current Observed State",
            "Accepted Baseline You May Assume",
            "Target Repo / Path / Part",
            "In Scope",
            "Out Of Scope",
            "Acceptance Criteria",
            "Verification",
            "Related Links"
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

        if (!TryParseArguments(args, out var executionUnit, out var domainOverride, out var targetRepo, out var dryRun, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!ExecutionUnitPattern.IsMatch(executionUnit!))
        {
            writer.WriteLine($"Invalid execution-unit id '{executionUnit}'. Expected an alphanumeric token like 'G244'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Draft(context, executionUnit!, domainOverride, targetRepo, dryRun);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    internal static PacketDraftResult Draft(
        CliContext context,
        string executionUnit,
        string? domainOverride,
        string? targetRepo,
        bool dryRun)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
        var mode = dryRun ? ModeDryRun : ModeWrite;

        if (!dryRun)
        {
            Directory.CreateDirectory(packetDirectory);
        }

        var planned = new[]
        {
            ("packet.yaml", BuildPacketYaml(executionUnit, domain, targetRepo)),
            ("implementation.md", BuildImplementationMd(executionUnit)),
            ("review-context.md", BuildReviewContextMd(executionUnit)),
            ("github-body.md", BuildGithubBodyMd(executionUnit))
        };

        var files = new List<PacketDraftFile>();
        foreach (var (name, content) in planned)
        {
            var path = Path.Combine(packetDirectory, name);
            var alreadyExists = File.Exists(path);

            string status;
            if (dryRun)
            {
                status = alreadyExists ? FileSkipped : FilePlanned;
            }
            else if (alreadyExists)
            {
                status = FileSkipped;
            }
            else
            {
                File.WriteAllText(path, content);
                status = FileCreated;
            }

            files.Add(new PacketDraftFile
            {
                Name = name,
                Path = path,
                Status = status
            });
        }

        // Validate contract sections against whatever github-body.md ends up on disk
        // (skeleton if newly written, existing content if previously authored).
        var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
        IReadOnlyList<string> missingSections = Array.Empty<string>();
        if (File.Exists(githubBodyPath))
        {
            var content = File.ReadAllText(githubBodyPath);
            missingSections = RequiredContractSections
                .Where(section => !ContainsSectionHeading(content, section))
                .ToArray();
        }
        else if (dryRun)
        {
            // Dry-run did not write the skeleton, but the planned content satisfies all
            // required headings, so the validation result mirrors that intent.
            missingSections = Array.Empty<string>();
        }
        else
        {
            missingSections = RequiredContractSections;
        }

        return new PacketDraftResult
        {
            ExecutionUnit = executionUnit,
            Domain = domain,
            TargetRepo = targetRepo,
            PacketDirectory = packetDirectory,
            Mode = mode,
            Files = files,
            MissingContractSections = missingSections
        };
    }

    private static string BuildPacketYaml(string executionUnit, string domain, string? targetRepo)
    {
        var repoLine = targetRepo ?? "<owner/repo>";
        return $"""
            implementation_issue_packet:
              issue_title: "{executionUnit} TODO short title"
              issue_kind: feature
              source_execution_unit: {executionUnit}
              domain: {domain}
              target_repo: {repoLine}
              target_path: <comma- or space-separated paths>
              target_part: "<one-line target description>"
              dependencies: []
              technical_baseline: []
              intent_references: []
              acceptance_criteria:
                - "TODO: at least one acceptance criterion"
            """;
    }

    private static string BuildImplementationMd(string executionUnit)
    {
        return $"""
            # {executionUnit} Implementation Packet

            ## Goal

            TODO: one-paragraph statement of what this slice changes.

            ## Why

            TODO: why this slice exists now.

            ## Scope

            - TODO

            ## Out of scope

            - TODO

            ## Verification

            TODO: focused tests and `git diff --check`.
            """;
    }

    private static string BuildReviewContextMd(string executionUnit)
    {
        return $"""
            # {executionUnit} Review Context

            Review that this slice moves operation toward the documented intent without widening scope.

            Flag findings if the implementation:

            - widens scope beyond the issue contract;
            - launches AI providers from `intent-cli`;
            - mutates GitHub or parent state when the issue is read-only;
            - skips required contract sections.
            """;
    }

    private static string BuildGithubBodyMd(string executionUnit)
    {
        return $"""
            ## Goal

            TODO: state what this slice will change.

            ## Why This Slice Exists Now

            TODO: explain why this is the next step.

            ## Current Observed State

            TODO: describe current behavior or repro.

            ## Accepted Baseline You May Assume

            - TODO

            ## Target Repo / Path / Part

            Repository: `<owner/repo>`

            Target paths: `<comma- or space-separated paths>`

            Target part: `<one-line target description>`

            ## In Scope

            - TODO

            ## Out Of Scope

            - TODO

            ## Acceptance Criteria

            - TODO

            ## Verification

            TODO: focused tests and `git diff --check`.

            ## Related Links

            - TODO
            """;
    }

    private static bool ContainsSectionHeading(string content, string section)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = line.TrimStart('#').Trim();
            if (string.Equals(heading, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteMarkdown(TextWriter writer, PacketDraftResult result)
    {
        writer.WriteLine($"# Packet draft — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- target repo: {(result.TargetRepo ?? "(unspecified)")}");
        writer.WriteLine($"- packet directory: {result.PacketDirectory}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine();

        writer.WriteLine("## Files");
        foreach (var file in result.Files)
        {
            writer.WriteLine($"- {file.Name}: {file.Status}");
        }
        writer.WriteLine();

        writer.WriteLine("## Contract validation");
        if (result.MissingContractSections.Count == 0)
        {
            writer.WriteLine("- missing sections: none");
        }
        else
        {
            writer.WriteLine("- missing sections:");
            foreach (var section in result.MissingContractSections)
            {
                writer.WriteLine($"  - {section}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? domainOverride,
        out string? targetRepo,
        out bool dryRun,
        out string format,
        out string error)
    {
        executionUnit = null;
        domainOverride = null;
        targetRepo = null;
        dryRun = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }

                    executionUnit = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
                    index++;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("packet draft");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Scaffolds packet.yaml, implementation.md, review-context.md, github-body.md for an execution unit. Existing files are never overwritten.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record PacketDraftResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<PacketDraftFile> Files { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }
}

internal sealed record PacketDraftFile
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
