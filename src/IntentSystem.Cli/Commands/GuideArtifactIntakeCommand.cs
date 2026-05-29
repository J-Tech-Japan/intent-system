using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G438: Read-only <c>intent-cli guide artifact-intake</c> command. Emits
/// AI-agent-facing guidance for importing external GitHub issues and PRs
/// into the intent workflow. Three lanes:
/// <list type="bullet">
///   <item><c>external-issue</c>: intake guidance before <c>intent-target</c>
///     is applied to an external issue.</item>
///   <item><c>external-pr-review</c>: intake guidance before normal review
///     transitions are applied to an external PR.</item>
///   <item><c>external-pr-adopt</c>: explicit adopt/import guidance for a
///     rare host decision to formally incorporate an external PR.</item>
/// </list>
/// Every lane requires the host to create or link lightweight
/// packet/review-context metadata before any label mutation. Never
/// mutates state. Never launches an AI provider.
/// </summary>
internal static class GuideArtifactIntakeCommand
{
    internal const string LaneExternalIssue = "external-issue";
    internal const string LaneExternalPrReview = "external-pr-review";
    internal const string LaneExternalPrAdopt = "external-pr-adopt";

    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide artifact-intake --lane external-issue|external-pr-review|external-pr-adopt [--repo <owner/repo>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var lane, out var repo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (lane is null
            || (!string.Equals(lane, LaneExternalIssue, StringComparison.Ordinal)
                && !string.Equals(lane, LaneExternalPrReview, StringComparison.Ordinal)
                && !string.Equals(lane, LaneExternalPrAdopt, StringComparison.Ordinal)))
        {
            writer.WriteLine(
                $"Unsupported --lane '{lane}'. Supported: {LaneExternalIssue}, {LaneExternalPrReview}, {LaneExternalPrAdopt}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guidance = Build(lane, repo);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(guidance, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, guidance);
        }

        return 0;
    }

    internal static ArtifactIntakeGuidance Build(string lane, string? repo)
    {
        return lane switch
        {
            LaneExternalIssue => BuildExternalIssue(repo),
            LaneExternalPrReview => BuildExternalPrReview(repo),
            LaneExternalPrAdopt => BuildExternalPrAdopt(repo),
            _ => throw new ArgumentException($"Unknown lane: {lane}", nameof(lane))
        };
    }

    // ── Lane: external-issue ─────────────────────────────────────────────────

    private static ArtifactIntakeGuidance BuildExternalIssue(string? repo)
    {
        var repoHint = repo ?? "<owner/repo>";
        return new ArtifactIntakeGuidance
        {
            Lane = LaneExternalIssue,
            Repo = repo,
            Summary =
                "External Issue intake — the host must create or link lightweight packet/review-context "
                + "metadata that connects the issue to relevant intents, related packets, and expected "
                + "outcome before applying `intent-target`. Comment-only or label-only handoff is not valid.",
            MetadataRequirements = MetadataRequirementsExternalIssue,
            GuardRails = GuardRailsExternalIssue,
            SuggestedSteps = SuggestedStepsExternalIssue(repoHint),
            ForbiddenActions = ForbiddenActionsIssue,
            AmbiguityPolicy =
                "When the mapping to an existing intent is ambiguous, stop and report `clarification-required` "
                + "rather than guessing. Do not apply `intent-target` until the host operator has confirmed "
                + "the intent linkage.",
            ShadowIssueRequired = false,
            OperatorConfirmationRequired = false
        };
    }

    private static IReadOnlyList<string> MetadataRequirementsExternalIssue =>
    [
        "source_artifact: the external GitHub issue URL and number.",
        "relevant_intents: links to one or more existing intent documents that this issue supports.",
        "related_packets: execution units of any related prepared packets (may be empty if none exist).",
        "expected_outcome: what a correct implementation must achieve, in terms the host can verify.",
        "constraints: any scope, compatibility, or sequencing constraints that bound the work.",
        "host_decision: explicit host-operator statement that this external issue is approved for intake."
    ];

    private static IReadOnlyList<string> GuardRailsExternalIssue =>
    [
        "Do not apply `intent-target` until packet/review-context metadata is created and approved.",
        "Do not mutate queue-state or packet directories directly; use intent-cli commands.",
        "Do not require external contributors to know about intent labels, queue-state, or packet YAML.",
        "If relevant intents cannot be identified, run `intent search` before proceeding.",
        "If the issue raises unresolved product questions, run interview/clarification before recording metadata."
    ];

    private static IReadOnlyList<string> SuggestedStepsExternalIssue(string repo) =>
    [
        $"`gh issue view <n> --repo {repo}` — read the external issue body and labels.",
        $"`intent-cli intent search --domain <domain> --query <keyword> --format json` — find related intents.",
        $"`intent-cli intent status --domain <domain> --format json` — confirm WIP cap and clarification state.",
        "Create lightweight metadata: record source_artifact, relevant_intents, related_packets, expected_outcome, constraints, host_decision in a durable location (e.g. a linked comment on the issue, or a review-context file).",
        "If unresolved product questions exist, run the interview/clarification flow first:",
        "  `intent-cli guide workflow task intent-interview --format markdown`",
        $"`intent-cli automation issue-publish --repo {repo} --issue <n> --write --format json` — apply `intent-target` after metadata is approved by the host operator."
    ];

    private static IReadOnlyList<string> ForbiddenActionsIssue =>
    [
        "Applying `intent-target` before lightweight metadata is recorded and approved.",
        "Using raw `gh issue edit --add-label` to apply workflow labels.",
        "Letting the AI agent decide the intent linkage without operator confirmation.",
        "Moving the issue into the intent queue without a host decision."
    ];

    // ── Lane: external-pr-review ─────────────────────────────────────────────

    private static ArtifactIntakeGuidance BuildExternalPrReview(string? repo)
    {
        var repoHint = repo ?? "<owner/repo>";
        return new ArtifactIntakeGuidance
        {
            Lane = LaneExternalPrReview,
            Repo = repo,
            Summary =
                "External PR review — the host must verify packet/review-context metadata exists and is "
                + "linked before starting normal review transitions. If the PR has no suitable linked issue, "
                + "a shadow issue must be created first. If unresolved product or technical intent questions "
                + "exist, run interview/clarification before shadow issue creation.",
            MetadataRequirements = MetadataRequirementsExternalPr,
            GuardRails = GuardRailsExternalPrReview,
            SuggestedSteps = SuggestedStepsExternalPrReview(repoHint),
            ForbiddenActions = ForbiddenActionsPr,
            AmbiguityPolicy =
                "If the PR's intent context is ambiguous (no linked issue, unclear which intent it supports), "
                + "do not start review transitions. Create the shadow issue and record metadata first, or stop "
                + "with `clarification-required` if the host cannot resolve the mapping.",
            ShadowIssueRequired = true,
            OperatorConfirmationRequired = false
        };
    }

    private static IReadOnlyList<string> MetadataRequirementsExternalPr =>
    [
        "source_artifact: the external GitHub PR URL and number.",
        "linked_issue: a suitable linked issue (may be a shadow issue created by the host) that anchors intent context.",
        "relevant_intents: links to one or more existing intent documents this PR relates to.",
        "review_focus: what the host review should verify — behavior, compatibility, contract sections.",
        "constraints: any scope or sequencing constraints on accepting the PR.",
        "host_decision: explicit host-operator statement that this external PR is approved for review."
    ];

    private static IReadOnlyList<string> GuardRailsExternalPrReview =>
    [
        "Do not start `intent-pr-reviewing` transitions until metadata and a linked issue exist.",
        "If the PR has no suitable linked issue, create a shadow issue before any review transition.",
        "If unresolved product or technical questions exist, run interview/clarification BEFORE creating the shadow issue.",
        "Do not require external contributors to understand intent workflow labels or packet mechanics.",
        "Ambiguous PR → intent mapping must stop for operator clarification before any label mutation."
    ];

    private static IReadOnlyList<string> SuggestedStepsExternalPrReview(string repo) =>
    [
        $"`gh pr view <n> --repo {repo}` — read the PR body, linked issues, and current labels.",
        "Check if a suitable linked issue exists: `gh pr view <n> --repo {repo} --json closingIssuesReferences`.",
        "If no linked issue and product questions are unresolved → run interview/clarification first:",
        "  `intent-cli guide workflow task intent-interview --format markdown`",
        "If no linked issue (and intent context is clear) → create a shadow issue with provenance:",
        "  `gh issue create --repo {repo} --title \"[Shadow] <pr-title>\" --body \"<provenance + metadata>\"` (operator confirms)",
        "Record review-context metadata: linked_issue, relevant_intents, review_focus, constraints, host_decision.",
        $"`intent-cli automation host-review-preflight --repo {repo} --format json` — verify review-start preconditions.",
        $"`intent-cli automation pr-transition --transition review-start --pr <n> --repo {repo} --write --format json` — start review after metadata is confirmed."
    ];

    private static IReadOnlyList<string> ForbiddenActionsPr =>
    [
        "Starting `intent-pr-reviewing` before metadata and a linked issue exist.",
        "Using raw `gh pr edit --add-label` to apply workflow labels.",
        "Skipping shadow issue creation when the PR has no suitable linked issue.",
        "Proceeding with review when unresolved product/technical questions exist."
    ];

    // ── Lane: external-pr-adopt ──────────────────────────────────────────────

    private static ArtifactIntakeGuidance BuildExternalPrAdopt(string? repo)
    {
        var repoHint = repo ?? "<owner/repo>";
        return new ArtifactIntakeGuidance
        {
            Lane = LaneExternalPrAdopt,
            Repo = repo,
            Summary =
                "External PR adopt/import — a rare host decision to formally incorporate an external PR "
                + "into the intent workflow. Requires explicit operator confirmation, full provenance "
                + "recording, and a shadow issue if no suitable linked issue exists. This lane is NOT "
                + "the default review path; use `external-pr-review` for normal external PR review.",
            MetadataRequirements = MetadataRequirementsExternalPrAdopt,
            GuardRails = GuardRailsExternalPrAdopt,
            SuggestedSteps = SuggestedStepsExternalPrAdopt(repoHint),
            ForbiddenActions = ForbiddenActionsAdopt,
            AmbiguityPolicy =
                "Adopt/import must never be the automatic path. If the operator has not explicitly stated "
                + "adoption intent, route to `external-pr-review` instead. Ambiguous provenance must stop "
                + "for operator confirmation before any label mutation.",
            ShadowIssueRequired = true,
            OperatorConfirmationRequired = true
        };
    }

    private static IReadOnlyList<string> MetadataRequirementsExternalPrAdopt =>
    [
        "source_artifact: the external GitHub PR URL and number.",
        "adoption_rationale: host-operator statement of WHY this PR is being adopted (not just reviewed).",
        "linked_issue: a shadow or existing issue anchoring intent context (required before adoption).",
        "relevant_intents: links to intent documents this adoption supports.",
        "provenance: original author, origin repo (if fork/external), prior discussion references.",
        "expected_outcome: what adoption achieves — merged behavior, contract sections it closes.",
        "constraints: compatibility, sequencing, or license constraints on adoption.",
        "operator_confirmation: explicit host-operator sign-off recorded in a durable location."
    ];

    private static IReadOnlyList<string> GuardRailsExternalPrAdopt =>
    [
        "Adoption is a rare explicit host decision; it must never be triggered automatically.",
        "Operator confirmation must be recorded before any label mutation or queue-state change.",
        "Shadow issue must exist before adoption proceeds if no suitable linked issue is present.",
        "Provenance must be recorded so future agents can trace the artifact's origin.",
        "Do not confuse adopt with review: adoption changes the PR's standing in the intent workflow permanently.",
        "If the adoption rationale is unclear, stop and ask the operator for explicit confirmation."
    ];

    private static IReadOnlyList<string> SuggestedStepsExternalPrAdopt(string repo) =>
    [
        $"`gh pr view <n> --repo {repo}` — read the PR body, linked issues, provenance.",
        "Confirm operator explicitly intends adoption (not just review). If unclear, route to `external-pr-review`.",
        "If no suitable linked issue → create shadow issue with full provenance first:",
        "  `gh issue create --repo {repo} --title \"[Adopt] <pr-title>\" --body \"<provenance + adoption rationale>\"` (operator confirms)",
        "Record adoption metadata: source_artifact, adoption_rationale, linked_issue, relevant_intents, provenance, expected_outcome, constraints, operator_confirmation.",
        $"`intent-cli intent search --domain <domain> --query <keyword> --format json` — verify intent linkage.",
        $"`intent-cli automation host-review-preflight --repo {repo} --format json` — verify review-start preconditions after metadata is complete.",
        $"`intent-cli automation pr-transition --transition review-start --pr <n> --repo {repo} --write --format json` — begin review after full adoption metadata is confirmed by operator."
    ];

    private static IReadOnlyList<string> ForbiddenActionsAdopt =>
    [
        "Automatically adopting PRs without explicit operator confirmation.",
        "Adopting a PR without a shadow or linked issue.",
        "Omitting provenance recording (origin author, prior discussion).",
        "Using raw `gh pr edit --add-label` to apply workflow labels.",
        "Treating adoption as the default path for external PRs (default is `external-pr-review`)."
    ];

    // ── Output ───────────────────────────────────────────────────────────────

    private static void WriteMarkdown(TextWriter writer, ArtifactIntakeGuidance guidance)
    {
        writer.WriteLine($"# Guide artifact-intake — {guidance.Lane} (G438)");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(guidance.Repo))
        {
            writer.WriteLine($"- repo: {guidance.Repo}");
            writer.WriteLine();
        }

        writer.WriteLine("## Summary");
        writer.WriteLine();
        writer.WriteLine(guidance.Summary);
        writer.WriteLine();

        writer.WriteLine("## Metadata requirements");
        writer.WriteLine();
        writer.WriteLine("The host must record all of the following before any label mutation:");
        foreach (var req in guidance.MetadataRequirements)
        {
            writer.WriteLine($"- {req}");
        }
        writer.WriteLine();

        writer.WriteLine("## Guard rails");
        foreach (var rail in guidance.GuardRails)
        {
            writer.WriteLine($"- {rail}");
        }
        writer.WriteLine();

        writer.WriteLine("## Suggested steps");
        foreach (var step in guidance.SuggestedSteps)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Forbidden actions");
        foreach (var action in guidance.ForbiddenActions)
        {
            writer.WriteLine($"- {action}");
        }
        writer.WriteLine();

        writer.WriteLine("## Ambiguity policy");
        writer.WriteLine();
        writer.WriteLine(guidance.AmbiguityPolicy);
        writer.WriteLine();

        if (guidance.ShadowIssueRequired)
        {
            writer.WriteLine("## Shadow issue");
            writer.WriteLine();
            writer.WriteLine("A shadow issue is required when the PR has no suitable linked issue. Create it before any review transition or adoption step.");
            writer.WriteLine();
        }

        if (guidance.OperatorConfirmationRequired)
        {
            writer.WriteLine("## Operator confirmation");
            writer.WriteLine();
            writer.WriteLine("Explicit operator confirmation must be recorded in a durable location (issue comment, metadata file) before this lane's mutations are applied. AI agents must not proceed past this point without confirmation.");
            writer.WriteLine();
        }
    }

