using System.Globalization;

namespace IntentSystem.Cli.Commands;

internal static class BugImplementationRepairRenderer
{
    public static void WriteSummary(
        TextWriter writer,
        BugImplementationRepairArtifact artifact,
        string artifactPath,
        BugImplementationRepairArtifact? previousArtifact = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug implementation-repair artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Ready to issue cut: {artifact.ReadyToIssueCut.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Implementation task candidates: {artifact.ImplementationTaskCandidates.Count}");
        writer.WriteLine($"Implementation repair targets: {artifact.ImplementationRepairTargets.Count}");
        writer.WriteLine($"Suggested issue title: {artifact.SuggestedIssueTitle}");

        if (HasRecordedRepairDetails(artifact))
        {
            writer.WriteLine($"Recorded repair link: {DescribeRecordedRepair(artifact)}");
        }

        if (previousArtifact is not null && HasRecordedRepairDetails(previousArtifact))
        {
            writer.WriteLine($"Previous recorded repair link: {DescribeRecordedRepair(previousArtifact)}");
        }
    }

    private static bool HasRecordedRepairDetails(BugImplementationRepairArtifact artifact)
    {
        return !string.IsNullOrWhiteSpace(artifact.RepairExecutionUnit)
            || artifact.RepairIssueNumber is not null
            || !string.IsNullOrWhiteSpace(artifact.RepairIssueUrl)
            || !string.IsNullOrWhiteSpace(artifact.RecordedBy)
            || !string.IsNullOrWhiteSpace(artifact.Note)
            || artifact.RecordedAt is not null;
    }

    private static string DescribeRecordedRepair(BugImplementationRepairArtifact artifact)
    {
        var fields = new List<string>();
        AddField(fields, "repair_execution_unit", artifact.RepairExecutionUnit);
        AddField(
            fields,
            "repair_issue_number",
            artifact.RepairIssueNumber?.ToString(CultureInfo.InvariantCulture));
        AddField(fields, "repair_issue_url", artifact.RepairIssueUrl);
        AddField(fields, "recorded_by", artifact.RecordedBy);
        AddField(fields, "note", artifact.Note);
        AddField(
            fields,
            "recorded_at",
            artifact.RecordedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return string.Join(", ", fields);
    }

    private static void AddField(List<string> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add($"{name}={value}");
        }
    }
}
