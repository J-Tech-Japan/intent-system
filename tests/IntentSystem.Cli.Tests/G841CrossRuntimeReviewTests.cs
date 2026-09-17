using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G841 AC8/AC9: cross-runtime review packet parse/read refusals and team-resolution
/// refusal literals on record, status, and pr-transition surfaces.
/// </summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G841CrossRuntimeReviewTests : IDisposable
{
    private const string Domain = G841TestHelpers.Domain;
    private const string Team = G841TestHelpers.Team;
    private const string Repo = G841TestHelpers.Repo;
    private const string Unit = "G841";
    private const int Pr = 1823;
    private const string Head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string BaseTeamUnresolvedCause = G841TestHelpers.BaseTeamUnresolvedCause;
    private const string BaseQueueLinkageMissing = G841TestHelpers.BaseQueueLinkageMissing;
    private const string BasePacketDomainMissing = G841TestHelpers.BasePacketDomainMissing;
    private const string BaseClaimTeamMissing = G841TestHelpers.BaseClaimTeamMissing;

    private const string BaseClaimTeamDetail =
        "execution unit 'G841' has no held claim with a team (claim status 'unheld': claim verification refused scope 'execution-unit:G841': holder is none (unheld); acquire the scope before starting work. canonical scope unheld).";

    private const string BaseClaimTeamFix =
        "acquire the execution-unit claim with a team: `intent-cli claim acquire --scope execution-unit:G841 --actor <actor> --team <team> --reason <text> --write`.";

    private const string BasePacketDomainDetail =
        "packet '.intent-cli/issues/G841/packet.yaml' is missing or declares no `implementation_issue_packet.domain`.";

    private const string BasePacketDomainFixImplementation =
        "author the packet with `intent-cli packet draft --execution-unit G841 --domain <domain> --target-repo J-Tech-Japan/intent-system` so it declares `implementation_issue_packet.domain`.";

    private const string BasePacketDomainFixDesign =
        "author the packet with `intent-cli packet draft --execution-unit G841 --domain <domain> --target-repo <owner/repo>`.";

    private const string BaseExecutionUnitInvalidUnit = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

    private const string BaseExecutionUnitDetail =
        $"execution unit '{BaseExecutionUnitInvalidUnit}' exceeds 128 characters.";

    private const string BaseExecutionUnitFixImplementation = "repair the queue item's execution unit id.";

    private const string BaseExecutionUnitFixDesign = "pass the canonical execution unit id.";

    private const string BaseQueueLinkageFix = "run from the host root that owns `.intent-cli/queue-state.json`.";

    private readonly string root = Directory.CreateTempSubdirectory("g841-host-").FullName;
    private readonly string verdictFile;

    public G841CrossRuntimeReviewTests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return new ClaimOwnershipVerification(
                false,
                ClaimOwnershipVerification.StatusTeamRequired,
                scope,
                true,
                null,
                "design",
                Team,
                "held");
        };
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.PrHeadReader = null;
        G841TestHelpers.WriteHostConfig(root);
        ConfigureClaimsStore();
        WriteQueue((Unit, $"https://github.com/{Repo}/pull/{Pr}"));
        WriteValidPacket();
        WriteImplementationClaim();
        verdictFile = Path.Combine(root, "verdict.json");
        File.WriteAllText(verdictFile, "{}");
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.PrHeadReader = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── existing AC8 design status packet probes (preserved) ─────────────

    [Fact]
    public void Record_Design_UnparseablePacket_RefusesPacketInvalidWithMissing()
    {
        WritePacketYaml(G841TestHelpers.UnparseableYaml);
        var (exit, output) = Route(DesignStatusArgs());
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        G841TestHelpers.AssertCrossRuntimeParseRefusal(
            json.RootElement,
            ExpectedUnparseableDetail());
    }

    [Fact]
    public void Record_Design_QuotedHashPacket_DoesNotRefusePacketInvalid()
    {
        WritePacketYaml(
            """
            implementation_issue_packet:
              issue_title: "G841 title"
              domain: intent-cli
              target_repo: J-Tech-Japan/intent-system
              source_artifact: "review of PR #1823"
            """);
        var (exit, output) = Route(DesignStatusArgs());
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        if (json.RootElement.TryGetProperty("cause", out var cause))
        {
            Assert.NotEqual(CrossRuntimeReviewCauses.PacketInvalid, cause.GetString());
        }
    }

    [Fact]
    public void Record_Design_UnreadablePacket_RefusesPacketUnreadable()
    {
        AssertUnreadableRefusal(DesignStatusArgs(), null, out _);
    }

    // ── AC8: five surfaces × packet refusal scenarios ────────────────────

    public enum Ac8Surface
    {
        RecordImplementation,
        StatusImplementation,
        PrTransition,
        RecordDesign,
        StatusDesign,
    }

    [Theory]
    [InlineData(Ac8Surface.RecordImplementation)]
    [InlineData(Ac8Surface.StatusImplementation)]
    [InlineData(Ac8Surface.PrTransition)]
    [InlineData(Ac8Surface.RecordDesign)]
    [InlineData(Ac8Surface.StatusDesign)]
    public void Ac8_UnparseablePacket_RefusesPacketInvalid(Ac8Surface surface)
    {
        ResetHappyPath();
        WritePacketYaml(G841TestHelpers.UnparseableYaml);
        var (exit, output, mutator) = RouteSurface(surface);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        AssertSurfacePacketInvalid(json.RootElement, surface, mutator);
    }

    [Theory]
    [InlineData(Ac8Surface.RecordImplementation)]
    [InlineData(Ac8Surface.StatusImplementation)]
    [InlineData(Ac8Surface.PrTransition)]
    [InlineData(Ac8Surface.RecordDesign)]
    [InlineData(Ac8Surface.StatusDesign)]
    public void Ac8_UnreadablePacket_RefusesPacketUnreadable(Ac8Surface surface)
    {
        ResetHappyPath();
        AssertUnreadableRefusal(SurfaceArgs(surface), surface, out _);
    }

    [Theory]
    [InlineData(Ac8Surface.RecordImplementation)]
    [InlineData(Ac8Surface.StatusImplementation)]
    [InlineData(Ac8Surface.PrTransition)]
    [InlineData(Ac8Surface.RecordDesign)]
    [InlineData(Ac8Surface.StatusDesign)]
    public void Ac8_QuotedHashPacket_DoesNotRefusePacketInvalid(Ac8Surface surface)
    {
        ResetHappyPath();
        WritePacketYaml(
            """
            implementation_issue_packet:
              issue_title: "G841 title"
              domain: intent-cli
              target_repo: J-Tech-Japan/intent-system
              source_artifact: "review of PR #1823"
            """);
        var (exit, output, _) = RouteSurface(surface);
        using var json = JsonDocument.Parse(output);
        if (json.RootElement.TryGetProperty("cause", out var cause))
        {
            Assert.NotEqual(CrossRuntimeReviewCauses.PacketInvalid, cause.GetString());
        }

        if (json.RootElement.TryGetProperty("cross_runtime_review", out var gate)
            && gate.TryGetProperty("cause", out var gateCause))
        {
            Assert.NotEqual(CrossRuntimeReviewCauses.PacketInvalid, gateCause.GetString());
        }

        if (surface is Ac8Surface.StatusImplementation or Ac8Surface.StatusDesign)
        {
            Assert.Equal(0, exit);
        }
        else
        {
            Assert.Equal(1, exit);
        }
    }

    // ── AC9: status refusal literals (four branches × two kinds) ─────────

    [Fact]
    public void Status_Implementation_Ac9_QueueLinkage_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        File.Delete(Path.Combine(root, ".intent-cli", "queue-state.json"));
        var expectedDetail =
            $"host queue-state '{Path.Combine(root, ".intent-cli", "queue-state.json")}' does not exist, so PR #{Pr} cannot be resolved to an execution unit.";
        var (exit, output) = Route(ImplementationStatusArgs());
        Assert.Equal(1, exit);
        AssertAc9TeamUnresolved(output, expectedDetail, BaseQueueLinkageFix, BaseQueueLinkageMissing);
    }

    [Fact]
    public void Status_Implementation_Ac9_PacketDomain_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        WritePacketYaml(
            $"""
            implementation_issue_packet:
              issue_title: "G841 title"
              target_repo: {Repo}
            """);
        var (exit, output) = Route(ImplementationStatusArgs());
        Assert.Equal(1, exit);
        AssertAc9TeamUnresolved(
            output,
            BasePacketDomainDetail,
            BasePacketDomainFixImplementation,
            BasePacketDomainMissing);
    }

    [Fact]
    public void Status_Implementation_Ac9_ClaimTeam_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        RemoveClaim();
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        var (exit, output) = Route(ImplementationStatusArgs());
        Assert.Equal(1, exit);
        AssertAc9TeamUnresolved(output, BaseClaimTeamDetail, BaseClaimTeamFix, BaseClaimTeamMissing);
    }

    [Fact]
    public void Status_Implementation_Ac9_ExecutionUnit_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        WriteQueue((BaseExecutionUnitInvalidUnit, $"https://github.com/{Repo}/pull/{Pr}"));
        var resolution = CrossRuntimeReviewTeamResolver.Resolve(
            root,
            Repo,
            Pr,
            executionUnitArgument: null,
            "pass the unit the host queue links to this PR with --execution-unit");
        Assert.False(resolution.Resolved);
        Assert.Equal(BaseTeamUnresolvedCause, resolution.Cause);
        Assert.Equal("execution-unit", resolution.Missing);
        Assert.Equal(BaseExecutionUnitDetail, resolution.Detail);
        Assert.Equal(BaseExecutionUnitFixImplementation, resolution.Fix);
    }

    [Fact]
    public void Status_Design_Ac9_QueueLinkage_IgnoresMissingQueueState()
    {
        ResetHappyPath();
        File.Delete(Path.Combine(root, ".intent-cli", "queue-state.json"));
        var (exit, output) = Route(DesignStatusArgs());
        Assert.Equal(0, exit);
        Assert.DoesNotContain(BaseQueueLinkageMissing, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_Design_Ac9_PacketDomain_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        WritePacketYaml(
            $"""
            implementation_issue_packet:
              issue_title: "G841 title"
              target_repo: {Repo}
            """);
        var (exit, output) = Route(DesignStatusArgs());
        Assert.Equal(1, exit);
        AssertAc9TeamUnresolved(output, BasePacketDomainDetail, BasePacketDomainFixDesign, BasePacketDomainMissing);
    }

    [Fact]
    public void Status_Design_Ac9_ClaimTeam_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        RemoveClaim();
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        var (exit, output) = Route(DesignStatusArgs());
        Assert.Equal(1, exit);
        AssertAc9TeamUnresolved(output, BaseClaimTeamDetail, BaseClaimTeamFix, BaseClaimTeamMissing);
    }

    [Fact]
    public void Status_Design_Ac9_ExecutionUnit_RefusalLiteralsMatchBaseCapture()
    {
        var resolution = CrossRuntimeReviewPacketClaimResolver.Resolve(root, "..");
        Assert.False(resolution.Resolved);
        Assert.Equal(BaseTeamUnresolvedCause, resolution.Cause);
        Assert.Equal("execution-unit", resolution.Missing);
        Assert.Equal(
            "execution unit '..' starts with '.', which is not a canonical identifier (a leading dot introduces a relative path segment).",
            resolution.Detail);
        Assert.Equal(BaseExecutionUnitFixDesign, resolution.Fix);

        var (exit, output) = Route(["review", "cross-runtime", "status", "--kind", "design", "--execution-unit", "..", "--format", "json"]);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ArgumentInvalid, json.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "--execution-unit is invalid: execution unit '..' starts with '.', which is not a canonical identifier (a leading dot introduces a relative path segment).",
            json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(BaseExecutionUnitFixDesign, json.RootElement.GetProperty("fix").GetString());
    }

    // ── AC9: pr-transition refusal literals (four branches) ──────────────

    [Fact]
    public void PrTransition_Ac9_QueueLinkage_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        File.Delete(Path.Combine(root, ".intent-cli", "queue-state.json"));
        var expectedDetail =
            $"host queue-state '{Path.Combine(root, ".intent-cli", "queue-state.json")}' does not exist, so PR #{Pr} cannot be resolved to an execution unit. Fix: {BaseQueueLinkageFix}";
        AssertPrTransitionTeamUnresolved(expectedDetail, BaseTeamUnresolvedCause);
    }

    [Fact]
    public void PrTransition_Ac9_PacketDomain_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        WritePacketYaml(
            $"""
            implementation_issue_packet:
              issue_title: "G841 title"
              target_repo: {Repo}
            """);
        var expectedDetail = $"{BasePacketDomainDetail} Fix: {BasePacketDomainFixImplementation}";
        AssertPrTransitionTeamUnresolved(expectedDetail, BaseTeamUnresolvedCause);
    }

    [Fact]
    public void PrTransition_Ac9_ClaimTeam_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        RemoveClaim();
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        var expectedDetail = $"{BaseClaimTeamDetail} Fix: {BaseClaimTeamFix}";
        AssertPrTransitionTeamUnresolved(expectedDetail, BaseTeamUnresolvedCause);
    }

    [Fact]
    public void PrTransition_Ac9_ExecutionUnit_RefusalLiteralsMatchBaseCapture()
    {
        ResetHappyPath();
        WriteQueue((BaseExecutionUnitInvalidUnit, $"https://github.com/{Repo}/pull/{Pr}"));
        var expectedDetail = $"{BaseExecutionUnitDetail} Fix: {BaseExecutionUnitFixImplementation}";
        AssertPrTransitionTeamUnresolved(expectedDetail, BaseTeamUnresolvedCause);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private void AssertAc9TeamUnresolved(string output, string expectedDetail, string expectedFix, string expectedMissing)
    {
        using var json = JsonDocument.Parse(output);
        var rootElement = json.RootElement;
        Assert.Equal(BaseTeamUnresolvedCause, rootElement.GetProperty("cause").GetString());
        Assert.Equal(expectedDetail, rootElement.GetProperty("detail").GetString());
        Assert.Equal(expectedFix, rootElement.GetProperty("fix").GetString());
        var resolution = rootElement.GetProperty("resolution");
        Assert.Equal(BaseTeamUnresolvedCause, resolution.GetProperty("cause").GetString());
        Assert.Equal(expectedMissing, resolution.GetProperty("missing").GetString());
        Assert.Equal(expectedDetail, resolution.GetProperty("detail").GetString());
        Assert.Equal(expectedFix, resolution.GetProperty("fix").GetString());
    }

    private void AssertPrTransitionTeamUnresolved(string expectedDetail, string expectedCause)
    {
        AutomationPrTransitionCommand.PrHeadReader = (_, _) => Head;
        var mutator = new RecordingMutator { Labels = ["intent-pr-reviewing"] };
        var (exit, output) = RunTransition(Context(), "approved", write: false, mutator);
        Assert.Equal(1, exit);
        Assert.Empty(mutator.Applied);
        using var json = JsonDocument.Parse(output);
        Assert.False(json.RootElement.GetProperty("applied").GetBoolean());
        var gate = json.RootElement.GetProperty("cross_runtime_review");
        Assert.Equal(expectedCause, gate.GetProperty("cause").GetString());
        Assert.Equal(expectedDetail, gate.GetProperty("detail").GetString());
        Assert.Contains(" Fix: ", gate.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.StartsWith(expectedCause, json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    private void AssertUnreadableRefusal(string[] args, Ac8Surface? surface, out RecordingMutator? mutator)
    {
        mutator = null;
        var packetPath = PacketPath();
        using var unreadable = G841TestHelpers.UnreadablePacket(
            packetPath,
            ArmDeniedPacketReader,
            DisarmDeniedPacketReader,
            out _);
        int exit;
        string output;
        if (surface == Ac8Surface.PrTransition)
        {
            (exit, output, mutator) = RunPrTransitionRoute(out _);
        }
        else
        {
            (exit, output) = Route(args);
        }
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        if (surface == Ac8Surface.PrTransition)
        {
            var gate = json.RootElement.GetProperty("cross_runtime_review");
            Assert.Equal(CrossRuntimeReviewCauses.PacketUnreadable, gate.GetProperty("cause").GetString());
            Assert.DoesNotContain("packet-invalid", output, StringComparison.Ordinal);
            var detail = gate.GetProperty("detail").GetString()!;
            Assert.Contains(PacketRelativePath(), detail, StringComparison.Ordinal);
            Assert.Contains("could not be read:", detail, StringComparison.Ordinal);
            Assert.EndsWith($"Fix: make `{PacketRelativePath()}` readable, then re-run.", detail, StringComparison.Ordinal);
            Assert.False(json.RootElement.GetProperty("applied").GetBoolean());
            Assert.StartsWith(CrossRuntimeReviewCauses.PacketUnreadable, json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
            Assert.Empty(mutator!.Applied);
        }
        else
        {
            G841TestHelpers.AssertCrossRuntimeReadRefusal(json.RootElement, PacketRelativePath());
        }
    }

    private static void AssertSurfacePacketInvalid(JsonElement json, Ac8Surface surface, RecordingMutator? mutator)
    {
        var expectedDetail = ExpectedUnparseableDetail();
        if (surface == Ac8Surface.PrTransition)
        {
            var gate = json.GetProperty("cross_runtime_review");
            Assert.Equal(CrossRuntimeReviewCauses.PacketInvalid, gate.GetProperty("cause").GetString());
            Assert.DoesNotContain("packet-unreadable", json.GetRawText(), StringComparison.Ordinal);
            Assert.Equal(
                $"{expectedDetail} Fix: repair `{PacketRelativePath()}` so the whole document parses as YAML.",
                gate.GetProperty("detail").GetString());
            Assert.False(json.GetProperty("applied").GetBoolean());
            Assert.StartsWith($"{CrossRuntimeReviewCauses.PacketInvalid}: {expectedDetail}", json.GetProperty("error").GetString(), StringComparison.Ordinal);
            Assert.Empty(mutator!.Applied);
            return;
        }

        G841TestHelpers.AssertCrossRuntimeParseRefusal(json, expectedDetail, PacketRelativePath());
    }

    private static string ExpectedUnparseableDetail() =>
        G841TestHelpers.ExpectedCrossRuntimeParseDetail(PacketRelativePath(), G841TestHelpers.UnparseableYaml);

    private (int ExitCode, string Output, RecordingMutator? Mutator) RouteSurface(Ac8Surface surface)
    {
        if (surface == Ac8Surface.PrTransition)
        {
            return RunPrTransitionRoute(out _);
        }

        var (routeExit, routeOutput) = Route(SurfaceArgs(surface));
        return (routeExit, routeOutput, null);
    }

    private (int ExitCode, string Output, RecordingMutator Mutator) RunPrTransitionRoute(out RecordingMutator mutator)
    {
        mutator = new RecordingMutator { Labels = ["intent-pr-reviewing"] };
        AutomationPrTransitionCommand.PrHeadReader = (_, _) => Head;
        var (exit, output) = RunTransition(Context(), "approved", write: false, mutator);
        return (exit, output, mutator);
    }

    private string[] SurfaceArgs(Ac8Surface surface) => surface switch
    {
        Ac8Surface.RecordImplementation => ImplementationRecordArgs(),
        Ac8Surface.StatusImplementation => ImplementationStatusArgs(),
        Ac8Surface.RecordDesign => DesignRecordArgs(),
        Ac8Surface.StatusDesign => DesignStatusArgs(),
        Ac8Surface.PrTransition => ["automation", "pr-transition", "--repo", Repo, "--pr", Pr.ToString(), "--transition", "approved", "--head-sha", Head, "--format", "json"],
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    private (int ExitCode, string Output) Route(string[] args)
    {
        using var routeWriter = new StringWriter();
        var routeExit = CommandRouter.Execute(args, Context(), routeWriter);
        return (routeExit, routeWriter.ToString());
    }

    private (int ExitCode, string Output) RunTransition(CliContext context, string transition, bool write, RecordingMutator mutator)
    {
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;
        var args = new List<string>
        {
            "--repo", Repo,
            "--pr", Pr.ToString(),
            "--transition", transition,
            "--head-sha", Head,
            "--format", "json",
        };
        if (write)
        {
            args.Add("--write");
        }

        using var writer = new StringWriter();
        var exit = AutomationPrTransitionCommand.Execute(context, args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private CliContext Context() => G841TestHelpers.GatedContext(root);

    private void ResetHappyPath()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return new ClaimOwnershipVerification(
                false,
                ClaimOwnershipVerification.StatusTeamRequired,
                scope,
                true,
                null,
                "design",
                Team,
                "held");
        };
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        WriteQueue((Unit, $"https://github.com/{Repo}/pull/{Pr}"));
        WriteValidPacket();
        WriteImplementationClaim();
        ConfigureClaimsStore();
    }

    private void ConfigureClaimsStore() =>
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));

    private string[] ImplementationStatusArgs() =>
        ["review", "cross-runtime", "status", "--repo", Repo, "--pr", Pr.ToString(), "--head-sha", Head, "--execution-unit", Unit, "--format", "json"];

    private string[] DesignStatusArgs() =>
        ["review", "cross-runtime", "status", "--kind", "design", "--execution-unit", Unit, "--format", "json"];

    private string[] ImplementationRecordArgs() =>
        [
            "review", "cross-runtime", "record",
            "--repo", Repo,
            "--pr", Pr.ToString(),
            "--head-sha", Head,
            "--execution-unit", Unit,
            "--kind", "implementation",
            "--runtime", "codex",
            "--runtime-version", "codex-cli 0.154.0",
            "--verdict-file", verdictFile,
            "--format", "json",
        ];

    private string[] DesignRecordArgs() =>
        [
            "review", "cross-runtime", "record",
            "--kind", "design",
            "--execution-unit", Unit,
            "--packet-digest", "0000000000000000000000000000000000000000000000000000000000000000",
            "--runtime", "codex",
            "--runtime-version", "codex-cli 0.154.0",
            "--verdict-file", verdictFile,
            "--format", "json",
        ];

    private void WriteQueue(params (string Unit, string? LinkedPr)[] items)
    {
        var queue = new
        {
            schema_version = "1",
            updated_at = "2026-09-14T00:00:00+00:00",
            items = items.Select(item => new Dictionary<string, object?>
            {
                ["execution_unit"] = item.Unit,
                ["title"] = item.Unit,
                ["state"] = "active",
                ["dependencies"] = Array.Empty<string>(),
                ["blocked_by"] = Array.Empty<string>(),
                ["clarification_return_path"] = "intents/intent-cli/clarifications/open.md",
                ["packet_paths"] = new
                {
                    implementation = $".intent-cli/issues/{item.Unit}/implementation.md",
                    review_context = $".intent-cli/issues/{item.Unit}/review-context.md",
                    yaml = $".intent-cli/issues/{item.Unit}/packet.yaml",
                },
                ["linked_issue"] = new { repo = Repo, number = 1811, url = $"https://github.com/{Repo}/issues/1811" },
                ["linked_pr"] = item.LinkedPr,
                ["worker_role"] = "builder",
                ["review_role"] = "reviewer",
                ["priority"] = "high",
            }).ToArray(),
        };
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), JsonSerializer.Serialize(queue));
    }

    private void WriteValidPacket() =>
        G841TestHelpers.WritePacketFiles(root, Unit, G841TestHelpers.LegacyEquivalentPacket(Domain));

    private void WritePacketYaml(string yaml) => File.WriteAllText(PacketPath(), yaml);

    private void WriteImplementationClaim() => G841TestHelpers.WriteClaim(root, Unit, Team, "implementation");

    private void RemoveClaim()
    {
        var claimPath = Path.Combine(
            root,
            ClaimCommand.ClaimPath($"execution-unit:{Unit}").Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(claimPath))
        {
            File.Delete(claimPath);
        }
    }

    private string PacketPath() => G841TestHelpers.PacketPath(root, Unit);

    private static string PacketRelativePath() => ".intent-cli/issues/G841/packet.yaml";

    private static void ArmDeniedPacketReader()
    {
        PacketFileReader.ReadAllText = _ => throw new UnauthorizedAccessException("Access to the path is denied.");
        PacketFileReader.ReadAllBytes = _ => throw new UnauthorizedAccessException("Access to the path is denied.");
    }

    private static void DisarmDeniedPacketReader()
    {
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
    }

    internal sealed class RecordingMutator : IGitHubLabelMutator, IGitHubLabelSetReplacer
    {
        public IReadOnlyList<string> Labels { get; set; } = [];

        public List<string> Applied { get; } = [];

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Applied.Add($"apply +{string.Join(",", addLabels)} -{string.Join(",", removeLabels)}");

        public void ApplyReconcileTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Applied.Add("reconcile");

        public LabelSetReplacementCertainty ReplaceLabelSet(string repo, string kind, int number, IReadOnlyCollection<string> currentLabels, IReadOnlyCollection<string> desiredLabels)
        {
            Applied.Add($"replace {string.Join(",", desiredLabels)}");
            return LabelSetReplacementCertainty.AppliedAndVerified;
        }
    }
}
