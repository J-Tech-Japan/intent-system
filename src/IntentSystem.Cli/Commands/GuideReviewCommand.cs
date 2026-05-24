using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G248 / G316: Read-only <c>intent-cli guide review</c> command. Emits
/// PR-specific review guidance so an AI reviewer can ask intent-cli what
/// to inspect without reading local skill files. Resolves the execution
/// unit via the queue item's <c>linked_pr</c> field, lists packet refs,
/// surfaces the head of <c>review-context.md</c> when present, and emits a
/// deterministic review checklist, boundaries, and validation
/// suggestions. Never launches an AI provider, never posts comments,
/// never mutates state.
///
/// G316 extends the output to be intent-and-packet aware: structured
/// <c>packet_paths</c> for the canonical packet files, structured
/// <c>intent_reference_paths</c> for the parent intent host's
/// <c>specs</c>/<c>intent-tree</c>/<c>rules</c> directories under the
/// resolved domain, an explicit <c>tests_pass_is_necessary_not_sufficient</c>
/// signal, and approval-summary / request-update requirement lists so
/// reviewers tie evidence to packet contract and intent boundaries
/// rather than tests-only.
/// </summary>
internal static class GuideReviewCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const int ReviewContextHeadLines = 12;

    private const string UsageLine =
        "Usage: intent-cli guide review --pr <n> --repo <owner/repo> [--domain <name>] [--format markdown|json]";

    // G316: canonical packet filenames the reviewer must consult before
    // approval. Order matches the order the reviewer should read them in:
    // packet contract (yaml) → implementation requirements →
    // review-context focus → published GitHub body (verifiable on the PR).
    private static readonly IReadOnlyList<string> CanonicalPacketFiles = new[]
    {
        "packet.yaml",
        "implementation.md",
        "review-context.md",
        "github-body.md"
    };

    // G316 (post-review-fix): intent_reference_paths now surfaces only
    // PR-specific references parsed from the packet artifacts
    // (packet.yaml / implementation.md / review-context.md /
    // github-body.md). Broad directory pointers like
    // `intents/<domain>/specs/` are no longer emitted unprompted —
    // surfacing them unconditionally encouraged full-tree traversal,
    // which contradicts the G316 boundary "do not read the full intent
    // tree per PR". When the packet is silent, the field is empty.
    //
    // The classification heuristic is path-prefix based: each reference
    // path beginning with `intents/` is mapped to one of `specs` /
    // `intent-tree` / `rules` (any other intents/<domain>/<X>/...
    // bucket is reported as `other`).
    private static readonly System.Text.RegularExpressions.Regex IntentReferencePathRegex = new(
        @"\bintents/[A-Za-z0-9_.\-]+/(?<kind>[A-Za-z0-9_.\-]+)(?:/[A-Za-z0-9_.\-/]+)*\.(?:md|markdown|ya?ml|txt)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Collections.Generic.HashSet<string> KnownIntentReferenceKinds =
        new(StringComparer.OrdinalIgnoreCase) { "specs", "intent-tree", "rules" };

    private static readonly IReadOnlyList<string> ReviewChecklist = new[]
    {
        // G316: intent-and-packet-aware checklist. Reviewer must trace
        // each item to packet/issue/intent evidence, not just to test
        // output.
        "Linked issue body's In Scope / Out Of Scope sections are honored by the changeset (cite the section).",
        "packet.yaml execution-unit contract (scope, dependencies, contract pre/post) matches the changeset.",
        "implementation.md requirements are reflected in the diff (cite the requirement → file/symbol mapping).",
        "review-context.md focus areas have been inspected end-to-end (cite the focus area).",
        "github-body.md (if present) is consistent with what the PR actually delivered.",
        "Acceptance Criteria from the issue body are satisfied — each criterion mapped to a concrete change or test.",
        "Verification steps named in the issue body are runnable AND pass.",
        "Out-of-scope boundaries from the issue body are NOT silently expanded by the PR.",
        "Related parent intent / spec / rule references are checked: the change does not contradict design intent for the resolved domain.",
        "PR closing reference (Closes/Fixes/Resolves #<source-issue>) is present and points at the linked source issue (G311).",
        "No prompt-specific label mutation knowledge leaked into the change.",
        "No `intent-target` or `intent-pr-created` are added to the PR by the change.",
        "No AI provider is launched by `intent-cli` in the change.",
        "Generated artifacts alone are not treated as the solution when real code changes are required.",
        "Passing tests is NECESSARY but NOT SUFFICIENT — tests-pass without packet/intent conformance evidence is a request-update, not an approval."
    };

    private static readonly IReadOnlyList<string> ReviewBoundaries = new[]
    {
        "Review is read-only; do not push commits or mutate labels from this loop.",
        "Do not merge the PR from this command; merge happens via the host closeout path.",
        "Do not invent label transitions; rely on installed `intent-cli` claim/complete recommendations.",
        "Do not read parent host packet files to fill contract gaps; flag the gap and stop instead.",
        // G316: bound the intent-trace. Reviewers should NOT read the
        // entire intent tree per PR — packet/review-context references
        // are the primary signal.
        "Do not read the full intent tree per PR — packet.yaml + review-context.md are the primary intent signal; intent_reference_paths are supplementary and consulted only when the packet itself points at them."
    };

    // G316: approval is gated on packet/intent conformance evidence, not
    // just the test result. Surfaced as a structured field so the host
    // loop can quote each requirement back into the approval summary.
    private static readonly IReadOnlyList<string> ApprovalSummaryRequirements = new[]
    {
        "Name the execution unit and packet contract that was checked (packet.yaml scope and pre/post).",
        "Name the issue acceptance criteria that were verified, mapped to changes or tests in the PR.",
        "State that out-of-scope boundaries from the issue body were NOT crossed.",
        "Cite at least one related intent / spec / rule reference (or explicitly state none applied) that the change is consistent with.",
        "Report the test command(s) executed AND state that tests-pass is treated as necessary, not sufficient — pair the test result with packet/intent evidence.",
        "Confirm the PR closing reference (`Closes #<source-issue>`) matches the linked source issue (G311)."
    };

    // G316: request-update comments must distinguish three classes so
    // the implementer knows what they own and what host owns.
    private static readonly IReadOnlyList<string> RequestUpdateRequirements = new[]
    {
        "Classify each finding as ONE of: implementation-finding (code/contract gap the implementer fixes on the PR branch), host-metadata-blocked (parent host metadata; never a PR comment — G287), or intent-ambiguity (packet/issue/intent text is unclear — operator clarification, not a code change).",
        "For implementation-finding entries, tie the finding to a specific packet contract clause, acceptance criterion, or intent reference; vague review notes are not actionable.",
        "Do NOT post host-metadata-blocked or intent-ambiguity findings as PR comments (G287); surface them as structured operator stops instead.",
        "Tests-only failure mode (\"tests pass but evidence missing\") is itself an implementation-finding when the PR cannot show packet/intent conformance — request the implementer to add the missing evidence (test names mapped to AC, comments tying changes to packet clauses, etc.)."
    };

    private static readonly IReadOnlyList<string> DefaultValidationSuggestions = new[]
    {
        "Run focused tests named in the packet's Verification section.",
        "Run `git diff --check` against the merge result.",
        "Confirm the PR head SHA before and after the review pass.",
        // G316: validation is not just test execution.
        "After tests pass, restate at least one packet contract clause AND one intent reference touched by the diff in your approval summary; if you cannot, treat the PR as request-update."
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

        // G267: nested `guide review run` paste-ready review-prompt subcommand.
        if (args.Length >= 1 && string.Equals(args[0], "run", StringComparison.Ordinal))
        {
            return GuideReviewRunCommand.Execute(context, args[1..], writer);
        }

        if (!TryParseArguments(args, out var pr, out var repo, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var queueStatePath = context.GetQueueStatePath();
        var gaps = new List<string>();
        QueueState? queueState = null;

        if (!File.Exists(queueStatePath))
        {
            gaps.Add($"queue-state file not found: {queueStatePath}");
        }
        else
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException jsonException)
            {
                gaps.Add($"queue-state JSON could not be parsed: {jsonException.Message}");
            }
            catch (InvalidOperationException invalidOperation)
            {
                gaps.Add($"queue-state payload was invalid: {invalidOperation.Message}");
            }
        }

        QueueItem? matchedItem = null;
        if (queueState is not null)
        {
            var prToken = pr!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item.LinkedPr, repo!, prToken));
            if (matchedItem is null)
            {
                gaps.Add($"no queue item found with linked_pr matching #{pr}.");
            }
        }

        string? packetDirectory = null;
        IReadOnlyList<string> packetFiles = Array.Empty<string>();
        IReadOnlyList<GuideReviewPacketPath> packetPaths = Array.Empty<GuideReviewPacketPath>();
        string? reviewContextHead = null;
        if (matchedItem is not null)
        {
            packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", matchedItem.ExecutionUnit);
            if (Directory.Exists(packetDirectory))
            {
                packetFiles = Directory.EnumerateFiles(packetDirectory)
                    .Select(file => Path.GetFileName(file))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                var reviewContextPath = Path.Combine(packetDirectory, "review-context.md");
                if (File.Exists(reviewContextPath))
                {
                    reviewContextHead = string.Join('\n', File.ReadAllLines(reviewContextPath).Take(ReviewContextHeadLines));
                }
            }
            else
            {
                gaps.Add($"packet directory not found: {packetDirectory}");
            }

            // G316: enumerate the canonical packet files (whether or not
            // the packet directory exists) so the reviewer always sees
            // exactly which files are expected and which are missing.
            packetPaths = CanonicalPacketFiles
                .Select(name =>
                {
                    var absolute = Path.Combine(
                        packetDirectory ?? string.Empty,
                        name);
                    return new GuideReviewPacketPath
                    {
                        Name = name,
                        Path = absolute,
                        Exists = File.Exists(absolute)
                    };
                })
                .ToArray();
        }

        // G316 (post-review-fix): scan packet artifacts for explicit
        // references to `intents/<domain>/<kind>/<file>` paths and
        // surface only those — never broad directory pointers. Order
        // is deterministic: paths appear in the order they were first
        // encountered while reading packet.yaml → implementation.md →
        // review-context.md → github-body.md.
        var intentReferencePaths = ExtractIntentReferencePaths(
            packetDirectory,
            packetPaths,
            context.RepoRoot);

        var result = new GuideReviewResult
        {
            Domain = domain,
            Repo = repo!,
            Pr = pr!.Value,
            QueueStatePath = queueStatePath,
            ExecutionUnit = matchedItem?.ExecutionUnit,
            QueueItemTitle = matchedItem?.Title,
            QueueItemState = matchedItem?.State.ToString().ToLowerInvariant(),
            PacketDirectory = packetDirectory,
            PacketFiles = packetFiles,
            PacketPaths = packetPaths,
            IntentReferencePaths = intentReferencePaths,
            ReviewContextHead = reviewContextHead,
            ReviewChecklist = ReviewChecklist,
            ReviewBoundaries = ReviewBoundaries,
            ApprovalSummaryRequirements = ApprovalSummaryRequirements,
            RequestUpdateRequirements = RequestUpdateRequirements,
            ReviewBlockerProtocol = ReviewBlockerProtocol.ProtocolRules,
            PrBlockerCommentTemplate = ReviewBlockerProtocol.BlockerCommentTemplateSections,
            ReviewBlockerRoutingExamples = BuildBlockerRoutingExamples(),
            ChatIsNotDurableWorkflowState = ReviewBlockerProtocol.ChatIsNotDurableWorkflowState,
            ValidationSuggestions = DefaultValidationSuggestions,
            TestsPassIsNecessaryNotSufficient = true,
            Gaps = gaps,
            Ready = gaps.Count == 0 && matchedItem is not null
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return result.Ready ? 0 : 1;
    }

    // G316 (post-review-fix): parse the packet artifacts for explicit
    // `intents/<domain>/<kind>/<file>` references and return one
    // structured entry per unique path. Returns an empty list when the
    // packet directory is missing or none of the canonical packet files
    // mention any intent path — broad directory pointers are
    // intentionally NOT synthesized.
    private static IReadOnlyList<GuideReviewIntentReferencePath> ExtractIntentReferencePaths(
        string? packetDirectory,
        IReadOnlyList<GuideReviewPacketPath> packetPaths,
        string repoRoot)
    {
        if (string.IsNullOrEmpty(packetDirectory) || packetPaths.Count == 0)
        {
            return Array.Empty<GuideReviewIntentReferencePath>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<GuideReviewIntentReferencePath>();

        // Read in canonical order: packet.yaml → implementation.md →
        // review-context.md → github-body.md.
        foreach (var packetPath in packetPaths)
        {
            if (!packetPath.Exists)
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(packetPath.Path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in IntentReferencePathRegex.Matches(content))
            {
                var relative = match.Value.Replace('\\', '/');
                if (!seen.Add(relative))
                {
                    continue;
                }
                var kindGroup = match.Groups["kind"].Value;
                var kind = KnownIntentReferenceKinds.Contains(kindGroup) ? kindGroup : "other";
                var absolute = Path.Combine(
                    repoRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                ordered.Add(new GuideReviewIntentReferencePath
                {
                    Kind = kind,
                    RelativePath = relative,
                    Path = absolute,
                    Exists = File.Exists(absolute) || Directory.Exists(absolute)
                });
            }
        }

        return ordered;
    }

    // G394: render the canonical blocker shapes through the shared classifier
    // so the guidance's worked routing examples cannot drift from the
    // decision procedure the unit tests pin (ReviewBlockerProtocol.Classify).
    private static IReadOnlyList<GuideReviewBlockerRoutingExample> BuildBlockerRoutingExamples()
    {
        var examples = new List<GuideReviewBlockerRoutingExample>(ReviewBlockerProtocol.CanonicalScenarios.Count);
        foreach (var (scenario, signal) in ReviewBlockerProtocol.CanonicalScenarios)
        {
            var classification = ReviewBlockerProtocol.Classify(signal);
            examples.Add(new GuideReviewBlockerRoutingExample
            {
                Scenario = scenario,
                Category = classification.Category.ToString(),
                RequiresDurablePrComment = classification.RequiresDurablePrComment,
                MustNotBePrComment = classification.MustNotBePrComment,
                RequiresFollowUpIssue = classification.RequiresFollowUpIssue,
                RecommendedOutcome = classification.RecommendedOutcome,
                Rationale = classification.Rationale,
            });
        }
        return examples;
    }

    private static bool MatchesLinkedPr(string? linkedPr, string repo, string prToken)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        if (string.Equals(linkedPr, prToken, StringComparison.Ordinal))
        {
            return true;
        }

        if (linkedPr!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return linkedPr.StartsWith($"https://github.com/{repo}/pull/", StringComparison.OrdinalIgnoreCase)
                && linkedPr.EndsWith($"/{prToken}", StringComparison.Ordinal);
        }

        return linkedPr!.EndsWith($"/{prToken}", StringComparison.Ordinal);
    }

    private static void WriteMarkdown(TextWriter writer, GuideReviewResult result)
    {
        writer.WriteLine($"# Guide review — {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- execution unit: {(result.ExecutionUnit ?? "(unresolved)")}");
        if (!string.IsNullOrWhiteSpace(result.QueueItemTitle))
        {
            writer.WriteLine($"- queue item title: {result.QueueItemTitle}");
        }
        if (!string.IsNullOrWhiteSpace(result.QueueItemState))
        {
            writer.WriteLine($"- queue item state: {result.QueueItemState}");
        }
        writer.WriteLine($"- ready: {(result.Ready ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Packet");
        writer.WriteLine($"- packet directory: {(result.PacketDirectory ?? "(unknown)")}");
        if (result.PacketFiles.Count == 0)
        {
            writer.WriteLine("- files: (none)");
        }
        else
        {
            writer.WriteLine("- files:");
            foreach (var file in result.PacketFiles)
            {
                writer.WriteLine($"  - {file}");
            }
        }

        // G316: canonical packet paths block — always present (even when
        // the packet directory is missing) so the reviewer knows which
        // files are required for an intent/packet-aware review.
        if (result.PacketPaths.Count > 0)
        {
            writer.WriteLine("- canonical paths:");
            foreach (var path in result.PacketPaths)
            {
                writer.WriteLine($"  - {path.Name}: {path.Path} (exists: {(path.Exists ? "yes" : "no")})");
            }
        }
        writer.WriteLine();

        if (!string.IsNullOrWhiteSpace(result.ReviewContextHead))
        {
            writer.WriteLine($"## review-context.md head ({ReviewContextHeadLines} lines)");
            writer.WriteLine();
            writer.WriteLine("```");
            writer.WriteLine(result.ReviewContextHead);
            writer.WriteLine("```");
            writer.WriteLine();
        }

        // G316 (post-review-fix): intent reference paths now lists ONLY
        // PR-specific references parsed from the packet artifacts;
        // when the packet is silent, the section explicitly reports
        // "(none referenced by packet)" so the reviewer is not nudged
        // toward broad-tree traversal.
        writer.WriteLine("## Intent reference paths");
        writer.WriteLine($"- domain: {result.Domain}");
        if (result.IntentReferencePaths.Count == 0)
        {
            writer.WriteLine("- (none referenced by packet)");
        }
        else
        {
            foreach (var reference in result.IntentReferencePaths)
            {
                writer.WriteLine($"- {reference.Kind}: `{reference.RelativePath}` (exists: {(reference.Exists ? "yes" : "no")})");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Review checklist");
        foreach (var item in result.ReviewChecklist)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review boundaries");
        foreach (var item in result.ReviewBoundaries)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        // G316: tests-pass disclosure + structured approval / request-update
        // requirement blocks so the host loop can quote them verbatim.
        writer.WriteLine("## Sufficiency of evidence");
        writer.WriteLine($"- tests_pass_is_necessary_not_sufficient: {(result.TestsPassIsNecessaryNotSufficient ? "yes" : "no")}");
        writer.WriteLine("- Passing tests is necessary but NOT sufficient for approval. Approval requires packet/intent conformance evidence per the requirements below.");
        writer.WriteLine();

        writer.WriteLine("## Approval summary requirements");
        foreach (var item in result.ApprovalSummaryRequirements)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Request-update requirements");
        foreach (var item in result.RequestUpdateRequirements)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        // G394: durable blocker protocol + PR blocker comment template +
        // worked routing examples. Record current-PR blockers on the PR, not
        // only in chat, before completing as request-update / clarification.
        writer.WriteLine("## Review blocker protocol");
        writer.WriteLine($"- chat_is_not_durable_workflow_state: {(result.ChatIsNotDurableWorkflowState ? "yes" : "no")}");
        foreach (var item in result.ReviewBlockerProtocol)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## PR blocker comment template");
        foreach (var item in result.PrBlockerCommentTemplate)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review blocker routing examples");
        foreach (var example in result.ReviewBlockerRoutingExamples)
        {
            writer.WriteLine($"- {example.Scenario}");
            writer.WriteLine(
                $"  - category: {example.Category}; durable_pr_comment: {(example.RequiresDurablePrComment ? "yes" : "no")}; "
                + $"follow_up_issue: {(example.RequiresFollowUpIssue ? "yes" : "no")}; "
                + $"never_pr_comment: {(example.MustNotBePrComment ? "yes" : "no")}; outcome: {example.RecommendedOutcome}");
        }
        writer.WriteLine();

        writer.WriteLine("## Validation suggestions");
        foreach (var item in result.ValidationSuggestions)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        if (result.Gaps.Count > 0)
        {
            writer.WriteLine("## Gaps");
            foreach (var gap in result.Gaps)
            {
                writer.WriteLine($"- {gap}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int? pr,
        out string? repo,
        out string? domainOverride,
        out string format,
        out string error)
    {
        pr = null;
        repo = null;
        domainOverride = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue) || prValue <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    pr = prValue;
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

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
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

        if (pr is null)
        {
            error = "--pr is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide review");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only PR-specific review guidance: execution unit, packet refs, review-context excerpt, deterministic review checklist, boundaries, and validation suggestions.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideReviewResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("queue_item_title")]
    public string? QueueItemTitle { get; init; }

    [JsonPropertyName("queue_item_state")]
    public string? QueueItemState { get; init; }

    [JsonPropertyName("packet_directory")]
    public string? PacketDirectory { get; init; }

    [JsonPropertyName("packet_files")]
    public required IReadOnlyList<string> PacketFiles { get; init; }

    /// <summary>
    /// G316: structured per-canonical-file packet path entries with
    /// existence flags so the reviewer always knows which expected
    /// packet artifacts are present.
    /// </summary>
    [JsonPropertyName("packet_paths")]
    public required IReadOnlyList<GuideReviewPacketPath> PacketPaths { get; init; }

    /// <summary>
    /// G316: structured intent-reference directory entries
    /// (`specs`/`intent-tree`/`rules` under the resolved domain) so the
    /// reviewer can locate parent design intent without grepping.
    /// </summary>
    [JsonPropertyName("intent_reference_paths")]
    public required IReadOnlyList<GuideReviewIntentReferencePath> IntentReferencePaths { get; init; }

    [JsonPropertyName("review_context_head")]
    public string? ReviewContextHead { get; init; }

    [JsonPropertyName("review_checklist")]
    public required IReadOnlyList<string> ReviewChecklist { get; init; }

    [JsonPropertyName("review_boundaries")]
    public required IReadOnlyList<string> ReviewBoundaries { get; init; }

    /// <summary>
    /// G316: requirements the reviewer must include in an approval
    /// summary; tests-pass alone is not sufficient.
    /// </summary>
    [JsonPropertyName("approval_summary_requirements")]
    public required IReadOnlyList<string> ApprovalSummaryRequirements { get; init; }

    /// <summary>
    /// G316: requirements for request-update comments — distinguish
    /// implementation-finding from host-metadata-blocked from
    /// intent-ambiguity, and tie each finding to a packet/intent clause.
    /// </summary>
    [JsonPropertyName("request_update_requirements")]
    public required IReadOnlyList<string> RequestUpdateRequirements { get; init; }

    /// <summary>
    /// G394: durable-routing rules for review clarification stops — record
    /// current-PR blockers on the PR (not only in chat) before completing.
    /// </summary>
    [JsonPropertyName("review_blocker_protocol")]
    public required IReadOnlyList<string> ReviewBlockerProtocol { get; init; }

    /// <summary>
    /// G394: required sections of a durable PR blocker comment.
    /// </summary>
    [JsonPropertyName("pr_blocker_comment_template")]
    public required IReadOnlyList<string> PrBlockerCommentTemplate { get; init; }

    /// <summary>
    /// G394: worked routing examples (one per canonical blocker shape) so the
    /// reviewer sees PR-comment vs follow-up-issue vs host-metadata routing
    /// concretely, including the Zero4Racer PR #406 canonical-flow case.
    /// </summary>
    [JsonPropertyName("review_blocker_routing_examples")]
    public required IReadOnlyList<GuideReviewBlockerRoutingExample> ReviewBlockerRoutingExamples { get; init; }

    /// <summary>
    /// G394: explicit signal that chat is not durable workflow state for a
    /// blocked PR. Always true.
    /// </summary>
    [JsonPropertyName("chat_is_not_durable_workflow_state")]
    public required bool ChatIsNotDurableWorkflowState { get; init; }

    [JsonPropertyName("validation_suggestions")]
    public required IReadOnlyList<string> ValidationSuggestions { get; init; }

    /// <summary>
    /// G316: explicit signal that passing tests is necessary but not
    /// sufficient for approval. Always true; surfaced as a structured
    /// field so automation can quote it back into the approval summary.
    /// </summary>
    [JsonPropertyName("tests_pass_is_necessary_not_sufficient")]
    public required bool TestsPassIsNecessaryNotSufficient { get; init; }

    [JsonPropertyName("gaps")]
    public required IReadOnlyList<string> Gaps { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }
}

/// <summary>
/// G316: single canonical packet file entry for the reviewer.
/// </summary>
internal sealed record GuideReviewPacketPath
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("exists")]
    public required bool Exists { get; init; }
}

/// <summary>
/// G316: single intent-reference directory pointer (specs / intent-tree
/// / rules) for the reviewer.
/// </summary>
internal sealed record GuideReviewIntentReferencePath
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("relative_path")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("exists")]
    public required bool Exists { get; init; }
}

/// <summary>
/// G394: a single worked review-blocker routing example, generated by
/// <see cref="ReviewBlockerProtocol.Classify"/> over a canonical blocker shape.
/// </summary>
internal sealed record GuideReviewBlockerRoutingExample
{
    [JsonPropertyName("scenario")]
    public required string Scenario { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("requires_durable_pr_comment")]
    public required bool RequiresDurablePrComment { get; init; }

    [JsonPropertyName("must_not_be_pr_comment")]
    public required bool MustNotBePrComment { get; init; }

    [JsonPropertyName("requires_follow_up_issue")]
    public required bool RequiresFollowUpIssue { get; init; }

    [JsonPropertyName("recommended_outcome")]
    public required string RecommendedOutcome { get; init; }

    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }
}
