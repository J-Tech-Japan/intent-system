using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G835: cross-runtime design review keyed by packet digest, optional
/// <c>--model</c> on request and record, and the design gate shared by
/// <c>review cross-runtime status --kind design</c> and
/// <c>issue publish-flow --write</c> for declared teams.
/// </summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G835CrossRuntimeReviewTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G835";
    private const string PinnedDigest = "24f8ff53d9800ccfa59cf7eb4311394c2f939c5cb13898ab7081c2c878655913";
    private const string D1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string D2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string D3 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private readonly string root = Directory.CreateTempSubdirectory("g835-host-").FullName;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };
    private DateTimeOffset clock = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public G835CrossRuntimeReviewTests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return claims.TryGetValue(unit, out var team)
                ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "design", team ?? string.Empty, "held")
                : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
        };
        ReviewCrossRuntimeCommand.Clock = () => clock = clock.AddMinutes(1);
        ReviewCrossRuntimeCommand.NestedProviderLauncher = () => throw new InvalidOperationException("cross-runtime review must never launch a provider");
        ReviewCrossRuntimeCommand.ProcessRunnerFactory = () => throw new InvalidOperationException("cross-runtime review must never construct a process runner");
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
        WriteClaim(Unit, Team);
        CopyFixturePacket(Unit);
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        ReviewCrossRuntimeCommand.Clock = null;
        ReviewCrossRuntimeCommand.NestedProviderLauncher = null;
        ReviewCrossRuntimeCommand.ProcessRunnerFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── model ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_Model_RendersInsideQuotes_ForBracketOverrideAndShellMetacharacters(string runtime)
    {
        const string model = "claude-opus-4-8[context=1m,effort=high]";
        var outDir = Path.Combine(root, "model-" + runtime);
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRequestArgs(runtime, outDir, model)]).ExitCode);
        var command = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(model), command, StringComparison.Ordinal);

        foreach (var metachar in new[] { "it's", "$HOME", "a;rm -rf x", "`whoami`" })
        {
            var metacharOut = Path.Combine(root, "meta-" + runtime + "-" + metachar.Length);
            Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRequestArgs(runtime, metacharOut, metachar)]).ExitCode);
            var metacharCommand = File.ReadAllText(Path.Combine(metacharOut, "invocation.txt")).Split('\n')[1];
            Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(metachar), metacharCommand, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Request_AndRecord_RefuseEmptyModelString()
    {
        var outDir = Path.Combine(root, "empty-model");
        var (requestExit, requestOutput) = Route(["review", "cross-runtime", .. DesignRequestArgs("codex", outDir), "--model", "", "--format", "json"]);
        Assert.Equal(1, requestExit);
        Assert.Contains(CrossRuntimeReviewCauses.ModelInvalid, requestOutput, StringComparison.Ordinal);

        var verdict = WriteVerdictFile("codex", Verdict("approve", CurrentDigest()));
        var (recordExit, recordOutput) = Route(["review", "cross-runtime", .. DesignRecordArgs("codex", verdict, CurrentDigest(), write: false), "--model", "", "--format", "json"]);
        Assert.Equal(1, recordExit);
        Assert.Contains(CrossRuntimeReviewCauses.ModelInvalid, recordOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void DesignRequest_AndRecord_RefuseMissingModel_ForCopilotAndOpencode(string runtime)
    {
        var outDir = Path.Combine(root, "missing-model-" + runtime);
        var (requestExit, requestOutput) = Route(["review", "cross-runtime", .. DesignRequestArgs(runtime, outDir), "--format", "json"]);
        Assert.Equal(1, requestExit);
        Assert.Contains(CrossRuntimeReviewCauses.ModelRequired, requestOutput, StringComparison.Ordinal);

        var verdict = WriteVerdictFile(runtime, File.ReadAllText(FixtureG842(runtime == "copilot" ? "copilot-approve.jsonl" : "opencode-approve.jsonl")));
        var (recordExit, recordOutput) = Route(["review", "cross-runtime", .. DesignRecordArgs(runtime, verdict, CurrentDigest(), write: false), "--format", "json"]);
        Assert.Equal(1, recordExit);
        Assert.Contains(CrossRuntimeReviewCauses.ModelRequired, recordOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-bad")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    [InlineData("a\u007fb")]
    public void Request_AndRecord_RefuseInvalidModel(string model)
    {
        var outDir = Path.Combine(root, "bad-model");
        var (requestExit, requestOutput) = Route(["review", "cross-runtime", .. DesignRequestArgs("codex", outDir, model), "--format", "json"]);
        Assert.Equal(1, requestExit);
        Assert.Contains(CrossRuntimeReviewCauses.ModelInvalid, requestOutput, StringComparison.Ordinal);

        var verdict = WriteVerdictFile("codex", Verdict("approve", CurrentDigest()));
        var (recordExit, recordOutput) = Route(["review", "cross-runtime", .. DesignRecordArgs("codex", verdict, CurrentDigest(), write: false, model: model), "--format", "json"]);
        Assert.Equal(1, recordExit);
        Assert.Contains(CrossRuntimeReviewCauses.ModelInvalid, recordOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Request_ModelFlag_IsInTheRuntimeAllowList(string runtime)
    {
        var outDir = Path.Combine(root, "flags-" + runtime);
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRequestArgs(runtime, outDir, "composer-2.5")]).ExitCode);
        var command = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        var tokens = ShellTokens(command);
        var allowed = CrossRuntimeReviewRuntimes.AllowedFlags[runtime];
        foreach (var token in tokens.Where(token => token.StartsWith('-')))
        {
            Assert.True(allowed.Contains(token, StringComparer.Ordinal), $"'{token}' is outside the {runtime} allow-list: {command}");
        }

        Assert.Contains(runtime == "codex" ? "-m" : "--model", tokens);
    }

    [Fact]
    public void Record_StoresModelOnlyWhenSet_AndCommentNamesIt()
    {
        var digest = CurrentDigest();
        var withModel = WriteVerdictFile("codex", Verdict("approve", digest));
        var commentOut = Path.Combine(root, "comments", "with-model.md");
        var (exit, output) = Route(["review", "cross-runtime", .. DesignRecordArgs("codex", withModel, digest, write: true, model: "cursor-grok-4.6-xhigh"), "--comment-out", commentOut, "--format", "json"]);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("cursor-grok-4.6-xhigh", result.RootElement.GetProperty("record").GetProperty("model").GetString());
        Assert.Contains("model: cursor-grok-4.6-xhigh", File.ReadAllText(commentOut), StringComparison.Ordinal);

        clock = clock.AddMinutes(1);
        var withoutModel = WriteVerdictFile("cursor", CursorEnvelope(Verdict("approve", digest)));
        var (secondExit, secondOutput) = Route(["review", "cross-runtime", .. DesignRecordArgs("cursor", withoutModel, digest, write: true), "--format", "json"]);
        Assert.Equal(0, secondExit);
        using var second = JsonDocument.Parse(secondOutput);
        Assert.False(second.RootElement.GetProperty("record").TryGetProperty("model", out _));
        Assert.DoesNotContain("model:", second.RootElement.GetProperty("comment_body").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_PreG835DesignRecordWithoutModel_StillReads()
    {
        var digest = CurrentDigest();
        var directory = CrossRuntimeReviewPaths.DesignDirectory(root, Unit);
        Directory.CreateDirectory(directory);
        var recordedAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var rawPath = Path.Combine(directory, "20260913T1200000000000Z-codex-aaaaaaa.raw-verdict");
        File.WriteAllText(rawPath, Verdict("approve", digest));
        var recordJson = $$"""
            {
              "artifact_kind": "cross-runtime-review-record",
              "packet_digest": "{{digest}}",
              "target_repo": "{{Repo}}",
              "execution_unit": "{{Unit}}",
              "domain": "{{Domain}}",
              "team": "{{Team}}",
              "kind": "design",
              "runtime": "codex",
              "runtime_version": "codex-cli 0.154.0",
              "conductor_runtime": "claude",
              "relation": "cross-runtime",
              "verdict": "approve",
              "blocking_findings": [],
              "notes": ["legacy"],
              "recorded_at": "2026-09-13T12:00:00+00:00",
              "raw_verdict_file": ".intent-cli/cross-runtime-reviews/design/{{Unit}}/20260913T1200000000000Z-codex-aaaaaaa.raw-verdict",
              "raw_verdict_sha256": "{{CrossRuntimeReviewStore.Sha256Hex(File.ReadAllBytes(rawPath))}}"
            }
            """;
        File.WriteAllText(Path.Combine(directory, "20260913T1200000000000Z-codex-aaaaaaa.json"), recordJson);

        var read = CrossRuntimeDesignReviewStore.Read(root, Unit);
        Assert.Single(read.Records);
        Assert.Null(read.Records[0].Record.Model);
    }

    [Fact]
    public void ImplementationGate_Decision_DoesNotDependOnModel()
    {
        const string head = "1111111111111111111111111111111111111111";
        var withModel = CrossRuntimeReviewGate.Evaluate(
            Declaration("claude"),
            ImplementationResolution(),
            head,
            ImplementationRead(head, model: "cursor-grok-4.6-xhigh"));
        var withoutModel = CrossRuntimeReviewGate.Evaluate(
            Declaration("claude"),
            ImplementationResolution(),
            head,
            ImplementationRead(head, model: null));

        Assert.Equal(withModel.Decision, withoutModel.Decision);
        Assert.Equal("satisfied", withModel.Decision);
    }

    // ── digest ─────────────────────────────────────────────────────────

    [Fact]
    public void Digest_FixturePacket_YieldsPinnedDigest()
    {
        CopyFixturePacket("G835FIX");
        var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDir("G835FIX"));
        Assert.Equal(File.ReadAllText(Fixture("pinned-digest.txt")).Trim(), digest);
        Assert.Equal(PinnedDigest, digest);
    }

    [Theory]
    [InlineData("packet.yaml")]
    [InlineData("github-body.md")]
    [InlineData("review-context.md")]
    [InlineData("implementation.md")]
    public void Digest_ChangingOneByte_ChangesTheDigest(string fileName)
    {
        var directory = PacketDir(Unit);
        var before = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0x01;
        File.WriteAllBytes(path, bytes);
        var after = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(directory);
        Assert.NotEqual(before, after);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("record")]
    public void Digest_MissingFile_Refuses(string subcommand)
    {
        File.Delete(Path.Combine(PacketDir(Unit), "implementation.md"));
        var args = subcommand switch
        {
            "request" => DesignRequestArgs("codex", Path.Combine(root, "out")),
            _ => DesignRecordArgs("codex", WriteVerdictFile("codex", Verdict("approve", D1)), D1, write: false),
        };
        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Contains(CrossRuntimeReviewCauses.PacketMissing, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_WithMissingPacketFile_ReportsNotRequiredWithoutDigest()
    {
        File.Delete(Path.Combine(PacketDir(Unit), "implementation.md"));
        var (exit, status) = DesignStatus();
        Assert.Equal(0, exit);
        Assert.Equal("not-required", Decision(status));
        Assert.False(status.TryGetProperty("packet_digest", out var digest) && digest.ValueKind != JsonValueKind.Null);
    }

    // ── design request ─────────────────────────────────────────────────

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void DesignRequest_WritesExactlyThreeFiles_EmbedsPacketBytes_AndShowsDigest(string runtime)
    {
        var outDir = Path.Combine(root, "design-out-" + runtime);
        var (exit, output) = Route(["review", "cross-runtime", .. DesignRequestArgs(runtime, outDir), "--format", "json"]);
        Assert.Equal(0, exit);
        Assert.Equal(
            ["invocation.txt", "prompt.md", "verdict.schema.json"],
            Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));

        var digest = CurrentDigest();
        var prompt = File.ReadAllText(Path.Combine(outDir, "prompt.md"));
        foreach (var fileName in CrossRuntimeDesignReviewDigest.PacketFileNames)
        {
            Assert.Contains($"### {fileName}", prompt, StringComparison.Ordinal);
            Assert.Contains("```" + fileName, prompt, StringComparison.Ordinal);
            Assert.Contains(File.ReadAllText(Path.Combine(PacketDir(Unit), fileName)), prompt, StringComparison.Ordinal);
        }

        Assert.Contains(digest, prompt, StringComparison.Ordinal);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(digest, result.RootElement.GetProperty("packet_digest").GetString());
    }

    [Fact]
    public void DesignRequest_EmbedsAPacketFileContainingFences_WithALongerFence()
    {
        var bodyPath = Path.Combine(PacketDir(Unit), "github-body.md");
        File.AppendAllText(bodyPath, "\n```toml\n[[cross_runtime_review.teams]]\n```\n\n````text\nfour\n````\n");
        var outDir = Path.Combine(root, "design-out-fence");
        var (exit, output) = Route(["review", "cross-runtime", .. DesignRequestArgs("cursor", outDir), "--format", "json"]);
        Assert.True(exit == 0, output);

        var prompt = File.ReadAllText(Path.Combine(outDir, "prompt.md"));
        var body = File.ReadAllText(bodyPath);
        Assert.Equal(4, ReviewCrossRuntimeCommand.LongestBacktickRun(body));
        Assert.Contains("`````github-body.md\n" + body + "`````\n", prompt, StringComparison.Ordinal);
        Assert.Contains("```packet.yaml\n", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignRequest_RefusesNonUtf8PacketBytes()
    {
        File.WriteAllBytes(Path.Combine(PacketDir(Unit), "github-body.md"), [0xFF, 0xFE, 0x00]);
        var outDir = Path.Combine(root, "design-out-invalid");
        var (exit, output) = Route(["review", "cross-runtime", .. DesignRequestArgs("cursor", outDir), "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Contains(CrossRuntimeReviewCauses.PacketInvalid, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--repo")]
    [InlineData("--pr")]
    [InlineData("--head-sha")]
    public void DesignRequest_RefusesPrArguments(string flag)
    {
        var args = new List<string> { "request", "--kind", "design", "--execution-unit", Unit, "--runtime", "codex", "--out-dir", Path.Combine(root, "refused"), flag, flag == "--pr" ? "1" : flag == "--head-sha" ? D1 : Repo };
        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(1, exit);
        Assert.Contains(CrossRuntimeReviewCauses.ArgumentInvalid, output, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignRequest_Workspace_IsCloneWhenGiven_OtherwiseOutDir()
    {
        var clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(clone);
        var outDir = Path.Combine(root, "workspace-out");
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRequestArgs("codex", outDir, clone: clone)]).ExitCode);
        var withClone = File.ReadAllText(Path.Combine(outDir, "invocation.txt")).Split('\n')[1];
        Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(clone), withClone, StringComparison.Ordinal);

        var outOnly = Path.Combine(root, "workspace-out-only");
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRequestArgs("cursor", outOnly)]).ExitCode);
        var withoutClone = File.ReadAllText(Path.Combine(outOnly, "invocation.txt")).Split('\n')[1];
        Assert.Contains(CrossRuntimeReviewPaths.ShellQuote(outOnly), withoutClone, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignRequest_RecordAndStatus_NeverLaunchAProviderOrConstructAProcessRunner()
    {
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRequestArgs("claude", Path.Combine(root, "o"))]).ExitCode);
        var verdict = WriteVerdictFile("codex", Verdict("approve", CurrentDigest()));
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignRecordArgs("codex", verdict, CurrentDigest(), write: true)]).ExitCode);
        Assert.Equal(0, Route(["review", "cross-runtime", .. DesignStatusArgs()]).ExitCode);
    }

    // ── design record ──────────────────────────────────────────────────

    [Theory]
    [InlineData("digest-stale")]
    [InlineData("digest-mismatch")]
    [InlineData("undeclared-team")]
    [InlineData("team-unresolved")]
    [InlineData("invalid-verdict")]
    public void DesignRecord_NamedRefusals(string scenario)
    {
        var digest = CurrentDigest();
        var content = scenario == "invalid-verdict" ? "{not json" : Verdict("approve", digest);
        var file = WriteVerdictFile("codex", content);
        var context = scenario == "undeclared-team" ? Context(declare: false) : Context();
        if (scenario == "team-unresolved")
        {
            claims.Remove(Unit);
        }

        var packetDigest = scenario switch
        {
            "digest-stale" => D2,
            "digest-mismatch" => digest,
            _ => digest,
        };
        if (scenario == "digest-mismatch")
        {
            content = Verdict("approve", D2);
            file = WriteVerdictFile("codex", content);
        }

        var (exit, output) = Route(["review", "cross-runtime", .. DesignRecordArgs("codex", file, packetDigest, write: true), "--format", "json"], context);
        Assert.Equal(1, exit);
        var expected = scenario switch
        {
            "digest-stale" => CrossRuntimeReviewCauses.DigestStale,
            "digest-mismatch" => CrossRuntimeReviewCauses.DigestMismatch,
            "undeclared-team" => CrossRuntimeReviewCauses.NotDeclared,
            "team-unresolved" => CrossRuntimeReviewCauses.TeamUnresolved,
            _ => CrossRuntimeReviewCauses.VerdictInvalid,
        };
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(expected, refusal.RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void DesignRecord_ReturnDigestA_ApprovedByBoth_ThenB_ThenBackToA_IsAllowed_AndCreateNew()
    {
        var digestA = CurrentDigest();
        RecordDesign("claude", "approve", digestA);
        RecordDesign("cursor", "approve", digestA);

        AppendPacketByte();
        var digestB = CurrentDigest();
        RecordDesign("codex", "request-changes", digestB);

        RestoreFixturePacket();
        var digestAReturn = CurrentDigest();
        Assert.Equal(digestA, digestAReturn);

        var file = WriteVerdictFile("codex", Verdict("approve", digestAReturn));
        var (exit, output) = Route(["review", "cross-runtime", .. DesignRecordArgs("codex", file, digestAReturn, write: true), "--format", "json"]);
        Assert.Equal(0, exit);

        var directory = CrossRuntimeReviewPaths.DesignDirectory(root, Unit);
        Assert.True(Directory.EnumerateFiles(directory, "*.json").Count() >= 4);

        var firstClaudeRecordedAt = CrossRuntimeDesignReviewStore.Read(root, Unit)
            .Records.First(record => record.Record.Runtime == "claude")
            .Record.RecordedAt;
        var previousClock = ReviewCrossRuntimeCommand.Clock;
        ReviewCrossRuntimeCommand.Clock = () => firstClaudeRecordedAt;
        try
        {
            var sameFile = WriteVerdictFile("claude", ClaudeEnvelope(Verdict("request-changes", digestAReturn)));
            var (collisionExit, collisionOutput) = Route(["review", "cross-runtime", .. DesignRecordArgs("claude", sameFile, digestAReturn, write: true), "--format", "json"]);
            Assert.Equal(1, collisionExit);
            Assert.Contains(CrossRuntimeReviewCauses.RecordCollision, collisionOutput, StringComparison.Ordinal);
        }
        finally
        {
            ReviewCrossRuntimeCommand.Clock = previousClock;
        }
    }

    [Fact]
    public void DesignRecord_RelationComesFromDeclaredConductor()
    {
        var digest = CurrentDigest();
        RecordDesign("claude", "approve", digest);
        var read = CrossRuntimeDesignReviewStore.Read(root, Unit);
        Assert.Equal("same-runtime", Assert.Single(read.Records).Record.Relation);
    }

    // ── design gate ────────────────────────────────────────────────────

    [Fact]
    public void DesignGate_NotRequired_ForAnUndeclaredTeam()
    {
        var (exit, status) = DesignStatus(Context(declare: false));
        Assert.Equal(0, exit);
        Assert.False(status.GetProperty("declared").GetBoolean());
        Assert.Equal("not-required", Decision(status));
    }

    [Fact]
    [RequiresCrossRuntimeApprove]
    public void DesignGate_MissingEachRelation_StaleDigestNotCounted_SatisfiedOnlyWithBothApproves()
    {
        var digest = CurrentDigest();
        Assert.Equal(
            ["cross-runtime-review-missing:same-runtime", "cross-runtime-review-missing:cross-runtime"],
            Reasons(DesignStatus().Status));

        RecordDesign("claude", "approve", digest);
        AppendPacketByte();
        var stale = DesignStatus().Status;
        Assert.Equal("missing", Decision(stale));
        Assert.All(stale.GetProperty("gate").GetProperty("records").EnumerateArray(), entry => Assert.Equal("stale-digest", entry.GetProperty("status").GetString()));

        RestoreFixturePacket();
        digest = CurrentDigest();
        Assert.Equal(["cross-runtime-review-missing:cross-runtime"], Reasons(DesignStatus().Status));

        RecordDesign("cursor", "approve", digest);
        var satisfied = DesignStatus().Status;
        Assert.Equal("satisfied", Decision(satisfied));
        Assert.Empty(Reasons(satisfied));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void DesignGate_BlockedByTheLatestRecordOfEitherRelation(string blocker)
    {
        var digest = CurrentDigest();
        RecordDesign("claude", blocker == "claude" ? "request-changes" : "approve", digest);
        RecordDesign("codex", blocker == "codex" ? "request-changes" : "approve", digest);
        var status = DesignStatus().Status;
        Assert.Equal("blocked", Decision(status));
        var reason = status.GetProperty("gate").GetProperty("reasons").EnumerateArray().Single(item => item.GetProperty("cause").GetString() == CrossRuntimeReviewCauses.Blocked);
        Assert.Equal([blocker], reason.GetProperty("runtimes").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void DesignGate_SameRuntimeRequestChanges_SupersededByLaterApprove_OnSameDigest()
    {
        var digest = CurrentDigest();
        RecordDesign("claude", "request-changes", digest);
        RecordDesign("cursor", "approve", digest);
        Assert.Equal("blocked", Decision(DesignStatus().Status));

        RecordDesign("claude", "approve", digest);
        Assert.Equal("satisfied", Decision(DesignStatus().Status));
    }

    [Fact]
    public void DesignGate_RereviewMissing_IncludingD1BlockD2ApproveD3Satisfied()
    {
        WritePacketDigestVariant("variant-a");
        var d1 = CurrentDigest();
        RecordDesign("claude", "approve", d1);
        RecordDesign("codex", "request-changes", d1);

        WritePacketDigestVariant("variant-b");
        var d2 = CurrentDigest();
        RecordDesign("claude", "approve", d2);
        RecordDesign("cursor", "approve", d2);
        var h2 = DesignStatus().Status;
        Assert.Equal("missing", Decision(h2));
        Assert.Equal(CrossRuntimeReviewCauses.RereviewMissing, h2.GetProperty("gate").GetProperty("reasons").EnumerateArray().Single().GetProperty("cause").GetString());

        RecordDesign("codex", "approve", d2);
        Assert.Equal("satisfied", Decision(DesignStatus().Status));

        WritePacketDigestVariant("variant-c");
        var d3 = CurrentDigest();
        RecordDesign("claude", "approve", d3);
        RecordDesign("cursor", "approve", d3);
        Assert.Equal("satisfied", Decision(DesignStatus().Status));
    }

    [Fact]
    public void DesignGate_Epoch_AApprovedByBothThenBBlockedThenBackToA_OldApprovalsAreStaleEpoch()
    {
        WritePacketDigestVariant("epoch-a");
        var digestA = CurrentDigest();
        RecordDesign("claude", "approve", digestA);
        RecordDesign("cursor", "approve", digestA);
        Assert.Equal("satisfied", Decision(DesignStatus().Status));

        WritePacketDigestVariant("epoch-b");
        var digestB = CurrentDigest();
        RecordDesign("codex", "request-changes", digestB);

        WritePacketDigestVariant("epoch-a");
        var digestAReturn = CurrentDigest();
        Assert.Equal(digestA, digestAReturn);
        var status = DesignStatus().Status;
        Assert.Equal("missing", Decision(status));
        Assert.Contains(status.GetProperty("gate").GetProperty("records").EnumerateArray(), entry => entry.GetProperty("status").GetString() == CrossRuntimeReviewGate.StatusStaleEpoch);
    }

    [Fact]
    public void DesignGate_ForeignRecords_NeverCounted()
    {
        var digest = CurrentDigest();
        RecordDesign("claude", "approve", digest);
        RecordDesign("cursor", "approve", digest);
        var read = CrossRuntimeDesignReviewStore.Read(root, Unit);
        var foreignResolution = DesignResolution() with { Team = "other-team" };
        var gate = CrossRuntimeReviewGate.EvaluateDesign(Declaration("claude"), foreignResolution, digest, read);
        Assert.Equal("missing", gate.Decision);
        Assert.All(gate.Records, entry => Assert.Equal("foreign", entry.Status));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("raw-tampered")]
    public void DesignGate_UnreadableRecord_FailsClosed(string corruption)
    {
        var digest = CurrentDigest();
        RecordDesign("claude", "approve", digest);
        RecordDesign("cursor", "approve", digest);
        var directory = CrossRuntimeReviewPaths.DesignDirectory(root, Unit);
        switch (corruption)
        {
            case "garbage":
                File.WriteAllText(Path.Combine(directory, "20990101T000000Z-codex-zzzzzzz.json"), "{\"artifact_kind\":\"nope\"}");
                break;
            default:
                var target = Directory.EnumerateFiles(directory, "*.json").First();
                File.AppendAllText(Path.ChangeExtension(target, ".raw-verdict"), " ");
                break;
        }

        var status = DesignStatus().Status;
        Assert.Equal("blocked", Decision(status));
        Assert.Contains(CrossRuntimeReviewCauses.RecordUnreadable, Reasons(status).Select(reason => reason.Split(':')[0]));
    }

    [Fact]
    public void DesignAndImplementationRecords_NeverCrossCount()
    {
        const int pr = 1812;
        const string head = "1111111111111111111111111111111111111111";
        const string crossUnit = "G835X";
        claims[crossUnit] = Team;
        WriteImplementationPacket(crossUnit);
        WriteQueue((crossUnit, $"https://github.com/{Repo}/pull/{pr}"));
        WriteClaim(crossUnit, Team);
        var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDir(crossUnit));

        RecordImplementation("claude", "approve", head, crossUnit);
        RecordImplementation("cursor", "approve", head, crossUnit);
        var designRead = CrossRuntimeDesignReviewStore.Read(root, crossUnit);
        var designGate = CrossRuntimeReviewGate.EvaluateDesign(Declaration("claude"), DesignResolution(crossUnit), digest, designRead);
        Assert.Equal("missing", designGate.Decision);

        const string designOnlyUnit = "G835Y";
        claims[designOnlyUnit] = Team;
        CopyFixturePacket(designOnlyUnit);
        WriteClaim(designOnlyUnit, Team);
        var designOnlyDigest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDir(designOnlyUnit));
        RecordDesign("claude", "approve", designOnlyDigest, designOnlyUnit);
        RecordDesign("cursor", "approve", designOnlyDigest, designOnlyUnit);
        var implGate = CrossRuntimeReviewGate.Evaluate(
            Declaration("claude"),
            ImplementationResolution(crossUnit),
            head,
            CrossRuntimeReviewStore.Read(root, Repo, pr));
        Assert.Equal("satisfied", implGate.Decision);
        Assert.Equal("satisfied", CrossRuntimeReviewGate.EvaluateDesign(
            Declaration("claude"),
            DesignResolution(designOnlyUnit),
            designOnlyDigest,
            CrossRuntimeDesignReviewStore.Read(root, designOnlyUnit)).Decision);
        Assert.Equal("missing", CrossRuntimeReviewGate.Evaluate(
            Declaration("claude"),
            ImplementationResolution(designOnlyUnit),
            head,
            CrossRuntimeReviewStore.Read(root, Repo, pr)).Decision);
    }

    // ── guides and docs ────────────────────────────────────────────────

    [Fact]
    public void Routing_RegistersDesignKind_AndCatalogsMentionIt()
    {
        Assert.Equal(["record", "request", "status"], CommandRouter.ReviewCrossRuntimeSubcommands.Keys.OrderBy(key => key, StringComparer.Ordinal));
        var list = GuideCommandsListCommand.Groups.Single(group => group.Name == "review").Purpose;
        Assert.Contains("--kind design", list, StringComparison.Ordinal);

        using var help = new StringWriter();
        Assert.Equal(0, GuideHelpCommand.Execute(Context(), ["--format", "json"], help));
        Assert.Contains("review cross-runtime", help.ToString(), StringComparison.Ordinal);
        Assert.Contains("--kind design", help.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Guides_SoloConductorStep3_DescribesDesignReviewLoop_AndStep7ShowsModel()
    {
        var guide = GuideSoloConductorCommand.BuildGuide();
        var step3 = guide.Loop.Single(step => step.Number == 3);
        Assert.Contains(step3.Commands, command => command.Command.Contains("--kind design", StringComparison.Ordinal));
        Assert.Contains(step3.Commands, command => command.Command.Contains("review cross-runtime request --kind design", StringComparison.Ordinal));
        Assert.Contains(step3.Commands, command => command.Command.Contains("review cross-runtime status --kind design", StringComparison.Ordinal));
        Assert.Contains("digest changes", step3.Instruction, StringComparison.Ordinal);

        var step7 = guide.Loop.Single(step => step.Number == 7);
        Assert.Contains(step7.Commands, command => command.Command.Contains("--model <model>", StringComparison.Ordinal));

        var affirmative = new Regex(@"intent-cli (starts|launches|manages|spawns|runs) (an? |the )?(agent|subagent|reviewer|provider)", RegexOptions.IgnoreCase);
        using var solo = new StringWriter();
        Assert.Equal(0, GuideSoloConductorCommand.Execute(Context(), ["--format", "markdown"], solo));
        Assert.DoesNotMatch(affirmative, solo.ToString());
    }

    [Fact]
    public void Docs_EnJa_SectionAndLedgerRows_MentionG835()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var orchestration = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("G835", orchestration, StringComparison.Ordinal);
            Assert.Contains("--kind design", orchestration, StringComparison.Ordinal);
            Assert.Contains("--model", orchestration, StringComparison.Ordinal);

            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("cross-runtime design review record store", ledger, StringComparison.Ordinal);
            Assert.Contains("issue publish-flow design gate", ledger, StringComparison.Ordinal);
            foreach (var row in new[] { "| `review cross-runtime request` |", "| `review cross-runtime record` |", "| `review cross-runtime status` |" })
            {
                var line = Assert.Single(ledger.Split('\n'), candidate => candidate.StartsWith(row, StringComparison.Ordinal));
                Assert.Contains("G835", line, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    [RequiresCrossRuntimeApprove]
    public void Gate_MutationGuard_Documentation_OnlySameRuntimeApproveIsInsufficient()
    {
        var digest = CurrentDigest();
        var read = new CrossRuntimeDesignReviewReadResult([StoredDesign("claude", "approve", digest, clock)], []);
        var gate = CrossRuntimeReviewGate.EvaluateDesign(Declaration("claude"), DesignResolution(), digest, read);
        Assert.Equal("missing", gate.Decision);
        Assert.Contains(gate.Reasons, reason => reason.Cause == CrossRuntimeReviewCauses.Missing && reason.Relation == CrossRuntimeReviewRecord.RelationCrossRuntime);
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

    private static CrossRuntimeReviewResolution DesignResolution(string unit = Unit) => new()
    {
        Resolved = true,
        ExecutionUnit = unit,
        Domain = Domain,
        Team = Team,
    };

    private static CrossRuntimeReviewResolution ImplementationResolution(string unit = Unit) => DesignResolution(unit);

    private (int ExitCode, string Output) Route(string[] args, CliContext? context = null)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, context ?? Context(), writer);
        return (exit, writer.ToString());
    }

    private string[] DesignRequestArgs(string runtime, string outDir, string? model = null, string? clone = null)
    {
        var args = new List<string>
        {
            "request", "--kind", "design", "--execution-unit", Unit, "--runtime", runtime, "--out-dir", outDir,
        };
        if (clone is not null)
        {
            args.AddRange(["--clone", clone]);
        }

        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        return args.ToArray();
    }

    private string[] DesignRecordArgs(string runtime, string verdictFile, string digest, bool write, string? model = null)
    {
        var args = new List<string>
        {
            "record", "--kind", "design", "--execution-unit", Unit, "--packet-digest", digest,
            "--runtime", runtime, "--runtime-version", runtime == "codex" ? "codex-cli 0.154.0" : "2.1.269",
            "--verdict-file", verdictFile,
        };
        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        if (write)
        {
            args.Add("--write");
        }

        return args.ToArray();
    }

    private string[] DesignStatusArgs() => ["status", "--kind", "design", "--execution-unit", Unit];

    private (int ExitCode, JsonElement Status) DesignStatus(CliContext? context = null)
    {
        var (exit, output) = Route(["review", "cross-runtime", .. DesignStatusArgs(), "--format", "json"], context);
        using var document = JsonDocument.Parse(output);
        return (exit, document.RootElement.Clone());
    }

    private static string Decision(JsonElement status) =>
        status.GetProperty("gate").GetProperty("decision").GetString()!;

    private static string[] Reasons(JsonElement status) =>
        status.GetProperty("gate").GetProperty("reasons").EnumerateArray()
            .Select(reason => reason.GetProperty("cause").GetString()
                + (reason.TryGetProperty("relation", out var relation) ? ":" + relation.GetString() : string.Empty))
            .ToArray();

    private void RecordDesign(string runtime, string verdict, string digest, string unit = Unit, string? model = null)
    {
        var body = Verdict(verdict, digest);
        var content = runtime == "codex" ? body : runtime == "claude" ? ClaudeEnvelope(body) : CursorEnvelope(body);
        var file = WriteVerdictFile(runtime, content);
        var args = new List<string>
        {
            "record", "--kind", "design", "--execution-unit", unit, "--packet-digest", digest,
            "--runtime", runtime, "--runtime-version", runtime == "codex" ? "codex-cli 0.154.0" : "2.1.269",
            "--verdict-file", file, "--write",
        };
        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.True(exit == 0, output);
    }

    private void RecordImplementation(string runtime, string verdict, string head, string unit = Unit, string? model = null)
    {
        var body = ImplementationVerdict(verdict, head);
        var content = runtime == "codex" ? body : runtime == "claude" ? ClaudeEnvelopeImplementation(body) : CursorEnvelopeImplementation(body);
        var file = WriteVerdictFile(runtime, content);
        var args = new List<string>
        {
            "record", "--repo", Repo, "--pr", "1812", "--head-sha", head, "--execution-unit", unit,
            "--kind", "implementation", "--runtime", runtime, "--runtime-version", runtime == "codex" ? "codex-cli 0.154.0" : "2.1.269",
            "--verdict-file", file, "--write",
        };
        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        var (exit, output) = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.True(exit == 0, output);
    }

    private static CrossRuntimeReviewReadResult ImplementationRead(string head, string? model)
    {
        var recordedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        CrossRuntimeReviewRecord Record(string runtime, string relation) => new()
        {
            ArtifactKind = "cross-runtime-review-record",
            Repo = Repo,
            Pr = 1812,
            HeadSha = head,
            ExecutionUnit = Unit,
            Domain = Domain,
            Team = Team,
            Kind = CrossRuntimeReviewRecord.KindImplementation,
            Runtime = runtime,
            RuntimeVersion = "2.1.269",
            ConductorRuntime = "claude",
            Relation = relation,
            Model = model,
            Verdict = CrossRuntimeReviewVerdict.Approve,
            BlockingFindings = [],
            Notes = [],
            RecordedAt = recordedAt,
            RawVerdictFile = ".intent-cli/x.raw-verdict",
            RawVerdictSha256 = "00",
        };

        return new CrossRuntimeReviewReadResult(
        [
            new CrossRuntimeReviewStoredRecord("a.json", ".intent-cli/a.json", Record("claude", CrossRuntimeReviewRecord.RelationSameRuntime)),
            new CrossRuntimeReviewStoredRecord("b.json", ".intent-cli/b.json", Record("cursor", CrossRuntimeReviewRecord.RelationCrossRuntime)),
        ], []);
    }

    private static CrossRuntimeDesignReviewStoredRecord StoredDesign(string runtime, string verdict, string digest, DateTimeOffset recordedAt) =>
        new(
            $"{recordedAt:yyyyMMdd'T'HHmmssfffffff'Z'}-{runtime}-{digest[..7]}.json",
            $".intent-cli/cross-runtime-reviews/design/{Unit}/{recordedAt:yyyyMMdd'T'HHmmssfffffff'Z'}-{runtime}-{digest[..7]}.json",
            new CrossRuntimeDesignReviewRecord
            {
                ArtifactKind = CrossRuntimeDesignReviewRecord.ArtifactKindValue,
                PacketDigest = digest,
                TargetRepo = Repo,
                ExecutionUnit = Unit,
                Domain = Domain,
                Team = Team,
                Kind = CrossRuntimeReviewRecord.KindDesign,
                Runtime = runtime,
                RuntimeVersion = "2.1.269",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
                Verdict = verdict,
                BlockingFindings = [],
                Notes = [],
                RecordedAt = recordedAt,
                RawVerdictFile = $".intent-cli/cross-runtime-reviews/design/{Unit}/x.raw-verdict",
                RawVerdictSha256 = "00",
            });

    private string WriteVerdictFile(string runtime, string content)
    {
        var directory = Path.Combine(root, "runs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{runtime}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string Verdict(string verdict, string digest) => verdict == "approve"
        ? JsonSerializer.Serialize(new { verdict, packet_digest = digest, blocking_findings = Array.Empty<object>(), notes = new[] { "looks right" } })
        : JsonSerializer.Serialize(new { verdict, packet_digest = digest, blocking_findings = new[] { new { file = "src/A.cs", line = 12, scenario = "blocking" } }, notes = Array.Empty<string>() });

    private static string ImplementationVerdict(string verdict, string head) => verdict == "approve"
        ? JsonSerializer.Serialize(new { verdict, head_sha = head, blocking_findings = Array.Empty<object>(), notes = new[] { "looks right" } })
        : JsonSerializer.Serialize(new { verdict, head_sha = head, blocking_findings = new[] { new { file = "src/A.cs", line = 12, scenario = "blocking" } }, notes = Array.Empty<string>() });

    private static string ClaudeEnvelope(string verdict)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FixtureG834("claude-envelope.json")))!.AsObject();
        node["structured_output"] = System.Text.Json.Nodes.JsonNode.Parse(verdict);
        node["result"] = verdict;
        return node.ToJsonString();
    }

    private static string CursorEnvelope(string resultText)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FixtureG834("cursor-envelope.json")))!.AsObject();
        node["result"] = resultText;
        return node.ToJsonString();
    }

    private static string ClaudeEnvelopeImplementation(string verdict) => ClaudeEnvelope(verdict);
    private static string CursorEnvelopeImplementation(string verdict) => CursorEnvelope(verdict);

    private string CurrentDigest() => CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDir(Unit));

    private string PacketDir(string unit) => Path.Combine(root, ".intent-cli", "issues", unit);

    private void CopyFixturePacket(string unit)
    {
        var directory = PacketDir(unit);
        Directory.CreateDirectory(directory);
        foreach (var fileName in CrossRuntimeDesignReviewDigest.PacketFileNames)
        {
            File.Copy(Fixture(fileName), Path.Combine(directory, fileName), overwrite: true);
        }
    }

    private void RestoreFixturePacket() => CopyFixturePacket(Unit);

    private void AppendPacketByte()
    {
        var path = Path.Combine(PacketDir(Unit), "implementation.md");
        File.AppendAllText(path, " ");
    }

    private void WritePacketDigestVariant(string marker)
    {
        var path = Path.Combine(PacketDir(Unit), "implementation.md");
        File.WriteAllText(path, $"# implementation {marker}\n");
    }

    private void WriteImplementationPacket(string unit = Unit)
    {
        var directory = PacketDir(unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            "implementation_issue_packet:\n  issue_title: \"G835\"\n  domain: intent-cli\n  target_repo: J-Tech-Japan/intent-system\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G835\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
    }

    private void WriteClaim(string unit, string team)
    {
        var claimPath = Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
        File.WriteAllText(claimPath, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            scope = $"execution-unit:{unit}",
            actor = "design",
            team,
            claimed_at = DateTimeOffset.UtcNow,
            base_commit = D1,
        }));
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

    private static string Fixture(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G835", name);

    private static string FixtureG834(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

    private static string FixtureG842(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G842", name);

    private static List<string> ShellTokens(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
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
}
