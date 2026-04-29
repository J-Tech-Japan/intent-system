namespace IntentSystem.Cli.Commands;

using Actions = TaskingHandoffBundleImportDryRunConstants.Actions;
using CurrentStatuses = TaskingHandoffBundleImportDryRunConstants.CurrentStatuses;
using Roles = TaskingHandoffBundleImportDryRunConstants.Roles;
using VerifyCheckIds = TaskingHandoffBundleVerifyConstants.CheckIds;

/// <summary>
/// G198 — pure logic that turns a deserialized G194 bundle plus a G196/G197
/// verify check list into a deterministic restore plan. No I/O, no network,
/// no process launches. The command layer is responsible for reading the
/// bundle, observing referenced files, and dispatching to
/// <see cref="TaskingHandoffBundleVerifyAnalyzer.BuildChecks"/>; this analyzer
/// then projects those check results into the per-role plan entries.
///
/// The restore plan is descriptive: it never performs the listed actions and
/// the bundle does not carry artifact bytes, only paths and sha256 values.
/// See <see cref="TaskingHandoffBundleImportDryRunConstants.RestorePlanDisclaimer"/>.
/// </summary>
internal static class TaskingHandoffBundleImportDryRunAnalyzer
{
    /// <summary>
    /// Build the four-entry restore plan in the canonical order
    /// (task_packet, preview, checklist, handoff). Caller is responsible for
    /// only invoking this when the verify result is <c>valid == true</c>;
    /// when verify failed, the command layer emits an empty plan instead.
    /// </summary>
    public static IReadOnlyList<RestorePlanEntry> BuildRestorePlan(
        TaskingHandoffBundleArtifact bundle,
        IReadOnlyList<VerifyCheck> verifyChecks)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(verifyChecks);

        return new[]
        {
            BuildEntry(
                Roles.TaskPacket,
                bundle.SourceTaskPacketPath,
                bundle.SourceTaskPacketSha256,
                verifyChecks,
                VerifyCheckIds.TaskPacketFileExists,
                VerifyCheckIds.TaskPacketHashMatches),
            BuildEntry(
                Roles.Preview,
                bundle.SourcePreviewPath,
                bundle.SourcePreviewSha256,
                verifyChecks,
                VerifyCheckIds.PreviewFileExists,
                VerifyCheckIds.PreviewHashMatches),
            BuildEntry(
                Roles.Checklist,
                bundle.SourceChecklistPath,
                bundle.SourceChecklistSha256,
                verifyChecks,
                VerifyCheckIds.ChecklistFileExists,
                VerifyCheckIds.ChecklistHashMatches),
            BuildEntry(
                Roles.Handoff,
                bundle.SourceHandoffPath,
                bundle.SourceHandoffSha256,
                verifyChecks,
                VerifyCheckIds.HandoffFileExists,
                VerifyCheckIds.HandoffHashMatches)
        };
    }

    /// <summary>
    /// Project the failing-check ids out of a verify check list, preserving
    /// the analyzer's stable order. Used both for the
    /// <see cref="TaskingHandoffBundleImportDryRunVerifySummary.FailedCheckIds"/>
    /// projection and for the human-readable error list.
    /// </summary>
    public static IReadOnlyList<string> ExtractFailedCheckIds(IReadOnlyList<VerifyCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var ids = new List<string>();
        foreach (var check in checks)
        {
            if (!check.Passed)
            {
                ids.Add(check.Id);
            }
        }

        return ids;
    }

    private static RestorePlanEntry BuildEntry(
        string role,
        string? recordedPath,
        string? recordedSha256,
        IReadOnlyList<VerifyCheck> verifyChecks,
        string fileExistsId,
        string hashMatchesId)
    {
        var fileExistsCheck = FindCheck(verifyChecks, fileExistsId);
        var hashMatchesCheck = FindCheck(verifyChecks, hashMatchesId);

        var (currentStatus, action) = ResolveStatusAndAction(fileExistsCheck, hashMatchesCheck);

        return new RestorePlanEntry
        {
            Role = role,
            RecordedPath = recordedPath ?? string.Empty,
            RecordedSha256 = recordedSha256 ?? string.Empty,
            CurrentStatus = currentStatus,
            DryRunAction = action
        };
    }

    private static (string currentStatus, string action) ResolveStatusAndAction(
        VerifyCheck? fileExistsCheck,
        VerifyCheck? hashMatchesCheck)
    {
        var fileExists = fileExistsCheck?.Passed ?? false;
        var hashMatches = hashMatchesCheck?.Passed ?? false;

        if (fileExists && hashMatches)
        {
            return (CurrentStatuses.AlreadyPresentAndMatching, Actions.NoOpAlreadyMatches);
        }

        if (fileExists && !hashMatches)
        {
            return (CurrentStatuses.PresentButHashDiffers, Actions.WouldOverwriteWithRecordedContent);
        }

        return (CurrentStatuses.Missing, Actions.WouldRecreateFromRecordedContent);
    }

    private static VerifyCheck? FindCheck(IReadOnlyList<VerifyCheck> checks, string id)
    {
        foreach (var check in checks)
        {
            if (string.Equals(check.Id, id, StringComparison.Ordinal))
            {
                return check;
            }
        }

        return null;
    }
}
