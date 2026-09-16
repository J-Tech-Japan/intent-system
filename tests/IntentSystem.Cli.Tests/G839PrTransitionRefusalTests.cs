using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G839PrTransitionRefusalTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G839";
    private const int Pr = 1823;
    private const string H1 = "1111111111111111111111111111111111111111";
    private const string H2 = "2222222222222222222222222222222222222222";
    private const string H3 = "3333333333333333333333333333333333333333";

    private readonly string root = Directory.CreateTempSubdirectory("g839-pr-transition-").FullName;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };

    public G839PrTransitionRefusalTests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return claims.TryGetValue(unit, out var team)
                ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", team ?? string.Empty, "held")
                : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
        };
        AutomationPrTransitionCommand.MutatorFactory = () => new RecordingMutator { Labels = ["intent-pr-reviewing"] };
        AutomationPrTransitionCommand.PrHeadReader = null;
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"),
            "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n\n"
            + "[[cross_runtime_review.teams]]\n"
            + "team = \"intent-cli/intent-cli-dev\"\n"
            + "conductor_runtime = \"claude\"\n"
            + "repos = [\"J-Tech-Japan/intent-system\"]\n");
        WriteQueue((Unit, $"https://github.com/{Repo}/pull/{Pr}"));
        WritePacket(Unit, Domain);
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.PrHeadReader = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("head-required")]
    [InlineData("head-stale")]
    [InlineData("head-unreadable")]
    [InlineData("team-unresolved")]
    [InlineData("missing")]
    [InlineData("blocked")]
    [InlineData("rereview-missing")]
    [InlineData("record-unreadable")]
    public void RefusedTransition_ReportsEmptyPlanAndRefusalSummary(string scenario)
    {
        ConfigureScenario(scenario);
        ConfigurePrHeadReader(scenario);
        AutomationPrTransitionCommand.MutatorFactory = () => new RecordingMutator { Labels = ["intent-pr-reviewing"] };

        var (exitCode, output) = RunTransition(write: false, format: "json", extraArgs: ScenarioArgs(scenario));
        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(output);
        var rootElement = document.RootElement;
        var gate = rootElement.GetProperty("cross_runtime_review");
        var expectedCause = ExpectedCause(scenario);
        var cause = gate.GetProperty("cause").GetString();
        Assert.Equal(expectedCause, cause);
        Assert.Contains(ExpectedDetailFragment(scenario), gate.GetProperty("detail").GetString()!, StringComparison.Ordinal);

        Assert.Empty(rootElement.GetProperty("add_labels").EnumerateArray());
        Assert.Empty(rootElement.GetProperty("remove_labels").EnumerateArray());
        Assert.Empty(rootElement.GetProperty("addLabels").EnumerateArray());
        Assert.Empty(rootElement.GetProperty("removeLabels").EnumerateArray());
        Assert.Equal(
            $"Refused host PR transition 'approved' on PR #{Pr} in {Repo}: {expectedCause}. No labels were changed.",
            rootElement.GetProperty("summary").GetString());
    }

    [Theory]
    [InlineData("head-required")]
    [InlineData("head-stale")]
    [InlineData("team-unresolved")]
    [InlineData("missing")]
    [InlineData("blocked")]
    public void RefusedTransition_PreservesEveryOtherField(string scenario)
    {
        ConfigureScenario(scenario);
        ConfigurePrHeadReader(scenario);
        var mutator = new RecordingMutator { Labels = ["intent-pr-reviewing", "intent-target"] };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;
        var (exitCode, output) = RunTransition(write: true, format: "json", extraArgs: ScenarioArgs(scenario));
        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(output);
        var rootElement = document.RootElement;
        var gate = rootElement.GetProperty("cross_runtime_review");
        var cause = gate.GetProperty("cause").GetString()!;

        Assert.Equal(Repo, rootElement.GetProperty("repo").GetString());
        Assert.Equal(Pr, rootElement.GetProperty("pr").GetInt32());
        Assert.Equal("approved", rootElement.GetProperty("transition").GetString());
        Assert.Equal("write", rootElement.GetProperty("mode").GetString());
        Assert.False(rootElement.GetProperty("applied").GetBoolean());
        Assert.False(rootElement.GetProperty("ci_wait_cleared").GetBoolean());
        Assert.False(rootElement.GetProperty("may_have_applied").GetBoolean());
        Assert.True(rootElement.GetProperty("current_labels").EnumerateArray().Any());
        Assert.Equal($"{cause}: {gate.GetProperty("detail").GetString()}", rootElement.GetProperty("error").GetString());
        Assert.Equal(ExpectedGateDecision(scenario), gate.GetProperty("decision").GetString());
        Assert.Empty(mutator.Applied);
    }

    private static string ExpectedGateDecision(string scenario) =>
        scenario switch
        {
            "missing" => "missing",
            "blocked" => "blocked",
            _ => "refused",
        };

    private void ConfigureScenario(string scenario)
    {
        switch (scenario)
        {
            case "missing":
                RecordVerdict("claude", "approve", H2);
                break;
            case "blocked":
                RecordVerdict("claude", "approve", H2, offsetSeconds: 0);
                RecordVerdict("codex", "request-changes", H2, offsetSeconds: 1);
                break;
            case "rereview-missing":
                RecordVerdict("cursor", "request-changes", H1, offsetSeconds: 0);
                RecordVerdict("claude", "approve", H2, offsetSeconds: 1);
                RecordVerdict("codex", "approve", H2, offsetSeconds: 2);
                break;
            case "record-unreadable":
                RecordVerdict("claude", "approve", H2, offsetSeconds: 0);
                RecordVerdict("codex", "approve", H2, offsetSeconds: 1);
                File.WriteAllText(Path.Combine(CrossRuntimeReviewPaths.PrDirectory(root, Repo, Pr), "zz.json"), "{}");
                break;
            case "team-unresolved":
                claims.Remove(Unit);
                break;
        }
    }

    private static string[] ScenarioArgs(string scenario) =>
        scenario == "head-required" ? Array.Empty<string>() : ["--head-sha", H2];

    private static void ConfigurePrHeadReader(string scenario)
    {
        AutomationPrTransitionCommand.PrHeadReader = scenario switch
        {
            "head-stale" => (_, _) => H3,
            "head-unreadable" => (_, _) => throw new IOException("simulated head read failure"),
            _ => (_, _) => H2,
        };
    }

    private static string ExpectedCause(string scenario) =>
        scenario switch
        {
            "head-required" => "cross-runtime-review-head-required",
            "head-stale" => "cross-runtime-review-head-stale",
            "head-unreadable" => "cross-runtime-review-head-stale",
            "team-unresolved" => "cross-runtime-review-team-unresolved",
            "missing" => "cross-runtime-review-missing",
            "blocked" => "cross-runtime-review-blocked",
            "rereview-missing" => "cross-runtime-review-rereview-missing",
            "record-unreadable" => "cross-runtime-review-record-unreadable",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "unknown refusal scenario"),
        };

    private static string ExpectedDetailFragment(string scenario) =>
        scenario switch
        {
            "head-stale" => "is not the current head",
            "head-unreadable" => "could not be read",
            "team-unresolved" => "has no held claim with a team",
            "missing" => "[cross-runtime-review-missing]",
            "blocked" => "[cross-runtime-review-blocked]",
            "rereview-missing" => "[cross-runtime-review-rereview-missing]",
            "record-unreadable" => "[cross-runtime-review-record-unreadable]",
            _ => string.Empty,
        };

    private void WritePacket(string unit, string? domain)
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            "implementation_issue_packet:\n  issue_title: \"G839\"\n"
            + (domain is null ? string.Empty : $"  domain: {domain}\n")
            + $"  target_repo: {Repo}\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G839\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
    }

    private (int ExitCode, string Output) RunTransition(
        bool write,
        string format,
        string[] extraArgs)
    {
        var args = new List<string>
        {
            "automation", "pr-transition",
            "--repo", Repo,
            "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--transition", "approved",
            "--format", format,
        };
        if (write)
        {
            args.Add("--write");
        }

        args.AddRange(extraArgs);
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args.ToArray(), Context(), writer);
        return (exit, writer.ToString());
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = CliConfigLoader.Load(File.ReadAllText(Path.Combine(root, ".intent-cli", "config.toml"))),
    };

    private void WriteQueue((string Unit, string? LinkedPr) item)
    {
        var queuePath = Path.Combine(root, ".intent-cli", "queue-state.json");
        var linkedPrJson = item.LinkedPr is null ? "null" : $"\"{item.LinkedPr}\"";
        File.WriteAllText(queuePath, $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-09-16T12:00:00Z",
              "items": [
                {
                  "execution_unit": "{{item.Unit}}",
                  "title": "{{item.Unit}} title",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "",
                  "packet_paths": {
                    "yaml": ".intent-cli/issues/{{item.Unit}}/packet.yaml",
                    "implementation": ".intent-cli/issues/{{item.Unit}}/implementation.md",
                    "review_context": ".intent-cli/issues/{{item.Unit}}/review-context.md"
                  },
                  "linked_issue": {
                    "repo": "{{Repo}}",
                    "number": 839,
                    "url": "https://github.com/{{Repo}}/issues/839"
                  },
                  "linked_pr": {{linkedPrJson}},
                  "worker_role": "builder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
    }

    private void RecordVerdict(string runtime, string verdict, string head, int offsetSeconds = 0) =>
        G839CrossRuntimeReviewRecordWriter.WriteImplementationRecord(
            root,
            Repo,
            Pr,
            Unit,
            Domain,
            Team,
            runtime,
            verdict,
            head,
            DateTimeOffset.Parse("2026-09-16T12:00:00Z").AddSeconds(offsetSeconds));

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public List<string> Labels { get; set; } = [];

        public List<(IReadOnlyCollection<string> Add, IReadOnlyCollection<string> Remove)> Applied { get; } = [];

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Applied.Add((addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }
}
