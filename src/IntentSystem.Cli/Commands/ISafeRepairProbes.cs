using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G342: testability seam for the auto-probe of
/// <c>intent-cli automation publish-recovery --format json</c>.
/// Production uses <see cref="IntentCliPublishRecoveryProbe"/>; tests
/// inject a fake to model recoverable / unsafe-stop / no-repairs
/// outcomes without touching live queue-state.
/// </summary>
internal interface IPublishRecoveryProbe
{
    PublishRecoveryProbeResult? Probe(string repo);
}

/// <summary>
/// G342: minimal projection of the publish-recovery dry-run output.
/// Only the counts the host-loop analyzer needs surface here; richer
/// per-repair detail stays in the full command output for operators
/// who run it directly.
/// </summary>
internal sealed record PublishRecoveryProbeResult
{
    public required int SafeRepairCount { get; init; }
    public required int UnsafeStopCount { get; init; }
}

/// <summary>
/// G342: in-process invoker of <see cref="AutomationPublishRecoveryCommand"/>.
/// Reuses the host's existing <see cref="CliContext"/> so queue-state
/// resolves from the parent host root, not a child worktree. Fail-soft:
/// returns <see langword="null"/> on any error so the host loop never
/// crashes when publish-recovery itself is unhealthy.
/// </summary>
internal sealed class IntentCliPublishRecoveryProbe : IPublishRecoveryProbe
{
    private readonly CliContext _context;

    public IntentCliPublishRecoveryProbe(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public PublishRecoveryProbeResult? Probe(string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var args = new[]
        {
            "--repo", repo,
            "--dry-run",
            "--format", "json"
        };

        using var buffer = new StringWriter();
        int exitCode;
        try
        {
            exitCode = AutomationPublishRecoveryCommand.Execute(_context, args, buffer);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
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

            var safeRepairCount = 0;
            if (root.TryGetProperty("safe_repairs", out var safeRepairsElement)
                && safeRepairsElement.ValueKind == JsonValueKind.Array)
            {
                safeRepairCount = safeRepairsElement.GetArrayLength();
            }

            var unsafeStopCount = 0;
            if (root.TryGetProperty("unsafe_stops", out var unsafeStopsElement)
                && unsafeStopsElement.ValueKind == JsonValueKind.Array)
            {
                unsafeStopCount = unsafeStopsElement.GetArrayLength();
            }

            return new PublishRecoveryProbeResult
            {
                SafeRepairCount = safeRepairCount,
                UnsafeStopCount = unsafeStopCount
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// G342: testability seam for the auto-probe of
/// <c>intent-cli automation host-sync-preflight --format json</c>.
/// </summary>
internal interface IHostSyncPreflightProbe
{
    HostSyncPreflightProbeResult? Probe();
}

/// <summary>
/// G342: minimal projection of the host-sync-preflight output. Only
/// the classification string the host-loop analyzer needs is exposed;
/// the full preflight report stays for operators who run the command
/// directly.
/// </summary>
internal sealed record HostSyncPreflightProbeResult
{
    public required string Classification { get; init; }
}

/// <summary>
/// G342: in-process invoker of <see cref="AutomationHostSyncPreflightCommand"/>.
/// Resolves the working tree from the host context. Fail-soft: returns
/// <see langword="null"/> on any error so the host loop falls through
/// to its existing analyzer lanes when the preflight is unhealthy.
/// </summary>
internal sealed class IntentCliHostSyncPreflightProbe : IHostSyncPreflightProbe
{
    private readonly CliContext _context;

    public IntentCliHostSyncPreflightProbe(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public HostSyncPreflightProbeResult? Probe()
    {
        var args = new[] { "--format", "json" };
        using var buffer = new StringWriter();
        int exitCode;
        try
        {
            exitCode = AutomationHostSyncPreflightCommand.Execute(_context, args, buffer);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return null;
        }

        // host-sync-preflight uses exit code 0 ONLY for the `clean`
        // classification; other classifications (behind-origin /
        // dirty-*) still produce useful JSON on stdout with a
        // non-zero exit. Probe the JSON regardless of exit code.
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

            if (root.TryGetProperty("classification", out var classificationElement)
                && classificationElement.ValueKind == JsonValueKind.String)
            {
                var classification = classificationElement.GetString();
                if (!string.IsNullOrWhiteSpace(classification))
                {
                    return new HostSyncPreflightProbeResult { Classification = classification! };
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }
}
