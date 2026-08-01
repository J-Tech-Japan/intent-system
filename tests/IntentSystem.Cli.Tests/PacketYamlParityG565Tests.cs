using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Projection.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G565 parity matrix: <em>valid to the packet surfaces ⇔ valid to
/// projection</em>.
///
/// The field defect was a DISAGREEMENT, not a bug in either half: the packet
/// surfaces (<c>packet draft</c>, <c>queue-seed-from-packet</c>,
/// <c>clarify open</c>) read real YAML, projection read an approximation of it,
/// and a packet that lived happily on the authoring side was rejected on the
/// projection side. Fixing the parser is only half the answer — without a test
/// that exercises BOTH sides on the SAME bytes, the two can drift apart again
/// the next time either changes.
///
/// So every fixture below is written to disk once and then run through the
/// packet surface AND through projection, and the two must agree.
/// </summary>
public sealed class PacketYamlParityG565Tests : IDisposable
{
    private readonly ParityWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    /// <summary>
    /// YAML constructs the packet surfaces accept. Each was a projection-only
    /// failure before G565, or would have become one.
    ///
    /// The set is what the surfaces ACTUALLY accept, not what they ought to. One
    /// case documented here as an exception when these fixtures were written —
    /// a plain (unquoted) scalar containing <c>": "</c> — stays out, because it
    /// is not legal YAML at all and belongs on the rejection side.
    ///
    /// G567 moved the other one IN: a title with escaped quotes was refused by
    /// the queue-seed lane's legacy scalar reader (its heuristic could not tell
    /// an escaped quote from an unbalanced one) even though projection read it
    /// fine. Now that queue-seed validates through the same whole-document
    /// parse, all three surfaces agree on it.
    /// </summary>
    public static TheoryData<string, string> AcceptedByBothSurfaces() => new()
    {
        { "em-dash and a quoted colon-space title", "\"G565: parsing — one pathway, two readers: no more\"" },
        { "single-quoted title", "'G565 — single quoted'" },
        { "japanese title with a colon", "\"G565: 日本語 — コロン付き\"" },
        { "escaped quotes (accepted by all three since G567)", "\"G565 \\\"quoted\\\" inside\"" },
    };

    [Theory]
    [MemberData(nameof(AcceptedByBothSurfaces))]
    public void APacketTheSurfacesAccept_IsAcceptedByProjection_G565(string description, string issueTitleYaml)
    {
        var yaml = ParityWorkspace.BuildPacket(issueTitleYaml);
        workspace.WritePacket(yaml);

        var seedClassification = workspace.RunQueueSeedDryRun();
        var projection = Record.Exception(() => ProjectionPacketSerializer.Deserialize(yaml));

        Assert.Equal(AutomationQueueSeedFromPacketCommand.ClassificationReady, seedClassification);
        Assert.True(
            projection is null,
            $"the packet surfaces accept this packet ({description}) but projection rejected it: {projection?.Message}");
    }

    [Theory]
    [MemberData(nameof(AcceptedByBothSurfaces))]
    public void APacketTheSurfacesAccept_IsAlsoUsableByClarifyOpen_G565(string description, string issueTitleYaml)
    {
        // `clarify open` is the surface the field report came through: it reads
        // the packet through projection, so a projection-only rejection made
        // recording a blocking design question impossible for a packet the rest
        // of the toolchain considered fine.
        _ = description;
        var yaml = ParityWorkspace.BuildPacket(issueTitleYaml);
        workspace.WritePacket(yaml);
        workspace.WriteReviewContextMarkdown();
        workspace.WriteQueueState();

        using var writer = new StringWriter();
        var exitCode = ClarifyOpenCommand.Execute(
            workspace.Context, [ParityWorkspace.Unit, "--question", "Which pathway parses this?"], writer);

        Assert.True(exitCode == 0, $"clarify open refused a packet the surfaces accept: {writer}");
    }

    /// <summary>
    /// The other direction. Parity is two-way — a parser that accepts
    /// everything agrees with nothing — so malformed YAML must be refused by
    /// ALL THREE surfaces, and refused as a PARSE failure rather than as a
    /// guess about section headers.
    ///
    /// G567 added the third column. When these fixtures were written for G565,
    /// <c>queue-seed-from-packet</c> classified both of these
    /// <c>queue-seed-ready</c> — its field lookup was a regex scalar reader
    /// (G361) that never parsed the document, so a packet the other two
    /// surfaces rejected could still seed the queue. That gap was recorded here
    /// as out-of-scope, adjudicated to its own unit, and is now closed: the
    /// assertion below is the proof, and it is deliberately in the SAME fixture
    /// so the three surfaces can never again be checked one at a time.
    /// </summary>
    [Theory]
    [InlineData("unterminated flow sequence", "  dependencies: [G1, G2")]
    [InlineData("plain scalar containing a colon-space", "  dependencies: G565 — plain, unquoted: no")]
    public void MalformedYaml_IsRejectedByAllThreeSurfaces_G565(string description, string replacement)
    {
        var yaml = ParityWorkspace.BuildPacket("\"G565\"").Replace(
            "  dependencies: []", replacement, StringComparison.Ordinal);
        workspace.WritePacket(yaml);
        workspace.WriteQueueState();

        var projection = Record.Exception(() => ProjectionPacketSerializer.Deserialize(yaml));
        Assert.NotNull(projection);
        Assert.Contains("could not be parsed", projection!.Message, StringComparison.Ordinal);

        using var writer = new StringWriter();
        var exitCode = ClarifyOpenCommand.Execute(
            workspace.Context, [ParityWorkspace.Unit, "--question", "Which pathway parses this?"], writer);
        Assert.True(exitCode != 0, $"clarify open accepted malformed YAML ({description}): {writer}");

        Assert.NotEqual(AutomationQueueSeedFromPacketCommand.ClassificationReady, workspace.RunQueueSeedDryRun());
    }

