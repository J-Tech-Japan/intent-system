using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunImplementCommand
{
    public static Func<IDirectRunLauncher> DirectRunLauncherFactory { get; set; } = () => new DirectRunLauncher();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run implement command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0]);
            RunImplementRenderer.WriteSummary(writer, result.Request, result.ArtifactPath, result.DirectRun);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static RunImplementResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            throw new InvalidOperationException($"Execution unit '{executionUnit}' was not found in queue state.");
        }

        if (queueItem.State is not (QueueItemState.Active or QueueItemState.Fixing))
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must be active or fixing before run implement.");
        }

        if (queueItem.LinkedIssue is null)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must have a linked issue before run implement.");
        }

        var packetPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var reviewContextPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.ReviewContext);
        if (!File.Exists(reviewContextPath))
        {
            throw new InvalidOperationException($"Review context artifact was not found at {reviewContextPath}");
        }

        var packet = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetPath));
        var childRepoRef = packet.ImplementationIssuePacket.TargetRepo;
        if (string.IsNullOrWhiteSpace(childRepoRef))
        {
            throw new InvalidOperationException("Projection packet must contain a target repo.");
        }

        var childRepoPath = ResolveChildRepoPath(context.RepoRoot, childRepoRef);
        if (!Directory.Exists(childRepoPath))
        {
            throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
        }

        var worktreePath = RunStartCommand.ResolveWorktreePath(context, executionUnit);
        if (!Directory.Exists(worktreePath))
        {
            throw new InvalidOperationException($"Worktree path was not found at {worktreePath}");
        }

        SyncCurrentIssueArtifactsToWorktree(packetPath, worktreePath);
        SyncCurrentRepoLocalContractArtifactsToWorktree(
            childRepoPath,
            worktreePath,
            packet.ImplementationIssuePacket);

        ChildWorkTargetGuard.EnsureTargetAllowed(
            executionUnit,
            context.RepoRoot,
            packet.ImplementationIssuePacket.TargetRepo,
            worktreePath,
            packet.ImplementationIssuePacket.TargetPath,
            packet.ImplementationIssuePacket.TargetPart);

        var reviewContext = ReviewContextMarkdownParser.Parse(File.ReadAllText(reviewContextPath));
        if (!string.Equals(reviewContext.SourceExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Review context execution unit '{reviewContext.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
        }

        var latestLinkedPr = ResolveLatestLinkedPr(context, executionUnit);
        var request = BuildRequest(
            context,
            queueItem,
            packet,
            reviewContext,
            worktreePath,
            childRepoPath,
            latestLinkedPr);
        var markdown = RunImplementRenderer.RenderMarkdown(request);
        var artifactPath = RunImplementArtifactWriter.Write(markdown, executionUnit, context.RepoRoot, overwrite: true);
        var relativeArtifactPath = ToRelativePath(context.RepoRoot, artifactPath);
        var directRun = DirectRunCommandSupport.CreateAndLaunch(
            context,
            DirectRunEntryKind.Implement,
            executionUnit,
            relativeArtifactPath,
            worktreePath,
            DirectRunLauncherFactory(),
            TimestampFactory());

        return new RunImplementResult
        {
            Request = request,
            ArtifactPath = relativeArtifactPath,
            DirectRun = directRun
        };
    }

    private static RunImplementRequest BuildRequest(
        CliContext context,
        QueueItem queueItem,
        Projection.Models.ProjectionPacketContract packet,
        Review.Models.ReviewContextSnapshot reviewContext,
        string worktreePath,
        string childRepoPath,
        string? latestLinkedPr)
    {
        return new RunImplementRequest
        {
            ExecutionUnit = queueItem.ExecutionUnit,
            State = FormatState(queueItem.State),
            ImplementRole = context.Config.Roles.Implement,
            QueueWorkerRole = queueItem.WorkerRole,
            QueueReviewRole = queueItem.ReviewRole,
            WorktreePath = worktreePath,
            ChildRepoPath = childRepoPath,
            Branch = RunStartCommand.ResolveBranchName(queueItem.ExecutionUnit, queueItem.LinkedIssue!),
            LinkedIssue = queueItem.LinkedIssue!.Url,
            LatestLinkedPr = latestLinkedPr,
            PacketRef = queueItem.PacketPaths.Yaml,
            ReviewContextRef = queueItem.PacketPaths.ReviewContext,
            IssueTitle = packet.ImplementationIssuePacket.IssueTitle,
            Goal = packet.ImplementationIssuePacket.Goal,
            TargetPart = packet.ImplementationIssuePacket.TargetPart,
            TargetRepo = packet.ImplementationIssuePacket.TargetRepo,
            TargetPath = packet.ImplementationIssuePacket.TargetPath,
            InScope = packet.ImplementationIssuePacket.InScope,
            OutOfScope = packet.ImplementationIssuePacket.OutOfScope,
            AcceptanceCriteria = reviewContext.AcceptanceCriteria,
            DeterministicReviewChecks = reviewContext.DeterministicReviewChecks,
            ExpectedEvidence = reviewContext.ExpectedEvidence
        };
    }

    private static string? ResolveLatestLinkedPr(CliContext context, string executionUnit)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return null;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        return LatestLinkedPrResolver.TryResolve(runEvents, executionUnit);
    }

    private static string ResolveArtifactPath(string repoRoot, string artifactRef)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void SyncCurrentIssueArtifactsToWorktree(
        string sourceArtifactPath,
        string worktreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArtifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        var sourceIssueDirectory = Path.GetDirectoryName(sourceArtifactPath)
            ?? throw new InvalidOperationException("Issue artifact path did not contain a directory.");
        if (!Directory.Exists(sourceIssueDirectory))
        {
            throw new InvalidOperationException($"Issue artifact directory was not found at {sourceIssueDirectory}");
        }

        var executionUnit = Path.GetFileName(sourceIssueDirectory);
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            throw new InvalidOperationException("Issue artifact directory did not contain an execution unit name.");
        }

        var targetIssueDirectory = Path.Combine(worktreePath, ".intent-cli", "issues", executionUnit);
        Directory.CreateDirectory(targetIssueDirectory);

        foreach (var sourceFilePath in Directory.GetFiles(sourceIssueDirectory))
        {
            var targetFilePath = Path.Combine(targetIssueDirectory, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, targetFilePath, overwrite: true);
        }
    }

    private static void SyncCurrentRepoLocalContractArtifactsToWorktree(
        string childRepoPath,
        string worktreePath,
        Projection.Models.ImplementationIssuePacket packet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childRepoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentNullException.ThrowIfNull(packet);

        foreach (var artifactRef in EnumerateRepoLocalContractRefs(packet))
        {
            if (!TryResolveRepoLocalArtifactPath(childRepoPath, artifactRef, out var sourceArtifactPath)
                || !TryResolveRepoLocalArtifactPath(worktreePath, artifactRef, out var targetArtifactPath))
            {
                continue;
            }

            if (File.Exists(sourceArtifactPath))
            {
                var targetDirectory = Path.GetDirectoryName(targetArtifactPath)
                    ?? throw new InvalidOperationException("Target artifact path did not contain a directory.");
                Directory.CreateDirectory(targetDirectory);
                File.Copy(sourceArtifactPath, targetArtifactPath, overwrite: true);
                continue;
            }

            if (Directory.Exists(sourceArtifactPath))
            {
                CopyDirectory(sourceArtifactPath, targetArtifactPath);
            }
        }
    }

    private static IEnumerable<string> EnumerateRepoLocalContractRefs(Projection.Models.ImplementationIssuePacket packet)
    {
        return packet.ProjectLocalGuide
            .Concat(packet.IntentReferences)
            .Concat(packet.RulesAndSpecs)
            .Distinct(StringComparer.Ordinal);
    }

    private static bool TryResolveRepoLocalArtifactPath(
        string repoRoot,
        string artifactRef,
        out string artifactPath)
    {
        artifactPath = string.Empty;

        if (string.IsNullOrWhiteSpace(repoRoot)
            || string.IsNullOrWhiteSpace(artifactRef)
            || Path.IsPathRooted(artifactRef))
        {
            return false;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(
            repoRoot,
            artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithinRoot(repoRoot, candidatePath))
        {
            return false;
        }

        artifactPath = candidatePath;
        return true;
    }

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedRoot, normalizedCandidate, StringComparison.Ordinal)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        Directory.CreateDirectory(targetDirectory);

        foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var targetFilePath = Path.Combine(targetDirectory, relativePath);
            var targetFileDirectory = Path.GetDirectoryName(targetFilePath)
                ?? throw new InvalidOperationException("Target file path did not contain a directory.");

            Directory.CreateDirectory(targetFileDirectory);
            File.Copy(sourceFilePath, targetFilePath, overwrite: true);
        }
    }

    private static string ResolveChildRepoPath(string repoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(repoRoot, childRepoRef));
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
