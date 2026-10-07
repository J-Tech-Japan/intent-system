using IntentSystem.Cli.Commands;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Tests;

public sealed class GuideReachabilityPlacementTests
{
    private const string OffendingPath = "implementation_issue_packet.guide_reachability";
    private const string ExpectedPath = "guide_reachability";

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("route")]
    [InlineData("null")]
    [InlineData("")]
    public void DirectNestedDeclarationIsRejectedRegardlessOfValue(string value)
    {
        var yaml = value.Length == 0
            ? "implementation_issue_packet:\n  guide_reachability:\n"
            : $"implementation_issue_packet:\n  guide_reachability: {value}\n";

        AssertMisplaced(yaml);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothRootAndNestedDeclarationsAreRejectedRegardlessOfOrder(bool nestedFirst)
    {
        const string nested = "implementation_issue_packet:\n  guide_reachability: null\n";
        const string root = "guide_reachability:\n  no_role_facing_surface: true\n  routes: []\n";

        AssertMisplaced(nestedFirst ? nested + root : root + nested);
    }

    [Fact]
    public void MisplacedKeyLocationIsReportedForMultilineAndFlowMappings()
    {
        AssertMisplaced("implementation_issue_packet:\n  guide_reachability: null\n");
        AssertMisplaced("{ implementation_issue_packet: { guide_reachability: null } }");
    }

    [Fact]
    public void ExplicitStringTaggedParentKeyStillRejectsNestedNullDeclaration()
    {
        const string yaml = "!!str implementation_issue_packet:\n  guide_reachability: null\n";
        var stream = new YamlStream();
        using (var reader = new StringReader(yaml))
        {
            stream.Load(reader);
        }

        var root = Assert.IsType<YamlMappingNode>(Assert.Single(stream.Documents).RootNode);
        Assert.False(root.Children.TryGetValue(new YamlScalarNode("implementation_issue_packet"), out _));

        AssertMisplaced(yaml);
    }

    [Fact]
    public void ExplicitStringTaggedParentControlsRemainAccepted()
    {
        AssertAccepted("!!str implementation_issue_packet:\n  source_execution_unit: G854\n", declared: false);
        AssertAccepted(
            "!!str implementation_issue_packet: {}\n"
                + "guide_reachability:\n  no_role_facing_surface: true\n  routes: []\n",
            declared: true);

        const string unrelatedTagged = """
            !!str unrelated_metadata:
              guide_reachability: unrelated
            implementation_issue_packet:
              source_execution_unit: G854
            """;
        AssertAccepted(unrelatedTagged, declared: false);
    }

    [Fact]
    public void TaggedAndUntaggedMatchingParentMappingsAreBothCheckedWhenYamlPreservesThem()
    {
        const string yaml = """
            implementation_issue_packet: {}
            !!str implementation_issue_packet:
              guide_reachability: null
            """;
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // This YAML parser version may treat tagged and implicit string
            // keys as duplicates before a node tree exists. The single tagged
            // parent case above still exercises the value-based lookup fix.
            return;
        }

        var root = Assert.IsType<YamlMappingNode>(Assert.Single(stream.Documents).RootNode);
        var matchingParents = root.Children.Keys
            .OfType<YamlScalarNode>()
            .Count(key => string.Equals(key.Value, "implementation_issue_packet", StringComparison.Ordinal));
        if (matchingParents < 2)
        {
            return;
        }

        AssertMisplaced(yaml);
    }

    [Fact]
    public void AbsentDeclarationAndValidPacketRootFormsRemainAccepted()
    {
        const string legacy = "implementation_issue_packet:\n  source_execution_unit: G854\n";
        AssertAccepted(legacy, declared: false);
        AssertAccepted(
            legacy + "guide_reachability:\n  no_role_facing_surface: true\n  routes: []\n",
            declared: true);
        AssertAccepted(
            legacy + "guide_reachability:\n  guide_surface: guide solo-conductor\n  role: design\n  target_surface: step 3\n",
            declared: true);
        AssertAccepted(
            legacy + "guide_reachability:\n  - guide_surface: guide solo-conductor\n    role: design\n    target_surface: step 3\n",
            declared: true);
    }

    [Fact]
    public void SameNamedKeysInUnrelatedMappingsAndUnknownFieldsRemainAccepted()
    {
        const string yaml = """
            implementation_issue_packet:
              source_execution_unit: G854
              unknown_field:
                guide_reachability: retained
            unrelated_metadata:
              guide_reachability: also-retained
            another_unknown_field: accepted
            """;

        Assert.True(PacketYamlDocument.TryParse(yaml, out var parsed, out var parseError), parseError);
        Assert.True(PacketYamlDocument.TryParseWithLocation(yaml, out _, out var locationError), locationError?.Message);
        Assert.True(parsed!.Fields.ContainsKey("implementation_issue_packet.unknown_field.guide_reachability"));
        Assert.True(parsed.Fields.ContainsKey("unrelated_metadata.guide_reachability"));
        Assert.False(GuideReachabilityDeclaration.Read(yaml).IsDeclared);
    }

    [Fact]
    public void ReadinessRefusesMisplacedDeclarationAfterAllOtherInputsAreComplete()
    {
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G854",
            PacketYaml = """
                implementation_issue_packet:
                  source_execution_unit: Z4R-G854
                  issue_title: Demo packet
                  target_repo: J-Tech-Creations/Zero4Racer
                  guide_reachability: null
                """,
            ImplementationMarkdown = "# implementation\n",
            ReviewContextMarkdown = "# review context\n",
            GithubBodyMarkdown = """
                # Demo packet
                ## Goal
                Demo.
                ## Why This Slice Exists Now
                Demo.
                ## Current Observed State
                Demo.
                ## Accepted Baseline You May Assume
                Demo.
                ## Target Repo / Path / Part
                Demo.
                ## In Scope
                Demo.
                ## Out Of Scope
                Demo.
                ## Acceptance Criteria
                Demo.
                ## Verification
                Demo.
                ## Related Links
                Demo.
                ## Base Branch Policy
                Demo.
                """,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, result.Reason);
        Assert.Contains(OffendingPath, result.Summary, StringComparison.Ordinal);
        Assert.Contains(ExpectedPath, result.Summary, StringComparison.Ordinal);
    }

    private static void AssertAccepted(string yaml, bool declared)
    {
        Assert.True(PacketYamlDocument.TryParse(yaml, out _, out var parseError), parseError);
        Assert.True(PacketYamlDocument.TryParseWithLocation(yaml, out _, out var locationError), locationError?.Message);
        Assert.Equal(declared, GuideReachabilityDeclaration.Read(yaml).IsDeclared);
    }

    private static void AssertMisplaced(string yaml)
    {
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var message));
        Assert.Equal(GuideReachabilityPlacement.ErrorMessage, message);
        Assert.Contains(OffendingPath, message, StringComparison.Ordinal);
        Assert.Contains("at the packet root as 'guide_reachability'", message, StringComparison.Ordinal);

        Assert.False(PacketYamlDocument.TryParseWithLocation(yaml, out _, out var error));
        Assert.Equal(GuideReachabilityPlacement.ErrorMessage, error!.Message);
        var implementationIndex = yaml.IndexOf("implementation_issue_packet", StringComparison.Ordinal);
        Assert.True(implementationIndex >= 0);
        var keyIndex = yaml.IndexOf("guide_reachability", implementationIndex, StringComparison.Ordinal);
        Assert.True(keyIndex >= 0);
        Assert.Equal(yaml[..keyIndex].Count(character => character == '\n') + 1, error.Line);
        var lineStart = yaml.LastIndexOf('\n', keyIndex);
        Assert.Equal(keyIndex - lineStart, error.Column);

        var exception = Assert.Throws<InvalidOperationException>(() => GuideReachabilityDeclaration.Read(yaml));
        Assert.Equal(GuideReachabilityPlacement.ErrorMessage, exception.Message);
    }
}
