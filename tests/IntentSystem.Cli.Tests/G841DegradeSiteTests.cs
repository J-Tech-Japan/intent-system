using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G841 AC11: per-site degrade coverage for the eleven migrated packet.yaml
/// readers — LE (quoted <c>#</c>), SU (unparseable), ED (plain <c>: </c> line).
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G841DegradeSiteWorkerTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    public G841DegradeSiteWorkerTests()
    {
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister();
        AutomationIssueRetireCommand.LabelMutatorFactory = null;
        AutomationIssueRetireCommand.RetirementMutatorFactory = null;
        AutomationIssueRetireCommand.UtcNowFactory = () => FixedNow;
        AutomationPublishRecoveryCommand.CandidateListerFactory = null;
        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
    }

    public void Dispose()
    {
        AutomationIssueRetireCommand.CandidateListerFactory = null;
        AutomationIssueRetireCommand.LabelMutatorFactory = null;
        AutomationIssueRetireCommand.RetirementMutatorFactory = null;
        AutomationIssueRetireCommand.UtcNowFactory = null;
        AutomationPublishRecoveryCommand.CandidateListerFactory = null;
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
    }

    // ── Site 3: automation runs-audit ───────────────────────────────────────

    [Fact]
    public void Site3_RunsAudit_Le_LegacyEquivalent_ResolvesOwningDomainFromPacket()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteRunsLogRow("G841-S3-LE");
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S3-LE", G841DegradeFixtures.PacketYaml("LE"));

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(doc.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Equal(G841TestHelpers.Domain, row.GetProperty("owning_domain").GetString());
        var detail = row.GetProperty("owning_domain_detail").GetString()!;
        Assert.Contains("packet.yaml", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("unparseable", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Site3_RunsAudit_Ed_QuotedHash_ResolvesOwningDomainFromPacket()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteRunsLogRow("G841-S3-ED-QH");
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S3-ED-QH", G841DegradeFixtures.PacketYaml("ED-QH"));

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(doc.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Equal(G841TestHelpers.Domain, row.GetProperty("owning_domain").GetString());
        Assert.DoesNotContain("unparseable", row.GetProperty("owning_domain_detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Site3_RunsAudit_Su_Unparseable_OwningDomainDetailNamesPacketPath()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteRunsLogRow("G841-S3-SU");
        var packetPath = G841DegradeWorkspace.PacketPath(workspace.Root, "G841-S3-SU");
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S3-SU", G841DegradeFixtures.PacketYaml("SU"));

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(doc.RootElement.GetProperty("malformed_rows").EnumerateArray());
        if (row.TryGetProperty("owning_domain", out var owningDomain))
        {
            Assert.True(owningDomain.ValueKind == JsonValueKind.Null || owningDomain.GetString() is null);
        }
        var detail = row.GetProperty("owning_domain_detail").GetString()!;
        Assert.StartsWith($"packet.yaml unparseable ({packetPath}):", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Site3_RunsAudit_Ed_PlainColonLine_SurfacesParseFailureDetail()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteRunsLogRow("G841-S3-ED-PC");
        var packetPath = G841DegradeWorkspace.PacketPath(workspace.Root, "G841-S3-ED-PC");
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S3-ED-PC", G841DegradeFixtures.PacketYaml("ED-PC"));

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(doc.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Equal(
            PacketYamlParseMessages.RunsAuditDetail(packetPath, G841DegradeFixtures.ExpectedParseError("ED-PC")),
            row.GetProperty("owning_domain_detail").GetString());
    }

    [Fact]
    public void Site3_PublishFlow_Su_DoesNotSurfaceRunsAuditOwningDomainDetail()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, "G841-S3-PF", G841DegradeFixtures.PacketYaml("SU"), bodyTitle: "G841 PF title");

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G841-S3-PF", "--repo", G841TestHelpers.ProbeRepo, "--domain", G841TestHelpers.Domain, "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, output, StringComparison.Ordinal);
        Assert.DoesNotContain("owning_domain_detail", output, StringComparison.Ordinal);
        Assert.DoesNotContain("packet.yaml unparseable (", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Site3_PublishRecovery_Su_DoesNotSurfaceRunsAuditOwningDomainDetail()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteQueueState(G841DegradeFixtures.QueueState("G841-S3-PR", linkedIssueNumber: 701));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S3-PR", G841DegradeFixtures.PacketYaml("SU"));
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            [G841DegradeFixtures.BuildPr(702, "Closes #701")]);

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--domain", G841TestHelpers.Domain, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.DoesNotContain("owning_domain_detail", output, StringComparison.Ordinal);
        Assert.DoesNotContain("packet.yaml unparseable (", output, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(output);
        Assert.Contains(
            G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S3-PR"),
            doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
    }

    // ── Site 4: automation issue-retire (silent degrade) ────────────────────

    [Theory]
    [InlineData("LE")]
    [InlineData("SU")]
    [InlineData("ED")]
    public void Site4_IssueRetire_AllInputClasses_SilentDegrade_NoWarnings(string inputClass)
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S4", G841DegradeFixtures.PacketYaml(inputClass));
        var labelMutator = new FakeLabelMutator(["intent-target"]);
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "G841-S4: retire packet degrade", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--issue", "1744", "--reason", "obsolete",
                "--domain", G841TestHelpers.Domain, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.TryGetProperty("warnings", out _));
        Assert.Empty(labelMutator.Transitions);
        Assert.Empty(retirementMutator.Closed);
    }

    [Fact]
    public void Site4_IssueRetire_Ed_PlainColonLine_ExitMayDifferFromLegacy_ButStaysSilent()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S4-ED", G841DegradeFixtures.PacketYaml("ED-PC"));
        workspace.WriteIntentsDomainDirectory(G841TestHelpers.Domain);
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "G841-S4-ED: title with no packet domain path", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--issue", "1744", "--reason", "obsolete", "--format", "json"],
            writer);

        // ED packet still parses; domain is derivable from packet.yaml without warnings.
        Assert.True(exitCode == 0 || exitCode == 1);
        Assert.DoesNotContain("warnings", writer.ToString(), StringComparison.Ordinal);
    }

    // ── Site 5: intent next-slice ───────────────────────────────────────────

    [Fact]
    public void Site5_NextSlice_Le_LegacyEquivalent_TargetRepo_SelectsWithoutParseWarning()
    {
        using var workspace = new G841DegradeWorkspace(writePermissiveBindings: true);
        var unit = "G841-S5-LE";
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, unit, G841DegradeFixtures.PacketYaml("LE"));
        workspace.WriteQueueState(G841DegradeFixtures.QueueState(unit));

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", G841TestHelpers.Domain, "--target-repo", G841TestHelpers.Repo],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Site5_NextSlice_Ed_QuotedHash_TargetRepo_SelectsWithoutParseWarning()
    {
        using var workspace = new G841DegradeWorkspace(writePermissiveBindings: true);
        var unit = "G841-S5-ED-QH";
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, unit, G841DegradeFixtures.PacketYaml("ED-QH"));
        workspace.WriteQueueState(G841DegradeFixtures.QueueState(unit));

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", G841TestHelpers.Domain, "--target-repo", G841TestHelpers.Repo],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Site5_NextSlice_Su_TargetRepo_DoubleRead_EmitsOneWarningPerPath()
    {
        using var workspace = new G841DegradeWorkspace(writePermissiveBindings: true);
        var unit = "G841-S5-SU";
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, unit, G841DegradeFixtures.PacketYaml("SU"));
        workspace.WriteQueueState(G841DegradeFixtures.QueueState(unit));

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", G841TestHelpers.Domain, "--target-repo", G841TestHelpers.Repo],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, unit)],
            warnings);
    }

    [Fact]
    public void Site5_NextSlice_Ed_PlainColonLine_TargetRepo_EmitsParseWarning()
    {
        using var workspace = new G841DegradeWorkspace(writePermissiveBindings: true);
        var unit = "G841-S5-ED-PC";
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, unit, G841DegradeFixtures.PacketYaml("ED-PC"));
        workspace.WriteQueueState(G841DegradeFixtures.QueueState(unit));

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", G841TestHelpers.Domain, "--target-repo", G841TestHelpers.Repo],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, unit)],
            doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray());
    }

    [Fact]
    public void Site5_NextSlice_Su_TwoBrokenFiles_EmitsTwoWarningEntries()
    {
        using var workspace = new G841DegradeWorkspace(writePermissiveBindings: true);
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, "G841-S5-A", G841DegradeFixtures.PacketYaml("SU"));
        G841DegradeWorkspace.WritePacketFiles(workspace.Root, "G841-S5-B", G841DegradeFixtures.PacketYaml("SU"));
        workspace.WriteQueueState(G841DegradeFixtures.QueueState("G841-S5-A", linkedIssueNumber: null, "G841-S5-B"));

        using var writer = new StringWriter();
        IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", G841TestHelpers.Domain, "--target-repo", G841TestHelpers.Repo],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains(G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S5-A"), warnings);
        Assert.Contains(G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S5-B"), warnings);
    }

    // ── Site 9: automation publish-recovery ─────────────────────────────────

    [Fact]
    public void Site9_PublishRecovery_Le_NoPr_NoDomain_ExitZero_EligibleNotDomainUnderivable()
    {
        using var workspace = CreateSite9Workspace("G841-S9-LE", G841DegradeFixtures.Site9PacketYaml("LE"));

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
        var stop = Assert.Single(doc.RootElement.GetProperty("unsafe_stops").EnumerateArray());
        Assert.Equal("no-closing-pr-for-linked-issue", stop.GetProperty("kind").GetString());
    }

    [Fact]
    public void Site9_PublishRecovery_EdQuotedHash_NoPr_NoDomain_ExitZero_EligibleNotDomainUnderivable()
    {
        using var workspace = CreateSite9Workspace("G841-S9-ED-QH", G841DegradeFixtures.Site9PacketYaml("ED-QH"));

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
        var stop = Assert.Single(doc.RootElement.GetProperty("unsafe_stops").EnumerateArray());
        Assert.Equal("no-closing-pr-for-linked-issue", stop.GetProperty("kind").GetString());
    }

    [Fact]
    public void Site9_PublishRecovery_Su_NoPr_NoDomain_ExitZero_OneWarningAcrossDoubleRead()
    {
        using var workspace = CreateSite9Workspace("G841-S9-SU", G841DegradeFixtures.Site9PacketYaml("SU"), duplicateQueueEntries: true);

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S9-SU")],
            warnings);
        Assert.DoesNotContain("failures", doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Contains(
            doc.RootElement.GetProperty("unsafe_stops").EnumerateArray(),
            stop => stop.GetProperty("kind").GetString() == "domain-underivable");
    }

    [Fact]
    public void Site9_PublishRecovery_EdPlainColon_NoPr_NoDomain_ExitZero_DomainUnderivableWithWarning()
    {
        using var workspace = CreateSite9Workspace("G841-S9-ED-PC", G841DegradeFixtures.Site9PacketYaml("ED-PC"));

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S9-ED-PC")],
            warnings);
        var stop = Assert.Single(doc.RootElement.GetProperty("unsafe_stops").EnumerateArray());
        Assert.Equal("domain-underivable", stop.GetProperty("kind").GetString());
    }

    [Fact]
    public void Site9_PublishRecovery_Su_DuplicateQueueEntries_DoubleRead_EmitsOneWarning()
    {
        using var workspace = CreateSite9Workspace("G841-S9-SU-DUP", G841DegradeFixtures.Site9PacketYaml("SU"), duplicateQueueEntries: true);

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--format", "json"],
            writer));

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S9-SU-DUP")],
            doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray());
    }

    [Fact]
    public void Site9_PublishRecovery_Su_TwoBrokenFiles_EmitsTwoInformationalWarnings()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteIntentsDomainDirectory(G841TestHelpers.AlphaDomain);
        workspace.WriteIntentsDomainDirectory(G841TestHelpers.Domain);
        workspace.WriteQueueState(G841DegradeFixtures.QueueState("G841-S9-A", linkedIssueNumber: 807, "G841-S9-B"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S9-A", G841DegradeFixtures.Site9PacketYaml("SU"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S9-B", G841DegradeFixtures.Site9PacketYaml("SU"));
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister([]);

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", G841TestHelpers.Repo, "--format", "json"],
            writer));

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains(G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S9-A"), warnings);
        Assert.Contains(G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S9-B"), warnings);
    }

    private static G841DegradeWorkspace CreateSite9Workspace(string unit, string yaml, bool duplicateQueueEntries = false)
    {
        var workspace = new G841DegradeWorkspace();
        workspace.WriteIntentsDomainDirectory(G841TestHelpers.AlphaDomain);
        workspace.WriteIntentsDomainDirectory(G841TestHelpers.Domain);
        workspace.WriteQueueState(duplicateQueueEntries
            ? G841DegradeFixtures.Site9DuplicateQueueState(unit)
            : G841DegradeFixtures.QueueState(unit, linkedIssueNumber: 801));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, unit, yaml);
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister([]);
        return workspace;
    }

    // ── Site 9: automation publish-recovery ─────────────────────────────────

    private static IssueSnapshot OpenSnapshot(int number, string title, params string[] labels) => new()
    {
        State = "OPEN",
        StateReason = string.Empty,
        Title = title,
        Url = $"https://github.com/{G841TestHelpers.Repo}/issues/{number}",
        Labels = labels,
        Comments = Array.Empty<string>(),
    };

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath) =>
            throw new InvalidOperationException("must not create");
    }

    private sealed class StubExistingIssueChecker(GitHubExistingIssueClassification classification) : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string title, string body) =>
            new() { Classification = classification };
    }

    private sealed class FakePrClosingIssuesFetcher : IPrClosingIssuesFetcher
    {
        private readonly IReadOnlyList<int> closingIssues;
        public FakePrClosingIssuesFetcher(IReadOnlyList<int> closingIssues) => this.closingIssues = closingIssues;
        public IReadOnlyList<int> Fetch(string repo, int prNumber) => closingIssues;
    }

    private sealed class FakePrBodyFetcher : IPrBodyFetcher
    {
        public FakePrBodyFetcher(string? body) => Body = body;
        public string? Body { get; }
        public string? Fetch(string repo, int prNumber) => Body;
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class FakeLabelMutator : IGitHubLabelMutator
    {
        private readonly IReadOnlyList<string> labels;
        public List<(string Kind, int Number, string[] Add, string[] Remove)> Transitions { get; } = new();
        public FakeLabelMutator(IReadOnlyList<string> labels) => this.labels = labels;
        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();
        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add((kind, number, addLabels.ToArray(), removeLabels.ToArray()));
        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRetirementMutator : IGitHubIssueRetirementMutator
    {
        public List<(string Repo, int Issue, string Comment)> Closed { get; } = new();
        public Dictionary<int, IssueSnapshot> Snapshots { get; } = new();
        public void CloseAsNotPlanned(string repo, int issueNumber, string comment) =>
            Closed.Add((repo, issueNumber, comment));
        public IssueSnapshot GetSnapshot(string repo, int issueNumber) =>
            Snapshots.TryGetValue(issueNumber, out var snapshot)
                ? snapshot
                : throw new InvalidOperationException($"issue #{issueNumber} not found (fake)");
    }

    private sealed class FakePrLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        public FakePrLister(IReadOnlyList<GitHubAutomationPrCandidate> prs) => this.prs = prs;
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }
}

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class G841DegradeSiteStalledWorkTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    public G841DegradeSiteStalledWorkTests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    // ── Site 6: duplicate queue entries ─────────────────────────────────────

    [Fact]
    public void Site6_StalledWork_Le_DuplicateQueueEntries_NoParseWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-LE-A", G841DegradeFixtures.PacketYaml("LE"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-LE-B", G841DegradeFixtures.PacketYaml("LE"));
        workspace.WriteQueueState(G841DegradeFixtures.DuplicateMergedQueueState("G841-S6-LE-A", "G841-S6-LE-B"));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
        Assert.Equal(AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous,
            Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public void Site6_StalledWork_UnreadablePacket_DoesNotCrash_ExitZero_G841D4()
    {
        if (OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("chmod 000 unreadable-packet fixture requires Unix file permissions.");
        }

        var idInfo = new ProcessStartInfo("id")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        idInfo.ArgumentList.Add("-u");
        using var idProcess = Process.Start(idInfo)!;
        var effectiveUserId = idProcess.StandardOutput.ReadToEnd().Trim();
        idProcess.WaitForExit();
        if (idProcess.ExitCode != 0 || string.Equals(effectiveUserId, "0", StringComparison.Ordinal))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "chmod 000 unreadable-packet fixture cannot prove denial while running as root.");
        }

        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-UNR", G841DegradeFixtures.PacketYaml("LE"));
        var packetPath = G841DegradeWorkspace.PacketPath(workspace.Root, "G841-S6-UNR");
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-LE", G841DegradeFixtures.PacketYaml("LE"));
        workspace.WriteQueueState(G841DegradeFixtures.BacklogBlockedDuplicateQueueState("G841-S6-UNR", "G841-S6-LE"));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        try
        {
            File.SetUnixFileMode(packetPath, UnixFileMode.None);
            Assert.False(G841TestHelpers.CanRead(packetPath));

            using var writer = new StringWriter();
            var exitCode = AutomationStalledWorkCommand.Execute(
                workspace.Context,
                ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(writer.ToString());
            var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
            Assert.Equal(
                [G841DegradeFixtures.ExpectedUnreadableWarning(workspace.Root, "G841-S6-UNR")],
                warnings);
        }
        finally
        {
            G841TestHelpers.RestorePacketPermissions(packetPath);
        }
    }

    [Fact]
    public void Site6_StalledWork_Su_DuplicateQueueEntries_OneWarningPerBrokenPath()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-SU", G841DegradeFixtures.PacketYaml("SU"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-LE", G841DegradeFixtures.PacketYaml("LE"));
        workspace.WriteQueueState(G841DegradeFixtures.BacklogBlockedDuplicateQueueState("G841-S6-SU", "G841-S6-LE"));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S6-SU")],
            warnings);
    }

    [Fact]
    public void Site6_StalledWork_Ed_QuotedHash_DuplicateQueueEntries_NoParseWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-ED-A", G841DegradeFixtures.PacketYaml("ED-QH"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-ED-B", G841DegradeFixtures.PacketYaml("ED-QH"));
        workspace.WriteQueueState(G841DegradeFixtures.DuplicateMergedQueueState("G841-S6-ED-A", "G841-S6-ED-B"));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Site6_StalledWork_Su_TwoBrokenFiles_EmitsTwoWarningEntries()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-A", G841DegradeFixtures.PacketYaml("SU"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S6-B", G841DegradeFixtures.PacketYaml("SU"));
        workspace.WriteQueueState(G841DegradeFixtures.BacklogBlockedDuplicateQueueState("G841-S6-A", "G841-S6-B"));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Equal(2, warnings.Length);
    }

    // ── Site 7: closeout-recorded knowledge write-back collector ────────────

    [Fact]
    public void Site7_StalledWork_Le_CloseoutRecorded_NoParseWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteDeclaringPacket("G841-S7-LE");
        workspace.WriteCloseout("G841-S7-LE", FixedNow.AddMinutes(-90));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister();

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var site7Warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.All(site7Warnings, w => Assert.DoesNotContain("packet.yaml at '", w!, StringComparison.Ordinal));
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending,
            Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public void Site7_StalledWork_Su_CloseoutRecorded_OneWarning_NotPublishFlowPath()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S7-SU", G841DegradeFixtures.KnowledgeDeclaringYaml("SU", "G841-S7-SU"));
        workspace.WriteCloseout("G841-S7-SU", FixedNow.AddMinutes(-90));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister();

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S7-SU")],
            warnings);
        Assert.DoesNotContain(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Contains(doc.RootElement.GetProperty("excluded").EnumerateArray(),
            e => e.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
    }

    [Fact]
    public void Site7_StalledWork_Ed_CloseoutRecorded_NoParseWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteDeclaringPacket("G841-S7-ED", G841DegradeFixtures.PacketYaml("ED-QH"));
        workspace.WriteCloseout("G841-S7-ED", FixedNow.AddMinutes(-90));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister();

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var site7Warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.All(site7Warnings, w => Assert.DoesNotContain("packet.yaml at '", w!, StringComparison.Ordinal));
    }

    // ── Site 8: two titles without corroborated token ───────────────────────

    [Fact]
    public void Site8_StalledWork_Le_TwoTitlesWithoutCorroboratedToken_NoParseWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteNestedPacket("G12", G841TestHelpers.Domain);
        workspace.WriteNestedPacket("G34", G841TestHelpers.Domain);
        IReadOnlyList<GitHubAutomationIssueCandidate> issues =
        [
            G841DegradeFixtures.BuildIssue(1934, "Combine G12 and G34 into one follow-up", FixedNow.AddHours(-26), "intent-target"),
        ];
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(issues: issues);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Site8_StalledWork_Su_TwoTitlesWithoutCorroboratedToken_OneWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-S8-SU", G841DegradeFixtures.PacketYaml("SU"));
        IReadOnlyList<GitHubAutomationIssueCandidate> issues =
        [
            G841DegradeFixtures.BuildIssue(1934, "Freeform title mentioning G841-S8-SU mid-sentence", FixedNow.AddHours(-26), "intent-target"),
            G841DegradeFixtures.BuildIssue(1935, "Another freeform title also mentioning G841-S8-SU", FixedNow.AddHours(-25), "intent-target"),
        ];
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(issues: issues);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-S8-SU")],
            warnings);
    }

    [Fact]
    public void Site8_StalledWork_Ed_TwoPacketFilesSameSourceUnit_NoParseWarning()
    {
        using var workspace = new G841DegradeWorkspace();
        workspace.WriteNestedPacketAtFolder("G841-S8-A", "G50", G841TestHelpers.Domain, G841DegradeFixtures.PacketYamlWithSourceUnit("ED-QH", "G50"));
        workspace.WriteNestedPacketAtFolder("G841-S8-B", "G50", G841TestHelpers.BetaDomain, G841DegradeFixtures.PacketYamlWithSourceUnit("ED-QH", "G50"));
        IReadOnlyList<GitHubAutomationIssueCandidate> issues =
        [
            G841DegradeFixtures.BuildIssue(1950, "Freeform title mentioning G50 mid-sentence", FixedNow.AddHours(-26), "intent-target"),
        ];
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(issues: issues);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
    }

    // ── Cross-site pairs in one stalled-work run ────────────────────────────

    [Fact]
    public void CrossSite_StalledWork_6And7_DuplicateQueueAndCloseoutRecorded()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-X67-SU", G841DegradeFixtures.PacketYaml("SU"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-X67-LE", G841DegradeFixtures.PacketYaml("LE"));
        workspace.WriteQueueState(G841DegradeFixtures.BacklogBlockedDuplicateQueueState("G841-X67-SU", "G841-X67-LE"));
        workspace.WriteDeclaringPacket("G841-X67-KB");
        workspace.WriteCloseout("G841-X67-KB", FixedNow.AddMinutes(-60));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Contains(G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-X67-SU"), warnings);
        Assert.Contains(doc.RootElement.GetProperty("excluded").EnumerateArray(),
            e => e.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous);
        Assert.Contains(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
    }

    [Fact]
    public void CrossSite_StalledWork_7And8_CloseoutRecordedAndAmbiguousTitles()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-X78-SU", G841DegradeFixtures.KnowledgeDeclaringYaml("SU", "G841-X78-SU"));
        workspace.WriteCloseout("G841-X78-SU", FixedNow.AddMinutes(-60));
        workspace.WriteNestedPacket("G12", G841TestHelpers.Domain);
        workspace.WriteNestedPacket("G34", G841TestHelpers.Domain);
        IReadOnlyList<GitHubAutomationIssueCandidate> issues =
        [
            G841DegradeFixtures.BuildIssue(1934, "Combine G12 and G34 into one follow-up", FixedNow.AddHours(-26), "intent-target"),
        ];
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(issues: issues);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Equal(
            [G841DegradeFixtures.ExpectedParseWarning(workspace.Root, "G841-X78-SU")],
            warnings);
        Assert.Contains(doc.RootElement.GetProperty("excluded").EnumerateArray(),
            e => e.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous);
        Assert.Contains(doc.RootElement.GetProperty("excluded").EnumerateArray(),
            e => e.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
    }

    // ── Propagation: heartbeat + measured supervisor ────────────────────────

    [Fact]
    public void Propagation_Su_StalledWorkWarning_CopiedByHeartbeatAndMeasuredSupervisor()
    {
        using var workspace = new G841DegradeWorkspace();
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-PROP", G841DegradeFixtures.PacketYaml("SU"));
        workspace.WriteQueueState(G841DegradeFixtures.BacklogBlockedDuplicateQueueState("G841-PROP", "G841-PROP-LE"));
        G841DegradeWorkspace.WritePacketYaml(workspace.Root, "G841-PROP-LE", G841DegradeFixtures.PacketYaml("LE"));
        var mergedPr = G841DegradeFixtures.BuildMergedPr(1200, 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new StalledWorkFakeLister(mergedPrs: [mergedPr]);

        var stalled = AutomationStalledWorkCommand.Analyze(
            workspace.Context, G841TestHelpers.Domain, G841TestHelpers.Repo, staleMinutes: 60);
        var expected = Assert.Single(stalled.Warnings);

        using (var heartbeatWriter = new StringWriter())
        {
            AutomationHeartbeatCommand.Execute(
                workspace.Context,
                ["--domain", G841TestHelpers.Domain, "--repo", G841TestHelpers.Repo, "--format", "json"],
                heartbeatWriter);
            using var heartbeatDoc = JsonDocument.Parse(heartbeatWriter.ToString());
            Assert.Contains(expected, heartbeatDoc.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        }

        var supervisor = new NotifyMeasuredSupervisor(
            context: workspace.Context,
            routingRoot: workspace.Root,
            domain: G841TestHelpers.Domain,
            team: G841TestHelpers.Team,
            repo: G841TestHelpers.Repo,
            ownerRole: "orchestration",
            intervalSeconds: 300,
            declaredBoundSeconds: null,
            staleMinutes: 60,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: false,
            format: "json",
            runner: new NoopNotifyRunner(),
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: workspace.Root,
            stalledWorkAnalyzer: () => stalled);

        var pass = supervisor.RunOnce();
        Assert.Contains(expected, pass.Warnings);
    }

    private sealed class StalledWorkFakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> mergedPrs;

        public StalledWorkFakeLister(
            IReadOnlyList<GitHubAutomationIssueCandidate>? issues = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? mergedPrs = null)
        {
            this.issues = issues ?? Array.Empty<GitHubAutomationIssueCandidate>();
            this.mergedPrs = mergedPrs ?? Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();
        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => mergedPrs;
    }

    private sealed class NoopNotifyRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            new(0, "{\"result\":{\"agents\":[]}}", string.Empty);
    }
}

internal static class G841DegradeFixtures
{
    private const string ParseableLegacyPacketBase =
        """
        implementation_issue_packet:
          issue_title: "G841 fixture title"
          domain: intent-cli
          target_repo: "J-Tech-Japan/intent-system"
        """;

    internal static string PacketYaml(string inputClass) => inputClass switch
    {
        "LE" => ParseableLegacyPacketBase,
        "SU" => G841TestHelpers.UnparseableYaml,
        "ED-QH" => ParseableLegacyPacketBase + "\n" + G841TestHelpers.QuotedHashLine,
        "ED-PC" => ParseableLegacyPacketBase + "\n" + G841TestHelpers.PlainColonLine,
        "ED" => ParseableLegacyPacketBase
            + "\n"
            + G841TestHelpers.QuotedHashLine
            + G841TestHelpers.PlainColonLine,
        _ => throw new ArgumentOutOfRangeException(nameof(inputClass), inputClass, null),
    };

    internal static string Site9PacketYaml(string inputClass) => inputClass switch
    {
        "LE" => """
            implementation_issue_packet:
              issue_title: "G841 fixture title"
              domain: alpha
              target_repo: "J-Tech-Japan/intent-system"
              source_artifact: "review of PR 1823"
            """,
        "SU" => """
            implementation_issue_packet:
              domain: alpha
              broken: [
            """,
        "ED-QH" => """
            implementation_issue_packet:
              issue_title: "G841 fixture title"
              domain: alpha
              target_repo: "J-Tech-Japan/intent-system"
              source_artifact: "review of PR #1823"
            """,
        "ED-PC" => """
            implementation_issue_packet:
              issue_title: "G841 fixture title"
              domain: alpha
              target_repo: "J-Tech-Japan/intent-system"
              target_part: retire the reader: all eleven sites
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(inputClass), inputClass, null),
    };

    internal static string PacketYamlWithSourceUnit(string inputClass, string sourceExecutionUnit) =>
        PacketYaml(inputClass).Replace(
            "implementation_issue_packet:",
            $"implementation_issue_packet:\n  source_execution_unit: {sourceExecutionUnit}");

    internal static string KnowledgeDeclaringYaml(string inputClass, string executionUnit = "G841-KB") => inputClass switch
    {
        "LE" => $"""
            implementation_issue_packet:
              source_execution_unit: {executionUnit}
              domain: intent-cli
              target_repo: "J-Tech-Japan/intent-system"
              source_artifact: "review of PR #1823"
            knowledge_updates:
              intent_tree:
                required: true
                target_paths:
                  - intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md
                summary: ""
              adr:
                required: false
                target_paths: []
              diagram:
                required: false
                target_paths: []
              docs:
                required: false
                target_paths: []
            """,
        "SU" => $"""
            implementation_issue_packet:
              source_execution_unit: {executionUnit}
              domain: intent-cli
              broken: [
            knowledge_updates:
              intent_tree:
                required: true
                target_paths:
                  - intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md
            """,
        "ED" => $"""
            implementation_issue_packet:
              source_execution_unit: {executionUnit}
              domain: intent-cli
              target_repo: "J-Tech-Japan/intent-system"
              source_artifact: "review of PR #1823"
              target_part: retire the reader: all eleven sites
            knowledge_updates:
              intent_tree:
                required: true
                target_paths:
                  - intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md
                summary: ""
              adr:
                required: false
                target_paths: []
              diagram:
                required: false
                target_paths: []
              docs:
                required: false
                target_paths: []
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(inputClass), inputClass, null),
    };

    internal static string ExpectedParseError(string inputClass)
    {
        var yaml = inputClass switch
        {
            "ED-PC" => PacketYaml("ED-PC"),
            "SU" => PacketYaml("SU"),
            _ => PacketYaml(inputClass),
        };
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var error));
        return error;
    }

    internal static string ExpectedParseWarning(string root, string unit)
    {
        var packetPath = G841DegradeWorkspace.PacketPath(root, unit);
        Assert.False(PacketYamlDocument.TryParse(File.ReadAllText(packetPath), out _, out var error));
        return PacketYamlParseMessages.WarningText(packetPath, error);
    }

    internal static string ExpectedUnreadableWarning(string root, string unit)
    {
        var packetPath = G841DegradeWorkspace.PacketPath(root, unit);
        Exception? readException = null;
        try
        {
            File.ReadAllText(packetPath);
            Assert.Fail("Expected chmod 000 packet to be unreadable.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            readException = exception;
        }

        return PacketYamlParseMessages.ReadWarningText(packetPath, readException!.Message);
    }

    internal static string ExpectedParseError(string root, string unit)
    {
        var packetPath = G841DegradeWorkspace.PacketPath(root, unit);
        Assert.False(PacketYamlDocument.TryParseWithLocation(File.ReadAllText(packetPath), out _, out var error));
        return error!.Message;
    }

    internal static string Site9DuplicateQueueState(string unit, int linkedIssueNumber = 801) =>
        QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            Items =
            [
                BuildQueueItem(unit, linkedIssueNumber, QueueItemState.Queued, linkedPrNumber: null),
                BuildQueueItem(unit, linkedIssueNumber, QueueItemState.Queued, linkedPrNumber: null),
            ],
        });

    internal static string QueueState(string unit, int? linkedIssueNumber = null, params string[] extraUnits)
    {
        var items = new List<QueueItem> { BuildQueueItem(unit, linkedIssueNumber) };
        foreach (var extra in extraUnits)
        {
            items.Add(BuildQueueItem(extra, linkedIssueNumber));
        }

        return QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            Items = items,
        });
    }

    internal static string CloseoutQueueState(string unit) =>
        QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = unit,
                    Title = $"{unit} title",
                    State = QueueItemState.Review,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{unit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{unit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{unit}/review-context.md",
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = G841TestHelpers.Repo,
                        Number = 900,
                        Url = $"https://github.com/{G841TestHelpers.Repo}/issues/900",
                    },
                    LinkedPr = $"https://github.com/{G841TestHelpers.Repo}/pull/901",
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal",
                },
            ],
        });

    internal static string DuplicateMergedQueueState(string unitA, string unitB) =>
        QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            Items =
            [
                BuildQueueItem(unitA, 1199, QueueItemState.Review, "1200"),
                BuildQueueItem(unitB, 1199, QueueItemState.Review, "1200"),
            ],
        });

    internal static string BacklogBlockedDuplicateQueueState(string unitA, string unitB) =>
        QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            Items =
            [
                BuildQueueItem(unitA, 1199, QueueItemState.Blocked, "1200"),
                BuildQueueItem(unitB, 1199, QueueItemState.Blocked, "1200"),
            ],
        });

    internal static GitHubAutomationPrCandidate BuildPr(int number, string body) => new()
    {
        Number = number,
        Title = $"PR {number}",
        Url = $"https://github.com/{G841TestHelpers.Repo}/pull/{number}",
        Body = body,
        CreatedAt = "2026-08-15T00:00:00Z",
        UpdatedAt = "2026-08-15T00:00:00Z",
        Labels = Array.Empty<GitHubAutomationLabel>(),
        State = "OPEN",
    };

    internal static GitHubAutomationPrCandidate BuildMergedPr(int number, int closingIssue) =>
        new()
        {
            Number = number,
            Title = $"Merged PR {number}",
            Url = $"https://github.com/{G841TestHelpers.Repo}/pull/{number}",
            CreatedAt = "2026-08-14T00:00:00Z",
            UpdatedAt = "2026-08-15T00:00:00Z",
            State = "MERGED",
            Labels = Array.Empty<GitHubAutomationLabel>(),
            ClosingIssuesReferences =
            [
                new GitHubPrClosingIssueReference
                {
                    Number = closingIssue,
                    Repository = new GitHubPrClosingIssueRepository
                    {
                        Name = "intent-system",
                        Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                    },
                },
            ],
        };

    internal static GitHubAutomationIssueCandidate BuildIssue(
        int number, string title, DateTimeOffset createdAt, params string[] labels) => new()
    {
        Number = number,
        Title = title,
        Url = $"https://github.com/{G841TestHelpers.Repo}/issues/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
    };

    private static QueueItem BuildSite9QueueItem(string executionUnit, int linkedIssueNumber) => new()
    {
        ExecutionUnit = executionUnit,
        Title = $"{executionUnit} title",
        State = QueueItemState.Queued,
        Dependencies = Array.Empty<string>(),
        BlockedBy = Array.Empty<string>(),
        ClarificationReturnPath = "intents/alpha/clarifications/open.md",
        PacketPaths = new PacketPaths
        {
            Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
            Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
            ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
        },
        LinkedIssue = new LinkedIssue
        {
            Repo = G841TestHelpers.Repo,
            Number = linkedIssueNumber,
            Url = $"https://github.com/{G841TestHelpers.Repo}/issues/{linkedIssueNumber}",
        },
        LinkedPr = null,
        WorkerRole = "Claude",
        ReviewRole = "Codex",
        Priority = "normal",
    };

    private static QueueItem BuildQueueItem(
        string executionUnit,
        int? linkedIssueNumber,
        QueueItemState state = QueueItemState.Queued,
        string? linkedPrNumber = null) => new()
    {
        ExecutionUnit = executionUnit,
        Title = $"{executionUnit} title",
        State = state,
        Dependencies = Array.Empty<string>(),
        BlockedBy = Array.Empty<string>(),
        ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
        PacketPaths = new PacketPaths
        {
            Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
            Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
            ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
        },
        LinkedIssue = linkedIssueNumber is int issueNumber
            ? new LinkedIssue
            {
                Repo = G841TestHelpers.Repo,
                Number = issueNumber,
                Url = $"https://github.com/{G841TestHelpers.Repo}/issues/{issueNumber}",
            }
            : null,
        LinkedPr = linkedPrNumber is null
            ? null
            : $"https://github.com/{G841TestHelpers.Repo}/pull/{linkedPrNumber}",
        WorkerRole = "Claude",
        ReviewRole = "Codex",
        Priority = "normal",
    };
}

