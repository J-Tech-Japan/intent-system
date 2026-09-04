using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G696: the installed seat-command registry is reachable for both supported
/// kinds, review guidance carries the same-account verdict convention, and a
/// domain-scoped orchestrator preflight names another domain's teams without
/// surfacing them as selected scopes.
/// </summary>
public sealed class SeatCommandGuidanceG696Tests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void GuideSeatCommands_RendersVersionedFormsBreakersAndAlternatives(string kind)
    {
        using var writer = new StringWriter();
        var exitCode = GuideSeatCommandsCommand.Execute(
            CreateContext(Path.GetTempPath()),
            ["--kind", kind, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("1", root.GetProperty("registry_version").GetString());
        Assert.Equal(kind, root.GetProperty("kind").GetString());

        var breakers = root.GetProperty("breakers").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)
            .ToArray();
        foreach (var breaker in new[]
        {
            "quoted arguments",
            "flags before the path",
            "differently-prefixed commands chained with &&",
            "$VAR expansion",
            "for-loops",
        })
        {
            Assert.Contains(breaker, breakers);
        }

        var alternatives = root.GetProperty("alternatives").EnumerateArray().ToArray();
        Assert.Equal(3, alternatives.Length);
        Assert.Contains(alternatives, item => item.GetProperty("denied_form").GetString() == "gh pr comment"
            && item.GetProperty("sanctioned_form").GetString()!.Contains("gh pr review --body-file", StringComparison.Ordinal));
        Assert.Contains(alternatives, item => item.GetProperty("denied_form").GetString() == "git checkout <branch>"
            && item.GetProperty("commands").EnumerateArray().Any(command => command.GetString()!.Contains("git fetch", StringComparison.Ordinal))
            && item.GetProperty("commands").EnumerateArray().Any(command => command.GetString()!.Contains("git diff --check", StringComparison.Ordinal)));
        Assert.Contains(alternatives, item => item.GetProperty("denied_form").GetString()!.Contains("npm", StringComparison.Ordinal)
            && item.GetProperty("sanctioned_form").GetString() == "exact-head CI evidence");
    }

    [Fact]
    public void CommandRouter_AndMarkdownSurface_ExposeSeatCommandGuide()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "seat-commands", "--kind", "claude", "--format", "markdown"],
            CreateContext(Path.GetTempPath()),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Seat command guidance — claude", output, StringComparison.Ordinal);
        Assert.Contains("## Prefix-matching breakers", output, StringComparison.Ordinal);
        Assert.Contains("## Sanctioned alternatives", output, StringComparison.Ordinal);
        Assert.Contains("git fetch origin <branch>", output, StringComparison.Ordinal);
        Assert.Contains("exact PR head SHA", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewGuidance_UsesCommentedAndCanonicalReportForSameAccount()
    {
        using var workspace = new TemporaryWorkspace("seat-review-g696-");
        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--pr", "1505", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        // The child checkout has no host queue-state by design, but the
        // read-only guidance still renders its policy payload.
        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var verdict = document.RootElement.GetProperty("same_account_review_verdict");
        Assert.True(verdict.GetProperty("github_approve_rejected_for_same_account").GetBoolean());
        Assert.Equal("COMMENTED", verdict.GetProperty("verdict").GetString());
        Assert.Contains("gh pr review --body-file", verdict.GetProperty("review_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", verdict.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("same GitHub account", verdict.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RoleFacingGuides_NameTheSeatCommandRoute_G696()
    {
        using var workspace = new TemporaryWorkspace("seat-route-g696-");

        using var reviewWriter = new StringWriter();
        Assert.Equal(1, GuideReviewCommand.Execute(
            workspace.Context,
            ["--pr", "1505", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            reviewWriter));
        AssertSeatCommandRoute(JsonDocument.Parse(reviewWriter.ToString()).RootElement, "reviewer");

        using var nextWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            workspace.Context,
            ["--role", "review", "--format", "json"],
            nextWriter));
        AssertSeatCommandRoute(JsonDocument.Parse(nextWriter.ToString()).RootElement, "review");

        using var orchestratorWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            orchestratorWriter));
        AssertSeatCommandRoute(JsonDocument.Parse(orchestratorWriter.ToString()).RootElement, "reviewer");

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            workspace.Context,
            ["--role", "review", "--format", "markdown"],
            markdownWriter));
        Assert.Contains("## Guide reachability (G645/G696)", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("guide seat-commands", markdownWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DomainScopedPreflight_ExcludesUnrelatedRecordedAndTopologyTeams_G696()
    {
        using var workspace = new TemporaryWorkspace("seat-preflight-g696-");
        RecordMode(workspace.Context, "idd-com", "idd-workers");
        RecordMode(workspace.Context, "sekiban-domain", "sekiban-workers");

        var unrelatedTopology = NotifyRoleTopologyStore.ResolvePath(
            workspace.RootPath,
            "sekiban-domain",
            "sekiban-topology-only");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedTopology)!);
        File.WriteAllText(unrelatedTopology, JsonSerializer.Serialize(new
        {
            domain = "sekiban-domain",
            team = "sekiban-topology-only",
            workspace_id = "unrelated-workspace",
            roles = new { },
        }));

        var direct = SessionLayerPreflight.Analyze(workspace.RootPath, "idd-com");
        Assert.Single(direct.Scopes);
        Assert.Equal("idd-com", direct.Scopes[0].Domain);
        Assert.Equal("idd-workers", direct.Scopes[0].Team);
        Assert.NotEmpty(direct.CrossDomainTeamsElided);
        Assert.Contains("sekiban-domain/sekiban-workers", direct.CrossDomainTeamsElided);
        Assert.Contains("sekiban-domain/sekiban-topology-only", direct.CrossDomainTeamsElided);
        Assert.DoesNotContain(direct.Scopes, scope => scope.Team is "sekiban-workers" or "sekiban-topology-only");
        Assert.DoesNotContain(direct.Scopes.SelectMany(scope => scope.Findings), finding => finding.Team is "sekiban-workers" or "sekiban-topology-only");

        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", "idd-com", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var scopes = document.RootElement
            .GetProperty("session_layer")
            .GetProperty("preflight")
            .GetProperty("scopes")
            .EnumerateArray()
            .ToArray();
        Assert.Single(scopes);
        Assert.Equal("idd-com", scopes[0].GetProperty("domain").GetString());
        Assert.Equal("idd-workers", scopes[0].GetProperty("team").GetString());
        var elided = document.RootElement
            .GetProperty("session_layer")
            .GetProperty("preflight")
            .GetProperty("cross_domain_teams_elided")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.NotEmpty(elided);
        Assert.Contains("sekiban-domain/sekiban-workers", elided);
        Assert.Contains("sekiban-domain/sekiban-topology-only", elided);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", "idd-com", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "markdown"],
            markdownWriter));
        Assert.Contains("cross-domain teams elided", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("sekiban-domain/sekiban-workers", markdownWriter.ToString(), StringComparison.Ordinal);
    }

    private static void AssertSeatCommandRoute(JsonElement root, string expectedRole)
    {
        var reachability = root.GetProperty("guide_reachability");
        Assert.True(reachability.GetProperty("is_declared").GetBoolean());
        Assert.False(reachability.GetProperty("no_role_facing_surface").GetBoolean());
        var route = Assert.Single(reachability.GetProperty("routes").EnumerateArray());
        Assert.Equal("guide seat-commands", route.GetProperty("guide_surface").GetString());
        Assert.Equal(expectedRole, route.GetProperty("role").GetString());
        Assert.Equal("per-kind sanctioned command forms and alternatives", route.GetProperty("target_surface").GetString());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CommandReferenceDocs_KeepTheG696SurfaceAndMeasuredPairs(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var content = File.ReadAllText(Path.Combine(root, "docs", language, "08-command-reference.md"));

        Assert.Contains("guide seat-commands", content, StringComparison.Ordinal);
        foreach (var marker in new[] { "flags", "&&", "$VAR", "for", "gh pr comment", "git checkout", "COMMENTED", "intent-cli notify report", "guide next", "guide orchestrator-thread" })
        {
            Assert.Contains(marker, content, StringComparison.Ordinal);
        }
        if (string.Equals(language, "en", StringComparison.Ordinal))
        {
            Assert.Contains("quoted arguments", content, StringComparison.Ordinal);
            Assert.Contains("exact PR", content, StringComparison.Ordinal);
            Assert.Contains("head SHA", content, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("quoted", content, StringComparison.Ordinal);
            Assert.Contains("arguments", content, StringComparison.Ordinal);
            Assert.Contains("exact PR head SHA", content, StringComparison.Ordinal);
        }
        Assert.Contains("G696", content, StringComparison.Ordinal);
    }

    private static void RecordMode(CliContext context, string domain, string team)
    {
        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", domain, "--team", team, "--mode", SessionLayerMode.Agmsg, "--write", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);
    }

    private static CliContext CreateContext(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace(string prefix)
        {
            RootPath = Directory.CreateTempSubdirectory(prefix).FullName;
            Context = CreateContext(RootPath);
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void Dispose()
        {
            // Intentionally leave the temp fixture in place. This test suite
            // must not delete /tmp paths; the workspace is disposable by the
            // host environment rather than by the test.
        }
    }
}
