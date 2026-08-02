using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class HerdrPaneTopologyG586Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string G589AgmsgBaselineSha256 = "90be8c7999369dc4dffa4fba58c13456e5f90319116473ad11cb9efd7baa47d8";

    private readonly string root = Directory.CreateTempSubdirectory("herdr-topology-g586-").FullName;

    [Fact]
    public void HerdrOnlyRendering_PinsOneTeamTabAndPanePerRole_WithPaneSplitFirst_G586()
    {
        var markdown = Render(herdrOnly: true, format: "markdown");
        var provisioning = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement
            .GetProperty(HerdrOnlyOperatingGuide.JsonProperty)
            .GetProperty("provisioning");

        const string topology =
            "One workspace per team, one tab named after the team, one pane per role, each pane opened with that "
            + "role's folder as its cwd.";
        Assert.Contains(topology, markdown, StringComparison.Ordinal);
        Assert.Equal(topology, provisioning.GetProperty("topology").GetString());
        Assert.Contains("all roles visible to the operator at once", markdown, StringComparison.Ordinal);
        Assert.Contains("G550 supervision pane scan", markdown, StringComparison.Ordinal);
        Assert.Contains("inactive tab", markdown, StringComparison.Ordinal);

        var paneDefault = markdown.IndexOf(
            "herdr pane split --pane <pane-id>",
            StringComparison.Ordinal);
        var tabException = markdown.IndexOf(
            "herdr tab create --workspace <workspace-id>",
            StringComparison.Ordinal);
        Assert.True(paneDefault >= 0, "the default pane-split example must render");
        Assert.True(tabException > paneDefault, "pane split must be shown before the exceptional tab-create path");
        Assert.Contains("default", provisioning.GetProperty("pane_default").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicitly authorizes", provisioning.GetProperty("tab_exception").GetString(), StringComparison.Ordinal);
        Assert.Contains("instead of simultaneous visibility", provisioning.GetProperty("tab_exception").GetString(), StringComparison.Ordinal);

        // G582 remains executable guidance on the now-default pane path.
        Assert.Contains("explicit non-empty pane/workspace target id", markdown, StringComparison.Ordinal);
        Assert.Contains("fail closed and DO NOT run the command", markdown, StringComparison.Ordinal);
        Assert.Contains("--pane <pane-id>", provisioning.GetProperty("pane_default").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OrchestrationDocs_MirrorPaneFirstTopologyAndSupervisionRationale_G586(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md");
        var doc = File.ReadAllText(path);
        var sectionHeading = language == "en"
            ? "### Provision and prove READY"
            : "### Provisioning と READY の証明";
        var sectionStart = doc.IndexOf(sectionHeading, StringComparison.Ordinal);
        Assert.True(sectionStart >= 0);
        var section = NormalizeWhitespace(doc[sectionStart..]);

        Assert.Contains(
            language == "en"
                ? "one workspace per team, one tab named after the team, one pane per role"
                : "1 チームにつき 1 workspace、team 名の 1 tab、role ごとに 1 pane",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "all roles visible to the operator at once" : "すべての role を同時に operator から見える",
            section,
            StringComparison.Ordinal);
        Assert.Contains("G550", section, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "inactive tab" : "inactive な tab", section, StringComparison.Ordinal);

        var paneDefault = section.IndexOf("herdr pane split --pane <pane-id>", StringComparison.Ordinal);
        var tabException = section.IndexOf("herdr tab create --workspace <workspace-id>", StringComparison.Ordinal);
        Assert.True(paneDefault >= 0);
        Assert.True(tabException > paneDefault);
        Assert.Contains(language == "en" ? "explicitly authorizes" : "明示的に authorize", section, StringComparison.Ordinal);
    }

    [Fact]
    public void AgmsgRendering_MatchesG589CiWakeContractBaseline_G589()
    {
        var markdown = Render(herdrOnly: false, format: "markdown");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));

        Assert.Equal(G589AgmsgBaselineSha256, hash);
    }

    private string Render(bool herdrOnly, string format)
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        var record = SessionLayerModeStore.ResolvePath(root);
        if (herdrOnly)
        {
            File.WriteAllText(
                record,
                $$"""
                {
                  "schema_version": "1",
                  "entries": [
                    {
                      "domain": "{{Domain}}",
                      "team": "{{Team}}",
                      "mode": "herdr-only",
                      "updated_at": "2026-08-02T12:00:00+00:00",
                      "transitions": [
                        { "from": "agmsg", "to": "herdr-only", "at": "2026-08-02T12:00:00+00:00" }
                      ]
                    }
                  ]
                }
                """);
        }
        else if (File.Exists(record))
        {
            File.Delete(record);
        }

        var context = new CliContext
        {
            RepoRoot = root,
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
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "guide", "orchestrator-thread", "--domain", Domain,
                "--target-repo", Repo, "--agent", "codex", "--team", Team,
                "--format", format,
            ],
            context,
            writer);

        Assert.Equal(0, exitCode);
        return writer.ToString();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