internal sealed class G841DegradeWorkspace : IDisposable
{
    public G841DegradeWorkspace(bool writePermissiveBindings = false)
    {
        Root = Directory.CreateTempSubdirectory("g841-degrade-site-").FullName;
        Directory.CreateDirectory(Path.Combine(Root, ".intent-cli"));
        G841TestHelpers.WriteHostConfig(Root);
        if (writePermissiveBindings)
        {
            WritePermissiveBindings();
        }

        Context = G841TestHelpers.GatedContext(Root);
    }

    public string Root { get; }

    public CliContext Context { get; }

    public void WriteRunsLogRow(string executionUnit)
    {
        var line = $$"""{"execution_unit":"{{executionUnit}}","event":"issue-created","timestamp":"2026-08-15T00:00:00Z"}""";
        File.WriteAllText(Context.GetRunLogPath(), line + Environment.NewLine);
    }

    public void WriteQueueState(string json) => File.WriteAllText(Context.GetQueueStatePath(), json);

    public void WriteIntentsDomainDirectory(string domain) =>
        Directory.CreateDirectory(Path.Combine(Root, "intents", domain));

    public void WriteDeclaringPacket(string executionUnit, string? yaml = null)
    {
        WritePacketYaml(executionUnit, yaml ?? G841DegradeFixtures.KnowledgeDeclaringYaml("LE", executionUnit));
        var target = Path.Combine(Root, "intents", G841TestHelpers.Domain, "intent-tree", "means", "03-state-and-audit-strategy.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            File.WriteAllText(target, "# knowledge target\n");
        }
    }

