using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G711: release npm publication must use job-scoped GitHub Actions OIDC
/// trusted publishing. The workflow source is the contract boundary for the
/// permission, toolchain, credential absence, and classified publish
/// failure that ordinary unit tests cannot exercise against npmjs.com.
/// </summary>
public sealed class NpmTrustedPublishingG711Tests
{
    private static readonly string[] PackageNames =
    [
        "intent-system",
        "@j-tech-japan/intent-cli-darwin-arm64",
        "@j-tech-japan/intent-cli-linux-x64",
        "@j-tech-japan/intent-cli-win32-x64",
    ];

    [Fact]
    public void Workflow_UsesJobScopedOidcAndPinnedNodeAndNpmWithoutCredentialPaths()
    {
        var workflow = ReadWorkflow();
        var npmJob = ExtractNpmJob(workflow);

        Assert.DoesNotContain("id-token: write", workflow[..workflow.IndexOf("\n  npm:", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("permissions:\n      contents: write\n      id-token: write", npmJob, StringComparison.Ordinal);
        Assert.Contains("node-version: 22.14.0", npmJob, StringComparison.Ordinal);
        Assert.Contains("npm install --global npm@11.5.1", npmJob, StringComparison.Ordinal);
        Assert.Contains("test \"$(npm --version)\" = \"11.5.1\"", npmJob, StringComparison.Ordinal);
        Assert.DoesNotContain("NPM_TOKEN", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("NODE_AUTH_TOKEN", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.NPM", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_ClassifiesTheActualFailureAndNamesTheClearingAct()
    {
        var workflow = ReadWorkflow();
        var npmJob = ExtractNpmJob(workflow);

        AssertTrustedPublishingFailureContract(npmJob);
        foreach (var packageName in PackageNames)
        {
            Assert.Contains(packageName, npmJob, StringComparison.Ordinal);
        }

        Assert.Contains("if: ${{ github.event_name == 'release' }}", npmJob, StringComparison.Ordinal);
        Assert.DoesNotContain("exit 0", npmJob, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowGuardRejectsTheFormerExitZeroSilentSkipShape()
    {
        var workflow = ReadWorkflow();
        var invalid = workflow.Replace(
            "npm registry/provenance rejection for package",
            "NPM_TOKEN is not set; skipping npm publish.\n            exit 0\n            npm registry/provenance rejection for package",
            StringComparison.Ordinal);

        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertTrustedPublishingFailureContract(ExtractNpmJob(invalid)));

        Assert.Contains("exit 0", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OperatorDocsDescribePerPackageRegistrationProvenanceAndFailure(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var path = Path.Combine(root, "docs", language, "13-npm-distribution.md");
        var document = File.ReadAllText(path);

        Assert.Contains("G711", document, StringComparison.Ordinal);
        Assert.Contains("22.14.0", document, StringComparison.Ordinal);
        Assert.Contains("11.5.1", document, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/intent-system", document, StringComparison.Ordinal);
        Assert.Contains(".github/workflows/release.yml", document, StringComparison.Ordinal);
        Assert.Contains("id-token: write", document, StringComparison.Ordinal);
        Assert.Contains("npmjs.com", document, StringComparison.Ordinal);
        Assert.Contains("trusted", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provenance", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dry run", document, StringComparison.OrdinalIgnoreCase);

        foreach (var packageName in PackageNames)
        {
            Assert.Contains(packageName, document, StringComparison.Ordinal);
        }

        if (language == "en")
        {
            Assert.Contains("one-time", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stores no npm", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fails with the package name", document, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("一度だけ", document, StringComparison.Ordinal);
            Assert.Contains("保存しません", document, StringComparison.Ordinal);
            Assert.Contains("対象 package 名", document, StringComparison.Ordinal);
        }
    }

    private static void AssertTrustedPublishingFailureContract(string npmJob)
    {
        Assert.Contains("publish_package()", npmJob, StringComparison.Ordinal);
        Assert.Contains("if ! publish_output=\"$(npm publish", npmJob, StringComparison.Ordinal);
        Assert.Contains("classify_publish_failure()", npmJob, StringComparison.Ordinal);
        Assert.Contains("npm registry/provenance rejection for package", npmJob, StringComparison.Ordinal);
        Assert.Contains("the authenticated publish reached npm", npmJob, StringComparison.Ordinal);
        Assert.Contains("repository.url matches https://github.com/J-Tech-Japan/intent-system", npmJob, StringComparison.Ordinal);
        Assert.Contains("npm trusted publishing authentication failure for package", npmJob, StringComparison.Ordinal);
        Assert.Contains("registering this package's npmjs.com trusted publisher", npmJob, StringComparison.Ordinal);
        Assert.Contains("${GITHUB_REPOSITORY}", npmJob, StringComparison.Ordinal);
        Assert.Contains(".github/workflows/release.yml", npmJob, StringComparison.Ordinal);
        Assert.Contains("id-token: write", npmJob, StringComparison.Ordinal);
        Assert.Contains("then rerun this release publish", npmJob, StringComparison.Ordinal);
        Assert.DoesNotContain("exit 0", npmJob, StringComparison.Ordinal);
    }

    private static string ReadWorkflow()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        return File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
    }

    private static string ExtractNpmJob(string workflow)
    {
        var match = Regex.Match(
            workflow,
            @"(?ms)^  npm:\s*\n(?<job>.*?)(?=^  [A-Za-z0-9_-]+:\s*$|\z)");
        Assert.True(match.Success, "release workflow must define an npm job");
        return match.Groups["job"].Value;
    }
}
