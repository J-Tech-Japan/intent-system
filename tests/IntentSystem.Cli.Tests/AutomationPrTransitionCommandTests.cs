using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationPrTransitionCommandTests : IDisposable
{
    public AutomationPrTransitionCommandTests()
    {
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_ReviewStart_DryRunPlansHostReviewLabelsWithoutApplying()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--pr", "542",
                "--transition", "review-start",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal(542, result.Pr);
        Assert.Equal("review-start", result.Transition);
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Contains("intent-target", result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("rereview-ready", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_ReviewStart_WriteAppliesExactHostReviewLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "review-start",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("pr", transition.Kind);
        Assert.Equal(542, transition.Number);
        Assert.Contains("intent-target", transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", transition.RemoveLabels);
        Assert.Contains("rereview-ready", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_ReviewStart_DryRunSucceedsWhenOptionalRereviewLabelsAreAbsent()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "review-start",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-reviewing", result.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("rereview-ready", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_ReviewStart_WriteSkipsAbsentOptionalRereviewLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "review-start",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-reviewing", result.AddLabels);
        Assert.Empty(result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-reviewing", transition.AddLabels);
        Assert.Empty(transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_ReviewStart_WriteRemovesOnlyPresentRereviewLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "review-start",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-reviewing", transition.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", transition.RemoveLabels);
        Assert.DoesNotContain("rereview-ready", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_Approved_WriteAppliesExactApprovedLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-approved", result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-approved", transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-created", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_Approved_DryRunPlansClearingAllActiveReviewLabels_G503()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-approved", result.AddLabels);
        // The dry-run plan supersedes rereview-ready and the other in-flight review labels.
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_Approved_WriteRemovesRereviewReady_WhenPresent_G503()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-approved", transition.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", transition.RemoveLabels);
        // Only present labels are removed: reviewing/request-update/update-in-progress were absent.
        Assert.DoesNotContain("intent-pr-reviewing", transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-request-update", transition.RemoveLabels);
    }

    [Fact]
    public void Execute_Approved_WriteIsIdempotent_WhenRereviewReadyAbsent_G503()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            // Already approved with no in-flight review labels left.
            Labels = new[] { "intent-target", "intent-pr-approved" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-approved", result.AddLabels);
        // Idempotent: nothing to remove when no in-flight review label is present.
        Assert.Empty(result.RemoveLabels);
        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Empty(transition.RemoveLabels);
    }

    [Fact]
    public void Execute_RequestUpdate_DryRunPlansRepairRequestLabelsWithoutApplying()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "request-update",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("request-update", result.Transition);
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-request-update", result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
        Assert.DoesNotContain("intent-pr-created", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_RequestUpdate_WriteAppliesExactRepairRequestLabels()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "request-update",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);

        // G535 review repair: request-update must apply via ONE atomic
        // ReplaceLabelSet call carrying the full desired set, never via
        // ApplyLabelTransitions' sequential add/remove.
        Assert.Empty(mutator.AppliedTransitions);
        var replacedSet = Assert.Single(mutator.ReplacedLabelSets);
        Assert.Contains("intent-target", replacedSet);
        Assert.Contains("intent-pr-request-update", replacedSet);
        Assert.DoesNotContain("intent-pr-reviewing", replacedSet);
        Assert.DoesNotContain("intent-pr-created", replacedSet);
    }

    // ─── G535 tests: request-update supersedes stale rereview-ready ─────────

    [Fact]
    public void Execute_RequestUpdate_DryRunNamesRereviewReadyAsSuperseded()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "1760",
                "--transition", "request-update",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-request-update", result.AddLabels);
        // Both the current and legacy rereview-ready forms are named as superseded.
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("rereview-ready", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_RequestUpdate_SksG824_ClearsStaleRereviewReadyAndClaimSucceeds()
    {
        // G535 field finding #5 (SKS-G824 / PR #1760): a design amendment
        // arrives while the PR is rereview-ready. request-update must clear
        // rereview-ready in the SAME write, so worker claim can proceed
        // afterwards instead of hitting the canonical-state deadlock
        // (request-update present, but rereview-ready still refuses claim).
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "1760",
                "--transition", "request-update",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-request-update", result.AddLabels);
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);

        // G535 review repair: applied via ONE atomic ReplaceLabelSet call,
        // never sequential add/remove.
        Assert.Empty(mutator.AppliedTransitions);
        var replacedSet = Assert.Single(mutator.ReplacedLabelSets);
        Assert.Contains("intent-target", replacedSet);
        Assert.Contains("intent-pr-request-update", replacedSet);
        Assert.DoesNotContain("intent-pr-rereview-ready", replacedSet);

        // The fake mutator's own Labels reflect the replacement (mirroring
        // the production adapter's re-read-and-verify contract) — prove
        // `worker claim` now succeeds directly against it.
        var claimDecision = WorkerClaimAnalyzer.Analyze(GhCliGitHubLabelMutator.Kinds.Pr, mutator.Labels);
        Assert.True(claimDecision.Proceed);
        Assert.Contains("intent-pr-update-in-progress", claimDecision.AddLabels);
    }

    [Fact]
    public void Execute_RequestUpdate_ClaimStillRefusesUntransitionedRereviewReady()
    {
        // G535: unchanged behavior pin — a rereview-ready PR that has NOT
        // been through request-update must still be refused by claim (it
        // is the reviewer's to pick up, not the worker's).
        var untransitionedLabels = new[] { "intent-target", "intent-pr-rereview-ready" };

        var claimDecision = WorkerClaimAnalyzer.Analyze(GhCliGitHubLabelMutator.Kinds.Pr, untransitionedLabels);

        Assert.False(claimDecision.Proceed);
        Assert.Contains(claimDecision.Errors, e => e.Contains("already-rereview-ready", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_RequestUpdate_WriteIsIdempotent_WhenRereviewReadyAlreadyAbsent()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            // Already transitioned once: rereview-ready is gone, request-update present.
            Labels = new[] { "intent-target", "intent-pr-request-update" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "1760",
                "--transition", "request-update",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        // Truthful output: nothing was actually superseded, so nothing is
        // reported as removed (G535 review repair blocker #1).
        Assert.Empty(result.RemoveLabels);

        Assert.Empty(mutator.AppliedTransitions);
        var replacedSet = Assert.Single(mutator.ReplacedLabelSets);
        Assert.Contains("intent-target", replacedSet);
        Assert.Contains("intent-pr-request-update", replacedSet);
    }

    [Fact]
    public void Execute_RequestUpdate_ApiFailureAppliesNoPartialTransition()
    {
        // G535 review repair blocker #2: the mutation is ONE atomic
        // ReplaceLabelSet call — a failure must leave the PR's labels
        // completely untouched, never a half-applied state (request-update
        // added but rereview-ready not yet cleared, or vice versa).
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new ThrowingMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "1760",
                "--transition", "request-update",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("failed to apply PR transition", writer.ToString(), StringComparison.Ordinal);
        // No half-transition: the original label set is exactly what remains.
        Assert.Equal(new[] { "intent-target", "intent-pr-rereview-ready" }, mutator.Labels);
    }

    [Fact]
    public void Execute_RequestUpdate_WritePreservesUnrelatedLabels()
    {
        // G535 review repair: labels this transition never touches (e.g. a
        // domain/priority label) must survive the atomic replace untouched.
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing", "priority-high", "domain-billing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "request-update",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var replacedSet = Assert.Single(mutator.ReplacedLabelSets);
        Assert.Contains("intent-target", replacedSet);
        Assert.Contains("priority-high", replacedSet);
        Assert.Contains("domain-billing", replacedSet);
        Assert.Contains("intent-pr-request-update", replacedSet);
        Assert.DoesNotContain("intent-pr-reviewing", replacedSet);
    }

    [Fact]
    public void Execute_RequestUpdate_DryRunDoesNotCallEitherMutationPath()
    {
        // G535 review repair: dry/write parity check — dry-run must never
        // call ApplyLabelTransitions NOR ReplaceLabelSet.
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "request-update",
                "--dry-run",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.False(result.Applied);
        // Both present forms are named as superseded, truthfully, in dry-run too.
        Assert.Contains("intent-pr-rereview-ready", result.RemoveLabels);
        Assert.Contains("rereview-ready", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Empty(mutator.ReplacedLabelSets);
    }

    [Fact]
    public void Execute_ReviewStartAndApproved_LabelSetsUnchanged_G535Regression()
    {
        // G535 must not touch any other transition's label set. Pins
        // review-start and approved stay byte-identical to their
        // pre-G535 plans.
        using var workspace = new AutomationPrTransitionWorkspace();
        var reviewStartMutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-rereview-ready", "rereview-ready" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => reviewStartMutator;

        using var reviewStartWriter = new StringWriter();
        AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "542", "--transition", "review-start", "--write", "--format", "json" },
            reviewStartWriter);
        var reviewStartTransition = Assert.Single(reviewStartMutator.AppliedTransitions);
        Assert.Equal(
            new[] { "intent-target", "intent-pr-reviewing" },
            reviewStartTransition.AddLabels);
        Assert.Equal(
            new[] { "intent-pr-rereview-ready", "rereview-ready" },
            reviewStartTransition.RemoveLabels);

        var approvedMutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => approvedMutator;

        using var approvedWriter = new StringWriter();
        AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "542", "--transition", "approved", "--write", "--format", "json" },
            approvedWriter);
        var approvedTransition = Assert.Single(approvedMutator.AppliedTransitions);
        Assert.Equal(new[] { "intent-pr-approved" }, approvedTransition.AddLabels);
        Assert.Equal(new[] { "intent-pr-reviewing" }, approvedTransition.RemoveLabels);
    }

    // ─── G292 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G292_ReviewRelease_RemovesReviewingWithoutAddingRequestUpdate()
    {
        // PR #682-shape: review-start was applied, then host metadata blocked
        // closeout. review-release must drop intent-pr-reviewing cleanly
        // without adding intent-pr-request-update so the implementer is not
        // told to repair host metadata.
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "682",
                "--transition", "review-release",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("review-release", result.Transition);
        Assert.True(result.Applied);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Empty(transition.AddLabels);
        Assert.Contains("intent-pr-reviewing", transition.RemoveLabels);
        // Critically: review-release MUST NOT add request-update or any other
        // review-side label.
        Assert.DoesNotContain("intent-pr-request-update", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-approved", transition.AddLabels);
        Assert.DoesNotContain("intent-pr-rereview-ready", transition.AddLabels);
    }

    [Fact]
    public void Execute_G292_ReviewRelease_DryRunReportsExactPlan()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        AutomationPrTransitionCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "682",
                "--transition", "review-release",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Empty(result.AddLabels);
        Assert.Contains("intent-pr-reviewing", result.RemoveLabels);
    }

    [Fact]
    public void Execute_G292_ReviewRelease_WriteIsIdempotentWhenReviewingAlreadyAbsent()
    {
        // Defensive: if a previous run already removed intent-pr-reviewing, a
        // re-run of review-release should not error and should not try to
        // remove a label that isn't there. Mirrors the review-start dry-run
        // hardening.
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "682",
                "--transition", "review-release",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Empty(transition.AddLabels);
        // The label wasn't present, so the write plan should not list it.
        Assert.DoesNotContain("intent-pr-reviewing", transition.RemoveLabels);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationPrTransition()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "automation",
                "pr-transition",
                "--repo",
                "J-Tech-Japan/intent-system",
                "--pr",
                "542",
                "--transition",
                "approved",
                "--format",
                "json",
            ],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationPrTransitionResult>(writer.ToString())!;
        Assert.Equal("approved", result.Transition);
    }

    [Fact]
    public void CommandRouter_HelpSurfacesAutomationPrTransitionUsage()
    {
        using var workspace = new AutomationPrTransitionWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Automation commands:", output, StringComparison.Ordinal);
        Assert.Contains("automation pr-transition", output, StringComparison.Ordinal);
        Assert.Contains("--transition review-start", output, StringComparison.Ordinal);
        Assert.Contains("--transition request-update", output, StringComparison.Ordinal);
        Assert.Contains("--transition approved", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new AutomationPrTransitionWorkspace();
        var launcherInvoked = false;
        AutomationPrTransitionCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        AutomationPrTransitionCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-reviewing" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationPrTransitionCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "542",
                "--transition", "approved",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked,
            "AutomationPrTransitionCommand must never invoke NestedProviderLauncher.");
    }

    private sealed class FakeMutator : IGitHubLabelMutator, IGitHubLabelSetReplacer
    {
        public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        /// <summary>G535: each ReplaceLabelSet call's full desired set, in call order.</summary>
        public List<IReadOnlyList<string>> ReplacedLabelSets { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            if (string.Equals(kind, "pr", StringComparison.Ordinal)
                && (addLabels.Contains("intent-pr-created", StringComparer.Ordinal)
                    || removeLabels.Contains("intent-pr-created", StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("'intent-pr-created' is issue-only.");
            }

            foreach (var removeLabel in removeLabels)
            {
                if (!Labels.Contains(removeLabel, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"cannot remove absent label '{removeLabel}' in fake mutator");
                }
            }

            AppliedTransitions.Add(new AppliedTransition(
                repo,
                kind,
                number,
                addLabels.ToArray(),
                removeLabels.ToArray()));
        }

        /// <summary>
        /// G535: mirrors the production adapter's contract — replaces
        /// <see cref="Labels"/> wholesale with the desired set (as if the
        /// PUT-then-verified-re-read succeeded), and records the call so
        /// tests can assert exactly one atomic mutation payload.
        /// </summary>
        public void ReplaceLabelSet(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> desiredLabels)
        {
            if (string.Equals(kind, "pr", StringComparison.Ordinal)
                && desiredLabels.Contains("intent-pr-created", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("'intent-pr-created' is issue-only.");
            }

            var normalized = desiredLabels
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(label => label, StringComparer.Ordinal)
                .ToArray();
            ReplacedLabelSets.Add(normalized);
            Labels = normalized;
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    private sealed record AppliedTransition(
        string Repo,
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);

    /// <summary>
    /// G535: fake mutator whose mutation paths always throw, simulating a
    /// `gh` API failure, to prove the command reports failure without
    /// claiming <c>applied: true</c> — no half-transition.
    /// </summary>
    private sealed class ThrowingMutator : IGitHubLabelMutator, IGitHubLabelSetReplacer
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new IOException("simulated gh API failure");

        public void ReplaceLabelSet(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> desiredLabels) =>
            throw new IOException("simulated gh API failure");

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    private sealed class AutomationPrTransitionWorkspace : IDisposable
    {
        public AutomationPrTransitionWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-pr-transition-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteOriginRemote(string remoteUrl)
        {
            var gitDirectory = Path.Combine(RootPath, ".git");
            Directory.CreateDirectory(gitDirectory);
            File.WriteAllText(
                Path.Combine(gitDirectory, "config"),
                $"""
                [remote "origin"]
                    url = {remoteUrl}
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