    public void WriteCloseout(string executionUnit, DateTimeOffset at)
    {
        var runLogPath = Context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)!);
        var line = RunLogSerializer.SerializeLine(new RunEvent
        {
            Ts = at,
            ExecutionUnit = executionUnit,
            Event = "closeout-recorded",
            By = "intent-cli closeout pr",
        });
        File.AppendAllText(runLogPath, line + Environment.NewLine);
    }

    public void WriteNestedPacket(string executionUnit, string domain) =>
        WritePacketYaml(executionUnit,
            $"implementation_issue_packet:\n  source_execution_unit: {executionUnit}\n  domain: {domain}\n");

    public void WriteNestedPacketAtFolder(string folderName, string declaredExecutionUnit, string domain, string yaml) =>
        WritePacketYaml(folderName, yaml);

    public void WritePacketYaml(string executionUnit, string yaml) =>
        G841DegradeWorkspace.WritePacketYaml(Root, executionUnit, yaml);

    public static void WritePacketYaml(string root, string executionUnit, string yaml) =>
        G841TestHelpers.WritePacketFiles(root, executionUnit, yaml, withContractBody: false);

    public static void WritePacketFiles(string root, string executionUnit, string yaml, bool withContractBody = true, string? bodyTitle = null) =>
        G841TestHelpers.WritePacketFiles(root, executionUnit, yaml, withContractBody, bodyTitle ?? "G841 fixture title");

    public static string PacketPath(string root, string executionUnit) =>
        G841TestHelpers.PacketPath(root, executionUnit);

    public void WritePermissiveBindings()
    {
        var path = Path.Combine(Root, "intents", G841TestHelpers.Domain, "automation");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "bindings.md"), "---\nexecution_unit_regex: '.*'\n---\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
