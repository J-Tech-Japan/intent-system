using System.Globalization;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ScopedModelResolutionEvidencePolicyG814Tests
{
    private const string BaselineInvocation = "claude --model m-test --effort medium";
    private const string CandidateInvocation = "claude --model m-candidate --effort high";
    private static readonly DateTimeOffset T1 = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddMinutes(1);

    [Fact]
    public void ExplicitAttributionLabelDoesNotAffectBaselineKey_AndLegacyPositiveCannotSupplyBaseline()
    {
        var (target, explicitKey) = CreateKey("friendly label A", "m-test");
        var sameScopeOtherLabel = ScopedVerified(explicitKey, T1, BaselineInvocation, "friendly label B");

        var selected = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline([sameScopeOtherLabel], explicitKey);
        Assert.True(selected.Resolved, selected.Reason);
        Assert.Same(sameScopeOtherLabel, selected.Entry);

        var legacyPositive = LegacyVerified(target.Kind, "different legacy label", T2, BaselineInvocation);
        var missing = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline([legacyPositive], explicitKey);
        Assert.False(missing.Resolved);
        Assert.Equal("missing-scoped-baseline", missing.Reason);
    }

    [Fact]
    public void NewestIncompleteOrDifferentGenerationRowIsSelectedBeforeValidation()
    {
        var (_, key) = CreateKey("friendly", "m-test");
        var older = ScopedVerified(key, T1, BaselineInvocation);
        var newerIncomplete = ScopedVerified(key, T2, BaselineInvocation) with { Host = null };

        var incomplete = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline([older, newerIncomplete], key);
        Assert.True(incomplete.Resolved, incomplete.Reason);
        Assert.Equal("baseline-selected", incomplete.Reason);
        Assert.Same(newerIncomplete, incomplete.Entry);
        Assert.Equal(1, incomplete.AppendIndex);

        var newerDifferentGeneration = ScopedVerified(key, T2, BaselineInvocation,
            host: "another-machine", pid: 9898, processStartTime: UtcString(T2), digest: "newer-digest");
        var selected = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline([older, newerDifferentGeneration], key);
        Assert.True(selected.Resolved, selected.Reason);
        Assert.Same(newerDifferentGeneration, selected.Entry);
    }

    [Fact]
    public void InformalLatestEqualTimeMappingsConflict_ButIdenticalDuplicatesAreHarmless()
    {
        var (_, key) = CreateKey("friendly", requestedModel: null, effort: "medium");
        var first = ScopedVerified(key, T2, "claude --model m-one --effort medium", observedModel: "m-one");
        var conflict = ScopedVerified(key, T2, "claude --model m-two --effort medium", observedModel: "m-two");

        var conflicting = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline([first, conflict], key);
        Assert.False(conflicting.Resolved);
        Assert.Equal("mapping-conflict", conflicting.Reason);

        var duplicate = ScopedVerified(key, T2, first.FullInvocation!, observedModel: "m-one");
        var identical = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline([first, duplicate], key);
        Assert.True(identical.Resolved, identical.Reason);
        Assert.Same(duplicate, identical.Entry);
        Assert.Equal(1, identical.AppendIndex);
    }

    [Fact]
    public void LegacyAndScopedRefusalDomainsDoNotClearEachOther()
    {
        var (target, key) = CreateKey("friendly", "m-test");
        var legacyRefusal = LegacyRefused(target.Kind, "unrelated label", T1, BaselineInvocation);
        var scopedPositive = ScopedVerified(key, T2, BaselineInvocation);
        var blockedLegacy = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(
            [legacyRefusal, scopedPositive], key, BaselineInvocation);
        Assert.False(blockedLegacy.Permitted);
        Assert.Same(legacyRefusal, blockedLegacy.BlockingLegacyRefusal);
        Assert.Null(blockedLegacy.BlockingScopedRefusal);

        var scopedRefusal = ScopedRefused(key, T1, BaselineInvocation);
        var legacyPositive = LegacyVerified(target.Kind, "unrelated label", T2, BaselineInvocation);
        var blockedScoped = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(
            [scopedRefusal, legacyPositive], key, BaselineInvocation);
        Assert.False(blockedScoped.Permitted);
        Assert.Null(blockedScoped.BlockingLegacyRefusal);
        Assert.Same(scopedRefusal, blockedScoped.BlockingScopedRefusal);
    }

    [Fact]
    public void LaterPositiveClearsOnlyItsOwnRefusalDomain()
    {
        var (target, key) = CreateKey("friendly", "m-test");
        var legacyRefusal = LegacyRefused(target.Kind, "legacy label", T1, BaselineInvocation);
        var laterLegacyPositive = LegacyVerified(target.Kind, "another label", T2, BaselineInvocation);
        var scopedRefusal = ScopedRefused(key, T1, BaselineInvocation);

        var scopedStillBlocks = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(
            [legacyRefusal, laterLegacyPositive, scopedRefusal], key, BaselineInvocation);
        Assert.False(scopedStillBlocks.Permitted);
        Assert.Null(scopedStillBlocks.BlockingLegacyRefusal);
        Assert.Same(scopedRefusal, scopedStillBlocks.BlockingScopedRefusal);

        var legacyStillBlocks = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(
            [scopedRefusal, ScopedVerified(key, T2, BaselineInvocation), legacyRefusal], key, BaselineInvocation);
        Assert.False(legacyStillBlocks.Permitted);
        Assert.Same(legacyRefusal, legacyStillBlocks.BlockingLegacyRefusal);
        Assert.Null(legacyStillBlocks.BlockingScopedRefusal);
    }

    [Fact]
    public void RefusalWinsEqualTimestampInEitherAppendOrder_ForBothDomains()
    {
        var (target, key) = CreateKey("friendly", "m-test");
        var legacyRefusal = LegacyRefused(target.Kind, "legacy label", T2, CandidateInvocation);
        var legacyPositive = LegacyVerified(target.Kind, "other label", T2, CandidateInvocation);
        var scopedRefusal = ScopedRefused(key, T2, CandidateInvocation);
        var scopedPositive = ScopedVerified(key, T2, CandidateInvocation);

        AssertBlocked([legacyRefusal, legacyPositive], key, CandidateInvocation, legacy: true);
        AssertBlocked([legacyPositive, legacyRefusal], key, CandidateInvocation, legacy: true);
        AssertBlocked([scopedRefusal, scopedPositive], key, CandidateInvocation, legacy: false);
        AssertBlocked([scopedPositive, scopedRefusal], key, CandidateInvocation, legacy: false);
    }

    [Fact]
    public void OtherScopeRefusalIsIgnoredButLegacyRefusalIgnoresInformalLabel()
    {
        var (target, key) = CreateKey("friendly", "m-test");
        var otherScope = ScopedRefused(key, T2, CandidateInvocation) with { Team = "another-team" };
        var unrelated = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(
            [otherScope], key, CandidateInvocation);
        Assert.True(unrelated.Permitted);

        var legacyOtherLabel = LegacyRefused(target.Kind, "a different label", T2, CandidateInvocation);
        var global = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(
            [otherScope, legacyOtherLabel], key, CandidateInvocation);
        Assert.False(global.Permitted);
        Assert.Same(legacyOtherLabel, global.BlockingLegacyRefusal);
        Assert.Null(global.BlockingScopedRefusal);
    }

    [Fact]
    public void QueryChecksBaselineAndOptionalCandidate_WhileUnrelatedCandidateIsHarmless()
    {
        var (_, key) = CreateKey("friendly", "m-test");
        var refusedCandidate = ScopedRefused(key, T2, CandidateInvocation);

        var refused = ScopedModelResolutionEvidencePolicy.EvaluateQueryCandidates(
            [refusedCandidate], key, BaselineInvocation, CandidateInvocation);
        Assert.False(refused.Permitted);
        Assert.Equal("refused-invocation", refused.Reason);
        Assert.True(refused.BaselineInvocation.Permitted);
        Assert.False(refused.OptionalCandidate!.Permitted);

        var harmless = ScopedModelResolutionEvidencePolicy.EvaluateQueryCandidates(
            [refusedCandidate], key, BaselineInvocation, "claude --model m-unrelated --effort low");
        Assert.True(harmless.Permitted);
        Assert.Null(harmless.Reason);
    }

    private static void AssertBlocked(
        IReadOnlyList<ModelResolutionLedgerEntry> entries,
        ScopedModelResolutionScopeKey key,
        string invocation,
        bool legacy)
    {
        var result = ScopedModelResolutionEvidencePolicy.EvaluateCandidateSafety(entries, key, invocation);
        Assert.False(result.Permitted);
        Assert.Equal(legacy, result.BlockingLegacyRefusal is not null);
        Assert.Equal(!legacy, result.BlockingScopedRefusal is not null);
    }

    private static (ScopedModelResolutionTarget Target, ScopedModelResolutionScopeKey Key) CreateKey(
        string informalName,
        string? requestedModel,
        string effort = "medium")
    {
        var target = new ScopedModelResolutionTarget(
            "intent-cli", "dev", "architect", "workspace-a", "workspace-a:p1", "claude",
            null, null, ["architect"], "selected-digest");
        return (target, ScopedModelResolutionScopeKey.Create(
            target, new ScopedModelResolutionRequest(informalName, requestedModel, effort)));
    }

    private static ModelResolutionLedgerEntry ScopedVerified(
        ScopedModelResolutionScopeKey key,
        DateTimeOffset recordedAt,
        string invocation,
        string? informalName = null,
        string host = "machine-a",
        long pid = 101,
        string? processStartTime = null,
        string digest = "selected-digest",
        string observedModel = "m-test",
        string observedEffort = "medium") => ScopedBase(key, recordedAt, informalName) with
    {
        Outcome = ModelResolutionLedgerCommand.VerifiedOutcome,
        FullInvocation = invocation,
        Evidence = "captured READY proof",
        Host = host,
        ProcessId = pid,
        ProcessStartTimeUtc = processStartTime ?? UtcString(recordedAt),
        IdentitySource = "local-process-start-time",
        TopologyDigest = digest,
        ObservedAt = recordedAt,
        ObservedArgv = ["claude", "--model", observedModel, "--effort", observedEffort],
        ObservedModel = observedModel,
        ObservedEffort = observedEffort,
    };

    private static ModelResolutionLedgerEntry ScopedRefused(
        ScopedModelResolutionScopeKey key,
        DateTimeOffset recordedAt,
        string invocation) => ScopedBase(key, recordedAt, null) with
    {
        Outcome = ModelResolutionLedgerCommand.RefusedOutcome,
        RefusedInvocation = invocation,
        ErrorText = "captured refusal",
    };

    private static ModelResolutionLedgerEntry ScopedBase(
        ScopedModelResolutionScopeKey key,
        DateTimeOffset recordedAt,
        string? informalName) => new()
    {
        InformalName = informalName ?? key.InformalName,
        Kind = key.Kind,
        Outcome = ModelResolutionLedgerCommand.VerifiedOutcome,
        RecordedAt = recordedAt,
        ScopeVersion = 1,
        Domain = key.Domain,
        Team = key.Team,
        Role = key.Role,
        RoleAliases = [key.Role],
        WorkspaceId = key.WorkspaceId,
        PaneId = key.PaneId,
        RequestForm = key.RequestForm,
        RequestedModel = key.RequestedModel,
        RequestedEffort = key.RequestedEffort,
    };

    private static ModelResolutionLedgerEntry LegacyVerified(
        string kind,
        string informalName,
        DateTimeOffset recordedAt,
        string invocation) => new()
    {
        InformalName = informalName,
        Kind = kind,
        Outcome = ModelResolutionLedgerCommand.VerifiedOutcome,
        FullInvocation = invocation,
        Evidence = "legacy READY proof",
        RecordedAt = recordedAt,
    };

    private static ModelResolutionLedgerEntry LegacyRefused(
        string kind,
        string informalName,
        DateTimeOffset recordedAt,
        string invocation) => new()
    {
        InformalName = informalName,
        Kind = kind,
        Outcome = ModelResolutionLedgerCommand.RefusedOutcome,
        RefusedInvocation = invocation,
        ErrorText = "legacy refusal",
        RecordedAt = recordedAt,
    };

    private static string UtcString(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
