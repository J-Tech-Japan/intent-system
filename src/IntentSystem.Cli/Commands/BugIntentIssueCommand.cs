namespace IntentSystem.Cli.Commands;

internal static class BugIntentIssueCommand
{
    public static Func<IQueueDispatchPublisher> PublisherFactory { get; set; } = () => new GhQueueDispatchPublisher();

    public static Func<IGitRemoteCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitRemoteCommandRunner();

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentIssueRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentIssueCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug intent-issue command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var intentRepairRef = $".intent-cli/bugs/{bugId}.intent-repair.yaml";
        var intentRepairPath = ResolveExistingArtifactPath(
            context.RepoRoot,
            intentRepairRef,
            "Bug intent-repair artifact");

        var repair = BugIntentRepairArtifactYaml.Deserialize(File.ReadAllText(intentRepairPath));
        if (!string.Equals(repair.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-repair artifact bug id '{repair.BugId}' does not match requested bug id '{bugId}'.");
        }

        string? createdIssueUrl = null;
        int? createdIssueNumber = null;

        if (repair.ReadyToIssueCut)
        {
            var targetRepo = ResolveParentGitHubTargetRepo(context, repair);
            var body = BuildIssueBody(repair);
            var linkedIssue = PublisherFactory().CreateIssue(targetRepo, repair.SuggestedIssueTitle, body);
            createdIssueUrl = linkedIssue.Url;
            createdIssueNumber = linkedIssue.Number;
        }

        var artifact = new BugIntentIssueArtifact
        {
            BugId = bugId,
            IntentRepairRef = intentRepairRef,
            CreatedIssueTitle = repair.SuggestedIssueTitle,
            CreatedIssueUrl = createdIssueUrl,
            CreatedIssueNumber = createdIssueNumber,
            ParentRepairTargets = repair.ParentRepairTargets
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugIntentIssueCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath
        };
    }

    private static string ResolveExistingArtifactPath(string repoRoot, string relativePath, string artifactLabel)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"{artifactLabel} was not found at {absolutePath}");
        }

        return absolutePath;
    }

    private static string ResolveParentGitHubTargetRepo(CliContext context, BugIntentRepairArtifact repair)
    {
        var parentRepoRoot = ParentRepairTargetRepoResolver.Resolve(context, repair.ParentRepairTargets);
        var result = GitCommandRunnerFactory().Run(parentRepoRoot, ["remote", "get-url", "origin"]);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? "git remote get-url origin failed."
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        return GitHubRepositoryTargetResolver.ParseRemoteUrl(result.StdOut.Trim());
    }

    private static string BuildIssueBody(BugIntentRepairArtifact repair)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"# {repair.SuggestedIssueTitle}",
                string.Empty,
                repair.SuggestedGoal,
                string.Empty,
                "## Parent Repair Targets",
                ..repair.ParentRepairTargets.Select(target => $"- {target}")
            ]);
    }

    private static string WriteArtifact(string repoRoot, BugIntentIssueArtifact artifact)
    {
        var relativePath = BugIntentIssueArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-issue artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentIssueArtifactYaml.Serialize(artifact));

        return relativePath;
    }
}
