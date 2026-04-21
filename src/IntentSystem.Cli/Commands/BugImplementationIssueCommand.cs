namespace IntentSystem.Cli.Commands;

internal static class BugImplementationIssueCommand
{
    private sealed record StandaloneIssuePacketContract(
        string PacketRef,
        string ExecutionUnit,
        string IssueTitle,
        string Goal,
        string TargetRepo,
        string TargetPath,
        string TargetPart,
        IReadOnlyList<string> Dependencies,
        IReadOnlyList<string> TechnicalBaseline,
        IReadOnlyList<string> ProjectLocalGuide,
        IReadOnlyList<string> IntentBaseline,
        IReadOnlyList<string> IntentReferences,
        IReadOnlyList<string> RulesAndSpecs,
        IReadOnlyList<string> InScope,
        IReadOnlyList<string> OutOfScope,
        IReadOnlyList<string> AcceptanceCriteria,
        IReadOnlyList<string> VerificationEvidence,
        string ClarificationReturnPath);

    private sealed record StandaloneIssueBodyContext(
        BugImplementationRepairArtifact Repair,
        BugExecutionArtifact? Execution,
        BugTriageArtifact? Triage,
        BugReportArtifact? Report,
        IReadOnlyList<StandaloneIssuePacketContract> Targets);

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
            var body = BuildIssueBody(BuildIssueBodyContext(context.RepoRoot, repair));
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

    private static StandaloneIssueBodyContext BuildIssueBodyContext(string repoRoot, BugImplementationRepairArtifact repair)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(repair);

        var execution = TryReadExecutionArtifact(repoRoot, repair.ExecutionRef);
        var triage = execution is null ? null : TryReadTriageArtifact(repoRoot, execution.TriageRef);
        var reportRef = execution?.ReportRef ?? triage?.ReportRef;
        var report = string.IsNullOrWhiteSpace(reportRef) ? null : TryReadReportArtifact(repoRoot, reportRef);
        var targets = repair.ImplementationRepairTargets
            .Select(target => ReadStandaloneIssuePacketContract(repoRoot, target))
            .ToArray();

