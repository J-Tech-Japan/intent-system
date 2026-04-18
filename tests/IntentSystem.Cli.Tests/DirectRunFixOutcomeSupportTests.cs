using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class DirectRunFixOutcomeSupportTests
{
    [Fact]
    public void TryResolveStartupOnlyFailureDetail_GivenIssue295CurrentSessionRawShape_ReturnsStartupOnlyDetail()
    {
        var providerEvents = CreateIssue295CurrentSessionRawEvents();

        var resolved = DirectRunFixOutcomeSupport.TryResolveStartupOnlyFailureDetail(
            providerEvents,
            "TOY-CALC-V0-01",
            out var detail);

        Assert.True(resolved);
        Assert.Contains("during provider startup", detail, StringComparison.Ordinal);
        Assert.Contains("Current-session provider output only contained startup warnings or noise", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveStartupOnlyFailureDetail_GivenEchoedStartupRequestMentionsInspectionCommands_StillReturnsStartupOnlyDetail()
    {
        var providerEvents = CreateIssue295CurrentSessionRawEvents(
            echoedRequest:
            "Use the request artifact at '/tmp/TOY-CALC-V0-01.request.md' as the bounded source of truth for this direct run. Continue beyond initial repository inspection even if the request mentions rg --files, git diff, or dotnet test, and do not stop after a single inspection command without producing one of those outcomes.");

        var resolved = DirectRunFixOutcomeSupport.TryResolveStartupOnlyFailureDetail(
            providerEvents,
            "TOY-CALC-V0-01",
            out var detail);

        Assert.True(resolved);
        Assert.Contains("during provider startup", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void HasPlanningProgressSignalBeyondInitialInventory_GivenOnlyRepoListing_ReturnsFalse()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("Use the request artifact at '/tmp/TOY-CALC-V0-01.request.md' as the bounded source of truth for this direct run. Continue beyond initial repository inspection even if the request mentions rg --files, git diff, or dotnet test, and do not stop after a single inspection command without producing one of those outcomes."),
            CreateProviderEvent("exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
        ];

        var resolved = DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(providerEvents);

        Assert.False(resolved);
    }

    [Fact]
    public void HasPlanningProgressSignalBeyondInitialInventory_GivenRequestReadAfterRepoListing_ReturnsFalse()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("Use the request artifact at '/tmp/TOY-CALC-V0-01.request.md' as the bounded source of truth for this direct run. Continue beyond initial repository inspection even if the request mentions rg --files, git diff, or dotnet test, and do not stop after a single inspection command without producing one of those outcomes."),
            CreateProviderEvent("exec /bin/zsh -lc 'rg --files' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,160p'' .intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms")
        ];

        var resolved = DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(providerEvents);

        Assert.False(resolved);
    }

    [Fact]
    public void HasPlanningProgressSignalBeyondInitialInventory_GivenOnlyRepoLocalSpecRead_ReturnsFalse()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,160p'' .intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md' failed in 0ms")
        ];

        var resolved = DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(providerEvents);

        Assert.False(resolved);
    }

    [Fact]
    public void HasPlanningProgressSignalBeyondInitialInventory_GivenSpecAndProductRead_ReturnsTrue()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,160p'' .intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Program.cs' succeeded in 0ms")
        ];

        var resolved = DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(providerEvents);

        Assert.True(resolved);
    }

    [Fact]
    public void HasPlanningProgressSignalBeyondInitialInventory_GivenFailedSpecReadThenProductReads_ReturnsFalse()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,160p'' .intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md'"),
            CreateProviderEvent(" failed in 0ms:"),
            CreateProviderEvent("sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Program.cs && printf ''\\n---\\n'' && sed -n ''1,220p'' src/ToyCalc/Calculator.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' tests/ToyCalc.Tests/CalculatorTests.cs'"),
            CreateProviderEvent(" succeeded in 0ms:")
        ];

        var resolved = DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(providerEvents);

        Assert.False(resolved);
    }

    [Fact]
    public void CreateCanonicalContractGapEventIfNeeded_GivenFailedSpecReadThenProductReads_ReportsSpecBoundaryMissing()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md'"),
            CreateProviderEvent(" failed in 0ms:"),
            CreateProviderEvent("sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Program.cs && printf ''\\n---\\n'' && sed -n ''1,220p'' src/ToyCalc/Calculator.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' tests/ToyCalc.Tests/CalculatorTests.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateBackendExitEvent()
        ];

        var contractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.Parse("2026-04-17T06:00:00Z"),
            "TOY-CALC-V0-01",
            "fix",
            "Codex",
            "pid:2579");

        Assert.NotNull(contractGapEvent);
        Assert.Equal("fix-session-ended-before-spec-source-test-read", contractGapEvent!.Payload.GetProperty("reason").GetString());
        Assert.Contains(
            "repo_local_spec_read=False",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "product_source_or_test_read=True",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCanonicalContractGapEventIfNeeded_GivenRequestRereadThenBackendExit_UsesPreSpecSourceBoundaryReason()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("2026-04-17T05:58:46.453938Z  WARN codex_core::plugins::manifest: ignoring interface.defaultPrompt: maximum of 3 prompts is supported"),
            CreateBackendExitEvent()
        ];

        var contractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.Parse("2026-04-17T06:00:00Z"),
            "TOY-CALC-V0-01",
            "fix",
            "Codex",
            "pid:2579");

        Assert.NotNull(contractGapEvent);
        Assert.Equal("provider-event", contractGapEvent!.Kind);
        Assert.Equal("fix-session-ended-before-spec-source-test-read", contractGapEvent.Payload.GetProperty("reason").GetString());
        Assert.Contains(
            "provider backend itself exited before the next bounded read",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCanonicalContractGapEventIfNeeded_GivenRequestRereadAndDeadSessionWithoutTerminalEvent_UsesMissingCaptureReason()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("2026-04-17T05:58:46.453938Z  WARN codex_core::plugins::manifest: ignoring interface.defaultPrompt: maximum of 3 prompts is supported")
        ];

        var contractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.Parse("2026-04-17T06:00:00Z"),
            "TOY-CALC-V0-01",
            "fix",
            "Codex",
            "pid:2579",
            providerSessionAlive: false);

        Assert.NotNull(contractGapEvent);
        Assert.Equal("fix-session-terminal-boundary-missing-after-request-reread", contractGapEvent!.Payload.GetProperty("reason").GetString());
        Assert.Contains(
            "event capture dropped after the request reread layer",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCanonicalContractGapEventIfNeeded_GivenDeepProgressAndDeadSessionWithoutTerminalEvent_UsesDeepProgressMissingTerminalReason()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md'"),
            CreateProviderEvent(" exited 1 in 0ms:"),
            CreateProviderEvent("sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Program.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' tests/ToyCalc.Tests/CalculatorTests.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'dotnet test'"),
            CreateProviderEvent(" succeeded in 0ms:")
        ];

        var contractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.Parse("2026-04-17T07:40:00Z"),
            "TOY-CALC-V0-01",
            "fix",
            "Codex",
            "pid:6210",
            providerSessionAlive: false);

        Assert.NotNull(contractGapEvent);
        Assert.Equal(
            "fix-session-terminal-boundary-missing-after-deep-progress",
            contractGapEvent!.Payload.GetProperty("reason").GetString());
        Assert.Contains(
            "product_source_or_test_read=True",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "dotnet_test=True",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCanonicalContractGapEventIfNeeded_GivenDeepProgressAndSuccessfulBackendExit_DoesNotCreateFailureBoundary()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md'"),
            CreateProviderEvent(" exited 1 in 0ms:"),
            CreateProviderEvent("sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Program.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' tests/ToyCalc.Tests/CalculatorTests.cs'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("exec /bin/zsh -lc 'dotnet test'"),
            CreateProviderEvent(" succeeded in 0ms:"),
            CreateProviderEvent("- Updated src/ToyCalc/Program.cs to preserve successful multiply output."),
            CreateSuccessfulBackendExitEvent()
        ];

        var contractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.Parse("2026-04-17T07:40:00Z"),
            "TOY-CALC-V0-01",
            "fix",
            "Codex",
            "pid:6210",
            providerSessionAlive: false);

        Assert.Null(contractGapEvent);
    }

    [Fact]
    public void CreateCanonicalContractGapEventIfNeeded_GivenExplicitContractGapRefusalTextAndSuccessfulBackendExit_CreatesFailureBoundary()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/specs/01-cli-surface.md'"),
            CreateProviderEvent(" exited 1 in 0ms:"),
            CreateProviderEvent("sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory"),
            CreateProviderEvent("I stopped with a contract-gap explanation rather than inventing a repair target because the deterministic review contract points at `intents/toy-calc/specs/01-cli-surface.md`, and that spec file does not exist in this worktree."),
            CreateProviderEvent("2. Close this run as a completed contract-gap refusal."),
            CreateSuccessfulBackendExitEvent()
        ];

        var contractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.Parse("2026-04-17T07:40:00Z"),
            "TOY-CALC-V0-01",
            "fix",
            "Codex",
            "pid:6210",
            providerSessionAlive: false);

        Assert.NotNull(contractGapEvent);
        Assert.Equal("failed", contractGapEvent!.Payload.GetProperty("run_status").GetString());
        Assert.Equal("provider-explicit-contract-gap-refusal", contractGapEvent.Payload.GetProperty("reason").GetString());
        Assert.Contains(
            "contract-gap refusal",
            contractGapEvent.Payload.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateIssue295CurrentSessionRawEvents(string? echoedRequest = null)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T08:08:23.1658640+00:00",
                ExecutionUnit = "TOY-CALC-V0-01",
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = "pid:97771",
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "gpt-5.4-mini",
                    transport = "stdio",
                    command = "/Users/tomohisa/bin/codex-isolated"
                })
            },
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("--------"),
            CreateProviderEvent("workdir: /Users/tomohisa/dev/GitHub/MyIntentHost/submodules/toy-calc-sample/.intent-cli/worktrees/TOY-CALC-V0-01"),
            CreateProviderEvent("model: gpt-5.4-mini"),
            CreateProviderEvent("provider: openai"),
            CreateProviderEvent("approval: never"),
            CreateProviderEvent("sandbox: danger-full-access"),
            CreateProviderEvent("reasoning effort: high"),
            CreateProviderEvent("reasoning summaries: none"),
            CreateProviderEvent("session id: 019d9555-85e9-7af2-a46d-d0836b0f8ecd"),
            CreateProviderEvent("--------"),
            CreateProviderEvent("user"),
            CreateProviderEvent(
                echoedRequest
                ?? "Use the request artifact at '/Users/tomohisa/dev/GitHub/MyIntentHost/submodules/toy-calc-sample/.intent-cli/fix/TOY-CALC-V0-01.request.md' as the bounded source of truth for this direct run. Continue beyond initial repository inspection and either complete the bounded repair attempt from that artifact or end with a deterministic refusal or contract-gap explanation. Do not stop after a single inspection command without producing one of those outcomes."),
            CreateProviderEvent("2026-04-16T08:08:23.310822Z  WARN codex_rollout::list: state db discrepancy during find_thread_path_by_id_str_in_subdir: falling_back"),
            CreateProviderEvent("2026-04-16T08:08:23.310983Z  WARN codex_rollout::state_db: state db discrepancy during read_repair_rollout_path: upsert_needed (slow path)"),
            CreateProviderEvent("2026-04-16T08:08:23.311130Z  WARN codex_rollout::state_db: state db reconcile_rollout extraction failed /Users/tomohisa/.codex-direct-backend/sessions/2026/04/15/rollout-2026-04-15T15-24-25-019d933e-e4e4-7743-abbb-ef13bb2666cf.jsonl: empty session file"),
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T08:32:47.8815580+00:00",
                ExecutionUnit = "TOY-CALC-V0-01",
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = "pid:97771",
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static DirectRunProviderEvent CreateProviderEvent(string payload)
    {
        return new DirectRunProviderEvent
        {
            Timestamp = "2026-04-16T08:08:23.2829410+00:00",
            ExecutionUnit = "TOY-CALC-V0-01",
            Provider = "Codex",
            EntryKind = "fix",
            SessionId = "pid:97771",
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(payload)
        };
    }

    private static DirectRunProviderEvent CreateBackendExitEvent()
    {
        return new DirectRunProviderEvent
        {
            Timestamp = "2026-04-17T05:58:47.0000000+00:00",
            ExecutionUnit = "TOY-CALC-V0-01",
            Provider = "Codex",
            EntryKind = "fix",
            SessionId = "pid:97771",
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(new
            {
                type = "backend-exit",
                exit_code = 1
            })
        };
    }

    private static DirectRunProviderEvent CreateSuccessfulBackendExitEvent()
    {
        return new DirectRunProviderEvent
        {
            Timestamp = "2026-04-17T05:58:47.0000000+00:00",
            ExecutionUnit = "TOY-CALC-V0-01",
            Provider = "Codex",
            EntryKind = "fix",
            SessionId = "pid:97771",
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(new
            {
                type = "backend-exit",
                exit_code = 0
            })
        };
    }
}
