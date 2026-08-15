using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuidePrimacyG701Tests
{
    [Fact]
    public void HerdrLayoutRegistry_IsVersionedEnumerableAndComplete()
    {
        var layout = HerdrStandardLayoutRegistry.Create();

        Assert.Equal("herdr-standard-layout", layout.RegistryId);
        Assert.Equal("herdr-standard-layout/v1", layout.RegistryVersion);
        Assert.Equal(1, layout.TeamTabCount);
        Assert.Equal(3, layout.PaneCount);
        Assert.Equal(
            new[] { "orchestration", "implementation", "review" },
            layout.Panes.Select(pane => pane.Role));
        Assert.Equal(
            new[] { "left", "right-top", "right-bottom" },
            layout.Panes.Select(pane => pane.Position));
        Assert.Equal(
            new[] { "orchestration", "implementation", "review" },
            layout.Panes.Select(pane => pane.Label));
        Assert.Contains("herdr workspace create", layout.Creation.Workspace, StringComparison.Ordinal);
        Assert.Contains("--direction right", layout.Creation.ImplementationPane, StringComparison.Ordinal);
        Assert.Contains("--direction down", layout.Creation.ReviewPane, StringComparison.Ordinal);
        Assert.Contains("herdr pane move", layout.Repair.MoveRight, StringComparison.Ordinal);
        Assert.Contains("--split right", layout.Repair.MoveRight, StringComparison.Ordinal);
        Assert.Contains("--split down", layout.Repair.MoveDown, StringComparison.Ordinal);
        Assert.Contains("--target-pane", layout.Repair.MoveDown, StringComparison.Ordinal);
        Assert.Contains("herdr pane rename", layout.Repair.Rename, StringComparison.Ordinal);
    }

    [Fact]
    public void HerdrWorkspaceCreate_EmitsTheMultiWordLabelAsOneQuotedArgument_G701Repair()
    {
        var layout = HerdrStandardLayoutRegistry.Create();
        var workspaceCommands = new[]
        {
            layout.Creation.Workspace,
            layout.Panes.Single(pane => pane.Role == "orchestration").CreateCommand,
        };

        foreach (var command in workspaceCommands)
        {
            Assert.Contains("--label \"<team> · herdr-only\"", command, StringComparison.Ordinal);
            Assert.DoesNotContain("--label <team> · herdr-only", command, StringComparison.Ordinal);

            var tokens = ShellTokenize(command);
            var labelIndex = Array.IndexOf(tokens, "--label");
            Assert.True(labelIndex >= 0, $"workspace command has no --label option: {command}");
            Assert.Equal("<team> · herdr-only", tokens[labelIndex + 1]);
            Assert.Equal("--no-focus", tokens[labelIndex + 2]);
        }
    }

    [Fact]
    public void LayoutCheck_IsNamedVisibleAndNeverReadyBlockingOrExecutable()
    {
        var check = HerdrStandardLayoutRegistry.Create().SetupCheck;

        Assert.Equal("layout-and-labels", check.Id);
        Assert.Equal("visible-incompleteness", check.IncompleteOutcome);
        Assert.False(check.ReadyBlocking);
        Assert.True(check.ReadOnly);
        Assert.True(check.NeverExecutesHerdr);

        var json = HerdrStandardLayoutRegistry.CreateJson();
        var jsonCheck = json["setup_check"]!.AsObject();
        Assert.False(jsonCheck["ready_blocking"]!.GetValue<bool>());
        Assert.True(jsonCheck["never_executes_herdr"]!.GetValue<bool>());
    }

    [Fact]
    public void OrchestratorGuide_BareMetadataFreeRouteRendersProductionRegistryAndDialogRule()
    {
        var context = BareContext();
        Assert.False(File.Exists(Path.Combine(context.RepoRoot, ".intent-cli", "config.toml")));

        using var jsonWriter = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                context,
                [
                    "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "json",
                ],
                jsonWriter));

        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var root = document.RootElement;
        Assert.Equal("herdr-standard-layout/v1", root.GetProperty("herdr_standard_layout").GetProperty("registry_version").GetString());
        Assert.Equal(1, root.GetProperty("herdr_standard_layout").GetProperty("team_tab_count").GetInt32());
        Assert.Equal(3, root.GetProperty("herdr_standard_layout").GetProperty("panes").GetArrayLength());
        Assert.Equal("dialog-answering/v1", root.GetProperty("dialog_answering_rule").GetProperty("rule_version").GetString());
        Assert.False(root.TryGetProperty(HerdrOnlyOperatingGuide.JsonProperty, out _));

        using var markdownWriter = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                context,
                [
                    "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "markdown",
                ],
                markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("Herdr standard layout registry (G701)", markdown, StringComparison.Ordinal);
        Assert.Contains("herdr pane move --tab --split right|down --target-pane", markdown, StringComparison.Ordinal);
        Assert.Contains("Three-tier dialog-answering rule (G701)", markdown, StringComparison.Ordinal);
        Assert.Contains("layout-and-labels", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void HerdrOnlySetup_RendersRegistryAndDialogRuleFromTheSameModels()
    {
        var markdown = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        using var json = JsonDocument.Parse(HerdrOnlyOperatingGuide.CreateJson([]).ToJsonString());
        var root = json.RootElement;
        var layout = root.GetProperty("layout_registry");
        var dialog = root.GetProperty("dialog_answering_rule");

        Assert.Contains("herdr-standard-layout/v1", markdown, StringComparison.Ordinal);
        Assert.Contains("one-team-tab-three-pane", markdown, StringComparison.Ordinal);
        Assert.Contains("orchestration", markdown, StringComparison.Ordinal);
        Assert.Contains("implementation", markdown, StringComparison.Ordinal);
        Assert.Contains("review", markdown, StringComparison.Ordinal);
        Assert.Contains("herdr pane rename", markdown, StringComparison.Ordinal);
        Assert.Contains("self-provisioned gates", markdown, StringComparison.Ordinal);
        Assert.Contains("human-approved execution", markdown, StringComparison.Ordinal);
        Assert.Contains("unapproved or uncertain dialogs", markdown, StringComparison.Ordinal);
        Assert.Contains("G690 distinction", markdown, StringComparison.Ordinal);
        Assert.Equal("herdr-standard-layout/v1", layout.GetProperty("registry_version").GetString());
        Assert.Equal("dialog-answering/v1", dialog.GetProperty("rule_version").GetString());
        Assert.Equal(3, dialog.GetProperty("tiers").GetArrayLength());
    }

    [Fact]
    public void DesignGuide_RendersExactThreeTierRuleAndG690Boundary()
    {
        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(BareContext(), ["--format", "json"], jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var rule = document.RootElement.GetProperty("dialog_answering_rule");

        Assert.Equal("dialog-answering/v1", rule.GetProperty("rule_version").GetString());
        Assert.Equal(
            new[] { "self-provisioned-gate", "human-approved-execution", "unapproved-or-uncertain" },
            rule.GetProperty("tiers").EnumerateArray().Select(tier => tier.GetProperty("id").GetString()));
        var ruleText = rule.ToString();
        Assert.Contains("session layer", ruleText, StringComparison.Ordinal);
        Assert.Contains("exactly matches", ruleText, StringComparison.Ordinal);
        Assert.Contains("never generalizes", ruleText, StringComparison.Ordinal);
        Assert.Contains("human decision", rule.GetProperty("g690_distinction").GetString(), StringComparison.Ordinal);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(BareContext(), [], markdownWriter));
        Assert.Contains("Three-tier dialog-answering rule (G701)", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("G690 distinction", markdownWriter.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("design", "intent-cli guide design-thread")]
    [InlineData("orchestration", "intent-cli guide orchestrator-thread")]
    [InlineData("review", "intent-cli guide review")]
    public void RoleFacingNextRoute_NamesTheInstalledGuide(string role, string expectedGuide)
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideNextCommand.Execute(
                BareContext(),
                [
                    "--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "J-Tech-Japan/intent-system",
                    "--role", role, "--format", "json",
                ],
                writer));
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(expectedGuide, document.RootElement.GetProperty("role_contract_first").GetProperty("guide").GetString());
    }

    [Fact]
    public void AdrAndEnglishJapaneseDocs_StateAllFourNormativeClausesAndDialogBoundary()
    {
        var repo = RepoVersionPolicySource.RepoRoot();
        var adr = File.ReadAllText(Path.Combine(repo, "docs", "adr", "0006-guide-surfaces-primary-interface.md"));
        Assert.Contains("Guide surfaces are the primary interface for humans and AI agents", adr, StringComparison.Ordinal);
        Assert.Contains("missing, wrong, or stale guide route", adr, StringComparison.Ordinal);
        Assert.Contains("Guide-route execution is acceptance substance equal to functional tests", adr, StringComparison.Ordinal);
        Assert.Contains("G645 per-unit reachability records enforce this decision", adr, StringComparison.Ordinal);

        foreach (var language in new[] { "en", "ja" })
        {
            var orchestration = File.ReadAllText(Path.Combine(repo, "docs", language, "12-agent-message-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(repo, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("G701", orchestration, StringComparison.Ordinal);
            Assert.Contains("herdr-standard-layout/v1", orchestration, StringComparison.Ordinal);
            Assert.Contains("dialog-answering/v1", orchestration, StringComparison.Ordinal);
            Assert.Contains("layout-and-labels", orchestration, StringComparison.Ordinal);
            Assert.Contains("guide primacy", orchestration.ToLowerInvariant(), StringComparison.Ordinal);
            Assert.Contains("G701", ledger, StringComparison.Ordinal);
        }
    }

    private static CliContext BareContext() => new()
    {
        RepoRoot = AppContext.BaseDirectory,
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

    private static string[] ShellTokenize(string command)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var character in command)
        {
            if (character == '\"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
        }

        Assert.False(quoted, "emitted command has an unterminated quote");
        if (token.Length > 0)
        {
            tokens.Add(token.ToString());
        }

        return tokens.ToArray();
    }
}
