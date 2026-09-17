using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>G841: retired reader and sequence consumption guards.</summary>
public sealed class G841SourceGuardsTests
{
    [Fact]
    public void Source_HasNoPreparedPacketYamlScalarParser_G841()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var hits = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("obj", StringComparison.Ordinal) && !path.Contains("bin", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("PreparedPacketYamlScalarParser", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(hits);
    }

    [Fact]
    public void MigratedSites_DoNotCallLookupSequence_G841()
    {
        string[] files =
        [
            "src/IntentSystem.Cli/Commands/CrossRuntimeReviewTeamResolver.cs",
            "src/IntentSystem.Cli/Commands/CrossRuntimeReviewPacketClaimResolver.cs",
            "src/IntentSystem.Cli/Commands/RunsLogRowInspector.cs",
            "src/IntentSystem.Cli/Commands/AutomationIssueRetireCommand.cs",
            "src/IntentSystem.Cli/Commands/IntentNextSliceCommand.cs",
            "src/IntentSystem.Cli/Commands/AutomationStalledWorkCommand.cs",
            "src/IntentSystem.Cli/Commands/AutomationPublishRecoveryCommand.cs",
            "src/IntentSystem.Cli/Commands/BugImplementationIssueCommand.cs",
            "src/IntentSystem.Cli/Commands/ReviewCloseoutPlanCommand.cs",
            "src/IntentSystem.Cli/Commands/IssuePublishFlowCommand.cs",
            "src/IntentSystem.Cli/Commands/PacketDraftCommand.cs",
        ];
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var relative in files)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.DoesNotContain("LookupSequence", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TryParse_ErrorString_StaysPrefixed_G841()
    {
        var yaml = """
            implementation_issue_packet:
              domain: intent-cli
              domain: other
            """;
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var error));
        Assert.StartsWith("packet.yaml is not valid YAML:", error, StringComparison.Ordinal);
    }
}
