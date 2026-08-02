using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G215: <c>intent-cli automation clarification-stop</c> renders a stable,
/// read-only summary for local automation wakes that must stop for owner
/// clarification instead of guessing or mutating labels.
/// </summary>
internal static class AutomationClarificationStopCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>Test sentinel: this command must never launch a provider.</summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

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

        if (!TryParseArguments(
                args,
                out var kind,
                out var repoOverride,
                out var workdir,
                out var number,
                out var targetUrl,
                out var reason,
                out var recommendedOwnerAction,
                out var cooldown,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var repo = repoOverride;
        if (string.IsNullOrWhiteSpace(targetUrl) && string.IsNullOrWhiteSpace(repo))
        {
            var resolvedWorkdir = WorkdirResolver.Resolve(context, workdir);
            if (!AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
            {
                writer.WriteLine(error);
                return 1;
            }
        }

        targetUrl ??= BuildTargetUrl(repo!, kind!, number);
        var result = new AutomationClarificationStopResult
        {
            Kind = kind!,
            Status = WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
            TargetNumber = number,
            TargetUrl = targetUrl,
            Reason = reason!,
            RecommendedOwnerAction = recommendedOwnerAction!,
            Cooldown = cooldown,
            Mutated = false,
            Warnings = Array.Empty<string>(),
            Summary = $"Clarification required for {kind} target #{number}: {reason}",
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string? repo,
        out string? workdir,
        out int number,
        out string? targetUrl,
        out string? reason,
        out string? recommendedOwnerAction,
        out string? cooldown,
        out string format,
        out string error)
    {
        kind = null;
        repo = null;
        workdir = null;
        number = 0;
        targetUrl = null;
        reason = null;
        recommendedOwnerAction = null;
        cooldown = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value (issue-to-pr or pr-comment-fix).";
                        return false;
                    }
                    kind = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1].Trim();
                    index++;
                    break;

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;

                case "--number":
                case "--issue":
                case "--pr":
                    if (!TryReadPositiveInt(args, index, argument, out number, out error))
                    {
                        return false;
                    }
                    index++;
                    break;

                case "--url":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--url requires a value.";
                        return false;
                    }
                    targetUrl = args[index + 1].Trim();
                    index++;
                    break;

                case "--reason":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--reason requires a value.";
                        return false;
                    }
                    reason = args[index + 1].Trim();
                    index++;
                    break;

                case "--recommended-owner-action":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--recommended-owner-action requires a value.";
                        return false;
                    }
                    recommendedOwnerAction = args[index + 1].Trim();
                    index++;
                    break;

                case "--cooldown":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--cooldown requires a value.";
                        return false;
                    }
                    cooldown = args[index + 1].Trim();
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --kind <issue-to-pr|pr-comment-fix> --number <n> [--url <url>] [--repo <owner/repo>] [--workdir <path>] --reason <text> --recommended-owner-action <text> [--cooldown <text>] [--format text|json].";
                    return false;
            }
        }

        if (!string.Equals(kind, WorkerResultSummaryConstants.Kinds.IssueToPr, StringComparison.Ordinal)
            && !string.Equals(kind, WorkerResultSummaryConstants.Kinds.PrCommentFix, StringComparison.Ordinal))
        {
            error = "--kind must be 'issue-to-pr' or 'pr-comment-fix'.";
            return false;
        }
        if (number <= 0)
        {
            error = "--number is required and must be a positive integer.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            error = "--reason is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(recommendedOwnerAction))
        {
            error = "--recommended-owner-action is required.";
            return false;
        }

        return true;
    }

    private static bool TryReadPositiveInt(
        string[] args,
        int index,
        string option,
        out int value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (index + 1 >= args.Length
            || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            || value <= 0)
        {
            error = $"{option} requires a positive integer.";
            return false;
        }
        return true;
    }

    private static string BuildTargetUrl(string repo, string kind, int number)
    {
        var pathPart = string.Equals(kind, WorkerResultSummaryConstants.Kinds.IssueToPr, StringComparison.Ordinal)
            ? "issues"
            : "pull";
        return $"https://github.com/{repo}/{pathPart}/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static void WriteText(TextWriter writer, AutomationClarificationStopResult result)
    {
        writer.WriteLine($"# Automation clarification stop ({result.Kind})");
        writer.WriteLine();
        writer.WriteLine($"- status: {result.Status}");
        writer.WriteLine($"- target: #{result.TargetNumber}");
        writer.WriteLine($"- url: {result.TargetUrl}");
        writer.WriteLine($"- reason: {result.Reason}");
        writer.WriteLine($"- recommended_owner_action: {result.RecommendedOwnerAction}");
        if (!string.IsNullOrWhiteSpace(result.Cooldown))
        {
            writer.WriteLine($"- cooldown: {result.Cooldown}");
        }
        writer.WriteLine($"- mutated: {result.Mutated}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }
}

internal sealed record AutomationClarificationStopResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("target_number")]
    public required int TargetNumber { get; init; }

    [JsonPropertyName("targetNumber")]
    public int TargetNumberCamelCase => TargetNumber;

    [JsonPropertyName("target_url")]
    public required string TargetUrl { get; init; }

    [JsonPropertyName("targetUrl")]
    public string TargetUrlCamelCase => TargetUrl;

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("recommended_owner_action")]
    public required string RecommendedOwnerAction { get; init; }

    [JsonPropertyName("recommendedOwnerAction")]
    public string RecommendedOwnerActionCamelCase => RecommendedOwnerAction;

    [JsonPropertyName("cooldown")]
    public string? Cooldown { get; init; }

    [JsonPropertyName("mutated")]
    public required bool Mutated { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
