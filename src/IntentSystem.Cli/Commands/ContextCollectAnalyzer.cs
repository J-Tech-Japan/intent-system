using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only state collector that turns local intent-cli files plus parent-host
/// domain artifacts into a <see cref="ContextCollectPacket"/> for the AI tasking
/// thread (G180). Never mutates queue state, runs, packet files, source files,
/// or GitHub.
/// </summary>
internal static class ContextCollectAnalyzer
{
    private const int RecentEventLimit = 5;
    private const int MarkdownExcerptCharLimit = 1500;

    public static ContextCollectPacket Analyze(
        CliContext context,
        string? domainOverride,
        IReadOnlyList<string>? scopeHints = null,
        IReadOnlyCollection<string>? facetFilter = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        var notes = new List<string>();

        // G530: same domain-root resolution the rest of this analyzer
        // already uses for clarification/automation-bindings paths (parent
        // host root when configured, else the local repo) — a child
        // implementation workspace's `intent_references`/`--scope` hints
        // and facet nodes both live under the PARENT host's intent tree.
        var facetDomainRoot = ResolveDomainPath(context, domain);
        var facetSelection = facetDomainRoot is null
            ? new FacetContextSelection
              {
                  Groups = Array.Empty<FacetContextGroup>(),
                  DomainHasAnyFacetNodes = false,
                  Warnings = Array.Empty<FacetContextWarning>(),
                  ScopeWarnings = Array.Empty<FacetScopeWarning>(),
                  AllScopeHintsRejected = false,
              }
            : FacetContextSelector.Select(facetDomainRoot, domain, scopeHints, facetFilter);
        var facetContextNote = facetSelection.DomainHasAnyFacetNodes
            ? null
            : $"no facet-annotated nodes found under intents/{domain}/ — facets (G529) are optional and this is the norm before a tree adopts them.";

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
        QueueItem? focusItem = null;

        if (queueState is not null)
        {
            var completed = new HashSet<string>(
                queueState.Items
                    .Where(item => item.State == QueueItemState.Completed)
                    .Select(item => item.ExecutionUnit),
                StringComparer.Ordinal);

            QueueItem? firstReview = null;
            QueueItem? firstActiveOrFixing = null;

            foreach (var item in queueState.Items)
            {
                switch (item.State)
                {
                    case QueueItemState.Review:
                        reviewUnits.Add(item.ExecutionUnit);
                        inFlight.Add(item.ExecutionUnit);
                        firstReview ??= item;
                        break;
                    case QueueItemState.Active:
                    case QueueItemState.Fixing:
                        inFlight.Add(item.ExecutionUnit);
                        firstActiveOrFixing ??= item;
                        break;
                }
            }

            var nextCandidateItem = queueState.Items
                .Where(item => item.State == QueueItemState.Queued)
                .Where(item => DependenciesSatisfied(item, completed))
                .FirstOrDefault();

            nextCandidate = nextCandidateItem?.ExecutionUnit;

            // Focus precedence: review (closeout-ready), then active/fixing,
            // then deps-satisfied next candidate. This mirrors the G179 brief
            // recommendation precedence so the two commands stay aligned.
            focusItem = firstReview ?? firstActiveOrFixing ?? nextCandidateItem;
        }

        var clarificationPath = ResolveDomainPath(context, domain, "clarifications", "open.md");
        var clarificationOpen = false;
        string? clarificationExcerpt = null;
        if (clarificationPath is not null && File.Exists(clarificationPath))
        {
            var content = File.ReadAllText(clarificationPath);
            clarificationOpen = ClarificationOpenDetector.HasOpenBlocker(content);
            clarificationExcerpt = TakeMarkdownExcerpt(content);
        }
        else if (clarificationPath is not null)
        {
            notes.Add($"no clarification file at {clarificationPath}");
        }

        var automationBindingsPath = ResolveDomainPath(context, domain, "automation", "bindings.md");
        var automationBindingsPresent = false;
        string? automationBindingsExcerpt = null;
        if (automationBindingsPath is not null && File.Exists(automationBindingsPath))
        {
            automationBindingsPresent = true;
            automationBindingsExcerpt = TakeMarkdownExcerpt(File.ReadAllText(automationBindingsPath));
        }
        else if (automationBindingsPath is not null)
        {
            notes.Add($"no automation bindings file at {automationBindingsPath}");
        }

        var focusPacket = focusItem is null ? null : BuildFocusPacket(context, focusItem);
        var recentEvents = LoadRecentEvents(context, notes);

        return new ContextCollectPacket
        {
            Domain = domain,
            FacetContext = facetSelection.Groups,
            FacetContextWarnings = facetSelection.Warnings,
            FacetContextScopeWarnings = facetSelection.ScopeWarnings,
            FacetContextAllScopeHintsRejected = facetSelection.AllScopeHintsRejected,
            FacetContextNote = facetContextNote,
            QueueStatePath = queueStatePath,
            QueueStatePresent = queueStatePresent,
            QueueStateReadable = queueStateReadable,
            InFlightUnits = inFlight,
            ReviewUnits = reviewUnits,
            NextCandidate = nextCandidate,
            FocusUnit = focusItem?.ExecutionUnit,
            FocusPacket = focusPacket,
            ClarificationOpenPath = clarificationPath,
            ClarificationOpen = clarificationOpen,
            ClarificationExcerpt = clarificationExcerpt,
            AutomationBindingsPath = automationBindingsPath,
            AutomationBindingsPresent = automationBindingsPresent,
            AutomationBindingsExcerpt = automationBindingsExcerpt,
            RecentEvents = recentEvents,
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

    private static ContextCollectPacketReference BuildFocusPacket(CliContext context, QueueItem item)
    {
        var implementationPath = ResolvePacketPath(context, item.PacketPaths.Implementation);
        var reviewContextPath = ResolvePacketPath(context, item.PacketPaths.ReviewContext);
        var yamlPath = ResolvePacketPath(context, item.PacketPaths.Yaml);

        return new ContextCollectPacketReference
        {
            ImplementationPath = implementationPath,
            ImplementationPresent = File.Exists(implementationPath),
            ReviewContextPath = reviewContextPath,
            ReviewContextPresent = File.Exists(reviewContextPath),
            YamlPath = yamlPath,
            YamlPresent = File.Exists(yamlPath)
        };
    }

    private static string ResolvePacketPath(CliContext context, string relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.GetFullPath(Path.Combine(context.RepoRoot, relativeOrAbsolute));
    }

    private static IReadOnlyList<ContextCollectRecentEvent> LoadRecentEvents(
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
                .Select(runEvent => new ContextCollectRecentEvent
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

    private static string TakeMarkdownExcerpt(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (content.Length <= MarkdownExcerptCharLimit)
        {
            return content;
        }

        return content[..MarkdownExcerptCharLimit] + "\n…(truncated)";
    }
}
