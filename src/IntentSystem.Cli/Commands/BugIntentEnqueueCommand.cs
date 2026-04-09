using IntentSystem.Cli.Models;
using IntentSystem.Projection;
using IntentSystem.Projection.Models;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class BugIntentEnqueueCommand
{
    private const string TransitionActor = "intent-cli";
    private const string ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md";
    private const string ClarificationReturnPath = "intents/intent-cli/clarifications/open.md";
    private const string DefaultPriority = "high";

    private static readonly string[] TechnicalBaseline =
    [
        "C# / .NET",
        ".NET 10.0.100+ baseline",
        "dnx / dotnet tool exec",
        "do not switch to Node or TypeScript toolchain"
    ];

    private static readonly string[] ProjectLocalGuide =
    [
        "AGENTS.md",
        "CLAUDE.md"
    ];

    private static readonly string[] VerificationEvidence =
    [
        "contract-reviewed",
        "tests-passing",
        "acceptance-criteria-checked"
    ];

    private static readonly string[] IntentBaseline =
    [
        "bug intent enqueue stays deterministic",
        "execution unit allocation follows queue snapshot monotonic G<number>"
    ];

    private static readonly string[] OutOfScope =
    [
        "parent repair issue recreation",
        "parent repair launch",
        "review / merge / closeout",
        "workflow execution",
        "parent source-of-truth canonical markdown mutation"
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentEnqueueRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentEnqueueCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug intent-enqueue command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var artifactPath = ResolveArtifactPath(context.RepoRoot, bugId);
        if (File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Bug intent-enqueue artifact already exists at {artifactPath}");
        }

        var intentIssueRef = $".intent-cli/bugs/{bugId}.intent-issue.yaml";
        var intentIssuePath = ResolveExistingArtifactPath(context.RepoRoot, intentIssueRef, "Bug intent-issue artifact");
        var intentIssue = BugIntentIssueArtifactYaml.Deserialize(File.ReadAllText(intentIssuePath));
        if (!string.Equals(intentIssue.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-issue artifact bug id '{intentIssue.BugId}' does not match requested bug id '{bugId}'.");
        }

        var intentRepairPath = ResolveExistingArtifactPath(context.RepoRoot, intentIssue.IntentRepairRef, "Bug intent-repair artifact");
        var intentRepair = BugIntentRepairArtifactYaml.Deserialize(File.ReadAllText(intentRepairPath));
        if (!string.Equals(intentRepair.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-repair artifact bug id '{intentRepair.BugId}' does not match requested bug id '{bugId}'.");
        }

        ValidateIssueLinkConsistency(intentIssue);

        if (intentIssue.CreatedIssueUrl is null)
        {
            var notReadyArtifact = new BugIntentEnqueueArtifact
            {
                BugId = bugId,
                IntentIssueRef = intentIssueRef,
                AllocatedExecutionUnit = null,
                LinkedIssueUrl = null,
                LinkedIssueNumber = null,
                PacketPaths = [],
                ReadyToEnqueue = false
            };

            return new BugIntentEnqueueCommandResult
            {
                Artifact = notReadyArtifact,
                ArtifactPath = WriteArtifact(context.RepoRoot, notReadyArtifact)
            };
        }

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            throw new InvalidOperationException($"Queue state artifact was not found at {queueStatePath}");
        }

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var executionUnit = AllocateNextExecutionUnit(queueState);
        var parentRepoRoot = ParentRepairTargetRepoResolver.Resolve(context, intentIssue.ParentRepairTargets);
        var packet = CreatePacket(context, executionUnit, intentIssue, intentRepair, parentRepoRoot);
        ProjectionArtifactWriter.Write(packet, context.RepoRoot, overwrite: false);

        var packetPaths = QueueEnqueueCommand.ResolvePacketPaths(context.RepoRoot, executionUnit);
        var queueItem = QueueEnqueueCommand.CreateQueueItem(
            context,
            new QueueEnqueueCommand.ResolvedQueuePacket
            {
                ExecutionUnit = executionUnit,
                IssueTitle = $"[{executionUnit}] {intentIssue.CreatedIssueTitle}",
                Dependencies = [],
                ClarificationReturnPath = ClarificationReturnPath
            },
            packetPaths) with
        {
            LinkedIssue = ParseLinkedIssue(intentIssue.CreatedIssueUrl, intentIssue.CreatedIssueNumber!.Value)
        };

        var enqueueResult = QueueManager.Enqueue(
            queueState,
            queueItem,
            TransitionActor,
            QueueEnqueueCommand.TimestampFactory());

        if (!enqueueResult.WasEnqueued)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' is already present in queue-state.json after allocation.");
        }

        QueueEnqueueCommand.PersistEnqueue(context, enqueueResult);

        var artifact = new BugIntentEnqueueArtifact
        {
            BugId = bugId,
            IntentIssueRef = intentIssueRef,
            AllocatedExecutionUnit = executionUnit,
            LinkedIssueUrl = intentIssue.CreatedIssueUrl,
            LinkedIssueNumber = intentIssue.CreatedIssueNumber,
            PacketPaths =
            [
                packet.Paths.Implementation,
                packet.Paths.ReviewContext,
                packet.Paths.Yaml
            ],
            ReadyToEnqueue = true
        };

        return new BugIntentEnqueueCommandResult
        {
            Artifact = artifact,
            ArtifactPath = WriteArtifact(context.RepoRoot, artifact)
        };
    }

    internal static string AllocateNextExecutionUnit(QueueState queueState)
    {
        ArgumentNullException.ThrowIfNull(queueState);

        var maxExecutionUnit = 0;
        foreach (var item in queueState.Items)
        {
            if (!TryParseExecutionUnitNumber(item.ExecutionUnit, out var number))
            {
                continue;
            }

            maxExecutionUnit = Math.Max(maxExecutionUnit, number);
        }

        return $"G{maxExecutionUnit + 1}";
    }

    private static GeneratedPacket CreatePacket(
        CliContext context,
        string executionUnit,
        BugIntentIssueArtifact intentIssue,
        BugIntentRepairArtifact intentRepair,
        string parentRepoRoot)
    {
        var relativeTargetRepo = Path.GetRelativePath(context.RepoRoot, parentRepoRoot)
            .Replace(Path.DirectorySeparatorChar, '/');
        var normalizedTargets = intentIssue.ParentRepairTargets
            .Select(NormalizeTarget)
            .ToArray();
        var issueTitle = $"[{executionUnit}] {intentIssue.CreatedIssueTitle}";

        return PacketGenerator.Generate(
            new SubSliceRow
            {
                SourceExecutionUnit = executionUnit,
                Goal = intentRepair.SuggestedGoal,
                TargetRepo = relativeTargetRepo,
                TargetPath = ".",
                TargetPart = "parent intent repair targets",
                DependsOnSubslices = [],
                RelatedIntents = normalizedTargets,
                SourceConcepts = normalizedTargets,
                SuccessSignal = $"Parent repair targets for `{intentIssue.CreatedIssueTitle}` are updated deterministically.",
                ReviewMode = "deterministic-review",
                CompletionAction = "wait-for-deterministic-review",
                LandingPolicy = "merge-after-review"
            },
            new ProjectionContext
            {
                IssueTitle = issueTitle,
                IssueKind = IssueKind.Bugfix,
                ParentIntentRoot = ParentIntentRoot,
                ClarificationReturnPath = ClarificationReturnPath,
                AcceptanceCriteria =
                [
                    $"Selected bug repair is allocated to execution unit `{executionUnit}`.",
                    $"Changes stay limited to declared parent repair targets.",
                    $"Linked issue `{intentIssue.CreatedIssueUrl}` remains the current parent repair issue."
                ],
                DeterministicReviewChecks =
                [
                    "queue insertion stays deterministic",
                    "changes stay limited to declared parent repair targets",
                    "linked issue ref remains unchanged"
                ],
                VerificationEvidence = VerificationEvidence,
                TechnicalBaseline = TechnicalBaseline,
                ProjectLocalGuide = ProjectLocalGuide,
                IntentBaseline = IntentBaseline,
                AdditionalInScope =
                [
                    $"bug id `{intentIssue.BugId}`",
                    $"linked parent issue `{intentIssue.CreatedIssueUrl}`",
                    ..intentIssue.ParentRepairTargets.Select(target => $"parent repair target `{target}`")
                ],
                OutOfScope = OutOfScope
            });
    }

    private static LinkedIssue ParseLinkedIssue(string createdIssueUrl, int createdIssueNumber)
    {
        if (!Uri.TryCreate(createdIssueUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Created issue URL '{createdIssueUrl}' must be an absolute URL.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 4
            || !string.Equals(segments[2], "issues", StringComparison.Ordinal)
            || !int.TryParse(segments[3], out var parsedIssueNumber))
        {
            throw new InvalidOperationException(
                $"Created issue URL '{createdIssueUrl}' must use the GitHub issue URL shape.");
        }

        if (parsedIssueNumber != createdIssueNumber)
        {
            throw new InvalidOperationException(
                $"Created issue URL number '{parsedIssueNumber}' does not match artifact issue number '{createdIssueNumber}'.");
        }

        return new LinkedIssue
        {
            Repo = $"{segments[0]}/{segments[1]}",
            Number = createdIssueNumber,
            Url = createdIssueUrl
        };
    }

    private static void ValidateIssueLinkConsistency(BugIntentIssueArtifact artifact)
    {
        if (artifact.CreatedIssueUrl is null && artifact.CreatedIssueNumber is null)
        {
            return;
        }

        if (artifact.CreatedIssueUrl is null || artifact.CreatedIssueNumber is null)
        {
            throw new InvalidOperationException(
                "Bug intent-issue artifact must contain both created_issue_url and created_issue_number when issue creation succeeded.");
        }
    }

    private static string NormalizeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var separatorIndex = target.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == target.Length - 1)
        {
            throw new InvalidOperationException($"Parent repair target '{target}' must use the kind:path shape.");
        }

        return target[(separatorIndex + 1)..].Trim();
    }

    private static bool TryParseExecutionUnitNumber(string executionUnit, out int number)
    {
        number = 0;
        if (executionUnit.Length < 2 || executionUnit[0] != 'G')
        {
            return false;
        }

        return int.TryParse(executionUnit[1..], out number);
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

    private static string ResolveArtifactPath(string repoRoot, string bugId)
    {
        return Path.GetFullPath(
            Path.Combine(
                repoRoot,
                BugIntentEnqueueArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string WriteArtifact(string repoRoot, BugIntentEnqueueArtifact artifact)
    {
        var relativePath = BugIntentEnqueueArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = ResolveArtifactPath(repoRoot, artifact.BugId);
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-enqueue artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentEnqueueArtifactYaml.Serialize(artifact));
        return relativePath;
    }
}
