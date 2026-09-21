using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G842: cross-runtime review for copilot and opencode runtimes. Extends G834/G835
/// with model/effort, JSONL envelopes, OpenCode config, and five-runtime gates.
/// </summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G842CrossRuntimeReviewTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G842";
    private const int Pr = 1812;
    private const string H1 = "1111111111111111111111111111111111111111";
    private const string H2 = "2222222222222222222222222222222222222222";
    private const string H3 = "3333333333333333333333333333333333333333";
    private const string CopilotModel = "gpt-5.6-sol";
    private const string OpencodeModel = "github-copilot/gpt-5.6-sol";

    private static readonly string[] CopilotEffortLevels =
        ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

    private static readonly string[] OpencodeDenyPermissionKeys =
    [
        "*", "edit", "bash", "task", "external_directory", "webfetch", "websearch", "skill",
        "todowrite", "question", "lsp", "doom_loop",
    ];

    private readonly string root = Directory.CreateTempSubdirectory("g842-host-").FullName;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };
    private DateTimeOffset clock = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public G842CrossRuntimeReviewTests()
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
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = null;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = null;
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
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = null;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── runtime set ──────────────────────────────────────────────────────

    [Fact]
    public void RuntimeSet_AllListsFiveRuntimes_InOrder_AndDescribeIsExact()
    {
        Assert.Equal(
            ["codex", "claude", "cursor", "copilot", "opencode"],
            CrossRuntimeReviewRuntimes.All);
        Assert.Equal(5, CrossRuntimeReviewRuntimes.All.Count);
        Assert.Equal("codex, claude, cursor, copilot, or opencode", CrossRuntimeReviewRuntimes.Describe());
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void Config_ParsesNewConductorRuntimes(string conductor)
    {
        var config = CliConfigLoader.Load($"""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [[cross_runtime_review.teams]]
            team = "intent-cli/intent-cli-dev"
            conductor_runtime = "{conductor}"
            repos = ["J-Tech-Japan/intent-system"]
            """);

        Assert.True(config.CrossRuntimeReview.TryGetDeclared("intent-cli", "intent-cli-dev", out var declared));
        Assert.Equal(conductor, declared.ConductorRuntime);
    }

    [Fact]
    public void Config_RejectsGeminiConductorRuntime_WithDescribeInMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [[cross_runtime_review.teams]]
            team = "intent-cli/intent-cli-dev"
            conductor_runtime = "gemini"
            repos = ["J-Tech-Japan/intent-system"]
            """));

        Assert.Equal(
            "CLI config value 'cross_runtime_review.teams' entry cross_runtime_review.teams[0] ('intent-cli/intent-cli-dev') field 'conductor_runtime' "
            + "value 'gemini' is not a supported runtime (codex, claude, cursor, copilot, or opencode).",
            exception.Message);
    }

    [Theory]
    [InlineData("copilot", "request")]
    [InlineData("opencode", "request")]
    [InlineData("copilot", "record")]
    [InlineData("opencode", "record")]
    public void Commands_AcceptCopilotAndOpencodeRuntimes(string runtime, string subcommand)
    {
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        var args = subcommand switch
        {
            "request" => RequestArgs(runtime, Path.Combine(root, "clone"), Path.Combine(root, "out-" + runtime), model),
            "record" => RecordArgs(
                runtime,
                WriteVerdictFile(runtime, runtime == "copilot" ? CopilotImplementationEnvelope("approve", H1) : OpencodeImplementationEnvelope("approve", H1)),
                H1,
                write: false),
            _ => StatusArgs(H1),
        };

        Assert.Equal(0, Route(["review", "cross-runtime", .. args, "--format", "json"]).ExitCode);
    }

    [Fact]
    public void Status_AcceptsDeclaredTeam_ForCopilotAndOpencodeRecords()
    {
        RecordVerdict("copilot", "approve", H1);
        Assert.Equal(0, Route(["review", "cross-runtime", .. StatusArgs(H1), "--format", "json"]).ExitCode);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("record")]
    public void Commands_RefuseGeminiRuntime_WithRuntimeInvalidAndDescribe(string subcommand)
    {
        var args = subcommand switch
        {
            "request" => RequestArgs("gemini", Path.Combine(root, "clone"), Path.Combine(root, "out-gemini")),
            "record" => RecordArgs("gemini", WriteVerdictFile("codex", Verdict("approve", H1)), H1, write: false),
            _ => StatusArgs(H1),
        };

        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.RuntimeInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "--runtime 'gemini' is not supported (codex, claude, cursor, copilot, or opencode).",
            refusal.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            "pass --runtime codex, claude, cursor, copilot, or opencode.",
            refusal.RootElement.GetProperty("fix").GetString());
    }

    [Fact]
    public void Store_PreG842CopilotRecord_StillReads()
    {
        var directory = CrossRuntimeReviewPaths.PrDirectory(root, Repo, Pr);
        Directory.CreateDirectory(directory);
        var recordedAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var rawPath = Path.Combine(directory, "20260913T1200000000000Z-copilot-aaaaaaa.raw-verdict");
        File.WriteAllText(rawPath, CopilotImplementationEnvelope("approve", H1));
        var recordJson = $$"""
            {
              "artifact_kind": "cross-runtime-review-record",
              "repo": "{{Repo}}",
              "pr": {{Pr}},
              "head_sha": "{{H1}}",
              "execution_unit": "{{Unit}}",
              "domain": "{{Domain}}",
              "team": "{{Team}}",
              "kind": "implementation",
              "runtime": "copilot",
              "runtime_version": "2.1.269",
              "conductor_runtime": "claude",
              "relation": "cross-runtime",
              "model": "{{CopilotModel}}",
              "verdict": "approve",
              "blocking_findings": [],
              "notes": ["legacy"],
              "recorded_at": "2026-09-13T12:00:00+00:00",
              "raw_verdict_file": ".intent-cli/cross-runtime-reviews/j-tech-japan__intent-system/pr-1812/20260913T1200000000000Z-copilot-aaaaaaa.raw-verdict",
              "raw_verdict_sha256": "{{CrossRuntimeReviewStore.Sha256Hex(File.ReadAllBytes(rawPath))}}"
            }
            """;
        File.WriteAllText(Path.Combine(directory, "20260913T1200000000000Z-copilot-aaaaaaa.json"), recordJson);

        var read = CrossRuntimeReviewStore.Read(root, Repo, Pr);
        var stored = Assert.Single(read.Records);
        Assert.Equal("copilot", stored.Record.Runtime);
        Assert.Equal(CopilotModel, stored.Record.Model);
        Assert.Equal(H1, stored.Record.HeadSha);
    }

    // ── G842CrossRuntimeRuntimeTests ───────────────────────────────────

    [Theory]
    [InlineData("codex", "codex", "same-runtime")]
    [InlineData("codex", "claude", "cross-runtime")]
    [InlineData("codex", "cursor", "cross-runtime")]
    [InlineData("codex", "copilot", "cross-runtime")]
    [InlineData("codex", "opencode", "cross-runtime")]
    [InlineData("claude", "codex", "cross-runtime")]
    [InlineData("claude", "claude", "same-runtime")]
    [InlineData("claude", "cursor", "cross-runtime")]
    [InlineData("claude", "copilot", "cross-runtime")]
    [InlineData("claude", "opencode", "cross-runtime")]
    [InlineData("cursor", "codex", "cross-runtime")]
    [InlineData("cursor", "claude", "cross-runtime")]
    [InlineData("cursor", "cursor", "same-runtime")]
    [InlineData("cursor", "copilot", "cross-runtime")]
    [InlineData("cursor", "opencode", "cross-runtime")]
    [InlineData("copilot", "codex", "cross-runtime")]
    [InlineData("copilot", "claude", "cross-runtime")]
    [InlineData("copilot", "cursor", "cross-runtime")]
    [InlineData("copilot", "copilot", "same-runtime")]
    [InlineData("copilot", "opencode", "cross-runtime")]
    [InlineData("opencode", "codex", "cross-runtime")]
    [InlineData("opencode", "claude", "cross-runtime")]
    [InlineData("opencode", "cursor", "cross-runtime")]
    [InlineData("opencode", "copilot", "cross-runtime")]
    [InlineData("opencode", "opencode", "same-runtime")]
    public void RelationFor_MatchesSectionSixTable(string conductor, string reviewer, string expected)
    {
        Assert.Equal(expected, CrossRuntimeReviewRecord.RelationFor(reviewer, conductor));
    }

    [Fact]
    [RequiresCrossRuntimeApprove]
    public void Gate_CopilotConductor_SatisfiedWithCopilotAndCrossRuntimeApprove()
    {
        RecordVerdict("copilot", "approve", H1, conductor: "copilot");
        RecordVerdict("cursor", "approve", H1, conductor: "copilot");
        var status = Status(H1, Context(conductor: "copilot")).Status;
        Assert.Equal("satisfied", status.GetProperty("gate").GetProperty("decision").GetString());
    }

    [Fact]
    [RequiresCrossRuntimeApprove]
    public void Gate_OpencodeReviewerWithGithubCopilotModel_IsCrossRuntimeForCopilotConductor()
    {
        RecordVerdict("opencode", "approve", H1, conductor: "copilot", model: OpencodeModel);
        var read = CrossRuntimeReviewStore.Read(root, Repo, Pr);
        Assert.Equal(
            CrossRuntimeReviewRecord.RelationCrossRuntime,
            Assert.Single(read.Records).Record.Relation);
    }

    // ── model / effort ─────────────────────────────────────────────────

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void ImplementationRequest_AndRecord_RefuseMissingModel(string runtime)
    {
        var outDir = Path.Combine(root, "missing-model-" + runtime);
        var (requestExit, requestOutput) = Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone"), outDir), "--format", "json"]);
        Assert.Equal(1, requestExit);
        using (var requestRefusal = JsonDocument.Parse(requestOutput))
        {
            Assert.Equal(CrossRuntimeReviewCauses.ModelRequired, requestRefusal.RootElement.GetProperty("cause").GetString());
            Assert.Equal($"--model is required for runtime '{runtime}'.", requestRefusal.RootElement.GetProperty("detail").GetString());
        }

        var verdict = WriteVerdictFile(runtime, runtime == "copilot" ? CopilotImplementationEnvelope("approve", H1) : OpencodeImplementationEnvelope("approve", H1));
        var (recordExit, recordOutput) = Route(["review", "cross-runtime", .. RecordArgs(runtime, verdict, H1, write: false, includeModel: false), "--format", "json"]);
        Assert.Equal(1, recordExit);
        using var recordRefusal = JsonDocument.Parse(recordOutput);
        Assert.Equal(CrossRuntimeReviewCauses.ModelRequired, recordRefusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal($"--model is required for runtime '{runtime}'.", recordRefusal.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void DesignRequest_AndRecord_RefuseMissingModel(string runtime)
    {
        var outDir = Path.Combine(root, "design-missing-" + runtime);
        var (requestExit, requestOutput) = Route(["review", "cross-runtime", .. DesignRequestArgs(runtime, outDir), "--format", "json"]);
        Assert.Equal(1, requestExit);
        Assert.Equal(CrossRuntimeReviewCauses.ModelRequired, JsonDocument.Parse(requestOutput).RootElement.GetProperty("cause").GetString());

        var digest = CurrentDigest();
        var verdict = WriteVerdictFile(runtime, runtime == "copilot" ? CopilotDesignEnvelope("approve", digest) : OpencodeDesignEnvelope("approve", digest));
        var (recordExit, recordOutput) = Route(["review", "cross-runtime", .. DesignRecordArgs(runtime, verdict, digest, write: false, includeModel: false), "--format", "json"]);
        Assert.Equal(1, recordExit);
        Assert.Equal(CrossRuntimeReviewCauses.ModelRequired, JsonDocument.Parse(recordOutput).RootElement.GetProperty("cause").GetString());
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("Auto")]
    [InlineData("AUTO")]
    public void Request_RefusesCopilotAutoModel(string model)
    {
        var outDir = Path.Combine(root, "auto-model");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), outDir, model), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ModelInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "copilot refuses model 'auto' because the model that reviews would be unknown until the run ends.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("gpt-5.6-sol")]
    [InlineData("github-copilot/")]
    [InlineData("/gpt-5.6-sol")]
    [InlineData("_bad/gpt-5.6-sol")]
    public void Request_RefusesInvalidOpencodeModels(string model)
    {
        var outDir = Path.Combine(root, "bad-opencode-model");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, model), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ModelInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            model.StartsWith('_') ? "opencode provider id must match ^[A-Za-z0-9][A-Za-z0-9._-]*$." : "opencode model must be a provider id, then '/', then a non-empty remainder.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("github-copilot/gpt-5.6-sol")]
    [InlineData("omlx070/Qwen3.8-Flash-Next-oQ4e-mtp")]
    public void Request_AcceptsValidOpencodeModelsVerbatim(string model)
    {
        var outDir = Path.Combine(root, "valid-opencode-" + model.Replace('/', '-'));
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, model), "--format", "json"]).ExitCode);
    }

    [Theory]
    [MemberData(nameof(CopilotEffortLevelCases))]
    public void Request_AcceptsEveryCopilotEffortLevel(string effort)
    {
        var outDir = Path.Combine(root, "effort-" + effort);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), outDir, CopilotModel, effort), "--format", "json"]).ExitCode);
    }

    [Theory]
    [InlineData("codex", "high")]
    [InlineData("claude", "high")]
    [InlineData("cursor", "high")]
    [InlineData("copilot", "turbo")]
    public void Request_RefusesInvalidEffort(string runtime, string effort)
    {
        var outDir = Path.Combine(root, "bad-effort-" + runtime);
        var args = runtime is "copilot" or "opencode"
            ? RequestArgs(runtime, Path.Combine(root, "clone"), outDir, runtime == "copilot" ? CopilotModel : OpencodeModel, effort)
            : RequestArgs(runtime, Path.Combine(root, "clone"), outDir);
        if (runtime is not ("copilot" or "opencode"))
        {
            args = [.. args, "--effort", effort];
        }

        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(
            runtime == "copilot" ? CrossRuntimeReviewCauses.EffortInvalid : CrossRuntimeReviewCauses.ArgumentInvalid,
            refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            runtime == "copilot"
                ? "copilot --effort must be one of: none, minimal, low, medium, high, xhigh, max."
                : "--effort is accepted only for runtimes copilot and opencode.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void Request_AndRecord_RefuseExplicitlyEmptyEffort(string runtime)
    {
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        var requestArgs = RequestArgs(runtime, Path.Combine(root, "empty-effort-clone"), Path.Combine(root, "empty-effort-" + runtime), model, string.Empty);
        var (requestExit, requestOutput) = Route(["review", "cross-runtime", .. requestArgs, "--format", "json"]);
        Assert.Equal(1, requestExit);
        using (var requestRefusal = JsonDocument.Parse(requestOutput))
        {
            Assert.Equal(CrossRuntimeReviewCauses.EffortInvalid, requestRefusal.RootElement.GetProperty("cause").GetString());
            Assert.Contains("empty", requestRefusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        var verdict = WriteVerdictFile(runtime, runtime == "copilot"
            ? CopilotImplementationEnvelope("approve", H1)
            : OpencodeImplementationEnvelope("approve", H1));
        var recordArgs = RecordArgs(runtime, verdict, H1, write: false, model: model, effort: string.Empty);
        var (recordExit, recordOutput) = Route(["review", "cross-runtime", .. recordArgs, "--format", "json"]);
        Assert.Equal(1, recordExit);
        using var recordRefusal = JsonDocument.Parse(recordOutput);
        Assert.Equal(CrossRuntimeReviewCauses.EffortInvalid, recordRefusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains("empty", recordRefusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Request_OpencodeProviderConfig_IsAcceptedOnlyForOpencode()
    {
        var provider = Path.Combine(root, "provider.json");
        File.Copy(Fixture("opencode-provider-omlx070.input.json"), provider);
        var copilotOut = Path.Combine(root, "provider-copilot");
        var (copilotExit, copilotOutput) = Route([
            "review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), copilotOut, CopilotModel),
            "--opencode-provider-config", provider, "--format", "json",
        ]);
        Assert.Equal(1, copilotExit);
        using (var refusal = JsonDocument.Parse(copilotOutput))
        {
            Assert.Equal(CrossRuntimeReviewCauses.ArgumentInvalid, refusal.RootElement.GetProperty("cause").GetString());
            Assert.Equal("--opencode-provider-config is accepted only for runtime opencode.", refusal.RootElement.GetProperty("detail").GetString());
        }

        var opencodeOut = Path.Combine(root, "provider-opencode");
        Assert.Equal(0, Route([
            "review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), opencodeOut, OpencodeModel),
            "--opencode-provider-config", provider, "--format", "json",
        ]).ExitCode);
    }

    [Theory]
    [InlineData("not-json", "is not valid JSON")]
    [InlineData("{\"extra\":1,\"provider\":{}}", "must have exactly one top-level key 'provider'")]
    [InlineData("{\"provider\":\"x\"}", "key 'provider' must be a JSON object")]
    public void Request_RefusesInvalidOpencodeProviderConfig(string body, string expectedFragment)
    {
        var path = Path.Combine(root, "bad-provider.json");
        File.WriteAllText(path, body);
        var outDir = Path.Combine(root, "bad-provider-out");
        var (exit, output) = Route([
            "review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, OpencodeModel),
            "--opencode-provider-config", path, "--format", "json",
        ]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.OpencodeProviderConfigInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains(expectedFragment, refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("copilot", "it's", "medium")]
    [InlineData("opencode", "it's", "fast")]
    public void Request_ModelAndEffort_ShellMetacharacters_AreQuoted(string runtime, string value, string effortKind)
    {
        var model = runtime == "copilot" ? value : $"omlx070/{value}-tail";
        var effort = effortKind;
        var outDir = Path.Combine(root, "meta-" + runtime);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone"), outDir, model, effort), "--format", "json"]).ExitCode);
        var command = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(model), command, StringComparison.Ordinal);
        Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(effort), command, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> CopilotEffortLevelCases() =>
        CopilotEffortLevels.Select(level => new object[] { level });

    // ── invocations ────────────────────────────────────────────────────

    [Theory]
    [InlineData("copilot", "implementation", null)]
    [InlineData("copilot", "implementation", "medium")]
    [InlineData("copilot", "design", null)]
    [InlineData("copilot", "design", "high")]
    [InlineData("opencode", "implementation", null)]
    [InlineData("opencode", "implementation", "fast")]
    [InlineData("opencode", "design", null)]
    [InlineData("opencode", "design", "fast")]
    public void Request_RenderInvocation_MatchesPinnedText(string runtime, string kind, string? effort)
    {
        var outDir = Path.Combine(root, "inv-" + runtime + "-" + kind + "-" + (effort ?? "none"));
        var designWithoutClone = kind == "design" && effort is null;
        var workspace = designWithoutClone && (runtime is "copilot" or "opencode")
            ? Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace)
            : designWithoutClone ? outDir : Path.Combine(root, "clone-" + kind);
        if (!designWithoutClone)
        {
            Directory.CreateDirectory(workspace);
        }

        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        var args = kind == "design"
            ? DesignRequestArgs(runtime, outDir, model, effort, designWithoutClone ? null : workspace)
            : RequestArgs(runtime, workspace, outDir, model, effort);
        Assert.Equal(0, Route(["review", "cross-runtime", .. args, "--format", "json"]).ExitCode);

        var expected = CrossRuntimeReviewRuntimes.InvocationLabel(runtime) + "\n"
            + G842PinnedContractTexts.ExpectedReviewerInvocation(runtime, workspace, outDir, model, effort) + "\n";
        Assert.Equal(expected, File.ReadAllText(Path.Combine(outDir, "invocation.txt")));

        var command = expected.Split('\n')[1];
        var invocationBody = command;
        if (runtime == "opencode")
        {
            var exitQuoted = CrossRuntimeReviewPaths.ShellQuote(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeExit));
            var rmPrefix = $"rm -f {exitQuoted};";
            var printfSuffix = $"; printf '%s\\n' \"$?\" > {exitQuoted}";
            Assert.StartsWith(rmPrefix, command, StringComparison.Ordinal);
            Assert.EndsWith(printfSuffix, command, StringComparison.Ordinal);
            invocationBody = command[rmPrefix.Length..^printfSuffix.Length];
        }

        foreach (var assignment in CrossRuntimeReviewRuntimes.EnvAssignments[runtime])
        {
            Assert.Contains(assignment, invocationBody, StringComparison.Ordinal);
        }

        var previousIndex = -1;
        foreach (var assignment in CrossRuntimeReviewRuntimes.EnvAssignments[runtime])
        {
            var index = invocationBody.IndexOf(assignment, StringComparison.Ordinal);
            Assert.True(index > previousIndex, assignment);
            previousIndex = index;
        }

        CrossRuntimeReviewInvocationTestHelpers.AssertNoDenyFlags(runtime, invocationBody);
        var tokens = CrossRuntimeReviewInvocationTestHelpers.ShellTokens(invocationBody);
        foreach (var token in tokens.Where(token => token.StartsWith('-')))
        {
            Assert.Contains(token, CrossRuntimeReviewRuntimes.AllowedFlags[runtime]);
        }

        if (runtime == "copilot")
        {
            Assert.Equal("view", tokens[tokens.IndexOf("--available-tools") + 1]);
            Assert.Equal("rg", tokens[tokens.IndexOf("--available-tools") + 2]);
            Assert.Equal("glob", tokens[tokens.IndexOf("--available-tools") + 3]);
            Assert.Equal("off", tokens[tokens.IndexOf("--stream") + 1]);
        }
        else
        {
            Assert.Equal("run", tokens[tokens.IndexOf("run")]);
            Assert.Equal("intent-cli-reviewer", tokens[tokens.IndexOf("--agent") + 1]);
            Assert.Equal("json", tokens[tokens.IndexOf("--format") + 1]);
        }
    }

    [Fact]
    public void DenyList_RejectsAllowAllToken_ButAllowsAllowAllTools()
    {
        var command = G842PinnedContractTexts.ExpectedReviewerInvocation(
            "copilot",
            Path.Combine(root, "clone"),
            Path.Combine(root, "out"),
            CopilotModel,
            null);
        CrossRuntimeReviewInvocationTestHelpers.AssertNoDenyFlags("copilot", command);
        var withDeniedToken = command.Replace("--allow-all-tools", "--allow-all", StringComparison.Ordinal);
        var exception = Assert.ThrowsAny<Exception>(() => CrossRuntimeReviewInvocationTestHelpers.AssertNoDenyFlags("copilot", withDeniedToken));
        Assert.Contains("--allow-all", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CopilotInstructionCanary_OnlyThePinnedFlagSuppressesWorkspaceInstructions()
    {
        var withFlag = File.ReadAllText(Fixture("copilot-instruction-canary-with-flag.jsonl"));
        var withoutFlag = File.ReadAllText(Fixture("copilot-instruction-canary-without-flag.jsonl"));
        Assert.DoesNotContain("CANARY-AGENTS-7Q4Z", withFlag, StringComparison.Ordinal);
        Assert.DoesNotContain("CANARY-GHINSTR-5W2X", withFlag, StringComparison.Ordinal);
        Assert.Contains("CANARY-AGENTS-7Q4Z", withoutFlag, StringComparison.Ordinal);
        Assert.Contains("CANARY-GHINSTR-5W2X", withoutFlag, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_InvocationsAndConfig_MatchPinnedContractTexts()
    {
        var builder = GuideSoloConductorCommand.BuildBuilder();
        Assert.Equal(G842PinnedContractTexts.CodexBuilderCommand, builder.Invocations[0].Command);
        Assert.Equal(G842PinnedContractTexts.ClaudeBuilderCommand, builder.Invocations[1].Command);
        Assert.Equal(G842PinnedContractTexts.CursorBuilderCommand, builder.Invocations[2].Command);
        Assert.Equal(G842PinnedContractTexts.CopilotBuilderCommand, builder.Invocations[3].Command);
        Assert.Equal(G842PinnedContractTexts.OpencodeBuilderCommand, builder.Invocations[4].Command);
        Assert.Equal(G842PinnedContractTexts.CodexBuilderEnforcement, builder.Invocations[0].Enforcement);
        Assert.Equal(G842PinnedContractTexts.ClaudeBuilderEnforcement, builder.Invocations[1].Enforcement);
        Assert.Equal(G842PinnedContractTexts.CursorBuilderEnforcement, builder.Invocations[2].Enforcement);
        Assert.Equal(G842PinnedContractTexts.CopilotBuilderEnforcement, builder.Invocations[3].Enforcement);
        Assert.Equal(G842PinnedContractTexts.OpencodeBuilderEnforcement, builder.Invocations[4].Enforcement);
        Assert.Equal(G842PinnedContractTexts.OpencodeBuilderConfigJson, builder.OpencodeConfig);
    }

    // ── OpenCode config ────────────────────────────────────────────────

    [Fact]
    public void OpenCodeConfig_DefaultReviewerBytes_MatchMeasuredFixture()
    {
        var outDir = Path.Combine(root, "config-default");
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, OpencodeModel), "--format", "json"]).ExitCode);
        Assert.Equal(
            File.ReadAllBytes(Fixture("opencode-reviewer.measured.json")),
            File.ReadAllBytes(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig)));
    }

    [Fact]
    public void OpenCodeConfig_WithProviderBytes_MatchRenderedFixture()
    {
        var provider = Fixture("opencode-provider-omlx070.input.json");
        var outDir = Path.Combine(root, "config-provider");
        Assert.Equal(0, Route([
            "review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, OpencodeModel),
            "--opencode-provider-config", provider, "--format", "json",
        ]).ExitCode);
        Assert.Equal(
            File.ReadAllBytes(Fixture("opencode-reviewer-with-provider.rendered.json")),
            File.ReadAllBytes(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig)));
    }

    [Fact]
    public void OpenCodeConfig_ParsedReviewerConfig_DeniesExpectedPermissionKeys()
    {
        using var document = JsonDocument.Parse(CrossRuntimeReviewOpencodeConfig.ReviewerConfigJson);
        var permission = document.RootElement.GetProperty("permission");
        foreach (var key in OpencodeDenyPermissionKeys)
        {
            Assert.Equal("deny", permission.GetProperty(key).GetString());
        }

        foreach (var key in new[] { "read", "glob", "grep", "list" })
        {
            Assert.Equal("allow", permission.GetProperty(key).GetString());
        }
    }

    [Theory]
    [InlineData("copilot", 3, 2)]
    [InlineData("opencode", 4, 2)]
    public void Request_WritesExpectedFileAndDirectoryCounts(string runtime, int files, int directories)
    {
        var outDir = Path.Combine(root, "rendered-" + runtime);
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone"), outDir, model), "--format", "json"]).ExitCode);
        Assert.Equal(files, Directory.EnumerateFiles(outDir).Count());
        Assert.Equal(directories, Directory.EnumerateDirectories(outDir).Count());
        Assert.Equal(CrossRuntimeReviewFiles.RenderedFor(runtime).OrderBy(name => name, StringComparer.Ordinal), Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void Request_RenderedModes_ArePrivateOnlyForNewRuntimes(string runtime)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outDir = Path.Combine(root, "modes-" + runtime);
        var model = runtime == "copilot" ? CopilotModel : runtime == "opencode" ? OpencodeModel : null;
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone-" + runtime), outDir, model), "--format", "json"]).ExitCode);

        var expectedFileMode = runtime is "copilot" or "opencode" ? UnixFileMode.UserRead | UnixFileMode.UserWrite : (UnixFileMode)0x1A4;
        foreach (var file in Directory.EnumerateFiles(outDir))
        {
            Assert.Equal(expectedFileMode, File.GetUnixFileMode(file) & (UnixFileMode)0x1FF);
        }

        if (runtime is "copilot" or "opencode")
        {
            foreach (var directory in Directory.EnumerateDirectories(outDir))
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory) & (UnixFileMode)0x1FF);
            }
        }
    }

    [Fact]
    public void Request_RefusesOutDirWithForeignEntry_AndNonEmptyIsolationDirectory()
    {
        var outDir = Path.Combine(root, "foreign");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "stale.txt"), "x");
        var (foreignExit, foreignOutput) = Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), outDir, CopilotModel), "--format", "json"]);
        Assert.Equal(1, foreignExit);
        Assert.Equal(CrossRuntimeReviewCauses.OutDirNotEmpty, JsonDocument.Parse(foreignOutput).RootElement.GetProperty("cause").GetString());

        var isolated = Path.Combine(root, "isolated");
        Directory.CreateDirectory(isolated);
        Directory.CreateDirectory(Path.Combine(isolated, CrossRuntimeReviewFiles.CopilotHome));
        File.WriteAllText(Path.Combine(isolated, CrossRuntimeReviewFiles.CopilotHome, "x"), "y");
        var (isolatedExit, isolatedOutput) = Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), isolated, CopilotModel), "--format", "json"]);
        Assert.Equal(1, isolatedExit);
        Assert.Equal(CrossRuntimeReviewCauses.OutDirNotEmpty, JsonDocument.Parse(isolatedOutput).RootElement.GetProperty("cause").GetString());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_LegacyOutDirRefusal_IsByteIdenticalToMergeBase(string runtime)
    {
        var outDir = Path.Combine(root, "legacy-foreign-" + runtime);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace));
        var args = RequestArgs(runtime, Path.Combine(root, "clone-" + runtime), outDir);

        var (jsonExit, jsonOutput) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(1, jsonExit);
        var expectedJson = $$"""
        {
          "command": "review cross-runtime request",
          "outcome": "refused",
          "cause": "{{CrossRuntimeReviewCauses.OutDirNotEmpty}}",
          "detail": "--out-dir \u0027{{outDir}}\u0027 contains other entries: workspace.",
          "fix": "pass a new or empty directory so a stale verdict can never be mixed with this request."
        }
        """ + Environment.NewLine;
        Assert.Equal(expectedJson, jsonOutput);

        var (markdownExit, markdownOutput) = Route(["review", "cross-runtime", .. args, "--format", "markdown"]);
        Assert.Equal(1, markdownExit);
        var expectedMarkdown =
            $"review cross-runtime request: refused ({CrossRuntimeReviewCauses.OutDirNotEmpty})\n"
            + $"- detail: --out-dir '{outDir}' contains other entries: workspace.\n"
            + "- fix: pass a new or empty directory so a stale verdict can never be mixed with this request.\n";
        Assert.Equal(expectedMarkdown, markdownOutput);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_LegacyRuntimes_FollowMergeBaseThroughRenderedSymlink(string runtime)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outDir = Path.Combine(root, "legacy-symlink-" + runtime);
        Directory.CreateDirectory(outDir);
        var target = Path.Combine(root, "legacy-symlink-target-" + runtime + ".md");
        File.WriteAllText(target, "merge-base-target");
        var rendered = Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt);
        File.CreateSymbolicLink(rendered, target);

        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone-" + runtime), outDir), "--format", "json"]);

        Assert.True(exit == 0, output);
        Assert.Contains("\"outcome\": \"rendered\"", output, StringComparison.Ordinal);
        Assert.Equal(target, new FileInfo(rendered).LinkTarget);
        Assert.NotEqual("merge-base-target", File.ReadAllText(target));
    }

    [Fact]
    public void Request_ReRenderToSameOutDir_IsRefusedWhenForeignEntryPresent()
    {
        var outDir = Path.Combine(root, "rerender");
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, OpencodeModel), "--format", "json"]).ExitCode);
        File.WriteAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.RawVerdict), "{}");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, OpencodeModel), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.OutDirNotEmpty, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void Request_HomeAccessGuard_RefusesProtectedPacketPath()
    {
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = path =>
            path.EndsWith("github-body.md", StringComparison.Ordinal);
        var outDir = Path.Combine(root, "guard");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), outDir, CopilotModel), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ArgumentInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            $"cross-runtime review must not read operator path '{Path.Combine(root, ".intent-cli", "issues", Unit, "github-body.md")}'.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_LegacyRuntimes_KeepMergeBasePacketReadBehavior(string runtime)
    {
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = path =>
            path.EndsWith("github-body.md", StringComparison.Ordinal);
        try
        {
            var (exit, output) = Route([
                "review", "cross-runtime",
                .. RequestArgs(runtime, Path.Combine(root, "clone-" + runtime), Path.Combine(root, "legacy-" + runtime)),
                "--format", "json",
            ]);

            Assert.True(exit == 0, output);
            Assert.Contains("\"outcome\": \"rendered\"", output, StringComparison.Ordinal);
        }
        finally
        {
            CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = null;
        }
    }

    [Fact]
    public void Request_RefusesProtectedOutDir_WithoutListingOperatorEntries()
    {
        var fakeHome = Path.Combine(root, "fakehome");
        Directory.CreateDirectory(fakeHome);
        var copilotHome = Path.Combine(fakeHome, ".copilot");
        Directory.CreateDirectory(copilotHome);
        File.WriteAllText(Path.Combine(copilotHome, "config.json"), "secret");

        var previousHome = Environment.GetEnvironmentVariable("HOME");
        var previousCopilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        Environment.SetEnvironmentVariable("HOME", fakeHome);
        Environment.SetEnvironmentVariable("COPILOT_HOME", null);
        try
        {
            var (exit, output) = Route([
                "review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), copilotHome, CopilotModel),
                "--format", "json",
            ]);
            Assert.Equal(1, exit);
            using var refusal = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
            Assert.Contains("overlaps protected home", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("config.json", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Environment.SetEnvironmentVariable("COPILOT_HOME", previousCopilotHome);
        }
    }

    [Fact]
    public void Request_RefusesMissingCopilotHomeOutDir_WithJsonRefusal()
    {
        var copilotHome = Path.Combine(root, "missing-copilot-home");
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");
        Environment.SetEnvironmentVariable("COPILOT_HOME", copilotHome);
        try
        {
            var (exit, output) = Route([
                "review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), copilotHome, CopilotModel),
                "--format", "json",
            ]);
            Assert.Equal(1, exit);
            using var refusal = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
            Assert.Contains("overlaps protected home", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
        }
    }

    // ── copilot envelopes ──────────────────────────────────────────────

    [Fact]
    public void CopilotEnvelope_ApproveAndRequestChanges_ParseWithObservedModel()
    {
        foreach (var (fixture, verdict, digest) in new[]
                 {
                     ("copilot-approve.jsonl", "approve", "0000000000000000000000000000000000000000000000000000000000000000"),
                     ("copilot-request-changes.jsonl", "request-changes", "1111111111111111111111111111111111111111111111111111111111111111"),
                 })
        {
            var content = CopilotDesignEnvelope(verdict, digest);
            Assert.True(CrossRuntimeReviewVerdict.TryParseDesign("copilot", content, out var parsed, out var error), error);
            Assert.True(CrossRuntimeReviewJsonlVerdict.TryReadCopilotObservedModel(content, out var observedModel, out _), observedModel);
            Assert.Equal(CopilotModel, observedModel);
            Assert.Equal(verdict, parsed.Verdict);
        }
    }

    [Fact]
    public void CopilotEnvelope_NoResult_Refuses()
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParse("copilot", File.ReadAllText(Fixture("copilot-no-result.jsonl")), out _, out _, out var error));
        Assert.Equal("verdict-invalid: exactly one 'result' event must be present and be the last event.", error);
    }

    [Theory]
    [InlineData("trailing-text", "verdict-invalid: copilot final_answer content does not end with a verdict JSON object.")]
    [InlineData("model-mismatch", null)]
    [InlineData("effort-mismatch", null)]
    [InlineData("effort-mixed-checkpoint", null)]
    [InlineData("effort-mismatch-no-checkpoint", null)]
    [InlineData("effort-mismatch-no-reasoning-effort", null)]
    [InlineData("result-removed", "verdict-invalid: exactly one 'result' event must be present and be the last event.")]
    [InlineData("result-not-last", "verdict-invalid: the last event must be the only 'result' event.")]
    [InlineData("exit-code-1", "verdict-invalid: the 'result' event must have exitCode 0.")]
    [InlineData("duplicate-final-answer", "verdict-invalid: exactly one 'assistant.message' event must have data.phase == \"final_answer\".")]
    [InlineData("tool-requests", "verdict-invalid: the final_answer event must not have non-empty data.toolRequests.")]
    [InlineData("non-json-line", "verdict-invalid: line 1 is not valid JSON:")]
    [InlineData("non-object-line", "verdict-invalid: line 1 must be a JSON object with a string 'type'.")]
    [InlineData("empty", "verdict-invalid: file is empty.")]
    [InlineData("bare-object", "copilot requires the runtime JSON envelope from the pinned invocation; a bare verdict object is refused.")]
    [InlineData("missing-model", null)]
    [InlineData("non-array-tool-requests", "verdict-invalid: the final_answer event data.toolRequests must be absent or an empty array.")]
    public void CopilotEnvelope_Mutations_RefuseWithExactMessages(string mutation, string? expectedPrefix)
    {
        string content;
        if (mutation == "missing-model")
        {
            var lines = CopilotDesignEnvelope("approve", CurrentDigest()).Split('\n').ToList();
            RemoveFinalAnswerModel(lines);
            content = string.Join('\n', lines);
        }
        else
        {
            content = mutation is "model-mismatch" or "effort-mismatch" or "effort-mixed-checkpoint" or "effort-mismatch-no-checkpoint" or "effort-mismatch-no-reasoning-effort"
                ? CopilotRecordMutationEnvelope(mutation)
                : MutateCopilotEnvelope("copilot-request-changes.jsonl", mutation);
        }

        if (mutation is "model-mismatch" or "effort-mismatch" or "effort-mixed-checkpoint" or "effort-mismatch-no-checkpoint" or "effort-mismatch-no-reasoning-effort" or "missing-model")
        {
            var file = WriteVerdictFile("copilot", content);
            var args = mutation == "missing-model"
                ? DesignRecordArgs("copilot", file, CurrentDigest(), write: false)
                : RecordArgs(
                    "copilot",
                    file,
                    H1,
                    write: false,
                    model: mutation == "model-mismatch" ? "other-model" : CopilotModel,
                    effort: mutation == "effort-mismatch" ? "medium" : "high");
            var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
            Assert.Equal(1, exit);
            using var refusal = JsonDocument.Parse(output);
            Assert.Equal(
                mutation is "model-mismatch" or "missing-model"
                    ? CrossRuntimeReviewCauses.ModelMismatch
                    : CrossRuntimeReviewCauses.EffortMismatch,
                refusal.RootElement.GetProperty("cause").GetString());
            Assert.Equal(
                mutation switch
                {
                    "model-mismatch" => $"copilot envelope data.model is '{CopilotModel}' but --model is 'other-model'.",
                    "missing-model" => $"copilot envelope data.model is missing but --model is '{CopilotModel}'.",
                    "effort-mismatch" => "observed reasoning_effort does not match --effort 'medium'.",
                    _ => "observed reasoning_effort does not match --effort 'high'.",
                },
                refusal.RootElement.GetProperty("detail").GetString());
            return;
        }

        Assert.False(CrossRuntimeReviewVerdict.TryParse("copilot", content, out _, out _, out var error));
        Assert.StartsWith(expectedPrefix!, error, StringComparison.Ordinal);
    }

    [Fact]
    public void CopilotEnvelope_NonFinalAnswerPhase_IsIgnored()
    {
        var lines = File.ReadAllLines(Fixture("copilot-request-changes.jsonl")).ToList();
        var finalIndex = FindFinalAnswerIndex(lines);
        var approveVerdict = DesignVerdict("approve", "1111111111111111111111111111111111111111111111111111111111111111");
        var decoy = JsonSerializer.Serialize(new
        {
            type = "assistant.message",
            data = new { content = approveVerdict, model = CopilotModel, toolRequests = Array.Empty<object>() },
        });
        lines.Insert(finalIndex, decoy);
        lines.Insert(finalIndex, decoy);
        lines.Insert(finalIndex + 3, decoy);
        Assert.True(CrossRuntimeReviewVerdict.TryParseDesign("copilot", string.Join('\n', lines), out var verdict, out _), verdict.Verdict);
        Assert.Equal("request-changes", verdict.Verdict);
    }

    [Fact]
    public void CopilotEnvelope_FinalAnswerPhaseRemoved_Refuses()
    {
        var lines = File.ReadAllLines(Fixture("copilot-request-changes.jsonl")).ToList();
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"]!.AsObject().Remove("phase");
        lines[index] = node.ToJsonString();
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("copilot", string.Join('\n', lines), out _, out var error));
        Assert.Equal("verdict-invalid: exactly one 'assistant.message' event must have data.phase == \"final_answer\".", error);
    }

    [Fact]
    public void CopilotEnvelope_ReasoningDelta_IsIgnored()
    {
        var lines = File.ReadAllLines(Fixture("copilot-request-changes.jsonl")).ToList();
        var reasoningIndex = lines.FindIndex(line => line.Contains("assistant.reasoning", StringComparison.Ordinal));
        Assert.True(reasoningIndex >= 0);
        var node = JsonNode.Parse(lines[reasoningIndex])!.AsObject();
        node["type"] = "assistant.reasoning_delta";
        lines.Insert(reasoningIndex + 1, node.ToJsonString());
        Assert.True(CrossRuntimeReviewVerdict.TryParseDesign("copilot", string.Join('\n', lines), out var verdict, out var error), error);
        Assert.Equal("request-changes", verdict.Verdict);
    }

    [Fact]
    public void CopilotEnvelope_ImplementationKind_ParsesHeadShaRewrite()
    {
        var content = CopilotImplementationEnvelope("approve", H1);
        Assert.True(CrossRuntimeReviewVerdict.TryParse("copilot", content, out var verdict, out _, out _));
        Assert.Equal(H1, verdict.HeadSha);
        var file = WriteVerdictFile("copilot", content);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RecordArgs("copilot", file, H1, write: false), "--format", "json"]).ExitCode);
    }

    public static IEnumerable<object[]> MalformedEnvelopeCases() =>
    [
        ["copilot", "exit-code-fraction", "verdict-invalid: the 'result' event must have exitCode 0."],
        ["copilot", "exit-code-overflow", "verdict-invalid: the 'result' event must have exitCode 0."],
        ["copilot", "final-answer-data-string", "verdict-invalid: line 10 assistant.message event must have an object data."],
        ["copilot", "null-line", "verdict-invalid: line 1 must be a JSON object with a string 'type'."],
        ["copilot", "string-line", "verdict-invalid: line 2 must be a JSON object with a string 'type'."],
        ["copilot", "checkpoint-models-array", null!],
        ["copilot", "checkpoint-conversation-number", null!],
        ["copilot", "checkpoint-data-string", null!],
        ["copilot", "checkpoint-prompt-cache-number", null!],
        ["copilot", "checkpoint-state-number", null!],
        ["copilot", "checkpoint-model-state-number", null!],
        ["opencode", "error-no-error-object", "verdict-invalid: line 1 error event must have an object 'error' with string 'name' and object 'data' with string 'message'."],
        ["opencode", "error-string", "verdict-invalid: line 1 error event must have an object 'error' with string 'name' and object 'data' with string 'message'."],
        ["opencode", "error-numeric-name", "verdict-invalid: line 1 error event must have a string error.name."],
        ["opencode", "error-no-message", "verdict-invalid: line 1 error event must have object error.data with string message."],
        ["opencode", "number-line", "verdict-invalid: line 3 must be a JSON object with a string 'type'."],
        ["opencode", "array-line", "verdict-invalid: line 1 must be a JSON object with a string 'type'."],
        ["opencode", "error-data-string", "verdict-invalid: line 1 error event must have object error.data with string message."],
        ["opencode", "error-message-number", "verdict-invalid: line 1 error event must have object error.data with string message."],
        ["opencode", "step-finish-part-string", "verdict-invalid: line 8 step_finish event must have an object 'part' with string 'reason'."],
        ["opencode", "step-finish-reason-number", "verdict-invalid: line 8 step_finish event must have an object 'part' with string 'reason'."],
        ["opencode", "text-part-string", "verdict-invalid: line 7 text event must have an object 'part' with string 'text'."],
        ["opencode", "text-part-text-number", "verdict-invalid: line 7 text event must have an object 'part' with string 'text'."],
    ];

    [Theory]
    [MemberData(nameof(MalformedEnvelopeCases))]
    public void MalformedEnvelope_RefusesWithLineNumber(string runtime, string mutation, string? expectedPrefix)
    {
        var content = MutateMalformedEnvelope(runtime, mutation);
        if (IsCopilotEffortMismatchMutation(mutation))
        {
            var file = WriteVerdictFile(runtime, content);
            var (exit, output) = Route([
                "review", "cross-runtime", .. DesignRecordArgs(runtime, file, CurrentDigest(), write: false),
                "--effort", "high", "--format", "json",
            ]);
            Assert.Equal(1, exit);
            using var refusal = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.EffortMismatch, refusal.RootElement.GetProperty("cause").GetString());
            return;
        }

        var parsed = runtime == "copilot"
            ? CrossRuntimeReviewVerdict.TryParseDesign(runtime, content, out _, out var error)
            : CrossRuntimeReviewVerdict.TryParseDesign(runtime, content, out _, out error);
        Assert.False(parsed);
        Assert.StartsWith(expectedPrefix!, error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedEnvelopeCases))]
    public void Record_MalformedEnvelope_NeverExits134(string runtime, string mutation, string? _)
    {
        var content = MutateMalformedEnvelope(runtime, mutation);
        var file = WriteVerdictFile(runtime, content);
        var args = runtime == "copilot"
            ? DesignRecordArgs(runtime, file, CurrentDigest(), write: false, effort: "high")
            : DesignRecordArgs(runtime, file, CurrentDigest(), write: false);
        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.NotEqual(134, exit);
        Assert.Equal(1, exit);
        Assert.DoesNotContain("Unhandled exception", output, StringComparison.Ordinal);
        using var refusal = JsonDocument.Parse(output);
        var cause = refusal.RootElement.GetProperty("cause").GetString();
        if (IsCopilotEffortMismatchMutation(mutation))
        {
            Assert.Equal(CrossRuntimeReviewCauses.EffortMismatch, cause);
        }
        else
        {
            Assert.Equal(CrossRuntimeReviewCauses.VerdictInvalid, cause);
        }
    }

    // ── OpenCode envelopes ─────────────────────────────────────────────

    [Theory]
    [InlineData("opencode-approve.jsonl")]
    [InlineData("opencode-approve-narrated.jsonl")]
    [InlineData("opencode-request-changes.jsonl")]
    public void OpencodeEnvelope_Fixtures_Parse(string fixture)
    {
        Assert.True(CrossRuntimeReviewVerdict.TryParseDesign("opencode", File.ReadAllText(Fixture(fixture)), out var verdict, out var error), error);
        Assert.Contains(verdict.Verdict, CrossRuntimeReviewVerdict.VerdictValues);
    }

    [Theory]
    [InlineData("opencode-error-401.jsonl", "verdict-invalid: T1 refused error event 'APIError' on line 1: Unauthorized: unauthorized: AuthenticateToken authentication failed")]
    [InlineData("opencode-error-unknown-model.jsonl", "verdict-invalid: T1 refused error event 'UnknownError' on line 1: Unexpected server error. Check server logs for details.")]
    [InlineData("opencode-error-local-unreachable.jsonl", "verdict-invalid: T1 refused error event 'APIError' on line 1: Cannot connect to API: Unable to connect. Is the computer able to access the url?")]
    public void OpencodeEnvelope_ErrorFixtures_RefuseWithExactMessages(string fixture, string expected)
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", File.ReadAllText(Fixture(fixture)), out _, out var error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("T1-inserted", "verdict-invalid: T1 refused error event 'APIError' on line 8: boom")]
    [InlineData("T1-appended", "verdict-invalid: T1 refused error event 'APIError' on line 9: boom")]
    [InlineData("T3-text", "verdict-invalid: T3 refused event 'text' on line 9 in state terminated.")]
    [InlineData("T3-tool_use", "verdict-invalid: T3 refused event 'tool_use' on line 9 in state terminated.")]
    [InlineData("T3-step_start", "verdict-invalid: T3 refused event 'step_start' on line 9 in state terminated.")]
    [InlineData("T3-step_finish", "verdict-invalid: T3 refused event 'step_finish' on line 9 in state terminated.")]
    [InlineData("T3-unknown", "verdict-invalid: T3 refused event 'assistant.message' on line 9 in state terminated.")]
    [InlineData("T3-second-step", "verdict-invalid: T3 refused event 'step_start' on line 9 in state terminated.")]
    [InlineData("T3-appended-capture", "verdict-invalid: T3 refused event 'step_start' on line 9 in state terminated.")]
    [InlineData("T6-text-before-start", "verdict-invalid: T6 refused event 'text' on line 1 in state idle.")]
    [InlineData("T6-tool_use-before-start", "verdict-invalid: T6 refused event 'tool_use' on line 1 in state idle.")]
    [InlineData("T6-duplicate-non-stop-finish", "verdict-invalid: T6 refused event 'step_finish' on line 6 in state idle.")]
    [InlineData("T6-no-step_start", "verdict-invalid: T6 refused event 'tool_use' on line 1 in state idle.")]
    [InlineData("T7", "verdict-invalid: T7 refused event 'step_start' on line 7 in state open.")]
    [InlineData("T13-not-terminated", "verdict-invalid: T13 refused because the file did not end in state terminated.")]
    [InlineData("T13-only-first-step_start", "verdict-invalid: T13 refused because the file did not end in state terminated.")]
    [InlineData("T13-length-reason", "verdict-invalid: T13 refused because the file did not end in state terminated.")]
    [InlineData("T13-empty-text", "verdict-invalid: T13 refused because the final step text is empty.")]
    [InlineData("malformed-step_finish", "verdict-invalid: line 3 step_finish event must have an object 'part' with string 'reason'.")]
    [InlineData("malformed-text", "verdict-invalid: line 2 text event must have an object 'part' with string 'text'.")]
    [InlineData("non-object-line", "verdict-invalid: line 1 must be a JSON object with a string 'type'.")]
    public void OpencodeEnvelope_StateMachineMutations_RefuseWithRowLabels(string row, string expected)
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", MutateOpencodeEnvelope(row), out _, out var error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("trailing-text", "verdict-invalid: opencode final step text does not end with a verdict JSON object.")]
    [InlineData("bare-object", "opencode requires the runtime JSON envelope from the pinned invocation; a bare verdict object is refused.")]
    public void OpencodeEnvelope_VerdictTextMutations_Refuse(string mutation, string expected)
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", MutateOpencodeEnvelope(mutation), out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void OpencodeEnvelope_ImplementationKind_ParsesHeadShaRewrite()
    {
        var content = OpencodeImplementationEnvelope("approve", H1);
        Assert.True(CrossRuntimeReviewVerdict.TryParse("opencode", content, out var verdict, out _));
        Assert.Equal(H1, verdict.HeadSha);
    }

    // ── prompts ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void Request_Prompt_EmbedsPacketBytes_NotHostPaths(string runtime)
    {
        var outDir = Path.Combine(root, "prompt-" + runtime);
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone"), outDir, model), "--format", "json"]).ExitCode);
        var prompt = File.ReadAllText(Path.Combine(outDir, "prompt.md"));
        Assert.Contains("### github-body.md", prompt, StringComparison.Ordinal);
        Assert.Contains("```github-body.md", prompt, StringComparison.Ordinal);
        Assert.Contains("# G842", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(root, ".intent-cli"), prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_Prompt_ListsHostPathsForLegacyRuntimes(string runtime)
    {
        var outDir = Path.Combine(root, "prompt-legacy-" + runtime);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone"), outDir), "--format", "json"]).ExitCode);
        var prompt = File.ReadAllText(Path.Combine(outDir, "prompt.md"));
        var quotedBody = CrossRuntimeReviewPaths.ShellQuote(Path.Combine(root, ".intent-cli", "issues", Unit, "github-body.md"));
        Assert.Contains($"- Issue contract (packet github-body.md): {quotedBody}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("### github-body.md", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_RefusesNonUtf8PacketBytes()
    {
        var bodyPath = Path.Combine(root, ".intent-cli", "issues", Unit, "github-body.md");
        File.WriteAllBytes(bodyPath, [0xFF, 0xFE, 0x00]);
        var outDir = Path.Combine(root, "bad-packet");
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), outDir, CopilotModel), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.PacketInvalid, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
        WritePacket(Unit, Domain);
    }

    [Fact]
    public void CopilotEnvelope_FencedVerdict_IsAccepted()
    {
        var content = MutateCopilotEnvelope("copilot-request-changes.jsonl", "fence");
        Assert.True(CrossRuntimeReviewVerdict.TryParseDesign("copilot", content, out var verdict, out var error), error);
        Assert.Equal("request-changes", verdict.Verdict);
    }

    [Fact]
    public void OpencodeEnvelope_FencedVerdict_IsAccepted()
    {
        var content = MutateOpencodeEnvelope("fence");
        Assert.True(CrossRuntimeReviewVerdict.TryParseDesign("opencode", content, out var verdict, out var error), error);
        Assert.Equal("approve", verdict.Verdict);
    }

    [Fact]
    public void OpencodeEnvelope_ImplementationCompactionCapture_ParsesRequestChanges()
    {
        Assert.True(CrossRuntimeReviewVerdict.TryParse("opencode", File.ReadAllText(Fixture("opencode-implementation-big-pickle-live-compaction.jsonl")), out var verdict, out var error), error);
        Assert.Equal("request-changes", verdict.Verdict);
    }

    [Fact]
    public void OpencodeEnvelope_DesignCompactionRetryCapture_ReachesValidationAfterCompaction()
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", File.ReadAllText(Fixture("opencode-design-big-pickle-live-compaction-retry.jsonl")), out _, out var error));
        Assert.Contains("notes", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OpencodeEnvelope_DesignCompactionCapture_RefusesInvalidFenceJson()
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", File.ReadAllText(Fixture("opencode-design-big-pickle-live-compaction.jsonl")), out _, out var error));
        Assert.Contains("invalid JSON in fenced block", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OpencodeEnvelope_CompactionTimeout_RefusesT13()
    {
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", File.ReadAllText(Fixture("opencode-design-local-live-compaction-timeout.jsonl")), out _, out var error));
        Assert.Equal("verdict-invalid: T13 refused because the file did not end in state terminated.", error);
    }

    [Fact]
    public void Request_RefusesExistingOpencodeExitFile()
    {
        var outDir = Path.Combine(root, "exit-foreign");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeExit), "0\n"u8.ToArray());
        var (exit, output) = Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), outDir, OpencodeModel), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.OutDirNotEmpty, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    [Theory]
    [InlineData("1\n", CrossRuntimeReviewCauses.ExitStatusNonzero)]
    [InlineData("142\n", CrossRuntimeReviewCauses.ExitStatusNonzero)]
    [InlineData("0", CrossRuntimeReviewCauses.ExitStatusNonzero)]
    [InlineData("", CrossRuntimeReviewCauses.ExitStatusNonzero)]
    public void Record_OpencodeExitStatus_RefusesNonZeroBytes(string bytes, string cause)
    {
        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit), Encoding.UTF8.GetBytes(bytes));
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("opencode", file, H1, write: false), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Equal(cause, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void Record_OpencodeExitStatusMissing_Refuses()
    {
        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        File.Delete(Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit));
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("opencode", file, H1, write: false), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.ExitStatusMissing, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void Record_OpencodeExitStatus_IsCheckedBeforeInvalidUtf8Verdict()
    {
        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        File.WriteAllBytes(file, [0xff]);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit), "1\n"u8.ToArray());

        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("opencode", file, H1, write: false), "--format", "json"]);

        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ExitStatusNonzero, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains("exactly 0\\n is required", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex", "implementation")]
    [InlineData("claude", "implementation")]
    [InlineData("cursor", "implementation")]
    [InlineData("copilot", "implementation")]
    [InlineData("opencode", "implementation")]
    [InlineData("codex", "design")]
    [InlineData("claude", "design")]
    [InlineData("cursor", "design")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "design")]
    public void Record_VerdictSymlink_PreservesLegacyReadAndRestrictsNewRuntimes(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var digest = CurrentDigest();
        var content = kind == CrossRuntimeReviewRecord.KindDesign
            ? runtime switch
            {
                "copilot" => CopilotDesignEnvelope("approve", digest),
                "opencode" => OpencodeDesignEnvelope("approve", digest),
                "claude" => ClaudeEnvelope(DesignVerdict("approve", digest)),
                "cursor" => CursorEnvelope(DesignVerdict("approve", digest)),
                _ => DesignVerdict("approve", digest),
            }
            : runtime switch
            {
                "copilot" => CopilotImplementationEnvelope("approve", H1),
                "opencode" => OpencodeImplementationEnvelope("approve", H1),
                "claude" => ClaudeEnvelope(Verdict("approve", H1)),
                "cursor" => CursorEnvelope(Verdict("approve", H1)),
                _ => Verdict("approve", H1),
            };
        var target = WriteVerdictFile(runtime, content);
        var link = Path.Combine(Path.GetDirectoryName(target)!, $"{runtime}-{kind}-{Guid.NewGuid():N}.jsonl");
        File.CreateSymbolicLink(link, target);

        var args = kind == CrossRuntimeReviewRecord.KindDesign
            ? DesignRecordArgs(runtime, link, digest, write: false, includeModel: runtime is "copilot" or "opencode")
            : RecordArgs(runtime, link, H1, write: false);
        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);

        if (runtime is "codex" or "claude" or "cursor")
        {
            Assert.True(exit == 0, output);
            Assert.Equal("would-record", JsonDocument.Parse(output).RootElement.GetProperty("outcome").GetString());
        }
        else
        {
            Assert.Equal(1, exit);
            using var refusal = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.VerdictInvalid, refusal.RootElement.GetProperty("cause").GetString());
            Assert.Contains("symlink", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    // ── record ─────────────────────────────────────────────────────────

    [Fact]
    public void DesignRecord_Write_StoresEffort_AndCommentBodyNamesEffort()
    {
        var content = CopilotDesignEnvelope("approve", CurrentDigest());
        var file = WriteVerdictFile("copilot", content);
        var commentOut = Path.Combine(root, "comments", "copilot-design.md");
        var (exit, output) = Route([
            "review", "cross-runtime", .. DesignRecordArgs("copilot", file, CurrentDigest(), write: true),
            "--effort", "low", "--comment-out", commentOut, "--format", "json",
        ]);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("low", result.RootElement.GetProperty("record").GetProperty("effort").GetString());
        var body = File.ReadAllText(commentOut);
        Assert.Contains("- effort: low", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignRecord_CopilotModelAndEffortMismatch_Refuse()
    {
        var content = CopilotDesignEnvelope("request-changes", CurrentDigest());
        var file = WriteVerdictFile("copilot", content);
        var (modelExit, modelOutput) = Route([
            "review", "cross-runtime", .. DesignRecordArgs("copilot", file, CurrentDigest(), write: false),
            "--model", "other-model", "--format", "json",
        ]);
        Assert.Equal(1, modelExit);
        Assert.Equal(CrossRuntimeReviewCauses.ModelMismatch, JsonDocument.Parse(modelOutput).RootElement.GetProperty("cause").GetString());

        var (effortExit, effortOutput) = Route([
            "review", "cross-runtime", .. DesignRecordArgs("copilot", file, CurrentDigest(), write: false),
            "--effort", "medium", "--format", "json",
        ]);
        Assert.Equal(1, effortExit);
        Assert.Equal(CrossRuntimeReviewCauses.EffortMismatch, JsonDocument.Parse(effortOutput).RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void Record_Write_StoresModelEffort_RelationFromConductor_AndCommentBody()
    {
        var content = CopilotImplementationEnvelope("request-changes", H1);
        var file = WriteVerdictFile("copilot", content);
        var commentOut = Path.Combine(root, "comments", "copilot.md");
        var (exit, output) = Route([
            "review", "cross-runtime", .. RecordArgs("copilot", file, H1, write: true, effort: "high"),
            "--comment-out", commentOut, "--format", "json",
        ]);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var record = result.RootElement.GetProperty("record");
        Assert.Equal(CopilotModel, record.GetProperty("model").GetString());
        Assert.Equal("high", record.GetProperty("effort").GetString());
        Assert.Equal("cross-runtime", record.GetProperty("relation").GetString());
        Assert.Equal("claude", record.GetProperty("conductor_runtime").GetString());
        var body = File.ReadAllText(commentOut);
        Assert.Contains("- model: gpt-5.6-sol", body, StringComparison.Ordinal);
        Assert.Contains("- effort: high", body, StringComparison.Ordinal);
    }

    // ── no-launch seam ─────────────────────────────────────────────────

    [Fact]
    public void Request_RecordAndStatus_NeverLaunchCopilotOrOpencode_AndSourceHasNoProcessUse()
    {
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("copilot", Path.Combine(root, "clone"), Path.Combine(root, "o-copilot"), CopilotModel)]).ExitCode);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "clone"), Path.Combine(root, "o-opencode"), OpencodeModel)]).ExitCode);
        var copilotVerdict = WriteVerdictFile("copilot", CopilotImplementationEnvelope("approve", H1));
        var opencodeVerdict = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        Assert.Equal(0, Route(["review", "cross-runtime", .. RecordArgs("copilot", copilotVerdict, H1, write: true)]).ExitCode);
        Assert.Equal(0, Route(["review", "cross-runtime", .. RecordArgs("opencode", opencodeVerdict, H1, write: true)]).ExitCode);
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

    [Fact]
    public void Guide_SoloConductor_NeverClaimsLaunch()
    {
        var affirmative = new Regex(@"intent-cli (starts|launches|manages|spawns|runs) (an? |the )?(agent|subagent|reviewer|provider)", RegexOptions.IgnoreCase);
        using var solo = new StringWriter();
        Assert.Equal(0, GuideSoloConductorCommand.Execute(Context(), ["--format", "markdown"], solo));
        Assert.DoesNotMatch(affirmative, solo.ToString());
        Assert.Contains("intent-cli renders these lines and never launches a builder, reviewer or AI provider CLI; the review commands' only launch is the existing claim-read `git fetch`.", GuideSoloConductorCommand.BuildGuide().Builder.Contract[^1], StringComparison.Ordinal);
    }

    // ── docs ───────────────────────────────────────────────────────────

    [Fact]
    public void Docs_EnJa_SectionAndLedgerRows_MentionG842()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var orchestration = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("G842", orchestration, StringComparison.Ordinal);
            Assert.Contains("copilot", orchestration, StringComparison.Ordinal);
            Assert.Contains("opencode", orchestration, StringComparison.Ordinal);
            Assert.Contains(
                "intent-cli review cross-runtime request --kind design --execution-unit <unit> --runtime codex|claude|cursor|copilot|opencode --out-dir <dir> [--clone <read-only-clone>] [--model <name>] [--effort <level>] [--opencode-provider-config <file>]",
                orchestration,
                StringComparison.Ordinal);
            Assert.Contains(
                "intent-cli review cross-runtime record --kind design --execution-unit <unit> --packet-digest <sha256> --runtime <runtime> --runtime-version <text> --verdict-file <file> [--model <name>] [--effort <level>] [--write]",
                orchestration,
                StringComparison.Ordinal);

            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            foreach (var row in new[] { "| `review cross-runtime request` |", "| `review cross-runtime record` |", "| `review cross-runtime status` |", "| cross-runtime review declaration |" })
            {
                var line = Assert.Single(ledger.Split('\n'), candidate => candidate.StartsWith(row, StringComparison.Ordinal));
                Assert.Contains("G842", line, StringComparison.Ordinal);
            }
        }
    }

    // ── helpers ────────────────────────────────────────────────────────

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

    private string[] DesignRequestArgs(string runtime, string outDir, string? model = null, string? effort = null, string? clone = null)
    {
        var args = new List<string> { "request", "--kind", "design", "--execution-unit", Unit, "--runtime", runtime, "--out-dir", outDir };
        if (clone is not null)
        {
            args.AddRange(["--clone", clone]);
        }

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

    private static string[] RecordArgs(string runtime, string verdictFile, string head, bool write, string? model = null, string? effort = null, bool includeModel = true)
    {
        var args = new List<string>
        {
            "record", "--repo", Repo, "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--head-sha", head,
            "--kind", "implementation", "--runtime", runtime, "--runtime-version", "2.1.269",
            "--verdict-file", verdictFile, "--execution-unit", Unit,
        };
        if (includeModel)
        {
            var resolvedModel = model ?? (runtime is "copilot" or "opencode"
                ? runtime == "copilot" ? CopilotModel : OpencodeModel
                : null);
            if (resolvedModel is not null)
            {
                args.AddRange(["--model", resolvedModel]);
            }
        }
        else if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        if (effort is not null)
        {
            args.AddRange(["--effort", effort]);
        }

        if (write)
        {
            args.Add("--write");
        }

        return args.ToArray();
    }

    private string[] DesignRecordArgs(string runtime, string verdictFile, string digest, bool write, bool includeModel = true, string? effort = null)
    {
        var args = new List<string>
        {
            "record", "--kind", "design", "--execution-unit", Unit, "--packet-digest", digest,
            "--runtime", runtime, "--runtime-version", "2.1.269", "--verdict-file", verdictFile,
        };
        if (includeModel)
        {
            args.AddRange(["--model", runtime == "copilot" ? CopilotModel : OpencodeModel]);
        }

        if (effort is not null)
        {
            args.AddRange(["--effort", effort]);
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

    private void RecordVerdict(string runtime, string verdict, string head, string conductor = "claude", string? model = null, string? effort = null)
    {
        var content = runtime switch
        {
            "copilot" => CopilotImplementationEnvelope(verdict, head),
            "opencode" => OpencodeImplementationEnvelope(verdict, head),
            "codex" => Verdict(verdict, head),
            "claude" => ClaudeEnvelope(Verdict(verdict, head)),
            _ => CursorEnvelope(Verdict(verdict, head)),
        };
        var file = WriteVerdictFile(runtime, content);
        var args = new List<string>
        {
            "record", "--repo", Repo, "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture), "--head-sha", head,
            "--kind", "implementation", "--runtime", runtime, "--runtime-version", "2.1.269",
            "--verdict-file", file, "--execution-unit", Unit, "--write",
        };
        if (runtime is "copilot" or "opencode")
        {
            args.AddRange(["--model", model ?? (runtime == "copilot" ? CopilotModel : OpencodeModel)]);
        }

        if (effort is not null)
        {
            args.AddRange(["--effort", effort]);
        }

        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"], Context(conductor: conductor));
        Assert.True(exit == 0, output);
    }

    private string WriteVerdictFile(string runtime, string content)
    {
        var directory = Path.Combine(root, "runs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{runtime}-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, content);
        if (runtime == "opencode")
        {
            File.WriteAllBytes(Path.Combine(directory, CrossRuntimeReviewFiles.OpencodeExit), "0\n"u8.ToArray());
        }

        return path;
    }

    private static string Verdict(string verdict, string head) => verdict == "approve"
        ? JsonSerializer.Serialize(new { verdict, head_sha = head, blocking_findings = Array.Empty<object>(), notes = new[] { "looks right" } })
        : JsonSerializer.Serialize(new { verdict, head_sha = head, blocking_findings = new[] { new { file = "src/A.cs", line = 12, scenario = "blocking" } }, notes = Array.Empty<string>() });

    private static string DesignVerdict(string verdict, string digest) => verdict == "approve"
        ? JsonSerializer.Serialize(new { verdict, packet_digest = digest, blocking_findings = Array.Empty<object>(), notes = new[] { "looks right" } })
        : JsonSerializer.Serialize(new { verdict, packet_digest = digest, blocking_findings = new[] { new { file = "packet.yaml", line = 3, scenario = "blocking" } }, notes = Array.Empty<string>() });

    private string CurrentDigest() => CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDir());

    private string PacketDir() => Path.Combine(root, ".intent-cli", "issues", Unit);

    private static string Fixture(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G842", name);

    private static string CopilotImplementationEnvelope(string verdict, string head)
    {
        return MutateCopilotFinalAnswer(
            Fixture(verdict == "approve" ? "copilot-approve.jsonl" : "copilot-request-changes.jsonl"),
            node =>
            {
                node["verdict"] = verdict;
                node["head_sha"] = head;
                node.Remove("packet_digest");
                if (verdict == "approve")
                {
                    node["blocking_findings"] = new JsonArray();
                    node["notes"] = new JsonArray("looks right");
                }
            });
    }

    private static string CopilotDesignEnvelope(string verdict, string digest)
    {
        return MutateCopilotFinalAnswer(
            Fixture(verdict == "approve" ? "copilot-approve.jsonl" : "copilot-request-changes.jsonl"),
            node =>
            {
                node["verdict"] = verdict;
                node["packet_digest"] = digest;
            });
    }

    private static string MutateCopilotFinalAnswer(string fixturePath, Action<JsonObject> mutate)
    {
        var lines = File.ReadAllLines(fixturePath).ToList();
        var index = FindFinalAnswerIndex(lines);
        var envelope = JsonNode.Parse(lines[index])!.AsObject();
        var content = JsonNode.Parse(envelope["data"]!["content"]!.GetValue<string>())!.AsObject();
        mutate(content);
        envelope["data"]!["content"] = content.ToJsonString();
        lines[index] = envelope.ToJsonString();
        return string.Join('\n', lines);
    }

    private static string OpencodeImplementationEnvelope(string verdict, string head) =>
        RewriteOpencodeText(Fixture("opencode-approve.jsonl"), Verdict(verdict, head));

    private static string OpencodeDesignEnvelope(string verdict, string digest) =>
        RewriteOpencodeText(Fixture("opencode-approve.jsonl"), DesignVerdict(verdict, digest));

    private static void ReplaceFinalAnswerContent(List<string> lines, string content)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"]!["content"] = content;
        lines[index] = node.ToJsonString();
    }

    private static string RewriteOpencodeText(string fixturePath, string verdictJson)
    {
        var lines = File.ReadAllLines(fixturePath).ToList();
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (!lines[index].Contains("\"type\":\"text\"", StringComparison.Ordinal) && !lines[index].Contains("\"type\": \"text\"", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["part"]!["text"] = verdictJson;
            lines[index] = node.ToJsonString();
            break;
        }

        return string.Join('\n', lines);
    }

    private static string CopilotRecordMutationEnvelope(string mutation)
    {
        var lines = CopilotImplementationEnvelope("request-changes", H1).Split('\n').ToList();
        switch (mutation)
        {
            case "model-mismatch":
                SetFinalAnswerModel(lines, CopilotModel);
                break;
            case "effort-mismatch-no-checkpoint":
                lines.RemoveAll(line => line.Contains("session.usage_checkpoint", StringComparison.Ordinal));
                break;
            case "effort-mixed-checkpoint":
                SetFirstMainCheckpointEffort(lines, "low");
                break;
            case "effort-mismatch-no-reasoning-effort":
                RemoveReasoningEffortKey(lines);
                break;
        }

        return string.Join('\n', lines);
    }

    private static string MutateCopilotEnvelope(string fixture, string mutation)
    {
        if (mutation == "bare-object")
        {
            return DesignVerdict("approve", "0000000000000000000000000000000000000000000000000000000000000000");
        }

        if (mutation == "empty")
        {
            return string.Empty;
        }

        if (mutation == "non-json-line")
        {
            return "not-json\n" + File.ReadAllText(Fixture(fixture));
        }

        if (mutation == "non-object-line")
        {
            return "5\n" + File.ReadAllText(Fixture(fixture));
        }

        var lines = File.ReadAllLines(Fixture(fixture)).ToList();
        switch (mutation)
        {
            case "fence":
                ReplaceFinalAnswerContent(lines, "```json\n" + ExtractFinalAnswerContent(lines) + "\n```");
                break;
            case "trailing-text":
                ReplaceFinalAnswerContent(lines, ExtractFinalAnswerContent(lines) + " trailing");
                break;
            case "model-mismatch":
                SetFinalAnswerModel(lines, CopilotModel);
                break;
            case "effort-mismatch":
            case "effort-mismatch-no-checkpoint":
                if (mutation == "effort-mismatch-no-checkpoint")
                {
                    lines.RemoveAll(line => line.Contains("session.usage_checkpoint", StringComparison.Ordinal));
                }

                break;
            case "effort-mixed-checkpoint":
                SetFirstMainCheckpointEffort(lines, "low");
                break;
            case "result-removed":
                lines.RemoveAll(line => line.Contains("\"type\":\"result\"", StringComparison.Ordinal) || line.Contains("\"type\": \"result\"", StringComparison.Ordinal));
                break;
            case "result-not-last":
                lines.Add("""{"type":"assistant.idle","data":{}}""");
                break;
            case "exit-code-1":
                return ReplaceResultExitCode(lines, 1);
            case "duplicate-final-answer":
                lines.Insert(FindFinalAnswerIndex(lines), lines[FindFinalAnswerIndex(lines)]);
                break;
            case "tool-requests":
                InsertToolRequests(lines);
                break;
            case "non-array-tool-requests":
                InsertNonArrayToolRequests(lines);
                break;
        }

        return string.Join('\n', lines);
    }

    private static string MutateOpencodeEnvelope(string mutation)
    {
        if (mutation == "bare-object")
        {
            return DesignVerdict("approve", "0000000000000000000000000000000000000000000000000000000000000000");
        }

        if (mutation == "non-object-line")
        {
            return "5";
        }

        if (mutation == "malformed-step_finish")
        {
            return string.Join('\n', new[]
            {
                """{"type":"step_start","part":{"type":"step-start"}}""",
                """{"type":"text","part":{"text":"{}"}}""",
                """{"type":"step_finish"}""",
            });
        }

        if (mutation == "malformed-text")
        {
            return string.Join('\n', new[]
            {
                """{"type":"step_start","part":{"type":"step-start"}}""",
                """{"type":"text"}""",
                """{"type":"step_finish","part":{"reason":"stop","type":"step-finish"}}""",
            });
        }

        if (mutation == "fence")
        {
            return MinimalOpencodeEnvelope("```json\n" + DesignVerdict("approve", "0000000000000000000000000000000000000000000000000000000000000000") + "\n```");
        }

        if (mutation == "trailing-text")
        {
            return MinimalOpencodeEnvelope(DesignVerdict("approve", "0000000000000000000000000000000000000000000000000000000000000000") + " trailing");
        }

        var approveLines = File.ReadAllLines(Fixture("opencode-approve.jsonl")).ToList();
        var errorLine = """{"type":"error","error":{"name":"APIError","data":{"message":"boom"}}}""";
        return mutation switch
        {
            "T1-inserted" => InsertOpencodeLineBeforeTerminalStop(approveLines, errorLine),
            "T1-appended" => AppendOpencodeAfterTerminalStop(approveLines, errorLine),
            "T3-text" => AppendOpencodeAfterTerminalStop(approveLines, """{"type":"text","part":{"text":"late"}}"""),
            "T3-tool_use" => AppendOpencodeAfterTerminalStop(approveLines, """{"type":"tool_use","part":{"tool":"read"}}"""),
            "T3-step_start" => AppendOpencodeAfterTerminalStop(approveLines, """{"type":"step_start","part":{"type":"step-start"}}"""),
            "T3-step_finish" => AppendOpencodeAfterTerminalStop(approveLines, approveLines[^1]),
            "T3-unknown" => AppendOpencodeAfterTerminalStop(approveLines, """{"type":"assistant.message","data":{}}"""),
            "T3-second-step" => AppendOpencodeAfterTerminalStop(approveLines, File.ReadAllText(Fixture("opencode-request-changes.jsonl"))),
            "T3-appended-capture" => string.Join('\n', approveLines) + '\n' + File.ReadAllText(Fixture("opencode-request-changes.jsonl")),
            "T6-text-before-start" => """{"type":"text","part":{"text":"early"}}""",
            "T6-tool_use-before-start" => """{"type":"tool_use","part":{"tool":"read"}}""",
            "T6-duplicate-non-stop-finish" => DuplicateOpencodeLine(approveLines, FindOpencodeNonStopFinishIndex(approveLines)),
            "T6-no-step_start" => string.Join('\n', approveLines.Where(line => !line.Contains("step_start", StringComparison.Ordinal))),
            "T7" => DuplicateOpencodeLine(approveLines, FindLastOpencodeStepStartIndex(approveLines)),
            "T13-not-terminated" => string.Join('\n', approveLines.Take(approveLines.Count - 1)),
            "T13-only-first-step_start" => approveLines[0],
            "T13-length-reason" => ReplaceOpencodeTerminalReason(approveLines, "length"),
            "T13-empty-text" => RemoveOpencodeTextLines(approveLines),
            _ => MinimalOpencodeEnvelope(DesignVerdict("approve", "0000000000000000000000000000000000000000000000000000000000000000")),
        };
    }

    private static string MinimalOpencodeEnvelope(string text, bool duplicateStepStart = false, string? extraAfterStop = null)
    {
        var lines = new List<string>
        {
            """{"type":"step_start","part":{"type":"step-start"}}""",
        };
        if (duplicateStepStart)
        {
            lines.Add("""{"type":"step_start","part":{"type":"step-start"}}""");
        }

        lines.Add("{\"type\":\"text\",\"part\":{\"text\":" + JsonSerializer.Serialize(text) + "}}");
        lines.Add("""{"type":"step_finish","part":{"reason":"stop","type":"step-finish"}}""");
        if (extraAfterStop is not null)
        {
            lines.Add(extraAfterStop);
        }

        return string.Join('\n', lines);
    }

    private static string ExtractFinalAnswerContent(List<string> lines)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        return node["data"]!["content"]!.GetValue<string>();
    }

    private static void SetFinalAnswerModel(List<string> lines, string model)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"]!["model"] = model;
        lines[index] = node.ToJsonString();
    }

    private static string ReplaceResultExitCode(List<string> lines, JsonNode exitCode)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("\"type\":\"result\"", StringComparison.Ordinal) && !lines[index].Contains("\"type\": \"result\"", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["exitCode"] = exitCode;
            lines[index] = node.ToJsonString();
            break;
        }

        return string.Join('\n', lines);
    }

    private static void InsertToolRequests(List<string> lines)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"]!["toolRequests"] = new JsonArray(new JsonObject { ["name"] = "view" });
        lines[index] = node.ToJsonString();
    }

    private static void InsertNonArrayToolRequests(List<string> lines)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"]!["toolRequests"] = new JsonObject { ["x"] = 1 };
        lines[index] = node.ToJsonString();
    }

    private static void RemoveFinalAnswerModel(List<string> lines)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"]!.AsObject().Remove("model");
        lines[index] = node.ToJsonString();
    }

    private static void RemoveReasoningEffortKey(List<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["data"]!["promptCacheBreakState"]![0]!["models"]![CopilotModel]!.AsObject().Remove("reasoning_effort");
            lines[index] = node.ToJsonString();
            return;
        }
    }

    private static void SetFirstMainCheckpointEffort(List<string> lines, string effort)
    {
        foreach (var index in Enumerable.Range(0, lines.Count))
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var original = lines[index];
            var node = JsonNode.Parse(lines[index])!.AsObject();
            var states = node["data"]!["promptCacheBreakState"]!.AsArray();
            foreach (var state in states)
            {
                if (state?["conversation"]?.GetValue<string>() != "main")
                {
                    continue;
                }

                state["models"]![CopilotModel]!["reasoning_effort"] = effort;
                lines[index] = node.ToJsonString();
                lines.Insert(index + 1, original);
                return;
            }
        }

        throw new InvalidOperationException("main checkpoint not found");
    }

    private static string MutateMalformedEnvelope(string runtime, string mutation)
    {
        if (runtime == "opencode")
        {
            return mutation switch
            {
                "error-no-error-object" => """{"type":"error"}""",
                "error-string" => """{"type":"error","error":"boom"}""",
                "error-numeric-name" => """{"type":"error","error":{"name":7,"data":{"message":"m"}}}""",
                "error-no-message" => """{"type":"error","error":{"name":"X","data":{}}}""",
                "error-data-string" => """{"type":"error","error":{"name":"X","data":"x"}}""",
                "error-message-number" => """{"type":"error","error":{"name":"X","data":{"message":5}}}""",
                "number-line" => MutateOpencodeEnvelopeLine(File.ReadAllLines(Fixture("opencode-approve.jsonl")).ToList(), 2, "5"),
                "array-line" => "[]",
                "step-finish-part-string" => MutateOpencodeEnvelopeLine(File.ReadAllLines(Fixture("opencode-approve.jsonl")).ToList(), 7, """{"type":"step_finish","part":"x"}"""),
                "step-finish-reason-number" => MutateOpencodeEnvelopeLine(File.ReadAllLines(Fixture("opencode-approve.jsonl")).ToList(), 7, """{"type":"step_finish","part":{"reason":5}}"""),
                "text-part-string" => MutateOpencodeEnvelopeLine(File.ReadAllLines(Fixture("opencode-approve.jsonl")).ToList(), 6, """{"type":"text","part":"x"}"""),
                "text-part-text-number" => MutateOpencodeEnvelopeLine(File.ReadAllLines(Fixture("opencode-approve.jsonl")).ToList(), 6, """{"type":"text","part":{"text":5}}"""),
                _ => MutateOpencodeEnvelope(mutation),
            };
        }

        var lines = File.ReadAllLines(Fixture("copilot-request-changes.jsonl")).ToList();
        return mutation switch
        {
            "exit-code-fraction" => ReplaceResultExitCode(lines, 1.5),
            "exit-code-overflow" => ReplaceResultExitCode(lines, 99999999999),
            "final-answer-data-string" => ReplaceFinalAnswerData(lines, "not-an-object"),
            "null-line" => "null",
            "string-line" => ReplaceCopilotLine(lines, 1, "\"hello\""),
            "checkpoint-models-array" => ReplaceCheckpointModels(lines, new JsonArray()),
            "checkpoint-conversation-number" => ReplaceCheckpointConversation(lines, 5),
            "checkpoint-data-string" => ReplaceCheckpointData(lines, "x"),
            "checkpoint-prompt-cache-number" => ReplaceCheckpointPromptCacheBreakState(lines, 5),
            "checkpoint-state-number" => ReplaceCheckpointPromptCacheBreakState(lines, new JsonArray(5)),
            "checkpoint-model-state-number" => ReplaceCheckpointModelState(lines, 5),
            _ => MutateCopilotEnvelope("copilot-request-changes.jsonl", mutation),
        };
    }

    private static string MutateOpencodeEnvelopeLine(List<string> lines, int lineIndex, string replacement)
    {
        lines[lineIndex] = replacement;
        return string.Join('\n', lines);
    }

    private static string ReplaceCopilotLine(List<string> lines, int lineIndex, string replacement)
    {
        lines[lineIndex] = replacement;
        return string.Join('\n', lines);
    }

    private static string ReplaceFinalAnswerData(List<string> lines, JsonNode data)
    {
        var index = FindFinalAnswerIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["data"] = data;
        lines[index] = node.ToJsonString();
        return string.Join('\n', lines);
    }

    private static bool IsCopilotEffortMismatchMutation(string mutation) =>
        mutation is "checkpoint-models-array"
            or "checkpoint-conversation-number"
            or "checkpoint-data-string"
            or "checkpoint-prompt-cache-number"
            or "checkpoint-state-number"
            or "checkpoint-model-state-number";

    private static string ReplaceCheckpointData(List<string> lines, JsonNode data)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["data"] = data;
            lines[index] = node.ToJsonString();
            return string.Join('\n', lines);
        }

        throw new InvalidOperationException("checkpoint not found");
    }

    private static string ReplaceCheckpointPromptCacheBreakState(List<string> lines, JsonNode states)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["data"]!["promptCacheBreakState"] = states;
            lines[index] = node.ToJsonString();
            return string.Join('\n', lines);
        }

        throw new InvalidOperationException("checkpoint not found");
    }

    private static string ReplaceCheckpointModelState(List<string> lines, JsonNode modelState)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["data"]!["promptCacheBreakState"]![0]!["models"]![CopilotModel] = modelState;
            lines[index] = node.ToJsonString();
            return string.Join('\n', lines);
        }

        throw new InvalidOperationException("checkpoint not found");
    }

    private static string ReplaceCheckpointModels(List<string> lines, JsonNode models)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["data"]!["promptCacheBreakState"]![0]!["models"] = models;
            lines[index] = node.ToJsonString();
            return string.Join('\n', lines);
        }

        throw new InvalidOperationException("checkpoint not found");
    }

    private static string ReplaceCheckpointConversation(List<string> lines, int conversation)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("session.usage_checkpoint", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            node["data"]!["promptCacheBreakState"]![0]!["conversation"] = conversation;
            lines[index] = node.ToJsonString();
            return string.Join('\n', lines);
        }

        throw new InvalidOperationException("checkpoint not found");
    }

    private static string InsertOpencodeLineBeforeTerminalStop(List<string> lines, string inserted)
    {
        var stopIndex = FindOpencodeTerminalStopIndex(lines);
        lines.Insert(stopIndex, inserted);
        return string.Join('\n', lines);
    }

    private static string AppendOpencodeAfterTerminalStop(List<string> lines, string appended) =>
        string.Join('\n', lines) + '\n' + appended;

    private static string DuplicateOpencodeLine(List<string> lines, int index)
    {
        lines.Insert(index, lines[index]);
        return string.Join('\n', lines);
    }

    private static int FindOpencodeTerminalStopIndex(List<string> lines)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (lines[index].Contains("\"reason\":\"stop\"", StringComparison.Ordinal) || lines[index].Contains("\"reason\": \"stop\"", StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("terminal stop not found");
    }

    private static int FindOpencodeNonStopFinishIndex(List<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Contains("step_finish", StringComparison.Ordinal)
                && !lines[index].Contains("\"reason\":\"stop\"", StringComparison.Ordinal)
                && !lines[index].Contains("\"reason\": \"stop\"", StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("non-stop finish not found");
    }

    private static int FindLastOpencodeStepStartIndex(List<string> lines)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (lines[index].Contains("step_start", StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("step_start not found");
    }

    private static string ReplaceOpencodeTerminalReason(List<string> lines, string reason)
    {
        var index = FindOpencodeTerminalStopIndex(lines);
        var node = JsonNode.Parse(lines[index])!.AsObject();
        node["part"]!["reason"] = reason;
        lines[index] = node.ToJsonString();
        return string.Join('\n', lines);
    }

    private static string RemoveOpencodeTextLines(List<string> lines) =>
        string.Join('\n', lines.Where(line => !line.Contains("\"type\":\"text\"", StringComparison.Ordinal) && !line.Contains("\"type\": \"text\"", StringComparison.Ordinal)));

    private static int FindFinalAnswerIndex(List<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Contains("final_answer", StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("final_answer not found");
    }

    private static string ClaudeEnvelope(string verdict)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", "claude-envelope.json")))!.AsObject();
        node["structured_output"] = JsonNode.Parse(verdict);
        node["result"] = verdict;
        return node.ToJsonString();
    }

    private static string CursorEnvelope(string resultText)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", "cursor-envelope.json")))!.AsObject();
        node["result"] = resultText;
        return node.ToJsonString();
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

    private void WritePacket(string unit, string? domain)
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            "implementation_issue_packet:\n  issue_title: \"G842\"\n"
            + (domain is null ? string.Empty : $"  domain: {domain}\n")
            + $"  target_repo: {Repo}\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G842\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
    }
}
