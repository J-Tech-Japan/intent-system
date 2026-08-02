using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G567: the queue-seed lane validates through the unified packet parser, so
/// malformed YAML can no longer classify <c>queue-seed-ready</c>.
///
/// This is G565's move one surface upstream and — the part that matters — on a
/// MUTATION path. The seeding lane read packet fields with a regex scalar
/// reader that never parsed the document, so a packet the schema and projection
/// surfaces both reject could seed the queue. The malformed unit then failed at
/// publish or preflight time, far from its cause.
///
/// Fail-closed here means all of: named parse error, no queue-state file, no
/// runs.jsonl entry, non-zero exit — in dry-run AND in <c>--write</c>.
/// </summary>
public sealed class QueueSeedPacketParsingG567Tests : IDisposable
{
    private readonly SeedWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    public static TheoryData<string, string> MalformedPackets() => new()
    {
        { "unterminated flow sequence", "  dependencies: [G1, G2" },
        { "plain scalar containing a colon-space", "  dependencies: G567 — plain, unquoted: no" },
        { "tab indentation", "\tdependencies: []" },
        { "a block sequence item with no parent key", "- orphan" },
    };

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public void MalformedPacket_FailsClosedInDryRun_G567(string description, string replacement)
    {
        workspace.WritePacket(SeedWorkspace.BuildPacket(replacement));

        var (exitCode, result) = workspace.RunSeed(write: false);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationUnsafe,
            result.GetProperty("classification").GetString());
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable,
            result.GetProperty("unsafe_reason").GetString());
        Assert.False(File.Exists(workspace.QueueStatePath), $"dry-run created queue-state.json for {description}");
        Assert.False(File.Exists(workspace.RunsPath), $"dry-run appended runs.jsonl for {description}");
    }

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public void MalformedPacket_FailsClosedInWriteMode_G567(string description, string replacement)
    {
        // The write path is the one that matters: this is the difference
        // between a diagnostic and a malformed unit living in the queue.
        workspace.WritePacket(SeedWorkspace.BuildPacket(replacement));

        var (exitCode, result) = workspace.RunSeed(write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationUnsafe,
            result.GetProperty("classification").GetString());
        Assert.False(File.Exists(workspace.QueueStatePath), $"--write seeded the queue for {description}");
        Assert.False(File.Exists(workspace.RunsPath), $"--write appended runs.jsonl for {description}");
    }

    [Fact]
    public void MalformedPacket_LeavesAnExistingQueueStateByteIdentical_G567()
    {
        // "No mutation" has to mean the file on disk, not just "no new item".
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies: [G1, G2"));
        workspace.WriteExistingQueueState();
        var before = File.ReadAllBytes(workspace.QueueStatePath);

        var (exitCode, _) = workspace.RunSeed(write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(before, File.ReadAllBytes(workspace.QueueStatePath));
        Assert.False(File.Exists(workspace.RunsPath));
    }

    [Fact]
    public void TheParseFailure_IsNamedInTheSummary_G567()
    {
        // A fail-closed stop the operator cannot diagnose just moves the cost.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies: [G1, G2"));

        var (_, result) = workspace.RunSeed(write: true);

        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("packet.yaml", summary, StringComparison.Ordinal);
        Assert.Contains("not valid YAML", summary, StringComparison.Ordinal);
    }

    // ------------------------------------------------------ byte-compatibility

    [Fact]
    public void AWellFormedPacket_SeedsExactlyAsBefore_G567()
    {
        // The seeded fields come from the parsed document now, so this pins that
        // the values did not shift: same classification, same title, same
        // dependencies expanded from the same flow sequence.
        workspace.WritePacket(SeedWorkspace.BuildPacket(
            "  dependencies: [G565, G566]",
            issueTitle: "Queue seed parity"));

        var (exitCode, result) = workspace.RunSeed(write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationReady,
            result.GetProperty("classification").GetString());

        var seeded = result.GetProperty("seeded_item");
        Assert.Equal("Queue seed parity", seeded.GetProperty("title").GetString());
        Assert.Collection(
            seeded.GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()),
            dependency => Assert.Equal("G565", dependency),
            dependency => Assert.Equal("G566", dependency));
    }

    [Fact]
    public void AQuotedTitleLosesItsQuotes_AsEveryOtherSurfaceAlreadyReadsIt_G567()
    {
        // The regex reader stripped quotes with a hand-rolled rule; the YAML
        // parser does it natively. Pinned because the seeded TITLE is what an
        // operator sees in the queue.
        workspace.WritePacket(SeedWorkspace.BuildPacket(
            "  dependencies: []",
            issueTitle: "G567: quoted — with a colon"));

        var (exitCode, result) = workspace.RunSeed(write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "G567: quoted — with a colon",
            result.GetProperty("seeded_item").GetProperty("title").GetString());
    }

    // --------------------------------------------------------- parser contract

    [Fact]
    public void PacketYamlDocument_FlattensDottedPathsWithBareKeyAliases_G567()
    {
        Assert.True(PacketYamlDocument.TryParse(
            """
            domain: intent-cli
            implementation_issue_packet:
              issue_title: "titled"
              target_repo: owner/repo
              dependencies: [A, B]
              blocked_by:
                - C
              empty_value:
            """,
            out var document,
            out var error));
        Assert.Equal(string.Empty, error);

        var fields = document!.Fields;
        Assert.Equal("intent-cli", fields["domain"]);
        Assert.Equal("titled", fields["implementation_issue_packet.issue_title"]);
        // Bare-key alias, exactly as the previous reader produced.
        Assert.Equal("titled", fields["issue_title"]);

        // G568 MOVED this pin rather than deleting it. G567 pinned the lossy
        // behaviour it faithfully preserved: a FLOW sequence survived as the
        // bracket TEXT `"[A, B]"` in Fields, and a BLOCK sequence recorded
        // nothing at all. The follow-up this pin was flagging is now done, so it
        // pins the faithful behaviour instead — sequences are structured, both
        // styles are indistinguishable, and no bracket text remains.
        Assert.False(fields.ContainsKey("implementation_issue_packet.dependencies"));
        Assert.Equal(["A", "B"], document.Sequences["implementation_issue_packet.dependencies"]);
        Assert.Equal(["C"], document.Sequences["implementation_issue_packet.blocked_by"]);
        Assert.Equal(["A", "B"], document.LookupSequence("implementation_issue_packet.dependencies"));

        // A valueless key still records nothing — unchanged, and still correct:
        // an absent value is not an empty list, it is an absent declaration.
        Assert.False(fields.ContainsKey("implementation_issue_packet.empty_value"));
        Assert.False(document.Sequences.ContainsKey("implementation_issue_packet.empty_value"));
    }

    [Fact]
    public void PacketYamlDocument_BareKeyAliasIsFirstWins_G567()
    {
        // The previous reader wrote the bare alias only when absent, so a nested
        // key could not shadow a top-level one seen earlier.
        Assert.True(PacketYamlDocument.TryParse(
            """
            target_repo: top/level
            implementation_issue_packet:
              target_repo: nested/one
            """,
            out var document,
            out _));

        Assert.Equal("top/level", document!.Fields["target_repo"]);
        Assert.Equal("nested/one", document.Fields["implementation_issue_packet.target_repo"]);
    }

    private sealed class SeedWorkspace : IDisposable
    {
        public const string Unit = "G567";
        public const string Domain = "intent-cli";
        public const string TargetRepo = "J-Tech-Japan/intent-system";

        public SeedWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("queue-seed-parsing-g567-").FullName;
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

        public string QueueStatePath => Context.GetQueueStatePath();

        public string RunsPath => Context.GetRunLogPath();

        public void WritePacket(string yaml)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), yaml);
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# impl\n");
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"),
                "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n"
                + "## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n"
                + "## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n## Base Branch Policy\nx\n");
        }

        public void WriteExistingQueueState() => File.WriteAllText(QueueStatePath, """
            {
              "schema_version": "1",
              "updated_at": "2026-08-01T00:00:00+00:00",
              "items": []
            }
            """);

        public (int ExitCode, JsonElement Result) RunSeed(bool write)
        {
            using var writer = new StringWriter();
            var args = new List<string>
            {
                "--execution-unit", Unit, "--domain", Domain, "--target-repo", TargetRepo, "--format", "json",
            };
            if (write)
            {
                args.Add("--write");
            }

            var exitCode = AutomationQueueSeedFromPacketCommand.Execute(Context, args.ToArray(), writer);
            var document = JsonDocument.Parse(writer.ToString());
            return (exitCode, document.RootElement.Clone());
        }

        public static string BuildPacket(string dependenciesLine, string issueTitle = "Demo") => $"""
            domain: {Domain}
            implementation_issue_packet:
              source_execution_unit: {Unit}
              issue_title: "{issueTitle}"
              target_repo: {TargetRepo}
            {dependenciesLine}
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
