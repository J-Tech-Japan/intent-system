using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class UnitStatusReadOnlyG855Tests
{
    private readonly string _repo = "J-Tech-Japan/intent-system";
    private readonly string _head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _metadataOid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly int _publicIssue = 1862;
    private readonly int _pullRequest = 1863;

    [Theory]
    [InlineData("success", 0)]
    [InlineData("provenance", 0)]
    [InlineData("api-503", 1)]
    [InlineData("malformed-json", 1)]
    public void ExecuteCore_UsesRealLocalAndRemoteParsers_AndLeavesTheHostByteIdentical(string scenario, int expectedExit)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner(scenario, _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Equal(6, github.Calls.Count);
        Assert.True(expectedExit == exit, output.ToString());

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("G855", root.GetProperty("execution_unit").GetString());
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal(_pullRequest, root.GetProperty("identity").GetProperty("pr").GetInt32());
        Assert.Equal(_head, root.GetProperty("identity").GetProperty("head_sha").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.Equal("solo-conductor", root.GetProperty("team_mode").GetString());
        Assert.Equal(expectedExit, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").EnumerateObject().Sum(item => item.Value.GetInt32()));

        if (scenario == "success")
        {
            Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            Assert.Equal("missing", FindFact(root, "issue-completion-marker").GetProperty("state").GetString());
            Assert.Equal("missing", FindFact(root, "posted-review").GetProperty("state").GetString());
            Assert.Equal("missing", FindFact(root, "observed-ci").GetProperty("state").GetString());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("unavailable_class_counts").GetProperty("provenance-limit").GetInt32());
        }
        else if (scenario == "provenance")
        {
            Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            AssertProvenanceLimit(FindFact(root, "posted-review"));
            AssertProvenanceLimit(FindFact(root, "delta-review"));
            var classes = root.GetProperty("summary").GetProperty("unavailable_class_counts");
            Assert.True(classes.GetProperty("provenance-limit").GetInt32() >= 2);
            Assert.Equal(0, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32()
                - classes.GetProperty("provenance-limit").GetInt32());
        }
        else if (scenario == "api-503")
        {
            Assert.Equal("unavailable", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            var marker = FindFact(root, "issue-completion-marker");
            Assert.Equal("unavailable", marker.GetProperty("state").GetString());
            Assert.Equal("github-read-failed", marker.GetProperty("cause").GetString());
            Assert.Equal("read-failure", marker.GetProperty("unavailable_class").GetString());
        }
        else
        {
            Assert.Equal("unavailable", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            var ci = FindFact(root, "observed-ci");
            Assert.Equal("unavailable", ci.GetProperty("state").GetString());
            Assert.Equal("github-json-invalid", ci.GetProperty("cause").GetString());
            Assert.Equal("read-failure", ci.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void ExecuteCore_AbsentExactModeRefusesBeforeGithub_AndPreservesEveryHostByte()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("team-mode-unrecorded", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("unavailable", step.GetProperty("state").GetString()));
        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("team-mode-unrecorded", fact.GetProperty("cause").GetString());
            Assert.Equal("applicability-unresolved", fact.GetProperty("unavailable_class").GetString());
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Fact]
    public void ExecuteCore_MalformedModeIsUnavailableBeforeGithub_AndPreservesEveryHostByte()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteMalformedMode();
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        AssertAllFactsUnavailable(report.RootElement, "team-mode-unreadable", "applicability-unresolved");
        Assert.Equal("not-observed", report.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
    }

    [Fact]
    public void ExecuteCore_CurrentDeliveryOverridesHistoricSolo_AndSkipsCorruptOptionalReceipt()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        host.WriteCorruptOptionalReviewRecord(_repo, _pullRequest);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(0, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("delivery", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.StartsWith("2026-10-03T", root.GetProperty("applicability").GetProperty("mode_entry_updated_at").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("not-applicable", step.GetProperty("state").GetString()));
        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("not-applicable", fact.GetProperty("state").GetString());
            Assert.Equal("recorded-non-solo-mode", fact.GetProperty("cause").GetString());
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("not-applicable").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Fact]
    public void ExecuteCore_CurrentSoloOverridesHistoricDelivery_AndObservesThePublishedIssueAndPr()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.SoloConductor, TeamMode.Delivery);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Equal(6, github.Calls.Count);
        Assert.Equal(0, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("solo-conductor", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal(_pullRequest, root.GetProperty("identity").GetProperty("pr").GetInt32());
    }

    [Fact]
    public void ExecuteCore_ClaimSnapshotFailurePrecedesCurrentDeliveryAndKeepsItsObservedPointer()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        var git = new BoundedGitRunner(_head, _metadataOid, failMetadataRef: true);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(3, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        AssertAllFactsUnavailable(root, "claim-snapshot-unavailable", "read-failure");
        AssertClaimSnapshotEvidence(root);
    }

    [Fact]
    public void ExecuteCore_ExplicitScopeDoesNotEstablishAnUnknownUnitBeforeDeliveryRefusal()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        var git = new BoundedGitRunner(_head, _metadataOid, executionUnit: "G999");
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G999", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        AssertAllFactsUnavailable(root, "unit-identity-unestablished", "applicability-unresolved");
        Assert.Equal("G999", root.GetProperty("execution_unit").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("team").GetString());
        Assert.Null(root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("issue").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("pr").ValueKind);
        Assert.Empty(root.GetProperty("identity").GetProperty("sources").EnumerateArray());
        AssertClaimSnapshotEvidence(root);
    }

    [Fact]
    public void ProgramMain_HostlessStatusIsStructuredAndHelpIsMetadataFreeWithoutWritingFiles()
    {
        using var host = new HostFixture();
        var child = Directory.CreateDirectory(Path.Combine(host.Root, "child"));
        using var directoryScope = new CurrentDirectoryScope(child.FullName);
        using var console = new ConsoleScope();
        var before = host.SnapshotAllFilesAndGitMarkers();

        var statusExit = Program.Main(["unit", "status", "--execution-unit", "G855", "--format", "json"]);
        var statusOutput = console.Output.ToString();
        console.Output.GetStringBuilder().Clear();
        var helpExit = Program.Main(["unit", "status", "--help"]);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(1, statusExit);
        Assert.Equal(0, helpExit);
        using var report = JsonDocument.Parse(statusOutput);
        var root = report.RootElement;
        Assert.Equal("host-context-unavailable", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.Contains("Read-only lifecycle evidence", console.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, console.Error.ToString());
    }

    private JsonElement FindFact(JsonElement root, string id) => root.GetProperty("steps").EnumerateArray()
        .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
        .Single(fact => fact.GetProperty("id").GetString() == id);

    private void AssertAllFactsUnavailable(JsonElement root, string cause, string unavailableClass)
    {
        Assert.Equal("unavailable", root.GetProperty("applicability").GetProperty("state").GetString());
        Assert.Equal(cause, root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal(unavailableClass, root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("unavailable", step.GetProperty("state").GetString()));
        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal(cause, fact.GetProperty("cause").GetString());
            Assert.Equal(unavailableClass, fact.GetProperty("unavailable_class").GetString());
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    private void AssertClaimSnapshotEvidence(JsonElement root)
    {
        var claimFact = FindFact(root, "design-claim-acquired");
        var pointer = Assert.Single(claimFact.GetProperty("evidence").EnumerateArray());
        Assert.Equal("local-claim-snapshot", pointer.GetProperty("kind").GetString());
        Assert.Contains("refs/remotes/origin/metadata", pointer.GetProperty("provenance").GetString(), StringComparison.Ordinal);
    }

    private void AssertProvenanceLimit(JsonElement fact)
    {
        Assert.Equal("unavailable", fact.GetProperty("state").GetString());
        Assert.Equal("legacy-review-identity-unrecorded", fact.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
        Assert.Empty(fact.GetProperty("repair_commands").EnumerateArray());
    }

    private sealed class HostFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("unit-status-readonly-g855-").FullName;
        private readonly string[] _trackedGitFiles =
        [
            ".git/HEAD",
            ".git/index",
            ".git/refs/heads/main",
            ".git/packed-refs",
            ".git/FETCH_HEAD",
        ];

        public string Root => _root;

        public CliContext Context => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    MetadataSourceBranch = "metadata",
                },
            },
        };

        public void Prepare(bool includeCurrentSoloMode)
        {
            Directory.CreateDirectory(Path.Combine(_root, ".intent-cli", "issues", "G855"));
            Directory.CreateDirectory(Path.Combine(_root, ".git", "refs", "heads"));
            File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/main\n");
            File.WriteAllBytes(Path.Combine(_root, ".git", "index"), [0x44, 0x49, 0x52, 0x43, 0x00, 0x01]);
            File.WriteAllText(Path.Combine(_root, ".git", "refs", "heads", "main"), "cccccccccccccccccccccccccccccccccccccccc\n");
            File.WriteAllText(Path.Combine(_root, ".git", "packed-refs"), "# pack-refs with: peeled fully-peeled\n");
            File.WriteAllText(Path.Combine(_root, ".git", "FETCH_HEAD"), "fixture-fetch-head-bytes\n");
            File.WriteAllText(
                Path.Combine(_root, ".intent-cli", "issues", "G855", "packet.yaml"),
                "schema_version: 1\ndomain: intent-cli\nimplementation_issue_packet:\n  target_repo: J-Tech-Japan/intent-system\n");
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = "G855",
                PublishStatus = "published",
                PacketPath = ".intent-cli/issues/G855/packet.yaml",
                IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
                CreatedIssueNumber = 1862,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1862",
                PublishedLabelName = "intent-target",
                LifecycleState = IssuePublishLifecycle.PrCreated,
                LinkedPrNumber = 1863,
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1863",
            };
            File.WriteAllText(
                Path.Combine(_root, ".intent-cli", "issues", "G855", "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(artifact));

            if (includeCurrentSoloMode)
            {
                var timestamp = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
                var state = new TeamModeState
                {
                    SchemaVersion = TeamModeStore.SchemaVersion,
                    Entries =
                    [
                        new TeamModeEntry
                        {
                            Domain = "intent-cli",
                            Team = "intent-cli-dev",
                            Mode = TeamMode.SoloConductor,
                            UpdatedAt = timestamp,
                            Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = timestamp }],
                        },
                    ],
                };
                File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
            }
        }

        public void WriteMalformedMode()
        {
            File.WriteAllText(TeamModeStore.ResolvePath(_root), "{ malformed team mode");
        }

        public void WriteModeHistory(string currentMode, string historicalMode)
        {
            var earlier = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
            var current = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
            IReadOnlyList<TeamModeTransition> transitions;
            if (currentMode == TeamMode.Delivery && historicalMode == TeamMode.SoloConductor)
            {
                transitions =
                [
                    new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = earlier },
                    new TeamModeTransition { From = TeamMode.SoloConductor, To = TeamMode.Delivery, At = current },
                ];
            }
            else if (currentMode == TeamMode.SoloConductor && historicalMode == TeamMode.Delivery)
            {
                transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = current }];
            }
            else
            {
                throw new ArgumentException("The fixture accepts only the two requested current/history mode pairs.");
            }

            var entry = new TeamModeEntry
            {
                Domain = "intent-cli",
                Team = "intent-cli-dev",
                Mode = currentMode,
                UpdatedAt = transitions[^1].At,
                Transitions = transitions,
            };
            var state = new TeamModeState { SchemaVersion = TeamModeStore.SchemaVersion, Entries = [entry] };
            File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
        }

        public void WriteCorruptOptionalReviewRecord(string repo, int pr)
        {
            var directory = CrossRuntimeReviewPaths.PrDirectory(_root, repo, pr);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "broken-record.json"), "{ malformed optional review receipt");
        }

        public string SnapshotAllFilesAndGitMarkers()
        {
            var rows = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(_root, path).Replace('\\', '/'),
                    Bytes = Convert.ToHexString(File.ReadAllBytes(path)),
                })
                .OrderBy(row => row.Path, StringComparer.Ordinal)
                .Select(row => row.Path + "\0" + row.Bytes)
                .ToList();
            rows.AddRange(_trackedGitFiles.Select(path =>
            {
                var fullPath = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
                return "git-marker\0" + path + "\0" + (File.Exists(fullPath)
                    ? "present\0" + Convert.ToHexString(File.ReadAllBytes(fullPath))
                    : "absent");
            }));
            return string.Join("\n", rows.OrderBy(row => row, StringComparer.Ordinal));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class BoundedGitRunner : IGitRemoteCommandRunner
    {
        private readonly string _head;
        private readonly string _metadataOid;
        private readonly string _claimPath;
        private readonly string _historyDirectory;
        private readonly bool _failMetadataRef;

        public BoundedGitRunner(string head, string metadataOid, string executionUnit = "G855", bool failMetadataRef = false)
        {
            _head = head;
            _metadataOid = metadataOid;
            _claimPath = ClaimCommand.ClaimPath("execution-unit:" + executionUnit);
            _historyDirectory = $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(_claimPath)}";
            _failMetadataRef = failMetadataRef;
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];
        public int ForbiddenCalls { get; private set; }

        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var copy = arguments.ToArray();
            Calls.Add(copy);
            if (copy.Length == 3 && copy[0] == "rev-parse" && copy[1] == "--verify" && copy[2] == "HEAD") return Success(_head);
            if (copy.Length == 3 && copy[0] == "rev-parse" && copy[1] == "--abbrev-ref" && copy[2] == "HEAD") return Success("feature/g855");
            if (copy.Length == 3 && copy[0] == "rev-parse" && copy[1] == "--verify"
                && copy[2] == "refs/remotes/origin/metadata^{commit}")
            {
                return _failMetadataRef
                    ? new GitRemoteCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "configured metadata ref is unavailable" }
                    : Success(_metadataOid);
            }
            if (copy.Length == 8 && copy[0] == "ls-tree" && copy[1] == "-r" && copy[2] == "-z"
                && copy[3] == "--name-only" && copy[4] == _metadataOid && copy[5] == "--"
                && copy[6] == _claimPath && copy[7] == _historyDirectory)
                return Success(string.Empty);
            if (copy.Length == 2 && copy[0] == "show" && copy[1].StartsWith(_metadataOid + ":", StringComparison.Ordinal)
                && (copy[1][(_metadataOid.Length + 1)..] == _claimPath
                    || copy[1][(_metadataOid.Length + 1)..].StartsWith(_historyDirectory + "/", StringComparison.Ordinal)))
                return Success(string.Empty);

            ForbiddenCalls++;
            return new GitRemoteCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "forbidden or unbounded git request" };
        }

        private GitRemoteCommandResult Success(string output) => new() { ExitCode = 0, StdOut = output, StdErr = string.Empty };
    }

    private sealed class BoundedGitHubRunner : IGitHubCommandRunner
    {
        private readonly string _scenario;
        private readonly string _repo;
        private readonly string _head;
        private readonly int _issue;
        private readonly int _pullRequest;

        public BoundedGitHubRunner(string scenario, string repo, string head, int issue, int pullRequest)
        {
            _scenario = scenario;
            _repo = repo;
            _head = head;
            _issue = issue;
            _pullRequest = pullRequest;
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];
        public int ForbiddenCalls { get; private set; }

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            var copy = arguments.ToArray();
            Calls.Add(copy);
            if (copy.Length != 4 || copy[0] != "api" || copy[1] != "--method" || copy[2] != "GET" || !IsAllowedEndpoint(copy[3]))
            {
                ForbiddenCalls++;
                return new GitHubCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "forbidden or unbounded GitHub request" };
            }

            var endpoint = copy[3];
            if (endpoint == $"repos/{_repo}/pulls/{_pullRequest}")
                return Json($"{{\"number\":{_pullRequest},\"head\":{{\"sha\":\"{_head}\"}},\"merged\":false,\"merge_commit_sha\":null,\"labels\":[]}}");
            if (endpoint == $"repos/{_repo}/issues/{_issue}")
            {
                return _scenario == "api-503"
                    ? new GitHubCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "gh: HTTP 503 Service Unavailable" }
                    : Json($"{{\"number\":{_issue},\"labels\":[]}}");
            }
            if (endpoint == $"repos/{_repo}/pulls/{_pullRequest}/reviews?per_page=100&page=1")
            {
                return _scenario == "provenance"
                    ? Json("[{\"id\":74,\"state\":\"COMMENTED\",\"commit_id\":null,\"body\":\"Reviewer: independent subagent review\\nNotes: historical prose without unit/head identity\",\"submitted_at\":\"2026-10-07T00:00:00Z\",\"user\":{\"login\":\"reviewer\"}}]")
                    : Json("[]");
            }
            if (endpoint == $"repos/{_repo}/commits/{_head}/check-runs?per_page=100&page=1")
            {
                return _scenario == "malformed-json"
                    ? Json("{not-json")
                    : Json("{\"total_count\":0,\"check_runs\":[]}");
            }
            if (endpoint == $"repos/{_repo}/commits/{_head}/statuses?per_page=100&page=1") return Json("[]");

            ForbiddenCalls++;
            return new GitHubCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "forbidden GitHub endpoint" };
        }

        private bool IsAllowedEndpoint(string endpoint) =>
            endpoint == $"repos/{_repo}/pulls/{_pullRequest}"
            || endpoint == $"repos/{_repo}/issues/{_issue}"
            || endpoint == $"repos/{_repo}/pulls/{_pullRequest}/reviews?per_page=100&page=1"
            || endpoint == $"repos/{_repo}/commits/{_head}/check-runs?per_page=100&page=1"
            || endpoint == $"repos/{_repo}/commits/{_head}/statuses?per_page=100&page=1";

        private GitHubCommandResult Json(string json) => new() { ExitCode = 0, StdOut = json, StdErr = string.Empty };
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _original = Directory.GetCurrentDirectory();

        public CurrentDirectoryScope(string path) => Directory.SetCurrentDirectory(path);

        public void Dispose() => Directory.SetCurrentDirectory(_original);
    }

    private sealed class ConsoleScope : IDisposable
    {
        private readonly TextWriter _originalOut = Console.Out;
        private readonly TextWriter _originalError = Console.Error;

        public StringWriter Output { get; } = new();
        public StringWriter Error { get; } = new();

        public ConsoleScope()
        {
            Console.SetOut(Output);
            Console.SetError(Error);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            Output.Dispose();
            Error.Dispose();
        }
    }
}
