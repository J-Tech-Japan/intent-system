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
/// <c>intent-cli packet draft</c> — and must NOT become more permissive toward
/// any packet that declares itself complete.
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
///
/// Review repair: routing is decided by the DECLARATION alone. A packet that
/// declares <c>review_context_packet</c> — whatever shape that declaration turns
/// out to have — goes through the unchanged strict serializer, so a
/// declared-but-broken packet fails exactly as loudly as before, and before any
/// mutation. The fixtures below prove both directions.
/// </summary>
[Collection(RunSubmitCommandCollection.Name)]
public sealed class ClarifyOpenScaffoldedPacketTests : IDisposable
{
    private readonly ScaffoldWorkspace workspace = new();
    private readonly Func<DateTimeOffset> originalTimestampFactory = ClarifyOpenCommand.TimestampFactory;

    public void Dispose()
    {
        ClarifyOpenCommand.TimestampFactory = originalTimestampFactory;
        workspace.Dispose();
    }

    // ---------------------------------------------------------------
    // The scaffold path.
    // ---------------------------------------------------------------

    [Fact]
    public void ScaffoldedPacket_HasNoReviewContextSection_AndIsIncomplete_G561()
    {
        // The premise of the whole slice, asserted rather than assumed — if the
        // scaffold ever grows a full contract, this test says so instead of the
        // rest of the file silently proving nothing.
        Assert.Equal(0, workspace.RunPacketDraft());

        var yaml = File.ReadAllText(workspace.PacketYamlPath);
        Assert.DoesNotContain("review_context_packet:", yaml, StringComparison.Ordinal);
        Assert.Contains("implementation_issue_packet:", yaml, StringComparison.Ordinal);
        // Also missing most of the strict contract's required implementation
        // fields — so "make review_context_packet optional" alone would not
        // have been enough.
        Assert.DoesNotContain("landing_policy:", yaml, StringComparison.Ordinal);

        var reviewContext = File.ReadAllText(workspace.ReviewContextPath);
        Assert.DoesNotContain("# Deterministic Review Checks", reviewContext, StringComparison.Ordinal);
        Assert.DoesNotContain("# Execution Unit", reviewContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ClarifyOpen_OnAFreshlyScaffoldedPacket_RecordsTheClarification_G561()
    {
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);
        ClarifyOpenCommand.TimestampFactory = () => ScaffoldWorkspace.FixedNow;

        using var writer = new StringWriter();
        var exitCode = ClarifyOpenCommand.Execute(
            workspace.Context,
            [ScaffoldWorkspace.Unit, "--question", "Should the pre-publish exit be a flag or its own command?"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Clarification opened for {ScaffoldWorkspace.Unit}", writer.ToString(), StringComparison.Ordinal);

        var artifact = workspace.ReadClarification();
        Assert.Equal(ScaffoldWorkspace.Unit, artifact.ExecutionUnit);
        Assert.Equal(ClarificationStatus.Open, artifact.Status);
        Assert.Equal("blocking", artifact.BlockingOrNonblocking);
        Assert.Equal("Should the pre-publish exit be a flag or its own command?", artifact.QuestionText);
        // The return path comes from the queue item, which is authoritative for
        // it; the scaffold has no review-context packet to carry a copy.
        Assert.Equal(ScaffoldWorkspace.ReturnPath, artifact.ClarificationReturnPath);

        // And the queue side moved, exactly as it does for a complete packet.
        var item = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.ClarifyBlocked, item.State);
        Assert.NotEmpty(item.BlockedBy);
    }

    [Fact]
    public void ClarifyOpen_OnAScaffold_WithoutAnExplicitQuestion_StillRecordsSomethingReadable_G561()
    {
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);
        ClarifyOpenCommand.TimestampFactory = () => ScaffoldWorkspace.FixedNow;

        using var writer = new StringWriter();
        Assert.Equal(0, ClarifyOpenCommand.Execute(workspace.Context, [ScaffoldWorkspace.Unit], writer));

        var artifact = workspace.ReadClarification();

        // A scaffold has not filled in a goal yet. The derived text makes that
        // gap explicit rather than asserting detail the packet does not
        // contain, and the reason still names the unit being held.
        Assert.Contains("not yet recorded in the packet", artifact.QuestionText, StringComparison.Ordinal);
        Assert.Contains(ScaffoldWorkspace.Unit, artifact.Reason, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // Identity — the one guard that must never relax.
    // ---------------------------------------------------------------

    [Fact]
    public void APacketWhoseExecutionUnitDisagreesWithTheQueueItem_IsStillRefused_G561()
    {
        // A clarification filed against the wrong unit is worse than none.
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);
        workspace.RewritePacket(yaml => yaml.Replace(
            $"source_execution_unit: {ScaffoldWorkspace.Unit}", "source_execution_unit: G999", StringComparison.Ordinal));

        workspace.AssertRefusedBeforeMutation([ScaffoldWorkspace.Unit], "must match queue item execution unit");
    }

    [Fact]
    public void APacketMissingItsExecutionUnitEntirely_IsRefused_G561()
    {
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);
        workspace.RewritePacket(yaml => yaml.Replace(
            $"source_execution_unit: {ScaffoldWorkspace.Unit}", "# the identity line, removed", StringComparison.Ordinal));

        workspace.AssertRefusedBeforeMutation([ScaffoldWorkspace.Unit], "source_execution_unit");
    }

    // ---------------------------------------------------------------
    // Review repair: a DECLARED review_context_packet routes through the
    // unchanged strict serializer, whatever shape the declaration has.
    // ---------------------------------------------------------------

    [Fact]
    public void ADeclaredCompletePacket_StillSucceeds_AndUsesItsReviewContextIntents_G561()
    {
        // Positive control for the strict route: the complete-packet path is
        // still the path a complete packet takes, and it still works.
        workspace.WriteCompletePacketArtifacts();
        workspace.WriteQueueState(QueueItemState.Queued);
        ClarifyOpenCommand.TimestampFactory = () => ScaffoldWorkspace.FixedNow;

        using var writer = new StringWriter();
        Assert.Equal(0, ClarifyOpenCommand.Execute(workspace.Context, [ScaffoldWorkspace.Unit], writer));

        var artifact = workspace.ReadClarification();
        // Sourced from review_context_packet, not from the implementation
        // section — the complete-packet behaviour is unchanged.
        Assert.Equal(["RCP.ONLY.REFERENCE"], artifact.AffectedIntents);
    }

    [Fact]
    public void ADeclaredPacketMissingARequiredField_FailsBeforeMutation_G561()
    {
        workspace.WriteCompletePacketArtifacts();
        workspace.WriteQueueState(QueueItemState.Queued);
        workspace.RewritePacket(yaml => yaml.Replace(
            "  landing_policy: \"merge-after-review\"\n", string.Empty, StringComparison.Ordinal));

        // The strict serializer's own message, unchanged — tolerance is not
        // applied to a packet that declared itself complete.
        workspace.AssertRefusedBeforeMutation(
            [ScaffoldWorkspace.Unit], "must contain required field 'landing_policy'");
    }

    [Fact]
    public void ADeclaredPacketWithAWrongTypedField_FailsBeforeMutation_G561()
    {
        workspace.WriteCompletePacketArtifacts();
        workspace.WriteQueueState(QueueItemState.Queued);
        // review_context_packet.intent_references declared as a scalar.
        workspace.RewritePacket(yaml => yaml.Replace(
            "  intent_references:\n    - \"RCP.ONLY.REFERENCE\"\n",
            "  intent_references: \"RCP.ONLY.REFERENCE\"\n",
            StringComparison.Ordinal));

        workspace.AssertRefusedBeforeMutation([ScaffoldWorkspace.Unit], "must be a list");
    }

    [Fact]
    public void ADeclaredSectionThatIsNotAMapping_FailsBeforeMutation_G561()
    {
        // Present-but-wrong-shape. This is the case a "is it a mapping?" check
        // would have silently treated as ABSENT and then tolerated — which is
        // how a broken complete packet could have slipped through.
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);
        workspace.RewritePacket(yaml => yaml + "\nreview_context_packet: oops\n");

        var (exitCode, output) = workspace.RunClarifyOpen([ScaffoldWorkspace.Unit]);
        Assert.Equal(1, exitCode);
        // It reached the strict serializer rather than the tolerant path: the
        // tolerant path would have IGNORED a non-mapping section and succeeded.
        Assert.DoesNotContain("Clarification opened", output, StringComparison.Ordinal);
        workspace.AssertNothingMutated();
    }

