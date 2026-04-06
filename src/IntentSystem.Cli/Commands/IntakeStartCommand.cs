namespace IntentSystem.Cli.Commands;

internal static class IntakeStartCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake start command requires a domain.");
            return 1;
        }

        var domain = args[0].Trim();

        try
        {
            var issueResult = IntakeIssueCommand.ExecuteCore(context.RepoRoot, domain);
            var launchResult = IntakeLaunchCommand.ExecuteCore(context, domain, writer);
            var result = new IntakeStartResult
            {
                Domain = domain,
                StartedExecutionUnits = launchResult.LaunchedExecutionUnits,
                GeneratedArtifactPaths = issueResult.ArtifactPaths,
                CreatedIssueRefs = launchResult.CreatedIssueRefs,
                WorktreePaths = launchResult.WorktreePaths,
                SkippedUnits = launchResult.SkippedUnits
            };

            IntakeStartRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
