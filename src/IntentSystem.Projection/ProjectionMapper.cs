using IntentSystem.Projection.Models;

namespace IntentSystem.Projection;

public static class ProjectionMapper
{
    public static ImplementationIssuePacket ToImplementationPacket(
        SubSliceRow row,
        ProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        return new ImplementationIssuePacket
        {
            IssueTitle = context.IssueTitle,
            IssueKind = context.IssueKind,
            SourceExecutionUnit = row.SourceExecutionUnit,
            Goal = row.Goal,
            InScope = BuildInScope(row, context),
            OutOfScope = context.OutOfScope.ToArray(),
            TargetRepo = row.TargetRepo,
            TargetPath = row.TargetPath,
            TargetPart = row.TargetPart,
            Dependencies = SelectDependencies(row),
            TechnicalBaseline = context.TechnicalBaseline.ToArray(),
            ProjectLocalGuide = context.ProjectLocalGuide.ToArray(),
            IntentBaseline = context.IntentBaseline.ToArray(),
            IntentReferences = BuildIntentReferences(row),
            RulesAndSpecs = ExtractRulesAndSpecs(row),
            AcceptanceCriteria = BuildAcceptanceCriteria(row, context),
            VerificationEvidence = BuildVerificationEvidence(context),
            ReviewMode = row.ReviewMode,
            CompletionAction = row.CompletionAction,
            LandingPolicy = row.LandingPolicy
        };
    }

    public static ReviewContextPacket ToReviewContextPacket(
        SubSliceRow row,
        ProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        return new ReviewContextPacket
        {
            SourceExecutionUnit = row.SourceExecutionUnit,
            ParentIntentRoot = context.ParentIntentRoot,
            IntentReferences = BuildIntentReferences(row),
            RulesAndSpecs = ExtractRulesAndSpecs(row),
            AcceptanceCriteria = BuildAcceptanceCriteria(row, context),
            DeterministicReviewChecks = context.DeterministicReviewChecks.ToArray(),
            ClarificationReturnPath = context.ClarificationReturnPath
        };
    }

    private static string[] BuildInScope(SubSliceRow row, ProjectionContext context)
    {
        var inScope = new string[context.AdditionalInScope.Count + 1];
        inScope[0] = row.TargetPart;

        for (var index = 0; index < context.AdditionalInScope.Count; index++)
        {
            inScope[index + 1] = context.AdditionalInScope[index];
        }

        return inScope;
    }

    private static string[] SelectDependencies(SubSliceRow row)
    {
        var dependencies = row.DependsOnSubslices.Count > 0
            ? row.DependsOnSubslices
            : row.DependsOn;

        return dependencies.ToArray();
    }

    private static string[] BuildIntentReferences(SubSliceRow row)
    {
        var intentReferences = new string[row.RelatedIntents.Count + row.SourceConcepts.Count];

        for (var index = 0; index < row.RelatedIntents.Count; index++)
        {
            intentReferences[index] = row.RelatedIntents[index];
        }

        for (var index = 0; index < row.SourceConcepts.Count; index++)
        {
            intentReferences[row.RelatedIntents.Count + index] = row.SourceConcepts[index];
        }

        return intentReferences;
    }

    private static string[] BuildVerificationEvidence(ProjectionContext context)
    {
        var missingEvidence = ProjectionConstants.RequiredVerificationEvidence
            .Where(requiredEvidence => !context.VerificationEvidence.Contains(requiredEvidence, StringComparer.Ordinal))
            .ToArray();

        if (missingEvidence.Length > 0)
        {
            throw new InvalidOperationException(
                $"Verification evidence is missing required entries: {string.Join(", ", missingEvidence)}.");
        }

        return context.VerificationEvidence.ToArray();
    }

    private static string[] BuildAcceptanceCriteria(SubSliceRow row, ProjectionContext context)
    {
        return context.AcceptanceCriteria.Count > 0
            ? context.AcceptanceCriteria.ToArray()
            : [row.SuccessSignal];
    }

    private static string[] ExtractRulesAndSpecs(SubSliceRow row)
    {
        return row.SourceConcepts
            .Where(IsRuleOrSpecPath)
            .ToArray();
    }

    private static bool IsRuleOrSpecPath(string path)
    {
        return ProjectionConstants.RulesAndSpecsPathSegments.Any(
            segment => path.Contains(segment, StringComparison.Ordinal));
    }
}
