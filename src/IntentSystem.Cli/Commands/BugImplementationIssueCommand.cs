namespace IntentSystem.Cli.Commands;

internal static class BugImplementationIssueCommand
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
            BugImplementationIssueRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugImplementationIssueCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug implementation-issue command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var implementationRepairRef = $".intent-cli/bugs/{bugId}.implementation-repair.yaml";
        var implementationRepairPath = ResolveExistingArtifactPath(
            context.RepoRoot,
            implementationRepairRef,
            "Bug implementation-repair artifact");

        var repair = BugImplementationRepairArtifactYaml.Deserialize(File.ReadAllText(implementationRepairPath));
        if (!string.Equals(repair.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug implementation-repair artifact bug id '{repair.BugId}' does not match requested bug id '{bugId}'.");
        }

        string? createdIssueUrl = null;
        int? createdIssueNumber = null;

        if (repair.ReadyToIssueCut)
        {
            var targetRepo = ResolveSingleGitHubTargetRepo(context.RepoRoot, repair.ImplementationRepairTargets);
            var body = BuildIssueBody(repair);
            var linkedIssue = PublisherFactory().CreateIssue(targetRepo, repair.SuggestedIssueTitle, body);
            createdIssueUrl = linkedIssue.Url;
            createdIssueNumber = linkedIssue.Number;
        }

        var artifact = new BugImplementationIssueArtifact
        {
            BugId = bugId,
            ImplementationRepairRef = implementationRepairRef,
            CreatedIssueTitle = repair.SuggestedIssueTitle,
            CreatedIssueUrl = createdIssueUrl,
            CreatedIssueNumber = createdIssueNumber,
            ImplementationRepairTargets = repair.ImplementationRepairTargets
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugImplementationIssueCommandResult
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

    private static string ResolveSingleGitHubTargetRepo(string repoRoot, IReadOnlyList<string> implementationRepairTargets)
    {
        if (implementationRepairTargets.Count == 0)
        {
            throw new InvalidOperationException("Implementation repair targets must contain at least one packet ref.");
        }

        var targetRepos = new List<string>();
        foreach (var target in implementationRepairTargets)
        {
            var packetPath = Path.GetFullPath(Path.Combine(repoRoot, target.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(packetPath))
            {
                throw new InvalidOperationException($"Implementation repair target packet was not found at {packetPath}");
            }

            var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
            if (string.IsNullOrWhiteSpace(packet.TargetRepo))
            {
                throw new InvalidOperationException("Projection packet must contain a target repo.");
            }

            targetRepos.Add(
                GitHubRepositoryTargetResolver.Resolve(
                    repoRoot,
                    packet.TargetRepo,
                    GitCommandRunnerFactory()));
        }

        var distinctRepos = targetRepos.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctRepos.Length != 1)
        {
            throw new InvalidOperationException(
                $"Implementation repair targets must resolve to exactly one child repo, but resolved: {string.Join(", ", distinctRepos)}");
        }

        return distinctRepos[0];
    }

    private static string BuildIssueBody(BugImplementationRepairArtifact repair)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"# {repair.SuggestedIssueTitle}",
                string.Empty,
                repair.SuggestedGoal,
                string.Empty,
                "## Implementation Repair Targets",
                ..repair.ImplementationRepairTargets.Select(target => $"- {target}")
            ]);
    }

    private static string WriteArtifact(string repoRoot, BugImplementationIssueArtifact artifact)
    {
        var relativePath = BugImplementationIssueArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug implementation-issue artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugImplementationIssueArtifactYaml.Serialize(artifact));

        return relativePath;
    }
}
