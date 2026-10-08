using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class SoloConductorApprovalGateTests
{
    private const string Unit = "G856";
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const int Pr = 1871;
    private const string H1 = "1111111111111111111111111111111111111111";
    private const string H2 = "2222222222222222222222222222222222222222";
    private static readonly DateTimeOffset T1 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private const string GenericTemplate = """
        ## Independent subagent review: approve

        - reviewer: independent subagent review
        - execution unit: <unit>
        - kind: implementation
        - head SHA: <full-head-sha>
        - verdict: approve

        ### Blocking findings

        - none

        ### Notes

        <actual reviewer notes>
        """;

    [Fact]
    public void GenericCurrentHeadApproval_SatisfiesGate()
    {
        var row = ParseRow(10, GenericBody(Unit, H2), "COMMENTED", H2, "ReviewBot", T1);

        var result = Evaluate([row]);

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, result.Decision);
        Assert.Null(result.Cause);
        Assert.Equal(10, Assert.Single(result.QualifyingReviews).ReviewId);
    }

    [Fact]
    public void TrustedCommitMismatch_CanBeRepairedOnlyByLaterCurrentHeadApprovalFromSameLogin()
    {
        var mismatch = ParseRow(20, GenericBody(Unit, H1), "COMMENTED", H2, "Reviewer", T1);
        var repair = ParseRow(21, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var repaired = Evaluate([mismatch, repair]);
        var otherLogin = Evaluate([mismatch, ParseRow(22, GenericBody(Unit, H2), "APPROVED", H2, "other", T1.AddMinutes(1))]);
        var staleRepair = Evaluate([mismatch, ParseRow(23, GenericBody(Unit, H1), "APPROVED", H1, "Reviewer", T1.AddMinutes(1))]);
        var pendingRepair = Evaluate([mismatch, ParseRow(24, GenericBody(Unit, H2), "PENDING", H2, "Reviewer", null, includeSubmittedAt: false)]);

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([20L], repaired.SupersededInvalidReviewIds);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, otherLogin.Cause);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, staleRepair.Cause);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, pendingRepair.Cause);
    }

    [Fact]
    public void InvalidRestReviewUrlCreatesLoginScopedObligationAndSameLoginCanRepairIt()
    {
        var malformedUrl = ParseRowWithHtmlUrl(60, GenericBody(Unit, H2), "Reviewer", T1, 42);
        var otherLoginRepair = ParseRow(61, GenericBody(Unit, H2), "APPROVED", H2, "other-login", T1.AddMinutes(1));
        var sameLoginRepair = ParseRow(62, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var refused = Evaluate([malformedUrl]);
        var wrongLogin = Evaluate([malformedUrl, otherLoginRepair]);
        var repaired = Evaluate([malformedUrl, sameLoginRepair]);

        Assert.Equal(ReviewParseFailure.Malformed, malformedUrl.ObserverFailureKind);
        Assert.False(malformedUrl.ObserverAccepted);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        var obligation = Assert.Single(refused.Obligations);
        Assert.Equal("invalid-evidence", obligation.Kind);
        Assert.Equal("Reviewer", obligation.Identity);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, wrongLogin.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([60L], repaired.SupersededInvalidReviewIds);
    }

    [Fact]
    public void ContradictorySecondHeadingCreatesApprovalOnlyObligationAndSameLoginCanRepairIt()
    {
        var contradictoryBody = GenericBody(Unit, H2)
            + "\n## Independent subagent review: request-changes\n";
        var contradictory = ParseRow(63, contradictoryBody, "COMMENTED", H2, "reviewer", T1);
        var otherLogin = ParseRow(64, GenericBody(Unit, H2), "APPROVED", H2, "other", T1.AddMinutes(1));
        var sameLogin = ParseRow(65, GenericBody(Unit, H2), "APPROVED", H2, "REVIEWER", T1.AddMinutes(1));

        var refused = Evaluate([contradictory]);
        var wrongRepair = Evaluate([contradictory, otherLogin]);
        var repaired = Evaluate([contradictory, sameLogin]);

        Assert.True(contradictory.ObserverAccepted);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        Assert.Equal("reviewer", Assert.Single(refused.Obligations).Identity);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, wrongRepair.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([63L], repaired.SupersededInvalidReviewIds);
    }

    [Fact]
    public void G855ConflictRemainsObservableAfterApprovalRecovery()
    {
        var conflictingBody = GenericBody(Unit, H2).Replace(
            "- verdict: approve\n\n### Blocking findings",
            "- verdict: approve\n- verdict: request-changes\n\n### Blocking findings",
            StringComparison.Ordinal);
        var conflict = ParseRow(66, conflictingBody, "COMMENTED", H2, "reviewer", T1);
        var repair = ParseRow(67, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var result = Evaluate([conflict, repair]);

        Assert.Equal(IndependentReviewEvidenceKind.IdentityConflict, conflict.Classification);
        Assert.False(conflict.ObserverAccepted);
        Assert.Equal(ReviewParseFailure.IdentityConflict, conflict.ObserverFailureKind);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, result.Decision);
        Assert.Equal([66L], result.SupersededInvalidReviewIds);
    }

    [Theory]
    [InlineData("metadata-verdict")]
    [InlineData("metadata-head")]
    public void ConflictingMetadataCreatesRepairableLoginObligation(string conflictKind)
    {
        var body = conflictKind == "metadata-verdict"
            ? GenericBody(Unit, H2).Replace(
                "- verdict: approve\n\n### Blocking findings",
                "- verdict: approve\n- verdict: request-changes\n\n### Blocking findings",
                StringComparison.Ordinal)
            : GenericBody(Unit, H2).Replace(
                $"- head SHA: {H2}\n",
                $"- head SHA: {H2}\n- head SHA: {H1}\n",
                StringComparison.Ordinal);
        var invalid = ParseRow(68, body, "COMMENTED", H2, "reviewer", T1);
        var repair = ParseRow(69, GenericBody(Unit, H2), "COMMENTED", H2, "reviewer", T1.AddMinutes(1));

        var refused = Evaluate([invalid]);
        var repaired = Evaluate([invalid, repair]);

        Assert.Equal(ReviewParseFailure.IdentityConflict, invalid.ObserverFailureKind);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([68L], repaired.SupersededInvalidReviewIds);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    public void InvalidCanonicalCitationCreatesRepairableLoginObligation(string citationKind)
    {
        var citation = citationKind == "missing"
            ? "Recorded as unavailable citation evidence."
            : "Recorded as `.intent-cli/issues/G999/reviews/missing.json` by `intent-cli review cross-runtime record`.";
        var body = GenericBody(Unit, H2).Replace("### Notes", citation + "\n\n### Notes", StringComparison.Ordinal);
        var invalid = ParseRow(71, body, "COMMENTED", H2, "reviewer", T1);
        var repair = ParseRow(72, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var refused = Evaluate([invalid]);
        var repaired = Evaluate([invalid, repair]);

        Assert.True(invalid.BodyEvidence!.HasCitationAssertion);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([71L], repaired.SupersededInvalidReviewIds);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    public void CanonicalCitationMustBePresentAndValidateBeforeApproval(string citationKind)
    {
        var bundle = CreateLocalGateBundle();
        var canonicalBody = bundle.Bodies["claude"];
        var citationLine = canonicalBody.Split('\n').Single(line => line.StartsWith("Recorded as `", StringComparison.Ordinal));
        string body;
        if (citationKind == "missing")
        {
            body = canonicalBody.Replace(citationLine + "\n", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            var pathStart = "Recorded as `".Length;
            var pathEnd = citationLine.IndexOf('`', pathStart);
            var path = citationLine[pathStart..pathEnd];
            body = canonicalBody.Replace(citationLine, citationLine.Replace(path, path + ".missing", StringComparison.Ordinal), StringComparison.Ordinal);
        }

        var invalid = ParseRow(92, body, "COMMENTED", H2, "reviewer", T1, bundle.Read);
        var repair = ParseRow(93, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1), bundle.Read);

        var refused = Evaluate([invalid]);
        var repaired = Evaluate([invalid, repair]);

        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([92L], repaired.SupersededInvalidReviewIds);
    }

    [Fact]
    public void LaterInvalidReviewReinstatesObligationAfterRepair()
    {
        var firstInvalid = ParseRow(73, GenericBody(Unit, H1), "COMMENTED", H2, "reviewer", T1);
        var repair = ParseRow(74, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));
        var laterInvalid = ParseRow(75, GenericBody(Unit, H1), "COMMENTED", H2, "reviewer", T1.AddMinutes(2));

        var result = Evaluate([firstInvalid, repair, laterInvalid]);

        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, result.Cause);
        Assert.Equal("reviewer", Assert.Single(result.Obligations).Identity);
        Assert.Equal(new long?[] { 75L }, result.Obligations[0].ReviewIds);
        Assert.Equal([73L], result.SupersededInvalidReviewIds);
    }

    [Fact]
    public void ExactDuplicateRowIsDeduplicatedToOneQualifyingReview()
    {
        var approval = ParseRow(76, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1);

        var result = Evaluate([approval, approval]);

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, result.Decision);
        Assert.Equal(76L, Assert.Single(result.QualifyingReviews).ReviewId);
    }

    [Fact]
    public void EmptyAndStaleOnlyInventoriesRemainReviewMissing()
    {
        var stale = ParseRow(77, GenericBody(Unit, H1), "APPROVED", H1, "reviewer", T1);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, Evaluate([]).Cause);
        Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, Evaluate([stale]).Cause);
    }

    [Fact]
    public void SameHeadBlockerRequiresRepairFromSameLogin()
    {
        var blocker = ParseRow(78, GenericBody(Unit, H2, "request-changes"), "COMMENTED", H2, "blocked-login", T1);
        var otherLoginApprove = ParseRow(79, GenericBody(Unit, H2), "APPROVED", H2, "other-login", T1.AddMinutes(1));

        var result = Evaluate([blocker, otherLoginApprove]);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, result.Cause);
        Assert.Equal("blocked-login", Assert.Single(result.Obligations).Identity);
    }

    [Fact]
    public void OptionalRuntimeDoesNotChangeGenericBlockerIdentity()
    {
        var genericWithRuntime = GenericBody(Unit, H2, "request-changes").Replace(
            $"- kind: implementation\n- head SHA: {H2}",
            $"- kind: implementation\n- runtime: claude\n- conductor runtime: claude\n- head SHA: {H2}",
            StringComparison.Ordinal);
        var blocker = ParseRow(80, genericWithRuntime, "COMMENTED", H2, "blocked-login", T1);
        var otherLoginApprove = ParseRow(81, GenericBody(Unit, H2), "APPROVED", H2, "other-login", T1.AddMinutes(1));

        var result = Evaluate([blocker, otherLoginApprove]);
        var sameLoginApprove = ParseRow(82, GenericBody(Unit, H2), "APPROVED", H2, "BLOCKED-LOGIN", T1.AddMinutes(1));
        var repaired = Evaluate([blocker, sameLoginApprove]);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, result.Cause);
        var obligation = Assert.Single(result.Obligations);
        Assert.Equal("github-login", obligation.IdentityKind);
        Assert.Equal("blocked-login", obligation.Identity);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
    }

    [Theory]
    [InlineData("G999", "implementation")]
    [InlineData(Unit, "design")]
    public void CoherentGenericForeignUnitOrDesignReviewIsNonqualifying(string unit, string kind)
    {
        var body = GenericBody(unit, H2).Replace("- kind: implementation", $"- kind: {kind}", StringComparison.Ordinal);
        var foreign = ParseRow(82, body, "COMMENTED", H2, "foreign-login", T1);
        var approval = ParseRow(83, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var foreignOnly = Evaluate([foreign]);
        var withApproval = Evaluate([foreign, approval]);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, foreignOnly.Cause);
        Assert.Empty(foreignOnly.Obligations);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, withApproval.Decision);
    }

    [Theory]
    [InlineData("heading")]
    [InlineData("submitted-state")]
    public void LegacyIndependentRequestChangesStillCreatesLoginObligation(string source)
    {
        const string legacy = "## Independent subagent review: notes\n\nlegacy reviewer prose";
        var body = source == "heading" ? "## Independent subagent review: request-changes\n\nlegacy prose" : legacy;
        var state = source == "submitted-state" ? "CHANGES_REQUESTED" : "COMMENTED";
        var row = ParseRow(84, body, state, H2, "legacy-reviewer", T1);

        var result = Evaluate([row]);

        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, result.Cause);
        Assert.Equal("legacy-reviewer", Assert.Single(result.Obligations).Identity);
    }

    [Fact]
    public void LegacyIndependentProseWithoutBlockerRemainsNonqualifyingProvenance()
    {
        var legacy = ParseRow(85, "## Independent subagent review: notes\n\nlegacy reviewer prose", "COMMENTED", H2, "legacy-reviewer", T1);

        var result = Evaluate([legacy]);

        Assert.Equal(IndependentReviewEvidenceKind.Provenance, legacy.Classification);
        Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, result.Cause);
        Assert.Empty(result.Obligations);
    }

    [Fact]
    public void PartialStructuredMetadataCreatesRepairableInvalidEvidenceObligation()
    {
        var partial = GenericBody(Unit, H2).Replace("- kind: implementation\n", string.Empty, StringComparison.Ordinal);
        var invalid = ParseRow(94, partial, "COMMENTED", H2, "reviewer", T1);
        var repair = ParseRow(95, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var refused = Evaluate([invalid]);
        var repaired = Evaluate([invalid, repair]);

        Assert.False(invalid.BodyEvidence!.IsStructured);
        Assert.True(invalid.BodyEvidence.HasStructuredMetadata);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([94L], repaired.SupersededInvalidReviewIds);
    }

    [Fact]
    public void UnknownSubmittedStateCreatesRepairableInvalidEvidenceObligation()
    {
        var unknown = ParseRow(96, GenericBody(Unit, H2), "UNKNOWN", H2, "reviewer", T1);
        var repair = ParseRow(97, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1.AddMinutes(1));

        var refused = Evaluate([unknown]);
        var repaired = Evaluate([unknown, repair]);

        Assert.Equal(ReviewParseFailure.Malformed, unknown.ObserverFailureKind);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, refused.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, repaired.Decision);
        Assert.Equal([96L], repaired.SupersededInvalidReviewIds);
    }

    [Fact]
    public void DismissedRequestChangesIsExcludedAndCannotRepairEarlierBlocker()
    {
        var dismissedBlocker = ParseRow(86, GenericBody(Unit, H2, "request-changes"), "DISMISSED", H2, "reviewer", T1);
        var blocker = ParseRow(87, GenericBody(Unit, H2, "request-changes"), "COMMENTED", H2, "reviewer", T1);
        var dismissedRepair = ParseRow(88, GenericBody(Unit, H2), "DISMISSED", H2, "reviewer", T1.AddMinutes(1));

        var dismissedOnly = Evaluate([dismissedBlocker]);
        var withDismissedRepair = Evaluate([blocker, dismissedRepair]);
        var withOtherApproval = Evaluate([dismissedBlocker, ParseRow(89, GenericBody(Unit, H2), "APPROVED", H2, "other-login", T1.AddMinutes(1))]);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, dismissedOnly.Cause);
        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, withDismissedRepair.Cause);
        Assert.Equal("reviewer", Assert.Single(withDismissedRepair.Obligations).Identity);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, withOtherApproval.Decision);
    }

    [Theory]
    [InlineData("APPROVED", "request-changes")]
    [InlineData("CHANGES_REQUESTED", "approve")]
    public void RequestChangesAssertionBlocksRegardlessOfReviewState(string state, string verdict)
    {
        var result = Evaluate([ParseRow(30, GenericBody(Unit, H2, verdict), state, H2, "Reviewer", T1)]);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, result.Cause);
        var obligation = Assert.Single(result.Obligations);
        Assert.Equal("github-login", obligation.IdentityKind);
        Assert.Equal("Reviewer", obligation.Identity);
    }

    [Fact]
    public void PreviousHeadBlockerNeedsLaterCurrentHeadApprovalFromSameLogin()
    {
        var oldBlock = ParseRow(40, GenericBody(Unit, H1, "request-changes"), "COMMENTED", H1, "reviewer", T1);
        var staleLaterApproval = ParseRow(41, GenericBody(Unit, H1), "APPROVED", H1, "reviewer", T1.AddMinutes(1));
        var currentRepair = ParseRow(42, GenericBody(Unit, H2), "COMMENTED", H2, "REVIEWER", T1.AddMinutes(2));

        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, Evaluate([oldBlock, staleLaterApproval]).Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, Evaluate([oldBlock, staleLaterApproval, currentRepair]).Decision);
    }

    [Fact]
    public void UnscopableNamedReviewCannotBeSupersededAndPendingWithoutTimestampIsExcluded()
    {
        var unscopable = ParseRow(null, GenericBody(Unit, H2), "COMMENTED", H2, "reviewer", T1);
        var missingLogin = ParseRow(52, GenericBody(Unit, H2), "COMMENTED", H2, null, T1);
        var goodApproval = ParseRow(51, GenericBody(Unit, H2), "APPROVED", H2, "other", T1.AddMinutes(1));
        var pending = ParseRow(null, GenericBody(Unit, H2), "PENDING", H2, null, null, includeSubmittedAt: false);

        var refused = Evaluate([unscopable, goodApproval]);
        var missingLoginRefused = Evaluate([missingLogin, goodApproval]);
        var pendingExcluded = Evaluate([pending, goodApproval]);

        Assert.Equal(SoloConductorApprovalGate.CauseUnscopableEvidence, refused.Cause);
        Assert.Equal(SoloConductorApprovalGate.CauseUnscopableEvidence, missingLoginRefused.Cause);
        Assert.Equal(SoloConductorApprovalGate.RepairUnavailableIdentity, refused.RepairUnavailableReason);
        Assert.Equal([null], refused.UnscopableReviewIds);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, pendingExcluded.Decision);
    }

    [Fact]
    public void NonObjectReviewRowFailsInventoryWithoutThrowing()
    {
        using var document = JsonDocument.Parse("\"not an object\"");
        var row = IndependentReviewEvidence.ParseRow(
            document.RootElement,
            Unit,
            Domain,
            Team,
            Repo,
            Pr,
            new CrossRuntimeReviewReadResult([], []));

        var result = Evaluate([row]);

        Assert.Equal(SoloConductorApprovalGate.CauseInventoryUnavailable, result.Cause);
        Assert.Contains("not a JSON object", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedDeletedUserReviewIsIgnoredBeforeLoginValidation()
    {
        var unrelated = ParseRow(60, "Ordinary review comment", "APPROVED", H2, null, T1);
        var approval = ParseRow(61, GenericBody(Unit, H2), "COMMENTED", H2, "reviewer", T1.AddMinutes(1));

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, Evaluate([unrelated, approval]).Decision);
    }

    [Fact]
    public void DuplicateRestIdWithDifferentPayloadRefusesWholeInventory()
    {
        var first = ParseRow(70, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1);
        var second = ParseRow(70, GenericBody(Unit, H2).Replace("actual reviewer notes", "different payload", StringComparison.Ordinal), "APPROVED", H2, "reviewer", T1);

        var result = Evaluate([first, second]);

        Assert.Equal(SoloConductorApprovalGate.CauseInventoryUnavailable, result.Cause);
    }

    [Fact]
    public void SameTimestampRowsUseNumericReviewIdForStrictSupersession()
    {
        var blocker9 = ParseRow(9, GenericBody(Unit, H2, "request-changes"), "COMMENTED", H2, "reviewer", T1);
        var approval10 = ParseRow(10, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1);
        var blocker10 = ParseRow(10, GenericBody(Unit, H2, "request-changes"), "COMMENTED", H2, "reviewer", T1);
        var approval9 = ParseRow(9, GenericBody(Unit, H2), "APPROVED", H2, "reviewer", T1);

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, Evaluate([approval10, blocker9]).Decision);
        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, Evaluate([approval9, blocker10]).Cause);
    }

    [Fact]
    public void DeclaredGateRequiresCanonicalPostedApprovalsForBothRelations()
    {
        var bundle = CreateLocalGateBundle(includeExtraCrossRuntime: true);
        var same = ParseRow(80, bundle.Bodies["claude"], "COMMENTED", H2, "same-reviewer", T1, bundle.Read);
        var cross = ParseRow(81, bundle.Bodies["cursor"], "COMMENTED", H2, "cross-reviewer", T1.AddMinutes(1), bundle.Read);
        var extraCross = ParseRow(82, bundle.Bodies["opencode"], "COMMENTED", H2, "extra-reviewer", T1.AddMinutes(2), bundle.Read);
        var genericOnly = ParseRow(83, GenericBody(Unit, H2), "APPROVED", H2, "generic", T1.AddMinutes(3), bundle.Read);

        var satisfied = Evaluate([same, cross], bundle.Gate);
        var missingCross = Evaluate([same, genericOnly], bundle.Gate);
        var oneCrossSlotIsEnough = Evaluate([same, cross, genericOnly], bundle.Gate);
        var genericBlocker = ParseRow(84, GenericBody(Unit, H2, "request-changes"), "COMMENTED", H2, "blocker", T1.AddMinutes(4), bundle.Read);
        var blocked = Evaluate([same, cross, genericBlocker], bundle.Gate);

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, satisfied.Decision);
        Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, missingCross.Cause);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, oneCrossSlotIsEnough.Decision);
        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, blocked.Cause);
        Assert.Equal("blocker", Assert.Single(blocked.Obligations).Identity);
    }

    [Fact]
    public void CanonicalCurrentHeadApprovalCanRepairSameLoginsInvalidEvidence()
    {
        var bundle = CreateLocalGateBundle();
        var mismatch = ParseRow(90, GenericBody(Unit, H1), "COMMENTED", H2, "same-reviewer", T1, bundle.Read);
        var same = ParseRow(91, bundle.Bodies["claude"], "COMMENTED", H2, "same-reviewer", T1.AddMinutes(1), bundle.Read);
        var cross = ParseRow(92, bundle.Bodies["cursor"], "COMMENTED", H2, "cross-reviewer", T1.AddMinutes(2), bundle.Read);

        var result = Evaluate([mismatch, same, cross], bundle.Gate);

        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, result.Decision);
        Assert.Equal([90L], result.SupersededInvalidReviewIds);
    }

    [Fact]
    public void CanonicalRuntimeBlockerIsNotClearedBySameLoginApprovingAnotherRuntime()
    {
        var records = CreateCanonicalRecords(
            ("claude", CrossRuntimeReviewVerdict.RequestChanges),
            ("cursor", CrossRuntimeReviewVerdict.Approve));
        var blockedBody = ReviewCrossRuntimeCommand.RenderCommentBody(records.Records[0].Record, records.Records[0].RelativePath);
        var otherRuntimeBody = ReviewCrossRuntimeCommand.RenderCommentBody(records.Records[1].Record, records.Records[1].RelativePath);
        var blocked = ParseRow(100, blockedBody, "COMMENTED", H2, "shared-login", T1, records.Read);
        var otherRuntimeApprove = ParseRow(101, otherRuntimeBody, "COMMENTED", H2, "shared-login", T1.AddMinutes(1), records.Read);

        var result = Evaluate([blocked, otherRuntimeApprove]);

        Assert.Equal(SoloConductorApprovalGate.CauseReviewBlocked, result.Cause);
        var obligation = Assert.Single(result.Obligations);
        Assert.Equal("runtime", obligation.IdentityKind);
        Assert.Equal("claude", obligation.Identity);
    }

    [Fact]
    public void CoherentForeignCanonicalCitationIsExcludedButForeignHeadMismatchTaintsByLogin()
    {
        var foreignRecord = new CrossRuntimeReviewRecord
        {
            ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
            Repo = Repo,
            Pr = Pr,
            HeadSha = H2,
            ExecutionUnit = "G999",
            Domain = Domain,
            Team = Team,
            Kind = CrossRuntimeReviewRecord.KindImplementation,
            Runtime = "claude",
            RuntimeVersion = "test-runtime-1",
            ConductorRuntime = "claude",
            Relation = CrossRuntimeReviewRecord.RelationSameRuntime,
            Verdict = CrossRuntimeReviewVerdict.Approve,
            BlockingFindings = [],
            Notes = ["foreign unit fixture"],
            RecordedAt = T1,
            RawVerdictFile = ".intent-cli/test/foreign.raw-verdict",
            RawVerdictSha256 = new string('b', 64),
        };
        var path = CrossRuntimeReviewStore.RecordRelativePath(foreignRecord);
        var localRead = new CrossRuntimeReviewReadResult(
            [new CrossRuntimeReviewStoredRecord(Path.GetFileName(path), path, foreignRecord)],
            []);
        var foreignBody = ReviewCrossRuntimeCommand.RenderCommentBody(foreignRecord, path);
        var coherentForeign = ParseRow(110, foreignBody, "COMMENTED", H2, "foreign-login", T1, localRead);
        var expectedApproval = ParseRow(111, GenericBody(Unit, H2), "APPROVED", H2, "expected-login", T1.AddMinutes(1), localRead);
        var badForeignRequest = ParseRow(112, GenericBody("G999", H1, "request-changes"), "COMMENTED", H2, "foreign-login", T1.AddMinutes(2), localRead);

        Assert.True(coherentForeign.CitedRecordValidatedForDeclaredUnit);
        Assert.False(coherentForeign.CitedRecordValidatedForExpected);
        Assert.Equal(SoloConductorApprovalGate.DecisionSatisfied, Evaluate([coherentForeign, expectedApproval]).Decision);
        Assert.Equal(SoloConductorApprovalGate.CauseInvalidEvidence, Evaluate([coherentForeign, expectedApproval, badForeignRequest]).Cause);
    }

    private static SoloConductorApprovalEvaluation Evaluate(
        IReadOnlyList<IndependentReviewRowEvidence> rows,
        CrossRuntimeReviewGateResult? localGate = null) =>
        SoloConductorApprovalGate.Evaluate(rows, Unit, H2, localGate);

    private static IndependentReviewRowEvidence ParseRow(
        long? id,
        string? body,
        string? state,
        string? commitId,
        string? login,
        DateTimeOffset? submittedAt,
        CrossRuntimeReviewReadResult? localReviews = null,
        bool includeSubmittedAt = true)
    {
        var row = new Dictionary<string, object?>();
        if (id is not null) row["id"] = id;
        if (state is not null) row["state"] = state;
        row["commit_id"] = commitId;
        row["body"] = body;
        row["html_url"] = id is null ? null : $"https://github.com/{Repo}/pull/{Pr}#pullrequestreview-{id.Value}";
        if (includeSubmittedAt)
        {
            row["submitted_at"] = submittedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }
        row["user"] = login is null ? null : new { login };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(row));
        return IndependentReviewEvidence.ParseRow(
            document.RootElement,
            Unit,
            Domain,
            Team,
            Repo,
            Pr,
            localReviews ?? new CrossRuntimeReviewReadResult([], []));
    }

    private static IndependentReviewRowEvidence ParseRowWithHtmlUrl(
        long id,
        string body,
        string login,
        DateTimeOffset submittedAt,
        object? htmlUrl)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["state"] = "COMMENTED",
            ["commit_id"] = H2,
            ["body"] = body,
            ["html_url"] = htmlUrl,
            ["submitted_at"] = submittedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["user"] = new { login },
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(row));
        return IndependentReviewEvidence.ParseRow(
            document.RootElement,
            Unit,
            Domain,
            Team,
            Repo,
            Pr,
            new CrossRuntimeReviewReadResult([], []));
    }

    private static string GenericBody(string unit, string head, string verdict = "approve") =>
        GenericTemplate
            .Replace("<unit>", unit, StringComparison.Ordinal)
            .Replace("<full-head-sha>", head, StringComparison.Ordinal)
            .Replace("approve", verdict, StringComparison.Ordinal);

    private static LocalGateBundle CreateLocalGateBundle(bool includeExtraCrossRuntime = false)
    {
        var pairs = includeExtraCrossRuntime
            ? new[]
            {
                ("claude", CrossRuntimeReviewVerdict.Approve),
                ("cursor", CrossRuntimeReviewVerdict.Approve),
                ("opencode", CrossRuntimeReviewVerdict.Approve),
            }
            : new[]
            {
                ("claude", CrossRuntimeReviewVerdict.Approve),
                ("cursor", CrossRuntimeReviewVerdict.Approve),
            };
        var records = CreateCanonicalRecords(pairs);
        var resolution = new CrossRuntimeReviewResolution
        {
            Resolved = true,
            ExecutionUnit = Unit,
            Domain = Domain,
            Team = Team,
        };
        var declaration = new CrossRuntimeReviewTeamDeclaration
        {
            Team = $"{Domain}/{Team}",
            ConductorRuntime = "claude",
            Repos = [Repo],
        };
        var gate = CrossRuntimeReviewGate.Evaluate(declaration, resolution, H2, records.Read);
        return new LocalGateBundle(records.Read, gate, records.Bodies);
    }

    private static CanonicalRecords CreateCanonicalRecords(params (string Runtime, string Verdict)[] entries)
    {
        var records = new List<CrossRuntimeReviewStoredRecord>();
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var (runtime, verdict) in entries)
        {
            var record = new CrossRuntimeReviewRecord
            {
                ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
                Repo = Repo,
                Pr = Pr,
                HeadSha = H2,
                ExecutionUnit = Unit,
                Domain = Domain,
                Team = Team,
                Kind = CrossRuntimeReviewRecord.KindImplementation,
                Runtime = runtime,
                RuntimeVersion = "test-runtime-1",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
                Verdict = verdict,
                BlockingFindings = [],
                Notes = ["actual canonical fixture"],
                RecordedAt = T1.AddMinutes(offset++),
                RawVerdictFile = $".intent-cli/test/{runtime}.raw-verdict",
                RawVerdictSha256 = new string('a', 64),
            };
            var path = CrossRuntimeReviewStore.RecordRelativePath(record);
            records.Add(new CrossRuntimeReviewStoredRecord(Path.GetFileName(path), path, record));
            bodies[runtime] = ReviewCrossRuntimeCommand.RenderCommentBody(record, path);
        }

        return new CanonicalRecords(new CrossRuntimeReviewReadResult(records, []), bodies, records);
    }

    private sealed record LocalGateBundle(
        CrossRuntimeReviewReadResult Read,
        CrossRuntimeReviewGateResult Gate,
        IReadOnlyDictionary<string, string> Bodies);

    private sealed record CanonicalRecords(
        CrossRuntimeReviewReadResult Read,
        IReadOnlyDictionary<string, string> Bodies,
        IReadOnlyList<CrossRuntimeReviewStoredRecord> Records);
}