        return new StandaloneIssueBodyContext(repair, execution, triage, report, targets);
    }

    private static string BuildIssueBody(StandaloneIssueBodyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lines = new List<string>
        {
            $"# {context.Repair.SuggestedIssueTitle}",
            string.Empty,
            "## Goal"
        };
        AppendListOrFallback(lines, ResolveGoalLines(context), "Repair the selected implementation slice.");

        lines.Add(string.Empty);
        lines.Add("## Background");
        AppendListOrFallback(lines, ResolveBackgroundLines(context), $"This child issue was cut from bug '{context.Repair.BugId}'.");

        lines.Add(string.Empty);
        lines.Add("## Reproduction or Current Observed State");
        AppendListOrFallback(lines, ResolveObservedStateLines(context), "Current observed bug state was captured in the bug flow artifacts.");

        lines.Add(string.Empty);
        lines.Add("## Actual Result");
        AppendListOrFallback(lines, ResolveActualResultLines(context), "The current implementation does not satisfy the bug-repair contract yet.");

        lines.Add(string.Empty);
        lines.Add("## Expected Repaired Result");
        AppendListOrFallback(lines, ResolveExpectedResultLines(context), "The repaired implementation satisfies the selected packet goal and acceptance criteria.");

        lines.Add(string.Empty);
        lines.Add("## Narrowest Acceptable Repair Direction");
        AppendListOrFallback(lines, ResolveRepairDirectionLines(context), "Limit the change to the selected implementation target and preserve current issue creation behavior.");

        lines.Add(string.Empty);
        lines.Add("## Accepted Baseline You May Assume");
        AppendListOrFallback(lines, ResolveAcceptedBaselineLines(context), "No additional packet baseline assumptions were provided.");

        lines.Add(string.Empty);
        lines.Add("## Dependencies");
        AppendListOrFallback(lines, ResolveDependencyLines(context), "No execution-unit dependencies were declared for the selected repair target.");

        lines.Add(string.Empty);
        lines.Add("## Target Repo / Path / Part");
        AppendListOrFallback(lines, ResolveTargetLines(context), "Target packet data was not available.");

        lines.Add(string.Empty);
        lines.Add("## In Scope");
        AppendListOrFallback(lines, DistinctPreserveOrder(context.Targets.SelectMany(target => target.InScope)), "The selected implementation target listed above.");

        lines.Add(string.Empty);
        lines.Add("## Out Of Scope");
        AppendListOrFallback(lines, DistinctPreserveOrder(context.Targets.SelectMany(target => target.OutOfScope)), "Behavior outside the selected bug-repair slice.");

        lines.Add(string.Empty);
        lines.Add("## Acceptance Criteria");
        AppendListOrFallback(lines, DistinctPreserveOrder(context.Targets.SelectMany(target => target.AcceptanceCriteria)), "The repaired implementation resolves the reported bug without widening scope.");

        lines.Add(string.Empty);
        lines.Add("## Verification");
        AppendListOrFallback(lines, DistinctPreserveOrder(context.Targets.SelectMany(target => target.VerificationEvidence)), "Run the most relevant tests for the selected target slice.");

        lines.Add(string.Empty);
        lines.Add("## Relevant Links");
        AppendListOrFallback(lines, ResolveRelevantLinkLines(context), $"Implementation repair artifact: {context.Repair.ExecutionRef}");

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> ResolveGoalLines(StandaloneIssueBodyContext context)
    {
        if (context.Targets.Count == 1)
        {
            return [context.Targets[0].Goal];
        }

        var goalLines = context.Targets
            .Select(target => $"{target.ExecutionUnit}: {target.Goal}")
            .ToArray();
        if (goalLines.Length != 0)
        {
            return goalLines;
        }

        return [context.Repair.SuggestedGoal];
    }

    private static IEnumerable<string> ResolveBackgroundLines(StandaloneIssueBodyContext context)
    {
        var lines = new List<string>();
        if (context.Report is not null)
        {
            lines.Add($"Bug title: {context.Report.Title} ({context.Report.BugId}).");
        }

        if (context.Triage is not null)
        {
            lines.Add(
                $"Triage classified the bug as '{context.Triage.TriageClassification}' and selected downstream action '{context.Triage.DownstreamAction}'.");
        }

        if (!string.IsNullOrWhiteSpace(context.Repair.SuggestedGoal))
        {
            lines.Add($"Current repair goal from bug planning: {context.Repair.SuggestedGoal}");
        }

        return lines;
    }

    private static IEnumerable<string> ResolveObservedStateLines(StandaloneIssueBodyContext context)
    {
        var lines = new List<string>();
        if (context.Report is not null)
        {
            lines.Add($"Problem statement: {context.Report.ProblemStatement}");
            lines.Add($"Suspected failure locus: {context.Report.SuspectedFailureLocus}");
        }

        if (context.Triage is not null && context.Triage.ResolvedExecutionUnits.Count != 0)
        {
            lines.Add($"Resolved execution units: {string.Join(", ", context.Triage.ResolvedExecutionUnits)}");
        }

        return lines;
    }

    private static IEnumerable<string> ResolveActualResultLines(StandaloneIssueBodyContext context)
    {
        var lines = new List<string>();
        if (context.Report is not null)
        {
            lines.Add(context.Report.ProblemStatement);
        }

        if (context.Triage is not null && context.Triage.LinkedReviewRefs.Count != 0)
        {
            lines.Add($"Review evidence: {string.Join(", ", context.Triage.LinkedReviewRefs)}");
        }

        return lines;
    }

    private static IEnumerable<string> ResolveExpectedResultLines(StandaloneIssueBodyContext context)
    {
        var lines = new List<string>();
        lines.AddRange(DistinctPreserveOrder(context.Targets.Select(target => target.Goal)));
        lines.AddRange(DistinctPreserveOrder(context.Targets.SelectMany(target => target.AcceptanceCriteria)));
        return lines;
    }

    private static IEnumerable<string> ResolveRepairDirectionLines(StandaloneIssueBodyContext context)
    {
        var lines = new List<string>
        {
            "Repair only the bug-driven implementation slice selected by triage and packet targets below.",
            "Preserve existing issue creation and target repo resolution behavior while fixing the underlying implementation mismatch."
        };

        lines.AddRange(context.Targets.Select(target =>
            $"{target.ExecutionUnit}: repo '{target.TargetRepo}', path '{target.TargetPath}', part '{target.TargetPart}'."));

        return lines;
    }

    private static IEnumerable<string> ResolveAcceptedBaselineLines(StandaloneIssueBodyContext context)
    {
        var lines = new List<string>();
        lines.AddRange(DistinctPreserveOrder(context.Targets.SelectMany(target => target.TechnicalBaseline)));
        lines.AddRange(DistinctPreserveOrder(context.Targets.SelectMany(target => target.ProjectLocalGuide)));
        lines.AddRange(DistinctPreserveOrder(context.Targets.SelectMany(target => target.IntentBaseline)));
        lines.AddRange(DistinctPreserveOrder(context.Targets.SelectMany(target => target.IntentReferences)));
        lines.AddRange(DistinctPreserveOrder(context.Targets.SelectMany(target => target.RulesAndSpecs)));
        return lines;
    }

    private static IEnumerable<string> ResolveDependencyLines(StandaloneIssueBodyContext context)
    {
        return DistinctPreserveOrder(context.Targets.SelectMany(target => target.Dependencies));
    }

    private static IEnumerable<string> ResolveTargetLines(StandaloneIssueBodyContext context)
    {
        return context.Targets.Select(target =>
            $"{target.ExecutionUnit}: repo '{target.TargetRepo}', path '{target.TargetPath}', part '{target.TargetPart}'.");
    }

    private static IEnumerable<string> ResolveRelevantLinkLines(StandaloneIssueBodyContext context)
    {
        var links = new List<string>
        {
            $"Bug report artifact: {context.Execution?.ReportRef ?? context.Triage?.ReportRef ?? $".intent-cli/bugs/{context.Repair.BugId}.report.yaml"}",
            $"Bug triage artifact: {context.Execution?.TriageRef ?? $".intent-cli/bugs/{context.Repair.BugId}.triage.yaml"}",
            $"Bug plan artifact: {context.Repair.ExecutionRef}",
            $"Implementation repair artifact: {BugImplementationRepairArtifactPathResolver.Resolve(context.Repair.BugId)}"
        };

        links.AddRange(context.Repair.ImplementationRepairTargets.Select(target => $"Packet target: {target}"));

        if (context.Report is not null)
        {
            links.AddRange(context.Report.LinkedIssueRefs.Select(link => $"Linked issue: {link}"));
            links.AddRange(context.Report.LinkedPrRefs.Select(link => $"Linked PR: {link}"));
            links.AddRange(context.Report.LinkedReviewRefs.Select(link => $"Linked review: {link}"));
        }

        links.AddRange(context.Targets.Select(target =>
            $"Clarification return path for {target.ExecutionUnit}: {target.ClarificationReturnPath}"));

        return DistinctPreserveOrder(links);
    }

    private static StandaloneIssuePacketContract ReadStandaloneIssuePacketContract(string repoRoot, string packetRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packetRef);

        var packetPath = ResolveExistingArtifactPath(repoRoot, packetRef, "Implementation repair target packet");
        var yaml = File.ReadAllText(packetPath);

        try
        {
            var packet = IntentSystem.Projection.Serialization.ProjectionPacketSerializer.Deserialize(yaml);
            return new StandaloneIssuePacketContract(
                packetRef,
                packet.ImplementationIssuePacket.SourceExecutionUnit,
                packet.ImplementationIssuePacket.IssueTitle,
                packet.ImplementationIssuePacket.Goal,
                packet.ImplementationIssuePacket.TargetRepo,
                packet.ImplementationIssuePacket.TargetPath,
                packet.ImplementationIssuePacket.TargetPart,
                packet.ImplementationIssuePacket.Dependencies,
                packet.ImplementationIssuePacket.TechnicalBaseline,
                packet.ImplementationIssuePacket.ProjectLocalGuide,
                packet.ImplementationIssuePacket.IntentBaseline,
                packet.ImplementationIssuePacket.IntentReferences,
                packet.ImplementationIssuePacket.RulesAndSpecs,
                packet.ImplementationIssuePacket.InScope,
                packet.ImplementationIssuePacket.OutOfScope,
                packet.ImplementationIssuePacket.AcceptanceCriteria,
                packet.ImplementationIssuePacket.VerificationEvidence,
                packet.ReviewContextPacket.ClarificationReturnPath);
        }
        catch (InvalidOperationException)
        {
            return ReadLegacyStandaloneIssuePacketContract(packetRef, yaml);
        }
    }

    private static StandaloneIssuePacketContract ReadLegacyStandaloneIssuePacketContract(string packetRef, string yaml)
    {
        var rootValues = new Dictionary<string, object>(StringComparer.Ordinal);
        var sections = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        Dictionary<string, object>? currentSection = null;
        string? currentListKey = null;

        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                if (line.EndsWith(":", StringComparison.Ordinal))
                {
                    var sectionName = line[..^1];
                    currentSection = new Dictionary<string, object>(StringComparer.Ordinal);
                    sections[sectionName] = currentSection;
                    currentListKey = null;
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0)
                {
                    throw new InvalidOperationException($"Projection packet YAML contains invalid root field '{line}'.");
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].TrimStart();
                rootValues[key] = ParseLegacyScalar(value);
                currentSection = null;
                currentListKey = null;
                continue;
            }

            if (currentSection is null)
            {
                throw new InvalidOperationException("Projection packet YAML must declare a section before nested fields.");
            }

            if (line.StartsWith("    - ", StringComparison.Ordinal))
            {
                if (currentListKey is null
                    || !currentSection.TryGetValue(currentListKey, out var listValue)
                    || listValue is not List<string> list)
                {
                    throw new InvalidOperationException(
                        $"Projection packet YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseLegacyScalar(line[6..]));
                continue;
            }

            var trimmed = line[2..];
            var separatorIndex2 = trimmed.IndexOf(':');
            if (separatorIndex2 < 0)
            {
                throw new InvalidOperationException($"Projection packet YAML field line is missing ':': '{trimmed}'.");
            }

            var key2 = trimmed[..separatorIndex2];
            var value2 = trimmed[(separatorIndex2 + 1)..].TrimStart();
            if (value2.Length == 0)
            {
                currentSection[key2] = new List<string>();
                currentListKey = key2;
                continue;
            }

            currentListKey = null;
            currentSection[key2] = value2 == "[]"
                ? Array.Empty<string>()
                : ParseLegacyScalar(value2);
        }

        if (!rootValues.TryGetValue("execution_unit", out var executionUnitValue)
            || executionUnitValue is not string executionUnit
            || string.IsNullOrWhiteSpace(executionUnit))
        {
            throw new InvalidOperationException("Projection packet YAML must contain root field 'execution_unit'.");
        }

        if (!sections.TryGetValue("implementation_issue", out var implementationIssue))
        {
            throw new InvalidOperationException("Projection packet YAML must contain required section 'implementation_issue'.");
        }

        var reviewContext = sections.TryGetValue("review", out var reviewSection)
            ? reviewSection
            : new Dictionary<string, object>(StringComparer.Ordinal);

        return new StandaloneIssuePacketContract(
            packetRef,
            executionUnit,
            GetRequiredLegacyScalar(implementationIssue, "issue_title"),
            GetRequiredLegacyScalar(implementationIssue, "goal"),
            GetRequiredLegacyScalar(implementationIssue, "target_repo"),
            GetLegacyScalarOrDefault(implementationIssue, "target_path", "."),
            GetLegacyScalarOrDefault(implementationIssue, "target_part", GetRequiredLegacyScalar(implementationIssue, "issue_title")),
            GetLegacyList(implementationIssue, "dependencies"),
            GetLegacyList(implementationIssue, "technical_baseline"),
            GetLegacyList(implementationIssue, "project_local_guide"),
            GetLegacyList(implementationIssue, "intent_baseline"),
            GetLegacyList(implementationIssue, "intent_references"),
            GetLegacyList(implementationIssue, "rules_and_specs"),
            GetLegacyList(implementationIssue, "in_scope"),
            GetLegacyList(implementationIssue, "out_of_scope"),
            GetLegacyList(implementationIssue, "acceptance_criteria"),
            GetLegacyList(implementationIssue, "verification_evidence"),
            GetLegacyScalarOrDefault(reviewContext, "clarification_return_path", "intents/intent-cli/clarifications/open.md"));
    }

    private static BugExecutionArtifact? TryReadExecutionArtifact(string repoRoot, string artifactRef)
    {
        var artifactPath = Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(artifactPath)
            ? BugExecutionArtifactYaml.Deserialize(File.ReadAllText(artifactPath))
            : null;
    }

    private static BugTriageArtifact? TryReadTriageArtifact(string repoRoot, string artifactRef)
    {
        var artifactPath = Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(artifactPath)
            ? BugTriageArtifactYaml.Deserialize(File.ReadAllText(artifactPath))
            : null;
    }

    private static BugReportArtifact? TryReadReportArtifact(string repoRoot, string artifactRef)
    {
        var artifactPath = Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(artifactPath)
            ? BugReportArtifactYaml.Deserialize(File.ReadAllText(artifactPath))
            : null;
    }

    private static void AppendListOrFallback(List<string> lines, IEnumerable<string> values, string fallback)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        var materialized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (materialized.Length == 0)
        {
            lines.Add($"- {fallback}");
            return;
        }

        lines.AddRange(materialized.Select(value => $"- {value}"));
    }

    private static IReadOnlyList<string> DistinctPreserveOrder(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                continue;
            }

            result.Add(value);
        }

        return result;
    }

    private static string GetRequiredLegacyScalar(IReadOnlyDictionary<string, object> values, string key)
    {
        return values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"Projection packet YAML must contain required field '{key}'.");
    }

    private static string GetLegacyScalarOrDefault(IReadOnlyDictionary<string, object> values, string key, string defaultValue)
    {
        if (values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return defaultValue;
    }

    private static IReadOnlyList<string> GetLegacyList(IReadOnlyDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Projection packet YAML field '{key}' must be a list.")
        };
    }

    private static string ParseLegacyScalar(string value)
    {
        if (value.Length >= 2
            && value[0] == '"'
            && value[^1] == '"')
        {
            return value[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
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
