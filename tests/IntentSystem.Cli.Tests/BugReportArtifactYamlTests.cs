using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugReportArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugReportArtifact()
    {
        var artifact = new BugReportArtifact
        {
            DomainSlug = "auth",
            BugId = "BUG-123",
            Title = "OAuth callback loop",
            ReportSource = "from-file",
            ProblemStatement = "Observed callback loop after login.\nAffects GitHub path.",
            SuspectedFailureLocus = "auth/callback handler state transition after provider return",
            OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL", "intents/rules/provider-interruption-and-retry.md"],
            AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
            AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            ClarificationCandidates = ["Should provider retry reuse the existing callback state token?"],
            LinkedExecutionUnits = ["G25", "G77"],
            LinkedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/178"],
            LinkedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180"],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
        };

        var yaml = BugReportArtifactYaml.Serialize(artifact);
        var roundTripped = BugReportArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.DomainSlug, roundTripped.DomainSlug);
        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.Title, roundTripped.Title);
        Assert.Equal(artifact.ReportSource, roundTripped.ReportSource);
        Assert.Equal(artifact.ProblemStatement, roundTripped.ProblemStatement);
        Assert.Equal(artifact.SuspectedFailureLocus, roundTripped.SuspectedFailureLocus);
        Assert.Equal(artifact.OriginalInstructionRefs, roundTripped.OriginalInstructionRefs);
        Assert.Equal(artifact.AffectedIntentRefs, roundTripped.AffectedIntentRefs);
        Assert.Equal(artifact.AffectedRuleSpecRefs, roundTripped.AffectedRuleSpecRefs);
        Assert.Equal(artifact.ClarificationCandidates, roundTripped.ClarificationCandidates);
        Assert.Equal(artifact.LinkedExecutionUnits, roundTripped.LinkedExecutionUnits);
        Assert.Equal(artifact.LinkedIssueRefs, roundTripped.LinkedIssueRefs);
        Assert.Equal(artifact.LinkedPrRefs, roundTripped.LinkedPrRefs);
        Assert.Equal(artifact.LinkedReviewRefs, roundTripped.LinkedReviewRefs);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        domain_slug: auth
        bug_id: BUG-123
        report_source: from-file
        problem_statement: "Observed callback loop."
        suspected_failure_locus: "callback state transition"
        original_instruction_refs: []
        affected_intent_refs: []
        affected_rule_spec_refs: []
        clarification_candidates: []
        linked_execution_units: []
        linked_issue_refs: []
        linked_pr_refs: []
        linked_review_refs: []
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(yaml));

        Assert.Contains("title", exception.Message, StringComparison.Ordinal);
    }
}
