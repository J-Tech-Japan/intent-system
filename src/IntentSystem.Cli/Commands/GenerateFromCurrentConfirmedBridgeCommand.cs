using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedBridgeCommand
{
    public static Func<CliContext, string[], GenerateFromCurrentReconcileResult> ReconcileExecutor { get; set; } =
        (context, args) => GenerateFromCurrentReconcileCommand.ExecuteCore(context, args);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            GenerateFromCurrentConfirmedBridgeRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static GenerateFromCurrentConfirmedBridgeResult ExecuteCore(CliContext context, string[] args)
    {
        var domain = ParseDomain(args);
        var reconcileResult = ReconcileExecutor(context, args);

        if (!string.Equals(reconcileResult.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reconciliation result domain '{reconcileResult.Domain}' does not match requested domain '{domain}'.");
        }

        if (string.Equals(reconcileResult.Route, "clarification-return", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = domain,
                Route = "clarification-return",
                ClarificationReturnArtifactPath = reconcileResult.ClarificationReturnArtifactPath,
                ConfirmedReconstructionArtifactPath = reconcileResult.ConfirmedReconstructionArtifactPath,
                InterviewArtifactPaths = [],
                RegeneratedArtifactPaths = [],
                ConfirmedItems = reconcileResult.ConfirmedItems,
                BlockedItems = reconcileResult.BlockedItems,
                DownstreamReadiness = reconcileResult.DownstreamReadiness
            };
        }

        if (!string.Equals(reconcileResult.DownstreamReadiness, "ready", StringComparison.Ordinal))
        {
            return new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = domain,
                Route = "reconciliation-required",
                ConfirmedReconstructionArtifactPath = reconcileResult.ConfirmedReconstructionArtifactPath,
                InterviewArtifactPaths = [],
                RegeneratedArtifactPaths = reconcileResult.ConfirmedReconstructionArtifactPath is null
                    ? []
                    : [reconcileResult.ConfirmedReconstructionArtifactPath],
                ConfirmedItems = reconcileResult.ConfirmedItems,
                BlockedItems = reconcileResult.BlockedItems,
                DownstreamReadiness = reconcileResult.DownstreamReadiness
            };
        }

        if (string.IsNullOrWhiteSpace(reconcileResult.ConfirmedReconstructionArtifactPath))
        {
            throw new InvalidOperationException("Confirmed bridge requires a confirmed reconstruction artifact path.");
        }

        var confirmedArtifact = LoadConfirmedReconstruction(
            context.RepoRoot,
            reconcileResult.ConfirmedReconstructionArtifactPath,
            domain);
        ValidateConfirmedReconstructionArtifact(confirmedArtifact, reconcileResult);

        var reconstructedConcept = GenerateFromCurrentBridgeCommand.LoadReconstructedConcept(context.RepoRoot, domain);
        var reconstructedInterview = GenerateFromCurrentBridgeCommand.LoadReconstructedInterview(context.RepoRoot, domain);
        var recommendedUpdates = GenerateFromCurrentBridgeCommand.BuildRecommendedUpdates(reconstructedInterview);
        var conceptPacket = BuildConceptPacket(confirmedArtifact, reconstructedConcept, reconstructedInterview);

        var conceptArtifactPath = IntakeConceptArtifactWriter.Write(
            IntakeConceptArtifactYaml.Serialize(conceptPacket),
            domain,
            context.RepoRoot);

        var interviewArtifactPaths = GenerateFromCurrentBridgeCommand.WriteInterviewArtifacts(
            context.RepoRoot,
            domain,
            reconstructedConcept,
            reconstructedInterview,
            recommendedUpdates);

        var regeneratedArtifactPaths = new List<string>
        {
            GenerateFromCurrentBridgeCommand.ToRelativePath(context.RepoRoot, conceptArtifactPath)
        };
        regeneratedArtifactPaths.AddRange(interviewArtifactPaths);

        return new GenerateFromCurrentConfirmedBridgeResult
        {
            Domain = domain,
            Route = "confirmed-bridge",
            ConceptArtifactPath = GenerateFromCurrentBridgeCommand.ToRelativePath(context.RepoRoot, conceptArtifactPath),
            InterviewArtifactPaths = interviewArtifactPaths,
            ConfirmedReconstructionArtifactPath = reconcileResult.ConfirmedReconstructionArtifactPath,
            RegeneratedArtifactPaths = regeneratedArtifactPaths,
            ConfirmedItems = confirmedArtifact.ConfirmedItems,
            BlockedItems = confirmedArtifact.BlockedItems,
            DownstreamReadiness = confirmedArtifact.DownstreamReadiness
        };
    }

    private static ConfirmedReconstructionArtifact LoadConfirmedReconstruction(
        string repoRoot,
        string artifactRef,
        string domain)
    {
        var artifactPath = Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Confirmed reconstruction artifact was not found at {artifactPath}");
        }

        var artifact = ConfirmedReconstructionArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
        if (!string.Equals(artifact.DomainSlug, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed reconstruction artifact domain '{artifact.DomainSlug}' does not match requested domain '{domain}'.");
        }

        return artifact;
    }

    private static string ParseDomain(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Generate-from-current confirmed-bridge requires a domain.");
        }

        return args[0].Trim();
    }

    private static void ValidateConfirmedReconstructionArtifact(
        ConfirmedReconstructionArtifact artifact,
        GenerateFromCurrentReconcileResult reconcileResult)
    {
        if (!string.Equals(artifact.SourceBundleArtifactPath, reconcileResult.SourceBundleArtifactPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed reconstruction artifact source bundle path '{artifact.SourceBundleArtifactPath}' must match current source bundle path '{reconcileResult.SourceBundleArtifactPath}'.");
        }

        if (!artifact.ReconstructedArtifactPaths.SequenceEqual(reconcileResult.ReconstructedArtifactPaths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Confirmed reconstruction artifact reconstructed artifact paths must match the current reconstruction output.");
        }

        if (!string.Equals(artifact.ReviewArtifactPath, reconcileResult.ReviewArtifactPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Confirmed reconstruction artifact review path '{artifact.ReviewArtifactPath}' must match current review path '{reconcileResult.ReviewArtifactPath}'.");
        }

        if (!string.Equals(
                artifact.DeveloperConfirmationArtifactPath,
                reconcileResult.DeveloperConfirmationArtifactPath,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Confirmed reconstruction artifact developer confirmation path must match the current reconciliation output.");
        }
    }

    private static ConceptIntakePacket BuildConceptPacket(
        ConfirmedReconstructionArtifact confirmedArtifact,
        ReconstructedConceptArtifact reconstructedConcept,
        ReconstructedInterviewArtifact reconstructedInterview)
    {
        return new ConceptIntakePacket
        {
            DomainSlug = confirmedArtifact.DomainSlug,
            ConceptSource = "generate-from-current-confirmed-bridge",
            ConceptText = BuildConceptText(confirmedArtifact, reconstructedConcept, reconstructedInterview),
            UpstreamPaths = confirmedArtifact.ReturnToIntentPaths,
            InitialGoal = reconstructedConcept.InitialGoal,
            Constraints = reconstructedConcept.CandidateRules
                .Concat(reconstructedConcept.CandidateSpecs)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            KnownUnknowns = confirmedArtifact.DeferredItems
                .Concat(confirmedArtifact.BlockedItems)
                .Concat(reconstructedInterview.BridgeQuestions.Select(question => question.QuestionText))
                .Concat(reconstructedInterview.Gaps)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static string BuildConceptText(
        ConfirmedReconstructionArtifact confirmedArtifact,
        ReconstructedConceptArtifact reconstructedConcept,
        ReconstructedInterviewArtifact reconstructedInterview)
    {
        var lines = new List<string>
        {
            $"Confirmed reconstruction bridge for domain '{confirmedArtifact.DomainSlug}'."
        };

        AppendSection(lines, "confirmed_items", confirmedArtifact.ConfirmedItems);
        AppendSection(lines, "rejected_items", confirmedArtifact.RejectedItems);
        AppendSection(lines, "deferred_items", confirmedArtifact.DeferredItems);
        AppendSection(lines, "blocked_items", confirmedArtifact.BlockedItems);
        AppendSection(lines, "candidate_intent_nodes", reconstructedConcept.CandidateIntentNodes);
        AppendSection(lines, "candidate_user_context", reconstructedConcept.CandidateUserContext);
        AppendSection(lines, "candidate_means", reconstructedConcept.CandidateMeans);
        AppendSection(lines, "candidate_execution_units", reconstructedConcept.CandidateExecutionUnits);
        AppendSection(lines, "source_concept_refs", reconstructedConcept.SourceConceptRefs);
        AppendSection(
            lines,
            "recommended_follow_up_questions",
            reconstructedInterview.BridgeQuestions.Select(question => question.QuestionText).ToArray());

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendSection(List<string> lines, string title, IReadOnlyList<string> values)
    {
        lines.Add($"{title}:");
        if (values.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(values.Select(value => $"- {value}"));
        }

        lines.Add(string.Empty);
    }
}
