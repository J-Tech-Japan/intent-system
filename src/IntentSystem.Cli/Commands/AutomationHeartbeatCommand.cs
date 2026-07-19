using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G526: <c>intent-cli automation heartbeat --domain &lt;d&gt; --repo &lt;r&gt;
/// [--stale-minutes &lt;m, default 45&gt;] [--format json|markdown]</c> — a
/// read-only wrapper around <see cref="AutomationStalledWorkCommand.Analyze"/>
/// that additionally emits a ready-to-send orchestrator reconcile message
/// body when anything is stale.
///
/// Field motivation: the orchestrator reconciles correctly within minutes of
/// ANY inbound wake (G524), but the two existing safety nets — a 5-minute
/// in-session orchestrator fallback timer, and a design-side watchdog —
/// both failed a real 16-day field trial (fast polling nobody wanted; a
/// watchdog living in the single most fragile, frequently-restarting
/// component). A session-independent external scheduler (cron/launchd)
/// running this command at a LOW frequency (60-minute class) survives every
/// agent restart, stays completely silent while the pipeline is healthy,
/// and caps the worst-case stall at its own interval.
///
/// This command NEVER sends a message, launches an agent, or mutates any
/// state — it only computes and emits text. A thin external wrapper
/// (documented in the orchestrator-thread guide) is responsible for piping
/// <see cref="AutomationHeartbeatResult.MessageBody"/> to <c>agmsg send.sh</c>
/// when present, at most once per run.
/// </summary>
internal static class AutomationHeartbeatCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>
    /// Default staleness threshold: below G523's typical single-message
    /// recovery latency (minutes) but well above one external-pinger
    /// interval cycle, so a healthy pipeline reconciling within its normal
    /// rhythm never trips a false alarm.
    /// </summary>
    public const int DefaultStaleMinutes = 45;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation heartbeat --domain <name> --repo <owner/repo> [--stale-minutes <m, default 45>] [--format json|markdown]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var repo, out var staleMinutes, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        AutomationStalledWorkResult stalledWork;
        try
        {
            stalledWork = AutomationStalledWorkCommand.Analyze(context, domain!, repo!, staleMinutes);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to read GitHub state for {repo}: {exception.Message}");
            return 1;
        }

        var result = new AutomationHeartbeatResult
        {
            Domain = domain!,
            Repo = repo!,
            StaleMinutesThreshold = staleMinutes,
            Stale = stalledWork.Stalled,
            Items = stalledWork.Items,
            Warnings = stalledWork.Warnings,
            MessageBody = stalledWork.Stalled ? BuildMessageBody(stalledWork) : null,
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

        return 0;
    }

    /// <summary>
    /// Builds a deterministic, ready-to-send reconcile status-request naming
    /// every stale item and its canonical next command. Per the G524 wake
    /// contract, a single message is sufficient to trigger a full recovery
    /// wake — this body is meant to be sent once per heartbeat run, verbatim,
    /// by the external wrapper.
    ///
    /// G533: the summary line and per-item framing distinguish ACTIONABLE
    /// items (a runnable command is recommended) from INFORMATIONAL ones
    /// (<see cref="StalledWorkItem.IsInformational"/> — repair-pending,
    /// rereview-pending, claimed-but-silent) so a reader — human or
    /// orchestrator — never mistakes "FYI, no transition needed" for
    /// "pending transition(s)".
    /// </summary>
    private static string BuildMessageBody(AutomationStalledWorkResult stalledWork)
    {
        var actionableCount = stalledWork.Items.Count(item => !item.IsInformational);
        var informationalCount = stalledWork.Items.Count(item => item.IsInformational);

        var builder = new StringBuilder();
        builder
            .Append("WAKE (heartbeat): ")
            .Append(actionableCount)
            .Append(" pending transition(s)");
        if (informationalCount > 0)
        {
            builder.Append(", ").Append(informationalCount).Append(" informational note(s)");
        }
        builder
            .Append(" stale >= ")
            .Append(stalledWork.StaleMinutesThreshold)
            .Append("m in ")
            .Append(stalledWork.Repo)
            .Append('.');

        foreach (var item in stalledWork.Items)
        {
            builder
                .Append('\n')
                .Append("- `")
                .Append(item.ExecutionUnit)
                .Append("` (")
                .Append(item.Kind)
                .Append(", ")
                .Append(item.AgeMinutes)
                .Append('m');

            if (item.Issue is { } issue)
            {
                builder.Append(", issue #").Append(issue.Number);
            }
            if (item.Pr is { } pr)
            {
                builder.Append(", pr #").Append(pr.Number);
            }

            builder.Append(')');
            if (item.IsInformational)
            {
                builder.Append(" — FYI: ").Append(item.RecommendedAction);
            }
            else
            {
                builder.Append(" — recommended: `").Append(item.RecommendedAction).Append('`');
            }
        }

        return builder.ToString();
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? repo,
        out int staleMinutes,
        out string format,
        out string error)
    {
        domain = null;
        repo = null;
        staleMinutes = DefaultStaleMinutes;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--stale-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedMinutes)
                        || parsedMinutes < 0)
                    {
                        error = "--stale-minutes requires a non-negative integer.";
                        return false;
                    }
                    staleMinutes = parsedMinutes;
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "automation heartbeat requires '--domain <name>'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation heartbeat requires '--repo <owner/repo>'.";
            return false;
        }
        return true;
    }

    private static void WriteMarkdown(TextWriter writer, AutomationHeartbeatResult result)
    {
        writer.WriteLine($"# automation heartbeat — `{result.Domain}` / `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- stale_minutes_threshold: {result.StaleMinutesThreshold}");
        writer.WriteLine($"- stale: {(result.Stale ? "true" : "false")}");
        writer.WriteLine($"- items: {result.Items.Count}");
        writer.WriteLine();

        if (result.Items.Count == 0)
        {
            writer.WriteLine("Healthy — nothing stale.");
        }
        else
        {
            foreach (var item in result.Items)
            {
                var kindLabel = item.IsInformational ? $"{item.Kind}, informational" : item.Kind;
                writer.WriteLine($"## `{item.ExecutionUnit}` — {kindLabel} ({item.AgeMinutes}m)");
                if (item.Issue is { } issue)
                {
                    writer.WriteLine($"- issue: #{issue.Number} — {issue.Url}");
                }
                if (item.Pr is { } pr)
                {
                    writer.WriteLine($"- pr: #{pr.Number} — {pr.Url}");
                }
                if (item.IsInformational)
                {
                    writer.WriteLine($"- status: {item.RecommendedAction}");
                }
                else
                {
                    writer.WriteLine($"- recommended_action: `{item.RecommendedAction}`");
                }
                writer.WriteLine();
            }
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
            writer.WriteLine();
        }

        if (!string.IsNullOrEmpty(result.MessageBody))
        {
            writer.WriteLine("## message_body (ready to send)");
            writer.WriteLine();
            writer.WriteLine(result.MessageBody);
        }
    }
}

internal sealed record AutomationHeartbeatResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("stale_minutes_threshold")]
    public required int StaleMinutesThreshold { get; init; }

    [JsonPropertyName("stale")]
    public required bool Stale { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<StalledWorkItem> Items { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Ready-to-send orchestrator reconcile status-request, present only
    /// when <see cref="Stale"/> is true. Never sent by this command — an
    /// external wrapper (see the orchestrator-thread guide's "External
    /// heartbeat" section) pipes this verbatim to <c>agmsg send.sh</c>, at
    /// most once per run.
    /// </summary>
    [JsonPropertyName("message_body")]
    public string? MessageBody { get; init; }
}