    // ── Argument parsing ─────────────────────────────────────────────────────

    private static bool TryParseArguments(
        string[] args,
        out string? lane,
        out string? repo,
        out string format,
        out string error)
    {
        lane = null;
        repo = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--lane":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--lane requires a value.";
                        return false;
                    }

                    lane = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
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

        if (string.IsNullOrWhiteSpace(lane))
        {
            error = "--lane is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide artifact-intake (G438)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only AI-agent-facing guidance for importing external GitHub issues and PRs into the intent workflow.");
        writer.WriteLine("Lanes: external-issue (issue intake), external-pr-review (PR review), external-pr-adopt (explicit adoption).");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

/// <summary>G438: structured guidance payload for external artifact intake.</summary>
internal sealed record ArtifactIntakeGuidance
{
    [JsonPropertyName("lane")]
    public required string Lane { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("metadata_requirements")]
    public required IReadOnlyList<string> MetadataRequirements { get; init; }

    [JsonPropertyName("guard_rails")]
    public required IReadOnlyList<string> GuardRails { get; init; }

    [JsonPropertyName("suggested_steps")]
    public required IReadOnlyList<string> SuggestedSteps { get; init; }

    [JsonPropertyName("forbidden_actions")]
    public required IReadOnlyList<string> ForbiddenActions { get; init; }

    [JsonPropertyName("ambiguity_policy")]
    public required string AmbiguityPolicy { get; init; }

    [JsonPropertyName("shadow_issue_required")]
    public required bool ShadowIssueRequired { get; init; }

    [JsonPropertyName("operator_confirmation_required")]
    public required bool OperatorConfirmationRequired { get; init; }
}
