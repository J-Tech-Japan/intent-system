using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G383: read-only <c>intent-cli guide review-verification-policy</c>.
/// The deterministic decision surface for a host review loop facing a
/// visible / manual / runtime-gated verification acceptance criterion, so
/// it stops re-asking the operator a standing A/B/C policy question every
/// wake. With no input it prints the policy + the three classifications +
/// routing + the no-false-runtime-claim boundary. With
/// <c>--evidence &lt;kind&gt;</c> (plus the situation flags) it runs
/// <see cref="ReviewVerificationPolicyClassifier"/> and reports the
/// decision, route, and reason. Host-state-free; never publishes; never
/// launches an AI provider.
/// </summary>
internal static class GuideReviewVerificationPolicyCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide review-verification-policy [--standing-policy] [--evidence source-mapping|documented-observation|static-screenshot|none] [--false-runtime-claim] [--implementation-actionable] [--format markdown|json]";

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

        if (!TryParseArguments(
                args,
                out var standingPolicy,
                out var evidence,
                out var hasEvidence,
                out var falseRuntimeClaim,
                out var implementationActionable,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        ReviewVerificationPolicyDecision? decision = hasEvidence
            ? ReviewVerificationPolicyClassifier.Classify(standingPolicy, evidence!, falseRuntimeClaim, implementationActionable)
            : null;

        var result = Build(decision);

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

    private static GuideReviewVerificationPolicyResult Build(ReviewVerificationPolicyDecision? decision)
    {
        var prompt =
@"When a host review PR is blocked ONLY by a visible / manual / runtime-gated verification acceptance criterion, do NOT ask the operator the same standing A/B/C policy question every wake — the review loop is not the product-owner interview loop. Pick one deterministic route:

1) If a standing norm is encoded that permits approval on source mapping + documented observation (and you NEVER claim runtime/manual verification was actually executed), apply it and continue approval/merge/closeout.
2) If the gap is implementation-actionable (the implementer can add the missing evidence on the PR branch), leave an actionable PR comment and apply `pr-transition --transition request-update` via intent-cli — do not ask in chat.
3) If the gap is a host-owned policy/design decision, record it ONCE as a durable host clarification/signal and mark it pending, so later wakes do not re-ask. Never post a host-policy gap on the child PR as an implementer request.

Never approve a path that would require falsely claiming runtime/manual verification was performed.";

        var decisions = new[]
        {
            new GuideReviewPolicyClassification
            {
                Name = ReviewVerificationPolicyClassifier.Decisions.StandingPolicyApprove,
                Route = ReviewVerificationPolicyClassifier.Routes.ProceedApprove,
                When = "A standing norm is encoded and visible evidence (source-mapping / documented-observation / static-screenshot) supports the AC with no false runtime claim. Apply it; proceed to approve/merge/closeout.",
            },
            new GuideReviewPolicyClassification
            {
                Name = ReviewVerificationPolicyClassifier.Decisions.ImplementationFinding,
                Route = ReviewVerificationPolicyClassifier.Routes.PrFeedbackRequestUpdate,
                When = "The implementer can add the missing evidence on the PR branch. Leave an actionable PR comment + `automation pr-transition --transition request-update` via intent-cli — not a chat question.",
            },
            new GuideReviewPolicyClassification
            {
                Name = ReviewVerificationPolicyClassifier.Decisions.ReviewPolicyGap,
                Route = ReviewVerificationPolicyClassifier.Routes.HostDurableSignalOnce,
                When = "The missing piece is a host-owned policy/design decision. Record it ONCE as a durable host clarification/signal (`intent-cli clarify open`/`record`, or the worker-signal protocol) and mark it pending; do not re-ask each wake and do not post it on the child PR.",
            },
        };

        var summaryRequirements = new[]
        {
            "State exactly what WAS verified (source mapping, documented observation, static/screenshot evidence) and exactly what was NOT run.",
            "Never claim local / Godot / runtime / manual verification was executed when it was not.",
            "Auto-merge only when no implementation findings exist; visible-verification approval still requires the encoded standing policy.",
        };

        return new GuideReviewVerificationPolicyResult
        {
            Kind = "review-verification-policy",
            Prompt = prompt,
            Classifications = decisions,
            SummaryRequirements = summaryRequirements,
            Decision = decision is null
                ? null
                : new GuideReviewPolicyDecisionView
                {
                    Decision = decision.Decision,
                    Route = decision.Route,
                    RecordHostGapOnce = decision.RecordHostGapOnce,
                    PostPrFeedback = decision.PostPrFeedback,
                    Reason = decision.Reason,
                },
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideReviewVerificationPolicyResult result)
    {
        writer.WriteLine("# Guide — host review visible-verification policy");
        writer.WriteLine();

        if (result.Decision is { } decision)
        {
            writer.WriteLine($"## Decision: `{decision.Decision}`");
            writer.WriteLine($"- route: `{decision.Route}`");
            writer.WriteLine($"- record host gap once: {decision.RecordHostGapOnce.ToString().ToLowerInvariant()}");
            writer.WriteLine($"- post PR feedback: {decision.PostPrFeedback.ToString().ToLowerInvariant()}");
            writer.WriteLine($"- {decision.Reason}");
            writer.WriteLine();
        }

        writer.WriteLine("## Classifications");
        foreach (var classification in result.Classifications)
        {
            writer.WriteLine($"- `{classification.Name}` → route `{classification.Route}`: {classification.When}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review summary requirements");
        foreach (var requirement in result.SummaryRequirements)
        {
            writer.WriteLine($"- {requirement}");
        }
        writer.WriteLine();

        writer.WriteLine("## Protocol");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
    }

    private static bool TryParseArguments(
        string[] args,
        out bool standingPolicy,
        out string? evidence,
        out bool hasEvidence,
        out bool falseRuntimeClaim,
        out bool implementationActionable,
        out string format,
        out string error)
    {
        standingPolicy = false;
        evidence = null;
        hasEvidence = false;
        falseRuntimeClaim = false;
        implementationActionable = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--standing-policy":
                    standingPolicy = true;
                    break;

                case "--false-runtime-claim":
                    falseRuntimeClaim = true;
                    break;

                case "--implementation-actionable":
                    implementationActionable = true;
                    break;

                case "--evidence":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--evidence requires a value (source-mapping | documented-observation | static-screenshot | none).";
                        return false;
                    }
                    evidence = args[index + 1].Trim();
                    hasEvidence = true;
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

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide review-verification-policy");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: deterministic route for visible/manual/runtime-gated verification ACs (standing-policy-approve / implementation-finding / review-policy-gap) so the review loop never re-asks the operator each wake.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideReviewVerificationPolicyResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("classifications")]
    public required IReadOnlyList<GuideReviewPolicyClassification> Classifications { get; init; }

    [JsonPropertyName("summary_requirements")]
    public required IReadOnlyList<string> SummaryRequirements { get; init; }

    [JsonPropertyName("decision")]
    public GuideReviewPolicyDecisionView? Decision { get; init; }
}

internal sealed record GuideReviewPolicyClassification
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("route")]
    public required string Route { get; init; }

    [JsonPropertyName("when")]
    public required string When { get; init; }
}

internal sealed record GuideReviewPolicyDecisionView
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("route")]
    public required string Route { get; init; }

    [JsonPropertyName("record_host_gap_once")]
    public required bool RecordHostGapOnce { get; init; }

    [JsonPropertyName("post_pr_feedback")]
    public required bool PostPrFeedback { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}
