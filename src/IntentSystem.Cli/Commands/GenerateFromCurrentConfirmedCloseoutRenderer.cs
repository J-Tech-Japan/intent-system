namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedCloseoutRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentConfirmedCloseoutResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current confirmed-closeout processed for domain '{result.Domain}'.");
        writer.WriteLine($"Selected closeout path: {result.SelectedCloseoutPath}");

        if (!string.IsNullOrWhiteSpace(result.ClarificationReturnArtifactPath))
        {
            writer.WriteLine($"Clarification-return artifact path: {result.ClarificationReturnArtifactPath}");
        }

        if (!string.IsNullOrWhiteSpace(result.ConfirmedReconstructionArtifactPath))
        {
            writer.WriteLine($"Confirmed reconstruction artifact path: {result.ConfirmedReconstructionArtifactPath}");
        }

        writer.WriteLine("Updated source file paths:");
        WriteList(writer, result.UpdatedSourceFilePaths);
        writer.WriteLine("Updated execution file paths:");
        WriteList(writer, result.UpdatedExecutionFilePaths);
        writer.WriteLine("Regenerated artifact paths:");
        WriteList(writer, result.RegeneratedArtifactPaths);
        writer.WriteLine("Started execution units:");
        WriteList(writer, result.StartedExecutionUnits);
        writer.WriteLine("Created issue refs:");
        WriteList(writer, result.CreatedIssueRefs);
        writer.WriteLine("Worktree paths:");
        WriteList(writer, result.WorktreePaths);
        writer.WriteLine("Generated implement request artifact paths:");
        WriteList(writer, result.ImplementRequestArtifactPaths);
        writer.WriteLine("Created PR refs:");
        WriteList(writer, result.CreatedPrRefs);
        writer.WriteLine("Review execution units:");
        WriteList(writer, result.ReviewExecutionUnits);
        writer.WriteLine("Review request artifact paths:");
        WriteList(writer, result.ReviewRequestArtifactPaths);
        writer.WriteLine("Posted comment artifact paths:");
        WriteList(writer, result.PostedCommentArtifactPaths);
        writer.WriteLine("Comment refs:");
        WriteList(writer, result.CommentRefs);
        writer.WriteLine("Fixing execution units:");
        WriteList(writer, result.FixingExecutionUnits);
        writer.WriteLine("Fix request artifact paths:");
        WriteList(writer, result.FixRequestArtifactPaths);
        writer.WriteLine("Resubmitted execution units:");
        WriteList(writer, result.ResubmittedExecutionUnits);
        writer.WriteLine("Resubmitted PR refs:");
        WriteList(writer, result.ResubmittedPrRefs);
        writer.WriteLine("Rereviewed execution units:");
        WriteList(writer, result.RereviewedExecutionUnits);
        writer.WriteLine("Rereviewed PR refs:");
        WriteList(writer, result.RereviewedPrRefs);
        writer.WriteLine("Completed execution units:");
        WriteList(writer, result.CompletedExecutionUnits);
        writer.WriteLine("Closed issue refs:");
        WriteList(writer, result.ClosedIssueRefs);
        writer.WriteLine("Merged PR refs:");
        WriteList(writer, result.MergedPrRefs);
        writer.WriteLine("Confirmed items:");
        WriteList(writer, result.ConfirmedItems);
        writer.WriteLine("Blocked items:");
        WriteList(writer, result.BlockedItems);
        writer.WriteLine($"Downstream readiness: {result.DownstreamReadiness}");
        writer.WriteLine("Skipped stages:");
        WriteList(writer, result.SkippedStages);
    }

    private static void WriteList(TextWriter writer, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var value in values)
        {
            writer.WriteLine($"- {value}");
        }
    }
}