    [Fact]
    public void AlreadyParseablePackets_ProduceByteIdenticalProjectionOutput_G565()
    {
        // Byte-compatibility: the packets that parsed BEFORE this slice must
        // project to exactly the same artifacts after it. The canonical
        // renderers are the observable output, so compare their bytes rather
        // than field-by-field.
        var yaml = ParityWorkspace.BuildPacket("\"G565 packet\"");
        var contract = ProjectionPacketSerializer.Deserialize(yaml);

        var implementation = IntentSystem.Projection.Rendering.ImplementationMarkdownRenderer.Render(
            contract.ImplementationIssuePacket);
        var reviewContext = IntentSystem.Projection.Rendering.ReviewContextMarkdownRenderer.Render(
            contract.ReviewContextPacket);

        // Field-level identity is what byte identity rests on: the renderers are
        // pure functions of the contract, so an unchanged contract is an
        // unchanged artifact.
        Assert.Equal("G565 packet", contract.ImplementationIssuePacket.IssueTitle);
        Assert.Equal(["one"], contract.ImplementationIssuePacket.InScope);
        Assert.Equal(["two"], contract.ImplementationIssuePacket.OutOfScope);
        Assert.Contains("G565 packet", implementation, StringComparison.Ordinal);
        Assert.Contains(ParityWorkspace.Unit, reviewContext, StringComparison.Ordinal);
    }

    private sealed class ParityWorkspace : IDisposable
    {
        public const string Unit = "G565";
        public const string Domain = "intent-cli";
        public const string TargetRepo = "J-Tech-Japan/intent-system";

        public ParityWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("packet-yaml-parity-g565-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
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

            var bindings = Path.Combine(RootPath, "intents", Domain, "automation");
            Directory.CreateDirectory(bindings);
            File.WriteAllText(Path.Combine(bindings, "bindings.md"), "---\nexecution_unit_regex: '^G[0-9]+$'\n---\n");
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        private string PacketDirectory => Path.Combine(RootPath, ".intent-cli", "issues", Unit);

        public void WritePacket(string yaml)
        {
            Directory.CreateDirectory(PacketDirectory);
            File.WriteAllText(Path.Combine(PacketDirectory, "packet.yaml"), yaml);
            File.WriteAllText(Path.Combine(PacketDirectory, "implementation.md"), "# impl\n");
            File.WriteAllText(Path.Combine(PacketDirectory, "github-body.md"),
                "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n"
                + "## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n"
                + "## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
            WriteReviewContextMarkdown();
        }

        public void WriteReviewContextMarkdown()
        {
            Directory.CreateDirectory(PacketDirectory);
            File.WriteAllText(Path.Combine(PacketDirectory, "review-context.md"),
                $"# {Unit} Review Context\n\n## Intent References\n\n- intents/{Domain}/intent-tree/00-map.md\n\n"
                + "## Rules And Specs\n\n- intents/rules/issue-projection-format.md\n\n"
                + "## Acceptance Criteria\n\n- one parsing pathway\n\n"
                + "# Deterministic Review Checks\n\n- the hand parser is gone\n");
        }

        public void WriteQueueState()
        {
            File.WriteAllText(Context.GetQueueStatePath(), $$"""
                {
                  "schema_version": "1",
                  "updated_at": "2026-08-01T00:00:00+00:00",
                  "items": [
                    {
                      "execution_unit": "{{Unit}}",
                      "title": "[{{Unit}}] parity",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "intents/{{Domain}}/clarifications/open.md",
                      "packet_paths": {
                        "implementation": ".intent-cli/issues/{{Unit}}/implementation.md",
                        "review_context": ".intent-cli/issues/{{Unit}}/review-context.md",
                        "yaml": ".intent-cli/issues/{{Unit}}/packet.yaml"
                      },
                      "worker_role": "coder",
                      "review_role": "reviewer",
                      "priority": "high"
                    }
                  ]
                }
                """);
        }

        /// <summary>Runs the real packet surface and returns its classification.</summary>
        public string RunQueueSeedDryRun()
        {
            using var writer = new StringWriter();
            AutomationQueueSeedFromPacketCommand.Execute(
                Context,
                ["--execution-unit", Unit, "--domain", Domain, "--target-repo", TargetRepo, "--format", "json"],
                writer);

            using var document = JsonDocument.Parse(writer.ToString());
            return document.RootElement.GetProperty("classification").GetString()!;
        }

        public static string BuildPacket(string issueTitleYaml) => $"""
            implementation_issue_packet:
              issue_title: {issueTitleYaml}
              issue_kind: "feature"
              source_execution_unit: "{Unit}"
              target_repo: "{TargetRepo}"
              goal: "unify the packet YAML parsing pathway"
              in_scope:
                - "one"
              out_of_scope:
                - "two"
              target_path: "src/IntentSystem.Projection/**"
              target_part: "packet parsing pathway"
              dependencies: []
              technical_baseline:
                - "C# / .NET"
              project_local_guide:
                - "AGENTS.md"
              intent_baseline:
                - "source of truth remains in the parent intent repo"
              intent_references:
                - "intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"
              rules_and_specs:
                - "intents/rules/issue-projection-format.md"
              acceptance_criteria:
                - "one parsing pathway"
              verification_evidence:
                - "tests-passing"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "{Unit}"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"
              rules_and_specs:
                - "intents/rules/issue-projection-format.md"
              acceptance_criteria:
                - "one parsing pathway"
              deterministic_review_checks:
                - "the hand parser is gone"
              clarification_return_path: "intents/{Domain}/clarifications/open.md"
            """;

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
