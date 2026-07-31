using System.Text.Json;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G561: <c>clarify open</c> must work on a packet produced by
/// <c>intent-cli packet draft</c>.
///
/// Field incident (2026-07-31, G559 wake): the orchestrator tried to record the
/// required design clarification and <c>clarify open</c> failed pre-mutation
/// with "Projection packet YAML must contain required section
/// review_context_packet". Scaffolded packets carry
/// <c>implementation_issue_packet</c> / <c>intent_placement</c> /
/// <c>knowledge_updates</c> / <c>closeout_learning</c> — review context lives in
/// <c>review-context.md</c>, not in packet.yaml — so the G552 design-decision
/// flow was structurally blocked at exactly the moment it exists for: recording
/// a blocking question EARLY, before the packet is complete.
///
/// The scaffold here is produced by the REAL <see cref="PacketDraftCommand"/>
/// rather than by a hand-written fixture. A fixture copy of the scaffold would
/// keep passing after the scaffold changed shape, which is precisely how this
/// gap survived: every existing clarify fixture was a complete packet.
/// </summary>
[Collection(RunSubmitCommandCollection.Name)]
public sealed class ClarifyOpenScaffoldedPacketTests : IDisposable
{
    private const string Unit = "G900";
    private const string Domain = "intent-cli";
    private const string ReturnPath = "intents/intent-cli/clarifications/open.md";

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private readonly string rootPath = Directory.CreateTempSubdirectory("clarify-open-scaffold-tests-").FullName;
    private readonly Func<DateTimeOffset> originalTimestampFactory = ClarifyOpenCommand.TimestampFactory;

