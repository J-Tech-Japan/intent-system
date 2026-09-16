using System.Globalization;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed partial class G839ByteIdentityTests
{
    internal static string CapturePrTransition(string fixtureId)
    {
        using var scope = new PrTransitionSeams(fixtureId);
        return scope.Capture(fixtureId);
    }

    private sealed class PrTransitionSeams : IDisposable
    {
    private readonly string root = Directory.CreateTempSubdirectory("g839-pr-transition-").FullName;
    private readonly Dictionary<string, string?> claims = new(StringComparer.Ordinal) { [G839ByteIdentityHarness.Unit] = G839ByteIdentityHarness.Team };

        public PrTransitionSeams(string fixtureId)
        {
            CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
            {
                var unit = scope["execution-unit:".Length..];
                return claims.TryGetValue(unit, out var team)
                    ? new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", team ?? string.Empty, "held")
                    : new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusUnheld, scope, true, null, null, null, "unheld");
            };
            AutomationPrTransitionCommand.MutatorFactory = () => new G839ByteIdentityHarness.RecordingPrMutator { Labels = ["intent-target", "intent-pr-reviewing"] };
            AutomationPrTransitionCommand.PrHeadReader = null;
            AutomationPrTransitionCommand.NestedProviderLauncher = null;

            Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
            if (fixtureId.Contains("ungated", StringComparison.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(root, ".intent-cli", "config.toml"),
                    "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
            }
            else if (fixtureId.Contains("undeclared", StringComparison.Ordinal))
            {
                claims[G839ByteIdentityHarness.Unit] = "some-other-team";
                File.WriteAllText(
                    Path.Combine(root, ".intent-cli", "config.toml"),
                    "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n\n"
                    + "[[cross_runtime_review.teams]]\n"
                    + "team = \"intent-cli/intent-cli-dev\"\n"
                    + "conductor_runtime = \"claude\"\n"
                    + "repos = [\"J-Tech-Japan/intent-system\"]\n");
                WriteQueue((G839ByteIdentityHarness.Unit, $"https://github.com/{G839ByteIdentityHarness.Repo}/pull/{G839ByteIdentityHarness.Pr}"));
                WritePacket(G839ByteIdentityHarness.Unit, G839ByteIdentityHarness.Domain);
            }
            else if (fixtureId.StartsWith("pr-transition-refusal-", StringComparison.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(root, ".intent-cli", "config.toml"),
                    "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n\n"
                    + "[[cross_runtime_review.teams]]\n"
                    + "team = \"intent-cli/intent-cli-dev\"\n"
                    + "conductor_runtime = \"claude\"\n"
                    + "repos = [\"J-Tech-Japan/intent-system\"]\n");
                WriteQueue((G839ByteIdentityHarness.Unit, $"https://github.com/{G839ByteIdentityHarness.Repo}/pull/{G839ByteIdentityHarness.Pr}"));
                WritePacket(G839ByteIdentityHarness.Unit, G839ByteIdentityHarness.Domain);
            }
            else
            {
                File.WriteAllText(
                    Path.Combine(root, ".intent-cli", "config.toml"),
                    "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
            }
        }

        public string Capture(string fixtureId)
        {
            ConfigurePrTransitionScenario(fixtureId);
            var args = BuildPrTransitionArgs(fixtureId);
            using var writer = new StringWriter();
            var exit = CommandRouter.Execute(args, Context(), writer);
            if (exit != 0 && fixtureId.Contains("failure", StringComparison.Ordinal))
            {
                return writer.ToString();
            }

            if (exit != 0 && fixtureId.StartsWith("pr-transition-refusal-", StringComparison.Ordinal))
            {
                return writer.ToString();
            }

            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"pr-transition capture for '{fixtureId}' exited {exit}: {writer}");
            }

            return writer.ToString();
        }

        public void Dispose()
        {
            CrossRuntimeReviewTeamResolver.ClaimReader = null;
            AutomationPrTransitionCommand.MutatorFactory = null;
            AutomationPrTransitionCommand.PrHeadReader = null;
            AutomationPrTransitionCommand.NestedProviderLauncher = null;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

    private CliContext Context() => new()
        {
            RepoRoot = root,
            Config = CliConfigLoader.Load(File.ReadAllText(Path.Combine(root, ".intent-cli", "config.toml"))),
        };

    private void ConfigurePrTransitionScenario(string fixtureId)
        {
            var mutator = new G839ByteIdentityHarness.RecordingPrMutator
            {
                Labels = ["intent-pr-reviewing", "intent-target"],
            };
            AutomationPrTransitionCommand.MutatorFactory = () => mutator;

            switch (fixtureId)
            {
                case "pr-transition-refusal-head-required-text":
                case "pr-transition-refusal-head-stale-text":
                case "pr-transition-refusal-team-unresolved-text":
                case "pr-transition-refusal-missing-text":
                case "pr-transition-refusal-blocked-text":
                    ConfigureRefusalScenario(fixtureId);
                    break;
                case "pr-transition-failure-may-have-applied-write-json":
                    AutomationPrTransitionCommand.MutatorFactory = () => new G839ByteIdentityHarness.ThrowingPrMutator
                    {
                        Labels = ["intent-target", "intent-pr-rereview-ready"],
                        ReplaceLabelSetFailureCertainty = LabelSetReplacementFailureCertainty.MayHaveApplied,
                    };
                    break;
                case "pr-transition-review-release-write-text":
                    AutomationPrTransitionCommand.MutatorFactory = () => new G839ByteIdentityHarness.RecordingPrMutator
                    {
                        Labels = ["intent-target", "intent-pr-reviewing"],
                    };
                    break;
                case "pr-transition-approved-ungated-dry-run-json":
                    AutomationPrTransitionCommand.MutatorFactory = () => new G839ByteIdentityHarness.RecordingPrMutator
                    {
                        Labels = ["intent-target", "intent-pr-reviewing"],
                    };
                    break;
                default:
                    AutomationPrTransitionCommand.MutatorFactory = () => new G839ByteIdentityHarness.RecordingPrMutator
                    {
                        Labels = ["intent-target", "intent-pr-reviewing"],
                    };
                    break;
            }
        }

    private void ConfigureRefusalScenario(string fixtureId)
        {
            AutomationPrTransitionCommand.PrHeadReader = (_, _) =>
                fixtureId == "pr-transition-refusal-head-stale-text" ? G839ByteIdentityHarness.H3 : G839ByteIdentityHarness.H2;

            switch (fixtureId)
            {
                case "pr-transition-refusal-missing-text":
                    RecordVerdict("claude", "approve", G839ByteIdentityHarness.H2);
                    break;
                case "pr-transition-refusal-blocked-text":
                    RecordVerdict("claude", "approve", G839ByteIdentityHarness.H2);
                    RecordVerdict("codex", "request-changes", G839ByteIdentityHarness.H2);
                    break;
                case "pr-transition-refusal-team-unresolved-text":
                    claims.Remove(G839ByteIdentityHarness.Unit);
                    break;
            }
        }

    private static string[] BuildPrTransitionArgs(string fixtureId)
        {
            var transition = fixtureId switch
            {
                var id when id.Contains("review-start", StringComparison.Ordinal) => "review-start",
                var id when id.Contains("request-update", StringComparison.Ordinal) => "request-update",
                var id when id.Contains("review-release", StringComparison.Ordinal) => "review-release",
                _ => "approved",
            };

            var repo = fixtureId.Contains("ungated", StringComparison.Ordinal) ? G839ByteIdentityHarness.UngatedRepo : G839ByteIdentityHarness.Repo;
            var args = new List<string>
            {
                "automation", "pr-transition",
                "--repo", repo,
                "--pr", G839ByteIdentityHarness.Pr.ToString(CultureInfo.InvariantCulture),
                "--transition", transition,
            };

            if (fixtureId.EndsWith("-json", StringComparison.Ordinal))
            {
                args.AddRange(["--format", "json"]);
            }
            else if (fixtureId.EndsWith("-text", StringComparison.Ordinal))
            {
                args.AddRange(["--format", "text"]);
            }

            if (fixtureId.Contains("-write-", StringComparison.Ordinal))
            {
                args.Add("--write");
            }

            if (fixtureId.StartsWith("pr-transition-refusal-", StringComparison.Ordinal)
                && fixtureId != "pr-transition-refusal-head-required-text")
            {
                args.AddRange(["--head-sha", G839ByteIdentityHarness.H2]);
            }

            return args.ToArray();
        }

    private void WriteQueue((string Unit, string? LinkedPr) item)
        {
            var queuePath = Path.Combine(root, ".intent-cli", "queue-state.json");
            var linkedPrJson = item.LinkedPr is null ? "null" : $"\"{item.LinkedPr}\"";
            File.WriteAllText(queuePath, $$"""
                {
                  "schema_version": "1",
                  "updated_at": "{{G839ByteIdentityHarness.FixedNow:O}}",
                  "items": [
                    {
                      "execution_unit": "{{item.Unit}}",
                      "title": "{{item.Unit}} title",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "",
                      "packet_paths": {
                        "yaml": ".intent-cli/issues/{{item.Unit}}/packet.yaml",
                        "implementation": ".intent-cli/issues/{{item.Unit}}/implementation.md",
                        "review_context": ".intent-cli/issues/{{item.Unit}}/review-context.md"
                      },
                      "linked_issue": {
                        "repo": "{{G839ByteIdentityHarness.Repo}}",
                        "number": {{G839ByteIdentityHarness.Issue}},
                        "url": "https://github.com/{{G839ByteIdentityHarness.Repo}}/issues/{{G839ByteIdentityHarness.Issue}}"
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

    private void WritePacket(string unit, string? domain)
        {
            var directory = Path.Combine(root, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "packet.yaml"),
                "implementation_issue_packet:\n  issue_title: \"G839\"\n"
                + (domain is null ? string.Empty : $"  domain: {domain}\n")
                + $"  target_repo: {G839ByteIdentityHarness.Repo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G839\n");
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
        }

    private void RecordVerdict(string runtime, string verdict, string head)
        {
            var recordDir = CrossRuntimeReviewPaths.PrDirectory(root, G839ByteIdentityHarness.Repo, G839ByteIdentityHarness.Pr);
            Directory.CreateDirectory(recordDir);
            var recordFile = Path.Combine(recordDir, $"{runtime}-{head[..7]}.json");
            File.WriteAllText(recordFile, JsonSerializer.Serialize(new
            {
                artifact_kind = "cross-runtime-review-record",
                repo = G839ByteIdentityHarness.Repo,
                pr = G839ByteIdentityHarness.Pr,
                head_sha = head,
                execution_unit = G839ByteIdentityHarness.Unit,
                domain = G839ByteIdentityHarness.Domain,
                team = G839ByteIdentityHarness.Team,
                kind = "implementation",
                runtime,
                runtime_version = "fixture",
                conductor_runtime = "claude",
                relation = "cross-runtime",
                verdict,
                blocking_findings = Array.Empty<string>(),
                notes = string.Empty,
                recorded_at = "2026-09-16T12:00:00Z",
                raw_verdict_file = "fixture.json",
                raw_verdict_sha256 = G839ByteIdentityHarness.H2,
            }));
        }
    }
}
