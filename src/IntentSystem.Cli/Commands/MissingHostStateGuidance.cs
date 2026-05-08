using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G299: structured fail-closed guidance for any <c>intent-cli</c> invocation
/// that could not resolve the parent host repo's <c>.intent-cli/</c> state.
///
/// The bare <c>"Could not find .intent-cli directory"</c> error encouraged
/// agents to fall back to ordinary GitHub review or raw PR comments,
/// because the message did not name the host vs child distinction or the
/// canonical re-run path. This helper replaces the bare error with a
/// structured object (markdown by default, JSON when the caller passed
/// <c>--format json</c>) covering the four required elements:
///
/// <list type="bullet">
/// <item><description>Names the parent host repo root requirement.</description></item>
/// <item><description>Distinguishes <c>host repo cwd</c>, <c>child implementation repo cwd</c>, and <c>target GitHub repo</c>.</description></item>
/// <item><description>Says implementation findings may become PR comments, but host metadata / guidance failures must NOT.</description></item>
/// <item><description>Gives the canonical re-run instruction (re-run from the parent host repo root that owns <c>.intent-cli/</c>).</description></item>
/// </list>
///
/// Pure data + render: never invokes <c>gh</c>, never writes files, never
/// launches an AI provider.
/// </summary>
internal static class MissingHostStateGuidance
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string StatusKind = "missing-host-state";

    public static int Emit(TextWriter writer, string[] args, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var format = ParseFormat(args);
        var targetRepo = ParseTargetRepo(args);
        var commandName = ParseCommandName(args);
        var result = Build(currentDirectory, commandName, targetRepo);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 1;
    }

    internal static MissingHostStateGuidanceResult Build(
        string currentDirectory,
        string? commandName,
        string? targetRepo)
    {
        var hardRules = new[]
        {
            "Do NOT fall back to ordinary GitHub review or raw PR comments when host state is missing. Implementation findings (real code/contract gaps the implementer can fix on the PR branch) may still become PR comments; host metadata / guidance failures MUST NOT.",
            "Do NOT apply raw `gh ... edit --add-label` / `--remove-label` for workflow labels. Every workflow label transition goes through installed `intent-cli automation` / `intent-cli worker` commands.",
            "Do NOT call `intent-cli run` as a fallback. `run` is advanced runtime (integration smoke / replay / dogfooding), not the chat-first review/worker path.",
            "Do NOT ask `intent-cli` to launch Claude/Codex or any AI provider; the chat-first model has the human agent driving the conversation."
        };

        var nextSteps = new List<string>
        {
            $"Re-run `intent-cli {commandName ?? "<command>"} ...` from the parent host repo root — the directory that owns the `.intent-cli/` package and `intents/<domain>/` data.",
            "If you are currently inside a child implementation repo (e.g. a submodule checkout), `cd` to the parent host repo before retrying. Child checkouts typically do not carry their own `.intent-cli/`.",
            "If you do not know the parent host repo root, ask the operator to provide it; do not guess from prompt memory.",
            "If host state is genuinely required but unavailable, stop and surface the gap to the operator via structured clarification rather than fall back to a non-host action."
        };

        if (!string.IsNullOrWhiteSpace(targetRepo))
        {
            nextSteps.Add($"Note: the target GitHub repo `{targetRepo}` is the workflow target, NOT the host. Host data lives in a separate parent host repo on disk.");
        }

        return new MissingHostStateGuidanceResult
        {
            Status = StatusKind,
            Command = commandName,
            Cwd = currentDirectory,
            Missing = CliRuntimeContracts.IntentCliDirectoryName,
            HostRepoCwd = null,
            ChildRepoCwd = currentDirectory,
            TargetGithubRepo = targetRepo,
            Summary = $"intent-cli could not resolve the parent host repo's '{CliRuntimeContracts.IntentCliDirectoryName}/' state from '{currentDirectory}'. Fail-closed (G299) — no GitHub mutation, no PR comment, no label transition was performed.",
            HardRules = hardRules,
            NextSteps = nextSteps
        };
    }

    private static void WriteMarkdown(TextWriter writer, MissingHostStateGuidanceResult result)
    {
        writer.WriteLine($"# intent-cli — missing host state (G299)");
        writer.WriteLine();
        writer.WriteLine("**Fail-closed**: the parent host repo's `.intent-cli/` state could not be resolved from the current directory. No GitHub mutation, no PR comment, and no label transition were performed.");
        writer.WriteLine();
        writer.WriteLine("## Distinguished cwd buckets");
        writer.WriteLine($"- Host repo cwd: _unresolved_ (intent-cli looks for `{result.Missing}` here).");
        writer.WriteLine($"- Child implementation repo cwd: `{result.ChildRepoCwd}` (current cwd; child checkouts often do not carry their own `{result.Missing}`).");
        writer.WriteLine($"- Target GitHub repo: {(string.IsNullOrWhiteSpace(result.TargetGithubRepo) ? "_unspecified_" : "`" + result.TargetGithubRepo + "`")} (workflow target, not the host).");
        writer.WriteLine();
        writer.WriteLine("## Hard rules");
        foreach (var rule in result.HardRules)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();
        writer.WriteLine("## Next steps");
        foreach (var step in result.NextSteps)
        {
            writer.WriteLine($"- {step}");
        }
    }

    private static string ParseFormat(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--format", StringComparison.Ordinal)
                && index + 1 < args.Length
                && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                var requested = args[index + 1].Trim();
                if (string.Equals(requested, FormatJson, StringComparison.Ordinal))
                {
                    return FormatJson;
                }
                if (string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                {
                    return FormatMarkdown;
                }
            }
        }
        return FormatMarkdown;
    }

    private static string? ParseTargetRepo(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--repo", StringComparison.Ordinal)
                || string.Equals(args[index], "--target-repo", StringComparison.Ordinal))
            {
                var value = args[index + 1].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        return null;
    }

    private static string? ParseCommandName(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return null;
        }
        if (args.Length == 1 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("-", StringComparison.Ordinal))
        {
            return args[0].Trim();
        }
        return $"{args[0].Trim()} {args[1].Trim()}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

internal sealed record MissingHostStateGuidanceResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    [JsonPropertyName("missing")]
    public required string Missing { get; init; }

    [JsonPropertyName("host_repo_cwd")]
    public string? HostRepoCwd { get; init; }

    [JsonPropertyName("child_repo_cwd")]
    public string? ChildRepoCwd { get; init; }

    [JsonPropertyName("target_github_repo")]
    public string? TargetGithubRepo { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("hard_rules")]
    public required IReadOnlyList<string> HardRules { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }
}
