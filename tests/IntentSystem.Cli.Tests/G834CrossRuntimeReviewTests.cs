using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G834: cross-runtime implementation review. intent-cli renders the request,
/// records a head-bound verdict, and evaluates one gate shared by
/// <c>review cross-runtime status</c> and the approved transition — only for
/// teams declared in <c>[[cross_runtime_review.teams]]</c>. It never starts,
/// launches, or manages a reviewer.
/// </summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G834CrossRuntimeReviewTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G834";
    private const int Pr = 1812;
    private const string H1 = "1111111111111111111111111111111111111111";
    private const string H2 = "2222222222222222222222222222222222222222";
    private const string H3 = "3333333333333333333333333333333333333333";

    private readonly string root = Directory.CreateTempSubdirectory("g834-host-").FullName;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };
    private DateTimeOffset clock = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public G834CrossRuntimeReviewTests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return claims.TryGetValue(unit, out var team)
                ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", team ?? string.Empty, "held")
                : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
        };
        ReviewCrossRuntimeCommand.Clock = () => clock = clock.AddMinutes(1);
        ReviewCrossRuntimeCommand.NestedProviderLauncher = () => throw new InvalidOperationException("cross-runtime review must never launch a provider");
        ReviewCrossRuntimeCommand.ProcessRunnerFactory = () => throw new InvalidOperationException("cross-runtime review must never construct a process runner");
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.PrHeadReader = null;
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
        WriteQueue((Unit, $"https://github.com/{Repo}/pull/{Pr}"));
        WritePacket(Unit, Domain);
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        ReviewCrossRuntimeCommand.Clock = null;
        ReviewCrossRuntimeCommand.NestedProviderLauncher = null;
        ReviewCrossRuntimeCommand.ProcessRunnerFactory = null;
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.PrHeadReader = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── config ─────────────────────────────────────────────────────────

    [Fact]
    public void Config_ParsesDeclaredTeams_WithConductorRuntimeAndRepos_AndAbsentMeansNone()
    {
        var config = CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [[cross_runtime_review.teams]]
            team = "intent-cli/intent-cli-dev"
            conductor_runtime = "claude"
            repos = ["J-Tech-Japan/intent-system"]

            [[cross_runtime_review.teams]]
            team = "sekiban/sekiban-dev"
            conductor_runtime = "codex"
            repos = ["J-Tech-Japan/Sekiban", "J-Tech-Japan/SekibanAsAService"]
            """);

        Assert.Equal(2, config.CrossRuntimeReview.Teams.Count);
        Assert.True(config.CrossRuntimeReview.TryGetDeclared("intent-cli", "intent-cli-dev", out var declared));
        Assert.Equal("claude", declared.ConductorRuntime);
        Assert.Equal(["J-Tech-Japan/intent-system"], declared.Repos);
        Assert.False(config.CrossRuntimeReview.TryGetDeclared("Intent-cli", "intent-cli-dev", out _));
        Assert.False(config.CrossRuntimeReview.TryGetDeclared("intent-cli", "other", out _));
        Assert.True(config.CrossRuntimeReview.IsGatedRepo("j-tech-japan/INTENT-SYSTEM"));
        Assert.True(config.CrossRuntimeReview.IsGatedRepo("J-Tech-Japan/SekibanAsAService"));
        Assert.False(config.CrossRuntimeReview.IsGatedRepo("J-Tech-Japan/other"));
        Assert.Equal("config:cross_runtime_review.teams", CrossRuntimeReviewConfig.Source);

        var absent = CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"
            """);
        Assert.Empty(absent.CrossRuntimeReview.Teams);
        Assert.False(absent.CrossRuntimeReview.IsGatedRepo(Repo));
    }

    [Theory]
    [InlineData("intent-cli-dev", "claude", "[\"J-Tech-Japan/intent-system\"]", "field 'team'")]
    [InlineData("intent-cli/intent cli", "claude", "[\"J-Tech-Japan/intent-system\"]", "field 'team'")]
    [InlineData("../intent-cli-dev", "claude", "[\"J-Tech-Japan/intent-system\"]", "field 'team'")]
    [InlineData("intent-cli/intent-cli-dev", "gemini", "[\"J-Tech-Japan/intent-system\"]", "field 'conductor_runtime'")]
    [InlineData("intent-cli/intent-cli-dev", "Claude", "[\"J-Tech-Japan/intent-system\"]", "field 'conductor_runtime'")]
    [InlineData("intent-cli/intent-cli-dev", "claude", "[]", "field 'repos'")]
    [InlineData("intent-cli/intent-cli-dev", "claude", "[\"intent-system\"]", "field 'repos'")]
    [InlineData("intent-cli/intent-cli-dev", "claude", "[\"../x/y\"]", "field 'repos'")]
    [InlineData("intent-cli/intent-cli-dev", "claude", "\"J-Tech-Japan/intent-system\"", "field 'repos'")]
    public void Config_RejectsMalformedEntries_NamingKeyEntryAndField(string team, string runtime, string repos, string field)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load($"""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [[cross_runtime_review.teams]]
            team = "{team}"
            conductor_runtime = "{runtime}"
            repos = {repos}
            """));
        Assert.Contains("cross_runtime_review.teams", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cross_runtime_review.teams[0]", exception.Message, StringComparison.Ordinal);
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("team = \"intent-cli/intent-cli-dev\"\nconductor_runtime = \"claude\"", "missing field 'repos'")]
    [InlineData("conductor_runtime = \"claude\"\nrepos = [\"a/b\"]", "missing field 'team'")]
    [InlineData("team = \"intent-cli/intent-cli-dev\"\nrepos = [\"a/b\"]", "missing field 'conductor_runtime'")]
    [InlineData("team = \"intent-cli/intent-cli-dev\"\nconductor_runtime = \"claude\"\nrepos = [\"a/b\"]\nextra = 1", "unknown field 'extra'")]
    public void Config_RejectsMissingAndUnknownFields(string body, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(
            "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n\n[[cross_runtime_review.teams]]\n" + body + "\n"));
        Assert.Contains("cross_runtime_review.teams[0]", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[cross_runtime_review]\nteams = \"intent-cli/intent-cli-dev\"", "must be an array of tables")]
    [InlineData("cross_runtime_review = \"yes\"", "must be a table")]
    public void Config_RejectsMalformedTables(string body, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(
            "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n" + body + "\n"));
        Assert.Contains("cross_runtime_review", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_RejectsDuplicateTeam_NamingTheEntry()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [[cross_runtime_review.teams]]
            team = "intent-cli/intent-cli-dev"
            conductor_runtime = "claude"
            repos = ["J-Tech-Japan/intent-system"]

            [[cross_runtime_review.teams]]
            team = "intent-cli/intent-cli-dev"
            conductor_runtime = "codex"
            repos = ["J-Tech-Japan/intent-system"]
            """));
        Assert.Contains("cross_runtime_review.teams[1] ('intent-cli/intent-cli-dev')", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'team' duplicates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_OptInTeams_StillUsesTheSharedDomainTeamRule()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [supervision]
            opt_in_teams = ["intent-cli-dev"]
            """));
        Assert.Equal(
            "CLI config value 'supervision.opt_in_teams' entry 'intent-cli-dev' must be '<domain>/<team>' with two non-empty segments and no whitespace.",
            exception.Message);
    }

    // ── resolution ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("string")]
    [InlineData("object")]
    [InlineData("legacy-number")]
    public void Resolution_PrToUnitDomainAndTeam_FromQueuePacketAndClaim(string linkedPrShape)
    {
        WriteQueueRaw(linkedPrShape switch
        {
            "string" => $"\"https://github.com/{Repo}/pull/{Pr}\"",
            "object" => $"{{\"repo\":\"{Repo}\",\"number\":{Pr},\"url\":\"https://github.com/{Repo}/pull/{Pr}\"}}",
            _ => $"\"{Pr}\"",
        });

        var resolution = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "pass --execution-unit");
        Assert.True(resolution.Resolved, resolution.Detail);
        Assert.Equal(Unit, resolution.ExecutionUnit);
        Assert.Equal(CrossRuntimeReviewTeamResolver.SourceQueueLinkedPr, resolution.ExecutionUnitSource);
        Assert.Equal(Domain, resolution.Domain);
        Assert.Equal(CrossRuntimeReviewTeamResolver.SourcePacketDomain, resolution.DomainSource);
        Assert.Equal(Team, resolution.Team);
        Assert.Equal(CrossRuntimeReviewTeamResolver.SourceClaimTeam, resolution.TeamSource);
    }

    [Fact]
    public void Resolution_TeamComesFromTheRealClaimReader_ForALocalClaimFixture()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        var claimPath = Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{Unit}").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
        File.WriteAllText(claimPath, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            scope = $"execution-unit:{Unit}",
            actor = "implementation",
            team = Team,
            claimed_at = DateTimeOffset.UtcNow,
            base_commit = H1,
        }));

        var resolution = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "pass --execution-unit");
        Assert.True(resolution.Resolved, resolution.Detail);
        Assert.Equal(Team, resolution.Team);
    }

    [Fact]
    public void Resolution_UnlinkedPr_ResolvesThroughTheNamedUnit_AndMismatchRefuses()
    {
        WriteQueue((Unit, null), ("G900", $"https://github.com/{Repo}/pull/77"));
        WritePacket("G900", Domain);
        claims["G900"] = Team;

        var unlinked = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "pass --execution-unit <unit> to pr-transition");
        Assert.False(unlinked.Resolved);
        Assert.Equal(CrossRuntimeReviewCauses.TeamUnresolved, unlinked.Cause);
        Assert.Equal("queue-linkage", unlinked.Missing);
        Assert.Contains("intent-cli worker complete", unlinked.Fix, StringComparison.Ordinal);
        Assert.Contains($"--pr {Pr}", unlinked.Fix, StringComparison.Ordinal);
        Assert.Contains("pass --execution-unit <unit> to pr-transition", unlinked.Fix, StringComparison.Ordinal);

        var named = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, Unit, "x");
        Assert.True(named.Resolved, named.Detail);
        Assert.Equal(CrossRuntimeReviewTeamResolver.SourceExecutionUnitArgument, named.ExecutionUnitSource);

        var linkedElsewhere = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, 77, Unit, "x");
        Assert.Equal(CrossRuntimeReviewCauses.UnitMismatch, linkedElsewhere.Cause);

        var namedUnitLinkedToOtherPr = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, "G900", "x");
        Assert.Equal(CrossRuntimeReviewCauses.UnitMismatch, namedUnitLinkedToOtherPr.Cause);
    }

    [Fact]
    public void Resolution_TeamUnresolved_ForEachMissingInput_WithTheFixNamed()
    {
        Directory.Delete(Path.Combine(root, ".intent-cli", "issues", Unit), recursive: true);
        var noPacket = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "x");
        Assert.Equal(CrossRuntimeReviewCauses.TeamUnresolved, noPacket.Cause);
        Assert.Equal("packet-domain", noPacket.Missing);
        Assert.Contains("implementation_issue_packet.domain", noPacket.Fix, StringComparison.Ordinal);

        WritePacket(Unit, domain: null);
        Assert.Equal("packet-domain", CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "x").Missing);

        WritePacket(Unit, Domain);
        claims.Remove(Unit);
        var unheld = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "x");
        Assert.Equal("claim-team", unheld.Missing);
        Assert.Contains($"intent-cli claim acquire --scope execution-unit:{Unit}", unheld.Fix, StringComparison.Ordinal);
        Assert.Contains("--team <team>", unheld.Fix, StringComparison.Ordinal);

        claims[Unit] = null;
        Assert.Equal("claim-team", CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "x").Missing);

        claims[Unit] = Team;
        WriteQueue((Unit, $"https://github.com/{Repo}/pull/{Pr}"), ("G901", $"https://github.com/{Repo}/pull/{Pr}"));
        var duplicate = CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "x");
        Assert.Equal(CrossRuntimeReviewCauses.TeamUnresolved, duplicate.Cause);
        Assert.Contains("more than one queue item", duplicate.Detail, StringComparison.Ordinal);

        File.Delete(Path.Combine(root, ".intent-cli", "queue-state.json"));
        Assert.Equal("queue-linkage", CrossRuntimeReviewTeamResolver.Resolve(root, Repo, Pr, null, "x").Missing);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("record")]
    [InlineData("status")]
    public void Commands_RefuseWithoutHostConfig_HostRootRequired(string subcommand)
    {
        File.Delete(Path.Combine(root, ".intent-cli", "config.toml"));
        var args = subcommand switch
        {
            "request" => RequestArgs("codex", Path.Combine(root, "clone"), Path.Combine(root, "out")),
            "record" => RecordArgs("codex", Path.Combine(root, "v.json"), H1, write: true),
            _ => StatusArgs(H1),
        };

        var (exit, output) = Route(["review", "cross-runtime", .. args]);
        Assert.Equal(1, exit);
        Assert.Contains(CrossRuntimeReviewCauses.HostRootRequired, output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, ".intent-cli", "cross-runtime-reviews")));
    }

    // ── request ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_WritesExactlyThreeFiles_AndThePinnedInvocation(string runtime)
    {
        var clone = Path.Combine(root, "clone");
        var outDir = Path.Combine(root, "out-" + runtime);
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs(runtime, clone, outDir), "--format", "json"]);

        Assert.Equal(0, exit);
        Assert.Equal(
            ["invocation.txt", "prompt.md", "verdict.schema.json"],
            Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));

        var invocation = File.ReadAllText(Path.Combine(outDir, "invocation.txt"));
        var lines = invocation.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("run by the seat", lines[0], StringComparison.Ordinal);
        var expected = runtime switch
        {
            "codex" => $"codex exec -s read-only -C '{clone}' --output-schema '{outDir}/verdict.schema.json' -o '{outDir}/verdict.raw.json' - < '{outDir}/prompt.md'",
            "claude" => $"cd '{clone}' && claude -p --permission-mode plan --disallowedTools Edit,Write,NotebookEdit --output-format json --json-schema \"$(cat '{outDir}/verdict.schema.json')\" < '{outDir}/prompt.md' > '{outDir}/verdict.raw.json'",
            _ => $"cursor-agent -p --mode ask --sandbox enabled --trust --workspace '{clone}' --output-format json \"$(cat '{outDir}/prompt.md')\" > '{outDir}/verdict.raw.json'",
        };
        Assert.Equal(expected, lines[1]);

        using var result = JsonDocument.Parse(output);
        Assert.Equal("rendered", result.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("seat", result.RootElement.GetProperty("run_by").GetString());
        Assert.Equal(CrossRuntimeReviewRuntimes.ReadOnlyEnforcement[runtime], result.RootElement.GetProperty("read_only_enforcement").GetString());
        Assert.Equal(lines[1], result.RootElement.GetProperty("invocation").GetString()!.Split('\n')[1]);

        var prompt = File.ReadAllText(Path.Combine(outDir, "prompt.md"));
        Assert.Contains($"'{Path.Combine(root, ".intent-cli", "issues", Unit, "github-body.md")}'", prompt, StringComparison.Ordinal);
        Assert.Contains($"'{Path.Combine(root, ".intent-cli", "issues", Unit, "review-context.md")}'", prompt, StringComparison.Ordinal);
        Assert.Contains($"'{Path.Combine(root, ".intent-cli", "issues", Unit, "implementation.md")}'", prompt, StringComparison.Ordinal);
        Assert.Contains($"PR #{Pr}", prompt, StringComparison.Ordinal);
        Assert.Contains(H1, prompt, StringComparison.Ordinal);
        Assert.Contains("read-only", prompt, StringComparison.Ordinal);
        Assert.Contains("Return only one JSON object", prompt, StringComparison.Ordinal);
        Assert.Contains("\"head_sha\": echo the head SHA", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_UsesOnlyTheAllowedKeywords_AndAgreesWithTheValidator()
    {
        var schema = CrossRuntimeReviewVerdict.SchemaJson;
        foreach (var forbidden in new[] { "\"if\"", "\"then\"", "\"else\"", "\"oneOf\"", "\"anyOf\"", "\"allOf\"", "\"not\"", "\"dependentRequired\"", "\"dependentSchemas\"", "\"$schema\"", "\"minItems\"", "\"maxItems\"", "\"const\"" })
        {
            Assert.DoesNotContain(forbidden, schema, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(schema);
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        CollectKeywords(document.RootElement, keywords);
        Assert.Subset(new HashSet<string>(["type", "additionalProperties", "required", "properties", "items", "enum"]), keywords);

        var top = document.RootElement;
        Assert.Equal("object", top.GetProperty("type").GetString());
        Assert.False(top.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(CrossRuntimeReviewVerdict.TopLevelFields, top.GetProperty("required").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(CrossRuntimeReviewVerdict.VerdictValues, top.GetProperty("properties").GetProperty("verdict").GetProperty("enum").EnumerateArray().Select(item => item.GetString()!));
        var finding = top.GetProperty("properties").GetProperty("blocking_findings").GetProperty("items");
        Assert.Equal(CrossRuntimeReviewVerdict.FindingFields, finding.GetProperty("required").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal("integer", finding.GetProperty("properties").GetProperty("line").GetProperty("type").GetString());
        Assert.Equal("string", top.GetProperty("properties").GetProperty("notes").GetProperty("items").GetProperty("type").GetString());

        // The validator agrees with the schema on every field: dropping any required
        // field, adding a property, or using a wrong type is refused.
        foreach (var field in CrossRuntimeReviewVerdict.TopLevelFields)
        {
            var node = JsonSerializer.Deserialize<Dictionary<string, object>>(Verdict("approve", H1))!;
            node.Remove(field);
            Assert.False(Validate(JsonSerializer.Serialize(node)), field);
        }

        foreach (var field in CrossRuntimeReviewVerdict.FindingFields)
        {
            var findingNode = new Dictionary<string, object> { ["file"] = "a.cs", ["line"] = 1, ["scenario"] = "s" };
            findingNode.Remove(field);
            Assert.False(Validate(JsonSerializer.Serialize(new { verdict = "request-changes", head_sha = H1, blocking_findings = new[] { findingNode }, notes = Array.Empty<string>() })), field);
        }

        Assert.False(Validate($"{{\"verdict\":\"approve\",\"head_sha\":\"{H1}\",\"blocking_findings\":[],\"notes\":[],\"extra\":1}}"));
        Assert.False(Validate($"{{\"verdict\":\"maybe\",\"head_sha\":\"{H1}\",\"blocking_findings\":[],\"notes\":[]}}"));
        Assert.False(Validate($"{{\"verdict\":\"approve\",\"head_sha\":1,\"blocking_findings\":[],\"notes\":[]}}"));
        Assert.False(Validate($"{{\"verdict\":\"request-changes\",\"head_sha\":\"{H1}\",\"blocking_findings\":[{{\"file\":\"a\",\"line\":1.5,\"scenario\":\"s\"}}],\"notes\":[]}}"));
        Assert.False(Validate($"{{\"verdict\":\"approve\",\"head_sha\":\"{H1}\",\"blocking_findings\":[],\"notes\":[1]}}"));
        Assert.True(Validate(Verdict("approve", H1)));
        Assert.True(Validate(Verdict("request-changes", H1)));
    }

    [Theory]
    [InlineData("codex", null)]
    [InlineData("claude", null)]
    [InlineData("cursor", null)]
    [InlineData("copilot", "gpt-5.6-sol")]
    [InlineData("opencode", "github-copilot/gpt-5.6-sol")]
    public void Request_EveryInvocationFlag_IsInTheRuntimeAllowList_AndNoWriteEnablingFlagAppears(string runtime, string? model)
    {
        var outDir = Path.Combine(root, "flags-" + runtime);
        var args = RequestArgs(runtime, "/tmp/clone", outDir, model);
        Assert.Equal(0, Route(["review", "cross-runtime", .. args]).ExitCode);
        var command = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        var invocationBody = CrossRuntimeReviewInvocationTestHelpers.InvocationBodyForAllowListChecks(runtime, command, outDir);

        CrossRuntimeReviewInvocationTestHelpers.AssertNoDenyFlags(runtime, invocationBody);

        var tokens = ShellTokens(invocationBody);
        Assert.DoesNotContain("-c", tokens);
        var allowed = CrossRuntimeReviewRuntimes.AllowedFlags[runtime];
        foreach (var token in tokens.Where(token => token.StartsWith('-')))
        {
            Assert.True(allowed.Contains(token, StringComparer.Ordinal), $"'{token}' is outside the {runtime} allow-list: {command}");
        }

        switch (runtime)
        {
            case "codex":
                Assert.Equal("codex", tokens[0]);
                Assert.Equal("read-only", tokens[tokens.IndexOf("-s") + 1]);
                break;
            case "claude":
                Assert.Equal("claude", tokens[tokens.IndexOf("&&") + 1]);
                Assert.Equal("plan", tokens[tokens.IndexOf("--permission-mode") + 1]);
                Assert.Equal("Edit,Write,NotebookEdit", tokens[tokens.IndexOf("--disallowedTools") + 1]);
                break;
            case "cursor":
                Assert.Equal("cursor-agent", tokens[0]);
                Assert.Equal("ask", tokens[tokens.IndexOf("--mode") + 1]);
                Assert.Equal("enabled", tokens[tokens.IndexOf("--sandbox") + 1]);
                break;
            case "copilot":
                Assert.Equal("copilot", tokens[tokens.IndexOf("copilot")]);
                Assert.Contains("--allow-all-tools", tokens);
                Assert.DoesNotContain("--allow-all", tokens);
                Assert.Equal("view", tokens[tokens.IndexOf("--available-tools") + 1]);
                Assert.Equal("rg", tokens[tokens.IndexOf("--available-tools") + 2]);
                Assert.Equal("glob", tokens[tokens.IndexOf("--available-tools") + 3]);
                Assert.Equal("off", tokens[tokens.IndexOf("--stream") + 1]);
                break;
            default:
                Assert.Equal("opencode", tokens[tokens.IndexOf("opencode")]);
                Assert.Equal("run", tokens[tokens.IndexOf("run")]);
                Assert.Equal("intent-cli-reviewer", tokens[tokens.IndexOf("--agent") + 1]);
                Assert.Equal("json", tokens[tokens.IndexOf("--format") + 1]);
                break;
        }
    }

    [Fact]
    public void Request_Copilot_AllowAllToolsPasses_WholeTokenAllowAllWouldFail()
    {
        const string model = "gpt-5.6-sol";
        var outDir = Path.Combine(root, "allow-all-tools");
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), outDir, model)]).ExitCode);
        var command = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        var tokens = ShellTokens(command);
        Assert.Contains("--allow-all-tools", tokens);
        Assert.DoesNotContain("--allow-all", tokens);
        CrossRuntimeReviewInvocationTestHelpers.AssertNoDenyFlags("copilot", command);
    }

    [Theory]
    [InlineData("clone with space")]
    [InlineData("it's")]
    [InlineData("$HOME")]
    [InlineData("a;rm -rf x")]
    [InlineData("`whoami`")]
    public void Request_PathsWithShellMetacharacters_RenderAsOneQuotedArgument(string segment)
    {
        var clone = Path.Combine(root, segment);
        var outDir = Path.Combine(root, "out " + segment);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("codex", clone, outDir)]).ExitCode);
        var command = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        var tokens = ShellTokens(command);
        Assert.Equal(clone, tokens[tokens.IndexOf("-C") + 1]);
        Assert.Equal(Path.Combine(outDir, "verdict.schema.json"), tokens[tokens.IndexOf("--output-schema") + 1]);
        Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(clone), File.ReadAllText(Path.Combine(outDir, "prompt.md")), StringComparison.Ordinal);
        Assert.Equal("'it'\\''s'", CrossRuntimeReviewPaths.ShellQuote("it's"));
    }

    [Theory]
    [InlineData("--clone")]
    [InlineData("--out-dir")]
    public void Request_RefusesNewlineOrNulInAPath(string flag)
    {
        foreach (var bad in new[] { "a\nb", "a\0b" })
        {
            var clone = flag == "--clone" ? Path.Combine(root, bad) : Path.Combine(root, "clone");
            var outDir = flag == "--out-dir" ? Path.Combine(root, bad) : Path.Combine(root, "nl-out");
            var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("claude", clone, outDir), "--format", "json"]);
            Assert.Equal(1, exit);
            Assert.Contains(CrossRuntimeReviewCauses.PathInvalid, output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "nl-out")));
        }
    }

    [Theory]
    [InlineData("github-body.md")]
    [InlineData("review-context.md")]
    [InlineData("implementation.md")]
    public void Request_RefusesAnIncompletePacket(string missing)
    {
        File.Delete(Path.Combine(root, ".intent-cli", "issues", Unit, missing));
        var outDir = Path.Combine(root, "incomplete");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("codex", Path.Combine(root, "clone"), outDir), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.PacketMissing, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains(missing, refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public void Request_RefusesAnOutDirWithForeignEntries_IncludingAStaleVerdict()
    {
        var outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "verdict.raw.json"), "{}");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("codex", Path.Combine(root, "clone"), outDir), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Contains(CrossRuntimeReviewCauses.OutDirNotEmpty, output, StringComparison.Ordinal);
        Assert.Equal(["verdict.raw.json"], Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName));
    }

    [Fact]
    public void Request_RecordAndStatus_NeverLaunchAProviderOrConstructAProcessRunner_AndSourceHasNoProcessUse()
    {
        // The constructor installed throwing seams; every subcommand still succeeds.
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("claude", Path.Combine(root, "clone"), Path.Combine(root, "o"))]).ExitCode);
        var verdict = WriteVerdictFile("codex", Verdict("approve", H1));
        Assert.Equal(0, Route(["review", "cross-runtime", .. RecordArgs("codex", verdict, H1, write: true)]).ExitCode);
        Assert.Equal(0, Route(["review", "cross-runtime", .. StatusArgs(H1)]).ExitCode);

        var sourceRoot = Path.Combine(RepoVersionPolicySource.RepoRoot(), "src", "IntentSystem.Cli", "Commands");
        foreach (var file in new[]
                 {
                     "ReviewCrossRuntimeCommand.cs", "CrossRuntimeReviewRuntimes.cs", "CrossRuntimeReviewPaths.cs",
                     "CrossRuntimeReviewVerdict.cs", "CrossRuntimeReviewRecord.cs", "CrossRuntimeReviewGate.cs",
                     "CrossRuntimeReviewTeamResolver.cs", "CrossRuntimeReviewJsonlVerdict.cs",
                     "CrossRuntimeReviewHomeAccessGuard.cs", "CrossRuntimeReviewOpencodeConfig.cs",
                     "CrossRuntimeReviewRequestSupport.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(sourceRoot, file));
            Assert.DoesNotContain("System.Diagnostics", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new NotifyProcessRunner", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProcessRunnerFactory?.Invoke", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NestedProviderLauncher?.Invoke", source, StringComparison.Ordinal);
        }
    }

    // ── record ─────────────────────────────────────────────────────────

    [Fact]
    public void Record_AcceptsTheCodexBareVerdictAndTheMeasuredClaudeAndCursorEnvelopes_FromRealRunFixtures()
    {
        foreach (var (runtime, fixture) in new[] { ("codex", "codex-verdict.json"), ("claude", "claude-envelope.json"), ("cursor", "cursor-envelope.json") })
        {
            var content = File.ReadAllText(Fixture(fixture));
            Assert.True(CrossRuntimeReviewVerdict.TryParse(runtime, content, out var verdict, out var error), $"{runtime}: {error}");
            Assert.True(CrossRuntimeReviewPaths.IsFullHeadSha(verdict.HeadSha), verdict.HeadSha);
            Assert.Contains(verdict.Verdict, CrossRuntimeReviewVerdict.VerdictValues);

            var head = verdict.HeadSha;
            var file = WriteVerdictFile(runtime, content);
            var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs(runtime, file, head, write: false), "--format", "json"]);
            Assert.True(exit == 0, output);
        }
    }

    [Fact]
    public void Record_CursorEnvelope_MeasuredShape_NarrationThenVerdict_AndBareObjectsAreRefusedForEnvelopeRuntimes()
    {
        // The real cursor-agent run's envelope: these keys, and a result text whose
        // progress narration precedes the verdict object.
        using (var measured = JsonDocument.Parse(File.ReadAllText(Fixture("cursor-envelope.json"))))
        {
            Assert.Equal(
                ["duration_api_ms", "duration_ms", "is_error", "request_id", "result", "session_id", "subtype", "type"],
                measured.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            var text = measured.RootElement.GetProperty("result").GetString()!;
            Assert.False(text.TrimStart().StartsWith('{'));
            Assert.EndsWith("}", text.TrimEnd(), StringComparison.Ordinal);
        }

        var narrated = CursorEnvelope("Confirming HEAD and gathering review guidance {braces in prose}." + Verdict("approve", H1));
        Assert.True(CrossRuntimeReviewVerdict.TryParse("cursor", narrated, out var cursor, out var cursorError), cursorError);
        Assert.Equal(H1, cursor.HeadSha);
        Assert.True(CrossRuntimeReviewVerdict.TryParse("cursor", CursorEnvelope(Verdict("approve", H1)), out _, out _));
        Assert.False(CrossRuntimeReviewVerdict.TryParse("cursor", CursorEnvelope(Verdict("approve", H1) + " done."), out _, out var trailingError));
        Assert.Contains("does not end with a verdict JSON object", trailingError, StringComparison.Ordinal);
        Assert.False(CrossRuntimeReviewVerdict.TryParse("cursor", CursorEnvelope("no verdict at all"), out _, out _));

        foreach (var runtime in new[] { "claude", "cursor" })
        {
            Assert.False(CrossRuntimeReviewVerdict.TryParse(runtime, Verdict("approve", H1), out _, out var error));
            Assert.Contains("bare verdict object is refused", error, StringComparison.Ordinal);
            var file = WriteVerdictFile(runtime, Verdict("approve", H1));
            var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs(runtime, file, H1, write: true), "--format", "json"]);
            Assert.Equal(1, exit);
            Assert.Contains(CrossRuntimeReviewCauses.VerdictInvalid, output, StringComparison.Ordinal);
        }

        var failed = JsonSerializer.Serialize(new { type = "result", subtype = "error_max_turns", is_error = true, structured_output = new { } });
        Assert.False(CrossRuntimeReviewVerdict.TryParse("claude", failed, out _, out _));
    }

    [Theory]
    [InlineData("head-mismatch")]
    [InlineData("invalid-json")]
    [InlineData("approve-with-findings")]
    [InlineData("request-changes-without-findings")]
    [InlineData("unknown-runtime")]
    [InlineData("undeclared-team")]
    public void Record_NamedRefusals(string scenario)
    {
        var content = scenario switch
        {
            "invalid-json" => "{not json",
            "approve-with-findings" => $"{{\"verdict\":\"approve\",\"head_sha\":\"{H1}\",\"blocking_findings\":[{{\"file\":\"a.cs\",\"line\":3,\"scenario\":\"x\"}}],\"notes\":[]}}",
            "request-changes-without-findings" => $"{{\"verdict\":\"request-changes\",\"head_sha\":\"{H1}\",\"blocking_findings\":[],\"notes\":[]}}",
            _ => Verdict("approve", H1),
        };
        var file = WriteVerdictFile("codex", content);
        var context = scenario == "undeclared-team" ? Context(declare: false) : Context();
        var args = RecordArgs(scenario == "unknown-runtime" ? "gemini" : "codex", file, scenario == "head-mismatch" ? H2 : H1, write: true);

        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"], context);
        Assert.Equal(1, exit);
        var expected = scenario switch
        {
            "head-mismatch" => CrossRuntimeReviewCauses.HeadMismatch,
            "unknown-runtime" => CrossRuntimeReviewCauses.RuntimeInvalid,
            "undeclared-team" => CrossRuntimeReviewCauses.NotDeclared,
            _ => CrossRuntimeReviewCauses.VerdictInvalid,
        };
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(expected, refusal.RootElement.GetProperty("cause").GetString());
        Assert.False(Directory.Exists(Path.Combine(root, ".intent-cli", "cross-runtime-reviews")));
    }

    [Fact]
    public void Record_Write_CreatesRecordAndRawCopy_RelationFromDeclaredConductor_AndCommentBody()
    {
        var content = Verdict("request-changes", H1);
        var file = WriteVerdictFile("codex", content);
        var commentOut = Path.Combine(root, "comments", "codex.md");
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("codex", file, H1, write: true), "--comment-out", commentOut, "--format", "json"]);
        Assert.True(exit == 0, output);

        using var result = JsonDocument.Parse(output);
        var record = result.RootElement.GetProperty("record");
        Assert.Equal("cross-runtime-review-record", record.GetProperty("artifact_kind").GetString());
        Assert.Equal("cross-runtime", record.GetProperty("relation").GetString());
        Assert.Equal("claude", record.GetProperty("conductor_runtime").GetString());
        Assert.Equal(Domain, record.GetProperty("domain").GetString());
        Assert.Equal(Team, record.GetProperty("team").GetString());
        foreach (var field in new[] { "artifact_kind", "repo", "pr", "head_sha", "execution_unit", "domain", "team", "kind", "runtime", "runtime_version", "conductor_runtime", "relation", "verdict", "blocking_findings", "notes", "recorded_at", "raw_verdict_file", "raw_verdict_sha256" })
        {
            Assert.True(record.TryGetProperty(field, out _), field);
        }

        var recordFile = Path.Combine(root, result.RootElement.GetProperty("record_file").GetString()!);
        var rawFile = Path.Combine(root, record.GetProperty("raw_verdict_file").GetString()!);
        Assert.StartsWith(Path.Combine(root, ".intent-cli", "cross-runtime-reviews", "j-tech-japan__intent-system", $"pr-{Pr}"), recordFile, StringComparison.Ordinal);
        Assert.True(File.Exists(recordFile));
        Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(rawFile));
        Assert.Equal(CrossRuntimeReviewStore.Sha256Hex(File.ReadAllBytes(file)), record.GetProperty("raw_verdict_sha256").GetString());
        Assert.Contains("only in this checkout until it is committed and pushed", result.RootElement.GetProperty("durability").GetString(), StringComparison.Ordinal);

        var body = File.ReadAllText(commentOut);
        foreach (var expected in new[] { "cross-runtime review", "runtime: codex", "runtime version: codex-cli 0.154.0", $"head SHA: {H1}", "kind: implementation", "verdict: request-changes", "`src/A.cs:12`" })
        {
            Assert.Contains(expected, body, StringComparison.Ordinal);
        }

        var sameFile = WriteVerdictFile("claude", ClaudeEnvelope(Verdict("approve", H1)));
        var (sameExit, sameOutput) = Route(["review", "cross-runtime", .. RecordArgs("claude", sameFile, H1, write: true), "--format", "json"]);
        Assert.True(sameExit == 0, sameOutput);
        using var same = JsonDocument.Parse(sameOutput);
        Assert.Equal("same-runtime", same.RootElement.GetProperty("record").GetProperty("relation").GetString());
        Assert.Contains("independent same-runtime subagent review", same.RootElement.GetProperty("comment_body").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_DryRunWritesNothing_AndWriteNeverOverwrites_CollisionFails()
    {
        var file = WriteVerdictFile("codex", Verdict("approve", H1));
        var (dryExit, dryOutput) = Route(["review", "cross-runtime", .. RecordArgs("codex", file, H1, write: false), "--format", "json"]);
        Assert.Equal(0, dryExit);
        Assert.Contains("\"would-record\"", dryOutput, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, ".intent-cli", "cross-runtime-reviews")));

        var fixedClock = new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
        ReviewCrossRuntimeCommand.Clock = () => fixedClock;
        var (firstExit, firstOutput) = Route(["review", "cross-runtime", .. RecordArgs("codex", file, H1, write: true), "--format", "json"]);
        Assert.Equal(0, firstExit);
        using var first = JsonDocument.Parse(firstOutput);
        var recordPath = Path.Combine(root, first.RootElement.GetProperty("record_file").GetString()!);
        var before = File.ReadAllBytes(recordPath);

        var other = WriteVerdictFile("codex", Verdict("request-changes", H1));
        var (secondExit, secondOutput) = Route(["review", "cross-runtime", .. RecordArgs("codex", other, H1, write: true), "--format", "json"]);
        Assert.Equal(1, secondExit);
        Assert.Contains(CrossRuntimeReviewCauses.RecordCollision, secondOutput, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(recordPath));
        Assert.Equal(2, Directory.EnumerateFiles(Path.GetDirectoryName(recordPath)!).Count());
    }

    [Fact]
    public void Record_RefusesAVerdictForASupersededHead_SoALateOlderApproveCannotClearARereviewRequirement()
    {
        var clock = new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
        ReviewCrossRuntimeCommand.Clock = () => clock;
        void Record(string runtime, string verdict, string head, int expectedExit)
        {
            clock = clock.AddMinutes(1);
            var content = runtime == "codex" ? Verdict(verdict, head) : runtime == "claude" ? ClaudeEnvelope(Verdict(verdict, head)) : CursorEnvelope(Verdict(verdict, head));
            var file = WriteVerdictFile(runtime, content);
            var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs(runtime, file, head, write: true), "--format", "json"]);
            Assert.True(exit == expectedExit, output);
            if (expectedExit != 0)
            {
                Assert.Contains(CrossRuntimeReviewCauses.HeadSuperseded, output, StringComparison.Ordinal);
            }
        }

        // codex blocks H1; the fix lands as H2 and the other runtimes approve it.
        Record("claude", "approve", H1, 0);
        Record("codex", "request-changes", H1, 0);
        Record("claude", "approve", H2, 0);
        Record("cursor", "approve", H2, 0);
        string H2Decision() => CrossRuntimeReviewGate.Evaluate(Declaration("claude"), Resolution(), H2, CrossRuntimeReviewStore.Read(root, Repo, Pr)).Decision;
        var before = CrossRuntimeReviewGate.Evaluate(Declaration("claude"), Resolution(), H2, CrossRuntimeReviewStore.Read(root, Repo, Pr));
        Assert.Contains(before.Reasons, reason => reason.Cause == CrossRuntimeReviewCauses.RereviewMissing);

        // A late codex approve for the superseded H1 is refused (dry-run too),
        // so it cannot become codex's most recent earlier-head record.
        var lateH1 = WriteVerdictFile("codex", Verdict("approve", H1));
        var (dryExit, dryOutput) = Route(["review", "cross-runtime", .. RecordArgs("codex", lateH1, H1, write: false), "--format", "json"]);
        Assert.Equal(1, dryExit);
        Assert.Contains(CrossRuntimeReviewCauses.HeadSuperseded, dryOutput, StringComparison.Ordinal);
        Record("codex", "approve", H1, 1);
        var after = CrossRuntimeReviewGate.Evaluate(Declaration("claude"), Resolution(), H2, CrossRuntimeReviewStore.Read(root, Repo, Pr));
        Assert.Contains(after.Reasons, reason => reason.Cause == CrossRuntimeReviewCauses.RereviewMissing);
        Assert.NotEqual("satisfied", H2Decision());

        // Re-recording the newest head and recording a never-seen head both pass.
        Record("cursor", "approve", H2, 0);
        Record("codex", "approve", H2, 0);
        Assert.Equal("satisfied", H2Decision());
        Record("claude", "approve", H3, 0);
    }

    // ── gate ───────────────────────────────────────────────────────────

    [Fact]
    public void Gate_NotRequired_ForAnUndeclaredTeam()
    {
        var (exit, status) = Status(H1, Context(declare: false));
        Assert.Equal(0, exit);
        Assert.False(status.GetProperty("declared").GetBoolean());
        Assert.Equal("not-required", Decision(status));
    }

    [Fact]
    public void Gate_MissingEachRelation_StaleHeadNotCounted_SatisfiedOnlyWithBothApprovesOnH()
    {
        Assert.Equal(["cross-runtime-review-missing:same-runtime", "cross-runtime-review-missing:cross-runtime"], Reasons(Status(H2).Status));

        RecordVerdict("claude", "approve", H1);
        RecordVerdict("codex", "approve", H1);
        var stale = Status(H2).Status;
        Assert.Equal("missing", Decision(stale));
        Assert.All(stale.GetProperty("gate").GetProperty("records").EnumerateArray(), entry => Assert.Equal("stale-head", entry.GetProperty("status").GetString()));

        RecordVerdict("claude", "approve", H2);
        Assert.Equal(["cross-runtime-review-missing:cross-runtime"], Reasons(Status(H2).Status));

        RecordVerdict("cursor", "approve", H2);
        var satisfied = Status(H2).Status;
        Assert.Equal("satisfied", Decision(satisfied));
        Assert.Empty(Reasons(satisfied));

        var read = CrossRuntimeReviewStore.Read(root, Repo, Pr);
        var crossOnly = new CrossRuntimeReviewReadResult(read.Records.Where(stored => stored.Record.Runtime != "claude").ToArray(), []);
        var gate = CrossRuntimeReviewGate.Evaluate(Declaration("claude"), Resolution(), H2, crossOnly);
        Assert.Equal("missing", gate.Decision);
        Assert.Equal(CrossRuntimeReviewCauses.Missing, Assert.Single(gate.Reasons).Cause);
        Assert.Equal("same-runtime", gate.Reasons[0].Relation);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void Gate_BlockedByTheLatestRecordOfEitherRelation(string blocker)
    {
        RecordVerdict("claude", blocker == "claude" ? "request-changes" : "approve", H1);
        RecordVerdict("codex", blocker == "codex" ? "request-changes" : "approve", H1);
        var status = Status(H1).Status;
        Assert.Equal("blocked", Decision(status));
        var reason = status.GetProperty("gate").GetProperty("reasons").EnumerateArray().Single(item => item.GetProperty("cause").GetString() == CrossRuntimeReviewCauses.Blocked);
        Assert.Equal([blocker], reason.GetProperty("runtimes").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void Gate_SameRuntimeRequestChanges_SupersededByThatRuntimesLaterApproveOnTheSameHead_AndStaysListed()
    {
        RecordVerdict("claude", "request-changes", H1);
        RecordVerdict("codex", "approve", H1);
        Assert.Equal("blocked", Decision(Status(H1).Status));

        RecordVerdict("codex", "approve", H1);
        Assert.Equal("blocked", Decision(Status(H1).Status));

        RecordVerdict("claude", "approve", H1);
        var status = Status(H1).Status;
        Assert.Equal("satisfied", Decision(status));
        var statuses = status.GetProperty("gate").GetProperty("records").EnumerateArray()
            .Select(entry => (entry.GetProperty("runtime").GetString(), entry.GetProperty("verdict").GetString(), entry.GetProperty("status").GetString()))
            .ToArray();
        Assert.Contains(("claude", "request-changes", "superseded"), statuses);
        Assert.Contains(("claude", "approve", "deciding"), statuses);
    }

    [Fact]
    public void Gate_RereviewMissing_AfterAnEarlierHeadBlock_AndH1BlockH2ApproveIsResolvedForH3()
    {
        RecordVerdict("claude", "approve", H1);
        RecordVerdict("codex", "request-changes", H1);
        RecordVerdict("claude", "approve", H2);
        RecordVerdict("cursor", "approve", H2);
        var h2 = Status(H2).Status;
        Assert.Equal("missing", Decision(h2));
        var rereview = h2.GetProperty("gate").GetProperty("reasons").EnumerateArray().Single();
        Assert.Equal(CrossRuntimeReviewCauses.RereviewMissing, rereview.GetProperty("cause").GetString());
        Assert.Equal(["codex"], rereview.GetProperty("runtimes").EnumerateArray().Select(item => item.GetString()));

        RecordVerdict("codex", "approve", H2);
        Assert.Equal("satisfied", Decision(Status(H2).Status));

        RecordVerdict("claude", "approve", H3);
        RecordVerdict("cursor", "approve", H3);
        var h3 = Status(H3).Status;
        Assert.Equal("satisfied", Decision(h3));
    }

    [Fact]
    public void Gate_ForeignRecords_NeverCounted_AndListed()
    {
        RecordVerdict("claude", "approve", H1);
        RecordVerdict("codex", "approve", H1);
        var read = CrossRuntimeReviewStore.Read(root, Repo, Pr);
        Assert.Equal("satisfied", CrossRuntimeReviewGate.Evaluate(Declaration("claude"), Resolution(), H1, read).Decision);

        foreach (var foreign in new[]
                 {
                     Resolution() with { ExecutionUnit = "G999" },
                     Resolution() with { Team = "other-team" },
                     Resolution() with { Domain = "other-domain" },
                 })
        {
            var gate = CrossRuntimeReviewGate.Evaluate(Declaration("claude"), foreign, H1, read);
            Assert.Equal("missing", gate.Decision);
            Assert.All(gate.Records, entry => Assert.Equal("foreign", entry.Status));
            Assert.Equal(2, gate.Records.Count);
        }

        var otherKind = new CrossRuntimeReviewReadResult(
            read.Records.Select(stored => stored with { Record = stored.Record with { Kind = "design" } }).ToArray(), []);
        var kindGate = CrossRuntimeReviewGate.Evaluate(Declaration("claude"), Resolution(), H1, otherKind);
        Assert.Equal("missing", kindGate.Decision);
        Assert.All(kindGate.Records, entry => Assert.Equal("foreign", entry.Status));
    }

    [Fact]
    public void Gate_RelationIsRecomputed_WhenTheDeclaredConductorRuntimeChanges()
    {
        RecordVerdict("claude", "approve", H1);
        RecordVerdict("codex", "approve", H1);
        Assert.Equal("satisfied", Decision(Status(H1).Status));

        var read = CrossRuntimeReviewStore.Read(root, Repo, Pr);
        Assert.All(read.Records.Where(stored => stored.Record.Runtime == "claude"), stored => Assert.Equal("same-runtime", stored.Record.Relation));
        var asCursorConductor = CrossRuntimeReviewGate.Evaluate(Declaration("cursor"), Resolution(), H1, read);
        Assert.Equal("missing", asCursorConductor.Decision);
        Assert.Equal("same-runtime", Assert.Single(asCursorConductor.Reasons).Relation);
        Assert.All(asCursorConductor.Records, entry => Assert.Equal("cross-runtime", entry.Relation));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("raw-tampered")]
    [InlineData("wrong-pr")]
    public void Gate_UnreadableRecord_FailsClosed(string corruption)
    {
        RecordVerdict("claude", "approve", H1);
        RecordVerdict("codex", "approve", H1);
        var directory = CrossRuntimeReviewPaths.PrDirectory(root, Repo, Pr);
        var target = Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal).First();
        switch (corruption)
        {
            case "garbage":
                File.WriteAllText(Path.Combine(directory, "20990101T000000Z-codex-zzzzzzz.json"), "{\"artifact_kind\":\"nope\"}");
                break;
            case "raw-tampered":
                File.AppendAllText(Path.ChangeExtension(target, ".raw-verdict"), " ");
                break;
            default:
                File.WriteAllText(target, File.ReadAllText(target).Replace($"\"pr\": {Pr}", "\"pr\": 5", StringComparison.Ordinal));
                break;
        }

        var status = Status(H1).Status;
        Assert.Equal("blocked", Decision(status));
        Assert.Contains(CrossRuntimeReviewCauses.RecordUnreadable, Reasons(status).Select(reason => reason.Split(':')[0]));
        Assert.NotEmpty(status.GetProperty("gate").GetProperty("unreadable").EnumerateArray());
    }

    [Fact]
    public void Status_Markdown_ReportsSourcesDeclarationRecordsAndDecision()
    {
        RecordVerdict("claude", "request-changes", H1);
        var (exit, output) = Route(["review", "cross-runtime", .. StatusArgs(H1)]);
        Assert.Equal(0, exit);
        foreach (var expected in new[] { "queue-state:linked_pr", "packet:implementation_issue_packet.domain", "claim:execution-unit", "config:cross_runtime_review.teams", "conductor runtime claude", "decision: blocked", "[deciding] claude (same-runtime) request-changes" })
        {
            Assert.Contains(expected, output, StringComparison.Ordinal);
        }
    }

    // ── pr-transition ──────────────────────────────────────────────────

    [Theory]
    [InlineData("review-start")]
    [InlineData("request-update")]
    [InlineData("approved")]
    [InlineData("review-release")]
    public void PrTransition_UngatedRepoOnADeclaringHost_IsByteIdenticalToNoDeclaration_AndPerformsNoResolution(string transition)
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, _) => throw new InvalidOperationException("an ungated repository must never be resolved");
        AutomationPrTransitionCommand.PrHeadReader = (_, _) => throw new InvalidOperationException("an ungated repository must never read the head");
        foreach (var write in new[] { false, true })
        {
            foreach (var format in new[] { "json", "text" })
            {
                var undeclared = RunTransition(Context(declare: false), "J-Tech-Japan/other", transition, write, format, []);
                var declaring = RunTransition(Context(), "J-Tech-Japan/other", transition, write, format, ["--head-sha", H1]);
                Assert.Equal(undeclared.Output, declaring.Output);
                Assert.Equal(undeclared.ExitCode, declaring.ExitCode);
                Assert.DoesNotContain("cross_runtime_review", declaring.Output, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void PrTransition_GatedRepo_UndeclaredTeam_IsByteIdentical()
    {
        claims[Unit] = "some-other-team";
        AutomationPrTransitionCommand.PrHeadReader = (_, _) => throw new InvalidOperationException("an undeclared team must never read the head");
        foreach (var write in new[] { false, true })
        {
            var baseline = RunTransition(Context(declare: false), Repo, "approved", write, "json", []);
            var gated = RunTransition(Context(), Repo, "approved", write, "json", []);
            Assert.Equal(baseline.Output, gated.Output);
            Assert.Equal(0, gated.ExitCode);
        }
    }

    [Theory]
    [InlineData("head-required")]
    [InlineData("head-stale")]
    [InlineData("team-unresolved")]
    [InlineData("missing")]
    [InlineData("blocked")]
    [InlineData("rereview-missing")]
    [InlineData("record-unreadable")]
    public void PrTransition_DeclaredTeam_RefusesWithLabelsUntouchedAndCiWaitKept_InWriteAndDryRun(string scenario)
    {
        var args = new List<string> { "--head-sha", H2 };
        var currentHead = H2;
        switch (scenario)
        {
            case "head-required":
                args.Clear();
                break;
            case "head-stale":
                currentHead = H3;
                break;
            case "team-unresolved":
                claims.Remove(Unit);
                break;
            case "missing":
                RecordVerdict("claude", "approve", H2);
                break;
            case "blocked":
                RecordVerdict("claude", "approve", H2);
                RecordVerdict("codex", "request-changes", H2);
                break;
            case "rereview-missing":
                RecordVerdict("cursor", "request-changes", H1);
                RecordVerdict("claude", "approve", H2);
                RecordVerdict("codex", "approve", H2);
                break;
            case "record-unreadable":
                RecordVerdict("claude", "approve", H2);
                RecordVerdict("codex", "approve", H2);
                File.WriteAllText(Path.Combine(CrossRuntimeReviewPaths.PrDirectory(root, Repo, Pr), "zz.json"), "{}");
                break;
        }

        var expectedCause = "cross-runtime-review-" + scenario;
        AutomationPrTransitionCommand.PrHeadReader = (_, _) => currentHead;
        Assert.True(CiWaitStore.Record(root, new CiWaitRecord { Domain = Domain, Repo = Repo, Pr = Pr, ObservedHead = H2, OwedTransition = "approved", RecordedAt = DateTimeOffset.UtcNow }, write: true).Applied);

        foreach (var write in new[] { true, false })
        {
            var mutator = new RecordingMutator { Labels = ["intent-pr-reviewing"] };
            var (exit, output) = RunTransition(Context(), Repo, "approved", write, "json", args.ToArray(), mutator);
            Assert.Equal(1, exit);
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
            var gate = result.RootElement.GetProperty("cross_runtime_review");
            Assert.Equal(expectedCause, gate.GetProperty("cause").GetString());
            Assert.StartsWith(expectedCause, result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
            var detail = gate.GetProperty("detail").GetString()!;
            if (scenario == "head-required")
            {
                Assert.Contains("--head-sha <head-sha>", detail, StringComparison.Ordinal);
            }

            if (scenario == "team-unresolved")
            {
                Assert.Contains("intent-cli claim acquire", detail, StringComparison.Ordinal);
            }

            Assert.Empty(mutator.Applied);
            Assert.Single(CiWaitStore.ReadOpen(root, repo: Repo).Records);
        }
    }

    [Fact]
    public void PrTransition_DeclaredTeam_UnlinkedPrUsesExecutionUnit_AndAppliesWhenSatisfied()
    {
        WriteQueue((Unit, null));
        AutomationPrTransitionCommand.PrHeadReader = (repo, pr) => repo == Repo && pr == Pr ? H2 : throw new InvalidOperationException();
        var unlinked = RunTransition(Context(), Repo, "approved", write: false, "json", ["--head-sha", H2]);
        Assert.Equal(1, unlinked.ExitCode);
        using (var unlinkedResult = JsonDocument.Parse(unlinked.Output))
        {
            Assert.Contains("--execution-unit <unit>", unlinkedResult.RootElement.GetProperty("cross_runtime_review").GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        RecordVerdict("claude", "approve", H2, withUnitArgument: true);
        RecordVerdict("cursor", "approve", H2, withUnitArgument: true);
        Assert.True(CiWaitStore.Record(root, new CiWaitRecord { Domain = Domain, Repo = Repo, Pr = Pr, ObservedHead = H2, OwedTransition = "approved", RecordedAt = DateTimeOffset.UtcNow }, write: true).Applied);

        var dry = RunTransition(Context(), Repo, "approved", write: false, "json", ["--head-sha", H2, "--execution-unit", Unit]);
        Assert.Equal(0, dry.ExitCode);
        using (var dryResult = JsonDocument.Parse(dry.Output))
        {
            Assert.Equal("satisfied", dryResult.RootElement.GetProperty("cross_runtime_review").GetProperty("decision").GetString());
            Assert.Equal(Team, dryResult.RootElement.GetProperty("cross_runtime_review").GetProperty("team").GetString());
        }

        var mutator = new RecordingMutator { Labels = ["intent-pr-reviewing"] };
        var applied = RunTransition(Context(), Repo, "approved", write: true, "text", ["--head-sha", H2, "--execution-unit", Unit], mutator);
        Assert.Equal(0, applied.ExitCode);
        Assert.Contains("applied: true", applied.Output, StringComparison.Ordinal);
        Assert.Contains("cross_runtime_review: satisfied", applied.Output, StringComparison.Ordinal);
        Assert.Single(mutator.Applied);
        Assert.Empty(CiWaitStore.ReadOpen(root, repo: Repo).Records);

        WriteQueue((Unit, $"https://github.com/{Repo}/pull/{Pr}"), ("G999", null));
        var mismatch = RunTransition(Context(), Repo, "approved", write: false, "json", ["--head-sha", H2, "--execution-unit", "G999"]);
        Assert.Equal(1, mismatch.ExitCode);
        Assert.Contains(CrossRuntimeReviewCauses.UnitMismatch, mismatch.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PrTransition_GatedRepo_OtherTransitionsAreUnaffected()
    {
        claims.Remove(Unit);
        AutomationPrTransitionCommand.PrHeadReader = (_, _) => throw new InvalidOperationException("never");
        foreach (var transition in new[] { "review-start", "request-update", "review-release" })
        {
            var baseline = RunTransition(Context(declare: false), Repo, transition, write: true, "json", []);
            var gated = RunTransition(Context(), Repo, transition, write: true, "json", []);
            Assert.Equal(baseline.Output, gated.Output);
        }
    }

    // ── guides and docs ────────────────────────────────────────────────

    [Fact]
    public void Routing_ReviewCrossRuntimeIsOneHandler_AndCatalogsRegisterAllThree()
    {
        var registry = (System.Collections.IDictionary)typeof(CommandRouter)
            .GetField("ImplementedCommands", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var review = (System.Collections.IDictionary)registry["review"]!;
        Assert.True(review.Contains("cross-runtime"));
        Assert.False(review.Contains("request"));
        Assert.Equal(["record", "request", "status"], CommandRouter.ReviewCrossRuntimeSubcommands.Keys.OrderBy(key => key, StringComparer.Ordinal));

        var list = GuideCommandsListCommand.Groups.Single(group => group.Name == "review").Purpose;
        using var help = new StringWriter();
        Assert.Equal(0, GuideHelpCommand.Execute(Context(), ["--format", "json"], help));
        foreach (var subcommand in new[] { "request", "record", "status" })
        {
            Assert.Contains(subcommand, list, StringComparison.Ordinal);
            Assert.Contains($"intent-cli review cross-runtime {subcommand}", help.ToString(), StringComparison.Ordinal);
        }

        var isRouted = typeof(CommandRouter).Assembly.GetType("IntentSystem.Cli.Program")!
            .GetMethod("IsReviewCrossRuntimeCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)isRouted.Invoke(null, [new[] { "review", "cross-runtime", "status" }])!);

        var (helpExit, groupHelp) = Route(["review", "cross-runtime", "--help"]);
        Assert.Equal(0, helpExit);
        Assert.Contains(ReviewCrossRuntimeCommand.RequestUsage, groupHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void Guides_SoloConductorAndReview_DescribeTheFlow_TermsAndNoLaunchClaim()
    {
        var guide = GuideSoloConductorCommand.BuildGuide();
        var step7 = guide.Loop.Single(step => step.Number == 7);
        Assert.Contains(step7.Commands, command => command.Command.StartsWith("intent-cli review cross-runtime request ", StringComparison.Ordinal));
        Assert.Contains(step7.Commands, command => command.Command.StartsWith("intent-cli review cross-runtime record ", StringComparison.Ordinal) && command.Command.Contains("--write", StringComparison.Ordinal));
        Assert.Contains(step7.Commands, command => command.Command.StartsWith("gh pr review ", StringComparison.Ordinal) && command.Command.Contains("--comment --body-file", StringComparison.Ordinal));
        Assert.Contains("invocation.txt", step7.Instruction, StringComparison.Ordinal);
        Assert.Contains("re-review", guide.Loop.Single(step => step.Number == 8).Instruction, StringComparison.Ordinal);
        var step9 = guide.Loop.Single(step => step.Number == 9);
        Assert.Contains(step9.Commands, command => command.Command.Contains("--transition approved --head-sha <head-sha>", StringComparison.Ordinal));
        Assert.Contains("host root", step9.Instruction, StringComparison.Ordinal);

        using var solo = new StringWriter();
        Assert.Equal(0, GuideSoloConductorCommand.Execute(Context(), ["--format", "markdown"], solo));
        using var review = new StringWriter();
        GuideReviewCommand.Execute(Context(), ["--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--repo", Repo, "--domain", Domain, "--format", "markdown"], review);
        Assert.Contains("## Cross-runtime review (declared teams, G834)", review.ToString(), StringComparison.Ordinal);

        var affirmative = new Regex(@"intent-cli (starts|launches|manages|spawns|runs) (an? |the )?(agent|subagent|reviewer|provider)", RegexOptions.IgnoreCase);
        foreach (var text in new[] { solo.ToString(), review.ToString(), ReviewCrossRuntimeCommand.NoExecutionBoundary, ReviewCrossRuntimeCommand.TermsNotice })
        {
            Assert.DoesNotMatch(affirmative, text);
        }

        foreach (var text in new[] { solo.ToString(), review.ToString(), ReviewCrossRuntimeCommand.TermsNotice })
        {
            Assert.Contains("automation terms", text, StringComparison.Ordinal);
        }

        Assert.Contains("operator's responsibility", solo.ToString(), StringComparison.Ordinal);
        foreach (var text in new[] { solo.ToString(), review.ToString() })
        {
            Assert.Contains("codex `-s read-only` is sandbox-enforced", text, StringComparison.Ordinal);
            Assert.Contains("shell-level writes are not sandbox-enforced", text, StringComparison.Ordinal);
            Assert.Contains("cursor `--mode ask` refuses every non-read-only tool", text, StringComparison.Ordinal);
        }

        var reviewRules = string.Join("\n", GuideReviewCommand.CrossRuntimeReviewRules);
        Assert.Contains("--runtime codex|claude|cursor|copilot|opencode", reviewRules, StringComparison.Ordinal);
        Assert.Contains("`--available-tools view rg glob` leaves only those three tools", reviewRules, StringComparison.Ordinal);
        Assert.Contains("`COPILOT_HOME` and `XDG_CONFIG_HOME` point at empty directories created under the out-dir", reviewRules, StringComparison.Ordinal);
        Assert.Contains("`--no-custom-instructions` stops the reviewed workspace from instructing the reviewer", reviewRules, StringComparison.Ordinal);
        Assert.Contains("The rendered config denies every tool (`*`) and allows only read, glob, grep and list", reviewRules, StringComparison.Ordinal);
        Assert.Contains("`OPENCODE_DISABLE_PROJECT_CONFIG=1` and `--pure` stop", reviewRules, StringComparison.Ordinal);

        Assert.Contains("not sandbox-enforced", CrossRuntimeReviewRuntimes.ReadOnlyEnforcement["claude"], StringComparison.Ordinal);
        Assert.Contains("sandbox-enforced", CrossRuntimeReviewRuntimes.ReadOnlyEnforcement["codex"], StringComparison.Ordinal);
        Assert.Contains("operator's responsibility", review.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cross-review ", string.Join("\n", GuideReviewCommand.CrossRuntimeReviewRules), StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_EnJa_SectionAndLedgerRows()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var orchestration = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("G834", orchestration, StringComparison.Ordinal);
            Assert.Contains("[[cross_runtime_review.teams]]", orchestration, StringComparison.Ordinal);
            Assert.Contains("--head-sha", orchestration, StringComparison.Ordinal);

            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            foreach (var row in new[] { "| `review cross-runtime request` |", "| `review cross-runtime record` |", "| `review cross-runtime status` |", "| cross-runtime review declaration |" })
            {
                var line = Assert.Single(ledger.Split('\n'), candidate => candidate.StartsWith(row, StringComparison.Ordinal));
                Assert.Contains("G834", line, StringComparison.Ordinal);
                Assert.Contains("`preview-through-1.x`", line, StringComparison.Ordinal);
            }

            Assert.Contains("`review cross-runtime`", ledger, StringComparison.Ordinal);
            Assert.Contains("`pr-transition --head-sha`", ledger, StringComparison.Ordinal);
        }
    }

    // ── fixtures ───────────────────────────────────────────────────────

    private CliContext Context(bool declare = true, string conductor = "claude") => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            CrossRuntimeReview = declare
                ? new CrossRuntimeReviewConfig { Teams = [Declaration(conductor)] }
                : new CrossRuntimeReviewConfig(),
        },
    };

    private static CrossRuntimeReviewTeamDeclaration Declaration(string conductor) => new()
    {
        Team = $"{Domain}/{Team}",
        ConductorRuntime = conductor,
        Repos = [Repo],
    };

    private static CrossRuntimeReviewResolution Resolution() => new()
    {
        Resolved = true,
        ExecutionUnit = Unit,
        Domain = Domain,
        Team = Team,
    };

    private (int ExitCode, string Output) Route(string[] args, CliContext? context = null)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, context ?? Context(), writer);
        return (exit, writer.ToString());
    }

    private static string[] RequestArgs(string runtime, string clone, string outDir, string? model = null, string? effort = null)
    {
        var args = new List<string>
        {
            "request", "--repo", Repo, "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--head-sha", H1,
            "--execution-unit", Unit, "--runtime", runtime, "--clone", clone, "--out-dir", outDir,
        };
        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        if (effort is not null)
        {
            args.AddRange(["--effort", effort]);
        }

        return args.ToArray();
    }

    private static string[] RecordArgs(string runtime, string verdictFile, string head, bool write, bool withUnit = true)
    {
        var args = new List<string>
        {
            "record", "--repo", Repo, "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--head-sha", head,
            "--kind", "implementation", "--runtime", runtime, "--runtime-version", runtime == "codex" ? "codex-cli 0.154.0" : "2.1.269",
            "--verdict-file", verdictFile,
        };
        if (withUnit)
        {
            args.AddRange(["--execution-unit", Unit]);
        }

        if (write)
        {
            args.Add("--write");
        }

        return args.ToArray();
    }

    private static string[] StatusArgs(string head) =>
        ["status", "--repo", Repo, "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--head-sha", head, "--execution-unit", Unit];

    private (int ExitCode, JsonElement Status) Status(string head, CliContext? context = null)
    {
        var (exit, output) = Route(["review", "cross-runtime", .. StatusArgs(head), "--format", "json"], context);
        using var document = JsonDocument.Parse(output);
        return (exit, document.RootElement.Clone());
    }

    private static string Decision((int, JsonElement Status) status) => Decision(status.Status);

    private static string Decision(JsonElement status) =>
        status.GetProperty("gate").GetProperty("decision").GetString()!;

    private static string[] Reasons(JsonElement status) =>
        status.GetProperty("gate").GetProperty("reasons").EnumerateArray()
            .Select(reason => reason.GetProperty("cause").GetString()
                + (reason.TryGetProperty("relation", out var relation) ? ":" + relation.GetString() : string.Empty))
            .ToArray();

    private void RecordVerdict(string runtime, string verdict, string head, bool withUnitArgument = true)
    {
        var body = Verdict(verdict, head);
        var content = runtime == "codex" ? body : runtime == "claude" ? ClaudeEnvelope(body) : CursorEnvelope(body);
        var file = WriteVerdictFile(runtime, content);
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs(runtime, file, head, write: true, withUnitArgument), "--format", "json"]);
        Assert.True(exit == 0, output);
    }

    private string WriteVerdictFile(string runtime, string content)
    {
        var directory = Path.Combine(root, "runs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{runtime}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string Verdict(string verdict, string head) => verdict == "approve"
        ? JsonSerializer.Serialize(new { verdict, head_sha = head, blocking_findings = Array.Empty<object>(), notes = new[] { "looks right" } })
        : JsonSerializer.Serialize(new { verdict, head_sha = head, blocking_findings = new[] { new { file = "src/A.cs", line = 12, scenario = "a stale head satisfies the gate" } }, notes = Array.Empty<string>() });

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

    private static bool Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CrossRuntimeReviewVerdict.TryValidate(document.RootElement, out _, out _);
    }

    private static string Fixture(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

    private static void CollectKeywords(JsonElement element, HashSet<string> keywords)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            keywords.Add(property.Name);
            if (property.Name == "properties")
            {
                foreach (var child in property.Value.EnumerateObject())
                {
                    CollectKeywords(child.Value, keywords);
                }
            }
            else
            {
                CollectKeywords(property.Value, keywords);
            }
        }
    }

    /// <summary>A small POSIX-shell tokenizer for single-quoted, double-quoted, and bare words.</summary>
    private static List<string> ShellTokens(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inToken = false;
        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            if (character == '\'')
            {
                inToken = true;
                var end = command.IndexOf('\'', index + 1);
                current.Append(command, index + 1, end - index - 1);
                index = end;
            }
            else if (character == '"')
            {
                inToken = true;
                var end = command.IndexOf('"', index + 1);
                current.Append(command, index + 1, end - index - 1);
                index = end;
            }
            else if (character == '\\' && index + 1 < command.Length)
            {
                inToken = true;
                current.Append(command[++index]);
            }
            else if (char.IsWhiteSpace(character))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                inToken = true;
                current.Append(character);
            }
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private (int ExitCode, string Output) RunTransition(
        CliContext context,
        string repo,
        string transition,
        bool write,
        string format,
        string[] extra,
        RecordingMutator? mutator = null)
    {
        var fake = mutator ?? new RecordingMutator { Labels = ["intent-pr-reviewing", "intent-pr-rereview-ready"] };
        AutomationPrTransitionCommand.MutatorFactory = () => fake;
        var args = new List<string> { "--repo", repo, "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--transition", transition, "--format", format };
        if (write)
        {
            args.Add("--write");
        }

        args.AddRange(extra);
        using var writer = new StringWriter();
        var exit = AutomationPrTransitionCommand.Execute(context, args.ToArray(), writer);
        return (exit, writer.ToString());
    }

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
                ["packet_paths"] = new { implementation = $".intent-cli/issues/{item.Unit}/implementation.md", review_context = $".intent-cli/issues/{item.Unit}/review-context.md", yaml = $".intent-cli/issues/{item.Unit}/packet.yaml" },
                ["linked_issue"] = new { repo = Repo, number = 1811, url = $"https://github.com/{Repo}/issues/1811" },
                ["linked_pr"] = item.LinkedPr,
                ["worker_role"] = "builder",
                ["review_role"] = "reviewer",
                ["priority"] = "high",
            }).ToArray(),
        };
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), JsonSerializer.Serialize(queue));
    }

    private void WriteQueueRaw(string linkedPrJson)
    {
        WriteQueue((Unit, "PLACEHOLDER"));
        var path = Path.Combine(root, ".intent-cli", "queue-state.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"PLACEHOLDER\"", linkedPrJson, StringComparison.Ordinal));
    }

    private void WritePacket(string unit, string? domain)
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            "implementation_issue_packet:\n  issue_title: \"G834\"\n"
            + (domain is null ? string.Empty : $"  domain: {domain}\n")
            + $"  target_repo: {Repo}\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G834\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
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
