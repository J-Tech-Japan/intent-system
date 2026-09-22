using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G842 round-7: §3a path guard, compaction, fences, exit file, and mutation witnesses.</summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G842CrossRuntimeReviewRound7Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G842";
    private const int Pr = 1812;
    private const string H1 = "1111111111111111111111111111111111111111";
    private const string CopilotModel = "gpt-5.6-sol";
    private const string OpencodeModel = "github-copilot/gpt-5.6-sol";
    private const string R3I = "opencode-implementation-big-pickle-live-compaction.jsonl";
    private const string R3D = "opencode-design-big-pickle-live-compaction.jsonl";
    private const string R3D3 = "opencode-design-big-pickle-live-compaction-retry.jsonl";
    private const string R4 = "opencode-design-local-live-compaction-timeout.jsonl";
    private static readonly TimeSpan RouteTimeout = TimeSpan.FromSeconds(5);

    private readonly string root = Directory.CreateTempSubdirectory("g842-r7-").FullName;
    private readonly Dictionary<string, string?> savedEnv = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };

    public G842CrossRuntimeReviewRound7Tests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return claims.TryGetValue(unit, out var team)
                ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", team ?? string.Empty, "held")
                : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
        };
        ReviewCrossRuntimeCommand.Clock = () => DateTimeOffset.UtcNow;
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
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = null;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = null;
        RestoreEnv();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── §3a path guard (R4-1) ───────────────────────────────────────────

    [Fact]
    public void Request_RefusesOutDirUnderSymlinkToCopilotHome_WhenChildMissing()
    {
        using var env = ScratchHome();
        var realCopilot = Path.Combine(env.Home, ".copilot");
        Directory.CreateDirectory(realCopilot);
        var link = Path.Combine(env.Home, "copilot-link");
        Directory.CreateSymbolicLink(link, realCopilot);
        var outDir = Path.Combine(link, "r1");
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesOutDirUnderSymlinkToCopilotHome_WhenChildExists()
    {
        using var env = ScratchHome();
        var realCopilot = Path.Combine(env.Home, ".copilot");
        Directory.CreateDirectory(realCopilot);
        var link = Path.Combine(env.Home, "copilot-link");
        Directory.CreateSymbolicLink(link, realCopilot);
        var outDir = Path.Combine(link, "r1");
        Directory.CreateDirectory(outDir);
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesOutDirUnderSymlinkToConfigOpencode()
    {
        using var env = ScratchHome();
        var realConfig = Path.Combine(env.Home, ".config");
        Directory.CreateDirectory(realConfig);
        var link = Path.Combine(env.Home, "cfg-link");
        Directory.CreateSymbolicLink(link, realConfig);
        var outDir = Path.Combine(link, "opencode", "r1");
        AssertPathInvalid(Request("opencode", outDir));
    }

    [Fact]
    public void Request_RefusesCaseVariantCopilotHome_WhenDotCopilotIsSymlink()
    {
        using var env = ScratchHome();
        var target = Path.Combine(env.Home, "real-copilot");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.Combine(env.Home, ".copilot"), target);
        var outDir = Path.Combine(env.Home, ".COPILOT", "r1");
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesCaseOnlyPathOverlap_OnCaseSensitiveTmpPaths()
    {
        var id = Guid.NewGuid().ToString("N");
        var basePath = $"/tmp/g842-q1-{id}";
        Directory.CreateDirectory($"{basePath}/copilot");
        using var env = ScratchHome();
        SetEnv("COPILOT_HOME", $"{basePath}/copilot");
        var outDir = $"{basePath}/COPILOT/r1";
        Directory.CreateDirectory(Path.GetDirectoryName(outDir)!);
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesCopilotHomeIsolationOverlap_ReportsIsolationPlannedPath()
    {
        var outDir = Path.Combine(root, "s-overlap");
        using var env = ScratchHome();
        var isolation = Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotHome);
        SetEnv("COPILOT_HOME", isolation);
        var result = Request("copilot", outDir);
        Assert.Equal(1, result.ExitCode);
        using var refusal = JsonDocument.Parse(result.Output);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
        var detail = refusal.RootElement.GetProperty("detail").GetString()!;
        Assert.Contains(CrossRuntimeReviewFiles.CopilotHome, detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/tmp", "/private/tmp")]
    [InlineData("/private/tmp", "/tmp")]
    [InlineData("/tmp", "/tmp")]
    public void Request_RefusesTmpPrivateTmpSpellings_ForCopilotHomeOverlap(string copilotPrefix, string outPrefix)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var homePath = $"{copilotPrefix}/g842-r7-{id}/copilot-home";
        Directory.CreateDirectory(homePath);
        using var env = ScratchHome();
        SetEnv("COPILOT_HOME", homePath);
        var outDir = outPrefix == "/tmp"
            ? $"/tmp/g842-r7-{id}/copilot-home"
            : $"/private/tmp/g842-r7-{id}/copilot-home";
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesXdgDataHomeOpencodeOverlap_WithTmpSpelling()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var dataRoot = $"/private/tmp/g842-r7-{id}";
        Directory.CreateDirectory(Path.Combine(dataRoot, "opencode"));
        using var env = ScratchHome();
        SetEnv("XDG_DATA_HOME", dataRoot);
        var outDir = $"/tmp/g842-r7-{id}/opencode/r1";
        AssertPathInvalid(Request("opencode", outDir));
    }

    [Fact]
    public void Request_RefusesSymlinkedRenderedPrompt()
    {
        var outDir = Path.Combine(root, "symlink-prompt");
        Directory.CreateDirectory(outDir);
        var victim = Path.Combine(root, "victim-prompt.md");
        File.WriteAllText(victim, "secret");
        File.CreateSymbolicLink(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt), victim);
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesDanglingSymlinkedRenderedPrompt()
    {
        var outDir = Path.Combine(root, "dangling-prompt");
        Directory.CreateDirectory(outDir);
        File.CreateSymbolicLink(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt), Path.Combine(root, "missing-prompt.md"));
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesSymlinkedRenderedPrompt_TargetInsideOutDir()
    {
        var outDir = Path.Combine(root, "symlink-inside-prompt");
        Directory.CreateDirectory(outDir);
        var real = Path.Combine(outDir, "real-prompt.md");
        File.WriteAllText(real, "inside");
        File.CreateSymbolicLink(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt), real);
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesSymlinkedIsolationDirectory_TargetInsideOutDir()
    {
        var outDir = Path.Combine(root, "symlink-inside-isolation");
        Directory.CreateDirectory(outDir);
        var victim = Path.Combine(outDir, "nested-home");
        Directory.CreateDirectory(victim);
        Directory.CreateSymbolicLink(Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotHome), victim);
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesSymlinkedIsolationDirectory()
    {
        var outDir = Path.Combine(root, "symlink-isolation");
        Directory.CreateDirectory(outDir);
        var victim = Path.Combine(root, "victim-home");
        Directory.CreateDirectory(victim);
        Directory.CreateSymbolicLink(Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotHome), victim);
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesOutDirInsideRealCopilotHome()
    {
        using var env = ScratchHome();
        var copilotHome = Path.Combine(env.Home, ".copilot");
        Directory.CreateDirectory(copilotHome);
        AssertPathInvalid(Request("copilot", Path.Combine(copilotHome, "r1")));
    }

    [Fact]
    public void Request_RefusesOutDirInsideXdgConfigOpencode()
    {
        using var env = ScratchHome();
        var configRoot = Path.Combine(env.Home, ".config");
        Directory.CreateDirectory(Path.Combine(configRoot, "opencode"));
        SetEnv("XDG_CONFIG_HOME", configRoot);
        AssertPathInvalid(Request("opencode", Path.Combine(configRoot, "opencode", "r1")));
    }

    [Fact]
    public void Request_RefusesOutDirInsideOpencodeConfigDir()
    {
        using var env = ScratchHome();
        var configDir = Path.Combine(env.Home, "ocd");
        Directory.CreateDirectory(configDir);
        SetEnv("OPENCODE_CONFIG_DIR", configDir);
        AssertPathInvalid(Request("opencode", Path.Combine(configDir, "r1")));
    }

    [Fact]
    public void Request_RefusesDotfileLayoutUnderConfigOpencode()
    {
        using var env = ScratchHome();
        var dotfiles = Path.Combine(env.Home, "dotfiles", "opencode");
        Directory.CreateDirectory(dotfiles);
        Directory.CreateDirectory(Path.Combine(env.Home, ".config"));
        Directory.CreateSymbolicLink(Path.Combine(env.Home, ".config", "opencode"), dotfiles);
        var outDir = Path.Combine(dotfiles, "r1");
        AssertPathInvalid(Request("opencode", outDir));
    }

    [Fact]
    public void Request_RefusesCopilotHomeInsideOutDir()
    {
        var outDir = Path.Combine(root, "copilot-nested");
        using var env = ScratchHome();
        SetEnv("COPILOT_HOME", Path.Combine(outDir, "copilot-home"));
        AssertPathInvalid(Request("copilot", outDir));
    }

    [Fact]
    public void Request_RefusesXdgConfigHomeOpencodeInsideOutDir()
    {
        var outDir = Path.Combine(root, "xdg-nested");
        using var env = ScratchHome();
        SetEnv("XDG_CONFIG_HOME", Path.Combine(outDir, "opencode-xdg"));
        AssertPathInvalid(Request("opencode", outDir));
    }

    [Fact]
    public void Request_RefusesOpencodeConfigDirInsideOutDir()
    {
        var outDir = Path.Combine(root, "ocd-nested");
        using var env = ScratchHome();
        SetEnv("OPENCODE_CONFIG_DIR", Path.Combine(outDir, "opencode-config-dir"));
        AssertPathInvalid(Request("opencode", outDir));
    }

    [Fact]
    public void Request_AcceptsXdgConfigHomeTmpX_NegativeCase()
    {
        var id = Guid.NewGuid().ToString("N");
        var xdgRoot = $"/tmp/g842-o8-{id}";
        Directory.CreateDirectory(xdgRoot);
        using var env = ScratchHome();
        SetEnv("XDG_CONFIG_HOME", xdgRoot);
        var outDir = Path.Combine(xdgRoot, "r1");
        Assert.Equal(0, Request("opencode", outDir).ExitCode);
    }

    [Fact]
    public void Request_HardLinkedPrompt_LeavesOutsideFileUnchanged()
    {
        var outside = Path.Combine(root, "outside-prompt.md");
        File.WriteAllText(outside, "outside-content");
        var outsideInode = GetInode(outside);
        var outDir = Path.Combine(root, "hardlink-out");
        Directory.CreateDirectory(outDir);
        CreateHardLink(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt), outside);
        var (exit, output) = Request("copilot", outDir);
        Assert.True(exit == 0, output);
        Assert.Equal("outside-content", File.ReadAllText(outside));
        Assert.Equal(outsideInode, GetInode(outside));
        Assert.NotEqual(outsideInode, GetInode(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt)));
    }

    [Fact]
    public void Request_RefusesDirectoryNamedPromptMd()
    {
        var outDir = Path.Combine(root, "dir-prompt");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt));
        var (exit, output) = Request("copilot", outDir);
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
        Assert.DoesNotContain("Unhandled exception", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_RefusesFifoNamedPromptMd_WithoutHanging()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        var outDir = Path.Combine(root, "fifo-prompt");
        Directory.CreateDirectory(outDir);
        var fifoPath = Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt);
        Assert.Equal(0, mkfifo(fifoPath, 384));
        var routeTask = Task.Run(() => Request("copilot", outDir));
        var completed = await Task.WhenAny(routeTask, Task.Delay(RouteTimeout));
        Assert.Same(routeTask, completed);
        var (exit, output) = await routeTask;
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains("not a regular file", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("copilot", "implementation", "prompt.md")]
    [InlineData("copilot", "implementation", "invocation.txt")]
    [InlineData("copilot", "design", "prompt.md")]
    [InlineData("copilot", "design", "invocation.txt")]
    [InlineData("opencode", "implementation", "prompt.md")]
    [InlineData("opencode", "implementation", "invocation.txt")]
    [InlineData("opencode", "design", "prompt.md")]
    [InlineData("opencode", "design", "invocation.txt")]
    public void Request_ZeroByteRenderedFile_RefusesWithoutWriting_AndAcceptsAfterDelete(
        string runtime,
        string kind,
        string renderedName)
    {
        var outDir = Path.Combine(root, $"zero-byte-{runtime}-{kind}-{Path.GetFileNameWithoutExtension(renderedName)}");
        var workspace = Path.Combine(root, $"zero-byte-workspace-{runtime}-{kind}");
        var hasClone = kind == CrossRuntimeReviewRecord.KindImplementation;

        var first = RequestAt(runtime, kind, workspace, outDir, hasClone);
        Assert.True(first.ExitCode == 0, first.Output);

        var renderedPath = Path.Combine(outDir, renderedName);
        File.WriteAllBytes(renderedPath, []);
        Assert.Equal(0, new FileInfo(renderedPath).Length);
        var beforeRefusal = SnapshotOutDir(outDir);

        var refusalResult = RequestAt(runtime, kind, workspace, outDir, hasClone);
        Assert.Equal(1, refusalResult.ExitCode);
        using (var refusal = JsonDocument.Parse(refusalResult.Output))
        {
            Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
            Assert.Equal(
                $"planned rendered path '{renderedPath}' is not a regular file.",
                refusal.RootElement.GetProperty("detail").GetString());
        }

        Assert.Equal(beforeRefusal, SnapshotOutDir(outDir));
        Assert.Equal(0, new FileInfo(renderedPath).Length);

        File.Delete(renderedPath);
        var accepted = RequestAt(runtime, kind, workspace, outDir, hasClone);
        Assert.True(accepted.ExitCode == 0, accepted.Output);
        Assert.True(new FileInfo(renderedPath).Length > 0);
    }

    // ── compaction (R4-2) ───────────────────────────────────────────────

    [Theory]
    [InlineData("no-synthetic")]
    [InlineData("no-metadata")]
    [InlineData("cc-string")]
    [InlineData("parttype-reasoning")]
    public void OpencodeCompactionCapture_ReopenMutations_RefuseT3(string mutation)
    {
        var content = MutateCompactionReopen(R3I, mutation);
        Assert.False(CrossRuntimeReviewVerdict.TryParse("opencode", content, out _, out var error));
        Assert.Contains("T3 refused event 'text'", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dup-reopen")]
    [InlineData("reopen-inside-final-step")]
    public void OpencodeCompactionCapture_ReopenMutations_RefuseT4(string mutation)
    {
        var content = MutateCompactionReopen(R3I, mutation);
        Assert.False(CrossRuntimeReviewVerdict.TryParse("opencode", content, out _, out var error));
        Assert.Contains("T4 refused event 'text'", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(R3I)]
    [InlineData(R3D)]
    [InlineData(R3D3)]
    [InlineData(R4)]
    public void OpencodeCompactionCapture_TruncatedBeforeReopen_RefusesVerdictText(string fixture)
    {
        var lines = File.ReadAllLines(Fixture(fixture)).ToList();
        var reopenIndex = FindCompactionReopenIndex(lines);
        var truncated = string.Join('\n', lines.Take(reopenIndex));
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", truncated, out _, out var error));
        Assert.Equal("verdict-invalid: opencode final step text does not end with a verdict JSON object.", error);
    }

    [Fact]
    public void OpencodeCompactionCapture_TruncatedAfterReopen_RefusesT13()
    {
        var lines = File.ReadAllLines(Fixture(R3I)).ToList();
        var reopenIndex = FindCompactionReopenIndex(lines);
        var truncated = string.Join('\n', lines.Take(reopenIndex + 1));
        Assert.False(CrossRuntimeReviewVerdict.TryParse("opencode", truncated, out _, out var error));
        Assert.Equal("verdict-invalid: T13 refused because the file did not end in state terminated.", error);
    }

    [Fact]
    public void OpencodeCompactionCapture_DesignCompaction_RefusesMetadataRemovedInCapture()
    {
        var lines = File.ReadAllLines(Fixture(R3D)).ToList();
        var reopenIndex = FindCompactionReopenIndex(lines);
        var node = JsonNode.Parse(lines[reopenIndex])!.AsObject();
        node["part"]!.AsObject().Remove("metadata");
        lines[reopenIndex] = node.ToJsonString();
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", string.Join('\n', lines), out _, out var error));
        Assert.Contains("T3 refused event 'text'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OpencodeCompactionCapture_DesignCompaction_RefusesSyntheticRemovedInCapture()
    {
        var lines = File.ReadAllLines(Fixture(R3D)).ToList();
        var reopenIndex = FindCompactionReopenIndex(lines);
        var node = JsonNode.Parse(lines[reopenIndex])!.AsObject();
        node["part"]!.AsObject().Remove("synthetic");
        lines[reopenIndex] = node.ToJsonString();
        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", string.Join('\n', lines), out _, out var error));
        Assert.Contains("T3 refused event 'text'", error, StringComparison.Ordinal);
    }

    // ── fence forms (R4-2 / R4-6) ─────────────────────────────────────

    [Theory]
    [InlineData("as-captured", true)]
    [InlineData("plain-opener", true)]
    [InlineData("no-narration", true)]
    [InlineData("trailing-blanks", true)]
    [InlineData("text-after-fence", false)]
    [InlineData("two-blocks", false)]
    [InlineData("two-objects", false)]
    [InlineData("object-plus-prose", false)]
    [InlineData("array", false)]
    [InlineData("unterminated", false)]
    [InlineData("yaml-opener", false)]
    public void OpencodeFenceForms_OnR3iAnswer_ParseAtParserLayer(string mutation, bool accepted)
    {
        var answer = ExtractR3iFinalAnswer();
        var mutated = MutateFenceForm(answer, mutation);
        var parsed = CrossRuntimeReviewVerdict.TryParseVerdictText(mutated, out var document, out var error);
        if (accepted)
        {
            Assert.True(parsed, error);
            using (document)
            {
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            }
        }
        else
        {
            Assert.False(parsed);
            Assert.StartsWith("verdict-invalid:", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OpencodeR3d3_FencedVerdict_AcceptsAtParserLayer_ButRecordRefusesSchema()
    {
        var answer = ExtractFinalAnswerFromCapture(R3D3);
        Assert.True(CrossRuntimeReviewVerdict.TryParseVerdictText(answer, out var document, out var parseError), parseError);
        using (document)
        {
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        Assert.False(CrossRuntimeReviewVerdict.TryParseDesign("opencode", File.ReadAllText(Fixture(R3D3)), out _, out var designError));
        Assert.Contains("notes", designError, StringComparison.Ordinal);

        var file = WriteVerdictFile("opencode", File.ReadAllText(Fixture(R3D3)));
        var (exit, output) = Route(["review", "cross-runtime", .. DesignRecordArgs("opencode", file, CurrentDigest(), write: false), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.VerdictInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains("notes", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_CursorFencedVerdict_RefusesWithBaseCauseAndMessage()
    {
        var verdict = Verdict("approve", H1);
        var fenced = "```json\n" + verdict + "\n```";
        var content = CursorEnvelope(fenced);
        var file = WriteVerdictFile("cursor", content);
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("cursor", file, H1, write: false), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.VerdictInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            $"verdict file '{file}' is invalid for runtime 'cursor': cursor envelope 'result' does not end with a verdict JSON object.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    // ── exit file (R4-2 / R4-4 / R4-5) ────────────────────────────────

    [Fact]
    public void Record_OpencodeExitSymlink_RefusesMissing()
    {
        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        var exitPath = Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit);
        File.Delete(exitPath);
        File.CreateSymbolicLink(exitPath, Path.Combine(root, "exit-target.txt"));
        File.WriteAllBytes(Path.Combine(root, "exit-target.txt"), "0\n"u8.ToArray());
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("opencode", file, H1, write: false), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ExitStatusMissing, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "opencode-exit.txt must be a regular file, not a symlink.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void Record_OpencodeExitDirectory_RefusesMissing()
    {
        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        var exitPath = Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit);
        File.Delete(exitPath);
        Directory.CreateDirectory(exitPath);
        AssertExitMissing(RecordArgs("opencode", file, H1, write: false));
    }

    [Fact]
    public async Task Record_OpencodeExitFifo_RefusesNonzeroWithoutHanging()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        var exitPath = Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit);
        File.Delete(exitPath);
        Assert.Equal(0, mkfifo(exitPath, 384));
        await AssertExitNonzeroBoundedAsync(RecordArgs("opencode", file, H1, write: false));
    }

    [Fact]
    public async Task Record_VerdictFifo_RefusesInvalidWithinBoundedTime()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        var fifo = Path.Combine(root, "verdict.fifo");
        Assert.Equal(0, mkfifo(fifo, 384));
        File.WriteAllBytes(Path.Combine(root, CrossRuntimeReviewFiles.OpencodeExit), "0\n"u8.ToArray());
        var routeTask = Task.Run(() => Route([
            "review", "cross-runtime", .. RecordArgs("opencode", fifo, H1, write: false), "--format", "json"]));
        var completed = await Task.WhenAny(routeTask, Task.Delay(RouteTimeout));
        Assert.Same(routeTask, completed);
        var (exit, output) = await routeTask;
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.VerdictInvalid, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void DesignRecord_OpencodeExitMissing_Refuses()
    {
        var file = WriteVerdictFile("opencode", OpencodeDesignEnvelope("approve", CurrentDigest()));
        File.Delete(Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit));
        AssertExitMissing(DesignRecordArgs("opencode", file, CurrentDigest(), write: false));
    }

    [Fact]
    public void Record_OpencodeExitStatus_Refuses143WithBoundedEscapedBytes()
    {
        var file = WriteVerdictFile("opencode", OpencodeImplementationEnvelope("approve", H1));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(file)!, CrossRuntimeReviewFiles.OpencodeExit), "143\n"u8.ToArray());
        var (exit, output) = Route(["review", "cross-runtime", .. RecordArgs("opencode", file, H1, write: false), "--format", "json"]);
        Assert.Equal(1, exit);
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.ExitStatusNonzero, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            "opencode-exit.txt bytes are \\x31\\x34\\x33\\x0a; exactly 0\\n is required.",
            refusal.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void Request_HardLinkedOpencodeReviewerJson_LeavesOutsideFileUnchanged()
    {
        var outside = Path.Combine(root, "outside-config.json");
        File.WriteAllBytes(outside, [1, 2, 3, 4]);
        var outsideInode = GetInode(outside);
        var outDir = Path.Combine(root, "hardlink-config");
        Directory.CreateDirectory(outDir);
        CreateHardLink(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig), outside);
        Assert.Equal(0, Request("opencode", outDir).ExitCode);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(outside));
        Assert.Equal(outsideInode, GetInode(outside));
    }

    [Fact]
    public void RenderedFor_IsKindAware_AndOnlyNewDesignFallbacksCarryWorkspace()
    {
        foreach (var runtime in new[] { "copilot", "opencode" })
        {
            Assert.Contains(CrossRuntimeReviewFiles.Workspace,
                CrossRuntimeReviewFiles.RenderedFor(runtime, "design", hasClone: false));
            Assert.DoesNotContain(CrossRuntimeReviewFiles.Workspace,
                CrossRuntimeReviewFiles.RenderedFor(runtime, "design", hasClone: true));
            Assert.DoesNotContain(CrossRuntimeReviewFiles.Workspace,
                CrossRuntimeReviewFiles.RenderedFor(runtime, "implementation", hasClone: true));
        }

        foreach (var runtime in new[] { "codex", "claude", "cursor" })
        {
            Assert.Equal(CrossRuntimeReviewFiles.Rendered,
                CrossRuntimeReviewFiles.RenderedFor(runtime, "design", hasClone: false));
            Assert.DoesNotContain(CrossRuntimeReviewFiles.Workspace,
                CrossRuntimeReviewFiles.RenderedFor(runtime, "implementation", hasClone: true));
        }
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void DesignWithoutClone_CreatesDedicatedEmptyWorkspace_AndRerenders(string runtime)
    {
        var outDir = Path.Combine(root, "design-fallback-" + runtime);
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        var args = DesignRequestArgs(runtime, outDir, model);

        var first = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(0, first.ExitCode);
        var workspace = Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace);
        Assert.True(Directory.Exists(workspace));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace));
        Assert.Equal(
            CrossRuntimeReviewFiles.RenderedFor(runtime, "design", hasClone: false).OrderBy(name => name, StringComparer.Ordinal),
            Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories));

        var second = Route(["review", "cross-runtime", .. args, "--format", "json"]);
        Assert.Equal(0, second.ExitCode);
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_RefusesWorkspaceAndOutDirOverlapBeforeEnumeration(string runtime, string kind)
    {
        var cases = new[]
        {
            ("equal", Path.Combine(root, "equal"), Path.Combine(root, "equal")),
            ("out-under-workspace", Path.Combine(root, "workspace"), Path.Combine(root, "workspace", "review")),
            ("workspace-under-out", Path.Combine(root, "out", "clone"), Path.Combine(root, "out")),
        };

        foreach (var (name, workspace, outDir) in cases)
        {
            Directory.CreateDirectory(workspace);
            if (name == "workspace-under-out")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(workspace)!);
            }

            var result = RequestAt(runtime, kind, workspace, outDir, hasClone: true);
            Assert.Equal(1, result.ExitCode);
            using var refusal = JsonDocument.Parse(result.Output);
            Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
            Assert.DoesNotContain(CrossRuntimeReviewCauses.OutDirNotEmpty, result.Output, StringComparison.Ordinal);
        }

        var realWorkspace = Path.Combine(root, "real-workspace");
        Directory.CreateDirectory(realWorkspace);
        var linkedOut = Path.Combine(root, "linked-out");
        Directory.CreateSymbolicLink(linkedOut, realWorkspace);
        var linkedResult = RequestAt(runtime, kind, realWorkspace, linkedOut, hasClone: true);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, JsonDocument.Parse(linkedResult.Output).RootElement.GetProperty("cause").GetString());
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_RefusesEscapingWorkspaceSymlinks_ByWorkspaceRelativeName(string runtime, string kind)
    {
        var outside = Path.Combine(root, "outside-" + runtime + "-" + kind);
        Directory.CreateDirectory(outside);
        var rootWorkspace = Path.Combine(root, "symlink-workspace-root-" + runtime + "-" + kind);
        Directory.CreateDirectory(rootWorkspace);
        Directory.CreateSymbolicLink(Path.Combine(rootWorkspace, "root-escape"), outside);
        var rootResult = RequestAt(runtime, kind, rootWorkspace, Path.Combine(root, "out-root-" + runtime + kind), hasClone: true);
        AssertWorkspaceSymlinkRefusal(rootResult, "root-escape", outside);

        var nestedWorkspace = Path.Combine(root, "symlink-workspace-nested-" + runtime + "-" + kind);
        var nested = Path.Combine(nestedWorkspace, "nested");
        Directory.CreateDirectory(nested);
        Directory.CreateSymbolicLink(Path.Combine(nested, "nested-escape"), outside);
        var nestedResult = RequestAt(runtime, kind, nestedWorkspace, Path.Combine(root, "out-nested-" + runtime + kind), hasClone: true);
        AssertWorkspaceSymlinkRefusal(nestedResult, "nested/nested-escape", outside);

        var danglingWorkspace = Path.Combine(root, "symlink-workspace-dangling-" + runtime + "-" + kind);
        Directory.CreateDirectory(danglingWorkspace);
        File.CreateSymbolicLink(Path.Combine(danglingWorkspace, "dangling"), Path.Combine(root, "does-not-exist"));
        var danglingResult = RequestAt(runtime, kind, danglingWorkspace, Path.Combine(root, "out-dangling-" + runtime + kind), hasClone: true);
        AssertWorkspaceSymlinkRefusal(danglingResult, "dangling", Path.Combine(root, "does-not-exist"));
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_RefusesEscapingWorkspaceSymlink_WhenDotDotFollowsLink(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Path.Combine(root, "dotdot-parent-" + runtime + "-" + kind);
        var workspace = Path.Combine(parent, "grand", "W");
        var links = Path.Combine(workspace, "a", "b");
        Directory.CreateDirectory(links);
        File.WriteAllText(Path.Combine(workspace, "a", "secret.txt"), "decoy");
        var outside = Path.Combine(parent, "secret.txt");
        File.WriteAllText(outside, "outside");
        Directory.CreateSymbolicLink(Path.Combine(links, "dl"), "../..");
        File.CreateSymbolicLink(Path.Combine(workspace, "e"), "a/b/dl/../../secret.txt");

        var result = RequestAt(runtime, kind, workspace, Path.Combine(root, "dotdot-out-" + runtime + "-" + kind), hasClone: true);
        AssertWorkspaceSymlinkRefusal(result, "e", outside);
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_AcceptsWorkspaceSymlink_WhenDotDotFollowsLinkButStaysInside(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var workspace = Path.Combine(root, "dotdot-inside-" + runtime + "-" + kind);
        var links = Path.Combine(workspace, "a", "b");
        Directory.CreateDirectory(links);
        File.WriteAllText(Path.Combine(workspace, "a", "secret.txt"), "decoy");
        File.WriteAllText(Path.Combine(workspace, "a", "inside.txt"), "inside");
        Directory.CreateSymbolicLink(Path.Combine(links, "dl"), "../..");
        File.CreateSymbolicLink(Path.Combine(workspace, "e"), "a/b/dl/a/b/dl/a/inside.txt");

        var result = RequestAt(runtime, kind, workspace, Path.Combine(root, "dotdot-inside-out-" + runtime + "-" + kind), hasClone: true);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_AcceptsWorkspaceSymlink_WhenResolvedTargetExistsButRawJoinedTargetDoesNot(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var env = ScratchHome();
        var workspace = Path.Combine(root, "raw-presence-" + runtime + "-" + kind);
        var links = Path.Combine(workspace, "a", "b");
        Directory.CreateDirectory(links);
        Directory.CreateDirectory(Path.Combine(workspace, "b"));
        File.WriteAllText(Path.Combine(workspace, "b", "inside.txt"), "inside");
        Directory.CreateSymbolicLink(Path.Combine(links, "dl"), "..");
        File.CreateSymbolicLink(Path.Combine(workspace, "e"), "a/b/dl/../b/inside.txt");

        var rawJoinedTarget = Path.Combine(workspace, "a/b/dl/../b/inside.txt");
        Assert.False(File.Exists(rawJoinedTarget));
        Assert.True(CrossRuntimeReviewHomeAccessGuard.TryResolvePath(rawJoinedTarget, out var resolvedTarget), rawJoinedTarget);
        Assert.Equal(CrossRuntimeReviewHomeAccessGuard.ResolvePath(Path.Combine(workspace, "b", "inside.txt")), resolvedTarget);
        var resolvedWorkspace = CrossRuntimeReviewHomeAccessGuard.ResolvePath(workspace);
        Assert.True(CrossRuntimeReviewHomeAccessGuard.TryResolvePath(Path.Combine(resolvedWorkspace, "a/b/dl/../b/inside.txt"), out var scanResolvedTarget));
        Assert.Equal(resolvedTarget, scanResolvedTarget);
        Assert.True(CrossRuntimeReviewHomeAccessGuard.PathIsPresent(scanResolvedTarget), scanResolvedTarget);

        var result = RequestAt(runtime, kind, workspace, Path.Combine(root, "raw-presence-out-" + runtime + "-" + kind), hasClone: true);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_RefusesWorkspaceSymlinkCycle_NamingEntry(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var workspace = Path.Combine(root, "symlink-cycle-" + runtime + "-" + kind);
        Directory.CreateDirectory(workspace);
        Directory.CreateSymbolicLink(Path.Combine(workspace, "x"), "y");
        Directory.CreateSymbolicLink(Path.Combine(workspace, "y"), "x");

        var result = RequestAt(runtime, kind, workspace, Path.Combine(root, "symlink-cycle-out-" + runtime + "-" + kind), hasClone: true);
        AssertWorkspaceSymlinkRefusal(result, "x", Path.Combine(workspace, "y"));
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_RefusesWorkspaceSymlinkChainOverFortyExpansions_NamingEntry(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var workspace = Path.Combine(root, "symlink-chain-" + runtime + "-" + kind);
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "inside.txt"), "inside");
        for (var index = 40; index >= 0; index--)
        {
            var target = index == 40 ? "inside.txt" : "zchain" + (index + 1);
            File.CreateSymbolicLink(Path.Combine(workspace, "zchain" + index), target);
        }

        File.CreateSymbolicLink(Path.Combine(workspace, "entry"), "zchain0");

        var result = RequestAt(runtime, kind, workspace, Path.Combine(root, "symlink-chain-out-" + runtime + "-" + kind), hasClone: true);
        AssertWorkspaceSymlinkRefusal(result, "entry", Path.Combine(workspace, "zchain0"));
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_RefusesProtectedRoot_WhenDotDotFollowsLink(string runtime, string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var env = ScratchHome();
        var protectedRoot = Path.Combine(env.Home, ".config", "opencode");
        var protectedLinkTarget = Path.Combine(protectedRoot, "link-target", "child");
        Directory.CreateDirectory(protectedLinkTarget);

        var lexicalRoot = Path.Combine(root, "protected-dotdot-" + runtime + "-" + kind);
        var links = Path.Combine(lexicalRoot, "a", "b");
        Directory.CreateDirectory(links);
        Directory.CreateSymbolicLink(Path.Combine(links, "dl"), protectedLinkTarget);
        File.CreateSymbolicLink(Path.Combine(lexicalRoot, "e"), "a/b/dl/../../inside");

        var workspace = Path.Combine(root, "protected-dotdot-workspace-" + runtime + "-" + kind);
        Directory.CreateDirectory(workspace);
        var linkedOut = Path.Combine(lexicalRoot, "e", "review");
        var directOut = Path.Combine(protectedRoot, "direct-review-" + runtime + "-" + kind);

        var direct = RequestAt(runtime, kind, workspace, directOut, hasClone: true);
        var linked = RequestAt(runtime, kind, workspace, linkedOut, hasClone: true);

        AssertPathInvalid(direct);
        AssertPathInvalid(linked);
        Assert.Equal(
            JsonDocument.Parse(direct.Output).RootElement.GetProperty("cause").GetString(),
            JsonDocument.Parse(linked.Output).RootElement.GetProperty("cause").GetString());
        Assert.Contains("protected home", linked.Output, StringComparison.Ordinal);
        Assert.Contains(protectedRoot, linked.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("copilot", "implementation")]
    [InlineData("copilot", "design")]
    [InlineData("opencode", "implementation")]
    [InlineData("opencode", "design")]
    public void Request_AcceptsWorkspaceSymlinkResolvingInside(string runtime, string kind)
    {
        var workspace = Path.Combine(root, "inside-workspace-" + runtime + kind);
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, "target"));
        Directory.CreateSymbolicLink(Path.Combine(workspace, "inside-link"), Path.Combine(workspace, "target"));
        var result = RequestAt(runtime, kind, workspace, Path.Combine(root, "inside-out-" + runtime + kind), hasClone: true);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("022")]
    [InlineData("000")]
    public void Request_RendersPrivateFilesAndDirectoriesAtCreation_UnderUmask(string umaskText)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var old = umask(Convert.ToInt32(umaskText, 8));
        try
        {
            foreach (var (runtime, kind) in new[]
                     {
                         ("copilot", CrossRuntimeReviewRecord.KindImplementation),
                         ("copilot", CrossRuntimeReviewRecord.KindDesign),
                         ("opencode", CrossRuntimeReviewRecord.KindImplementation),
                         ("opencode", CrossRuntimeReviewRecord.KindDesign),
                     })
            {
                var outDir = Path.Combine(root, $"modes-{runtime}-{kind}-{umaskText}");
                var result = RequestAt(
                    runtime,
                    kind,
                    Path.Combine(root, $"clone-{runtime}-{kind}-{umaskText}"),
                    outDir,
                    hasClone: kind == CrossRuntimeReviewRecord.KindImplementation);
                Assert.Equal(0, result.ExitCode);

                foreach (var file in Directory.EnumerateFiles(outDir, "*", SearchOption.TopDirectoryOnly))
                {
                    Assert.Equal(
                        UnixFileMode.UserRead | UnixFileMode.UserWrite,
                        File.GetUnixFileMode(file) & (UnixFileMode)0x1ff);
                }

                foreach (var directory in Directory.EnumerateDirectories(outDir, "*", SearchOption.TopDirectoryOnly))
                {
                    Assert.Equal(
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                        File.GetUnixFileMode(directory) & (UnixFileMode)0x1ff);
                }

                if (kind == CrossRuntimeReviewRecord.KindDesign)
                {
                    var workspace = Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace);
                    Assert.Empty(Directory.EnumerateFileSystemEntries(workspace));
                }
            }
        }
        finally
        {
            umask(old);
        }
    }

    [Theory]
    [InlineData("022")]
    [InlineData("000")]
    public void Request_CreatesAtMost0600_AndPreservesOnlyStricterExistingMode_UnderUmask(string umaskText)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var old = umask(Convert.ToInt32(umaskText, 8));
        try
        {
            var outDir = Path.Combine(root, $"existing-modes-{umaskText}");
            Directory.CreateDirectory(outDir);
            var prompt = Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt);
            File.WriteAllText(prompt, "old");
            File.SetUnixFileMode(prompt, (UnixFileMode)0x100);
            UnixFileMode? modeAtCreation = null;
            using (CrossRuntimeReviewRequestSupport.RegisterBeforeMoveHook(
                       prompt,
                       temp => modeAtCreation = ReadUnixMode(temp)))
            {
                Assert.Equal(0, Request("opencode", outDir).ExitCode);
            }

            Assert.Equal((UnixFileMode)0x100, modeAtCreation);
            Assert.Equal((UnixFileMode)0x100, File.GetUnixFileMode(prompt) & (UnixFileMode)0x1ff);

            File.SetUnixFileMode(prompt, (UnixFileMode)0x1a4);
            modeAtCreation = null;
            using (CrossRuntimeReviewRequestSupport.RegisterBeforeMoveHook(
                       prompt,
                       temp => modeAtCreation = ReadUnixMode(temp)))
            {
                Assert.Equal(0, Request("opencode", outDir).ExitCode);
            }

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, modeAtCreation);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(prompt) & (UnixFileMode)0x1ff);

            var newOutDir = Path.Combine(root, $"new-mode-{umaskText}");
            var newPrompt = Path.Combine(newOutDir, CrossRuntimeReviewFiles.Prompt);
            modeAtCreation = null;
            using (CrossRuntimeReviewRequestSupport.RegisterBeforeMoveHook(
                       newPrompt,
                       temp => modeAtCreation = ReadUnixMode(temp)))
            {
                Assert.Equal(0, Request("opencode", newOutDir).ExitCode);
            }

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, modeAtCreation);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(newPrompt) & (UnixFileMode)0x1ff);
        }
        finally
        {
            umask(old);
        }
    }

    [Theory]
    [InlineData("022")]
    [InlineData("000")]
    public void Request_AcceptsExistingIsolationDirectories_AndNarrowsThemTo0700_UnderUmask(string umaskText)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var old = umask(Convert.ToInt32(umaskText, 8));
        try
        {
            foreach (var (runtime, kind, hasClone) in new[]
                     {
                         ("copilot", CrossRuntimeReviewRecord.KindImplementation, true),
                         ("copilot", CrossRuntimeReviewRecord.KindDesign, false),
                         ("opencode", CrossRuntimeReviewRecord.KindImplementation, true),
                         ("opencode", CrossRuntimeReviewRecord.KindDesign, false),
                     })
            {
                foreach (var initialMode in new[] { (UnixFileMode)0x1ed, (UnixFileMode)0x1c0 })
                {
                    var outDir = Path.Combine(root, $"existing-isolation-{runtime}-{kind}-{umaskText}-{(int)initialMode}");
                    Directory.CreateDirectory(outDir);
                    var directoryNames = CrossRuntimeReviewRuntimes.IsolationDirectories(runtime)
                        .Concat(kind == CrossRuntimeReviewRecord.KindDesign ? [CrossRuntimeReviewFiles.Workspace] : [])
                        .ToArray();
                    foreach (var directoryName in directoryNames)
                    {
                        var path = Path.Combine(outDir, directoryName);
                        Directory.CreateDirectory(path);
                        File.SetUnixFileMode(path, initialMode);
                    }

                    var result = RequestAt(
                        runtime,
                        kind,
                        Path.Combine(root, $"existing-isolation-clone-{runtime}-{kind}-{umaskText}-{(int)initialMode}"),
                        outDir,
                        hasClone);
                    Assert.True(result.ExitCode == 0, result.Output);

                    foreach (var directoryName in directoryNames)
                    {
                        Assert.Equal(
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                            File.GetUnixFileMode(Path.Combine(outDir, directoryName)) & (UnixFileMode)0x1ff);
                    }
                }
            }
        }
        finally
        {
            umask(old);
        }
    }

    [Fact]
    public void Request_ProviderSecretAppearsOnlyInReviewerConfig_AndSourceMustBeOutsideWorkspace()
    {
        var marker = "SECRET-MARKER-G842-DO-NOT-TREAT-AS-CREDENTIAL";
        var workspace = Path.Combine(root, "provider-workspace");
        Directory.CreateDirectory(workspace);
        var provider = Path.Combine(root, "provider.json");
        File.WriteAllText(provider, JsonSerializer.Serialize(new { provider = new { local = new { options = new { apiKey = marker } } } }));
        var outDir = Path.Combine(root, "provider-out");
        var result = Route(["review", "cross-runtime", .. RequestArgs("opencode", workspace, outDir, OpencodeModel), "--opencode-provider-config", provider, "--format", "json"]);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(marker, result.Output, StringComparison.Ordinal);
        foreach (var file in new[]
                 {
                     CrossRuntimeReviewFiles.Prompt,
                     CrossRuntimeReviewFiles.Schema,
                     CrossRuntimeReviewFiles.Invocation,
                     CrossRuntimeReviewFiles.OpencodeReviewerConfig,
                 })
        {
            var content = File.ReadAllText(Path.Combine(outDir, file));
            if (file == CrossRuntimeReviewFiles.OpencodeReviewerConfig)
            {
                Assert.Contains(marker, content, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(marker, content, StringComparison.Ordinal);
            }
        }

        var insideProvider = Path.Combine(workspace, "provider.json");
        File.Copy(provider, insideProvider);
        var inside = Route(["review", "cross-runtime", .. RequestArgs("opencode", workspace, Path.Combine(root, "provider-inside-out"), OpencodeModel), "--opencode-provider-config", insideProvider, "--format", "json"]);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, JsonDocument.Parse(inside.Output).RootElement.GetProperty("cause").GetString());
        Assert.DoesNotContain(marker, inside.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("protected")]
    public void Request_ProviderConfig_RefusesSymlinkedAncestor(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var env = target == "protected" ? ScratchHome() : null;
        var marker = "SYMLINKED-PROVIDER-MARKER-G842";
        var targetRoot = target == "workspace"
            ? Path.Combine(root, "provider-symlink-workspace")
            : Path.Combine(env!.Home, ".config", "opencode");
        Directory.CreateDirectory(targetRoot);
        var targetProvider = Path.Combine(targetRoot, "provider.json");
        File.WriteAllText(targetProvider, JsonSerializer.Serialize(new { provider = new { local = new { options = new { apiKey = marker } } } }));

        var link = Path.Combine(root, "provider-symlink-ancestor-" + target);
        Directory.CreateSymbolicLink(link, targetRoot);
        var linkedProvider = Path.Combine(link, "provider.json");
        var touched = false;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = _ => touched = true;

        var result = Route([
            "review", "cross-runtime", .. RequestArgs("opencode", Path.Combine(root, "provider-symlink-workspace"),
                Path.Combine(root, "provider-symlink-out-" + target), OpencodeModel),
            "--opencode-provider-config", linkedProvider, "--format", "json"]);

        Assert.Equal(1, result.ExitCode);
        using var refusal = JsonDocument.Parse(result.Output);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(
            target == "protected"
                ? $"opencode provider config source '{linkedProvider}' resolves inside an operator-protected root."
                : $"opencode provider config source '{linkedProvider}' resolves inside the review workspace.",
            refusal.RootElement.GetProperty("detail").GetString());
        Assert.False(touched);
        Assert.DoesNotContain(marker, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_ProtectedRootSeamAllowsMetadataButNoEnumerationOrContentRead()
    {
        using var env = ScratchHome();
        var protectedRoot = Path.Combine(env.Home, ".config", "opencode");
        Directory.CreateDirectory(protectedRoot);
        var marker = "PROTECTED-MARKER-G842";
        var provider = Path.Combine(protectedRoot, "provider.json");
        File.WriteAllText(provider, JsonSerializer.Serialize(new { provider = new { local = new { options = new { apiKey = marker } } } }));
        var touched = false;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = path =>
        {
            touched = true;
            throw new InvalidOperationException("protected path was accessed: " + path);
        };
        try
        {
            var workspace = Path.Combine(root, "seam-workspace");
            var result = Route(["review", "cross-runtime", .. RequestArgs("opencode", workspace, Path.Combine(root, "seam-out"), OpencodeModel), "--opencode-provider-config", provider, "--format", "json"]);
            Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, JsonDocument.Parse(result.Output).RootElement.GetProperty("cause").GetString());
            Assert.False(touched);
            Assert.DoesNotContain(marker, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = null;
        }
    }

    [Theory]
    [InlineData("home-copilot")]
    [InlineData("copilot-home")]
    [InlineData("home-config-opencode")]
    [InlineData("xdg-config-opencode")]
    [InlineData("opencode-config-dir")]
    [InlineData("home-data-opencode")]
    [InlineData("xdg-data-opencode")]
    [InlineData("home-state-opencode")]
    [InlineData("xdg-state-opencode")]
    [InlineData("home-cache-opencode")]
    [InlineData("xdg-cache-opencode")]
    [InlineData("gh-config-dir")]
    public void Request_RefusesEveryProtectedRoot_ForWorkspaceAndPlannedPaths(string rootName)
    {
        using var env = ScratchHome();
        var protectedRoot = ConfigureProtectedRoot(env, rootName);
        foreach (var runtime in new[] { "copilot", "opencode" })
        {
            foreach (var kind in new[] { CrossRuntimeReviewRecord.KindImplementation, CrossRuntimeReviewRecord.KindDesign })
            {
                var workspaceInside = Path.Combine(protectedRoot, "review-workspace");
                var inside = RequestAt(runtime, kind, workspaceInside, Path.Combine(root, $"protected-inside-{rootName}-{runtime}-{kind}"), hasClone: true);
                AssertPathInvalid(inside);
                Assert.DoesNotContain(CrossRuntimeReviewCauses.OutDirNotEmpty, inside.Output, StringComparison.Ordinal);

                var workspaceContaining = Path.GetDirectoryName(protectedRoot)!;
                var containing = RequestAt(runtime, kind, workspaceContaining, Path.Combine(root, $"protected-containing-{rootName}-{runtime}-{kind}"), hasClone: true);
                AssertPathInvalid(containing);
                Assert.DoesNotContain(CrossRuntimeReviewCauses.OutDirNotEmpty, containing.Output, StringComparison.Ordinal);

                var planned = RequestAt(runtime, kind, Path.Combine(root, $"protected-planned-workspace-{rootName}-{runtime}-{kind}"), Path.Combine(protectedRoot, "planned-out"), hasClone: true);
                AssertPathInvalid(planned);
                Assert.DoesNotContain(CrossRuntimeReviewCauses.OutDirNotEmpty, planned.Output, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Request_AcceptsWorkspaceBesideProtectedRoots()
    {
        using var env = ScratchHome();
        var protectedRoot = Path.Combine(env.Home, ".config", "opencode");
        Directory.CreateDirectory(protectedRoot);
        var workspace = Path.Combine(env.Home, "review-workspace");
        var result = RequestAt("opencode", CrossRuntimeReviewRecord.KindImplementation, workspace, Path.Combine(root, "beside-protected"), hasClone: true);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Request_ResolvesProtectedRootThroughSymlink()
    {
        using var env = ScratchHome();
        var target = Path.Combine(root, "symlinked-gh-target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(env.Home, ".config", "gh");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, target);
        var result = RequestAt("opencode", CrossRuntimeReviewRecord.KindImplementation, Path.Combine(target, "clone"), Path.Combine(root, "symlinked-gh-out"), hasClone: true);
        AssertPathInvalid(result);
    }

    [Fact]
    public void Request_UsesGhProtectedRootResolutionOrder()
    {
        using var env = ScratchHome();
        var cases = new[]
        {
            ("explicit", "GH_CONFIG_DIR", Path.Combine(root, "gh-explicit"), Path.Combine(root, "gh-explicit")),
            ("xdg", "XDG_CONFIG_HOME", Path.Combine(root, "gh-xdg"), Path.Combine(root, "gh-xdg", "gh")),
            ("home", "HOME", env.Home, Path.Combine(env.Home, ".config", "gh")),
        };

        foreach (var (name, variable, value, protectedRoot) in cases)
        {
            SetEnv("GH_CONFIG_DIR", null);
            SetEnv("XDG_CONFIG_HOME", null);
            if (variable == "GH_CONFIG_DIR")
            {
                SetEnv(variable, value);
            }
            else if (variable == "XDG_CONFIG_HOME")
            {
                SetEnv(variable, value);
            }

            var result = RequestAt("copilot", CrossRuntimeReviewRecord.KindImplementation, protectedRoot, Path.Combine(root, "gh-order-" + name), hasClone: true);
            Assert.True(result.ExitCode == 1, $"{name}: {result.Output}");
            Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, JsonDocument.Parse(result.Output).RootElement.GetProperty("cause").GetString());
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void LegacyRuntimes_AcceptOutDirInsideProtectedRoot(string runtime)
    {
        using var env = ScratchHome();
        var protectedRoot = Path.Combine(env.Home, ".copilot");
        var outDir = Path.Combine(protectedRoot, "review", runtime);
        var result = RequestAt(runtime, CrossRuntimeReviewRecord.KindImplementation, Path.Combine(root, "legacy-clone-" + runtime), outDir, hasClone: true);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(CrossRuntimeReviewCauses.PathInvalid, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void LegacyRuntimes_AcceptOutDirUnderDotfileSymlinkedProtectedRoot(string runtime)
    {
        using var env = ScratchHome();
        var target = Path.Combine(root, "legacy-config-target-" + runtime);
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.Combine(env.Home, ".config"), target);
        var outDir = Path.Combine(env.Home, ".config", "opencode", "review");
        var result = RequestAt(runtime, CrossRuntimeReviewRecord.KindImplementation, Path.Combine(root, "legacy-clone-symlink-" + runtime), outDir, hasClone: true);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(CrossRuntimeReviewCauses.PathInvalid, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CursorDesignWithoutClone_KeepsWorkspaceForeignLikeMergeBase()
    {
        var outDir = Path.Combine(root, "cursor-design-legacy");
        var first = RequestAt("cursor", CrossRuntimeReviewRecord.KindDesign, Path.Combine(root, "ignored-workspace"), outDir, hasClone: false);
        Assert.Equal(0, first.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace)));

        Directory.CreateDirectory(Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace));
        var second = RequestAt("cursor", CrossRuntimeReviewRecord.KindDesign, Path.Combine(root, "ignored-workspace"), outDir, hasClone: false);
        Assert.Equal(1, second.ExitCode);
        using var refusal = JsonDocument.Parse(second.Output);
        Assert.Equal(CrossRuntimeReviewCauses.OutDirNotEmpty, refusal.RootElement.GetProperty("cause").GetString());
    }

    private string ConfigureProtectedRoot(ScratchHomeScope env, string rootName) =>
        rootName switch
        {
            "home-copilot" => Path.Combine(env.Home, ".copilot"),
            "copilot-home" => SetAndReturn("COPILOT_HOME", Path.Combine(root, "configured-copilot")),
            "home-config-opencode" => Path.Combine(env.Home, ".config", "opencode"),
            "xdg-config-opencode" => Path.Combine(SetAndReturn("XDG_CONFIG_HOME", Path.Combine(root, "configured-xdg-config")), "opencode"),
            "opencode-config-dir" => SetAndReturn("OPENCODE_CONFIG_DIR", Path.Combine(root, "configured-opencode-config-dir")),
            "home-data-opencode" => Path.Combine(env.Home, ".local", "share", "opencode"),
            "xdg-data-opencode" => Path.Combine(SetAndReturn("XDG_DATA_HOME", Path.Combine(root, "configured-xdg-data")), "opencode"),
            "home-state-opencode" => Path.Combine(env.Home, ".local", "state", "opencode"),
            "xdg-state-opencode" => Path.Combine(SetAndReturn("XDG_STATE_HOME", Path.Combine(root, "configured-xdg-state")), "opencode"),
            "home-cache-opencode" => Path.Combine(env.Home, ".cache", "opencode"),
            "xdg-cache-opencode" => Path.Combine(SetAndReturn("XDG_CACHE_HOME", Path.Combine(root, "configured-xdg-cache")), "opencode"),
            "gh-config-dir" => SetAndReturn("GH_CONFIG_DIR", Path.Combine(root, "configured-gh")),
            _ => throw new ArgumentOutOfRangeException(nameof(rootName), rootName, null),
        };

    private string SetAndReturn(string variable, string value)
    {
        SetEnv(variable, value);
        return value;
    }

    // ── helpers ───────────────────────────────────────────────────────

    private (int ExitCode, string Output) Request(string runtime, string outDir)
    {
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        return Route(["review", "cross-runtime", .. RequestArgs(runtime, Path.Combine(root, "clone"), outDir, model), "--format", "json"]);
    }

    private (int ExitCode, string Output) RequestAt(string runtime, string kind, string workspace, string outDir, bool hasClone)
    {
        var model = runtime == "copilot" ? CopilotModel : OpencodeModel;
        var args = kind == CrossRuntimeReviewRecord.KindDesign
            ? DesignRequestArgs(runtime, outDir, model, hasClone ? workspace : null)
            : RequestArgs(runtime, workspace, outDir, model);
        return Route(["review", "cross-runtime", .. args, "--format", "json"]);
    }

    private static void AssertWorkspaceSymlinkRefusal(
        (int ExitCode, string Output) result,
        string relativePath,
        string resolvedTarget)
    {
        Assert.Equal(1, result.ExitCode);
        using var refusal = JsonDocument.Parse(result.Output);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, refusal.RootElement.GetProperty("cause").GetString());
        var detail = refusal.RootElement.GetProperty("detail").GetString();
        Assert.Contains(relativePath, detail, StringComparison.Ordinal);
        Assert.DoesNotContain(resolvedTarget, detail, StringComparison.Ordinal);
    }

    private void AssertPathInvalid((int ExitCode, string Output) result)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(CrossRuntimeReviewCauses.PathInvalid, JsonDocument.Parse(result.Output).RootElement.GetProperty("cause").GetString());
    }

    private static string[] SnapshotOutDir(string outDir) =>
        Directory.EnumerateFileSystemEntries(outDir)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path => Directory.Exists(path)
                ? $"{Path.GetFileName(path)}:directory"
                : $"{Path.GetFileName(path)}:file:{Convert.ToBase64String(File.ReadAllBytes(path))}")
            .ToArray();

    private void AssertExitMissing(string[] args) =>
        AssertExitMissingBoundedAsync(args).GetAwaiter().GetResult();

    private async Task AssertExitMissingBoundedAsync(string[] args)
    {
        var routeTask = Task.Run(() => Route(["review", "cross-runtime", .. args, "--format", "json"]));
        var completed = await Task.WhenAny(routeTask, Task.Delay(RouteTimeout));
        Assert.Same(routeTask, completed);
        var (exit, output) = await routeTask;
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.ExitStatusMissing, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    private async Task AssertExitNonzeroBoundedAsync(string[] args)
    {
        var routeTask = Task.Run(() => Route(["review", "cross-runtime", .. args, "--format", "json"]));
        var completed = await Task.WhenAny(routeTask, Task.Delay(RouteTimeout));
        Assert.Same(routeTask, completed);
        var (exit, output) = await routeTask;
        Assert.Equal(1, exit);
        Assert.Equal(CrossRuntimeReviewCauses.ExitStatusNonzero, JsonDocument.Parse(output).RootElement.GetProperty("cause").GetString());
    }

    private ScratchHomeScope ScratchHome() => new(this);

    private void SetEnv(string name, string? value)
    {
        if (!savedEnv.ContainsKey(name))
        {
            savedEnv[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnv()
    {
        foreach (var (name, value) in savedEnv)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static ulong GetInode(string path)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return 0;
        }

        var buffer = new byte[144];
        if (lstat(path, buffer) != 0)
        {
            return 0;
        }

        var offset = OperatingSystem.IsMacOS() ? 8u : 8u;
        return BitConverter.ToUInt64(buffer, (int)offset);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int lstat(string pathname, byte[] buf);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkfifo(string pathname, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string existing, string newPath);

    [DllImport("libc", SetLastError = true)]
    private static extern int umask(int mask);

    private static void CreateHardLink(string linkPath, string existingPath)
    {
        if (link(existingPath, linkPath) != 0)
        {
            throw new IOException($"link failed for '{existingPath}' -> '{linkPath}'");
        }
    }

    private static int FindCompactionReopenIndex(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Contains("compaction_continue", StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("compaction reopen not found");
    }

    private static string MutateCompactionReopen(string fixture, string mutation)
    {
        var lines = File.ReadAllLines(Fixture(fixture)).ToList();
        var reopenIndex = FindCompactionReopenIndex(lines);
        return mutation switch
        {
            "no-synthetic" => MutateReopen(lines, reopenIndex, node => node["part"]!.AsObject().Remove("synthetic")),
            "no-metadata" => MutateReopen(lines, reopenIndex, node => node["part"]!.AsObject().Remove("metadata")),
            "cc-string" => MutateReopen(lines, reopenIndex, node => node["part"]!["metadata"]!["compaction_continue"] = "true"),
            "parttype-reasoning" => MutateReopen(lines, reopenIndex, node => node["part"]!["type"] = "reasoning"),
            "dup-reopen" => string.Join('\n', lines.Take(reopenIndex + 1).Append(lines[reopenIndex]).Concat(lines.Skip(reopenIndex + 1))),
            "reopen-inside-final-step" => InsertReopenInsideFinalStep(lines, reopenIndex),
            _ => string.Join('\n', lines),
        };
    }

    private static string MutateReopen(List<string> lines, int reopenIndex, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(lines[reopenIndex])!.AsObject();
        mutate(node);
        lines[reopenIndex] = node.ToJsonString();
        return string.Join('\n', lines);
    }

    private static string InsertReopenInsideFinalStep(List<string> lines, int reopenIndex)
    {
        var reopen = lines[reopenIndex];
        for (var index = lines.Count - 1; index >= reopenIndex; index--)
        {
            if (lines[index].Contains("\"type\":\"text\"", StringComparison.Ordinal) || lines[index].Contains("\"type\": \"text\"", StringComparison.Ordinal))
            {
                lines.Insert(index, reopen);
                break;
            }
        }

        return string.Join('\n', lines);
    }

    private string ExtractR3iFinalAnswer() => ExtractFinalAnswerFromCapture(R3I);

    private static string ExtractFinalAnswerFromCapture(string fixture)
    {
        var lines = File.ReadAllLines(Fixture(fixture));
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (!lines[index].Contains("\"type\":\"text\"", StringComparison.Ordinal) && !lines[index].Contains("\"type\": \"text\"", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonNode.Parse(lines[index])!.AsObject();
            if (node["part"]!.AsObject().TryGetPropertyValue("synthetic", out var synthetic) && synthetic is JsonValue value && value.GetValue<bool>())
            {
                continue;
            }

            return node["part"]!["text"]!.GetValue<string>();
        }

        throw new InvalidOperationException("final answer not found");
    }

    private static string MutateFenceForm(string answer, string mutation)
    {
        var lines = answer.Split('\n').ToList();
        var fenceStart = lines.FindIndex(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        var fenceEnd = lines.FindLastIndex(line => line.Trim() == "```");
        var inner = string.Join('\n', lines.Skip(fenceStart + 1).Take(fenceEnd - fenceStart - 1));
        return mutation switch
        {
            "as-captured" => answer,
            "plain-opener" => "```\n" + inner + "\n```",
            "no-narration" => "```json\n" + inner + "\n```",
            "trailing-blanks" => answer + "\n\n",
            "text-after-fence" => answer + "\ntrailing",
            "two-blocks" => answer + "\n```json\n" + inner + "\n```",
            "two-objects" => ReplaceFenceInner(answer, inner + "\n" + inner),
            "object-plus-prose" => ReplaceFenceInner(answer, inner + "\nprose"),
            "array" => ReplaceFenceInner(answer, "[{\"verdict\":\"approve\"}]"),
            "unterminated" => answer[..answer.LastIndexOf("```", StringComparison.Ordinal)],
            "yaml-opener" => answer.Replace("```json", "```yaml", StringComparison.Ordinal),
            _ => answer,
        };
    }

    private static string ReplaceFenceInner(string answer, string inner)
    {
        var lines = answer.Split('\n').ToList();
        var fenceStart = lines.FindIndex(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        var fenceEnd = lines.FindLastIndex(line => line.Trim() == "```");
        return string.Join('\n', lines.Take(fenceStart + 1).Concat(inner.Split('\n')).Concat(lines.Skip(fenceEnd)));
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            CrossRuntimeReview = new CrossRuntimeReviewConfig
            {
                Teams = [new CrossRuntimeReviewTeamDeclaration { Team = $"{Domain}/{Team}", ConductorRuntime = "claude", Repos = [Repo] }],
            },
        },
    };

    private (int ExitCode, string Output) Route(string[] args)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, Context(), writer);
        return (exit, writer.ToString());
    }

    private static string[] RequestArgs(string runtime, string clone, string outDir, string model) =>
        ["request", "--repo", Repo, "--pr", Pr.ToString(), "--head-sha", H1, "--execution-unit", Unit, "--runtime", runtime, "--clone", clone, "--out-dir", outDir, "--model", model];

    private static UnixFileMode ReadUnixMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return UnixFileMode.None;
        }

        return File.GetUnixFileMode(path) & (UnixFileMode)0x1ff;
    }

    private static string[] DesignRequestArgs(string runtime, string outDir, string model, string? clone = null)
    {
        var args = new List<string>
        {
            "request", "--kind", CrossRuntimeReviewRecord.KindDesign, "--execution-unit", Unit,
            "--runtime", runtime, "--out-dir", outDir, "--model", model,
        };
        if (clone is not null)
        {
            args.Add("--clone");
            args.Add(clone);
        }

        return args.ToArray();
    }

    private static string[] RecordArgs(string runtime, string verdictFile, string head, bool write)
    {
        var args = new List<string>
        {
            "record", "--repo", Repo, "--pr", Pr.ToString(), "--head-sha", head, "--kind", "implementation",
            "--runtime", runtime, "--runtime-version", "2.1.269", "--verdict-file", verdictFile, "--execution-unit", Unit,
            "--model", runtime == "copilot" ? CopilotModel : OpencodeModel,
        };
        if (write)
        {
            args.Add("--write");
        }

        return args.ToArray();
    }

    private string[] DesignRecordArgs(string runtime, string verdictFile, string digest, bool write)
    {
        var args = new List<string>
        {
            "record", "--kind", "design", "--execution-unit", Unit, "--packet-digest", digest,
            "--runtime", runtime, "--runtime-version", "2.1.269", "--verdict-file", verdictFile, "--model", OpencodeModel,
        };
        if (write)
        {
            args.Add("--write");
        }

        return args.ToArray();
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

    private string CurrentDigest() => CrossRuntimeDesignReviewDigest.ComputeFromDirectory(Path.Combine(root, ".intent-cli", "issues", Unit));

    private static string OpencodeImplementationEnvelope(string verdict, string head) =>
        RewriteOpencodeText(Fixture("opencode-approve.jsonl"), Verdict(verdict, head));

    private static string OpencodeDesignEnvelope(string verdict, string digest) =>
        RewriteOpencodeText(Fixture("opencode-approve.jsonl"), DesignVerdict(verdict, digest));

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

    private static string Verdict(string verdict, string head) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["verdict"] = verdict,
        ["head_sha"] = head,
        ["blocking_findings"] = verdict == "approve"
            ? new object[0]
            : new object[] { new Dictionary<string, object> { ["file"] = "src/A.cs", ["line"] = 12, ["scenario"] = "blocking" } },
        ["notes"] = verdict == "approve" ? new[] { "looks right" } : Array.Empty<string>(),
    });

    private static string DesignVerdict(string verdict, string digest) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["verdict"] = verdict,
        ["packet_digest"] = digest,
        ["blocking_findings"] = verdict == "approve"
            ? new object[0]
            : new object[] { new Dictionary<string, object> { ["file"] = "packet.yaml", ["line"] = 3, ["scenario"] = "blocking" } },
        ["notes"] = verdict == "approve" ? new[] { "looks right" } : Array.Empty<string>(),
    });

    private static string CursorEnvelope(string resultText)
    {
        var node = JsonNode.Parse(File.ReadAllText(FixtureG834("cursor-envelope.json")))!.AsObject();
        node["result"] = resultText;
        return node.ToJsonString();
    }

    private static string Fixture(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G842", name);

    private static string FixtureG834(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

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

    private sealed class ScratchHomeScope : IDisposable
    {
        private readonly G842CrossRuntimeReviewRound7Tests parent;
        public string Home { get; }

        public ScratchHomeScope(G842CrossRuntimeReviewRound7Tests parent)
        {
            this.parent = parent;
            Home = Path.Combine(parent.root, "scratch-home-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Home);
            parent.SetEnv("HOME", Home);
            parent.SetEnv("COPILOT_HOME", null);
            parent.SetEnv("XDG_CONFIG_HOME", null);
            parent.SetEnv("XDG_DATA_HOME", null);
            parent.SetEnv("XDG_STATE_HOME", null);
            parent.SetEnv("XDG_CACHE_HOME", null);
            parent.SetEnv("OPENCODE_CONFIG_DIR", null);
            parent.SetEnv("GH_CONFIG_DIR", null);
        }

        public void Dispose()
        {
            parent.SetEnv("HOME", parent.savedEnv.GetValueOrDefault("HOME"));
            parent.SetEnv("COPILOT_HOME", parent.savedEnv.GetValueOrDefault("COPILOT_HOME"));
            parent.SetEnv("XDG_CONFIG_HOME", parent.savedEnv.GetValueOrDefault("XDG_CONFIG_HOME"));
            parent.SetEnv("XDG_DATA_HOME", parent.savedEnv.GetValueOrDefault("XDG_DATA_HOME"));
            parent.SetEnv("XDG_STATE_HOME", parent.savedEnv.GetValueOrDefault("XDG_STATE_HOME"));
            parent.SetEnv("XDG_CACHE_HOME", parent.savedEnv.GetValueOrDefault("XDG_CACHE_HOME"));
            parent.SetEnv("OPENCODE_CONFIG_DIR", parent.savedEnv.GetValueOrDefault("OPENCODE_CONFIG_DIR"));
            parent.SetEnv("GH_CONFIG_DIR", parent.savedEnv.GetValueOrDefault("GH_CONFIG_DIR"));
        }
    }
}
