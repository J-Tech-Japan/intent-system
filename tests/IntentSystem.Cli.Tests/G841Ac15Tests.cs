using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>G841 AC15: TryParse consumers, downstream summaries, and out-of-scope guards.</summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G841Ac15Tests : IDisposable
{
    private const string Unit = "G841AC";
    private const string TargetRepo = G841TestHelpers.Repo;
    private const string Domain = G841TestHelpers.Domain;

    private readonly string root = Directory.CreateTempSubdirectory("g841-ac15-").FullName;

    public G841Ac15Tests()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        G841TestHelpers.WriteHostConfig(root);
        WriteBindings();
    }

    public void Dispose()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string BaseInvalidYamlError =
        "packet.yaml is not valid YAML: While parsing a node, did not find expected node content.";

    private const string BaseDuplicateKeyError =
        "packet.yaml is not valid YAML: Duplicate key domain";

    public static TheoryData<string, string> TryParseBaseErrorForms() => new()
    {
        { "   \n", "packet.yaml is empty." },
        { "just a scalar\n", "packet.yaml is empty or its top-level document is not a mapping." },
        { G841TestHelpers.UnparseableYaml, BaseInvalidYamlError },
        {
            """
            implementation_issue_packet:
              domain: intent-cli
              domain: other-domain
            """,
            BaseDuplicateKeyError
        },
    };

    [Theory]
    [MemberData(nameof(TryParseBaseErrorForms))]
    public void TryParse_ErrorStrings_StayByteIdenticalToBase_G841Ac15(string yaml, string expected)
    {
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void PreparedPacketCommitReadyAnalyzer_UnparseablePacket_SurfacesTryParseError_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        var result = AnalyzeUnparseablePacket();
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, result.Reason);
        Assert.Contains($"packet.yaml does not parse as YAML: {parseError}", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationHostLoopNextAction_UnparseablePacket_SurfacesTryParseError_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        WriteApprovedPrRoutingState(G841TestHelpers.UnparseableYaml);

        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new HostLoopLister(
            prs:
            [
                HostLoopLister.NewPr(
                    1468,
                    isDraft: false,
                    state: "OPEN",
                    labels: ["intent-target", "intent-pr-approved"],
                    closingIssue: 1467,
                    checksGreen: true),
            ],
            issues: [HostLoopLister.NewIssue(1467, ["intent-target", "intent-pr-created"], null, $"{Unit} operator merge")]);
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FixedNextSliceProbe();
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FixedRecoveryProbe();
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ =>
            new FixedSyncProbe(HostSyncPreflightAnalyzer.ClassificationClean);
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = _ => new FixedDriftProbe();

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(
            Context(),
            ["--repo", TargetRepo, "--domain", Domain, "--format", "json"],
            writer));

        using var doc = JsonDocument.Parse(writer.ToString());
        var evidence = doc.RootElement.GetProperty("evidence").EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Contains(evidence, line => line.Contains(
            $"immutable packet routing could not be parsed for {Unit}: {parseError}",
            StringComparison.Ordinal));
    }

    [Fact]
    public void BranchLaneDecisionRecord_UnparseablePacket_SurfacesTryParseError_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        G841TestHelpers.WritePacketFiles(root, Unit, G841TestHelpers.UnparseableYaml, withContractBody: false);

        var gate = BranchLaneDecisionGate.Evaluate(root, Unit);
        Assert.False(gate.Passed);
        Assert.Equal($"could not parse packet.yaml: {parseError}", gate.Error);
    }

    [Fact]
    public void BranchLaneDecisionCommand_UnparseablePacket_SurfacesTryParseError_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        G841TestHelpers.WritePacketFiles(root, Unit, G841TestHelpers.UnparseableYaml, withContractBody: false);

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "automation", "branch-lane-propose-record",
                "--execution-unit", Unit,
                "--actor", "design",
                "--rationale", "lane rationale",
                "--evidence", "packet snapshot",
                "--format", "json",
            ],
            Context(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains($"could not parse packet.yaml: {parseError}", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationQueueDependencyReconcile_UnparseablePacket_SurfacesTryParseError_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        WriteQueueState();
        G841TestHelpers.WritePacketFiles(root, Unit, G841TestHelpers.UnparseableYaml, withContractBody: false);

        using var writer = new StringWriter();
        var exitCode = AutomationQueueDependencyReconcileCommand.Execute(
            Context(),
            ["--execution-unit", Unit, "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var detail = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("detail")
            .GetString()!;
        Assert.Contains($"does not parse: {parseError}", detail, StringComparison.Ordinal);
        Assert.Equal(
            AutomationQueueDependencyReconcileCommand.StatusPacketUnparseable,
            Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public void ProjectionGenerate_UnparseablePacket_SurfacesTryParseError_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        var packetDir = Path.Combine(root, ".intent-cli", "issues", Unit);
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), G841TestHelpers.UnparseableYaml);

        using var writer = new StringWriter();
        var exitCode = ProjectionGenerateCommand.Regenerate(Context(), [Unit], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet YAML could not be parsed", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains(parseError["packet.yaml is not valid YAML: ".Length..], writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DownstreamSummaries_UnparseablePacket_StayByteIdentical_G841Ac15()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var parseError));
        var packetDirectory = $".intent-cli/issues/{Unit}/";
        var analyzerSummary =
            $"prepared packet `{packetDirectory}` is not publishable; packet.yaml does not parse as YAML: {parseError}.";

        var analyzerResult = AnalyzeUnparseablePacket();
        Assert.Equal(analyzerSummary, analyzerResult.Summary);

        G841TestHelpers.WritePacketFiles(root, Unit, G841TestHelpers.UnparseableYaml);
        using (var seedWriter = new StringWriter())
        {
            var seedExit = AutomationQueueSeedFromPacketCommand.Execute(
                Context(),
                [
                    "--execution-unit", Unit,
                    "--domain", Domain,
                    "--target-repo", TargetRepo,
                    "--format", "json",
                ],
                seedWriter);
            Assert.Equal(1, seedExit);
            using var seedDoc = JsonDocument.Parse(seedWriter.ToString());
            Assert.Equal(
                $"refusing to seed queue-state from `{packetDirectory}`: {analyzerSummary}",
                seedDoc.RootElement.GetProperty("summary").GetString());
        }

        using var gitWorkspace = new DurableStateGitFixture();
        gitWorkspace.WriteBindings();
        gitWorkspace.WritePreparedPacket(Unit, TargetRepo, G841TestHelpers.UnparseableYaml);
        using var preflightWriter = new StringWriter();
        var preflightExit = AutomationDurableStatePreflightCommand.Execute(
            gitWorkspace.Context,
            ["--domain", Domain, "--target-repo", TargetRepo, "--format", "json"],
            preflightWriter);
        Assert.Equal(1, preflightExit);
        using var preflightDoc = JsonDocument.Parse(preflightWriter.ToString());
        Assert.Equal(analyzerSummary, PacketUnsafeReason(preflightDoc));
    }

    [Fact]
    public void PreparedPacketCommitReadyInput_KeepsSignature_G841Ac15()
    {
        var analyze = typeof(PreparedPacketCommitReadyAnalyzer).GetMethod(
            nameof(PreparedPacketCommitReadyAnalyzer.Analyze),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(analyze);
        Assert.Equal(typeof(PreparedPacketCommitReadyResult), analyze!.ReturnType);
        Assert.Equal(
            [typeof(PreparedPacketCommitReadyInput)],
            analyze.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        var members = typeof(PreparedPacketCommitReadyInput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, property.PropertyType, property.GetCustomAttribute<RequiredMemberAttribute>() is not null))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                ("ExecutionUnit", typeof(string), true),
                ("ExecutionUnitRegex", typeof(string), false),
                ("GithubBodyMarkdown", typeof(string), false),
                ("ImplementationMarkdown", typeof(string), false),
                ("PacketYaml", typeof(string), false),
                ("RequestedTargetRepo", typeof(string), false),
                ("RequireDomainBinding", typeof(bool), false),
                ("ReviewContextMarkdown", typeof(string), false),
            ],
            members);
    }

    [Theory]
    [InlineData("parseable")]
    [InlineData("unparseable")]
    [InlineData("unreadable")]
    public void QueueSeedAndDurablePreflight_AgreeAcrossPacketStates_G841Ac15(string state)
    {
        switch (state)
        {
            case "parseable":
                G841TestHelpers.WritePacketFiles(root, Unit, PublishablePacketYaml());
                break;
            case "unparseable":
                G841TestHelpers.WritePacketFiles(root, Unit, G841TestHelpers.UnparseableYaml);
                break;
            case "unreadable":
                G841TestHelpers.WritePacketFiles(root, Unit, PublishablePacketYaml());
                var packetPath = G841TestHelpers.PacketPath(root, Unit);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(packetPath, UnixFileMode.None);
                }

                if (G841TestHelpers.CanRead(packetPath))
                {
                    Assert.True(
                        true,
                        "skipped unreadable fixture under root: queue-seed and durable-state-preflight do not use PacketFileReader seam");
                    return;
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown state: {state}");
        }

        using var seedWriter = new StringWriter();
        var seedExit = AutomationQueueSeedFromPacketCommand.Execute(
            Context(),
            [
                "--execution-unit", Unit,
                "--domain", Domain,
                "--target-repo", TargetRepo,
                "--format", "json",
            ],
            seedWriter);
        using var seedDoc = JsonDocument.Parse(seedWriter.ToString());
        var seedClassification = seedDoc.RootElement.GetProperty("classification").GetString();
        var seedUnsafeReason = seedDoc.RootElement.TryGetProperty("unsafe_reason", out var unsafeReasonNode)
            ? unsafeReasonNode.GetString()
            : null;
        var seedSummary = seedDoc.RootElement.GetProperty("summary").GetString();
        var seedPublishable = seedDoc.RootElement.GetProperty("contract_publishable").GetBoolean();

        using var gitWorkspace = new DurableStateGitFixture();
        var packetYaml = state switch
        {
            "parseable" => PublishablePacketYaml(),
            "unparseable" => G841TestHelpers.UnparseableYaml,
            "unreadable" => PublishablePacketYaml(),
            _ => throw new InvalidOperationException(),
        };
        gitWorkspace.WriteBindings();
        gitWorkspace.WritePreparedPacket(Unit, TargetRepo, packetYaml);
        if (state == "unreadable")
        {
            var gitPacketPath = G841TestHelpers.PacketPath(gitWorkspace.RepoRoot, Unit);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(gitPacketPath, UnixFileMode.None);
            }

            if (G841TestHelpers.CanRead(gitPacketPath))
            {
                return;
            }
        }

        using var preflightWriter = new StringWriter();
        var preflightExit = AutomationDurableStatePreflightCommand.Execute(
            gitWorkspace.Context,
            ["--domain", Domain, "--target-repo", TargetRepo, "--format", "json"],
            preflightWriter);
        using var preflightDoc = JsonDocument.Parse(preflightWriter.ToString());
        var preflightClassification = preflightDoc.RootElement.GetProperty("classification").GetString();

        switch (state)
        {
            case "parseable":
                Assert.Equal(0, seedExit);
                Assert.Equal(0, preflightExit);
                Assert.Equal(AutomationQueueSeedFromPacketCommand.ClassificationReady, seedClassification);
                Assert.Equal(DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady, preflightClassification);
                Assert.True(seedPublishable);
                break;
            case "unparseable":
                Assert.Equal(1, seedExit);
                Assert.Equal(1, preflightExit);
                Assert.Equal(AutomationQueueSeedFromPacketCommand.ClassificationUnsafe, seedClassification);
                Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, seedUnsafeReason);
                Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, preflightClassification);
                Assert.False(seedPublishable);
                Assert.Contains("packet.yaml does not parse as YAML:", seedSummary, StringComparison.Ordinal);
                Assert.Contains(
                    "packet.yaml does not parse as YAML:",
                    PacketUnsafeReason(preflightDoc),
                    StringComparison.Ordinal);
                break;
            case "unreadable":
                Assert.Equal(1, seedExit);
                Assert.Equal(1, preflightExit);
                Assert.Equal(AutomationQueueSeedFromPacketCommand.ClassificationUnsafe, seedClassification);
                Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile, seedUnsafeReason);
                Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, preflightClassification);
                Assert.False(seedPublishable);
                break;
        }
    }

    [Fact]
    public void IssuePublishFlow_TitlePathsDoNotDiscardTryParseError_G841Ac15()
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "src/IntentSystem.Cli/Commands/IssuePublishFlowCommand.cs");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("PacketYamlDocument.TryParse(", text, StringComparison.Ordinal);
        Assert.Contains("PacketYamlDocument.TryParseWithLocation", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GuideReviewCommand.cs", "PacketYamlDocument.TryParse(packetYaml, out var packetDocument, out _)")]
    [InlineData("AutomationQueueSeedFromPacketCommand.cs", "PacketYamlDocument.TryParse(packetYaml, out packetDocument, out _)")]
    [InlineData("AutomationStalledWorkCommand.cs", "PacketYamlDocument.TryParse(File.ReadAllText(path), out var document, out _)")]
    [InlineData("BranchLaneRouting.cs", "PacketYamlDocument.TryParse(yaml, out var document, out _)")]
    public void OutOfScopeDiscardSites_StayByteIdentical_G841Ac15(string relativePath, string expectedSnippet)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "src/IntentSystem.Cli/Commands", relativePath);
        var text = File.ReadAllText(path);
        Assert.Contains(expectedSnippet, text, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchLaneRouting_UnparseablePacket_StillReturnsNull_G841Ac15()
    {
        Assert.Null(BranchLaneResolver.TryReadSnapshot(G841TestHelpers.UnparseableYaml));
    }

    private static string PublishablePacketYaml() =>
        $"""
        implementation_issue_packet:
          source_execution_unit: {Unit}
          issue_title: "G841 fixture title"
          domain: {Domain}
          target_repo: {TargetRepo}
        """;

    private static string PacketUnsafeReason(JsonDocument preflightDoc) =>
        preflightDoc.RootElement.GetProperty("unsafe_paths").EnumerateArray()
            .First(item => item.GetProperty("path").GetString() == $".intent-cli/issues/{Unit}/packet.yaml")
            .GetProperty("reason")
            .GetString()!;

    private static string ExpectedInvalidYamlError()
    {
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnparseableYaml, out _, out var error));
        return error;
    }

    private static string ExpectedDuplicateKeyError()
    {
        const string yaml = """
            implementation_issue_packet:
              domain: intent-cli
              domain: other-domain
            """;
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var error));
        return error;
    }

    private PreparedPacketCommitReadyResult AnalyzeUnparseablePacket() =>
        PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = Unit,
            PacketYaml = G841TestHelpers.UnparseableYaml,
            ImplementationMarkdown = "# impl",
            ReviewContextMarkdown = "# review",
            GithubBodyMarkdown = G841TestHelpers.MinimalContractBody(),
            ExecutionUnitRegex = "^G841AC$",
            RequestedTargetRepo = TargetRepo,
            RequireDomainBinding = true,
        });

    private CliContext Context() => G841TestHelpers.GatedContext(root);

    private void WriteBindings()
    {
        var dir = Path.Combine(root, "intents", Domain, "automation");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "bindings.md"),
            $"---\nexecution_unit_regex: '^{Unit}$'\n---\n");
    }

    private void WriteQueueState()
    {
        var queuePath = Path.Combine(root, ".intent-cli", "queue-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
        var queue = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = [CreateQueueItem(QueueItemState.Queued)],
        };
        File.WriteAllText(queuePath, QueueStateSerializer.Serialize(queue));
    }

    private void WriteApprovedPrRoutingState(string packetYaml)
    {
        var queuePath = Path.Combine(root, ".intent-cli", "queue-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
        var queue = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.UtcNow,
            Items =
            [
                CreateQueueItem(
                    QueueItemState.Review,
                    routingSnapshot: new QueueRoutingSnapshot
                    {
                        LaneId = "main-hotfix",
                        DefinitionRevision = "registry-g841ac",
                        StartBranch = "main",
                        PrBaseBranch = "main",
                        LandingMode = BranchLaneLandingModes.Direct,
                    },
                    linkedIssue: new LinkedIssue
                    {
                        Repo = TargetRepo,
                        Number = 1467,
                        Url = $"https://github.com/{TargetRepo}/issues/1467",
                    },
                    linkedPr: "1468"),
            ],
        };
        File.WriteAllText(queuePath, QueueStateSerializer.Serialize(queue));

        var packetDir = Path.Combine(root, ".intent-cli", "issues", Unit);
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), packetYaml);
        File.WriteAllText(Path.Combine(packetDir, "implementation.md"), "# impl\n");
        File.WriteAllText(Path.Combine(packetDir, "review-context.md"), "# review\n");
    }

    private static QueueItem CreateQueueItem(
        QueueItemState state,
        QueueRoutingSnapshot? routingSnapshot = null,
        LinkedIssue? linkedIssue = null,
        string? linkedPr = null) =>
        new()
        {
            ExecutionUnit = Unit,
            Title = $"{Unit} operator merge",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Yaml = $".intent-cli/issues/{Unit}/packet.yaml",
                Implementation = $".intent-cli/issues/{Unit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{Unit}/review-context.md",
            },
            RoutingSnapshot = routingSnapshot,
            LinkedIssue = linkedIssue,
            LinkedPr = linkedPr,
            WorkerRole = "implementation",
            ReviewRole = "review",
            Priority = "normal",
        };

    private sealed class DurableStateGitFixture : IDisposable
    {
        public DurableStateGitFixture()
        {
            RepoRoot = Directory.CreateTempSubdirectory("g841-ac15-git-").FullName;
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
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
            RunGit("init -q");
            RunGit("config user.email test@example.com");
            RunGit("config user.name test");
            File.WriteAllText(Path.Combine(RepoRoot, "README.md"), "# seed\n");
            RunGit("add README.md");
            RunGit("commit -q -m seed");
            Directory.CreateDirectory(Path.Combine(RepoRoot, ".intent-cli"));
        }

        public string RepoRoot { get; }

        public CliContext Context { get; }

        public void WriteBindings()
        {
            var dir = Path.Combine(RepoRoot, "intents", Domain, "automation");
            Directory.CreateDirectory(dir);
            var relativePath = $"intents/{Domain}/automation/bindings.md";
            File.WriteAllText(
                Path.Combine(dir, "bindings.md"),
                $"---\nexecution_unit_regex: '^{Unit}$'\n---\n");
            RunGit($"add {relativePath}");
            RunGit("commit -q -m bindings");
        }

        public void WritePreparedPacket(string executionUnit, string targetRepo, string packetYaml)
        {
            var dir = Path.Combine(RepoRoot, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), packetYaml);
            File.WriteAllText(Path.Combine(dir, "implementation.md"), "# impl\n");
            File.WriteAllText(Path.Combine(dir, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(dir, "github-body.md"), G841TestHelpers.MinimalContractBody());
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }

        private void RunGit(string arguments)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = RepoRoot;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
        }
    }

    private sealed class FixedNextSliceProbe : INextSliceDryRunProbe
    {
        public NextSliceProbeResult? Probe(string repo, string domain) =>
            new() { RecommendedOutcome = "no-actionable-item" };
    }

    private sealed class FixedRecoveryProbe : IPublishRecoveryProbe
    {
        public PublishRecoveryProbeResult? Probe(string repo) =>
            new() { SafeRepairCount = 0, UnsafeStopCount = 0 };
    }

    private sealed class FixedSyncProbe : IHostSyncPreflightProbe
    {
        private readonly string classification;

        public FixedSyncProbe(string classification) => this.classification = classification;

        public HostSyncPreflightProbeResult? Probe() => new() { Classification = classification };
    }

    private sealed class FixedDriftProbe : ICloseoutDriftCheckProbe
    {
        public CloseoutDriftCheckProbeResult? Probe(string repo) =>
            new() { SafeRepairCount = 0, UnsafeStopCount = 0 };
    }

    private sealed class HostLoopLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;

        public HostLoopLister(
            IReadOnlyList<GitHubAutomationPrCandidate> prs,
            IReadOnlyList<GitHubAutomationIssueCandidate> issues)
        {
            this.prs = prs;
            this.issues = issues;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;

        public static GitHubAutomationPrCandidate NewPr(
            int number,
            bool isDraft,
            string state,
            IReadOnlyList<string> labels,
            int? closingIssue = null,
            bool checksGreen = false) =>
            new()
            {
                Number = number,
                Title = $"PR {number}",
                Url = $"https://github.com/{TargetRepo}/pull/{number}",
                Body = "stub",
                CreatedAt = "2026-05-10T00:00:00Z",
                UpdatedAt = "2026-05-10T00:00:00Z",
                Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
                ClosingIssuesReferences = closingIssue is int issueNumber
                    ? [new GitHubPrClosingIssueReference { Number = issueNumber }]
                    : [],
                State = state,
                IsDraft = isDraft,
                HeadRefOid = checksGreen ? "head" : string.Empty,
                StatusCheckRollup = checksGreen
                    ?
                    [
                        new GitHubAutomationStatusCheckCandidate
                        {
                            TypeName = "CheckRun",
                            Status = "COMPLETED",
                            Conclusion = "SUCCESS",
                        },
                    ]
                    : [],
            };

        public static GitHubAutomationIssueCandidate NewIssue(
            int number,
            IReadOnlyList<string> labels,
            string? body,
            string title) =>
            new()
            {
                Number = number,
                Title = title,
                Url = $"https://github.com/{TargetRepo}/issues/{number}",
                CreatedAt = "2026-05-10T00:00:00Z",
                Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
                State = "OPEN",
                Body = body ?? string.Empty,
            };
    }
}
