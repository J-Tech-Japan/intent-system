using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G705: read-only project-feedback guidance. This command deliberately owns
/// only static rendering. It does not reference the issue-publish boundary,
/// a process runner, an HTTP client, or any telemetry store.
/// </summary>
internal static class GuideFeedbackCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string UsageLine =
        "Usage: intent-cli guide feedback [--format markdown|json]";

    internal const string Repository = "J-Tech-Japan/intent-system";
    internal const string IssuesUrl = "https://github.com/J-Tech-Japan/intent-system/issues";
    internal const string FilingCommand =
        "gh issue create --repo J-Tech-Japan/intent-system --title \"<short summary>\" --body-file <reviewed-report.md>";

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
            writer.WriteLine("guide feedback");
            writer.WriteLine(UsageLine);
            writer.WriteLine("Read-only public GitHub feedback guidance; it renders a filing form and never submits it.");
            return 0;
        }

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildResult();
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    internal static GuideFeedbackResult BuildResult() => new()
    {
        Surface = "project-feedback",
        Repository = Repository,
        IssuesUrl = IssuesUrl,
        FilingCommand = FilingCommand,
        RenderOnly = true,
        PublicChannelWarning =
            "PUBLIC / WORLD-READABLE PERMANENTLY: GitHub issues in J-Tech-Japan/intent-system are public and remain world-readable permanently. Never include credentials or tokens, private hostnames or private paths, customer or personal data, or internal URLs. Review pasted logs before filing.",
        DoNotInclude =
        [
            "Credentials or tokens.",
            "Private hostnames or private paths.",
            "Customer or personal data.",
            "Internal URLs.",
        ],
        ReviewBeforeFiling = "Review pasted logs before filing and remove secrets, private infrastructure, and identifying data.",
        RecommendedReportShape = new GuideFeedbackReportShape
        {
            RecommendationStatus = "Recommendations only; never required gates.",
            Elements =
            [
                "Exact installed version string.",
                "Timestamped observations.",
                "Expected versus actual behavior.",
                "Reproduction context.",
                "A verified-versus-assumed separation.",
            ],
            ImperfectReportRule =
                "An imperfect but real report remains fileable; missing a recommended element must not suppress or reject it.",
        },
        AiSeatRule =
            "An AI seat may draft the report body only. Deliberate filing is a per-action act by the design thread or the operator, consistent with G701; this guide grants no new standing authority and does not add a confirmation-based submission path.",
        ScopeBoundary =
            "This is project feedback for J-Tech-Japan/intent-system. It is distinct from execution-unit child issue publishing, packet publication, worker claims, and workflow-label transitions.",
        NoSendInvariants =
        [
            "intent-cli only renders Markdown or JSON; it never executes the rendered `gh issue create` command.",
            "No GitHub/API POST, network connection, or subprocess is opened by this surface.",
            "No confirmation-based submission, telemetry write, telemetry queue, issue body file, or publish artifact is created.",
            "Filing remains outside this command as an explicit per-action human/design decision.",
        ],
    };

    private static void WriteMarkdown(TextWriter writer, GuideFeedbackResult result)
    {
        writer.WriteLine("# intent-cli guide feedback (G705)");
        writer.WriteLine();
        writer.WriteLine("Read-only project-feedback guidance for a human or AI agent. This command renders a report shape and a command form; it never files an issue.");
        writer.WriteLine();
        writer.WriteLine("## Public channel — read before filing");
        writer.WriteLine();
        writer.WriteLine($"**{result.PublicChannelWarning}**");
        writer.WriteLine();
        writer.WriteLine($"- repository: `{result.Repository}`");
        writer.WriteLine($"- public issue channel: `{result.IssuesUrl}`");
        writer.WriteLine($"- review step: {result.ReviewBeforeFiling}");
        writer.WriteLine();
        writer.WriteLine("Never include:");
        foreach (var item in result.DoNotInclude)
        {
            writer.WriteLine($"- {item}");
        }

        writer.WriteLine();
        writer.WriteLine("## Rendered command form — not executed");
        writer.WriteLine();
        writer.WriteLine("A human or design thread may deliberately run this reviewed form with `gh`; the installed CLI only prints it:");
        writer.WriteLine();
        writer.WriteLine("```bash");
        writer.WriteLine(result.FilingCommand);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("## Recommended report shape — recommendations, never required gates");
        writer.WriteLine();
        writer.WriteLine($"- status: {result.RecommendedReportShape.RecommendationStatus}");
        foreach (var element in result.RecommendedReportShape.Elements)
        {
            writer.WriteLine($"- recommended element: {element}");
        }
        writer.WriteLine($"- imperfect-report rule: {result.RecommendedReportShape.ImperfectReportRule}");
        writer.WriteLine();
        writer.WriteLine("## AI-seat and scope boundary");
        writer.WriteLine();
        writer.WriteLine(result.AiSeatRule);
        writer.WriteLine();
        writer.WriteLine(result.ScopeBoundary);
        writer.WriteLine();
        writer.WriteLine("## No-send invariants");
        writer.WriteLine();
        foreach (var invariant in result.NoSendInvariants)
        {
            writer.WriteLine($"- {invariant}");
        }
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--format", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{args[index]}'.";
                return false;
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = "--format requires a value (markdown or json).";
                return false;
            }

            var requested = args[++index];
            if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
            {
                error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                return false;
            }

            format = requested;
        }

        return true;
    }
}

internal sealed record GuideFeedbackResult
{
    [JsonPropertyName("surface")]
    public required string Surface { get; init; }

    [JsonPropertyName("repository")]
    public required string Repository { get; init; }

    [JsonPropertyName("issues_url")]
    public required string IssuesUrl { get; init; }

    [JsonPropertyName("filing_command")]
    public required string FilingCommand { get; init; }

    [JsonPropertyName("render_only")]
    public required bool RenderOnly { get; init; }

    [JsonPropertyName("public_channel_warning")]
    public required string PublicChannelWarning { get; init; }

    [JsonPropertyName("do_not_include")]
    public required IReadOnlyList<string> DoNotInclude { get; init; }

    [JsonPropertyName("review_before_filing")]
    public required string ReviewBeforeFiling { get; init; }

    [JsonPropertyName("recommended_report_shape")]
    public required GuideFeedbackReportShape RecommendedReportShape { get; init; }

    [JsonPropertyName("ai_seat_rule")]
    public required string AiSeatRule { get; init; }

    [JsonPropertyName("scope_boundary")]
    public required string ScopeBoundary { get; init; }

    [JsonPropertyName("no_send_invariants")]
    public required IReadOnlyList<string> NoSendInvariants { get; init; }
}

internal sealed record GuideFeedbackReportShape
{
    [JsonPropertyName("recommendation_status")]
    public required string RecommendationStatus { get; init; }

    [JsonPropertyName("elements")]
    public required IReadOnlyList<string> Elements { get; init; }

    [JsonPropertyName("imperfect_report_rule")]
    public required string ImperfectReportRule { get; init; }
}
