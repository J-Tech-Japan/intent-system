using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G848ProviderConfigModeTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G848";
    private const int Pr = 1848;
    private const string Head = "8484848484848484848484848484848484848484";
    private const string Model = "github-copilot/gpt-5.6-sol";
    private const string ModeDetail = "file '{0}' has mode {1}; a provider config may carry credentials, so group and other must have no permission bits.";
    private const string ModeFix = "chmod 600 '{0}', or remove every group and other permission bit.";

    private readonly string root = Directory.CreateTempSubdirectory("g848-provider-mode-").FullName;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };

    public G848ProviderConfigModeTests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return claims.TryGetValue(unit, out var team)
                ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", team ?? string.Empty, "held")
                : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
        };
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigOpen = null;
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigRead = null;
        CrossRuntimeReviewOpencodeConfig.AfterProviderConfigValidation = null;
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = null;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = null;
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
        WriteQueue();
        WritePacket();
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigOpen = null;
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigRead = null;
        CrossRuntimeReviewOpencodeConfig.AfterProviderConfigValidation = null;
        CrossRuntimeReviewHomeAccessGuard.ShouldRefusePath = null;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("implementation", 0x1A4, false)] // 0644
    [InlineData("implementation", 0x1A0, false)] // 0640
    [InlineData("implementation", 0x184, false)] // 0604
    [InlineData("implementation", 0x1B0, false)] // 0660
    [InlineData("implementation", 0x188, false)] // 0610: group execute
    [InlineData("implementation", 0x180, true)]  // 0600
    [InlineData("implementation", 0x100, true)]  // 0400
    [InlineData("design", 0x1A4, false)]
    [InlineData("design", 0x1A0, false)]
    [InlineData("design", 0x184, false)]
    [InlineData("design", 0x1B0, false)]
    [InlineData("design", 0x188, false)]
    [InlineData("design", 0x180, true)]
    [InlineData("design", 0x100, true)]
    public void Request_ProviderConfigModeMatrix_RefusesSharedBitsBeforeWriting(
        string kind,
        int mode,
        bool accepted)
    {
        if (OperatingSystem.IsWindows()) return;
        var marker = $"apiKey-G848-{kind}-{mode}";
        var provider = WriteProvider("matrix-" + kind + "-" + mode, marker, mode);
        var outDir = Path.Combine(root, "matrix-out-" + kind + "-" + mode);
        Directory.CreateDirectory(outDir);
        var before = Snapshot(outDir);

        var result = Route(RequestArgs(kind, provider, outDir, "json"));
        Assert.Equal(accepted ? 0 : 1, result.ExitCode);
        Assert.DoesNotContain(marker, result.Output, StringComparison.Ordinal);

        if (accepted)
        {
            Assert.Contains(marker, File.ReadAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig)), StringComparison.Ordinal);
            return;
        }

        AssertModeRefusal(result.Output, provider, mode);
        Assert.Equal(before, Snapshot(outDir));
    }

    [Theory]
    [InlineData("implementation")]
    [InlineData("design")]
    public void Request_ProviderConfigMode_0204RefusesBeforeOpenAndKeepsOutDir(string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = WriteProvider("ordering-" + kind, "apiKey-G848-ordering", 0x84); // 0204
        var outDir = Path.Combine(root, "ordering-out-" + kind);
        Directory.CreateDirectory(outDir);
        var before = Snapshot(outDir);

        var result = Route(RequestArgs(kind, provider, outDir, "json"));

        Assert.Equal(1, result.ExitCode);
        AssertModeRefusal(result.Output, provider, 0x84);
        Assert.DoesNotContain("could not be read", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey-G848-ordering", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(outDir));
    }

    [Theory]
    [InlineData("implementation", 0x180, true)]
    [InlineData("implementation", 0x1A4, false)]
    [InlineData("design", 0x180, true)]
    [InlineData("design", 0x1A4, false)]
    public void Request_ProviderConfigSymlink_UsesFinalTargetMode(string kind, int targetMode, bool accepted)
    {
        if (OperatingSystem.IsWindows()) return;
        var target = WriteProvider("link-target-" + kind + "-" + targetMode, "apiKey-G848-link", targetMode);
        var link = Path.Combine(root, "provider-link-" + kind + "-" + targetMode);
        File.CreateSymbolicLink(link, target);
        var outDir = Path.Combine(root, "link-out-" + kind + "-" + targetMode);
        Directory.CreateDirectory(outDir);
        var before = Snapshot(outDir);

        var result = Route(RequestArgs(kind, link, outDir, "json"));

        Assert.Equal(accepted ? 0 : 1, result.ExitCode);
        if (accepted)
        {
            Assert.Contains("apiKey-G848-link", File.ReadAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig)), StringComparison.Ordinal);
        }
        else
        {
            AssertModeRefusal(result.Output, link, targetMode);
            Assert.Equal(before, Snapshot(outDir));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void Request_ProviderConfigModeRefusal_PinsDetailAndFix(string format)
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = WriteProvider("pinned-" + format, "apiKey-G848-pinned", 0x1A4);
        var outDir = Path.Combine(root, "pinned-out-" + format);
        Directory.CreateDirectory(outDir);

        var result = Route(RequestArgs("implementation", provider, outDir, format));

        Assert.Equal(1, result.ExitCode);
        if (format == "json")
        {
            AssertModeRefusal(result.Output, provider, 0x1A4);
        }
        else
        {
            Assert.Contains(string.Format(ModeDetail, provider, "0644"), result.Output, StringComparison.Ordinal);
            Assert.Contains(string.Format(ModeFix, provider), result.Output, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("apiKey-G848-pinned", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("implementation")]
    [InlineData("design")]
    public void Request_ProviderConfigDirectory_KeepsCouldNotBeReadRefusal(string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = Path.Combine(root, "provider-directory-" + kind);
        Directory.CreateDirectory(provider);
        var outDir = Path.Combine(root, "directory-out-" + kind);
        Directory.CreateDirectory(outDir);
        var before = Snapshot(outDir);

        var result = Route(RequestArgs(kind, provider, outDir, "json"));

        Assert.Equal(1, result.ExitCode);
        using var refusal = JsonDocument.Parse(result.Output);
        Assert.Equal(CrossRuntimeReviewCauses.OpencodeProviderConfigInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Contains("could not be read", refusal.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(outDir));
    }

    [Fact]
    public void Request_ProviderConfigHandleRecheck_RefusesPathSwapAfterPrecheck()
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = WriteProvider("handle-swap", "apiKey-G848-original", 0x180);
        var replacement = WriteProvider("handle-replacement", "apiKey-G848-replacement", 0x1A4);
        var outDir = Path.Combine(root, "handle-swap-out");
        Directory.CreateDirectory(outDir);
        var before = Snapshot(outDir);
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigOpen = path => File.Move(replacement, path, overwrite: true);

        var result = Route(RequestArgs("implementation", provider, outDir, "json"));

        Assert.Equal(1, result.ExitCode);
        AssertModeRefusal(result.Output, provider, 0x1A4);
        Assert.DoesNotContain("apiKey-G848-replacement", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(outDir));
    }

    [Fact]
    public void Request_ProviderConfigMissingAtPrecheck_StillGetsHandleModeRefusalAfterSwap()
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = Path.Combine(root, "provider-missing-at-precheck.json");
        var replacement = WriteProvider("missing-replacement", "apiKey-G848-missing-replacement", 0x1A4);
        var outDir = Path.Combine(root, "missing-swap-out");
        Directory.CreateDirectory(outDir);
        var before = Snapshot(outDir);
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigOpen = path => File.Move(replacement, path);

        var result = Route(RequestArgs("implementation", provider, outDir, "json"));

        Assert.Equal(1, result.ExitCode);
        AssertModeRefusal(result.Output, provider, 0x1A4);
        Assert.DoesNotContain("could not be read", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(outDir));
    }

    [Fact]
    public void Request_ProviderConfigValidatedBlock_IsRenderedAfterSourceSwap()
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = WriteProvider("validated", "apiKey-G848-validated", 0x180);
        var replacement = WriteProvider("validated-replacement", "apiKey-G848-late", 0x1A4);
        var outDir = Path.Combine(root, "validated-out");
        CrossRuntimeReviewOpencodeConfig.AfterProviderConfigValidation = path => File.Move(replacement, path, overwrite: true);

        var result = Route(RequestArgs("implementation", provider, outDir, "json"));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("apiKey-G848-validated", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey-G848-late", result.Output, StringComparison.Ordinal);
        var rendered = File.ReadAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig));
        Assert.Contains("apiKey-G848-validated", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey-G848-late", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_ProviderConfigRead_UsesOpenedHandleAfterPathSwap()
    {
        if (OperatingSystem.IsWindows()) return;
        var provider = WriteProvider("read-swap", "apiKey-G848-read-original", 0x180);
        var replacement = WriteProvider("read-replacement", "apiKey-G848-read-replacement", 0x180);
        var outDir = Path.Combine(root, "read-swap-out");
        CrossRuntimeReviewOpencodeConfig.BeforeProviderConfigRead = path => File.Move(replacement, path, overwrite: true);

        var result = Route(RequestArgs("implementation", provider, outDir, "json"));

        Assert.Equal(0, result.ExitCode);
        var rendered = File.ReadAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig));
        Assert.Contains("apiKey-G848-read-original", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey-G848-read-replacement", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_ProviderConfigValidatedPathSwapIntoProtectedRoot_IsNotReadAgain()
    {
        if (OperatingSystem.IsWindows()) return;
        var home = Path.Combine(root, "home");
        var protectedRoot = Path.Combine(home, ".config", "opencode");
        Directory.CreateDirectory(protectedRoot);
        using var homeScope = new EnvironmentVariableScope("HOME", home);
        var provider = WriteProvider("protected-swap", "apiKey-G848-protected-original", 0x180);
        var protectedProvider = Path.Combine(protectedRoot, "provider.json");
        File.WriteAllText(protectedProvider, "{\"provider\":{\"local\":{\"options\":{\"apiKey\":\"apiKey-G848-protected\"}}}}");
        SetMode(protectedProvider, 0x180);
        var outDir = Path.Combine(root, "protected-swap-out");
        var touched = false;
        CrossRuntimeReviewHomeAccessGuard.ProtectedPathAccessProbe = _ => touched = true;
        CrossRuntimeReviewOpencodeConfig.AfterProviderConfigValidation = path =>
        {
            File.Delete(path);
            File.CreateSymbolicLink(path, protectedProvider);
        };

        var result = Route(RequestArgs("implementation", provider, outDir, "json"));

        Assert.Equal(0, result.ExitCode);
        Assert.False(touched);
        Assert.Contains("apiKey-G848-protected-original", File.ReadAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig)), StringComparison.Ordinal);
        Assert.DoesNotContain("\"apiKey\": \"apiKey-G848-protected\"", File.ReadAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig)), StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGuard_UnixModeChecksAreInsideNonWindowsBranch_AndUseHandle()
    {
        var sourcePath = Path.Combine(RepoVersionPolicySource.RepoRoot(), "src", "IntentSystem.Cli", "Commands", "CrossRuntimeReviewOpencodeConfig.cs");
        var source = File.ReadAllText(sourcePath);
        var modeBranch = Regex.Match(
            source,
            @"if \(!OperatingSystem\.IsWindows\(\)\).*?File\.GetUnixFileMode\(handle\)",
            RegexOptions.Singleline);
        Assert.True(modeBranch.Success, "the Unix mode check must be inside the !OperatingSystem.IsWindows() branch");
        Assert.DoesNotContain("File.ReadAllBytes(path)", modeBranch.Value, StringComparison.Ordinal);
        Assert.Contains("File.ResolveLinkTarget(path, returnFinalTarget: true)", source, StringComparison.Ordinal);
        Assert.Contains("RandomAccess.Read(handle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideAndDocs_PinProviderSourceModeRuleAndCleanup()
    {
        const string guideText = "--opencode-provider-config must name a source outside the workspace and outside every operator-protected root, with no group or other permission bits (request refuses one that has any); a source extracted for one run is deleted after record --write, as the out-dir is.";
        var guide = GuideSoloConductorCommand.BuildGuide().Loop.Single(step => step.Number == 7).Instruction;
        Assert.Contains(guideText, guide, StringComparison.Ordinal);

        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var documentation = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("--opencode-provider-config", documentation, StringComparison.Ordinal);
            Assert.Contains("group and other", documentation, StringComparison.Ordinal);
            Assert.Contains("record --write", documentation, StringComparison.Ordinal);
            Assert.Contains("<nnnn>", documentation, StringComparison.Ordinal);
        }

        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));
        Assert.DoesNotMatch(new Regex(@"[A-Za-z]+(?:し|します|した|して|され|せず|しない)"), japanese);
    }

    private (int ExitCode, string Output) Route(string[] args)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, Context(), writer);
        return (exit, writer.ToString());
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            CrossRuntimeReview = new CrossRuntimeReviewConfig
            {
                Teams =
                [
                    new CrossRuntimeReviewTeamDeclaration
                    {
                        Team = $"{Domain}/{Team}",
                        ConductorRuntime = "opencode",
                        Repos = [Repo],
                    },
                ],
            },
        },
    };

    private string[] RequestArgs(string kind, string provider, string outDir, string format)
    {
        var args = kind == CrossRuntimeReviewRecord.KindDesign
            ? new List<string>
            {
                "review", "cross-runtime", "request", "--kind", "design", "--execution-unit", Unit,
                "--runtime", "opencode", "--out-dir", outDir, "--clone", Path.Combine(root, "design-clone-" + Path.GetFileName(outDir)),
                "--model", Model,
            }
            : new List<string>
            {
                "review", "cross-runtime", "request", "--repo", Repo, "--pr", Pr.ToString(), "--head-sha", Head,
                "--execution-unit", Unit, "--runtime", "opencode", "--clone", Path.Combine(root, "clone-" + Path.GetFileName(outDir)),
                "--out-dir", outDir, "--model", Model,
            };
        args.AddRange(["--opencode-provider-config", provider, "--format", format]);
        return args.ToArray();
    }

    private string WriteProvider(string name, string marker, int mode)
    {
        var path = Path.Combine(root, name + ".json");
        File.WriteAllText(path, $"{{\"provider\":{{\"local\":{{\"options\":{{\"apiKey\":\"{marker}\"}}}}}}}}");
        SetMode(path, mode);
        return path;
    }

    private static void SetMode(string path, int mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, (UnixFileMode)mode);
        }
    }

    private static void AssertModeRefusal(string output, string path, int mode)
    {
        using var refusal = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.OpencodeProviderConfigInvalid, refusal.RootElement.GetProperty("cause").GetString());
        Assert.Equal(string.Format(ModeDetail, path, Convert.ToString(mode, 8).PadLeft(4, '0')), refusal.RootElement.GetProperty("detail").GetString());
        Assert.Equal(string.Format(ModeFix, path), refusal.RootElement.GetProperty("fix").GetString());
    }

    private static string[] Snapshot(string path) =>
        Directory.EnumerateFileSystemEntries(path)
            .OrderBy(entry => Path.GetFileName(entry), StringComparer.Ordinal)
            .Select(entry => Directory.Exists(entry)
                ? $"{Path.GetFileName(entry)}|directory"
                : $"{Path.GetFileName(entry)}|file|{Convert.ToBase64String(File.ReadAllBytes(entry))}")
            .ToArray();

    private void WriteQueue()
    {
        var queue = new
        {
            schema_version = "1",
            updated_at = "2026-09-22T00:00:00+00:00",
            items = new[]
            {
                new Dictionary<string, object?>
                {
                    ["execution_unit"] = Unit,
                    ["title"] = Unit,
                    ["state"] = "active",
                    ["dependencies"] = Array.Empty<string>(),
                    ["blocked_by"] = Array.Empty<string>(),
                    ["clarification_return_path"] = "intents/intent-cli/clarifications/open.md",
                    ["packet_paths"] = new { implementation = $".intent-cli/issues/{Unit}/implementation.md", review_context = $".intent-cli/issues/{Unit}/review-context.md", yaml = $".intent-cli/issues/{Unit}/packet.yaml" },
                    ["linked_issue"] = new { repo = Repo, number = Pr, url = $"https://github.com/{Repo}/issues/{Pr}" },
                    ["linked_pr"] = $"https://github.com/{Repo}/pull/{Pr}",
                    ["worker_role"] = "builder",
                    ["review_role"] = "reviewer",
                    ["priority"] = "high",
                },
            },
        };
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), JsonSerializer.Serialize(queue));
    }

    private void WritePacket()
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", Unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), $"implementation_issue_packet:\n  issue_title: \"G848\"\n  domain: {Domain}\n  target_repo: {Repo}\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G848\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string name;
        private readonly string? previous;

        public EnvironmentVariableScope(string name, string value)
        {
            this.name = name;
            previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }
}
