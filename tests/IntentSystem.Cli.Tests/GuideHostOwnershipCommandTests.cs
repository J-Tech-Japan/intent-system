using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G326: tests for the read-only `intent-cli guide host-ownership`
/// surface. Verifies the canonical role list, the may-write /
/// must-not-write matrix, focus-role filtering, and the
/// controller-friendly JSON shape.
/// </summary>
public sealed class GuideHostOwnershipCommandTests
{
    [Fact]
    public void Execute_DefaultJson_ReturnsAllFourCanonicalRoles()
    {
        using var writer = new StringWriter();
        var exit = GuideHostOwnershipCommand.Execute(CreateContext(), Array.Empty<string>(), writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var roles = doc.RootElement.GetProperty("roles");
        Assert.Equal(4, roles.GetArrayLength());
        var ids = roles.EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("design", ids);
        Assert.Contains("review-runtime", ids);
        Assert.Contains("child-worker", ids);
        Assert.Contains("ambiguous", ids);
    }

    [Fact]
    public void Execute_EveryRoleHasMatrixFields()
    {
        using var writer = new StringWriter();
        var exit = GuideHostOwnershipCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        foreach (var role in doc.RootElement.GetProperty("roles").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(role.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(role.GetProperty("summary").GetString()));
            Assert.True(role.GetProperty("responsibilities").GetArrayLength() >= 1);
            // ambiguous role has empty may_write by design; every other
            // role has at least one entry.
            var id = role.GetProperty("id").GetString()!;
            if (id == "ambiguous")
            {
                Assert.Equal(0, role.GetProperty("may_write").GetArrayLength());
            }
            else
            {
                Assert.True(role.GetProperty("may_write").GetArrayLength() >= 1);
            }
            Assert.True(role.GetProperty("must_not_write").GetArrayLength() >= 1);
        }
    }

    [Theory]
    [InlineData("design")]
    [InlineData("review-runtime")]
    [InlineData("child-worker")]
    [InlineData("ambiguous")]
    public void Execute_FocusRole_ReturnsOnlyThatRole(string role)
    {
        using var writer = new StringWriter();
        var exit = GuideHostOwnershipCommand.Execute(
            CreateContext(),
            new[] { "--role", role, "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(role, doc.RootElement.GetProperty("focus_role").GetString());
        // Roles list still carries the full matrix (markdown filters
        // by focus_role; JSON exposes both the focus pointer and the
        // matrix so controllers can reason about cross-role
        // boundaries).
        Assert.Equal(4, doc.RootElement.GetProperty("roles").GetArrayLength());
    }

    [Fact]
    public void Execute_UnknownRoleValue_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exit = GuideHostOwnershipCommand.Execute(
            CreateContext(),
            new[] { "--role", "garbage", "--format", "json" },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--role 'garbage' did not resolve", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownDefault_FiltersToFocusRoleSection()
    {
        using var writer = new StringWriter();
        var exit = GuideHostOwnershipCommand.Execute(
            CreateContext(),
            new[] { "--role", "child-worker", "--format", "markdown" },
            writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("## `child-worker`", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## `design`", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## `review-runtime`", output, StringComparison.Ordinal);
        Assert.Contains("Must NOT write", output, StringComparison.Ordinal);
    }

    [Fact]
    public void HostOwnershipModel_ChildWorkerForbidsParentHostStateWrites()
    {
        // G326 acceptance: the write matrix MUST forbid child workers
        // from writing parent host state. This is a direct read on the
        // pure model so the contract can't drift from intent without
        // the test failing first.
        var childWorker = HostOwnershipModel.Roles
            .Single(r => r.Id == HostOwnershipModel.RoleChildWorker);

        Assert.Contains(childWorker.MustNotWrite,
            entry => entry.Contains(".intent-cli/queue-state.json", StringComparison.Ordinal));
        Assert.Contains(childWorker.MustNotWrite,
            entry => entry.Contains(".intent-cli/runs.jsonl", StringComparison.Ordinal));
        // The implementation repo MUST NOT contain `.intent-cli/`
        // (G300 reference).
        Assert.Contains(childWorker.MustNotWrite,
            entry => entry.Contains("MUST NOT contain a `.intent-cli/`", StringComparison.Ordinal));
    }

    [Fact]
    public void HostOwnershipModel_DesignAndReviewRuntimeBothOwnNextSliceCreation()
    {
        // G326 acceptance: the model explicitly allows design-side AND
        // review-runtime-side NextSlice creation with different
        // ownership labels (design produces prepared packets;
        // review-runtime publishes them and may produce runtime-created
        // packets recorded as such).
        var design = HostOwnershipModel.Roles.Single(r => r.Id == HostOwnershipModel.RoleDesign);
        var review = HostOwnershipModel.Roles.Single(r => r.Id == HostOwnershipModel.RoleReviewRuntime);

        Assert.Contains(design.Responsibilities,
            r => r.Contains("Prepare NextSlice", StringComparison.OrdinalIgnoreCase)
                || r.Contains("prepared packet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Responsibilities,
            r => r.Contains("Publish the next-slice", StringComparison.OrdinalIgnoreCase)
                || r.Contains("runtime-created NextSlice", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("design", "design")]
    [InlineData("Design", "design")]
    [InlineData("intent-design", "design")]
    [InlineData("review-runtime", "review-runtime")]
    [InlineData("review", "review-runtime")]
    [InlineData("runtime", "review-runtime")]
    [InlineData("child", "child-worker")]
    [InlineData("worker", "child-worker")]
    [InlineData("implementer", "child-worker")]
    [InlineData("garbage", "ambiguous")]
    [InlineData("", "ambiguous")]
    public void ResolveRole_NormalizesAliasToCanonicalIdentifier(string raw, string expected)
    {
        Assert.Equal(expected, HostOwnershipModel.ResolveRole(raw));
    }

    // --- G326: setup contract surfaces host_role -----------------------------

    [Fact]
    public void GuideAutomationSetup_ChildImplementWithoutFlag_InfersChildWorkerHostRole()
    {
        // G326 acceptance: when the operator did not pass --host-role,
        // a `child-implement` purpose (which implies cwd_role=child)
        // resolves to host_role=child-worker.
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            new[] { "--purpose", "child-implement", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("child-worker", doc.RootElement.GetProperty("host_role").GetString());
    }

    [Fact]
    public void GuideAutomationSetup_HostReviewWithoutFlag_SurfacesAmbiguous()
    {
        // G326 acceptance: a host cwd (host-review-next-slice purpose)
        // cannot be disambiguated between design and review-runtime
        // without an explicit --host-role flag. The setup contract MUST
        // say `ambiguous` so the operator names the role before any
        // role-scoped mutation guidance is acted on.
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            new[]
            {
                "--purpose", "host-review-next-slice",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("ambiguous", doc.RootElement.GetProperty("host_role").GetString());
    }

    [Fact]
    public void GuideAutomationSetup_ExplicitHostRoleFlag_RespectsOperatorChoice()
    {
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            new[]
            {
                "--purpose", "host-review-next-slice",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--host-role", "review-runtime",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("review-runtime", doc.RootElement.GetProperty("host_role").GetString());
    }

    [Fact]
    public void GuideAutomationSetup_UnknownHostRoleValue_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            new[]
            {
                "--purpose", "child-implement",
                "--host-role", "garbage",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--host-role 'garbage' did not resolve", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
