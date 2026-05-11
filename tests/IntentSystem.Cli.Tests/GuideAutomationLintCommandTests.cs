using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G322: tests for the read-only `intent-cli guide automation lint`
/// surface. Verifies that G320 generated contracts pass, that
/// intentionally-incomplete contracts fail with precise missing-clause
/// identifiers, and that the JSON output is controller-friendly.
/// </summary>
public sealed class GuideAutomationLintCommandTests
{
    [Fact]
    public void Lint_G320ChildLoopContract_Passes()
    {
        // G322 acceptance: a generated G320 child-loop contract MUST pass
        // lint out of the box. Regenerate it inline from
        // GuideAutomationSetupCommand so the test breaks if either the
        // contract content drifts or the lint clauses tighten.
        var contract = GenerateChildContract();

        var result = GuideAutomationContractLinter.Lint(contract);

        Assert.Equal("pass", result.Status);
        Assert.Empty(result.MissingClauses);
    }

    [Fact]
    public void Lint_G320HostLoopContract_Passes()
    {
        var contract = GenerateHostContract();

        var result = GuideAutomationContractLinter.Lint(contract);

        Assert.Equal("pass", result.Status);
        Assert.Empty(result.MissingClauses);
    }

    [Fact]
    public void Lint_ContractMissingNoRawLabelMutation_FailsWithPreciseClause()
    {
        // G322 acceptance: a contract that omits the "no raw `gh` label
        // mutation" prohibition fails lint with `no-raw-label-mutation`
        // in `missing_clauses`.
        var contract = GenerateChildContract().Replace(
            "No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.",
            "",
            StringComparison.Ordinal);

        var result = GuideAutomationContractLinter.Lint(contract);

        Assert.Equal("fail", result.Status);
        Assert.Contains(result.MissingClauses, m => m.Id == "no-raw-label-mutation");
    }

    [Fact]
    public void Lint_ContractMissingSameThreadScheduling_FailsWithPreciseClause()
    {
        // G322 acceptance: a contract that does not name a same-thread /
        // local-automation scheduling mechanism fails lint.
        var contract = "Some unrelated automation setup text without any scheduling guidance.";

        var result = GuideAutomationContractLinter.Lint(contract);

        Assert.Equal("fail", result.Status);
        Assert.Contains(result.MissingClauses, m => m.Id == "same-thread-scheduling");
    }

    [Fact]
    public void Lint_ContractThatAllowsDotnetRunFallback_FailsClause()
    {
        // G322 acceptance: a contract that does NOT include the explicit
        // "Do not run `dotnet run`" prohibition fails the
        // `no-dotnet-run-fallback` clause — a bare `dotnet run` mention
        // in a permissive context must NOT silently pass.
        const string contract = @"
Some automation setup that says:
- You may use `dotnet run` to invoke the CLI if `intent-cli` is unavailable.
- Same-thread `/loop 5m` is the scheduling mechanism.
- automation doctor is OK; stale-host-cli would abort.
";

        var result = GuideAutomationContractLinter.Lint(contract);

        Assert.Contains(result.MissingClauses, m => m.Id == "no-dotnet-run-fallback");
    }

    [Fact]
    public void Lint_ContractThatDescribesIntentCliRunWithoutProhibition_FailsClause()
    {
        // G322 review fix (PR #748): a contract that merely describes
        // `intent-cli run` as "the advanced runtime path" (or otherwise
        // permissively mentions it) MUST fail the `no-intent-cli-run`
        // clause. Only an explicit negative prohibition counts as
        // satisfying the safety clause. Mirrors the
        // `Lint_ContractThatAllowsDotnetRunFallback_FailsClause`
        // permissive-contract test for the `intent-cli run` surface.
        const string contract = @"
Some automation setup that says:
- You may use `intent-cli run` if you want the advanced runtime path.
- Same-thread `/loop 5m` is the scheduling mechanism.
- automation doctor is OK; stale-host-cli would abort the wake.
- Do not run `dotnet run` as a fallback.
- No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Do not ask `intent-cli` to launch Claude/Codex.
- worker next-action / worker claim / worker complete drive dispatch.
- stop with `idle` when next-action returns none.
- Do not read `intents/rules/**` or copied prompt files.
";

        var result = GuideAutomationContractLinter.Lint(contract);

        Assert.Equal("fail", result.Status);
        Assert.Contains(result.MissingClauses, m => m.Id == "no-intent-cli-run");
    }

    [Fact]
    public void Lint_JsonOutput_HasControllerFriendlyShape()
    {
        // G322 acceptance: JSON output exposes status / found_clauses /
        // missing_clauses / recommended_regeneration_command so a
        // controller can gate on `status` without parsing markdown.
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLintCommand.Execute(
            CreateContext(),
            ["--text", "broken contract with nothing in it", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode); // exit is always 0; gate on status field
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("fail", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("missing_clauses").GetArrayLength() > 0);
        Assert.NotNull(root.GetProperty("recommended_regeneration_command").GetString());
        // Each missing clause has id + description + required_any.
        var firstMiss = root.GetProperty("missing_clauses")[0];
        Assert.False(string.IsNullOrEmpty(firstMiss.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrEmpty(firstMiss.GetProperty("description").GetString()));
        Assert.True(firstMiss.GetProperty("required_any").GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_MissingFromFileAndText_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLintCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-file", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--text", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BothFromFileAndText_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLintCommand.Execute(
            CreateContext(),
            ["--from-file", "/tmp/whatever", "--text", "inline", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not both", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FromFileMissing_ReportsReadError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLintCommand.Execute(
            CreateContext(),
            ["--from-file", "/does/not/exist/whatever.md", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("failed to read", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideAutomationCommand_LintSubcommand_IsRouted()
    {
        // G322: `intent-cli guide automation lint ...` must reach the
        // lint command via the existing guide-automation router so the
        // CLI surface is discoverable.
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["lint", "--text", "broken", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("fail", doc.RootElement.GetProperty("status").GetString());
    }

    private static string GenerateChildContract()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "child-implement",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--frequency", "5m",
                "--format", "markdown"
            ],
            writer);
        Assert.Equal(0, exitCode);
        return writer.ToString();
    }

    private static string GenerateHostContract()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "host-review-next-slice",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--frequency", "5m",
                "--format", "markdown"
            ],
            writer);
        Assert.Equal(0, exitCode);
        return writer.ToString();
    }

    private static CliContext CreateContext() =>
        new()
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
