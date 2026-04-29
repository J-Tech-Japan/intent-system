using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only state collector that turns local intent-cli files plus the
/// operator-supplied question into a <see cref="ClarifyDraftPacket"/> scaffold
/// for owner review (G181). Never mutates queue state, runs, packet files,
/// clarification files, or GitHub.
/// </summary>
internal static class ClarifyDraftAnalyzer
{
    public static ClarifyDraftPacket Analyze(CliContext context, string? domainOverride, string question)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        var notes = new List<string>();
        var background = new List<string>();

        // Clarification return path
        var clarificationPath = ResolveDomainPath(context, domain, "clarifications", "open.md");
        var clarificationOpen = false;
        if (clarificationPath is not null)
        {
            if (File.Exists(clarificationPath))
            {
                var content = File.ReadAllText(clarificationPath);
                clarificationOpen = ClarificationOpenDetector.HasOpenBlocker(content);
                background.Add(
                    $"Clarification return path: {clarificationPath} (open blocker: {(clarificationOpen ? "yes" : "no")})");
            }
            else
            {
                notes.Add($"no clarification file at {clarificationPath}");
                background.Add($"Clarification return path: {clarificationPath} (file not present)");
            }
        }
        else
        {
            background.Add("Clarification return path: -");
        }

        // Queue focus
        var queueStatePath = context.GetQueueStatePath();
        if (File.Exists(queueStatePath))
        {
            try
            {
                var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
                AppendFocusBackground(queueState, background);
            }
            catch (Exception exception) when (
                exception is JsonException
                or InvalidOperationException)
            {
                notes.Add($"queue-state could not be parsed: {exception.Message}");
            }
        }
        else
        {
            notes.Add($"no queue-state file at {queueStatePath}");
        }

        // Recent events
        var runLogPath = context.GetRunLogPath();
        if (File.Exists(runLogPath))
        {
            try
            {
                var content = File.ReadAllText(runLogPath);
                var events = RunLogSerializer.DeserializeAll(content);
                var recent = events.TakeLast(3).ToArray();
                if (recent.Length > 0)
                {
                    background.Add("Recent run events:");
                    foreach (var runEvent in recent)
                    {
                        background.Add(
                            $"- {runEvent.Ts:O} {runEvent.ExecutionUnit} {runEvent.Event} (by={runEvent.By})");
                    }
                }
            }
            catch (Exception exception) when (
                exception is JsonException
                or InvalidOperationException
                or FormatException)
            {
                notes.Add($"runs.jsonl could not be parsed: {exception.Message}");
            }
        }

        // Two-option scaffold so the AI tasking thread fills in deterministically.
        var options = new[]
        {
            new ClarifyDraftOption
            {
                Label = "A",
                Description = "(operator to fill)",
                Pros = new List<string>(),
                Cons = new List<string>()
            },
            new ClarifyDraftOption
            {
                Label = "B",
                Description = "(operator to fill)",
                Pros = new List<string>(),
                Cons = new List<string>()
            }
        };

        return new ClarifyDraftPacket
        {
            Domain = domain,
            Question = question.Trim(),
            Background = background,
            Options = options,
            Recommendation = null,
            ReturnPath = clarificationPath,
            Notes = notes
        };
    }

    private static void AppendFocusBackground(QueueState queueState, List<string> background)
    {
        var inFlightUnits = queueState.Items
            .Where(item => item.State is QueueItemState.Active
                or QueueItemState.Review
                or QueueItemState.Fixing)
            .Select(item => $"{item.ExecutionUnit} ({FormatState(item.State)})")
            .ToArray();

        background.Add(inFlightUnits.Length == 0
            ? "In-flight units: -"
            : $"In-flight units: {string.Join(", ", inFlightUnits)}");

        var completed = new HashSet<string>(
            queueState.Items
                .Where(item => item.State == QueueItemState.Completed)
                .Select(item => item.ExecutionUnit),
            StringComparer.Ordinal);

        var nextCandidate = queueState.Items
            .Where(item => item.State == QueueItemState.Queued)
            .Where(item => DependenciesSatisfied(item, completed))
            .Select(item => item.ExecutionUnit)
            .FirstOrDefault();

        background.Add($"Next candidate: {nextCandidate ?? "-"}");
    }

    private static bool DependenciesSatisfied(QueueItem item, HashSet<string> completed)
    {
        if (item.Dependencies.Count == 0)
        {
            return true;
        }

        foreach (var dependency in item.Dependencies)
        {
            if (!completed.Contains(dependency))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatState(QueueItemState state) => state switch
    {
        QueueItemState.Queued => "queued",
        QueueItemState.Active => "active",
        QueueItemState.Review => "review",
        QueueItemState.Fixing => "fixing",
        QueueItemState.ClarifyBlocked => "clarify-blocked",
        QueueItemState.Blocked => "blocked",
        QueueItemState.Completed => "completed",
        _ => state.ToString().ToLowerInvariant()
    };

    private static string? ResolveDomainPath(CliContext context, string domain, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot)
            ? context.RepoRoot
            : parentRoot;

        var combined = new List<string> { baseRoot!, "intents", domain };
        combined.AddRange(parts);
        return Path.Combine(combined.ToArray());
    }
}
