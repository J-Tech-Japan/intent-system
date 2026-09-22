using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G842: byte identity for the legacy codex/claude/cursor request, record, and
/// status surfaces against merge base a24cf8ab. The capture host is entirely
/// synthetic; only the temporary host root is normalized.
/// </summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G842BackCompatGoldenTests
{
    private static readonly string FixtureRoot = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        "tests",
        "IntentSystem.Cli.Tests",
        "Fixtures",
        "G842",
        "base");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void LegacyCrossRuntimeSurfaces_MatchMergeBaseGoldens(string host, string kind, string runtime)
    {
        using var capture = new G842BackCompatCapture(host, kind, runtime);
        var actual = capture.CaptureAll();

        foreach (var artifact in actual)
        {
            var expectedPath = Path.Combine(FixtureRoot, host, kind, runtime, artifact.Key);
            Assert.True(File.Exists(expectedPath), $"missing G842 base golden: {expectedPath}");
            Assert.Equal(File.ReadAllText(expectedPath), artifact.Value);
        }
    }

    [Fact(Skip = "Run only in detached merge-base a24cf8ab; never capture on head.")]
    public void CaptureMergeBaseGoldens()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("G842_CAPTURE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(FixtureRoot);
        foreach (var scenario in Scenarios().Select(row => (Host: (string)row[0]!, Kind: (string)row[1]!, Runtime: (string)row[2]!)))
        {
            using var capture = new G842BackCompatCapture(scenario.Host, scenario.Kind, scenario.Runtime);
            foreach (var artifact in capture.CaptureAll())
            {
                var path = Path.Combine(FixtureRoot, scenario.Host, scenario.Kind, scenario.Runtime, artifact.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, artifact.Value);
            }
        }
    }

    public static TheoryData<string, string, string> Scenarios()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var host in new[] { "no-declaration", "claude-conductor" })
        {
            foreach (var kind in new[] { "implementation", "design" })
            {
                foreach (var runtime in new[] { "codex", "claude", "cursor" })
                {
                    data.Add(host, kind, runtime);
                }
            }
        }

        return data;
    }
}

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
internal sealed class G842BackCompatCapture : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G842";
    private const int Pr = 1812;
    private const string Head = "1111111111111111111111111111111111111111";
    private const string Placeholder = "{{G842_WORKSPACE_ROOT}}";
    private readonly string root = Directory.CreateTempSubdirectory("g842-backcompat-").FullName;
    private readonly string host;
    private readonly string kind;
    private readonly string runtime;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [Unit] = Team };

    internal G842BackCompatCapture(string host, string kind, string runtime)
    {
        this.host = host;
        this.kind = kind;
        this.runtime = runtime;

        G842CrossRuntimeReviewTests.ConfigureG842ClaimReader((_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return claims.TryGetValue(unit, out var team)
                ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", team ?? string.Empty, "held")
                : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
        });
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), ConfigText());
        WriteQueue();
        WritePacket();
        Directory.CreateDirectory(Path.Combine(root, "clone"));
    }

    internal Dictionary<string, string> CaptureAll()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["request.json"] = RunRequest("json", "request-json"),
            ["request.md"] = RunRequest("markdown", "request-markdown"),
            ["record.json"] = RunRecord("json"),
            ["record-comment.md"] = RecordComment(),
            ["status.json"] = RunStatus("json"),
            ["status.md"] = RunStatus("markdown"),
        };

        var renderedOut = Path.Combine(root, "request-json");
        foreach (var file in CrossRuntimeReviewFiles.Rendered)
        {
            result[file] = Normalize(File.ReadAllText(Path.Combine(renderedOut, file)));
        }

        return result;
    }

    public void Dispose()
    {
        G842CrossRuntimeReviewTests.ConfigureG842ClaimReader(null);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string RunRequest(string format, string outputName)
    {
        var outDir = Path.Combine(root, outputName);
        string[] args = kind == CrossRuntimeReviewRecord.KindDesign
            ? ["review", "cross-runtime", "request", "--kind", "design", "--execution-unit", Unit, "--runtime", runtime, "--out-dir", outDir, "--format", format]
            : ["review", "cross-runtime", "request", "--repo", Repo, "--pr", Pr.ToString(CultureInfo.InvariantCulture), "--head-sha", Head, "--execution-unit", Unit, "--runtime", runtime, "--clone", Path.Combine(root, "clone"), "--out-dir", outDir, "--format", format];
        return Execute(args);
    }

    private string RunRecord(string format)
    {
        var verdictPath = Path.Combine(root, "runs", runtime + "-" + kind + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(verdictPath)!);
        File.WriteAllText(verdictPath, VerdictEnvelope());
        string[] args = kind == CrossRuntimeReviewRecord.KindDesign
            ? ["review", "cross-runtime", "record", "--kind", "design", "--execution-unit", Unit, "--packet-digest", CurrentDigest(), "--runtime", runtime, "--runtime-version", "2.1.269", "--verdict-file", verdictPath, "--format", format]
            : ["review", "cross-runtime", "record", "--repo", Repo, "--pr", Pr.ToString(CultureInfo.InvariantCulture), "--head-sha", Head, "--kind", "implementation", "--runtime", runtime, "--runtime-version", "2.1.269", "--verdict-file", verdictPath, "--execution-unit", Unit, "--format", format];
        return Execute(args, host == "no-declaration" ? 1 : 0);
    }

    private string RecordComment()
    {
        var json = RunRecord("json");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("comment_body", out var comment)
            ? Normalize(comment.GetString() ?? string.Empty)
            : string.Empty;
    }

    private string RunStatus(string format)
    {
        string[] args = kind == CrossRuntimeReviewRecord.KindDesign
            ? ["review", "cross-runtime", "status", "--kind", "design", "--execution-unit", Unit, "--format", format]
            : ["review", "cross-runtime", "status", "--repo", Repo, "--pr", Pr.ToString(CultureInfo.InvariantCulture), "--head-sha", Head, "--execution-unit", Unit, "--format", format];
        return Execute(args);
    }

    private string Execute(string[] args, int expectedExitCode = 0)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, Context(), writer);
        Assert.Equal(expectedExitCode, exit);
        return Normalize(writer.ToString());
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = CliConfigLoader.Load(File.ReadAllText(Path.Combine(root, ".intent-cli", "config.toml"))),
    };

    private string ConfigText() => host == "claude-conductor"
        ? "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n\n"
            + "[[cross_runtime_review.teams]]\n"
            + "team = \"intent-cli/intent-cli-dev\"\n"
            + "conductor_runtime = \"claude\"\n"
            + "repos = [\"J-Tech-Japan/intent-system\"]\n"
        : "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n";

    private void WriteQueue()
    {
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-09-14T00:00:00+00:00",
              "items": [
                {
                  "execution_unit": "{{Unit}}",
                  "title": "G842 back-compat fixture",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "",
                  "packet_paths": {
                    "yaml": ".intent-cli/issues/{{Unit}}/packet.yaml",
                    "implementation": ".intent-cli/issues/{{Unit}}/implementation.md",
                    "review_context": ".intent-cli/issues/{{Unit}}/review-context.md"
                  },
                  "linked_issue": {
                    "repo": "{{Repo}}",
                    "number": 1842,
                    "url": "https://github.com/{{Repo}}/issues/1842"
                  },
                  "linked_pr": "https://github.com/{{Repo}}/pull/{{Pr}}",
                  "worker_role": "builder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
    }

    private void WritePacket()
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", Unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            "implementation_issue_packet:\n  issue_title: \"G842 back-compat fixture\"\n  domain: intent-cli\n  target_repo: J-Tech-Japan/intent-system\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G842 back-compat fixture\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review context\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# implementation notes\n");
    }

    private string CurrentDigest() => CrossRuntimeDesignReviewDigest.ComputeFromDirectory(Path.Combine(root, ".intent-cli", "issues", Unit));

    private string VerdictEnvelope()
    {
        var verdict = kind == CrossRuntimeReviewRecord.KindDesign
            ? JsonSerializer.Serialize(new { verdict = "approve", packet_digest = CurrentDigest(), blocking_findings = Array.Empty<object>(), notes = new[] { "base fixture" } })
            : JsonSerializer.Serialize(new { verdict = "approve", head_sha = Head, blocking_findings = Array.Empty<object>(), notes = new[] { "base fixture" } });
        return runtime switch
        {
            "claude" => ClaudeEnvelope(verdict),
            "cursor" => CursorEnvelope(verdict),
            _ => verdict,
        };
    }

    private static string ClaudeEnvelope(string verdict)
    {
        var node = JsonNode.Parse(File.ReadAllText(Fixture("claude-envelope.json")))!.AsObject();
        node["structured_output"] = JsonNode.Parse(verdict);
        return node.ToJsonString();
    }

    private static string CursorEnvelope(string verdict)
    {
        var node = JsonNode.Parse(File.ReadAllText(Fixture("cursor-envelope.json")))!.AsObject();
        node["result"] = verdict;
        return node.ToJsonString();
    }

    private static string Fixture(string name) => Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

    private string Normalize(string value)
    {
        var normalized = value.Replace(root, Placeholder, StringComparison.Ordinal);
        var forwardRoot = root.Replace('\\', '/');
        if (!string.Equals(root, forwardRoot, StringComparison.Ordinal))
        {
            normalized = normalized.Replace(forwardRoot, Placeholder, StringComparison.Ordinal);
        }

        normalized = Regex.Replace(
            normalized,
            @"20\d{2}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})",
            "{{G842_TIMESTAMP}}",
            RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"20\d{6}T\d{7,}Z",
            "{{G842_COMPACT_TIMESTAMP}}",
            RegexOptions.CultureInvariant);
        return normalized;
    }
}