    public void Dispose()
    {
        ClarifyOpenCommand.TimestampFactory = originalTimestampFactory;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.CandidateListerFactory = null;

        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void ScaffoldedPacket_HasNoReviewContextSection_AndIsIncomplete_G561()
    {
        // The premise of the whole slice, asserted rather than assumed — if the
        // scaffold ever grows a full contract, this test says so instead of the
        // rest of the file silently proving nothing.
        var context = CreateContext();
        Assert.Equal(0, RunPacketDraft(context));

        var yaml = File.ReadAllText(PacketYamlPath());
        Assert.DoesNotContain("review_context_packet:", yaml, StringComparison.Ordinal);
        Assert.Contains("implementation_issue_packet:", yaml, StringComparison.Ordinal);
        // Also missing most of the strict contract's required implementation
        // fields — so "make review_context_packet optional" alone would not
        // have been enough.
        Assert.DoesNotContain("landing_policy:", yaml, StringComparison.Ordinal);

        var reviewContext = File.ReadAllText(ReviewContextPath());
        Assert.DoesNotContain("# Deterministic Review Checks", reviewContext, StringComparison.Ordinal);
        Assert.DoesNotContain("# Execution Unit", reviewContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ClarifyOpen_OnAFreshlyScaffoldedPacket_RecordsTheClarification_G561()
    {
        var context = CreateContext();
        Assert.Equal(0, RunPacketDraft(context));
        WriteQueueState(QueueItemState.Queued);
        ClarifyOpenCommand.TimestampFactory = () => FixedNow;

        using var writer = new StringWriter();
        var exitCode = ClarifyOpenCommand.Execute(
            context,
            [Unit, "--question", "Should the pre-publish exit be a flag or its own command?"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Clarification opened for {Unit}", writer.ToString(), StringComparison.Ordinal);

        // The durable artifact — the thing detection and design actually read.
        var artifactPath = Path.Combine(rootPath, ".intent-cli", "clarifications", Unit, "request.json");
        Assert.True(File.Exists(artifactPath), writer.ToString());
        var artifact = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
        Assert.Equal(Unit, artifact.ExecutionUnit);
        Assert.Equal(ClarificationStatus.Open, artifact.Status);
        Assert.Equal("blocking", artifact.BlockingOrNonblocking);
        Assert.Equal(
            "Should the pre-publish exit be a flag or its own command?",
            artifact.QuestionText);
        // The return path comes from the queue item, which is authoritative for
        // it; the scaffold has no review-context packet to carry a copy.
        Assert.Equal(ReturnPath, artifact.ClarificationReturnPath);

        // And the queue side moved, exactly as it does for a complete packet.
        var item = ReadQueueItem();
        Assert.Equal(QueueItemState.ClarifyBlocked, item.State);
        Assert.NotEmpty(item.BlockedBy);
    }

    [Fact]
    public void ClarifyOpen_OnAScaffold_WithoutAnExplicitQuestion_StillRecordsSomethingReadable_G561()
    {
        var context = CreateContext();
        Assert.Equal(0, RunPacketDraft(context));
        WriteQueueState(QueueItemState.Queued);
        ClarifyOpenCommand.TimestampFactory = () => FixedNow;

        using var writer = new StringWriter();
        Assert.Equal(0, ClarifyOpenCommand.Execute(context, [Unit], writer));

        var artifact = ClarificationSerializer.Deserialize(
            File.ReadAllText(Path.Combine(rootPath, ".intent-cli", "clarifications", Unit, "request.json")));

        // A scaffold has not filled in a goal yet. The derived text makes that
        // gap explicit rather than asserting detail the packet does not
        // contain, and the reason still names the unit being held.
        Assert.Contains("not yet recorded in the packet", artifact.QuestionText, StringComparison.Ordinal);
        Assert.Contains(Unit, artifact.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResultingArtifact_IsReadByDesignDecisionPendingDetection_G561()
    {
        // Recording the clarification is only useful if the detector that
        // surfaces held design decisions can read what was written.
        var context = CreateContext();
        Assert.Equal(0, RunPacketDraft(context));
        WriteQueueState(QueueItemState.Queued);
        ClarifyOpenCommand.TimestampFactory = () => FixedNow.AddMinutes(-120);

        using var openWriter = new StringWriter();
        Assert.Equal(0, ClarifyOpenCommand.Execute(
            context, [Unit, "--question", "Flag or command?"], openWriter));

        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            context,
            ["--domain", Domain, "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var item = document.RootElement.GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindDesignDecisionPending);

        Assert.Equal(Unit, item.GetProperty("execution_unit").GetString());
        Assert.Equal(120, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("Flag or command?", item.GetProperty("recommended_action").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void APacketWhoseExecutionUnitDisagreesWithTheQueueItem_IsStillRefused_G561()
    {
        // The one guard that must NOT relax: identity. A clarification filed
        // against the wrong unit is worse than none.
        var context = CreateContext();
        Assert.Equal(0, RunPacketDraft(context));
        WriteQueueState(QueueItemState.Queued);

        var yaml = File.ReadAllText(PacketYamlPath())
            .Replace($"source_execution_unit: {Unit}", "source_execution_unit: G999", StringComparison.Ordinal);
        File.WriteAllText(PacketYamlPath(), yaml);

        var queueBefore = File.ReadAllText(QueueStatePath());
        using var writer = new StringWriter();
        var exitCode = ClarifyOpenCommand.Execute(context, [Unit], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(queueBefore, File.ReadAllText(QueueStatePath()));
        Assert.False(Directory.Exists(Path.Combine(rootPath, ".intent-cli", "clarifications", Unit)));
    }

    [Fact]
    public void APacketMissingItsExecutionUnitEntirely_IsRefused_G561()
    {
        var context = CreateContext();
        Assert.Equal(0, RunPacketDraft(context));
        WriteQueueState(QueueItemState.Queued);

        var yaml = File.ReadAllText(PacketYamlPath())
            .Replace($"source_execution_unit: {Unit}", "# the identity line, removed", StringComparison.Ordinal);
        File.WriteAllText(PacketYamlPath(), yaml);

        using var writer = new StringWriter();
        Assert.Equal(1, ClarifyOpenCommand.Execute(context, [Unit], writer));
        Assert.Contains("source_execution_unit", writer.ToString(), StringComparison.Ordinal);
    }

    private static int RunPacketDraft(CliContext context)
    {
        using var writer = new StringWriter();
        return PacketDraftCommand.Execute(
            context,
            ["--execution-unit", Unit, "--target-repo", "J-Tech-Japan/intent-system"],
            writer);
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = rootPath,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private string PacketYamlPath() => Path.Combine(rootPath, ".intent-cli", "issues", Unit, "packet.yaml");

    private string ReviewContextPath() => Path.Combine(rootPath, ".intent-cli", "issues", Unit, "review-context.md");

    private string QueueStatePath() => Path.Combine(rootPath, ".intent-cli", "queue-state.json");

    private QueueItem ReadQueueItem() =>
        QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath()))
            .Items.Single(item => item.ExecutionUnit == Unit);

    private void WriteQueueState(QueueItemState state)
    {
        Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
        File.WriteAllText(QueueStatePath(), QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = FixedNow,
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = Unit,
                    Title = $"[{Unit}] Scaffolded slice",
                    State = state,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = ReturnPath,
                    PacketPaths = new PacketPaths
                    {
                        Implementation = $".intent-cli/issues/{Unit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{Unit}/review-context.md",
                        Yaml = $".intent-cli/issues/{Unit}/packet.yaml",
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "normal",
                },
            ],
        }));
    }
}
