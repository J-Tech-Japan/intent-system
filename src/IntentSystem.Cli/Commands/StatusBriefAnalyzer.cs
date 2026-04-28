using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only state collector that turns local intent-cli files into a
/// <see cref="StatusBriefSummary"/> for the AI tasking thread (G179).
/// Never mutates queue state, runs, GitHub, or source files.
/// </summary>
internal static class StatusBriefAnalyzer
{
    private const int RecentEventLimit = 3;

    public static StatusBriefSummary Analyze(CliContext context, string? domainOverride)
    {
        ArgumentNullException.ThrowIfNull(context);

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        var notes = new List<string>();
        var queueStatePath = context.GetQueueStatePath();
        var queueStatePresent = File.Exists(queueStatePath);
        var queueStateReadable = false;
        QueueState? queueState = null;

        if (queueStatePresent)
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
                queueStateReadable = true;
            }
            catch (JsonException jsonException)
            {
                notes.Add($"queue-state JSON could not be parsed: {jsonException.Message}");
            }
            catch (InvalidOperationException invalidOperation)
            {
                notes.Add($"queue-state payload was invalid: {invalidOperation.Message}");
            }
        }
        else
        {
            notes.Add($"no queue-state file at {queueStatePath}");
        }

        var inFlight = new List<string>();
        var reviewUnits = new List<string>();
        string? nextCandidate = null;

        if (queueState is not null)
        {
            var completed = new HashSet<string>(
                queueState.Items
                    .Where(item => item.State == QueueItemState.Completed)
                    .Select(item => item.ExecutionUnit),
                StringComparer.Ordinal);

            foreach (var item in queueState.Items)
            {
                switch (item.State)
                {
                    case QueueItemState.Review:
                        reviewUnits.Add(item.ExecutionUnit);
                        inFlight.Add(item.ExecutionUnit);
                        break;
                    case QueueItemState.Active:
                    case QueueItemState.Fixing:
                        inFlight.Add(item.ExecutionUnit);
                        break;
                }
            }

            // Deterministic next candidate: first Queued item whose dependencies
            // are all satisfied within the local queue. We keep queue-state's
            // declared ordering — no priority sort — so the brief is stable.
            nextCandidate = queueState.Items
                .Where(item => item.State == QueueItemState.Queued)
                .Where(item => DependenciesSatisfied(item, completed))
                .Select(item => item.ExecutionUnit)
                .FirstOrDefault();
        }

        var clarificationPath = ResolveClarificationPath(context, domain);
        var clarificationOpen = false;
        if (clarificationPath is not null && File.Exists(clarificationPath))
        {
            var content = File.ReadAllText(clarificationPath);
            clarificationOpen = HasOpenBlocker(content);
        }

        var recentEvents = LoadRecentEvents(context, notes);

        var wipPresent = inFlight.Count > 0;
        var recommended = ComputeRecommendation(
            queueStatePresent,
            queueStateReadable,
            clarificationOpen,
            reviewUnits.Count > 0,
            wipPresent,
            nextCandidate is not null);

        return new StatusBriefSummary
        {
            Domain = domain,
            QueueStatePath = queueStatePath,
            QueueStatePresent = queueStatePresent,
            QueueStateReadable = queueStateReadable,
            InFlightUnits = inFlight,
            ReviewUnits = reviewUnits,
            WipPresent = wipPresent,
            ClarificationOpenPath = clarificationPath,
            ClarificationOpen = clarificationOpen,
            NextCandidate = nextCandidate,
            RecentEvents = recentEvents,
            RecommendedAction = recommended,
            Notes = notes
        };
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

    /// <summary>
    /// Parent-host clarification files (<c>intents/&lt;domain&gt;/clarifications/open.md</c>)
    /// are durable markdown artifacts that remain non-empty even when there is
    /// no root blocker. The host shape places live blockers as list items under a
    /// "## Current Open Blockers" heading, with an explicit no-blocker sentinel
    /// list item when nothing is currently blocking. Detect open blockers
    /// structurally so that a no-blocker file does not falsely force the
    /// <c>clarification-required</c> recommendation (G179 review fix).
    /// </summary>
    /// <remarks>
    /// Conservative behaviour: if no <c>## Current Open Blockers</c> section is
    /// present, treat the file as having no open blocker. The caller already
    /// records degraded notes for missing files; the AI tasking thread can still
    /// inspect manually if it suspects the file shape has drifted.
    /// </remarks>
    private static bool HasOpenBlocker(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inBlockerSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                inBlockerSection = string.Equals(
                    heading,
                    "Current Open Blockers",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inBlockerSection)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal)
                && !trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                continue;
            }

            var item = trimmed[2..].Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (IsNoBlockerSentinel(item))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsNoBlockerSentinel(string item)
    {
        // Host-established no-blocker sentinel (Japanese). Match on the substring
        // so any minor surrounding wording (e.g. trailing punctuation, links) is
        // still treated as a non-actionable entry.
        const string japaneseSentinel = "現時点で child issue cut を要する root blocker はない";
        if (item.Contains(japaneseSentinel, StringComparison.Ordinal))
        {
            return true;
        }

        // Defensive English fallback for any future host that uses a parallel
        // English wording. Kept narrow on purpose so unrelated bullets do not
        // accidentally suppress real blockers.
        return item.Contains(
            "no root blocker requiring child issue cut",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveClarificationPath(CliContext context, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot)
            ? context.RepoRoot
            : parentRoot;

        return Path.Combine(baseRoot!, "intents", domain, "clarifications", "open.md");
    }

    private static IReadOnlyList<StatusBriefRecentEvent> LoadRecentEvents(
        CliContext context,
        List<string> notes)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return [];
        }

        try
        {
            var content = File.ReadAllText(runLogPath);
            var events = RunLogSerializer.DeserializeAll(content);
            return events
                .TakeLast(RecentEventLimit)
                .Select(runEvent => new StatusBriefRecentEvent
                {
                    Timestamp = runEvent.Ts.ToString("O"),
                    ExecutionUnit = runEvent.ExecutionUnit,
                    Event = runEvent.Event,
                    By = runEvent.By
                })
                .ToArray();
        }
        catch (Exception exception) when (
            exception is JsonException
            or InvalidOperationException
            or FormatException)
        {
            notes.Add($"runs.jsonl could not be parsed: {exception.Message}");
            return [];
        }
    }

    private static string ComputeRecommendation(
        bool queueStatePresent,
        bool queueStateReadable,
        bool clarificationOpen,
        bool reviewPresent,
        bool wipPresent,
        bool hasNextCandidate)
    {
        // Deterministic precedence — top-most match wins.
        if (queueStatePresent && !queueStateReadable)
        {
            return StatusBriefRecommendation.InspectManually;
        }

        if (clarificationOpen)
        {
            return StatusBriefRecommendation.ClarificationRequired;
        }

        if (reviewPresent)
        {
            return StatusBriefRecommendation.ReviewCloseout;
        }

        if (wipPresent)
        {
            return StatusBriefRecommendation.SkipDueToWip;
        }

        if (hasNextCandidate)
        {
            return StatusBriefRecommendation.IssueCutReady;
        }

        return StatusBriefRecommendation.NoActionableItem;
    }
}
