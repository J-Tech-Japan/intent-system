using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G835: <c>issue publish-flow</c> design-review gate on gated repositories
/// for declared teams; ungated repos and undeclared teams stay byte-identical.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G835PublishFlowTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G835PF";
    private const string OtherRepo = "J-Tech-Japan/other";
    private const string UndeclaredTeam = "other-team";

    public static IEnumerable<object[]> S08NegativeCases =>
        from scenario in new[] { "missing", "inactive", "malformed", "digest", "provenance" }
        from declared in new[] { false, true }
        from gated in new[] { false, true }
        from write in new[] { false, true }
        from format in new[] { "json", "markdown" }
        select new object[] { scenario, declared, gated, write, format };

    public static IEnumerable<object[]> S08PositiveCases =>
        from declared in new[] { false, true }
        from gated in new[] { false, true }
        from write in new[] { false, true }
        from format in new[] { "json", "markdown" }
        select new object[] { declared, gated, write, format };

    private readonly ThrowingIssueCreator throwingCreator = new();
    private readonly StubExistingIssueChecker defaultChecker =
        new(GitHubExistingIssueClassification.None);

    public G835PublishFlowTests()
    {
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
    }

    internal static void ConfigureG842PublishFlowSeams(
        Func<IIssueCreator>? creatorFactory,
        Func<IGitHubExistingIssueChecker>? existingIssueCheckerFactory,
        Func<DateTimeOffset>? utcNowFactory)
    {
        IssuePublishFlowCommand.CreatorFactory = creatorFactory;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = existingIssueCheckerFactory;
        IssuePublishFlowCommand.UtcNowFactory = utcNowFactory;
    }

    // ── byte identity ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishFlow_NoDeclaration_IsByteIdenticalToDeclaringHostWithUngatedRepo(bool write)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false);
        workspace.WriteMinimalPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        var baseline = NormalizePublishOutput(Run(workspace, Unit, OtherRepo, write));
        using var declaring = new G835PublishFlowWorkspace(declare: true);
        declaring.WriteMinimalPacket(Unit, Repo);
        declaring.SeedQueueState(Unit, Title());
        var gated = NormalizePublishOutput(Run(declaring, Unit, OtherRepo, write));

        Assert.Equal(baseline.Json, gated.Json);
        Assert.Equal(baseline.ExitCode, gated.ExitCode);
        Assert.DoesNotContain("cross_runtime_design_review", gated.Json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishFlow_GatedRepo_UndeclaredTeam_IsByteIdentical(bool write)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WriteMinimalPacket(Unit, Repo, targetRepoOverride: "submodules/intent-system");
        workspace.SeedQueueState(Unit, Title());
        var stub = new StubIssueCreator($"https://github.com/{Repo}/issues/8350");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var baseline = NormalizePublishOutput(Run(workspace, Unit, Repo, write, team: UndeclaredTeam));
        using var gated = new G835PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        gated.WriteMinimalPacket(Unit, Repo, targetRepoOverride: "submodules/intent-system");
        gated.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
        var declaring = NormalizePublishOutput(Run(gated, Unit, Repo, write, team: UndeclaredTeam));

        Assert.Equal(baseline.Json, declaring.Json);
        Assert.Equal(0, declaring.ExitCode);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_LegacyDeclaredCreateUsesOnlyItsExistingPublishedAtClockRead(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8639");
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
        var clockCalls = 0;
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            clockCalls++;
            return new DateTimeOffset(2026, 10, 10, 12, 0, clockCalls, TimeSpan.Zero);
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true, format: format);

        Assert.True(exit == 0, output);
        Assert.Equal(1, creator.CallCount);
        Assert.Equal(1, clockCalls);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.TryGetProperty("scope_sources", out _));
        }
        else
        {
            Assert.Contains("- created: yes", output, StringComparison.Ordinal);
            Assert.DoesNotContain("## scope_sources", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_ExplicitEmptySourcesPreserveLegacyCreateAndClockBudget(string format)
    {
        using var legacy = new G835PublishFlowWorkspace(declare: true);
        legacy.WriteFullPacket(Unit, Repo);
        legacy.SeedQueueState(Unit, Title());
        legacy.RecordSatisfiedDesignReviews(Unit);
        var legacyCreator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8638");
        var legacyChecker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        IssuePublishFlowCommand.CreatorFactory = () => legacyCreator;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => legacyChecker;
        var legacyClockCalls = 0;
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            legacyClockCalls++;
            return new DateTimeOffset(2026, 10, 10, 12, 0, 1, TimeSpan.Zero);
        };
        var legacyResult = Run(legacy, Unit, Repo, write: true, format: format);

        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.AppendAllText(Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml"),
            "\nscope_sources: []\nscope_source_digests: {}\n");
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);

        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8638");
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        var clockCalls = 0;
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            clockCalls++;
            return new DateTimeOffset(2026, 10, 10, 12, 0, 1, TimeSpan.Zero);
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true, format: format);

        Assert.Equal(0, legacyResult.ExitCode);
        Assert.Equal(0, exit);
        Assert.Equal(1, legacyCreator.CallCount);
        Assert.Equal(1, creator.CallCount);
        Assert.Equal(1, legacyChecker.CallCount);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(1, legacyClockCalls);
        Assert.Equal(1, clockCalls);
        Assert.Equal(File.ReadAllBytes(legacy.GithubBodyPath(Unit)), legacyCreator.LastBodyBytes);
        Assert.Equal(File.ReadAllBytes(workspace.GithubBodyPath(Unit)), creator.LastBodyBytes);
        Assert.True(CrossRuntimeDesignReviewDigest.TryReadFromDirectory(
            legacy.PacketDirectory(Unit), out var legacyPacket, out var legacyMissing), legacyMissing);
        Assert.True(CrossRuntimeDesignReviewDigest.TryReadFromDirectory(
            workspace.PacketDirectory(Unit), out var emptyPacket, out var emptyMissing), emptyMissing);
        var legacyPacketDigest = CrossRuntimeDesignReviewDigest.Compute(legacyPacket);
        var emptyPacketDigest = CrossRuntimeDesignReviewDigest.Compute(emptyPacket);
        Assert.NotEqual(legacyPacketDigest, emptyPacketDigest);
        if (format == "json")
        {
            var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(NormalizePublishOutput(legacyResult).Json)!.AsObject();
            var emptyJson = System.Text.Json.Nodes.JsonNode.Parse(NormalizePublishOutput((exit, output)).Json)!.AsObject();
            var legacyReview = legacyJson["cross_runtime_design_review"]!.AsObject();
            var emptyReview = emptyJson["cross_runtime_design_review"]!.AsObject();
            Assert.Equal(legacyPacketDigest, legacyReview["digest"]!.GetValue<string>());
            Assert.Equal(emptyPacketDigest, emptyReview["digest"]!.GetValue<string>());
            legacyReview["digest"] = "<packet-digest>";
            emptyReview["digest"] = "<packet-digest>";
            Assert.Equal(legacyJson.ToJsonString(), emptyJson.ToJsonString());
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.TryGetProperty("scope_sources", out _));
        }
        else
        {
            Assert.Equal(
                legacyResult.Output.Replace(legacy.RootPath, "<normalized>", StringComparison.Ordinal)
                    .Replace(legacyPacketDigest, "<packet-digest>", StringComparison.Ordinal),
                output.Replace(workspace.RootPath, "<normalized>", StringComparison.Ordinal)
                    .Replace(emptyPacketDigest, "<packet-digest>", StringComparison.Ordinal));
            Assert.Contains("- created: yes", output, StringComparison.Ordinal);
            Assert.DoesNotContain("## scope_sources", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, "json")]
    [InlineData(false, "markdown")]
    [InlineData(true, "json")]
    [InlineData(true, "markdown")]
    public void PublishFlow_LegacyFirstDocumentIgnoresHarmlessLaterYamlDocument(bool write, string format)
    {
        using var baseline = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        using var extraDocument = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        baseline.WriteMinimalPacket(Unit, OtherRepo);
        extraDocument.WriteMinimalPacket(Unit, OtherRepo);
        File.AppendAllText(Path.Combine(extraDocument.PacketDirectory(Unit), "packet.yaml"),
            "\n---\nunrelated: metadata\n");
        baseline.SeedQueueState(Unit, Title());
        extraDocument.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.CreatorFactory = () => new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8677");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var expected = Run(baseline, Unit, OtherRepo, write, team: UndeclaredTeam, format: format);
        var actual = Run(extraDocument, Unit, OtherRepo, write, team: UndeclaredTeam, format: format);
        Assert.Equal(0, expected.ExitCode);
        Assert.Equal(expected.ExitCode, actual.ExitCode);
        if (format == "json")
        {
            var normalizedExpected = NormalizePublishOutput(expected, baseline.RootPath);
            var normalizedActual = NormalizePublishOutput(actual, extraDocument.RootPath);
            Assert.Equal(normalizedExpected.Json, normalizedActual.Json);
            using var result = JsonDocument.Parse(actual.Output);
            Assert.False(result.RootElement.TryGetProperty("scope_sources", out _));
            Assert.Equal(write, result.RootElement.GetProperty("created").GetBoolean());
        }
        else
        {
            Assert.Equal(expected.Output.Replace(baseline.RootPath, "<normalized>", StringComparison.Ordinal),
                actual.Output.Replace(extraDocument.RootPath, "<normalized>", StringComparison.Ordinal));
            Assert.DoesNotContain("## scope_sources", actual.Output, StringComparison.Ordinal);
            Assert.Contains(write ? "- created: yes" : "- created: no", actual.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("root-empty-alias", "json")]
    [InlineData("root-empty-alias", "markdown")]
    [InlineData("tagged-source-key", "json")]
    [InlineData("tagged-source-key", "markdown")]
    public void PublishFlow_ExplicitEmptyAliasOrTaggedSourceIsNotLegacy(string scenario, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.AppendAllText(Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml"), scenario == "root-empty-alias"
            ? "\nempty_sources: &no_sources []\nscope_sources: *no_sources\nscope_source_digests: {}\n"
            : "\n!bad scope_sources: []\nscope_source_digests: {}\n");
        workspace.SeedQueueState(Unit, Title());
        var checkerFactories = 0;
        var creatorFactories = 0;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => { checkerFactories++; return defaultChecker; };
        IssuePublishFlowCommand.CreatorFactory = () => { creatorFactories++; return throwingCreator; };

        var (exit, output) = Run(workspace, Unit, Repo, write: true, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(0, checkerFactories);
        Assert.Equal(0, creatorFactories);
        Assert.Equal(0, throwingCreator.CallCount);
        Assert.False(File.Exists(workspace.PublishYamlPath(Unit)));
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal("scope-sources-invalid-declaration", result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
        }
        else
        {
            Assert.Contains("scope-sources-invalid-declaration", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_OrdinaryOptInPreviewAndCreateKeepVerifiedSourceAndAuthoredBody(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        var source = workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8637");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (previewExit, previewOutput) = Run(workspace, Unit, OtherRepo, write: false,
            team: UndeclaredTeam, format: format);
        Assert.Equal(0, previewExit);
        if (format == "json")
        {
            using var preview = JsonDocument.Parse(previewOutput);
            Assert.False(preview.RootElement.GetProperty("created").GetBoolean());
            var sources = preview.RootElement.GetProperty("scope_sources");
            Assert.Equal("satisfied", sources.GetProperty("state").GetString());
            Assert.Equal("not-verified", sources.GetProperty("publication").GetString());
            Assert.Equal(source.Digest, sources.GetProperty("provenance")[0].GetProperty("sha256").GetString());
        }
        else
        {
            Assert.Contains("- created: no", previewOutput, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"satisfied\"", previewOutput, StringComparison.Ordinal);
            Assert.Contains("\"publication\": \"not-verified\"", previewOutput, StringComparison.Ordinal);
            Assert.Contains(source.Digest, previewOutput, StringComparison.Ordinal);
        }
        Assert.Equal(0, creator.CallCount);

        var (createExit, createOutput) = Run(workspace, Unit, OtherRepo, write: true,
            team: UndeclaredTeam, format: format);
        Assert.Equal(0, createExit);
        Assert.Equal(1, creator.CallCount);
        Assert.Equal(File.ReadAllBytes(workspace.GithubBodyPath(Unit)), creator.LastBodyBytes);
        Assert.Equal(source.BodyBytes, creator.LastBodyBytes);
        if (format == "json")
        {
            using var created = JsonDocument.Parse(createOutput);
            Assert.True(created.RootElement.GetProperty("created").GetBoolean());
            Assert.Equal("satisfied", created.RootElement.GetProperty("scope_sources").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("- created: yes", createOutput, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"satisfied\"", createOutput, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_LocalExistingIssueKeepsSatisfiedPinnedSource(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), OtherRepo, 8636, $"https://github.com/{OtherRepo}/issues/8636");
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8636");
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

        Assert.Equal(0, exit);
        Assert.Equal(0, creator.CallCount);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.Equal("satisfied", result.RootElement.GetProperty("scope_sources").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("- idempotent: yes", output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"satisfied\"", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_GitHubUniqueRestoreKeepsSatisfiedPinnedSource(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.Unique, 8635,
            $"https://github.com/{OtherRepo}/issues/8635");
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8635");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

        Assert.Equal(0, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.True(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            Assert.Equal("satisfied", result.RootElement.GetProperty("scope_sources").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("- idempotent: yes", output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: yes", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"satisfied\"", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("pin", "json")]
    [InlineData("pin", "markdown")]
    [InlineData("body", "json")]
    [InlineData("body", "markdown")]
    public void PublishFlow_DeclaredGitHubUniqueRestoreRefusesChangedPinnedSource(string mutation, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var checker = new CallbackExistingIssueChecker(new GitHubExistingIssueLookupResult
        {
            Classification = GitHubExistingIssueClassification.Unique,
            IssueNumber = 8640,
            IssueUrl = $"https://github.com/{Repo}/issues/8640",
        }, () =>
        {
            if (mutation == "pin")
            {
                File.WriteAllText(packetPath, File.ReadAllText(packetPath)
                    .Replace(source.Digest, new string('0', 64), StringComparison.Ordinal));
            }
            else
            {
                File.AppendAllText(workspace.GithubBodyPath(Unit), "\nchanged during unique lookup\n");
            }
        });
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8640");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.LookupInputChanged, result.RootElement.GetProperty("cause").GetString());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("refused", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-changed", sources.GetProperty("cause").GetString());
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains(CrossRuntimeReviewCauses.LookupInputChanged, output, StringComparison.Ordinal);
            Assert.Contains("scope-sources-changed", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"refused\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_DeclaredMalformedLookupSnapshotDoesNotRetainSatisfiedSourceProof(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8641");
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = () =>
            File.WriteAllText(Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml"), "[\n");

        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(0, creator.CallCount);
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            var root = result.RootElement;
            Assert.Equal("packet-yaml-unparseable", root.GetProperty("cause").GetString());
            var sources = root.GetProperty("scope_sources");
            Assert.Equal("refused", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-changed", sources.GetProperty("cause").GetString());
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains("packet-yaml-unparseable", output, StringComparison.Ordinal);
            Assert.Contains("scope-sources-changed", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"refused\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("before-lookup", "packet", "delete", "json")]
    [InlineData("before-lookup", "packet", "delete", "markdown")]
    [InlineData("before-lookup", "packet", "lock", "json")]
    [InlineData("before-lookup", "packet", "lock", "markdown")]
    [InlineData("before-lookup", "body", "delete", "json")]
    [InlineData("before-lookup", "body", "delete", "markdown")]
    [InlineData("before-lookup", "body", "lock", "json")]
    [InlineData("before-lookup", "body", "lock", "markdown")]
    [InlineData("unique-lookup", "packet", "delete", "json")]
    [InlineData("unique-lookup", "packet", "delete", "markdown")]
    [InlineData("unique-lookup", "packet", "lock", "json")]
    [InlineData("unique-lookup", "packet", "lock", "markdown")]
    [InlineData("unique-lookup", "body", "delete", "json")]
    [InlineData("unique-lookup", "body", "delete", "markdown")]
    [InlineData("unique-lookup", "body", "lock", "json")]
    [InlineData("unique-lookup", "body", "lock", "markdown")]
    public void PublishFlow_DeclaredLookupSnapshotReadFailureRetainsUnavailableSourceTruth(
        string boundary, string target, string failure, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();

        var targetPath = target == "packet"
            ? Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml")
            : workspace.GithubBodyPath(Unit);
        var targetBytes = File.ReadAllBytes(targetPath);
        var expectedRelativePath = $".intent-cli/issues/{Unit}/{(target == "packet" ? "packet.yaml" : "github-body.md")}";
        var mutationReached = false;
        bool? expectedTargetExistsAfterMutation = null;
        FileStream? exclusiveReadLock = null;
        void BreakTargetRead()
        {
            mutationReached = true;
            if (failure == "delete")
            {
                File.Delete(targetPath);
            }
            else
            {
                exclusiveReadLock = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            expectedTargetExistsAfterMutation = File.Exists(targetPath);
        }

        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8690");
        var uniqueResult = new GitHubExistingIssueLookupResult
        {
            Classification = GitHubExistingIssueClassification.Unique,
            IssueNumber = 8690,
            IssueUrl = $"https://github.com/{Repo}/issues/8690",
        };
        var checker = new CallbackExistingIssueChecker(uniqueResult, BreakTargetRead);
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        if (boundary == "before-lookup")
        {
            IssuePublishFlowCommand.BeforeLookupSnapshotHook = BreakTargetRead;
        }

        try
        {
            var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

            Assert.Equal(1, exit);
            Assert.True(mutationReached);
            Assert.Equal(boundary == "unique-lookup" ? 1 : 0, checker.CallCount);
            Assert.Equal(0, creator.CallCount);
            Assert.True(File.Exists(source.ArtifactPath));
            AssertDurableStateUntouched(workspace);

            Assert.Equal(failure == "lock", expectedTargetExistsAfterMutation);
            exclusiveReadLock?.Dispose();
            exclusiveReadLock = null;
            if (failure == "delete")
            {
                Assert.False(File.Exists(targetPath));
            }
            else
            {
                Assert.Equal(targetBytes, File.ReadAllBytes(targetPath));
            }

            var expectedTopCause = boundary == "unique-lookup"
                ? CrossRuntimeReviewCauses.LookupInputChanged
                : PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable;
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                var root = result.RootElement;
                Assert.Equal(expectedTopCause, root.GetProperty("cause").GetString());
                Assert.False(root.GetProperty("created").GetBoolean());
                Assert.False(root.GetProperty("idempotent").GetBoolean());
                Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
                Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
                Assert.False(root.GetProperty("publish_yaml_patched").GetBoolean());
                Assert.False(root.GetProperty("runs_appended").GetBoolean());
                var sources = root.GetProperty("scope_sources");
                Assert.Equal("unavailable", sources.GetProperty("state").GetString());
                Assert.Equal("scope-sources-packet-unavailable", sources.GetProperty("cause").GetString());
                var diagnostic = Assert.Single(sources.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(expectedRelativePath, diagnostic.GetProperty("path").GetString());
                Assert.False(string.IsNullOrWhiteSpace(diagnostic.GetProperty("detail").GetString()));
                AssertNullOrMissing(sources, "provenance");
                AssertNullOrMissing(sources, "expected_provenance_block");
            }
            else
            {
                Assert.Contains(expectedTopCause, output, StringComparison.Ordinal);
                Assert.Contains("scope-sources-packet-unavailable", output, StringComparison.Ordinal);
                Assert.Contains(expectedRelativePath, output, StringComparison.Ordinal);
                Assert.Contains("- created: no", output, StringComparison.Ordinal);
                Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
                Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
                Assert.Contains("- queue_state_patched: no", output, StringComparison.Ordinal);
                Assert.Contains("- publish_yaml_patched: no", output, StringComparison.Ordinal);
                Assert.Contains("- runs_appended: no", output, StringComparison.Ordinal);
                Assert.Contains("\"state\": \"unavailable\"", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Expected provenance block", output, StringComparison.Ordinal);
            }
        }
        finally
        {
            exclusiveReadLock?.Dispose();
            IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        }
    }

    [Theory]
    [InlineData("review-context", "json")]
    [InlineData("review-context", "markdown")]
    [InlineData("implementation", "json")]
    [InlineData("implementation", "markdown")]
    public void PublishFlow_DeclaredPacketReadPermissionFailureAfterLookupReturnsUnavailable(
        string target, string format)
    {
        if (OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture requires Unix file modes.");
        }

        if (!G841TestHelpers.IsNonRootUnixUser())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture requires a non-root test user.");
        }

        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();

        var targetPath = Path.Combine(workspace.PacketDirectory(Unit), target + ".md");
        var originalMode = File.GetUnixFileMode(targetPath);
        var packetDirectorySnapshot = Directory.EnumerateFiles(workspace.PacketDirectory(Unit))
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes, StringComparer.Ordinal);
        var deniedReadReached = false;
        var checker = new CallbackExistingIssueChecker(
            new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("Unix permission-denied fixture requires Unix file modes.");
                }
                File.SetUnixFileMode(targetPath, UnixFileMode.None);
                Assert.True(File.Exists(targetPath));
                Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(targetPath));
                deniedReadReached = true;
            });
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8691");
        var creatorFactoryCalls = 0;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            creatorFactoryCalls++;
            return creator;
        };

        try
        {
            var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

            Assert.Equal(1, exit);
            Assert.True(deniedReadReached);
            Assert.Equal(1, checker.CallCount);
            Assert.Equal(0, creatorFactoryCalls);
            Assert.Equal(0, creator.CallCount);
            Assert.True(File.Exists(source.ArtifactPath));
            AssertDurableStateUntouched(workspace);

            var relativePath = $".intent-cli/issues/{Unit}/{target}.md";
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                var root = result.RootElement;
                Assert.Equal(CrossRuntimeReviewCauses.PacketMissing, root.GetProperty("cause").GetString());
                Assert.StartsWith("packet file is missing:", root.GetProperty("error").GetString(), StringComparison.Ordinal);
                Assert.False(root.GetProperty("created").GetBoolean());
                Assert.False(root.GetProperty("idempotent").GetBoolean());
                Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
                var sources = root.GetProperty("scope_sources");
                Assert.Equal("unavailable", sources.GetProperty("state").GetString());
                Assert.Equal("scope-sources-packet-unavailable", sources.GetProperty("cause").GetString());
                var diagnostic = Assert.Single(sources.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(relativePath, diagnostic.GetProperty("path").GetString());
                Assert.Contains("became unavailable before create", diagnostic.GetProperty("detail").GetString(), StringComparison.Ordinal);
                AssertNullOrMissing(sources, "provenance");
                AssertNullOrMissing(sources, "expected_provenance_block");
            }
            else
            {
                Assert.Contains(CrossRuntimeReviewCauses.PacketMissing, output, StringComparison.Ordinal);
                Assert.Contains("packet file is missing:", output, StringComparison.Ordinal);
                Assert.Contains("scope-sources-packet-unavailable", output, StringComparison.Ordinal);
                Assert.Contains(relativePath, output, StringComparison.Ordinal);
                Assert.Contains("became unavailable before create", output, StringComparison.Ordinal);
                Assert.Contains("\"state\": \"unavailable\"", output, StringComparison.Ordinal);
                Assert.Contains("- created: no", output, StringComparison.Ordinal);
                Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Expected provenance block", output, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.SetUnixFileMode(targetPath, originalMode);
        }

        Assert.Equal(packetDirectorySnapshot.Keys.Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(workspace.PacketDirectory(Unit)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in packetDirectorySnapshot)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(workspace.PacketDirectory(Unit), name)));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_DeclaredInitialSnapshotPermissionFailureReturnsUnavailablePath(string format)
    {
        if (OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture requires Unix file modes.");
        }

        if (!G841TestHelpers.IsNonRootUnixUser())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture requires a non-root test user.");
        }

        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();

        var targetPath = Path.Combine(workspace.PacketDirectory(Unit), "review-context.md");
        var originalMode = File.GetUnixFileMode(targetPath);
        var packetDirectorySnapshot = Directory.EnumerateFiles(workspace.PacketDirectory(Unit))
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes, StringComparer.Ordinal);
        File.SetUnixFileMode(targetPath, UnixFileMode.None);
        var deniedReadReached = false;

        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8692");
        var checkerFactoryCalls = 0;
        var creatorFactoryCalls = 0;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
        {
            checkerFactoryCalls++;
            return checker;
        };
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            creatorFactoryCalls++;
            return creator;
        };

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(targetPath));
            deniedReadReached = true;
            var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

            Assert.Equal(1, exit);
            Assert.True(deniedReadReached);
            Assert.Equal(0, checkerFactoryCalls);
            Assert.Equal(0, checker.CallCount);
            Assert.Equal(0, creatorFactoryCalls);
            Assert.Equal(0, creator.CallCount);
            Assert.True(File.Exists(source.ArtifactPath));
            AssertDurableStateUntouched(workspace);

            const string relativePath = $".intent-cli/issues/{Unit}/review-context.md";
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                var root = result.RootElement;
                Assert.Equal("scope-sources-packet-unavailable", root.GetProperty("cause").GetString());
                var sources = root.GetProperty("scope_sources");
                Assert.Equal("unavailable", sources.GetProperty("state").GetString());
                Assert.Equal("scope-sources-packet-unavailable", sources.GetProperty("cause").GetString());
                var diagnostic = Assert.Single(sources.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(relativePath, diagnostic.GetProperty("path").GetString());
                Assert.Contains("unreadable before GitHub lookup", diagnostic.GetProperty("detail").GetString(), StringComparison.Ordinal);
                AssertNullOrMissing(sources, "provenance");
                AssertNullOrMissing(sources, "expected_provenance_block");
            }
            else
            {
                Assert.Contains("scope-sources-packet-unavailable", output, StringComparison.Ordinal);
                Assert.Contains(relativePath, output, StringComparison.Ordinal);
                Assert.Contains("unreadable before GitHub lookup", output, StringComparison.Ordinal);
                Assert.Contains("\"state\": \"unavailable\"", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.SetUnixFileMode(targetPath, originalMode);
        }

        Assert.Equal(packetDirectorySnapshot.Keys.Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(workspace.PacketDirectory(Unit)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in packetDirectorySnapshot)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(workspace.PacketDirectory(Unit), name)));
        }
    }

    [Theory]
    [InlineData("lookup-none", "json")]
    [InlineData("lookup-none", "markdown")]
    [InlineData("lookup-unique", "json")]
    [InlineData("lookup-unique", "markdown")]
    [InlineData("lookup-unheld", "json")]
    [InlineData("lookup-unheld", "markdown")]
    [InlineData("clock", "json")]
    [InlineData("clock", "markdown")]
    [InlineData("after-gate", "json")]
    [InlineData("after-gate", "markdown")]
    public void PublishFlow_DeclaredPinnedSourceRefusesWhenHeldClaimTeamChangesAtLateBoundary(
        string timing, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        var claimChanged = false;
        var lookupReturned = false;
        var checkerClassification = timing == "lookup-unique"
            ? GitHubExistingIssueClassification.Unique
            : GitHubExistingIssueClassification.None;
        var checker = new CallbackExistingIssueChecker(new GitHubExistingIssueLookupResult
        {
            Classification = checkerClassification,
            IssueNumber = checkerClassification == GitHubExistingIssueClassification.Unique ? 8641 : null,
            IssueUrl = checkerClassification == GitHubExistingIssueClassification.Unique
                ? $"https://github.com/{Repo}/issues/8641"
                : null,
        }, () =>
        {
            lookupReturned = true;
            if (timing is "lookup-none" or "lookup-unique" or "lookup-unheld")
            {
                if (timing == "lookup-unheld")
                {
                    File.Delete(Path.Combine(workspace.ClaimsDirectory,
                        ClaimCommand.ClaimPath($"execution-unit:{Unit}").Split('/').Last()));
                }
                else
                {
                    workspace.WriteClaim(Unit, UndeclaredTeam);
                }
                claimChanged = true;
                workspace.CaptureDurableBaseline();
            }
        });
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8642");
        var creatorFactoryCalls = 0;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            creatorFactoryCalls++;
            return creator;
        };
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            if (timing == "clock" && lookupReturned && !claimChanged)
            {
                workspace.WriteClaim(Unit, UndeclaredTeam);
                claimChanged = true;
                workspace.CaptureDurableBaseline();
            }
            return new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        };
        if (timing == "after-gate")
        {
            IssuePublishFlowCommand.AfterGateHook = () =>
            {
                workspace.WriteClaim(Unit, UndeclaredTeam);
                claimChanged = true;
                workspace.CaptureDurableBaseline();
            };
        }

        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

        Assert.Equal(1, exit);
        Assert.True(claimChanged);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.Equal(timing == "after-gate" ? 1 : 0, creatorFactoryCalls);
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal("scope-sources-identity-mismatch", result.RootElement.GetProperty("cause").GetString());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("refused", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-identity-mismatch", sources.GetProperty("cause").GetString());
            var expectedVerifierStatus = timing == "lookup-unheld" ? "unheld" : "held-by-other-team";
            Assert.Contains(expectedVerifierStatus, sources.GetProperty("diagnostics")[0].GetProperty("cause").GetString(), StringComparison.Ordinal);
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains("scope-sources-identity-mismatch", output, StringComparison.Ordinal);
            Assert.Contains(timing == "lookup-unheld" ? "unheld" : "held-by-other-team", output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_ConfiguredClaimStoreRemovalAfterLookupRefusesOptedInWrite(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        var initialClaim = ClaimOwnershipVerifier.Verify(
            workspace.RootPath, $"execution-unit:{Unit}", UndeclaredTeam);
        Assert.True(initialClaim.Passed);
        Assert.True(initialClaim.StoreConfigured);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, initialClaim.Status);
        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var runsBefore = File.Exists(workspace.RunsLogPath) ? File.ReadAllBytes(workspace.RunsLogPath) : Array.Empty<byte>();
        var checker = new CallbackExistingIssueChecker(
            new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            () => Directory.Delete(workspace.ClaimsDirectory, recursive: true));
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8681");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true,
            team: UndeclaredTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.False(Directory.Exists(workspace.ClaimsDirectory));
        Assert.Equal(queueBefore, File.ReadAllBytes(workspace.QueueStatePath));
        Assert.Equal(runsBefore, File.Exists(workspace.RunsLogPath) ? File.ReadAllBytes(workspace.RunsLogPath) : Array.Empty<byte>());
        Assert.False(File.Exists(workspace.PublishYamlPath(Unit)));
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("refused", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-identity-mismatch", sources.GetProperty("cause").GetString());
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
        }
        else
        {
            Assert.Contains("scope-sources-identity-mismatch", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"refused\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("remote-default", "unchanged", "json")]
    [InlineData("remote-default", "unchanged", "markdown")]
    [InlineData("remote-default", "changed-pin", "json")]
    [InlineData("remote-default", "changed-pin", "markdown")]
    [InlineData("metadata-write", "unchanged", "json")]
    [InlineData("metadata-write", "unchanged", "markdown")]
    [InlineData("metadata-write", "changed-pin", "json")]
    [InlineData("metadata-write", "changed-pin", "markdown")]
    public void PublishFlow_GitClaimRecheckKeepsInitialRemoteAuthorityWithoutFetchingOfflineOrigin(
        string branchMode, string scenario, string format)
    {
        var metadataWriteBranch = branchMode == "metadata-write" ? "main-metadata" : null;
        using var workspace = new G835PublishFlowWorkspace(
            declare: false, heldTeam: null, metadataWriteBranch: metadataWriteBranch);
        var source = workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        workspace.InitializeGitAndCapture();
        using (var claimOutput = new StringWriter())
        {
            var claimExit = ClaimCommand.ExecuteAcquire(workspace.Context,
                ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", UndeclaredTeam,
                 "--write", "--format", "json"], claimOutput);
            Assert.Equal(0, claimExit);
            using var acquired = JsonDocument.Parse(claimOutput.ToString());
            Assert.Equal("acquired", acquired.RootElement.GetProperty("status").GetString());
            Assert.True(acquired.RootElement.GetProperty("push_succeeded").GetBoolean());
            Assert.Equal(metadataWriteBranch is null ? "refs/heads/main" : $"refs/heads/{metadataWriteBranch}",
                acquired.RootElement.GetProperty("target_ref").GetString());
        }

        Assert.False(File.Exists(workspace.LocalClaimPath(Unit)));
        Assert.True(workspace.OriginHasClaim(Unit, metadataWriteBranch ?? "main"));
        var initialClaim = ClaimOwnershipVerifier.Verify(
            workspace.RootPath, $"execution-unit:{Unit}", UndeclaredTeam);
        Assert.True(initialClaim.Passed, initialClaim.Detail);
        Assert.True(initialClaim.StoreConfigured, initialClaim.Detail);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, initialClaim.Status);
        Assert.False(File.Exists(workspace.LocalClaimPath(Unit)));

        G835PublishFlowWorkspace.GitSnapshot? refsBeforeLateCheck = null;
        var lateBoundaryReached = false;
        var checker = new CallbackExistingIssueChecker(
            new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            () =>
            {
                lateBoundaryReached = true;
                refsBeforeLateCheck = workspace.CaptureGitState();
                workspace.AdvanceOriginClaimTeam(Unit, "remote-advanced-team", metadataWriteBranch ?? "main");
                workspace.MakeOriginUnavailable();
                if (scenario == "changed-pin")
                {
                    workspace.ChangePinnedSourceDigest(source.Digest);
                    workspace.CaptureDurableBaseline();
                }
            });
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8682");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true,
            team: UndeclaredTeam, format: format);

        Assert.True(lateBoundaryReached);
        Assert.NotNull(refsBeforeLateCheck);
        workspace.AssertGitStateUnchanged(refsBeforeLateCheck!);
        Assert.False(File.Exists(workspace.LocalClaimPath(Unit)));
        Assert.Equal(1, checker.CallCount);
        if (scenario == "unchanged")
        {
            Assert.Equal(0, exit);
            Assert.Equal(1, creator.CallCount);
            Assert.True(File.Exists(workspace.PublishYamlPath(Unit)));
            Assert.Equal(File.ReadAllBytes(workspace.GithubBodyPath(Unit)), creator.LastBodyBytes);
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                Assert.True(result.RootElement.GetProperty("created").GetBoolean());
                Assert.Equal("satisfied", result.RootElement.GetProperty("scope_sources").GetProperty("state").GetString());
                Assert.Equal(source.Digest,
                    result.RootElement.GetProperty("scope_sources").GetProperty("provenance")[0].GetProperty("sha256").GetString());
            }
            else
            {
                Assert.Contains("- created: yes", output, StringComparison.Ordinal);
                Assert.Contains("\"state\": \"satisfied\"", output, StringComparison.Ordinal);
                Assert.Contains(source.Digest, output, StringComparison.Ordinal);
            }
        }
        else
        {
            Assert.Equal(1, exit);
            Assert.Equal(0, creator.CallCount);
            AssertDurableStateUntouched(workspace);
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                Assert.Equal("scope-sources-changed", result.RootElement.GetProperty("cause").GetString());
                var sources = result.RootElement.GetProperty("scope_sources");
                Assert.Equal("scope-sources-changed", sources.GetProperty("cause").GetString());
                AssertNullOrMissing(sources, "provenance");
                AssertNullOrMissing(sources, "expected_provenance_block");
            }
            else
            {
                Assert.Contains("scope-sources-changed", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
                Assert.Contains("- created: no", output, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_MalformedLocalClaimAfterLookupIsStructuredRefusal(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        var claimPath = Path.Combine(workspace.ClaimsDirectory,
            ClaimCommand.ClaimPath($"execution-unit:{Unit}").Split('/').Last());
        var checker = new CallbackExistingIssueChecker(
            new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            () =>
            {
                File.WriteAllText(claimPath, "{ malformed");
                workspace.CaptureDurableBaseline();
            });
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8683");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true,
            team: UndeclaredTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("scope-sources-identity-mismatch", sources.GetProperty("cause").GetString());
            Assert.Contains("claim-verification-invalid", sources.GetProperty("diagnostics")[0].GetProperty("cause").GetString(), StringComparison.Ordinal);
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains("scope-sources-identity-mismatch", output, StringComparison.Ordinal);
            Assert.Contains("claim-verification-invalid", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_NonregularLocalClaimAfterLookupIsStructuredRefusal(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        var claimPath = Path.Combine(workspace.ClaimsDirectory,
            ClaimCommand.ClaimPath($"execution-unit:{Unit}").Split('/').Last());
        workspace.CaptureDurableBaseline();
        var checker = new CallbackExistingIssueChecker(
            new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            () =>
            {
                File.Delete(claimPath);
                Directory.CreateDirectory(claimPath);
                workspace.CaptureDurableBaseline();
            });
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8684");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true,
            team: UndeclaredTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.True(Directory.Exists(claimPath));
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("scope-sources-identity-mismatch", sources.GetProperty("cause").GetString());
            Assert.Contains("claim-verification-invalid",
                sources.GetProperty("diagnostics")[0].GetProperty("cause").GetString(), StringComparison.Ordinal);
            Assert.Contains("not a regular file",
                sources.GetProperty("diagnostics")[0].GetProperty("detail").GetString(), StringComparison.Ordinal);
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains("scope-sources-identity-mismatch", output, StringComparison.Ordinal);
            Assert.Contains("claim-verification-invalid", output, StringComparison.Ordinal);
            Assert.Contains("not a regular file", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_DeclaredPacketRemovedAfterGateRetainsUnavailableDiagnostic(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        var removedPath = Path.Combine(workspace.PacketDirectory(Unit), "implementation.md");
        var hookReached = false;
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8643");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.AfterGateHook = () =>
        {
            hookReached = true;
            File.Delete(removedPath);
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

        Assert.Equal(1, exit);
        Assert.True(hookReached);
        Assert.False(File.Exists(removedPath));
        Assert.Equal(0, creator.CallCount);
        Assert.True(File.Exists(source.ArtifactPath));
        AssertDurableStateUntouched(workspace);
        var relativePath = $".intent-cli/issues/{Unit}/implementation.md";
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.PacketMissing, result.RootElement.GetProperty("cause").GetString());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("unavailable", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-packet-unavailable", sources.GetProperty("cause").GetString());
            var diagnostic = Assert.Single(sources.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(relativePath, diagnostic.GetProperty("path").GetString());
            Assert.Contains("after the review gate", diagnostic.GetProperty("detail").GetString(), StringComparison.Ordinal);
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains(CrossRuntimeReviewCauses.PacketMissing, output, StringComparison.Ordinal);
            Assert.Contains("scope-sources-packet-unavailable", output, StringComparison.Ordinal);
            Assert.Contains(relativePath, output, StringComparison.Ordinal);
            Assert.Contains("after the review gate", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"unavailable\"", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_DeclaredPacketReadFailureAfterGateRetainsUnavailableDiagnostic(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo, Team);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        var implementationPath = Path.Combine(workspace.PacketDirectory(Unit), "implementation.md");
        FileStream? exclusiveReadLock = null;
        var hookReached = false;
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8644");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.AfterGateHook = () =>
        {
            hookReached = true;
            exclusiveReadLock = new FileStream(implementationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        };

        try
        {
            var (exit, output) = Run(workspace, Unit, Repo, write: true, team: Team, format: format);

            Assert.Equal(1, exit);
            Assert.True(hookReached);
            Assert.NotNull(exclusiveReadLock);
            Assert.Equal(0, creator.CallCount);
            Assert.True(File.Exists(source.ArtifactPath));
            AssertDurableStateUntouched(workspace);
            var relativePath = $".intent-cli/issues/{Unit}/implementation.md";
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                Assert.Equal(CrossRuntimeReviewCauses.PacketMissing, result.RootElement.GetProperty("cause").GetString());
                Assert.StartsWith("packet file is missing before create:", result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
                var sources = result.RootElement.GetProperty("scope_sources");
                Assert.Equal("unavailable", sources.GetProperty("state").GetString());
                Assert.Equal("scope-sources-packet-unavailable", sources.GetProperty("cause").GetString());
                var diagnostic = Assert.Single(sources.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(relativePath, diagnostic.GetProperty("path").GetString());
                Assert.Contains("after the review gate", diagnostic.GetProperty("detail").GetString(), StringComparison.Ordinal);
                AssertNullOrMissing(sources, "provenance");
                AssertNullOrMissing(sources, "expected_provenance_block");
            }
            else
            {
                Assert.Contains(CrossRuntimeReviewCauses.PacketMissing, output, StringComparison.Ordinal);
                Assert.Contains("packet file is missing before create:", output, StringComparison.Ordinal);
                Assert.Contains("scope-sources-packet-unavailable", output, StringComparison.Ordinal);
                Assert.Contains(relativePath, output, StringComparison.Ordinal);
                Assert.Contains("after the review gate", output, StringComparison.Ordinal);
                Assert.Contains("\"state\": \"unavailable\"", output, StringComparison.Ordinal);
            }
        }
        finally
        {
            exclusiveReadLock?.Dispose();
            IssuePublishFlowCommand.AfterGateHook = null;
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_OrdinaryCreatePacketReadFailureAtFinalRecheckReturnsUnavailable(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();
        var implementationPath = Path.Combine(workspace.PacketDirectory(Unit), "implementation.md");
        FileStream? exclusiveReadLock = null;
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creatorFactories = 0;
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8645");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
        {
            exclusiveReadLock = new FileStream(implementationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return checker;
        };
        IssuePublishFlowCommand.CreatorFactory = () => { creatorFactories++; return creator; };

        try
        {
            var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

            Assert.Equal(1, exit);
            Assert.NotNull(exclusiveReadLock);
            Assert.Equal(1, checker.CallCount);
            Assert.Equal(1, creatorFactories);
            Assert.Equal(0, creator.CallCount);
            AssertDurableStateUntouched(workspace);
            AssertLatePacketReadFailure(output, format);
        }
        finally
        {
            exclusiveReadLock?.Dispose();
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_LocalExistingPacketReadFailureAtRecheckReturnsUnavailable(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), OtherRepo, 8646,
            $"https://github.com/{OtherRepo}/issues/8646");
        workspace.CaptureDurableBaseline();
        var implementationPath = Path.Combine(workspace.PacketDirectory(Unit), "implementation.md");
        FileStream? exclusiveReadLock = null;
        var clockCalls = 0;
        var creatorFactories = 0;
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8646");
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            clockCalls++;
            if (clockCalls == 2)
            {
                exclusiveReadLock = new FileStream(implementationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            return new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        };
        IssuePublishFlowCommand.CreatorFactory = () => { creatorFactories++; return creator; };

        try
        {
            var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

            Assert.Equal(1, exit);
            Assert.Equal(2, clockCalls);
            Assert.NotNull(exclusiveReadLock);
            Assert.Equal(0, creatorFactories);
            Assert.Equal(0, creator.CallCount);
            AssertDurableStateUntouched(workspace);
            AssertLatePacketReadFailure(output, format);
        }
        finally
        {
            exclusiveReadLock?.Dispose();
        }
    }

    private static void AssertLatePacketReadFailure(string output, string format)
    {
        const string relativePath = ".intent-cli/issues/G835PF/implementation.md";
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal("scope-sources-packet-unavailable", result.RootElement.GetProperty("cause").GetString());
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("unavailable", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-packet-unavailable", sources.GetProperty("cause").GetString());
            var diagnostic = Assert.Single(sources.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(relativePath, diagnostic.GetProperty("path").GetString());
            Assert.Contains("unreadable during source recheck", diagnostic.GetProperty("detail").GetString(), StringComparison.Ordinal);
            AssertNullOrMissing(sources, "provenance");
            AssertNullOrMissing(sources, "expected_provenance_block");
        }
        else
        {
            Assert.Contains("scope-sources-packet-unavailable", output, StringComparison.Ordinal);
            Assert.Contains(relativePath, output, StringComparison.Ordinal);
            Assert.Contains("unreadable during source recheck", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"unavailable\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_PinnedSourceCreatePublishesExactAuthoredBody(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);

        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8630");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, Repo, write: true, format: format);
        Assert.Equal(0, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(1, creator.CallCount);
        Assert.Equal(source.BodyBytes, creator.LastBodyBytes);
        Assert.Equal(File.ReadAllBytes(workspace.GithubBodyPath(Unit)), creator.LastBodyBytes);

        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("created").GetBoolean());
            var sourceResult = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("satisfied", sourceResult.GetProperty("state").GetString());
            Assert.Equal("not-verified", sourceResult.GetProperty("publication").GetString());
            Assert.Equal(source.Digest, sourceResult.GetProperty("provenance")[0].GetProperty("sha256").GetString());
        }
        else
        {
            Assert.Contains("- created: yes", output, StringComparison.Ordinal);
            Assert.Contains("## scope_sources", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"satisfied\"", output, StringComparison.Ordinal);
            Assert.Contains("\"publication\": \"not-verified\"", output, StringComparison.Ordinal);
            Assert.Contains(source.Digest, output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_InvalidPinnedSourceRefusesBeforeGitHubAndDurableWrites(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var source = workspace.WritePinnedSourcePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var validPacket = File.ReadAllBytes(packetPath);
        var badPacket = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(validPacket)
            .Replace(source.Digest, new string('0', 64), StringComparison.Ordinal));
        File.WriteAllBytes(packetPath, badPacket);
        var bodyBytes = File.ReadAllBytes(workspace.GithubBodyPath(Unit));
        workspace.CaptureDurableBaseline();

        var checkerFactoryCalls = 0;
        var creatorFactoryCalls = 0;
        var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8631");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
        {
            checkerFactoryCalls++;
            return defaultChecker;
        };
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            creatorFactoryCalls++;
            return creator;
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(0, checkerFactoryCalls);
        Assert.Equal(0, creatorFactoryCalls);
        Assert.Equal(0, creator.CallCount);
        Assert.Equal(badPacket, File.ReadAllBytes(packetPath));
        Assert.Equal(bodyBytes, File.ReadAllBytes(workspace.GithubBodyPath(Unit)));
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            Assert.Equal("scope-sources-digest-mismatch", result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
            Assert.Contains("scope-sources-digest-mismatch", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("repo", "json")]
    [InlineData("repo", "markdown")]
    [InlineData("domain", "markdown")]
    [InlineData("domain", "json")]
    [InlineData("team", "json")]
    [InlineData("team", "markdown")]
    [InlineData("config-default", "markdown")]
    [InlineData("config-default", "json")]
    public void PublishFlow_OptedInSourceIdentityMustMatchExplicitOrConfiguredPublishScope(string mismatch, string format)
    {
        var heldTeam = mismatch == "team" ? Team : UndeclaredTeam;
        var sourceTeam = mismatch == "team" ? UndeclaredTeam : heldTeam;
        var workspace = new G835PublishFlowWorkspace(declare: mismatch == "team", heldTeam: heldTeam,
            configuredDomain: mismatch == "config-default" ? "foreign-domain" : Domain);
        using (workspace)
        {
            var source = workspace.WritePinnedSourcePacket(Unit, Repo, sourceTeam);
            workspace.SeedQueueState(Unit, Title());
            workspace.CaptureDurableBaseline();
            var sourceBytes = File.ReadAllBytes(source.ArtifactPath);
            var checkerFactories = 0;
            var creatorFactories = 0;
            var creator = new RecordingIssueCreator($"https://github.com/{Repo}/issues/8647");
            IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            {
                checkerFactories++;
                return defaultChecker;
            };
            IssuePublishFlowCommand.CreatorFactory = () =>
            {
                creatorFactories++;
                return creator;
            };

            var (exit, output) = mismatch switch
            {
                "repo" => Run(workspace, Unit, OtherRepo, write: true, team: heldTeam, format: format),
                "domain" => Run(workspace, Unit, Repo, write: true, team: heldTeam, domainOverride: "foreign-domain", format: format),
                "team" => Run(workspace, Unit, Repo, write: true, team: Team, format: format),
                "config-default" => RunWithoutDomain(workspace, Unit, Repo, write: true, team: heldTeam, format: format),
                _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
            };

            Assert.Equal(1, exit);
            Assert.Equal(0, checkerFactories);
            Assert.Equal(0, creatorFactories);
            Assert.Equal(0, creator.CallCount);
            Assert.Equal(sourceBytes, File.ReadAllBytes(source.ArtifactPath));
            AssertDurableStateUntouched(workspace);
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                Assert.False(result.RootElement.GetProperty("created").GetBoolean());
                var sources = result.RootElement.GetProperty("scope_sources");
                Assert.Equal("refused", sources.GetProperty("state").GetString());
                Assert.Equal("scope-sources-identity-mismatch", sources.GetProperty("cause").GetString());
                AssertNullOrMissing(sources, "provenance");
                AssertNullOrMissing(sources, "expected_provenance_block");
            }
            else
            {
                Assert.Contains("scope-sources-identity-mismatch", output, StringComparison.Ordinal);
                Assert.Contains("- created: no", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [MemberData(nameof(S08NegativeCases))]
    public void PublishFlow_InvalidPinnedSourceBlocksPreviewAndWriteAcrossIndependentAxes(
        string scenario, bool declared, bool gated, bool write, string format)
    {
        var sourceTeam = declared ? Team : UndeclaredTeam;
        var targetRepo = gated ? Repo : OtherRepo;
        using var workspace = new G835PublishFlowWorkspace(declare: declared, heldTeam: sourceTeam,
            extraTeams: S08ExtraTeams(declared, gated));
        var source = workspace.WritePinnedSourcePacket(Unit, targetRepo, sourceTeam);
        if (scenario == "missing" && !declared && gated && !write)
            workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), targetRepo, 42, $"https://github.com/{targetRepo}/issues/42");
        else
            workspace.SeedQueueState(Unit, Title());
        if (declared && gated) workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);

        var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var bodyPath = workspace.GithubBodyPath(Unit);
        switch (scenario)
        {
            case "missing": File.Delete(source.ArtifactPath); break;
            case "inactive": workspace.WriteSuccessorRuling(Unit, targetRepo, sourceTeam, "R-G863-PUBLISH"); break;
            case "malformed": File.WriteAllText(source.ArtifactPath, "{\"schema_version\":\"1\"}\n"); break;
            case "digest": RewriteRulingDecision(source.ArtifactPath); break;
            case "provenance":
                File.WriteAllText(bodyPath, File.ReadAllText(bodyPath).Replace(
                    "Authority: supplied-not-authenticated.", "Authority: authenticated.", StringComparison.Ordinal));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var expectedCause = scenario switch
        {
            "missing" => "scope-sources-ruling-missing",
            "inactive" => "scope-sources-ruling-inactive",
            "malformed" => "scope-sources-ruling-unavailable",
            "digest" => "scope-sources-digest-mismatch",
            "provenance" => "scope-sources-provenance-mismatch",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var changedArtifactBytes = File.Exists(source.ArtifactPath) ? File.ReadAllBytes(source.ArtifactPath) : null;
        var changedPacketBytes = File.ReadAllBytes(packetPath);
        var changedBodyBytes = File.ReadAllBytes(bodyPath);
        workspace.CaptureDurableBaseline();
        workspace.InitializeGitAndCapture();
        var initialClaim = ClaimOwnershipVerifier.Verify(
            workspace.RootPath, $"execution-unit:{Unit}", sourceTeam);
        Assert.True(initialClaim.Passed, initialClaim.Detail);
        Assert.True(initialClaim.StoreConfigured, initialClaim.Detail);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, initialClaim.Status);
        var gitBefore = workspace.CaptureGitState();

        var checkerFactoryCalls = 0;
        var creatorFactoryCalls = 0;
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{targetRepo}/issues/8640");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => { checkerFactoryCalls++; return checker; };
        IssuePublishFlowCommand.CreatorFactory = () => { creatorFactoryCalls++; return creator; };

        var (exit, output) = Run(workspace, Unit, targetRepo, write, team: sourceTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(0, checkerFactoryCalls);
        Assert.Equal(0, checker.CallCount);
        Assert.Equal(0, creatorFactoryCalls);
        Assert.Equal(0, creator.CallCount);
        Assert.Equal(changedArtifactBytes, File.Exists(source.ArtifactPath) ? File.ReadAllBytes(source.ArtifactPath) : null);
        Assert.Equal(changedPacketBytes, File.ReadAllBytes(packetPath));
        Assert.Equal(changedBodyBytes, File.ReadAllBytes(bodyPath));
        AssertDurableStateUntouched(workspace);
        workspace.AssertGitStateUnchanged(gitBefore);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal(expectedCause, sources.GetProperty("cause").GetString());
            Assert.Equal(scenario == "malformed" ? "unavailable" : "refused", sources.GetProperty("state").GetString());
            if (scenario == "provenance")
            {
                var provenance = Assert.Single(sources.GetProperty("provenance").EnumerateArray());
                Assert.Equal(source.Digest, provenance.GetProperty("sha256").GetString());
                Assert.Equal(Path.GetRelativePath(workspace.RootPath, source.ArtifactPath).Replace('\\', '/'),
                    provenance.GetProperty("path").GetString());
                Assert.False(string.IsNullOrWhiteSpace(sources.GetProperty("expected_provenance_block").GetString()));
            }
            else
            {
                AssertNullOrMissing(sources, "provenance");
            }
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
            Assert.Contains(expectedCause, output, StringComparison.Ordinal);
            Assert.Contains($"\"state\": \"{(scenario == "malformed" ? "unavailable" : "refused")}\"", output, StringComparison.Ordinal);
            if (scenario == "provenance")
            {
                Assert.Contains(source.Digest, output, StringComparison.Ordinal);
                Assert.Contains(Path.GetRelativePath(workspace.RootPath, source.ArtifactPath).Replace('\\', '/'),
                    output, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [MemberData(nameof(S08PositiveCases))]
    public void PublishFlow_ValidPinnedSourceWorksAcrossIndependentAxes(bool declared, bool gated, bool write, string format)
    {
        var sourceTeam = declared ? Team : UndeclaredTeam;
        var targetRepo = gated ? Repo : OtherRepo;
        using var workspace = new G835PublishFlowWorkspace(declare: declared, heldTeam: sourceTeam,
            extraTeams: S08ExtraTeams(declared, gated));
        var source = workspace.WritePinnedSourcePacket(Unit, targetRepo, sourceTeam);
        workspace.SeedQueueState(Unit, Title());
        if (declared && gated) workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        workspace.InitializeGitAndCapture();
        var initialClaim = ClaimOwnershipVerifier.Verify(
            workspace.RootPath, $"execution-unit:{Unit}", sourceTeam);
        Assert.True(initialClaim.Passed, initialClaim.Detail);
        Assert.True(initialClaim.StoreConfigured, initialClaim.Detail);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, initialClaim.Status);
        var gitBefore = workspace.CaptureGitState();
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{targetRepo}/issues/8680");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, targetRepo, write, team: sourceTeam, format: format);

        Assert.Equal(0, exit);
        Assert.Equal(write ? 1 : 0, creator.CallCount);
        Assert.Equal(write ? 1 : 0, checker.CallCount);
        workspace.AssertGitStateUnchanged(gitBefore);
        if (!write) AssertDurableStateUntouched(workspace);
        if (write) Assert.Equal(source.BodyBytes, creator.LastBodyBytes);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal(write, result.RootElement.GetProperty("created").GetBoolean());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("satisfied", sources.GetProperty("state").GetString());
            Assert.Equal("not-verified", sources.GetProperty("publication").GetString());
        }
        else
        {
            Assert.Contains(write ? "- created: yes" : "- created: no", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"satisfied\"", output, StringComparison.Ordinal);
            Assert.Contains("\"publication\": \"not-verified\"", output, StringComparison.Ordinal);
        }
    }

    private static CrossRuntimeReviewTeamDeclaration[]? S08ExtraTeams(bool declared, bool gated) =>
        !declared && gated
            ? [new CrossRuntimeReviewTeamDeclaration
            {
                Team = "foreign-domain/unrelated-team",
                ConductorRuntime = "claude",
                Repos = [Repo],
            }]
            : null;

    [Theory]
    [InlineData("missing", true, "json")]
    [InlineData("missing", false, "markdown")]
    [InlineData("inactive", true, "markdown")]
    [InlineData("inactive", false, "json")]
    [InlineData("malformed", true, "json")]
    [InlineData("malformed", false, "markdown")]
    [InlineData("provenance", true, "markdown")]
    [InlineData("provenance", false, "json")]
    public void PublishFlow_InitialMissingInactiveMalformedAndBodyFailuresStopBeforeFactories(string scenario,
        bool declared, string format)
    {
        var sourceTeam = declared ? Team : UndeclaredTeam;
        var targetRepo = declared ? Repo : OtherRepo;
        using var workspace = new G835PublishFlowWorkspace(declare: declared, heldTeam: sourceTeam);
        var recordAt = scenario == "inactive" ? DateTimeOffset.UtcNow.AddMinutes(-5) : (DateTimeOffset?)null;
        TimeSpan? expiry = scenario == "inactive" ? TimeSpan.FromMinutes(30) : null;
        var source = workspace.WritePinnedSourcePacket(Unit, targetRepo, sourceTeam, expiry, recordAt);
        workspace.SeedQueueState(Unit, Title());
        if (declared) workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        var expectedCause = scenario switch
        {
            "missing" => "scope-sources-ruling-missing",
            "inactive" => "scope-sources-ruling-inactive",
            "malformed" => "scope-sources-ruling-unavailable",
            "provenance" => "scope-sources-provenance-mismatch",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        switch (scenario)
        {
            case "missing": File.Delete(source.ArtifactPath); break;
            case "malformed": File.WriteAllText(source.ArtifactPath, "{\"schema_version\":\"1\"}\n"); break;
            case "provenance":
                File.WriteAllText(workspace.GithubBodyPath(Unit),
                    File.ReadAllText(workspace.GithubBodyPath(Unit)).Replace("Authority: supplied-not-authenticated.", "Authority: operator-authenticated.", StringComparison.Ordinal));
                break;
        }
        IssuePublishFlowCommand.UtcNowFactory = () => recordAt is { } activeRecordAt
            ? activeRecordAt.AddMinutes(30)
            : new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        workspace.CaptureDurableBaseline();

        var checkerFactories = 0;
        var creatorFactories = 0;
        var creator = new RecordingIssueCreator($"https://github.com/{targetRepo}/issues/8643");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => { checkerFactories++; return defaultChecker; };
        IssuePublishFlowCommand.CreatorFactory = () => { creatorFactories++; return creator; };

        var (exit, output) = Run(workspace, Unit, targetRepo, write: true, team: sourceTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(0, checkerFactories);
        Assert.Equal(0, creatorFactories);
        Assert.Equal(0, creator.CallCount);
        workspace.AssertDurableBaselineUntouched(Unit);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal(expectedCause, sources.GetProperty("cause").GetString());
            Assert.Equal(scenario == "malformed" ? "unavailable" : "refused", sources.GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains(expectedCause, output, StringComparison.Ordinal);
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("missing", "json")]
    [InlineData("missing", "markdown")]
    [InlineData("successor", "json")]
    [InlineData("successor", "markdown")]
    [InlineData("tampered", "json")]
    [InlineData("tampered", "markdown")]
    [InlineData("pin", "json")]
    [InlineData("pin", "markdown")]
    public void PublishFlow_NoneFoundLookupRechecksMissingSuccessorPinAndTamperedSources(string mutation, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        var source = workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();
        var checker = new CallbackExistingIssueChecker(
            new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            () =>
            {
                switch (mutation)
                {
                    case "missing":
                        File.Delete(source.ArtifactPath);
                        break;
                    case "successor":
                        workspace.WriteSuccessorRuling(Unit, OtherRepo, UndeclaredTeam, "R-G863-PUBLISH");
                        break;
                    case "tampered":
                        RewriteRulingDecision(source.ArtifactPath);
                        break;
                    case "pin":
                        var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
                        File.WriteAllText(packetPath, File.ReadAllText(packetPath)
                            .Replace(source.Digest, new string('0', 64), StringComparison.Ordinal));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            });
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8641");
        var creatorFactoryCalls = 0;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => { creatorFactoryCalls++; return creator; };

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        // The command constructs the creator after lookup but still rechecks the
        // captured source before calling CreateIssue; this seam is not the mutation.
        Assert.Equal(1, creatorFactoryCalls);
        Assert.Equal(0, creator.CallCount);
        AssertDurableStateUntouched(workspace);
        var expectedCause = mutation switch
        {
            "missing" => "scope-sources-ruling-missing",
            "successor" => "scope-sources-ruling-inactive",
            "tampered" => "scope-sources-digest-mismatch",
            "pin" => "scope-sources-changed",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            Assert.Equal(expectedCause, result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains(expectedCause, output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, "json")]
    [InlineData(false, "markdown")]
    public void PublishFlow_ExpiryAtFinalRecheckRefusesDeclaredAndOrdinaryCreate(bool declared, string format)
    {
        var sourceTeam = declared ? Team : UndeclaredTeam;
        var targetRepo = declared ? Repo : OtherRepo;
        using var workspace = new G835PublishFlowWorkspace(declare: declared, heldTeam: sourceTeam);
        var recordAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var source = workspace.WritePinnedSourcePacket(Unit, targetRepo, sourceTeam,
            expiresAfter: TimeSpan.FromMinutes(30), recordedAt: recordAt);
        workspace.SeedQueueState(Unit, Title());
        if (declared) workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        var boundaryReached = false;
        var beforeExpiry = recordAt.AddMinutes(2);
        var expiry = recordAt.AddMinutes(30);
        var clockReads = 0;
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            clockReads++;
            return boundaryReached ? expiry : beforeExpiry;
        };
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{targetRepo}/issues/8642");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            if (!declared) boundaryReached = true;
            return creator;
        };
        if (declared) IssuePublishFlowCommand.AfterGateHook = () => boundaryReached = true;

        var (exit, output) = Run(workspace, Unit, targetRepo, write: true, team: sourceTeam, format: format);

        Assert.Equal(1, exit);
        Assert.True(boundaryReached);
        Assert.True(clockReads >= 2);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        AssertDurableStateUntouched(workspace);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            Assert.Equal("scope-sources-ruling-inactive", result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
            Assert.Contains("expired", result.RootElement.GetProperty("scope_sources").GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("scope-sources-ruling-inactive", output, StringComparison.Ordinal);
            Assert.Contains("expired", output, StringComparison.Ordinal);
        }
        Assert.True(File.Exists(source.ArtifactPath));
    }

    [Theory]
    [InlineData("missing", "json")]
    [InlineData("missing", "markdown")]
    [InlineData("successor", "json")]
    [InlineData("successor", "markdown")]
    [InlineData("tampered", "json")]
    [InlineData("tampered", "markdown")]
    [InlineData("pin", "json")]
    [InlineData("pin", "markdown")]
    public void PublishFlow_GitHubUniqueRestoreRechecksMissingSuccessorPinAndTamperedSources(string mutation, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false);
        var source = workspace.WritePinnedSourcePacket(Unit, OtherRepo);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();
        var checker = new CallbackExistingIssueChecker(new GitHubExistingIssueLookupResult
        {
            Classification = GitHubExistingIssueClassification.Unique,
            IssueNumber = 8632,
            IssueUrl = $"https://github.com/{OtherRepo}/issues/8632",
        }, () =>
        {
            switch (mutation)
            {
                case "missing":
                    File.Delete(source.ArtifactPath);
                    break;
                case "successor":
                    workspace.WriteSuccessorRuling(Unit, OtherRepo, Team, "R-G863-PUBLISH");
                    break;
                case "pin":
                    var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
                    File.WriteAllText(packetPath, File.ReadAllText(packetPath)
                        .Replace(source.Digest, new string('0', 64), StringComparison.Ordinal));
                    break;
                case "tampered":
                    RewriteRulingDecision(source.ArtifactPath);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        });
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8633");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        var expectedCause = mutation switch
        {
            "missing" => "scope-sources-ruling-missing",
            "successor" => "scope-sources-ruling-inactive",
            "pin" => "scope-sources-changed",
            "tampered" => "scope-sources-digest-mismatch",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            Assert.Equal(expectedCause, result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
            Assert.Contains(expectedCause, output, StringComparison.Ordinal);
        }
        AssertDurableStateUntouched(workspace);
    }

    [Theory]
    [InlineData("missing", "json")]
    [InlineData("missing", "markdown")]
    [InlineData("successor", "json")]
    [InlineData("successor", "markdown")]
    [InlineData("tampered", "json")]
    [InlineData("tampered", "markdown")]
    [InlineData("pin", "json")]
    [InlineData("pin", "markdown")]
    public void PublishFlow_LocalExistingIssueRechecksMissingSuccessorPinAndTamperedRulingAtSnapshot(string mutation, string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        var source = workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam);
        var initialRulingBytes = File.ReadAllBytes(source.ArtifactPath);
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), OtherRepo, 8634,
            $"https://github.com/{OtherRepo}/issues/8634");
        workspace.CaptureDurableBaseline();
        var sourceRecheckCalls = 0;
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8634");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        IssuePublishFlowCommand.UtcNowFactory = () =>
        {
            sourceRecheckCalls++;
            if (sourceRecheckCalls == 2)
            {
                switch (mutation)
                {
                    case "missing":
                        File.Delete(source.ArtifactPath);
                        break;
                    case "successor":
                        workspace.WriteSuccessorRuling(Unit, OtherRepo, UndeclaredTeam, "R-G863-PUBLISH");
                        break;
                    case "tampered":
                        RewriteRulingDecision(source.ArtifactPath);
                        break;
                    case "pin":
                        var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
                        File.WriteAllText(packetPath, File.ReadAllText(packetPath)
                            .Replace(source.Digest, new string('0', 64), StringComparison.Ordinal));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            }
            return new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        };

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(2, sourceRecheckCalls);
        Assert.Equal(0, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        if (mutation == "tampered") Assert.NotEqual(initialRulingBytes, File.ReadAllBytes(source.ArtifactPath));
        var expectedCause = mutation switch
        {
            "missing" => "scope-sources-ruling-missing",
            "successor" => "scope-sources-ruling-inactive",
            "tampered" => "scope-sources-digest-mismatch",
            "pin" => "scope-sources-changed",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            Assert.Equal(expectedCause, result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
            Assert.Contains(expectedCause, output, StringComparison.Ordinal);
        }
        AssertDurableStateUntouched(workspace);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_LocalExistingIssueRerunRechecksPinnedRulingAtInclusiveExpiry(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        var source = workspace.WritePinnedSourcePacket(Unit, OtherRepo, UndeclaredTeam, expiresAfter: TimeSpan.FromMinutes(30));
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), OtherRepo, 8644, $"https://github.com/{OtherRepo}/issues/8644");
        workspace.CaptureDurableBaseline();

        var clockCalls = 0;
        var beforeExpiry = new DateTimeOffset(2026, 10, 10, 11, 15, 0, TimeSpan.Zero);
        var expiry = new DateTimeOffset(2026, 10, 10, 11, 30, 0, TimeSpan.Zero);
        IssuePublishFlowCommand.UtcNowFactory = () => ++clockCalls == 1 ? beforeExpiry : expiry;
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8645");
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;
        var sourceBytes = File.ReadAllBytes(source.ArtifactPath);

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, team: UndeclaredTeam, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(2, clockCalls);
        Assert.Equal(0, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.Equal(sourceBytes, File.ReadAllBytes(source.ArtifactPath));
        Assert.False(File.Exists(workspace.PublishYamlPath(Unit)));
        workspace.AssertDurableBaselineUntouched(Unit);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("refused", sources.GetProperty("state").GetString());
            Assert.Equal("scope-sources-ruling-inactive", sources.GetProperty("cause").GetString());
            Assert.Contains("expired", sources.GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.Equal(RulingArtifact.FormatTimestamp(expiry), sources.GetProperty("evaluated_at").GetString());
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
            Assert.Contains("scope-sources-ruling-inactive", output, StringComparison.Ordinal);
            Assert.Contains("expired", output, StringComparison.Ordinal);
            Assert.Contains(RulingArtifact.FormatTimestamp(expiry), output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("missing", true, "json")]
    [InlineData("missing", false, "markdown")]
    [InlineData("successor", true, "markdown")]
    [InlineData("successor", false, "json")]
    [InlineData("ref", true, "json")]
    [InlineData("ref", false, "markdown")]
    [InlineData("tampered", true, "json")]
    [InlineData("tampered", false, "markdown")]
    [InlineData("pin", true, "markdown")]
    [InlineData("pin", false, "json")]
    [InlineData("body", true, "json")]
    [InlineData("body", false, "markdown")]
    public void PublishFlow_FinalCreateRecheckRejectsReachedSourceMutationAcrossModesAndFormats(
        string mutation, bool declared, string format)
    {
        var sourceTeam = declared ? Team : UndeclaredTeam;
        var targetRepo = declared ? Repo : OtherRepo;
        using var workspace = new G835PublishFlowWorkspace(declare: declared, heldTeam: sourceTeam);
        var source = workspace.WritePinnedSourcePacket(Unit, targetRepo, sourceTeam);
        workspace.SeedQueueState(Unit, Title());
        if (declared) workspace.RecordSatisfiedDesignReviewsViaCommand(Unit);
        workspace.CaptureDurableBaseline();
        workspace.InitializeGitAndCapture();
        var initialClaim = ClaimOwnershipVerifier.Verify(workspace.RootPath,
            $"execution-unit:{Unit}", sourceTeam);
        Assert.True(initialClaim.Passed, initialClaim.Detail);
        var gitBefore = workspace.CaptureGitState();
        var initialRulingBytes = File.ReadAllBytes(source.ArtifactPath);
        var initialPacketBytes = File.ReadAllBytes(Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml"));
        var initialBodyBytes = File.ReadAllBytes(workspace.GithubBodyPath(Unit));
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{targetRepo}/issues/8646");
        var callbackReached = false;
        var creatorFactoryCalls = 0;
        IssuePublishFlowCommand.UtcNowFactory = () => new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;

        void MutateAtFinalBoundary()
        {
            callbackReached = true;
            switch (mutation)
            {
                case "missing":
                    File.Delete(source.ArtifactPath);
                    break;
                case "successor":
                    workspace.WriteSuccessorRuling(Unit, targetRepo, sourceTeam, "R-G863-PUBLISH");
                    break;
                case "tampered":
                    RewriteRulingDecision(source.ArtifactPath);
                    break;
                case "pin":
                    var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
                    File.WriteAllText(packetPath, File.ReadAllText(packetPath)
                        .Replace(source.Digest, new string('0', 64), StringComparison.Ordinal));
                    break;
                case "ref":
                    var refPacketPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
                    File.WriteAllText(refPacketPath, File.ReadAllText(refPacketPath)
                        .Replace("R-G863-PUBLISH", "R-G863-OTHER", StringComparison.Ordinal));
                    break;
                case "body":
                    File.AppendAllText(workspace.GithubBodyPath(Unit), "\nchanged at the final create boundary\n");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }

        if (declared)
        {
            IssuePublishFlowCommand.CreatorFactory = () => { creatorFactoryCalls++; return creator; };
            IssuePublishFlowCommand.AfterGateHook = MutateAtFinalBoundary;
        }
        else
        {
            IssuePublishFlowCommand.CreatorFactory = () =>
            {
                creatorFactoryCalls++;
                MutateAtFinalBoundary();
                return creator;
            };
        }

        var (exit, output) = Run(workspace, Unit, targetRepo, write: true, team: sourceTeam, format: format);

        Assert.Equal(1, exit);
        Assert.True(callbackReached);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(1, creatorFactoryCalls);
        Assert.Equal(0, creator.CallCount);
        AssertDurableStateUntouched(workspace);
        workspace.AssertGitStateUnchanged(gitBefore);
        if (mutation == "missing") Assert.False(File.Exists(source.ArtifactPath));
        if (mutation == "tampered") Assert.NotEqual(initialRulingBytes, File.ReadAllBytes(source.ArtifactPath));
        if (mutation == "pin") Assert.NotEqual(initialPacketBytes, File.ReadAllBytes(Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml")));
        if (mutation == "body") Assert.NotEqual(initialBodyBytes, File.ReadAllBytes(workspace.GithubBodyPath(Unit)));

        var expectedSourceCause = mutation switch
        {
            "missing" => "scope-sources-ruling-missing",
            "successor" => "scope-sources-ruling-inactive",
            "tampered" => "scope-sources-digest-mismatch",
            "pin" or "body" or "ref" when declared => CrossRuntimeReviewCauses.DigestStale,
            "pin" or "body" or "ref" => "scope-sources-changed",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.False(result.RootElement.GetProperty("idempotent").GetBoolean());
            Assert.False(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
            if (expectedSourceCause == CrossRuntimeReviewCauses.DigestStale)
            {
                Assert.Equal(expectedSourceCause, result.RootElement.GetProperty("cause").GetString());
                var nestedSources = result.RootElement.GetProperty("scope_sources");
                Assert.Equal("refused", nestedSources.GetProperty("state").GetString());
                Assert.Equal("scope-sources-changed", nestedSources.GetProperty("cause").GetString());
                Assert.False(nestedSources.TryGetProperty("provenance", out _));
                Assert.False(nestedSources.TryGetProperty("expected_provenance_block", out _));
            }
            else
            {
                Assert.Equal("refused", result.RootElement.GetProperty("scope_sources").GetProperty("state").GetString());
                Assert.Equal(expectedSourceCause, result.RootElement.GetProperty("scope_sources").GetProperty("cause").GetString());
            }
        }
        else
        {
            Assert.Contains("- created: no", output, StringComparison.Ordinal);
            Assert.Contains("- idempotent: no", output, StringComparison.Ordinal);
            Assert.Contains("- durable_state_synced: no", output, StringComparison.Ordinal);
            Assert.Contains(expectedSourceCause, output, StringComparison.Ordinal);
            if (expectedSourceCause == CrossRuntimeReviewCauses.DigestStale)
            {
                Assert.Contains("\"state\": \"refused\"", output, StringComparison.Ordinal);
                Assert.Contains("\"cause\": \"scope-sources-changed\"", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Verified sources", output, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PublishFlow_OrdinaryCreateClearsStaleSourceProvenanceAfterCreatorFactoryChangesPacket(string format)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false);
        workspace.WritePinnedSourcePacket(Unit, OtherRepo);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();
        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new RecordingIssueCreator($"https://github.com/{OtherRepo}/issues/8634");
        var creatorFactoryCalls = 0;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            creatorFactoryCalls++;
            File.AppendAllText(Path.Combine(workspace.PacketDirectory(Unit), "implementation.md"), "\nchanged during factory\n");
            return creator;
        };

        var (exit, output) = Run(workspace, Unit, OtherRepo, write: true, format: format);

        Assert.Equal(1, exit);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(1, creatorFactoryCalls);
        Assert.Equal(0, creator.CallCount);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            var sources = result.RootElement.GetProperty("scope_sources");
            Assert.Equal("scope-sources-changed", sources.GetProperty("cause").GetString());
            Assert.Equal("refused", sources.GetProperty("state").GetString());
            Assert.False(sources.TryGetProperty("provenance", out _));
            Assert.False(sources.TryGetProperty("expected_provenance_block", out _));
        }
        else
        {
            Assert.Contains("scope-sources-changed", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\"provenance\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\"expected_provenance_block\"", output, StringComparison.Ordinal);
        }
        AssertDurableStateUntouched(workspace);
    }

    private static void RewriteRulingDecision(string artifactPath)
    {
        var bytes = File.ReadAllBytes(artifactPath);
        Assert.True(RulingArtifact.TryParse(bytes, true, out var record, out var cause, out var detail), $"{cause}: {detail}");
        File.WriteAllBytes(artifactPath, RulingArtifact.Serialize(record! with { Decision = "Changed by an existing test hook." }));
    }

    // ── resolution ─────────────────────────────────────────────────────

    [Fact]
    public void PublishFlow_Resolution_UsesPacketDomain_NotDomainFlag()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo, domain: Domain);
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false, domainOverride: "sekiban");
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var review = result.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal("intent-cli-dev", review.GetProperty("team").GetString());
        Assert.Equal(Domain, review.GetProperty("domain").GetString());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void PublishFlow_DomainMismatch_RefusesWhenClaimTeamDeclaredUnderAnotherDomain(bool write, int expectedExit)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, extraTeams:
        [
            new CrossRuntimeReviewTeamDeclaration
            {
                Team = "sekiban/sekiban-dev",
                ConductorRuntime = "codex",
                Repos = [Repo],
            },
        ]);
        workspace.WriteFullPacket(Unit, Repo, domain: "wrong-domain");
        workspace.SeedQueueState(Unit, Title());
        workspace.WriteClaim(Unit, "sekiban-dev");

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write, team: "sekiban-dev");
        Assert.Equal(expectedExit, exit);
        using var result = JsonDocument.Parse(output);
        var detail = "packet domain 'wrong-domain' resolves team 'wrong-domain/sekiban-dev' as undeclared, but the claim team is declared under 'sekiban' whose repos include 'J-Tech-Japan/intent-system'.";
        AssertRefusalSurface(
            result.RootElement,
            write ? CrossRuntimeReviewCauses.DomainMismatch : null,
            write ? detail : null,
            CrossRuntimeReviewCauses.DomainMismatch,
            detail,
            "wrong-domain",
            "sekiban-dev");
        if (write)
        {
            AssertZeroCreates();
            AssertDurableStateUntouched(workspace);
        }
    }

    [Fact]
    public void PublishFlow_TargetRepoMismatch_RefusesForDeclaredTeam_NamesFix()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, "J-Tech-Japan/wrong");
        workspace.SeedQueueState(Unit, Title());

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        var detail = $"packet target_repo 'J-Tech-Japan/wrong' does not match --repo '{Repo}'.";
        var error = $"{detail} set implementation_issue_packet.target_repo to '{Repo}' in `.intent-cli/issues/{Unit}/packet.yaml`.";
        AssertRefusalSurface(
            result.RootElement,
            CrossRuntimeReviewCauses.TargetRepoMismatch,
            error,
            CrossRuntimeReviewCauses.TargetRepoMismatch,
            detail,
            Domain,
            Team);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DryRun_TargetRepoMismatch_ReportsResolutionRefusal()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, "J-Tech-Japan/wrong");
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var detail = $"packet target_repo 'J-Tech-Japan/wrong' does not match --repo '{Repo}'.";
        AssertRefusalSurface(
            result.RootElement,
            null,
            null,
            CrossRuntimeReviewCauses.TargetRepoMismatch,
            detail,
            Domain,
            Team);
        AssertPinnedNormalizedOutput("P02-target-repo-dry-run", (exit, output));
    }

    [Fact]
    public void PublishFlow_UndeclaredTeam_IncompleteTwoFilePacket_PublishesAsToday()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/9001");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: UndeclaredTeam);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("created").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void PublishFlow_TeamUnresolved_RefusesOnWrite(bool write, int expectedExit)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write);
        Assert.Equal(expectedExit, exit);
        using var result = JsonDocument.Parse(output);
        const string detail = "execution unit 'G835PF' has no held claim with a team (claim status 'not-configured': No claims store is configured; legacy single-team behavior applies unchanged.).";
        var error = write ? detail : null;
        AssertRefusalSurface(
            result.RootElement,
            write ? CrossRuntimeReviewCauses.TeamUnresolved : null,
            error,
            CrossRuntimeReviewCauses.TeamUnresolved,
            detail,
            Domain,
            null);
        AssertPinnedNormalizedOutput(write ? "P04-no-claim-write" : "P04-no-claim-dry-run", (exit, output));
        if (write)
        {
            AssertZeroCreates();
            AssertDurableStateUntouched(workspace);
        }
    }

    // ── body swap seams ────────────────────────────────────────────────

    [Fact]
    public void PublishFlow_CreatorFactoryMutatesPacketDuringConstruction_RefusesDigestStale()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        workspace.CaptureDurableBaseline();
        var bodyPath = workspace.GithubBodyPath(Unit);
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            File.AppendAllText(bodyPath, "\nmutated-during-creator-factory\n");
            return throwingCreator;
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_BodySwap_AfterGate_RefusesDigestStale()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.AfterGateHook = () =>
            File.AppendAllText(workspace.GithubBodyPath(Unit), "\n");

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_PacketSwapDuringLookup_RefusesDigestStale()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo, bodyTitle: Title());
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new PacketSwapDuringLookupChecker(workspace, Unit, Repo, Title("B"));

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_BodySwap_BetweenLookupAndRecovery_RefusesLookupInputChanged()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        var bodyPath = workspace.GithubBodyPath(Unit);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new MutatingExistingIssueChecker(bodyPath);

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        const string detail = "packet.yaml or github-body.md changed after the GitHub lookup snapshot; refusing recovery.";
        AssertRefusalSurface(
            result.RootElement,
            CrossRuntimeReviewCauses.LookupInputChanged,
            detail,
            CrossRuntimeReviewCauses.LookupInputChanged,
            detail,
            Domain,
            Team);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void PublishFlow_Gated_UnparseablePacket_RefusesWithPacketInvalidField(bool write, int expectedExit)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.WriteAllText(
            Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml"),
            "implementation_issue_packet:\n  issue_title: \"unterminated\n  domain: intent-cli\n  target_repo: J-Tech-Japan/intent-system\n");
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write);
        Assert.Equal(expectedExit, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("packet-yaml-unparseable", result.RootElement.GetProperty("cause").GetString());
        const string topLevelError = "packet '<normalized>/.intent-cli/issues/G835PF/packet.yaml' could not be parsed at line 2, column 16: While scanning a multi-line double-quoted scalar, found wrong indentation.";
        const string fieldDetail = "packet '.intent-cli/issues/G835PF/packet.yaml' could not be parsed at line 2, column 16: While scanning a multi-line double-quoted scalar, found wrong indentation.";
        AssertRefusalSurface(
            result.RootElement,
            "packet-yaml-unparseable",
            topLevelError,
            CrossRuntimeReviewCauses.PacketInvalid,
            fieldDetail,
            null,
            null,
            workspace.RootPath);
        AssertPinnedNormalizedOutput(
            write ? "P05-unparseable-write" : "P05-unparseable-dry-run",
            (exit, output),
            workspace.RootPath);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void PublishFlow_Gated_UnreadablePacket_RefusesWithPacketUnreadableField(bool write, int expectedExit)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        var packetPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        if (OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("chmod 000 unreadable-packet fixture requires Unix file permissions.");
        }

        if (!G841TestHelpers.IsNonRootUnixUser())
        {
            throw Xunit.Sdk.SkipException.ForSkip("chmod 000 unreadable-packet fixture requires a non-root Unix user.");
        }

        File.SetUnixFileMode(packetPath, UnixFileMode.None);
        var deniedMessage = ReadDeniedPacketMessage(packetPath);
        var normalizedDeniedMessage = deniedMessage.Replace(workspace.RootPath, "<normalized>", StringComparison.Ordinal);
        workspace.CaptureDurableBaseline();

        try
        {
            var (exit, output) = Run(workspace, Unit, Repo, write);
            Assert.Equal(expectedExit, exit);
            using var result = JsonDocument.Parse(output);
            Assert.Equal("packet-yaml-unreadable", result.RootElement.GetProperty("cause").GetString());
            var topLevelError = $"packet '<normalized>/.intent-cli/issues/{Unit}/packet.yaml' could not be read: {normalizedDeniedMessage}";
            var fieldDetail = $"packet '.intent-cli/issues/{Unit}/packet.yaml' could not be read: {normalizedDeniedMessage}";
            AssertPinnedNormalizedOutput(
                write ? "P06-unreadable-write" : "P06-unreadable-dry-run",
                (exit, output),
                workspace.RootPath);
            AssertRefusalSurface(
                result.RootElement,
                "packet-yaml-unreadable",
                topLevelError,
                CrossRuntimeReviewCauses.PacketUnreadable,
                fieldDetail,
                null,
                null,
                workspace.RootPath);
            AssertPinnedNormalizedOutput(
                write ? "P06-unreadable-write" : "P06-unreadable-dry-run",
                (exit, output),
                workspace.RootPath);
            AssertZeroCreates();
            AssertDurableStateUntouched(workspace);
        }
        finally
        {
            G841TestHelpers.RestorePacketPermissions(packetPath);
        }
    }

    private static string ReadDeniedPacketMessage(string packetPath)
    {
        try
        {
            _ = File.ReadAllText(packetPath);
            throw new InvalidOperationException("The chmod 000 fixture unexpectedly remained readable.");
        }
        catch (UnauthorizedAccessException exception)
        {
            return exception.Message;
        }
    }

    [Fact]
    public void BuildResolutionRefusalField_ResolvedNonDomainMismatchRequiresExplicitCause()
    {
        var resolution = new CrossRuntimeReviewPublishResolver.PublishResolution
        {
            Resolved = true,
            Declared = true,
            Domain = Domain,
            Team = Team,
            TargetRepo = Repo,
        };

        Assert.Throws<InvalidOperationException>(() => IssuePublishFlowCommand.BuildResolutionRefusalField(resolution));
    }

    // ── lookup and recovery ────────────────────────────────────────────

    [Fact]
    public void PublishFlow_Lookup_ReceivesInMemoryTitleAndBody()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo, bodyTitle: Title());
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var packetYamlPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var bodyPath = workspace.GithubBodyPath(Unit);
        var checker = new PostSnapshotMutatingChecker(
            packetYamlPath,
            bodyPath,
            GitHubExistingIssueClassification.None);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());

        Assert.Equal(1, checker.CallCount);
        Assert.Equal(IssuePublishFlowCommand.FormatIssueTitle(Unit, Title()), checker.LastTitle);
        Assert.DoesNotContain("mutated-on-disk", checker.LastBody, StringComparison.Ordinal);
        Assert.Contains("## Goal", checker.LastBody, StringComparison.Ordinal);
        Assert.Contains("mutated-on-disk", File.ReadAllText(packetYamlPath), StringComparison.Ordinal);
        Assert.Contains("mutated-on-disk", File.ReadAllText(bodyPath), StringComparison.Ordinal);
    }

    [Fact]
    public void PublishFlow_ExistingMarker_ShortCircuits_EvenWhenResolutionWouldFail()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteTwoFilePacket(Unit, Repo, targetRepoOverride: "wrong/repo");
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), Repo, 42, $"https://github.com/{Repo}/issues/42");

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("idempotent").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    [Fact]
    public void PublishFlow_ExactlyOneRecovery_ProceedsWithoutReviewGate_ForTwoFilePacket()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new StubExistingIssueChecker(GitHubExistingIssueClassification.Unique, 55, $"https://github.com/{Repo}/issues/55");
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;

        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
        Assert.NotEqual(queueBefore, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void PublishFlow_ExactlyOneRecovery_ProceedsWhenResolutionWouldFail()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new StubExistingIssueChecker(GitHubExistingIssueClassification.Unique, 88, $"https://github.com/{Repo}/issues/88");
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;

        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(0, throwingCreator.CallCount);
        Assert.NotEqual(queueBefore, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void PublishFlow_DryRun_ReportsGateDecision_ForDeclaredTeam()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var review = result.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal("missing", review.GetProperty("decision").GetString());
        Assert.NotNull(review.GetProperty("digest").GetString());
    }

    [Fact]
    public void PublishFlow_DryRun_IdempotentNotGated_WhenIssueAlreadyExists_ForDeclaredTeamOnly()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), Repo, 99, $"https://github.com/{Repo}/issues/99");

        var (exit, output) = Run(workspace, Unit, Repo, write: false);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("idempotent-not-gated", result.RootElement.GetProperty("cross_runtime_design_review").GetProperty("decision").GetString());
    }

    [Fact]
    public void PublishFlow_DryRun_UndeclaredTeam_EmitsNoDesignReviewField()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false, team: UndeclaredTeam);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    // ── gate refusals and success ──────────────────────────────────────

    [Fact]
    public void PublishFlow_DeclaredTeam_GateMissing_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Contains("cross-runtime-review-missing", result.RootElement.GetProperty("cause").GetString(), StringComparison.Ordinal);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateBlocked_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "request-changes");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.Blocked, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateRereviewMissing_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WritePacketVariant(Unit, Repo, "variant-a");
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "codex", "request-changes");
        workspace.WritePacketVariant(Unit, Repo, "variant-b");
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.RereviewMissing, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateStaleEpoch_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WritePacketVariant(Unit, Repo, "epoch-a");
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        workspace.WritePacketVariant(Unit, Repo, "epoch-b");
        workspace.RecordDesignReview(Unit, "codex", "request-changes");
        workspace.WritePacketVariant(Unit, Repo, "epoch-a");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("missing", result.RootElement.GetProperty("cross_runtime_design_review").GetProperty("decision").GetString());
        var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(workspace.PacketDirectory(Unit));
        var designResolution = CrossRuntimeReviewDesignTeamResolver.Resolve(workspace.Context.RepoRoot, Unit);
        var declaration = workspace.Context.Config.CrossRuntimeReview.Teams.Single(t => t.Team == $"{Domain}/{Team}");
        var gate = CrossRuntimeReviewGate.EvaluateDesign(
            declaration,
            CrossRuntimeReviewDesignTeamResolver.ToCrossRuntimeResolution(designResolution),
            digest,
            CrossRuntimeDesignReviewStore.Read(workspace.Context.RepoRoot, Unit));
        Assert.Contains(gate.Records, entry => entry.Status == CrossRuntimeReviewGate.StatusStaleEpoch);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateRecordUnreadable_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        var directory = CrossRuntimeReviewPaths.DesignDirectory(workspace.Context.RepoRoot, Unit);
        File.WriteAllText(Path.Combine(directory, "20990101T000000Z-codex-zzzzzzz.json"), "{\"artifact_kind\":\"nope\"}");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.RecordUnreadable, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_PacketMissing_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.PacketMissing, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateSatisfied_CreatesIssue()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var packetYamlPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var expectedBodyBytes = File.ReadAllBytes(workspace.GithubBodyPath(Unit));
        var expectedTitle = IssuePublishFlowCommand.ResolveLookupTitle(
            Unit,
            packetYamlPath,
            File.ReadAllBytes(packetYamlPath),
            expectedBodyBytes);
        var recorder = new RecordingIssueCreator($"https://github.com/{Repo}/issues/835");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal(1, recorder.CallCount);
        Assert.Equal(expectedTitle, recorder.LastTitle);
        Assert.Equal(expectedBodyBytes, recorder.LastBodyBytes);
        Assert.True(File.Exists(workspace.PublishYamlPath(Unit)));
    }

    [Fact]
    public void G845_Point2_StagesExactSnapshotAndCleansPrivateDirectory()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        var expected = G845BodyFixtures.ValidBytes(50003, Title());
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), expected);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var recorder = new RecordingIssueCreator($"https://github.com/{Repo}/issues/845");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);

        Assert.Equal(0, exit);
        Assert.Equal(1, recorder.CallCount);
        Assert.Equal(expected, recorder.LastBodyBytes);
        Assert.NotEqual(workspace.GithubBodyPath(Unit), recorder.LastBodyFilePath);
        Assert.StartsWith(Path.GetTempPath(), recorder.LastBodyFilePath!, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, recorder.LastBodyFileMode);
        }

        Assert.False(File.Exists(recorder.LastBodyFilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(recorder.LastBodyFilePath!)!));
        Assert.DoesNotContain("body-file", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G845_Point2_CreatorFactoryMutationIsRejectedBeforeTransmission()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        var expected = G845BodyFixtures.ValidBytes(50003, Title());
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), expected);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var recorder = new RecordingIssueCreator($"https://github.com/{Repo}/issues/845");
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            File.WriteAllBytes(workspace.GithubBodyPath(Unit), G845BodyFixtures.ValidBytes(50004, Title()));
            return recorder;
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true);

        Assert.Equal(1, exit);
        Assert.Contains("cross-runtime-review-digest-stale", output, StringComparison.Ordinal);
        Assert.Equal(0, recorder.CallCount);
        Assert.NotEqual(expected, File.ReadAllBytes(workspace.GithubBodyPath(Unit)));
    }

    [Theory]
    [InlineData(65535, true, "")]
    [InlineData(65536, true, "")]
    [InlineData(65537, false, "issue-body-too-large: github-body.md is 65537 bytes, which exceeds the 65536-byte limit.")]
    [InlineData(70000, false, "issue-body-too-large: github-body.md is 70000 bytes, which exceeds the 65536-byte limit.")]
    public void G845_Point2_BoundaryAndCauseAreLiteral(int bodyBytes, bool accepted, string expectedError)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), G845BodyFixtures.ValidBytes(bodyBytes, Title()));
        workspace.SeedQueueState(Unit, Title());
        if (accepted)
        {
            workspace.RecordSatisfiedDesignReviews(Unit);
        }

        var recorder = new RecordingIssueCreator("unused");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;
        var (exit, output) = Run(workspace, Unit, Repo, write: true);

        Assert.Equal(accepted ? 0 : 1, exit);
        Assert.Equal(accepted ? 1 : 0, recorder.CallCount);
        if (!accepted)
        {
            using var document = JsonDocument.Parse(output);
            Assert.Equal("issue-body-too-large", document.RootElement.GetProperty("cause").GetString());
            Assert.Equal(expectedError, document.RootElement.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData(50003)]
    public void G845_Point2_InvalidUtf8IsRefusedBeforeCreator(int bodyBytes)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), G845BodyFixtures.InvalidOrdinaryTextBytes(bodyBytes, Title()));
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var recorder = new RecordingIssueCreator("unused");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("issue-body-invalid-utf8", document.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "issue-body-invalid-utf8: github-body.md is not valid UTF-8 at byte offset 1000.",
            document.RootElement.GetProperty("error").GetString());
        Assert.Equal(0, recorder.CallCount);
    }

    [Fact]
    public void G845_Point2_ExistingMalformedHeadingValidationStillWins()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), G845BodyFixtures.InvalidHeadingBytes(50003, Title()));
        workspace.SeedQueueState(Unit, Title());
        var recorder = new RecordingIssueCreator("unused");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(
            "Child Issue Contract is incomplete; the existing publish gate rejected headings or placeholder-only Related Links.",
            root.GetProperty("error").GetString());
        Assert.Contains(
            "Goal",
            root.GetProperty("missing_contract_sections").EnumerateArray().Select(section => section.GetString()));
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.Equal(0, recorder.CallCount);
    }

    [Fact]
    public void G845_Point2_NonUtf8BomBodiesNeverReachCreate()
    {
        foreach (var bytes in new[]
        {
            G845BodyFixtures.Utf16Bytes(Title("UTF-16")),
            G845BodyFixtures.Utf32Bytes(Title("UTF-32")),
        })
        {
            using var workspace = new G835PublishFlowWorkspace(declare: true);
            workspace.WriteFullPacket(Unit, Repo);
            File.WriteAllBytes(workspace.GithubBodyPath(Unit), bytes);
            workspace.SeedQueueState(Unit, Title());
            workspace.RecordSatisfiedDesignReviews(Unit);
            var recorder = new RecordingIssueCreator("unused");
            IssuePublishFlowCommand.CreatorFactory = () => recorder;

            var (exit, _) = Run(workspace, Unit, Repo, write: true);

            Assert.Equal(1, exit);
            Assert.Equal(0, recorder.CallCount);
        }
    }

    [Fact]
    public void G845_Point2_BomCountsRawBytesAndIsRefusedForSize()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), G845BodyFixtures.BomBytes(65539, Title()));
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var recorder = new RecordingIssueCreator("unused");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("issue-body-too-large", document.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "issue-body-too-large: github-body.md is 65539 bytes, which exceeds the 65536-byte limit.",
            document.RootElement.GetProperty("error").GetString());
        Assert.Equal(0, recorder.CallCount);
    }

    [Fact]
    public void G845_Point2_StagingFailureUsesTheResultErrorAndDoesNotCallCreator()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        File.WriteAllBytes(workspace.GithubBodyPath(Unit), G845BodyFixtures.ValidBytes(50003, Title()));
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var recorder = new RecordingIssueCreator("unused");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;

        try
        {
            IssueBodyFileStager.FileWriteOverride = (_, _) => throw new IOException("write failure");
            var (exit, output) = Run(workspace, Unit, Repo, write: true);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                "could not stage the validated body for upload (write failure). No issue was created.",
                document.RootElement.GetProperty("error").GetString());
            Assert.Equal(0, recorder.CallCount);
        }
        finally
        {
            IssueBodyFileStager.FileWriteOverride = null;
        }
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_CreatesWithTheSnapshotTitle_NotTheAnalysisTimeTitle()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var directory = workspace.PacketDirectory(Unit);
        string[] names = ["packet.yaml", "github-body.md", "review-context.md", "implementation.md"];

        // The reviewed packet is the renamed one; approvals are recorded on its digest.
        workspace.WriteFullPacket(Unit, Repo, bodyTitle: Title("renamed"));
        workspace.RecordSatisfiedDesignReviews(Unit);
        var reviewed = names.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(directory, name)));
        var expectedTitle = IssuePublishFlowCommand.ResolveLookupTitle(
            Unit,
            Path.Combine(directory, "packet.yaml"),
            reviewed["packet.yaml"],
            reviewed["github-body.md"]);

        // Analysis reads the original title; the reviewed bytes land before the lookup snapshot.
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = () =>
        {
            foreach (var (name, bytes) in reviewed)
            {
                File.WriteAllBytes(Path.Combine(directory, name), bytes);
            }
        };
        var recorder = new RecordingIssueCreator($"https://github.com/{Repo}/issues/836");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.True(exit == 0, output);
        Assert.Equal(1, recorder.CallCount);
        Assert.Contains("renamed", expectedTitle, StringComparison.Ordinal);
        Assert.Equal(expectedTitle, recorder.LastTitle);
        Assert.Equal(reviewed["github-body.md"], recorder.LastBodyBytes);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static string Title() => "G835PF Publish-flow design gate";

    private static string Title(string suffix) => $"G835PF Publish-flow design gate {suffix}";

    private static (int ExitCode, string Output) Run(
        G835PublishFlowWorkspace workspace,
        string unit,
        string repo,
        bool write,
        string? team = Team,
        string? domainOverride = null,
        string format = "json")
    {
        var args = new List<string> { unit, "--repo", repo, "--domain", domainOverride ?? Domain, "--format", format };
        if (team is not null)
        {
            args.AddRange(["--team", team]);
        }

        if (write)
        {
            args.Add("--write");
        }

        using var writer = new StringWriter();
        var exit = IssuePublishFlowCommand.Execute(workspace.Context, args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private static (int ExitCode, string Output) RunWithoutDomain(
        G835PublishFlowWorkspace workspace,
        string unit,
        string repo,
        bool write,
        string team,
        string format)
    {
        var args = new List<string> { unit, "--repo", repo, "--team", team, "--format", format };
        if (write) args.Add("--write");
        using var writer = new StringWriter();
        var exit = IssuePublishFlowCommand.Execute(workspace.Context, args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private static void AssertNullOrMissing(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value))
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    private static (int ExitCode, string Json) NormalizePublishOutput(
        (int ExitCode, string Output) result,
        string? temporaryRoot = null)
    {
        var source = temporaryRoot is null
            ? result.Output
            : result.Output.Replace(temporaryRoot, "<normalized>", StringComparison.Ordinal);
        var node = System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject();
        foreach (var path in new[]
        {
            "packet_directory", "github_body_path", "publish_yaml_path", "issue_url", "issue_number",
        })
        {
            if (node.ContainsKey(path))
            {
                node[path] = path == "issue_number" ? 0 : "<normalized>";
            }
        }

        if (node.TryGetPropertyValue("cross_runtime_design_review", out var review) && review is null)
        {
            node.Remove("cross_runtime_design_review");
        }

        return (result.ExitCode, node.ToJsonString());
    }

    private static void AssertRefusalSurface(
        JsonElement result,
        string? expectedTopLevelCause,
        string? expectedTopLevelError,
        string expectedFieldCause,
        string expectedFieldDetail,
        string? expectedDomain,
        string? expectedTeam,
        string? temporaryRoot = null)
    {
        if (expectedTopLevelCause is null)
        {
            Assert.False(result.TryGetProperty("cause", out _));
        }
        else
        {
            Assert.Equal(expectedTopLevelCause, result.GetProperty("cause").GetString());
        }

        if (expectedTopLevelError is null)
        {
            Assert.False(result.TryGetProperty("error", out _));
        }
        else
        {
            var actualError = result.GetProperty("error").GetString()!;
            if (temporaryRoot is not null)
            {
                actualError = actualError.Replace(temporaryRoot, "<normalized>", StringComparison.Ordinal);
            }

            Assert.Equal(expectedTopLevelError, actualError);
        }

        var expectedField = new System.Text.Json.Nodes.JsonObject
        {
            ["decision"] = "blocked",
            ["reasons"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["cause"] = expectedFieldCause,
                    ["detail"] = expectedFieldDetail,
                },
            },
        };
        if (expectedDomain is not null)
        {
            expectedField["domain"] = expectedDomain;
        }

        if (expectedTeam is not null)
        {
            expectedField["team"] = expectedTeam;
        }

        var actualField = result.GetProperty("cross_runtime_design_review");
        var actualFieldJson = actualField.GetRawText();
        if (temporaryRoot is not null)
        {
            actualFieldJson = actualFieldJson.Replace(temporaryRoot, "<normalized>", StringComparison.Ordinal);
        }

        Assert.Equal(
            expectedField.ToJsonString(),
            System.Text.Json.Nodes.JsonNode.Parse(actualFieldJson)!.ToJsonString());
    }

    private static void AssertPinnedNormalizedOutput(
        string name,
        (int ExitCode, string Output) result,
        string? temporaryRoot = null)
    {
        var normalized = NormalizePublishOutput(result, temporaryRoot);
        var expected = name switch
        {
            "P02-target-repo-dry-run" => (0, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"dry-run","title":"G835PF Publish-flow design gate","issue_title":"G835PF Publish-flow design gate","title_source":"packet-yaml","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-target-repo-mismatch","detail":"packet target_repo \u0027J-Tech-Japan/wrong\u0027 does not match --repo \u0027J-Tech-Japan/intent-system\u0027."}],"domain":"intent-cli","team":"intent-cli-dev"}}
                """),
            "P04-no-claim-dry-run" => (0, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"dry-run","title":"G835PF Publish-flow design gate","issue_title":"G835PF Publish-flow design gate","title_source":"packet-yaml","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-team-unresolved","detail":"execution unit \u0027G835PF\u0027 has no held claim with a team (claim status \u0027not-configured\u0027: No claims store is configured; legacy single-team behavior applies unchanged.)."}],"domain":"intent-cli"}}
                """),
            "P04-no-claim-write" => (1, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"write","title":"G835PF Publish-flow design gate","issue_title":"G835PF Publish-flow design gate","title_source":"packet-yaml","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"error":"execution unit \u0027G835PF\u0027 has no held claim with a team (claim status \u0027not-configured\u0027: No claims store is configured; legacy single-team behavior applies unchanged.).","cause":"cross-runtime-review-team-unresolved","cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-team-unresolved","detail":"execution unit \u0027G835PF\u0027 has no held claim with a team (claim status \u0027not-configured\u0027: No claims store is configured; legacy single-team behavior applies unchanged.)."}],"domain":"intent-cli"}}
                """),
            "P05-unparseable-dry-run" => (1, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"dry-run","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"error":"packet \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 could not be parsed at line 2, column 16: While scanning a multi-line double-quoted scalar, found wrong indentation.","cause":"packet-yaml-unparseable","cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-packet-invalid","detail":"packet \u0027.intent-cli/issues/G835PF/packet.yaml\u0027 could not be parsed at line 2, column 16: While scanning a multi-line double-quoted scalar, found wrong indentation."}]}}
                """),
            "P05-unparseable-write" => (1, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"write","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"error":"packet \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 could not be parsed at line 2, column 16: While scanning a multi-line double-quoted scalar, found wrong indentation.","cause":"packet-yaml-unparseable","cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-packet-invalid","detail":"packet \u0027.intent-cli/issues/G835PF/packet.yaml\u0027 could not be parsed at line 2, column 16: While scanning a multi-line double-quoted scalar, found wrong indentation."}]}}
                """),
            "P06-unreadable-dry-run" => (1, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"dry-run","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"error":"packet \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 could not be read: Access to the path \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 is denied.","cause":"packet-yaml-unreadable","cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-packet-unreadable","detail":"packet \u0027.intent-cli/issues/G835PF/packet.yaml\u0027 could not be read: Access to the path \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 is denied."}]}}
                """),
            "P06-unreadable-write" => (1, """
                {"execution_unit":"G835PF","domain":"intent-cli","repo":"J-Tech-Japan/intent-system","packet_directory":"\u003Cnormalized\u003E","github_body_path":"\u003Cnormalized\u003E","publish_yaml_path":"\u003Cnormalized\u003E","packet_exists":true,"github_body_present":true,"missing_contract_sections":[],"mode":"write","warnings":[],"created":false,"idempotent":false,"durable_state_synced":false,"queue_state_patched":false,"publish_yaml_patched":false,"runs_appended":false,"intent_target_applied":false,"next_steps":[],"error":"packet \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 could not be read: Access to the path \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 is denied.","cause":"packet-yaml-unreadable","cross_runtime_design_review":{"decision":"blocked","reasons":[{"cause":"cross-runtime-review-packet-unreadable","detail":"packet \u0027.intent-cli/issues/G835PF/packet.yaml\u0027 could not be read: Access to the path \u0027\u003Cnormalized\u003E/.intent-cli/issues/G835PF/packet.yaml\u0027 is denied."}]}}
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No G844 baseline snapshot is registered."),
        };

        Assert.Equal(expected.Item1, normalized.ExitCode);
        Assert.Equal(expected.Item2, normalized.Json);
    }

    private void AssertZeroCreates() => Assert.Equal(0, throwingCreator.CallCount);

    private static void AssertDurableStateUntouched(G835PublishFlowWorkspace workspace) =>
        workspace.AssertDurableBaselineUntouched(Unit);

    internal sealed class G835PublishFlowWorkspace : IDisposable
    {
        private readonly string rootPath = CreateRoot();
        private string? gitOriginPath;
        private byte[]? queueStateBaseline;
        private byte[]? runsBaseline;
        private IReadOnlyDictionary<string, byte[]>? claimsBaseline;
        private IReadOnlyDictionary<string, byte[]>? handoffBaseline;
        private int designReviewRecordedAtStep;

        private static string CreateRoot()
        {
            var parent = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
            var path = Path.Combine(parent, "g835-publish-flow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public G835PublishFlowWorkspace(bool declare = false, string? heldTeam = Team,
            CrossRuntimeReviewTeamDeclaration[]? extraTeams = null, string? configuredDomain = null,
            string? metadataWriteBranch = null)
        {
            var teams = new List<CrossRuntimeReviewTeamDeclaration>();
            if (declare)
            {
                teams.Add(new CrossRuntimeReviewTeamDeclaration
                {
                    Team = $"{Domain}/{Team}",
                    ConductorRuntime = "claude",
                    Repos = [Repo],
                });
            }
            if (extraTeams is not null) teams.AddRange(extraTeams);

            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = configuredDomain ?? Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                        SameRepoTopology = metadataWriteBranch is not null,
                        MetadataWriteBranch = metadataWriteBranch ?? string.Empty,
                    },
                    CrossRuntimeReview = new CrossRuntimeReviewConfig { Teams = teams },
                },
            };

            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            var configText = metadataWriteBranch is null
                ? "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n"
                : "[project]\ndomain = \"intent-cli\"\nartifact_root = \".intent-cli\"\nsame_repo_topology = true\nmetadata_write_branch = \""
                    + metadataWriteBranch + "\"\n";
            File.WriteAllText(Path.Combine(rootPath, ".intent-cli", "config.toml"), configText);
            MetadataWriteBranch = metadataWriteBranch;
            if (heldTeam is not null)
            {
                WriteClaim(Unit, heldTeam);
            }
        }

        public CliContext Context { get; }

        public string RootPath => rootPath;

        private string? MetadataWriteBranch { get; }

        public string LocalClaimPath(string unit) => Path.Combine(rootPath,
            ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));

        public sealed record GitSnapshot(byte[] IndexBytes, string Refs);

        public GitSnapshot InitializeGitAndCapture()
        {
            Git("init", "--quiet");
            Git("checkout", "-b", "main");
            Git("config", "user.name", "G863 test fixture");
            Git("config", "user.email", "g863-fixture@example.invalid");
            Git("add", "-A");
            Git("commit", "--quiet", "-m", "fixture baseline");

            gitOriginPath = rootPath + "-origin.git";
            Git("init", "--bare", "--quiet", gitOriginPath);
            _ = Git("--git-dir", gitOriginPath, "symbolic-ref", "HEAD", "refs/heads/main");
            Git("remote", "add", "origin", gitOriginPath);
            Git("push", "--quiet", "--set-upstream", "origin", "main");
            if (MetadataWriteBranch is not null)
            {
                Git("checkout", "-b", MetadataWriteBranch);
                Git("push", "--quiet", "--set-upstream", "origin", MetadataWriteBranch);
                Git("checkout", "main");
            }
            Git("remote", "set-head", "origin", "main");
            return CaptureGitState();
        }

        public bool OriginHasClaim(string unit, string branch)
        {
            if (gitOriginPath is null) throw new InvalidOperationException("Git fixture origin has not been initialized.");
            var relative = ClaimCommand.ClaimPath($"execution-unit:{unit}");
            var result = RunGit(rootPath, ["--git-dir", gitOriginPath, "show", $"refs/heads/{branch}:{relative}"]);
            return result.Length > 0;
        }

        public void ChangePinnedSourceDigest(string oldDigest)
        {
            var packetPath = Path.Combine(PacketDirectory(Unit), "packet.yaml");
            var yaml = File.ReadAllText(packetPath);
            Assert.Contains(oldDigest, yaml, StringComparison.Ordinal);
            File.WriteAllText(packetPath, yaml.Replace(oldDigest, new string('0', oldDigest.Length), StringComparison.Ordinal));
        }

        public GitSnapshot CaptureGitState()
        {
            var index = Path.Combine(rootPath, ".git", "index");
            var head = Git("symbolic-ref", "HEAD").Trim();
            var commit = Git("rev-parse", "HEAD").Trim();
            var refs = Git("for-each-ref", "--format=%(refname) %(objectname)")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .TrimEnd('\n');
            return new GitSnapshot(File.ReadAllBytes(index), $"{head}\n{commit}\n{refs}");
        }

        public void AssertGitStateUnchanged(GitSnapshot before)
        {
            var after = CaptureGitState();
            Assert.Equal(before.IndexBytes, after.IndexBytes);
            Assert.Equal(before.Refs, after.Refs);
        }

        public void AdvanceOriginClaimTeam(string unit, string team, string branch = "main")
        {
            if (gitOriginPath is null) throw new InvalidOperationException("Git fixture origin has not been initialized.");
            var clonePath = rootPath + "-origin-advance";
            try
            {
                _ = RunGit(rootPath, ["clone", "--quiet", "--branch", branch, gitOriginPath, clonePath]);
                _ = RunGit(clonePath, ["config", "user.name", "G863 remote fixture"]);
                _ = RunGit(clonePath, ["config", "user.email", "g863-remote-fixture@example.invalid"]);
                var scope = $"execution-unit:{unit}";
                var relativeClaimPath = ClaimCommand.ClaimPath(scope);
                var claimPath = Path.Combine(clonePath, relativeClaimPath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(claimPath, JsonSerializer.Serialize(new ClaimRecord(
                    "1", scope, "remote-advancer", team, DateTimeOffset.UtcNow,
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")) + Environment.NewLine);
                _ = RunGit(clonePath, ["add", "--", relativeClaimPath]);
                _ = RunGit(clonePath, ["commit", "--quiet", "-m", "advance remote claim fixture"]);
                _ = RunGit(clonePath, ["push", "--quiet", "origin", branch]);
            }
            finally
            {
                if (Directory.Exists(clonePath)) Directory.Delete(clonePath, recursive: true);
            }
        }

        public void MakeOriginUnavailable()
        {
            _ = Git("remote", "set-url", "origin", rootPath + "-offline-origin.git");
        }

        private string Git(params string[] args) => RunGit(rootPath, args);

        private static string RunGit(string workingDirectory, string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start git fixture process.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr}");
            }
            return stdout;
        }

        public string QueueStatePath => Path.Combine(rootPath, ".intent-cli", "queue-state.json");

        public string RunsLogPath => Path.Combine(rootPath, ".intent-cli", "runs.jsonl");

        public string GithubBodyPath(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit, "github-body.md");

        public string PublishYamlPath(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit, "publish.yaml");

        public string PacketDirectory(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit);

        public string ClaimsDirectory => Path.Combine(rootPath, ".intent-cli", "claims");

        public string HandoffDirectory => Path.Combine(rootPath, PublishedExternalHandoffStore.RecordRootRelativePath);

        public void CaptureDurableBaseline()
        {
            queueStateBaseline = File.Exists(QueueStatePath) ? File.ReadAllBytes(QueueStatePath) : Array.Empty<byte>();
            runsBaseline = File.Exists(RunsLogPath) ? File.ReadAllBytes(RunsLogPath) : Array.Empty<byte>();
            claimsBaseline = SnapshotDirectory(ClaimsDirectory);
            handoffBaseline = SnapshotDirectory(HandoffDirectory);
        }

        public void AssertDurableBaselineUntouched(string unit)
        {
            Assert.False(File.Exists(PublishYamlPath(unit)));
            var queueState = File.Exists(QueueStatePath) ? File.ReadAllBytes(QueueStatePath) : Array.Empty<byte>();
            Assert.Equal(queueStateBaseline ?? Array.Empty<byte>(), queueState);
            var runs = File.Exists(RunsLogPath) ? File.ReadAllBytes(RunsLogPath) : Array.Empty<byte>();
            Assert.Equal(runsBaseline ?? Array.Empty<byte>(), runs);
            Assert.Equal(claimsBaseline ?? new Dictionary<string, byte[]>(), SnapshotDirectory(ClaimsDirectory));
            Assert.Equal(handoffBaseline ?? new Dictionary<string, byte[]>(), SnapshotDirectory(HandoffDirectory));
        }

        public void WriteClaim(string unit, string team)
        {
            var claimPath = Path.Combine(rootPath, ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
            File.WriteAllText(claimPath, JsonSerializer.Serialize(new
            {
                schema_version = "1",
                scope = $"execution-unit:{unit}",
                actor = "design",
                team,
                claimed_at = DateTimeOffset.UtcNow,
                base_commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            }));
        }

        public void WriteMinimalPacket(string unit, string targetRepo, string domain = Domain, string? targetRepoOverride = null)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{Title()}\"\n  domain: {domain}\n  target_repo: {targetRepoOverride ?? targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody(Title()));
        }

        public void WriteTwoFilePacket(string unit, string targetRepo, string domain = Domain, string? targetRepoOverride = null)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{Title()}\"\n  domain: {domain}\n  target_repo: {targetRepoOverride ?? targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody(Title()));
        }

        public void WriteFullPacket(string unit, string targetRepo, string domain = Domain, string? bodyTitle = null)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{bodyTitle ?? Title()}\"\n  domain: {domain}\n  target_repo: {targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody(bodyTitle ?? Title()));
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
        }

        public (string Digest, byte[] BodyBytes, string ArtifactPath) WritePinnedSourcePacket(
            string unit, string targetRepo, string sourceTeam = Team, TimeSpan? expiresAfter = null,
            DateTimeOffset? recordedAt = null)
        {
            WriteFullPacket(unit, targetRepo);
            var rulingRecordedAt = recordedAt ?? new DateTimeOffset(2026, 10, 10, 11, 0, 0, TimeSpan.Zero);
            var artifact = new RulingArtifact
            {
                Id = "R-G863-PUBLISH",
                Domain = Domain,
                Team = sourceTeam,
                AuthorityRole = "operator",
                TargetRepo = targetRepo,
                ScopeKind = "execution-units",
                ExecutionUnits = [unit],
                Decision = "Use this exact pinned source.",
                Rationale = "The packet publishes an explicit, digest-pinned source.",
                EvidenceRefs = [$"https://github.com/J-Tech-Japan/intent-system/issues/1887"],
                RecordedAt = rulingRecordedAt,
                ExpiresAt = expiresAfter is null ? null : rulingRecordedAt.Add(expiresAfter.Value),
                Supersedes = [],
            };
            var inputPath = Path.Combine(rootPath, "ruling-input.json");
            File.WriteAllBytes(inputPath, RulingArtifact.Serialize(artifact));
            using var writer = new StringWriter();
            var exit = RulingCommand.ExecuteRecord(Context,
                ["--id", artifact.Id, "--domain", Domain, "--team", sourceTeam, "--from-file", inputPath,
                 "--authority-role", "operator", "--write", "--format", "json"], writer, null, rulingRecordedAt);
            Assert.True(exit == 0, writer.ToString());
            using var writeResult = JsonDocument.Parse(writer.ToString());
            Assert.True(writeResult.RootElement.GetProperty("wrote").GetBoolean());
            var artifactPath = Path.Combine(rootPath, ".intent-cli", "rulings", Domain, sourceTeam, artifact.Id + ".json");
            var diskBytes = File.ReadAllBytes(artifactPath);
            Assert.Equal(RulingArtifact.Serialize(artifact), diskBytes);
            var digest = Convert.ToHexString(SHA256.HashData(diskBytes)).ToLowerInvariant();

            var packetPath = Path.Combine(PacketDirectory(unit), "packet.yaml");
            var yaml = File.ReadAllText(packetPath)
                .Replace("  target_repo: " + targetRepo,
                    "  target_repo: " + targetRepo + "\n  source_execution_unit: " + unit + "\n  team: " + sourceTeam,
                    StringComparison.Ordinal)
                + "\nscope_sources:\n  - \"ruling:" + artifact.Id + "\"\nscope_source_digests:\n  \"ruling:" + artifact.Id + "\": \"" + digest + "\"\n";
            File.WriteAllText(packetPath, yaml);
            var bodyPath = GithubBodyPath(unit);
            var baseBody = File.ReadAllText(bodyPath);
            var pending = PacketScopeSources.Evaluate(rootPath, unit, Encoding.UTF8.GetBytes(yaml), Encoding.UTF8.GetBytes(baseBody), rulingRecordedAt);
            Assert.Equal("scope-sources-provenance-mismatch", pending.Cause);
            Assert.NotNull(pending.ExpectedProvenanceBlock);
            var body = Encoding.UTF8.GetBytes(baseBody.TrimEnd() + "\n\n" + pending.ExpectedProvenanceBlock + "\n");
            File.WriteAllBytes(bodyPath, body);
            var verified = PacketScopeSources.Evaluate(rootPath, unit, Encoding.UTF8.GetBytes(yaml), body, rulingRecordedAt);
            Assert.Equal("satisfied", verified.State);
            File.Delete(inputPath);
            return (digest, body, artifactPath);
        }

        public string WriteSuccessorRuling(string unit, string targetRepo, string sourceTeam, string predecessorId)
        {
            var recordedAt = new DateTimeOffset(2026, 10, 10, 11, 30, 0, TimeSpan.Zero);
            var artifact = new RulingArtifact
            {
                Id = "R-G863-SUCCESSOR",
                Domain = Domain,
                Team = sourceTeam,
                AuthorityRole = "operator",
                TargetRepo = targetRepo,
                ScopeKind = "execution-units",
                ExecutionUnits = [unit],
                Decision = "Use the replacement source for the final create check.",
                Rationale = "The pin must stop authorizing its superseded predecessor.",
                EvidenceRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/1887"],
                RecordedAt = recordedAt,
                ExpiresAt = null,
                Supersedes = [predecessorId],
            };
            var inputPath = Path.Combine(rootPath, artifact.Id + ".json");
            File.WriteAllBytes(inputPath, RulingArtifact.Serialize(artifact));
            using var output = new StringWriter();
            var exit = RulingCommand.ExecuteRecord(Context,
                ["--id", artifact.Id, "--domain", Domain, "--team", sourceTeam, "--from-file", inputPath,
                 "--authority-role", "operator", "--write", "--format", "json"], output, null, recordedAt);
            Assert.True(exit == 0, output.ToString());
            using var result = JsonDocument.Parse(output.ToString());
            Assert.True(result.RootElement.GetProperty("wrote").GetBoolean());
            var artifactPath = Path.Combine(rootPath, ".intent-cli", "rulings", Domain, sourceTeam, artifact.Id + ".json");
            Assert.Equal(RulingArtifact.Serialize(artifact), File.ReadAllBytes(artifactPath));
            File.Delete(inputPath);
            return artifactPath;
        }

        public void WritePacketVariant(string unit, string targetRepo, string variant)
        {
            var directory = PacketDirectory(unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{Title()} {variant}\"\n  domain: {Domain}\n  target_repo: {targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody($"{Title()} {variant}"));
            File.WriteAllText(Path.Combine(directory, "review-context.md"), $"# review {variant}\n");
            File.WriteAllText(Path.Combine(directory, "implementation.md"), $"# notes {variant}\n");
        }

        public void RecordDesignReview(string unit, string runtime, string verdict)
        {
            var recordedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero).AddMinutes(designReviewRecordedAtStep++);
            var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDirectory(unit));
            var verdictJson = JsonSerializer.Serialize(new
            {
                verdict,
                packet_digest = digest,
                blocking_findings = verdict == "request-changes"
                    ? new[] { new { file = "x", line = 1, scenario = "x", finding = "x" } }
                    : Array.Empty<object>(),
                notes = new[] { "ok" },
            });
            var raw = Encoding.UTF8.GetBytes(runtime == "claude" ? ClaudeEnvelope(verdictJson) : CursorEnvelope(verdictJson));
            var draft = new CrossRuntimeDesignReviewRecord
            {
                ArtifactKind = CrossRuntimeDesignReviewRecord.ArtifactKindValue,
                PacketDigest = digest,
                TargetRepo = Repo,
                ExecutionUnit = unit,
                Domain = Domain,
                Team = Team,
                Kind = CrossRuntimeReviewRecord.KindDesign,
                Runtime = runtime,
                RuntimeVersion = "2.1.269",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
                Verdict = verdict,
                BlockingFindings = verdict == "request-changes" ? [new CrossRuntimeReviewFinding { File = "x", Line = 1, Scenario = "x" }] : [],
                Notes = ["ok"],
                RecordedAt = recordedAt,
                RawVerdictFile = string.Empty,
                RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
            };
            var record = draft with { RawVerdictFile = CrossRuntimeDesignReviewStore.RawRelativePath(draft) };
            Assert.True(CrossRuntimeDesignReviewStore.Write(rootPath, record, raw).Written);
        }

        public void RecordSatisfiedDesignReviews(string unit)
        {
            var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(Path.Combine(rootPath, ".intent-cli", "issues", unit));
            foreach (var runtime in new[] { "claude", "cursor" })
            {
                var at = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero).AddMinutes(designReviewRecordedAtStep++);
                var verdict = JsonSerializer.Serialize(new
                {
                    verdict = "approve",
                    packet_digest = digest,
                    blocking_findings = Array.Empty<object>(),
                    notes = new[] { "ok" },
                });
                var raw = Encoding.UTF8.GetBytes(runtime == "claude" ? ClaudeEnvelope(verdict) : CursorEnvelope(verdict));
                var draft = new CrossRuntimeDesignReviewRecord
                {
                    ArtifactKind = CrossRuntimeDesignReviewRecord.ArtifactKindValue,
                    PacketDigest = digest,
                    TargetRepo = Repo,
                    ExecutionUnit = unit,
                    Domain = Domain,
                    Team = Team,
                    Kind = CrossRuntimeReviewRecord.KindDesign,
                    Runtime = runtime,
                    RuntimeVersion = "2.1.269",
                    ConductorRuntime = "claude",
                    Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
                    Verdict = "approve",
                    BlockingFindings = [],
                    Notes = ["ok"],
                    RecordedAt = at,
                    RawVerdictFile = string.Empty,
                    RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
                };
                var record = draft with { RawVerdictFile = CrossRuntimeDesignReviewStore.RawRelativePath(draft) };
                Assert.True(CrossRuntimeDesignReviewStore.Write(rootPath, record, raw).Written);
            }
        }

        public void RecordSatisfiedDesignReviewsViaCommand(string unit)
        {
            var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDirectory(unit));
            foreach (var runtime in new[] { "claude", "cursor" })
            {
                var verdict = JsonSerializer.Serialize(new
                {
                    verdict = "approve",
                    packet_digest = digest,
                    blocking_findings = Array.Empty<object>(),
                    notes = new[] { "ok" },
                });
                var raw = Encoding.UTF8.GetBytes(runtime == "claude" ? ClaudeEnvelope(verdict) : CursorEnvelope(verdict));
                var verdictPath = Path.Combine(rootPath, $"{runtime}-design-verdict.json");
                File.WriteAllBytes(verdictPath, raw);
                var args = new[]
                {
                    "review", "cross-runtime", "record", "--kind", "design", "--execution-unit", unit,
                    "--packet-digest", digest, "--runtime", runtime, "--runtime-version", "2.1.269",
                    "--verdict-file", verdictPath, "--write", "--format", "json",
                };
                using var writer = new StringWriter();
                var exit = CommandRouter.Execute(args, Context, writer);
                Assert.True(exit == 0, writer.ToString());
                using var result = JsonDocument.Parse(writer.ToString());
                Assert.Equal("satisfied", result.RootElement.GetProperty("scope_sources").GetProperty("state").GetString());
            }
        }

        public void SeedQueueState(string unit, string title) => WriteQueueStateForUnit(unit, title, linkedIssue: null);

        public void SeedQueueStateWithLinkedIssue(string unit, string title, string repo, int issueNumber, string issueUrl) =>
            WriteQueueStateForUnit(unit, title, new LinkedIssue { Repo = repo, Number = issueNumber, Url = issueUrl });

        private void WriteQueueStateForUnit(string unit, string title, LinkedIssue? linkedIssue)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            var state = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 5, 6, 0, 0, 0, TimeSpan.Zero),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = unit,
                        Title = title,
                        State = QueueItemState.Queued,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Implementation = $".intent-cli/issues/{unit}/implementation.md",
                            ReviewContext = $".intent-cli/issues/{unit}/review-context.md",
                            Yaml = $".intent-cli/issues/{unit}/packet.yaml",
                        },
                        LinkedIssue = linkedIssue,
                        WorkerRole = "child-impl",
                        ReviewRole = "host-review",
                        Priority = "normal",
                    },
                ],
            };
            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
            if (gitOriginPath is not null && Directory.Exists(gitOriginPath))
            {
                Directory.Delete(gitOriginPath, recursive: true);
            }
        }

        private static string BuildCompleteContractBody(string title) =>
            $"""
            # {title}

            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            - x

            ## Out Of Scope
            - x

            ## Acceptance Criteria
            - x

            ## Verification
            x

            ## Related Links
            - x

            ## Base Branch Policy
            Policy: `direct-main`
            Expected PR base branch: `main`
            Open all child PRs against `main` directly.
            """;

        private static string ClaudeEnvelope(string verdict)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Fixture("claude-envelope.json")))!.AsObject();
            node["structured_output"] = System.Text.Json.Nodes.JsonNode.Parse(verdict);
            node["result"] = verdict;
            return node.ToJsonString();
        }

        private static string CursorEnvelope(string resultText)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Fixture("cursor-envelope.json")))!.AsObject();
            node["result"] = resultText;
            return node.ToJsonString();
        }

        private static string Fixture(string name) =>
            Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

        private static Dictionary<string, byte[]> SnapshotDirectory(string directory)
        {
            var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (!Directory.Exists(directory))
            {
                return snapshot;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                snapshot[path] = File.ReadAllBytes(path);
            }

            return snapshot;
        }
    }

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public int CallCount { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            throw new InvalidOperationException("tests must not reach real gh issue create");
        }
    }

    private sealed class StubIssueCreator : IIssueCreator
    {
        private readonly string url;

        public StubIssueCreator(string url) => this.url = url;

        public int CallCount { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            return new IssueCreateOutcome(url);
        }
    }

    private sealed class StubExistingIssueChecker : IGitHubExistingIssueChecker
    {
        private readonly GitHubExistingIssueLookupResult result;

        public StubExistingIssueChecker(GitHubExistingIssueClassification classification, int? issueNumber = null, string? issueUrl = null)
        {
            result = new GitHubExistingIssueLookupResult
            {
                Classification = classification,
                IssueNumber = issueNumber,
                IssueUrl = issueUrl,
            };
        }

        public int CallCount { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class CallbackExistingIssueChecker(
        GitHubExistingIssueLookupResult result,
        Action callback) : IGitHubExistingIssueChecker
    {
        public int CallCount { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            CallCount++;
            callback();
            return result;
        }
    }

    private sealed class CapturingExistingIssueChecker : IGitHubExistingIssueChecker
    {
        public CapturingExistingIssueChecker(GitHubExistingIssueClassification classification) =>
            Inner = new StubExistingIssueChecker(classification);

        private StubExistingIssueChecker Inner { get; }

        public int CallCount => Inner.CallCount;

        public string? LastTitle { get; private set; }

        public string? LastBody { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            LastTitle = expectedTitle;
            LastBody = expectedBody;
            return Inner.FindExistingIssue(repo, executionUnit, expectedTitle, expectedBody);
        }
    }

    private sealed class MutatingExistingIssueChecker : IGitHubExistingIssueChecker
    {
        private readonly string bodyPath;

        public MutatingExistingIssueChecker(string bodyPath) => this.bodyPath = bodyPath;

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            File.AppendAllText(bodyPath, "\n");
            return new GitHubExistingIssueLookupResult
            {
                Classification = GitHubExistingIssueClassification.Unique,
                IssueNumber = 77,
                IssueUrl = $"https://github.com/{repo}/issues/77",
            };
        }
    }

    private sealed class PostSnapshotMutatingChecker : IGitHubExistingIssueChecker
    {
        private readonly string packetYamlPath;
        private readonly string bodyPath;
        private readonly StubExistingIssueChecker inner;

        public PostSnapshotMutatingChecker(string packetYamlPath, string bodyPath, GitHubExistingIssueClassification classification)
        {
            this.packetYamlPath = packetYamlPath;
            this.bodyPath = bodyPath;
            inner = new StubExistingIssueChecker(classification);
        }

        public int CallCount => inner.CallCount;

        public string? LastTitle { get; private set; }

        public string? LastBody { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            File.AppendAllText(packetYamlPath, "\nmutated-on-disk: true\n");
            File.AppendAllText(bodyPath, "\nmutated-on-disk\n");
            LastTitle = expectedTitle;
            LastBody = expectedBody;
            return inner.FindExistingIssue(repo, executionUnit, expectedTitle, expectedBody);
        }
    }

    private sealed class RecordingIssueCreator : IIssueCreator
    {
        private readonly string url;

        public RecordingIssueCreator(string url) => this.url = url;

        public int CallCount { get; private set; }

        public string? LastTitle { get; private set; }

        public byte[]? LastBodyBytes { get; private set; }

        public string? LastBodyFilePath { get; private set; }

        public UnixFileMode? LastBodyFileMode { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            LastTitle = title;
            LastBodyBytes = File.ReadAllBytes(bodyFilePath);
            LastBodyFilePath = bodyFilePath;
            LastBodyFileMode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(bodyFilePath);
            return new IssueCreateOutcome(url);
        }
    }

    private sealed class PacketSwapDuringLookupChecker : IGitHubExistingIssueChecker
    {
        private readonly G835PublishFlowWorkspace workspace;
        private readonly string unit;
        private readonly string targetRepo;
        private readonly string alternateTitle;

        public PacketSwapDuringLookupChecker(G835PublishFlowWorkspace workspace, string unit, string targetRepo, string alternateTitle)
        {
            this.workspace = workspace;
            this.unit = unit;
            this.targetRepo = targetRepo;
            this.alternateTitle = alternateTitle;
        }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            workspace.WriteFullPacket(unit, targetRepo, bodyTitle: alternateTitle);
            return new GitHubExistingIssueLookupResult
            {
                Classification = GitHubExistingIssueClassification.None,
            };
        }
    }
}
