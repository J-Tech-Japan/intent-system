using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class UnitStatusCommandG855Tests
{
    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [Fact]
    public void HostlessStatusReturnsStructuredUnavailableAndHelpRemainsMetadataFree()
    {
        using var writer = new StringWriter();
        var exit = UnitStatusCommand.ExecuteMetadataFree(
            ["unit", "status", "--execution-unit", "G855", "--format", "json"],
            "/tmp/child-without-host",
            writer);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("host-context-unavailable", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", document.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());

        using var help = new StringWriter();
        Assert.Equal(0, UnitStatusCommand.ExecuteMetadataFree(["unit", "status", "--help"], "/tmp/child", help));
        Assert.Contains("Read-only lifecycle evidence", help.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidFormatProducesStructuredJsonInsteadOfSilentlyUsingMarkdown()
    {
        using var host = new TempHost();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--format", "xml"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalid-request", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal(0, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void MissingFormatIsStructuredInvalidRequest()
    {
        using var host = new TempHost();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalid-request", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal(0, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void ExactRecordedNonSoloModeReturnsNotApplicableWithoutGithubCalls()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer,
            reader);

        Assert.Equal(0, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("delivery", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("not-applicable", step.GetProperty("state").GetString()));
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("not-applicable").GetInt32());
    }

    [Fact]
    public void MissingExactModeEntryIsUnavailableAndNeverUsesFallbackOrGithub()
    {
        using var host = new TempHost();
        host.WriteMode("delivery", domain: "other-domain", team: "other-team");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer,
            reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-mode-unrecorded", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void UnreadableCurrentPacketFileIsUnavailableRatherThanReportedMissing()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "implementation.md"));
        using var writer = new StringWriter();
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed",
            HeadBefore = HeadSha,
            HeadAfter = HeadSha,
            HeadSha = HeadSha,
            Facts = [],
        });

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var fileFact = document.RootElement.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
            .Single(fact => fact.GetProperty("id").GetString() == "packet-current-files");
        Assert.Equal("unavailable", fileFact.GetProperty("state").GetString());
        Assert.Equal("read-failure", fileFact.GetProperty("unavailable_class").GetString());
        Assert.Contains(fileFact.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/packet.yaml");
    }

    [Fact]
    public void DigestValidatedLocalReviewIsRecordedEvidenceAndDoesNotSubstituteForPostedReview()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        host.WriteReviewRecord();
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed",
            HeadBefore = HeadSha,
            HeadAfter = HeadSha,
            HeadSha = HeadSha,
            Facts =
            [
                new UnitStatusFact { Id = "delta-review", State = UnitStatusStates.Done, Cause = "delta-review-current-head" },
                new UnitStatusFact { Id = "posted-review", State = UnitStatusStates.Missing, Cause = "posted-review-not-recorded-current-head" },
            ],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var document = JsonDocument.Parse(writer.ToString());
        var reviewStep = document.RootElement.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("id").GetString() == "independent-subagent-review");
        Assert.Equal("done", reviewStep.GetProperty("subchecks")[0].GetProperty("state").GetString());
        Assert.Equal("missing", reviewStep.GetProperty("subchecks")[1].GetProperty("state").GetString());
        var observed = Assert.Single(document.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("reviews").EnumerateArray());
        Assert.Equal("local-cross-runtime-record", observed.GetProperty("source").GetString());
        Assert.Equal("request-changes", observed.GetProperty("verdict").GetString());
        Assert.Equal("cross-runtime", observed.GetProperty("relation").GetString());
    }

    private sealed class FixedReader : IUnitStatusSnapshotReader
    {
        private readonly UnitStatusRemoteSnapshot? _remote;
        public FixedReader(UnitStatusRemoteSnapshot? remote = null) => _remote = remote;
        public int ClaimReads { get; private set; }
        public int GitHubReads { get; private set; }

        public UnitStatusClaimSnapshot ReadClaimSnapshot(CliContext context, string executionUnit)
        {
            ClaimReads++;
            return new UnitStatusClaimSnapshot { State = "completed" };
        }

        public UnitStatusRemoteSnapshot ObserveGitHub(CliContext context, string repo, int issue, int pullRequest,
            string executionUnit, string domain, string team)
        {
            GitHubReads++;
            return _remote ?? new UnitStatusRemoteSnapshot { State = "completed", Facts = [] };
        }
    }

    private sealed class TempHost : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "g855-status-" + Guid.NewGuid().ToString("N"));

        public TempHost()
        {
            Directory.CreateDirectory(Path.Combine(_root, ".intent-cli"));
        }

        public CliContext Context => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
            },
        };

        public void WriteMode(string mode, string domain = "intent-cli", string team = "intent-cli-dev")
        {
            var state = new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries =
                [
                    new TeamModeEntry
                    {
                        Domain = domain,
                        Team = team,
                        Mode = mode,
                        UpdatedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
                        Transitions =
                        [
                            new TeamModeTransition
                            {
                                From = TeamMode.Default,
                                To = mode,
                                At = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
                            },
                        ],
                    },
                ],
            };
            File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
        }

        public void WritePacketAndPublishArtifact()
        {
            var directory = Path.Combine(_root, ".intent-cli", "issues", "G855");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), "schema_version: 1\ndomain: intent-cli\nimplementation_issue_packet:\n  target_repo: J-Tech-Japan/intent-system\n");
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = "G855",
                PublishStatus = "published",
                PacketPath = ".intent-cli/issues/G855/packet.yaml",
                IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
                CreatedIssueNumber = 1862,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1862",
                PublishedLabelName = "intent-target",
                LifecycleState = "pr-created",
                LinkedPrNumber = 1864,
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1864",
            };
            File.WriteAllText(Path.Combine(directory, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void WriteReviewRecord()
        {
            var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                verdict = "request-changes",
                head_sha = HeadSha,
                blocking_findings = new[] { new { file = "src/Unit.cs", line = 1, scenario = "fixture finding" } },
                notes = Array.Empty<string>(),
            }));
            var record = new CrossRuntimeReviewRecord
            {
                ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
                Repo = "J-Tech-Japan/intent-system",
                Pr = 1864,
                HeadSha = HeadSha,
                ExecutionUnit = "G855",
                Domain = "intent-cli",
                Team = "intent-cli-dev",
                Kind = CrossRuntimeReviewRecord.KindImplementation,
                Runtime = "codex",
                RuntimeVersion = "codex-test",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationCrossRuntime,
                Verdict = "request-changes",
                BlockingFindings = [new CrossRuntimeReviewFinding { File = "src/Unit.cs", Line = 1, Scenario = "fixture finding" }],
                Notes = [],
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                RawVerdictFile = "",
                RawVerdictSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
            };
            record = record with { RawVerdictFile = CrossRuntimeReviewStore.RawRelativePath(record) };
            var result = CrossRuntimeReviewStore.Write(_root, record, raw);
            Assert.True(result.Written, result.Error);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