    [Fact]
    public void AScaffoldThatMerelyDeclaresTheSection_LosesTolerance_G561()
    {
        // The declaration alone decides the route. A scaffold that claims to be
        // a complete packet is held to the complete contract and fails on the
        // implementation fields it never filled in.
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);
        workspace.RewritePacket(yaml => yaml + """

            review_context_packet:
              source_execution_unit: G900
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "RCP.ONLY.REFERENCE"
              rules_and_specs: []
              acceptance_criteria: []
              deterministic_review_checks: []
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);

        // It fails inside the strict serializer, which is the whole point.
        //
        // G565 changed WHERE it stops, and the new stop is the one this test
        // always meant. The scaffold used to trip the hand-rolled reader on its
        // own top-level comment lines ("invalid section header") — an accident
        // of the parser, not a statement about the packet. Now that projection
        // parses YAML with a YAML parser, comments are comments, and the
        // scaffold is refused for the real reason: declaring the section is a
        // claim of completeness, and the packet does not carry the required
        // implementation fields. Either way: refused, before any mutation.
        workspace.AssertRefusedBeforeMutation(
            [ScaffoldWorkspace.Unit], "Implementation issue packet must contain required field");
    }
}

/// <summary>
/// G561 review repair: the detection half lives in its own class because it
/// mutates <c>AutomationStalledWorkCommand</c>'s process-global seams, which are
/// owned by <see cref="AutomationStalledWorkSharedStateCollection"/>. A class can
/// join only one xUnit collection, and putting these assertions in the clarify
/// collection let the full suite run them in parallel with the stalled-work
/// suite — each resetting the other's factories mid-run. That is what turned a
/// focused-green fixture into a required-CI failure.
///
/// It deliberately does NOT touch <c>ClarifyOpenCommand.TimestampFactory</c>: it
/// reads the timestamp the artifact actually recorded and anchors the detector's
/// clock to that. So it owns exactly one collection's seams, and the age
/// assertion is relative to real recorded data rather than to a clock the test
/// imposed on two commands at once.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class ClarifyOpenScaffoldedPacketDetectionTests : IDisposable
{
    private readonly ScaffoldWorkspace workspace = new();

    public void Dispose()
    {
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        workspace.Dispose();
    }

    [Fact]
    public void TheResultingArtifact_IsReadByDesignDecisionPendingDetection_G561()
    {
        // Recording the clarification is only useful if the detector that
        // surfaces held design decisions can read what was written.
        Assert.Equal(0, workspace.RunPacketDraft());
        workspace.WriteQueueState(QueueItemState.Queued);

        using var openWriter = new StringWriter();
        Assert.Equal(0, ClarifyOpenCommand.Execute(
            workspace.Context, [ScaffoldWorkspace.Unit, "--question", "Flag or command?"], openWriter));

        // Anchor the detector's clock to what the artifact actually recorded,
        // rather than imposing a fixed clock on clarify open as well.
        var createdAt = workspace.ReadClarification().CreatedAt;
        AutomationStalledWorkCommand.UtcNowFactory = () => createdAt.AddMinutes(120);
        // Without this the command shells out to `gh` for its issue/PR
        // candidates. That happens to succeed on a developer machine with a
        // logged-in gh and fails on a CI runner — a test that depends on
        // ambient credentials is not a test. The design-decision lane reads the
        // clarification artifact off disk, so an empty candidate list is
        // exactly the right input.
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyCandidateLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", ScaffoldWorkspace.Domain, "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var item = document.RootElement.GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindDesignDecisionPending);

        Assert.Equal(ScaffoldWorkspace.Unit, item.GetProperty("execution_unit").GetString());
        Assert.Equal(120, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("Flag or command?", item.GetProperty("recommended_action").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>Keeps <c>stalled-work</c> off the network: no GitHub candidates, no `gh` invocation.</summary>
    private sealed class EmptyCandidateLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => [];
    }
}

/// <summary>
/// A throwaway repo root plus the packet/queue helpers both G561 clarify suites
/// need. Shared rather than duplicated so the two classes — which must live in
/// different xUnit collections — still exercise the same fixtures.
/// </summary>
internal sealed class ScaffoldWorkspace : IDisposable
{
    public const string Unit = "G900";
    public const string Domain = "intent-cli";
    public const string ReturnPath = "intents/intent-cli/clarifications/open.md";

    public static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private readonly string rootPath = Directory.CreateTempSubdirectory("clarify-open-scaffold-tests-").FullName;

    public CliContext Context => new()
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

    public string PacketYamlPath => Path.Combine(rootPath, ".intent-cli", "issues", Unit, "packet.yaml");

    public string ReviewContextPath => Path.Combine(rootPath, ".intent-cli", "issues", Unit, "review-context.md");

    public string QueueStatePath => Path.Combine(rootPath, ".intent-cli", "queue-state.json");

    public string ClarificationPath => Path.Combine(rootPath, ".intent-cli", "clarifications", Unit, "request.json");

    public int RunPacketDraft()
    {
        using var writer = new StringWriter();
        return PacketDraftCommand.Execute(
            Context,
            ["--execution-unit", Unit, "--target-repo", "J-Tech-Japan/intent-system"],
            writer);
    }

    public (int ExitCode, string Output) RunClarifyOpen(string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = ClarifyOpenCommand.Execute(Context, args, writer);
        return (exitCode, writer.ToString());
    }

    public void RewritePacket(Func<string, string> rewrite) =>
        File.WriteAllText(PacketYamlPath, rewrite(File.ReadAllText(PacketYamlPath)));

    public ClarificationItem ReadClarification() =>
        ClarificationSerializer.Deserialize(File.ReadAllText(ClarificationPath));

    public QueueItem ReadQueueItem() =>
        QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath))
            .Items.Single(item => item.ExecutionUnit == Unit);

    /// <summary>
    /// A refusal must happen BEFORE any mutation: exit 1, the named diagnostic,
    /// queue-state byte-identical, and no clarification artifact at all.
    /// </summary>
    public void AssertRefusedBeforeMutation(string[] args, string expectedMessage)
    {
        var queueBefore = File.ReadAllText(QueueStatePath);
        var (exitCode, output) = RunClarifyOpen(args);

        Assert.Equal(1, exitCode);
        Assert.Contains(expectedMessage, output, StringComparison.Ordinal);
        Assert.Equal(queueBefore, File.ReadAllText(QueueStatePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(ClarificationPath)!), output);
    }

    public void AssertNothingMutated() =>
        Assert.False(Directory.Exists(Path.GetDirectoryName(ClarificationPath)!));

    /// <summary>
    /// A packet that DECLARES review_context_packet — the complete-contract
    /// shape, used to prove the strict route is still the strict route.
    /// </summary>
    public void WriteCompletePacketArtifacts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PacketYamlPath)!);
        File.WriteAllText(PacketYamlPath, CompletePacketYaml);
        File.WriteAllText(ReviewContextPath, CompleteReviewContextMarkdown);
    }

    public void WriteQueueState(QueueItemState state)
    {
        Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
        File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(new QueueState
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

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private const string CompletePacketYaml = """
        implementation_issue_packet:
          issue_title: "[G900] Complete packet"
          issue_kind: "feature"
          source_execution_unit: "G900"
          goal: "Prove the strict route stays strict."
          in_scope:
            - "clarify open"
          out_of_scope:
            - "everything else"
          target_repo: "J-Tech-Japan/intent-system"
          target_path: "."
          target_part: "cli clarify open command"
          dependencies: []
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "clarify open stays entry-only"
          intent_references:
            - "IIP.ONLY.REFERENCE"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G900"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "RCP.ONLY.REFERENCE"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          deterministic_review_checks:
            - "clarify open command remains entry-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;

    private const string CompleteReviewContextMarkdown = """
        # Execution Unit

        `G900`

        # Deterministic Review Checks

        - clarify open command remains entry-only
        """;
}
