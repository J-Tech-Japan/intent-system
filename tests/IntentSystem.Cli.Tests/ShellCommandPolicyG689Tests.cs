using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ShellCommandPolicyG689Tests
{
    private const string Cwd = "/repo";
    private const string Root = "/repo";
    private const string Scratch = "/tmp/intent-g689-owned-scratch";
    private const string CurrentCycleId = "cycle-current";

    [Fact]
    public void Classify_ExtractsShellPayload_AndRequiresScopedPolicy()
    {
        var observed = Prompt("dotnet test tests/IntentSystem.Cli.Tests.csproj");

        var classified = AgentLaunchRecipeRegistry.Classify("codex", observed);

        Assert.True(classified.Known);
        Assert.Equal("shell-command", classified.PromptClass);
        Assert.NotNull(classified.ShellCommand);
        Assert.Equal("dotnet test tests/IntentSystem.Cli.Tests.csproj", classified.ShellCommand!.Command);
        Assert.False(NotifyPreApprovalPolicyStore.TryValidateRule(
            new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "shell-command" },
            out var error));
        Assert.Contains("scoped shell policy", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_FailsClosedWhenChoiceBlockHasResidualCommandTail()
    {
        var observed = "Would you like to run the following command?\n\n"
            + "$ dotnet test tests/IntentSystem.Cli.Tests.csproj\n"
            + "read -p \"continue? [y/n]\" answer\n"
            + "rm -rf " + Scratch + "\n\n"
            + "1. Allow once\n2. Deny";

        Assert.False(ShellCommandPromptRecognizer.TryExtract(observed, out var payload));
        Assert.Null(payload);

        var classified = AgentLaunchRecipeRegistry.Classify("codex", observed);

        Assert.Equal("unknown", classified.PromptClass);
        Assert.Null(classified.ShellCommand);
    }

    [Fact]
    public void Evaluate_RequiresEveryCompoundSegmentToHaveItsOwnScope()
    {
        var classified = Classify("dotnet test tests/IntentSystem.Cli.Tests.csproj && rm -rf " + Scratch);
        var policies = new[]
        {
            ProjectTestPolicy(),
            OwnedScratchPolicy(),
        };

        var accepted = ShellCommandPolicyRegistry.Evaluate(
            classified.ShellCommand!, policies, Cwd, currentCycleId: CurrentCycleId);

        Assert.Equal("accept", accepted.Decision);
        Assert.Contains("project-test", accepted.MatchedScopes);
        Assert.Contains("owned-scratch-delete", accepted.MatchedScopes);
        Assert.Contains("2 shell AST segment", accepted.Summary, StringComparison.Ordinal);

        var refused = ShellCommandPolicyRegistry.Evaluate(
            Classify("dotnet test tests/IntentSystem.Cli.Tests.csproj && rm -rf /tmp").ShellCommand!,
            policies,
            Cwd,
            currentCycleId: CurrentCycleId);

        Assert.Equal("escalate", refused.Decision);
        Assert.Contains("shell-segment-out-of-scope", refused.Rule, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedScratchDelete_RefusesStaleCycleIdentity()
    {
        var payload = Classify("rm -rf " + Scratch).ShellCommand!;
        var current = OwnedScratchPolicy();

        var accepted = ShellCommandPolicyRegistry.Evaluate(
            payload,
            [current],
            Cwd,
            currentCycleId: CurrentCycleId);
        Assert.Equal("accept", accepted.Decision);

        var stale = ShellCommandPolicyRegistry.Evaluate(
            payload,
            [current with { ScratchLedgerCycleId = "cycle-stale" }],
            Cwd,
            currentCycleId: CurrentCycleId);
        Assert.Equal("escalate", stale.Decision);
        Assert.Contains("owned-scratch-delete-stale-cycle", stale.Rule, StringComparison.Ordinal);
        Assert.Empty(stale.AnswerKeys);
        Assert.Null(stale.ExactAnswerScope);
    }

    [Fact]
    public void Evaluate_RefusesRedirectsAndUnknownSyntax()
    {
        var redirect = ShellCommandPolicyRegistry.Evaluate(
            Classify("dotnet test tests/IntentSystem.Cli.Tests.csproj > result.txt").ShellCommand!,
            [ProjectTestPolicy()],
            Cwd);
        Assert.Equal("escalate", redirect.Decision);
        Assert.Contains("shell-segment-out-of-scope", redirect.Rule, StringComparison.Ordinal);

        var substitution = ShellCommandAstParser.Parse("dotnet test $(pwd)");
        Assert.False(substitution.Parsed);
        Assert.True(substitution.UnknownSyntax);

        var quotedSubstitution = ShellCommandAstParser.Parse("dotnet test \"$(pwd)\"");
        Assert.False(quotedSubstitution.Parsed);
        Assert.True(quotedSubstitution.UnknownSyntax);

        Assert.True(ShellCommandAstParser.Parse("dotnet test tests/IntentSystem.Cli.Tests.csproj\n").Parsed);
    }

    [Fact]
    public void ExactCommandOnce_IsBoundToDigestAndDialogAndCannotBeRetried()
    {
        var classified = Classify("dotnet test tests/IntentSystem.Cli.Tests.csproj");
        var payload = classified.ShellCommand!;
        var policy = new NotifyScopedPromptPolicy
        {
            PolicyId = "exact-test-once",
            AgentKind = "codex",
            PromptClass = "shell-command",
            Scope = "exact-command-once",
            Decision = "accept",
            Category = "exact-command",
            CommandDigest = payload.CommandDigest,
            DialogHash = payload.DialogHash,
            Applicable = true,
        };

        var first = ShellCommandPolicyRegistry.Evaluate(payload, [policy], Cwd);
        Assert.Equal("accept", first.Decision);

        var audit = new NotifyPromptAudit
        {
            PromptKey = "prompt",
            Seat = "implementation",
            Pane = "pane",
            AgentKind = "codex",
            PromptClass = "shell-command",
            Rule = first.Rule,
            Actor = "orchestration",
            Timestamp = DateTimeOffset.UtcNow,
            Outcome = "bounded-answer-execution-pending",
            MatchedScopes = ["exact-command-once"],
            CommandDigest = payload.CommandDigest,
            DialogHash = payload.DialogHash,
        };
        var retry = ShellCommandPolicyRegistry.Evaluate(payload, [policy], Cwd, [audit]);

        Assert.Equal("escalate", retry.Decision);
        Assert.Contains("exact-command-once-consumed", retry.Rule, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedPolicyRecord_IsValidatedAndSerializedWithScopeMetadata()
    {
        var policy = new NotifyPreApprovalPolicy
        {
            Domain = "intent-cli",
            Team = "intent-cli-dev",
            RecordedAt = DateTimeOffset.UtcNow,
            Accept = [],
            Escalate = [],
            ScopedPolicies = [ProjectTestPolicy()],
        };

        Assert.True(NotifyPreApprovalPolicyStore.TryValidateScopedPolicies(policy.ScopedPolicies, out var error), error);
        var current = NotifyPreApprovalPolicyStore.WithCurrentApplicability(policy);
        Assert.True(current.Applicable);
        Assert.True(current.ScopedPolicies.Single().Applicable);

        var json = JsonSerializer.Serialize(current);
        Assert.Contains("scoped_policies", json, StringComparison.Ordinal);
        Assert.Contains("project-test", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedScratchDelete_RequiresAndPersistsCycleIdentity()
    {
        var policy = OwnedScratchPolicy();

        Assert.True(NotifyPreApprovalPolicyStore.TryValidateScopedPolicy(policy, out var error), error);
        var current = NotifyPreApprovalPolicyStore.WithCurrentApplicability(new NotifyPreApprovalPolicy
        {
            Domain = "intent-cli",
            Team = "intent-cli-dev",
            RecordedAt = DateTimeOffset.UtcNow,
            Accept = [],
            Escalate = [],
            ScopedPolicies = [policy],
        });
        var json = JsonSerializer.Serialize(current);
        Assert.Contains("scratch_ledger_cycle_id", json, StringComparison.Ordinal);
        Assert.Contains(CurrentCycleId, json, StringComparison.Ordinal);

        Assert.False(NotifyPreApprovalPolicyStore.TryValidateScopedPolicy(
            policy with { ScratchLedgerCycleId = null },
            out var missingIdentity));
        Assert.Contains("wake/cycle identity", missingIdentity, StringComparison.OrdinalIgnoreCase);
    }

    private static AgentPromptClassObservation Classify(string command)
    {
        var classified = AgentLaunchRecipeRegistry.Classify("codex", Prompt(command));
        Assert.NotNull(classified.ShellCommand);
        return classified;
    }

    private static string Prompt(string command) =>
        "Would you like to run the following command?\n\n$ " + command + "\n\n1. Allow once\n2. Deny";

    private static NotifyScopedPromptPolicy ProjectTestPolicy() => new()
    {
        PolicyId = "project-tests",
        AgentKind = "codex",
        PromptClass = "shell-command",
        Scope = "project-test",
        Decision = "accept",
        Category = "test-execution",
        ArgvTokenPrefix = ["dotnet", "test"],
        Cwd = Cwd,
        Root = Root,
        PathConstraints = ["/repo/tests"],
        EffectTags = ["executes-tests"],
        Applicable = true,
    };

    private static NotifyScopedPromptPolicy OwnedScratchPolicy() => new()
    {
        PolicyId = "owned-scratch",
        AgentKind = "codex",
        PromptClass = "shell-command",
        Scope = "owned-scratch-delete",
        Decision = "accept",
        Category = "destructive-scratch-cleanup",
        ArgvTokenPrefix = ["rm", "-rf"],
        Cwd = Cwd,
        PathConstraints = [Scratch],
        ScratchLedgerPaths = [Scratch],
        ScratchLedgerCycleId = CurrentCycleId,
        EffectTags = ["destructive"],
        Applicable = true,
    };
}
