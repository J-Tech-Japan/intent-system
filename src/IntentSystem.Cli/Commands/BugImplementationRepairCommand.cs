using System.Globalization;

namespace IntentSystem.Cli.Commands;

internal static class BugImplementationRepairCommand
{
    internal const string Usage =
        "intent-cli bug implementation-repair <bug-id> [--execution-unit <unit>] [--issue-number <n>] [--issue-url <url>] [--actor <name>] [--note <text>]";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugImplementationRepairRenderer.WriteSummary(
                writer,
                result.Artifact,
                result.ArtifactPath,
                result.PreviousArtifact);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            writer.WriteLine($"Usage: {Usage}");
            return 1;
        }
    }

    internal static BugImplementationRepairCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var parsed = ParseArguments(args);
        var bugId = parsed.BugId;
        var reportRef = $".intent-cli/bugs/{bugId}.report.yaml";
        var triageRef = $".intent-cli/bugs/{bugId}.triage.yaml";
        var executionRef = $".intent-cli/bugs/{bugId}.plan.yaml";
        var previousArtifact = TryReadExistingArtifact(context.RepoRoot, bugId);

        var reportPath = ResolveExistingArtifactPath(context.RepoRoot, reportRef, "Bug report artifact");
        var triagePath = ResolveExistingArtifactPath(context.RepoRoot, triageRef, "Bug triage artifact");
        var executionPath = ResolveExistingArtifactPath(context.RepoRoot, executionRef, "Bug plan artifact");

        var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
        var triage = BugTriageArtifactYaml.Deserialize(File.ReadAllText(triagePath));
        var execution = BugExecutionArtifactYaml.Deserialize(File.ReadAllText(executionPath));

        ValidateBugId("report", bugId, report.BugId);
        ValidateBugId("triage", bugId, triage.BugId);
        ValidateBugId("execution", bugId, execution.BugId);

        var implementationTaskCandidates = DistinctOrdered(execution.ImplementationTaskCandidates);
        var readyToIssueCut = !triage.ClarificationRequired
            && !string.Equals(triage.DownstreamAction, "intent-only", StringComparison.Ordinal)
            && implementationTaskCandidates.Length > 0;

        var implementationRepairTargets = readyToIssueCut
            ? NormalizeImplementationRepairTargets(implementationTaskCandidates)
            : [];

        var repairExecutionUnit = parsed.RepairExecutionUnit ?? previousArtifact?.RepairExecutionUnit;
        var repairIssueNumber = parsed.RepairIssueNumber ?? previousArtifact?.RepairIssueNumber;
        var repairIssueUrl = parsed.RepairIssueUrl ?? previousArtifact?.RepairIssueUrl;
        var recordedBy = parsed.RecordedBy ?? previousArtifact?.RecordedBy;
        var note = parsed.Note ?? previousArtifact?.Note;
        ValidateRepairIssueIdentity(repairIssueNumber, repairIssueUrl);

        var artifact = new BugImplementationRepairArtifact
        {
            BugId = bugId,
            ExecutionRef = executionRef,
            ImplementationTaskCandidates = implementationTaskCandidates,
            ImplementationRepairTargets = implementationRepairTargets,
            SuggestedIssueTitle = $"Implementation repair: {report.Title} ({bugId})",
            SuggestedGoal = BuildSuggestedGoal(report, bugId, implementationRepairTargets, executionRef, readyToIssueCut),
            ReadyToIssueCut = readyToIssueCut,
            RepairExecutionUnit = repairExecutionUnit,
            RepairIssueNumber = repairIssueNumber,
            RepairIssueUrl = repairIssueUrl,
            RecordedBy = recordedBy,
            Note = note,
            RecordedAt = parsed.HasRecordedRepairLinkUpdate
                ? TimestampFactory().ToUniversalTime()
                : previousArtifact?.RecordedAt
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugImplementationRepairCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath,
            PreviousArtifact = parsed.HasRecordedRepairLinkUpdate && HasRecordedRepairDetails(previousArtifact)
                ? previousArtifact
                : null
        };
    }

    private static ParsedArguments ParseArguments(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug implementation-repair command requires '<bug-id>'.");
        }

        var seenFlags = new HashSet<string>(StringComparer.Ordinal);
        string? repairExecutionUnit = null;
        int? repairIssueNumber = null;
        string? repairIssueUrl = null;
        string? recordedBy = null;
        string? note = null;

        for (var index = 1; index < args.Length; index += 2)
        {
            var flag = args[index];
            if (!flag.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unknown argument '{flag}'.");
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new InvalidOperationException($"Flag '{flag}' requires a value.");
            }

            if (!seenFlags.Add(flag))
            {
                throw new InvalidOperationException($"Flag '{flag}' may be specified only once.");
            }

            var value = args[index + 1].Trim();
            switch (flag)
            {
                case "--execution-unit":
                    if (!IsExecutionUnitToken(value))
                    {
                        throw new InvalidOperationException(
                            $"Repair execution unit '{value}' must be an alphanumeric token such as 'G782'.");
                    }

                    repairExecutionUnit = value;
                    break;
                case "--issue-number":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var issueNumber)
                        || issueNumber <= 0)
                    {
                        throw new InvalidOperationException($"Repair issue number '{value}' must be a positive integer.");
                    }

                    repairIssueNumber = issueNumber;
                    break;
                case "--issue-url":
                    repairIssueUrl = value;
                    break;
                case "--actor":
                    recordedBy = value;
                    break;
                case "--note":
                    note = value;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown argument '{flag}'. Supported: [--execution-unit <unit>] [--issue-number <n>] [--issue-url <url>] [--actor <name>] [--note <text>].");
            }
        }

        return new ParsedArguments(
            args[0].Trim(),
            repairExecutionUnit,
            repairIssueNumber,
            repairIssueUrl,
            recordedBy,
            note);
    }

    private static BugImplementationRepairArtifact? TryReadExistingArtifact(string repoRoot, string bugId)
    {
        var relativePath = BugImplementationRepairArtifactPathResolver.Resolve(bugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var artifact = BugImplementationRepairArtifactYaml.Deserialize(File.ReadAllText(absolutePath));
        ValidateBugId("implementation-repair", bugId, artifact.BugId);
        return artifact;
    }

    private static void ValidateRepairIssueIdentity(int? repairIssueNumber, string? repairIssueUrl)
    {
        if (repairIssueNumber is not null
            && !string.IsNullOrWhiteSpace(repairIssueUrl)
            && !repairIssueUrl.EndsWith(
                repairIssueNumber.Value.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Repair issue URL '{repairIssueUrl}' does not end with repair issue number '{repairIssueNumber.Value.ToString(CultureInfo.InvariantCulture)}'.");
        }
    }

    private static bool HasRecordedRepairDetails(BugImplementationRepairArtifact? artifact)
    {
        return artifact is not null
            && (!string.IsNullOrWhiteSpace(artifact.RepairExecutionUnit)
                || artifact.RepairIssueNumber is not null
                || !string.IsNullOrWhiteSpace(artifact.RepairIssueUrl)
                || !string.IsNullOrWhiteSpace(artifact.RecordedBy)
                || !string.IsNullOrWhiteSpace(artifact.Note)
                || artifact.RecordedAt is not null);
    }

    private static bool IsExecutionUnitToken(string value)
    {
        return value.Length > 0
            && char.IsLetter(value[0])
            && value.All(character => char.IsLetterOrDigit(character) || character == '-');
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

    private static void ValidateBugId(string source, string requestedBugId, string artifactBugId)
    {
        if (!string.Equals(requestedBugId, artifactBugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug {source} artifact bug id '{artifactBugId}' does not match requested bug id '{requestedBugId}'.");
        }
    }

    private static string[] NormalizeImplementationRepairTargets(IEnumerable<string> implementationTaskCandidates)
    {
        return implementationTaskCandidates
            .Select(candidate => $".intent-cli/issues/{candidate}/packet.yaml")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSuggestedGoal(
        BugReportArtifact report,
        string bugId,
        IReadOnlyList<string> implementationRepairTargets,
        string executionRef,
        bool readyToIssueCut)
    {
        if (!readyToIssueCut)
        {
            return $"Prepare child implementation repair for '{report.Title}' ({bugId}) once issue-cut blockers are cleared from {executionRef}.";
        }

        return $"Repair child implementation targets for '{report.Title}' ({bugId}) using {executionRef}: {string.Join(", ", implementationRepairTargets)}";
    }

    private static string WriteArtifact(string repoRoot, BugImplementationRepairArtifact artifact)
    {
        var relativePath = BugImplementationRepairArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug implementation-repair artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugImplementationRepairArtifactYaml.Serialize(artifact));

        return relativePath;
    }

    private static string[] DistinctOrdered(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record ParsedArguments(
        string BugId,
        string? RepairExecutionUnit,
        int? RepairIssueNumber,
        string? RepairIssueUrl,
        string? RecordedBy,
        string? Note)
    {
        public bool HasRecordedRepairLinkUpdate => RepairExecutionUnit is not null
            || RepairIssueNumber is not null
            || RepairIssueUrl is not null
            || RecordedBy is not null
            || Note is not null;
    }
}
