using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G451: coverage for the domain standing-policy registry — absent / valid /
/// invalid / ambiguous-high-risk cases, the safe-default fallback, and the
/// wiring through <c>guide review</c> (policy source + device-gated rules).
/// </summary>
public sealed class ReviewStandingPolicyRegistryTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private readonly string _root;
    private readonly CliContext _context;

    public ReviewStandingPolicyRegistryTests()
    {
        _root = Directory.CreateTempSubdirectory("review-policy-tests-").FullName;
        Directory.CreateDirectory(Path.Combine(_root, ".intent-cli"));
        _context = new CliContext
        {
            RepoRoot = _root,
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
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string PolicyPath => Path.Combine(_root, ".intent-cli", "review-policy.json");

    // ---- registry resolution ------------------------------------------------

    [Fact]
    public void Resolve_NoPolicyFile_ReturnsBuiltInDefaults()
    {
        var policy = ReviewStandingPolicyRegistry.Resolve(_context, "intent-cli");

        Assert.Equal(ReviewStandingPolicySources.BuiltInDefault, policy.Source);
        Assert.Empty(policy.Warnings);
        // Default device rules reproduce the prior G445 behavior verbatim.
        Assert.Equal(ReviewStandingPolicy.DefaultDeviceGatedEvidenceRules, policy.DeviceGatedEvidence.Rules);
        Assert.True(policy.DeviceGatedEvidence.ApproveWithRecordedGapAllowed);
        Assert.NotEmpty(policy.DraftHandling.Rules);
        Assert.NotEmpty(policy.ExternalArtifactIntake.Rules);
        Assert.NotEmpty(policy.TestEvidenceSufficiency.Rules);
        Assert.NotEmpty(policy.FollowUpTracking.Rules);
    }

    [Fact]
    public void Default_DraftHandling_PreservesInstalledDraftAwareFlow_NoRequestUpdateSolelyForDraft()
    {
        var draft = ReviewStandingPolicy.Default("intent-cli").DraftHandling.Rules;
        var joined = string.Join("\n", draft);

        // Draft state alone is NOT a stop and NOT a request-update reason.
        Assert.Contains(draft, r =>
            r.Contains("Draft state ALONE is not a review stop", StringComparison.Ordinal));
        Assert.Contains(draft, r =>
            r.Contains("never solely because the PR is draft", StringComparison.Ordinal));
        // It must NOT instruct "request the author mark it ready-for-review first"
        // as the default response to a draft (the regressed pre-fix behavior).
        Assert.DoesNotContain("Request the author mark it ready-for-review first", joined, StringComparison.Ordinal);
        // Approval/merge while the draft flag is set is still forbidden.
        Assert.Contains(draft, r =>
            r.Contains("NEVER approve or merge while the draft flag is still set", StringComparison.Ordinal));
        // The promote-then-approve path is referenced.
        Assert.Contains(draft, r => r.Contains("draft-ready-to-promote", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_ValidPolicyFile_AppliesOverrides_KeepsOmittedSectionDefaults()
    {
        File.WriteAllText(PolicyPath, """
            {
              "domain": "intent-cli",
              "draft_handling": { "rules": ["custom draft rule"] },
              "device_gated_evidence": {
                "approve_with_recorded_gap_allowed": false,
                "hard_block_categories": ["safety", "regulatory"]
              }
            }
            """);

        var policy = ReviewStandingPolicyRegistry.Resolve(_context, "intent-cli");

        Assert.Equal(ReviewStandingPolicySources.DomainFile, policy.Source);
        Assert.Empty(policy.Warnings);
        // Overridden sections.
        Assert.Equal(new[] { "custom draft rule" }, policy.DraftHandling.Rules);
        Assert.False(policy.DeviceGatedEvidence.ApproveWithRecordedGapAllowed);
        Assert.Contains("regulatory", policy.DeviceGatedEvidence.HardBlockCategories);
        // device rules omitted in the file → keep the safe default rules.
        Assert.Equal(ReviewStandingPolicy.DefaultDeviceGatedEvidenceRules, policy.DeviceGatedEvidence.Rules);
        // Omitted sections keep defaults (never dropped to empty).
        Assert.NotEmpty(policy.ExternalArtifactIntake.Rules);
        Assert.NotEmpty(policy.FollowUpTracking.Rules);
    }

    [Fact]
    public void Resolve_InvalidJson_FailsClosed_ToDefaultsWithWarning()
    {
        File.WriteAllText(PolicyPath, "{ this is not valid json ]");

        var policy = ReviewStandingPolicyRegistry.Resolve(_context, "intent-cli");

        Assert.Equal(ReviewStandingPolicySources.InvalidFallbackDefault, policy.Source);
        Assert.NotEmpty(policy.Warnings);
        // Defaults remain intact — no guidance is lost on an invalid file.
        Assert.Equal(ReviewStandingPolicy.DefaultDeviceGatedEvidenceRules, policy.DeviceGatedEvidence.Rules);
        Assert.NotEmpty(policy.DraftHandling.Rules);
    }

    [Fact]
    public void Resolve_EmptyFile_FailsClosed_ToDefaults()
    {
        File.WriteAllText(PolicyPath, "   ");

        var policy = ReviewStandingPolicyRegistry.Resolve(_context, "intent-cli");

        Assert.Equal(ReviewStandingPolicySources.InvalidFallbackDefault, policy.Source);
        Assert.NotEmpty(policy.Warnings);
    }

    [Fact]
    public void Resolve_HighRiskDeviceGap_HardBlock_WhenApproveWithGapDisabled()
    {
        // Ambiguous / high-risk configuration: a domain that forbids
        // approve-with-recorded-gap entirely must surface that deterministically.
        File.WriteAllText(PolicyPath, """
            {
              "device_gated_evidence": {
                "approve_with_recorded_gap_allowed": false,
                "hard_block_categories": ["safety", "security", "payment"]
              }
            }
            """);

        var policy = ReviewStandingPolicyRegistry.Resolve(_context, "intent-cli");

        Assert.False(policy.DeviceGatedEvidence.ApproveWithRecordedGapAllowed);
        Assert.Equal(3, policy.DeviceGatedEvidence.HardBlockCategories.Count);
    }

    [Fact]
    public void Resolve_PartialFile_EmptyRulesArray_KeepsDefaultForThatSection()
    {
        File.WriteAllText(PolicyPath, """
            { "draft_handling": { "rules": [] } }
            """);

        var policy = ReviewStandingPolicyRegistry.Resolve(_context, "intent-cli");

        // An empty rules array must not blank out the section — defaults stay.
        Assert.Equal(ReviewStandingPolicySources.DomainFile, policy.Source);
        Assert.NotEmpty(policy.DraftHandling.Rules);
    }

    // ---- guide review wiring ------------------------------------------------

    [Fact]
    public void GuideReview_NoPolicyFile_EmitsBuiltInDefaultSource_AndDefaultDeviceRules()
    {
        SeedReviewablePr();

        using var writer = new StringWriter();
        var exit = GuideReviewCommand.Execute(
            _context, ["--repo", Repo, "--pr", "598", "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            ReviewStandingPolicySources.BuiltInDefault,
            doc.RootElement.GetProperty("review_policy_source").GetString());
        var device = doc.RootElement.GetProperty("device_gated_evidence_policy")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(device, p => p.Contains("device-gap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GuideReview_ValidPolicyFile_FlowsOverriddenDeviceRules_AndDomainFileSource()
    {
        SeedReviewablePr();
        File.WriteAllText(PolicyPath, """
            {
              "device_gated_evidence": { "rules": ["DOMAIN-OVERRIDE device rule"] }
            }
            """);

        using var writer = new StringWriter();
        GuideReviewCommand.Execute(
            _context, ["--repo", Repo, "--pr", "598", "--format", "json"], writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            ReviewStandingPolicySources.DomainFile,
            doc.RootElement.GetProperty("review_policy_source").GetString());
        var device = doc.RootElement.GetProperty("device_gated_evidence_policy")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(new[] { "DOMAIN-OVERRIDE device rule" }, device);
    }

    [Fact]
    public void GuideReview_InvalidPolicyFile_StillSucceeds_WithFallbackSource()
    {
        SeedReviewablePr();
        File.WriteAllText(PolicyPath, "{ broken");

        using var writer = new StringWriter();
        var exit = GuideReviewCommand.Execute(
            _context, ["--repo", Repo, "--pr", "598", "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            ReviewStandingPolicySources.InvalidFallbackDefault,
            doc.RootElement.GetProperty("review_policy_source").GetString());
        // Device rules fall back to the safe defaults.
        var device = doc.RootElement.GetProperty("device_gated_evidence_policy")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(device, p => p.Contains("HARD-BLOCK", StringComparison.Ordinal));
    }

    [Fact]
    public void GuideReview_Markdown_IncludesStandingPolicySection_G451()
    {
        SeedReviewablePr();

        using var writer = new StringWriter();
        GuideReviewCommand.Execute(
            _context, ["--repo", Repo, "--pr", "598", "--format", "markdown"], writer);

        var output = writer.ToString();
        Assert.Contains("## Review standing policy (G451)", output, StringComparison.Ordinal);
        Assert.Contains("### Draft handling", output, StringComparison.Ordinal);
        Assert.Contains("### Follow-up tracking", output, StringComparison.Ordinal);
    }

    private void SeedReviewablePr()
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = "G248",
                    Title = "guide review",
                    State = QueueItemState.Review,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = ".intent-cli/issues/G248/packet.yaml",
                        Implementation = ".intent-cli/issues/G248/implementation.md",
                        ReviewContext = ".intent-cli/issues/G248/review-context.md",
                    },
                    LinkedIssue = null,
                    LinkedPr = "598",
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal",
                },
            },
        };
        File.WriteAllText(_context.GetQueueStatePath(), QueueStateSerializer.Serialize(state));
        var packetDir = Path.Combine(_root, ".intent-cli", "issues", "G248");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), "x");
    }
}
