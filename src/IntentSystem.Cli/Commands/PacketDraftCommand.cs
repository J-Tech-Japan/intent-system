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

    // G482: the required publish-gate sections now live in the single shared
    // source of truth so the scaffold's draft check and the publish-body
    // validator can never drift apart.
    internal static IReadOnlyList<string> RequiredContractSections => PublishContractSections.Required;

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

        // G347: resolve base branch policy from host config so the published
        // contract section carries the project-specific expected base branch.
        var baseBranchPolicy = context.Config.Project.BaseBranchPolicy;
        if (string.IsNullOrWhiteSpace(baseBranchPolicy))
        {
            baseBranchPolicy = CliRuntimeContracts.DefaultBaseBranchPolicy;
        }
        var expectedBaseBranch = BaseBranchPolicyContract.IsKnownPolicy(baseBranchPolicy)
            ? BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy)
            : CliRuntimeContracts.DirectMainBaseBranch;

        var planned = new[]
        {
            ("packet.yaml", BuildPacketYaml(executionUnit, domain, targetRepo)),
            ("implementation.md", BuildImplementationMd(executionUnit)),
            ("review-context.md", BuildReviewContextMd(executionUnit)),
            ("github-body.md", BuildGithubBodyMd(executionUnit, baseBranchPolicy, expectedBaseBranch))
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
            MissingContractSections = missingSections,
            // G449: derive the packet's publish-readiness verdict through the
            // SHARED NextSliceReadinessEvaluator so packet-draft validation
            // agrees with next-slice / publish-flow / diagnostics on contract
            // completeness (no missing required sections → publishable).
            ContractPublishable = NextSliceReadinessEvaluator.IsPublishable(
                executionUnit, contractComplete: missingSections.Count == 0)
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

            # G461: optional packet-time intent-maintenance metadata. OPTIONAL and
            # backward-compatible — packets that omit this whole block stay valid.
            # Fill it in or explicitly decline each part while the design context is
            # fresh; `improve` (G456 / G460) is the later safety net, not a substitute.
            intent_placement:
              primary_intent: <intents/{domain}/intent-tree/...>
              supporting_intents: []
              new_intent_needed: false
              placement_rationale: ""
            knowledge_updates:
              intent_tree:
                required: false
                target_paths: []
                summary: ""
              adr:
                required: false
                target_paths: []
                decision_title: ""
              diagram:
                required: false
                target_paths: []
                diagram_type: none
              docs:
                required: false
                target_paths: []
                summary: ""
            closeout_learning:
              expected: ""
              write_back_required: false
              write_back_targets: []
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

            ## Knowledge Maintenance (G461, optional)

            Captured while the design context is fresh. Answer or explicitly decline:

            - Intent placement: TODO which intent node this supports / whether a new node is needed.
            - ADR candidate: TODO ADR-worthy decision + path, or decline.
            - Diagram candidate: TODO concept/workflow/topology/state diagram update, or decline.
            - Docs update: TODO user-facing docs to change, or decline.
            - Closeout learning: TODO knowledge to write back after landing + whether `write_back_required`.

            `improve` (G456 / G460) is the later safety net; packet-time maintenance is the normal path.
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

            ## Knowledge Writeback Expectation (G461)

            If the packet's `closeout_learning.write_back_required` is `true`, confirm the
            expected intent-tree / ADR / diagram / docs writeback landed in this PR or was
            captured as a follow-up packet. If the packet declined all knowledge maintenance,
            that is acceptable — note it rather than blocking.
            """;
    }

    private static string BuildGithubBodyMd(string executionUnit, string baseBranchPolicy, string expectedBaseBranch)
    {
        // G347: derive policy-specific branch instruction for the mandatory contract section.
        var branchInstruction = string.Equals(baseBranchPolicy, CliRuntimeContracts.MainAiBaseBranchPolicy, StringComparison.Ordinal)
            ? $"Open all child PRs against `{expectedBaseBranch}`. Do NOT target `main` directly — the human operator periodically merges `{expectedBaseBranch}` → `main`."
            : $"Open all child PRs against `{expectedBaseBranch}` directly.";

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

            ## Standalone Child Issue Contract

            TODO: one-paragraph restatement of exactly what the child PR must deliver, readable on its own without the surrounding design thread.

            ## Acceptance Criteria

            - TODO

            ## Verification

            TODO: focused tests and `git diff --check`.

            ## Related Links

            - TODO

            ## Knowledge Maintenance

            Optional (G461). Tells the implementer/reviewer whether intent / ADR / diagram / docs
            writeback is expected for this slice. Answer or explicitly decline:

            - Intent placement: TODO / none
            - ADR candidate: TODO / none
            - Diagram candidate: TODO / none
            - Docs update: TODO / none
            - Closeout writeback expected: no

            ## Base Branch Policy

            Policy: `{baseBranchPolicy}`
            Expected PR base branch: `{expectedBaseBranch}`

            {branchInstruction}
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

    /// <summary>
    /// G449: the packet's publish-readiness verdict from the shared
    /// <see cref="NextSliceReadinessEvaluator"/> — true only when the contract
    /// is complete (no missing required sections). Routes packet-draft
    /// validation through the same engine as next-slice / publish-flow /
    /// diagnostics so the surfaces never contradict on contract completeness.
    /// </summary>
    [JsonPropertyName("contract_publishable")]
    public bool ContractPublishable { get; init; }
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
