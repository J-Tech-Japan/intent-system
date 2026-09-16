using System.Globalization;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using static IntentSystem.Cli.Tests.G839RecoveryFakes;

namespace IntentSystem.Cli.Tests;

public sealed partial class G839RecoveryByteIdentityTests
{
    internal static string CaptureRecovery(string fixtureId)
    {
        using var scope = new RecoverySeams();
        using var workspace = G839ByteIdentityHarness.CreateProceedWorkspace();
        SeedRecoveryGitHubFakes(workspace);
        ConfigureRecoveryScenario(workspace, fixtureId);
        return G839ByteIdentityHarness.RunRecovery(workspace, G839ByteIdentityHarness.BuildRecoveryArgs(fixtureId));
    }

    private static void ConfigureRecoveryScenario(G839ByteIdentityHarness.RecoveryWorkspace workspace, string fixtureId)
    {
        switch (fixtureId)
        {
            case "recovery-help":
            case "recovery-parse-error":
                return;
            case "recovery-ruling-missing-markdown":
                return;
            case "recovery-issue-not-open-refusal-json":
            case "recovery-issue-not-open-refusal-markdown":
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(G839ByteIdentityHarness.ClosedIssue());
                return;
            case "recovery-already-recovered-json":
                workspace.AppendRunEvent(G839ByteIdentityHarness.BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventRecovered, G839ByteIdentityHarness.Pr));
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(G839ByteIdentityHarness.OpenIssue("intent-target"));
                return;
            case "recovery-recovered-write-json":
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new RecordingLabelMutator("intent-target", "intent-pr-created");
                return;
            case "recovery-event-completed-write-json":
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(G839ByteIdentityHarness.OpenIssue("intent-target"));
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new RecordingLabelMutator("intent-target");
                return;
            case "recovery-recovery-completed-write-json":
                workspace.AppendRunEvent(G839ByteIdentityHarness.BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, G839ByteIdentityHarness.Pr));
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(G839ByteIdentityHarness.OpenIssue("intent-target"));
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new RecordingLabelMutator("intent-target");
                return;
            case "recovery-closed-then-label-removed-write-json":
                workspace.AppendRunEvent(G839ByteIdentityHarness.BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, G839ByteIdentityHarness.Pr));
                workspace.AppendRunEvent(G839ByteIdentityHarness.BuildRecoveryEvent(
                    AutomationPrCreatedStaleRecoveryCommand.EventAborted,
                    G839ByteIdentityHarness.Pr,
                    reason: "re-check refused: label-changed"));
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(G839ByteIdentityHarness.OpenIssue("intent-target"));
                return;
            case "recovery-target-absent-refusal-json":
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(G839ByteIdentityHarness.OpenIssue("intent-pr-created"));
                return;
            case "recovery-pr-merged-refusal-json":
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
                    new FakePrLookup(G839ByteIdentityHarness.Merged(G839ByteIdentityHarness.Pr));
                return;
            case "recovery-recovered-event-append-failed-write-json":
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new G839ByteIdentityHarness.ReadonlyAfterMutationLabelMutator(workspace, "intent-target", "intent-pr-created");
                return;
            case "recovery-label-readback-unconfirmed-write-json":
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new G839ByteIdentityHarness.ReadbackUnconfirmedLabelMutator();
                return;
            case "recovery-label-readback-failed-write-json":
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new G839ByteIdentityHarness.ThrowingReadbackLabelMutator("intent-target", "intent-pr-created");
                return;
            case "recovery-label-removal-failed-write-json":
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new G839ByteIdentityHarness.ThrowingApplyLabelMutator("intent-target", "intent-pr-created");
                return;
            case "recovery-started-event-append-failed-write-json":
                G839ByteIdentityHarness.SetRunLogReadOnly(workspace.Context, readOnly: true);
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new RecordingLabelMutator("intent-target", "intent-pr-created");
                return;
            case "recovery-superseded-event-append-failed-write-json":
                workspace.AppendRunEvent(G839ByteIdentityHarness.BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, 163));
                G839ByteIdentityHarness.SetRunLogReadOnly(workspace.Context, readOnly: true);
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
                    new FakePrLookup(G839ByteIdentityHarness.ClosedUnmerged(G839ByteIdentityHarness.Pr));
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new RecordingLabelMutator("intent-target", "intent-pr-created");
                workspace.WriteQueueState(linkedPr: G839ByteIdentityHarness.Pr, publishPr: 163);
                return;
            case "recovery-proceed-dry-run-json":
            case "recovery-proceed-dry-run-markdown":
                AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                    new RecordingLabelMutator("intent-target", "intent-pr-created");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixtureId), fixtureId, "unknown recovery scenario");
        }
    }
    private static void SeedRecoveryGitHubFakes(G839ByteIdentityHarness.RecoveryWorkspace workspace)
    {
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(G839ByteIdentityHarness.OpenIssue("intent-target", "intent-pr-created"));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(G839ByteIdentityHarness.ClosedUnmerged(G839ByteIdentityHarness.Pr));
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => new FakeLister();
    }
    private sealed class RecoverySeams : IDisposable
    {
        public RecoverySeams()
        {
            AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () => new ThrowingIssueLookup();
            AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new ThrowingPrLookup();
            AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => new ThrowingLister();
            AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => new ThrowingLabelMutator();
            AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory = null;
            AutomationPrCreatedStaleRecoveryCommand.UtcNowFactory = () => G839ByteIdentityHarness.FixedNow;
        }

        public void Dispose()
        {
            AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = null;
            AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = null;
            AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = null;
            AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = null;
            AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory = null;
            AutomationPrCreatedStaleRecoveryCommand.UtcNowFactory = null;
        }
    }

    internal static string CaptureWorkerNextAction()
    {
        using var scope = new WorkerNextActionSeams();
        using var workspace = new G839ByteIdentityHarness.CliWorkspace("worker-next-action-g839-");
        using var writer = new StringWriter();
        WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", G839ByteIdentityHarness.Repo, "--format", "json"],
            writer);
        return writer.ToString();
    }

    private sealed class WorkerNextActionSeams : IDisposable
    {
        public WorkerNextActionSeams()
        {
            WorkerNextActionCommand.CandidateListerFactory = () => new G839ByteIdentityHarness.WorkerLister();
            WorkerNextActionCommand.CommentsLookupFactory = () => new G839ByteIdentityHarness.WorkerCommentsLookup();
            WorkerNextActionCommand.IssueLookupFactory = () => new G839ByteIdentityHarness.WorkerIssueLookup();
            WorkerNextActionCommand.NestedProviderLauncher = null;
        }

        public void Dispose()
        {
            WorkerNextActionCommand.CandidateListerFactory = null;
            WorkerNextActionCommand.CommentsLookupFactory = null;
            WorkerNextActionCommand.IssueLookupFactory = null;
            WorkerNextActionCommand.NestedProviderLauncher = null;
        }
    }
}
