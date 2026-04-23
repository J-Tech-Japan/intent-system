namespace IntentSystem.Cli.Commands;

internal static class IssueStatusCommand
{
    private const string DraftedPublishStatus = "drafted";
    private const string IssueCreatedPublishStatus = "issue-created";
    private const string PublishedStatus = "published";
    private const string PublishLabelName = "intent-target";

    public static Func<IQueueDispatchPublisher> PublisherFactory { get; set; } = () => new GhQueueDispatchPublisher();

    public static Func<IGitRemoteCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitRemoteCommandRunner();

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Issue status command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0].Trim());
            WriteResult(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IssueStatusCommandResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var artifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
        var absoluteArtifactPath = ResolveRepoPath(context.RepoRoot, artifactPath);
        if (!File.Exists(absoluteArtifactPath))
        {
            throw new InvalidOperationException($"Issue publish artifact was not found at {absoluteArtifactPath}");
        }

        var artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(absoluteArtifactPath));
        if (!string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact execution unit '{artifact.ExecutionUnit}' does not match requested execution unit '{executionUnit}'.");
        }

        var requiresCreatedIssue = string.Equals(artifact.PublishStatus, IssueCreatedPublishStatus, StringComparison.Ordinal)
            || string.Equals(artifact.PublishStatus, PublishedStatus, StringComparison.Ordinal);
        var hasAnyCreatedIssueMetadata = artifact.CreatedIssueNumber is not null
            || !string.IsNullOrWhiteSpace(artifact.CreatedIssueUrl);
        if ((requiresCreatedIssue || hasAnyCreatedIssueMetadata)
            && (artifact.CreatedIssueNumber is null || string.IsNullOrWhiteSpace(artifact.CreatedIssueUrl)))
        {
            throw new InvalidOperationException(
                $"Issue publish artifact for '{executionUnit}' must contain both created issue number and URL.");
        }

        IssueStatusLinkedIssue? linkedIssue = null;
        if (artifact.CreatedIssueNumber is not null && !string.IsNullOrWhiteSpace(artifact.CreatedIssueUrl))
        {
            var targetRepo = ResolveTargetRepo(context, artifact);
            var labelNames = PublisherFactory().GetIssueLabels(targetRepo, artifact.CreatedIssueNumber.Value);
            var hasIntentTargetLabel = labelNames.Contains(PublishLabelName, StringComparer.Ordinal);

            linkedIssue = new IssueStatusLinkedIssue
            {
                TargetRepo = targetRepo,
                Number = artifact.CreatedIssueNumber.Value,
                Url = artifact.CreatedIssueUrl,
                HasIntentTargetLabel = hasIntentTargetLabel
            };
        }

        return new IssueStatusCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath,
            LinkedIssue = linkedIssue,
            AutomationState = ResolveAutomationState(artifact, linkedIssue)
        };
    }

    private static string ResolveTargetRepo(CliContext context, IssuePublishArtifact artifact)
    {
        var packetPath = ResolveRepoPath(context.RepoRoot, artifact.PacketPath);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
        if (!string.Equals(packet.ExecutionUnit, artifact.ExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projection packet execution unit '{packet.ExecutionUnit}' does not match publish artifact execution unit '{artifact.ExecutionUnit}'.");
        }

        return GitHubRepositoryTargetResolver.Resolve(context.RepoRoot, packet.TargetRepo, GitCommandRunnerFactory());
    }

    private static IssueStatusAutomationState ResolveAutomationState(
        IssuePublishArtifact artifact,
        IssueStatusLinkedIssue? linkedIssue)
    {
        if (string.Equals(artifact.PublishStatus, DraftedPublishStatus, StringComparison.Ordinal))
        {
            return IssueStatusAutomationState.DraftedOnly;
        }

        if (string.Equals(artifact.PublishStatus, IssueCreatedPublishStatus, StringComparison.Ordinal))
        {
            return linkedIssue?.HasIntentTargetLabel == true
                ? IssueStatusAutomationState.IssueCreatedLabelPresent
                : IssueStatusAutomationState.IssueCreatedNotAutomationVisible;
        }

        if (string.Equals(artifact.PublishStatus, PublishedStatus, StringComparison.Ordinal))
        {
            return linkedIssue?.HasIntentTargetLabel == true
                ? IssueStatusAutomationState.PublishedAndLabelPresent
                : IssueStatusAutomationState.PublishedMissingLabelDrift;
        }

        return IssueStatusAutomationState.Unknown;
    }

    private static void WriteResult(TextWriter writer, IssueStatusCommandResult result)
    {
        writer.WriteLine($"Issue status for {result.Artifact.ExecutionUnit}");
        writer.WriteLine($"Publish artifact: {result.ArtifactPath}");
        writer.WriteLine($"Publish status: {result.Artifact.PublishStatus}");
        writer.WriteLine($"Packet path: {result.Artifact.PacketPath}");
        writer.WriteLine($"Issue body path: {result.Artifact.IssueBodyPath}");
        writer.WriteLine($"Published label in artifact: {result.Artifact.PublishedLabelName ?? "none"}");

        if (result.LinkedIssue is null)
        {
            writer.WriteLine("Created issue: none");
            writer.WriteLine("intent-target label: not checked");
        }
        else
        {
            writer.WriteLine($"Created issue: #{result.LinkedIssue.Number} {result.LinkedIssue.Url}");
            writer.WriteLine($"Target repo: {result.LinkedIssue.TargetRepo}");
            writer.WriteLine($"intent-target label: {(result.LinkedIssue.HasIntentTargetLabel ? "present" : "missing")}");
        }

        writer.WriteLine($"Automation state: {FormatAutomationState(result.AutomationState)}");
    }

    private static string FormatAutomationState(IssueStatusAutomationState state)
    {
        return state switch
        {
            IssueStatusAutomationState.DraftedOnly => "drafted only; no linked GitHub issue",
            IssueStatusAutomationState.IssueCreatedNotAutomationVisible => "issue-created but not published / not automation-visible",
            IssueStatusAutomationState.IssueCreatedLabelPresent => "issue-created artifact but live intent-target label is already present",
            IssueStatusAutomationState.PublishedAndLabelPresent => "published and label present",
            IssueStatusAutomationState.PublishedMissingLabelDrift => "drift: artifact is published but live intent-target label is missing",
            _ => "unknown publish status"
        };
    }

    private static string ResolveRepoPath(string repoRoot, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}

internal sealed record IssueStatusCommandResult
{
    public required IssuePublishArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }

    public required IssueStatusLinkedIssue? LinkedIssue { get; init; }

    public required IssueStatusAutomationState AutomationState { get; init; }
}

internal sealed record IssueStatusLinkedIssue
{
    public required string TargetRepo { get; init; }

    public required int Number { get; init; }

    public required string Url { get; init; }

    public required bool HasIntentTargetLabel { get; init; }
}

internal enum IssueStatusAutomationState
{
    DraftedOnly,
    IssueCreatedNotAutomationVisible,
    IssueCreatedLabelPresent,
    PublishedAndLabelPresent,
    PublishedMissingLabelDrift,
    Unknown
}
