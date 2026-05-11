using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G318: testability seam for <c>intent-cli automation host-loop-next-action</c>.
/// Production lets the command probe <c>intent next-slice --dry-run</c>
/// in-process so the host loop never reports <c>true-idle</c> when a
/// matching next-slice candidate is ready to publish as a GitHub issue.
/// Tests inject a fake to avoid touching the live queue-state and to
/// model `issue-cut-ready` / `no-actionable-item` / `clarification-required`
/// outcomes deterministically.
/// </summary>
internal interface INextSliceDryRunProbe
{
    NextSliceProbeResult? Probe(string repo, string domain);
}

/// <summary>
/// G318: minimal structured projection of <c>intent next-slice --dry-run</c>.
/// Only the two fields the host-loop-next-action analyzer needs
/// (<see cref="RecommendedOutcome"/> + <see cref="ExecutionUnit"/>) are
/// surfaced; richer status / WIP / clarification detail stays in the
/// full <c>intent next-slice</c> output for operators who run it
/// directly.
/// </summary>
internal sealed record NextSliceProbeResult
{
    public required string RecommendedOutcome { get; init; }
    public string? ExecutionUnit { get; init; }
}

/// <summary>
/// G318: in-process invoker of <see cref="IntentNextSliceCommand"/>.
/// Reuses the host's existing <see cref="CliContext"/> (so queue-state /
/// packet directories resolve from the parent host root, not a child
/// worktree) and parses the JSON output for the analyzer.
/// </summary>
internal sealed class IntentCliNextSliceDryRunProbe : INextSliceDryRunProbe
{
    private readonly CliContext _context;

    public IntentCliNextSliceDryRunProbe(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public NextSliceProbeResult? Probe(string repo, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var args = new[]
        {
            "--dry-run",
            "--domain", domain,
            "--target-repo", repo,
            "--format", "json"
        };

        using var buffer = new StringWriter();
        int exitCode;
        try
        {
            exitCode = IntentNextSliceCommand.Execute(_context, args, buffer);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            // G318 fail-soft: the host loop must not crash when the
            // next-slice probe fails (e.g. queue-state missing, packet
            // root unreadable). Returning null falls through to the
            // existing true-idle / wip-cap-blocked classification.
            return null;
        }

        if (exitCode != 0)
        {
            return null;
        }

        var stdout = buffer.ToString();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var outcome = root.TryGetProperty("recommended_outcome", out var outcomeElement)
                && outcomeElement.ValueKind == JsonValueKind.String
                ? outcomeElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(outcome))
            {
                return null;
            }

            string? executionUnit = null;
            if (root.TryGetProperty("candidate", out var candidateElement)
                && candidateElement.ValueKind == JsonValueKind.Object
                && candidateElement.TryGetProperty("execution_unit", out var unitElement)
                && unitElement.ValueKind == JsonValueKind.String)
            {
                executionUnit = unitElement.GetString();
            }

            return new NextSliceProbeResult
            {
                RecommendedOutcome = outcome!,
                ExecutionUnit = string.IsNullOrWhiteSpace(executionUnit) ? null : executionUnit
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
