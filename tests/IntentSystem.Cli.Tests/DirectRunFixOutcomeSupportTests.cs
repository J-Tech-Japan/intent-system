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
    public void HasPlanningProgressSignalBeyondInitialInventory_GivenRequestReadAfterRepoListing_ReturnsTrue()
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            CreateProviderEvent("OpenAI Codex v0.118.0 (research preview)"),
            CreateProviderEvent("Use the request artifact at '/tmp/TOY-CALC-V0-01.request.md' as the bounded source of truth for this direct run. Continue beyond initial repository inspection even if the request mentions rg --files, git diff, or dotnet test, and do not stop after a single inspection command without producing one of those outcomes."),
            CreateProviderEvent("exec /bin/zsh -lc 'rg --files' succeeded in 0ms"),
            CreateProviderEvent("exec /bin/zsh -lc 'sed -n ''1,160p'' .intent-cli/fix/TOY-CALC-V0-01.request.md' succeeded in 0ms")
        ];

        var resolved = DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(providerEvents);

        Assert.True(resolved);
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
}
