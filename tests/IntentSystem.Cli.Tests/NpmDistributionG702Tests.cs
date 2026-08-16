using System.Text.Json;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G702: the npm distribution is a product interface, not just a release
/// implementation detail. These source-contract tests keep the package family,
/// shim behavior, release gate, and bilingual operator documentation aligned.
/// Packed-install behavior is exercised by packaging/npm/scripts/smoke-packed.js
/// in CI; this class makes the contract visible to the normal .NET suite too.
/// </summary>
public sealed class NpmDistributionG702Tests
{
    private static readonly (string Directory, string Name, string Os, string Cpu, string Rid)[] Platforms =
    [
        ("darwin-arm64", "@j-tech-japan/intent-cli-darwin-arm64", "darwin", "arm64", "osx-arm64"),
        ("linux-x64", "@j-tech-japan/intent-cli-linux-x64", "linux", "x64", "linux-x64"),
        ("win32-x64", "@j-tech-japan/intent-cli-win32-x64", "win32", "x64", "win-x64"),
    ];

    [Fact]
    public void PackageFamily_DeclaresThinMainShimAndExactlyThreeOptionalPlatformPackages()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        using var document = ReadJson(Path.Combine(root, "packaging", "npm", "package.json"));
        var package = document.RootElement;

        Assert.Equal("intent-system", package.GetProperty("name").GetString());
        Assert.Equal("bin/intent-cli.js", package.GetProperty("bin").GetProperty("intent-cli").GetString());
        Assert.False(HasScript(package, "postinstall"));

        var dependencies = package.GetProperty("optionalDependencies");
        Assert.Equal(Platforms.Length, dependencies.EnumerateObject().Count());
        foreach (var platform in Platforms)
        {
            Assert.Equal("0.0.0-dev", dependencies.GetProperty(platform.Name).GetString());
            var templatePath = Path.Combine(root, "packaging", "npm", "platforms", platform.Directory, "package.json");
            using var template = ReadJson(templatePath);
            Assert.Equal(platform.Name, template.RootElement.GetProperty("name").GetString());
            Assert.Equal("0.0.0-dev", template.RootElement.GetProperty("version").GetString());
            Assert.Equal(platform.Os, template.RootElement.GetProperty("os")[0].GetString());
            Assert.Equal(platform.Cpu, template.RootElement.GetProperty("cpu")[0].GetString());
            Assert.Equal(platform.Rid, template.RootElement.GetProperty("intentCli").GetProperty("platform").GetString());
            Assert.Equal("0.0.0-dev", template.RootElement.GetProperty("intentCli").GetProperty("version").GetString());
            Assert.Equal("__BINARY_SHA256__", template.RootElement.GetProperty("intentCli").GetProperty("binarySha256").GetString());
            Assert.False(HasScript(template.RootElement, "postinstall"));
        }
    }

    [Fact]
    public void Shim_UsesNpmUserAgentAndPathAbsence_WithoutAnInstallSideEffect()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var shimPath = Path.Combine(root, "packaging", "npm", "bin", "intent-cli.js");
        var shim = File.ReadAllText(shimPath);

        Assert.Contains("npm_config_user_agent", shim, StringComparison.Ordinal);
        Assert.Contains("process.env.PATH", shim, StringComparison.Ordinal);
        Assert.Contains("npm install -g intent-system", shim, StringComparison.Ordinal);
        Assert.Contains("intent-cli", shim, StringComparison.Ordinal);
        Assert.DoesNotContain("npm install", shim.Replace("npm install -g intent-system", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("postinstall", shim, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ci_IsPackOnlyAndRelease_NpmPublishUsesTheSameTagReleaseGateAsNuGet()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.Contains("npm-dry-run", ci, StringComparison.Ordinal);
        Assert.Contains("npm pack", ci, StringComparison.Ordinal);
        Assert.Contains("smoke-packed.js", ci, StringComparison.Ordinal);
        Assert.Contains("verify-packages.js", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("npm publish", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("NPM_TOKEN", ci, StringComparison.Ordinal);

        Assert.Contains("github.event.release.tag_name", release, StringComparison.Ordinal);
        Assert.Contains("VERSION=\"${RAW#v}\"", release, StringComparison.Ordinal);
        Assert.Contains("needs: [nupkg, binaries]", release, StringComparison.Ordinal);
        Assert.DoesNotContain("NPM_TOKEN", release, StringComparison.Ordinal);
        Assert.DoesNotContain("NODE_AUTH_TOKEN", release, StringComparison.Ordinal);
        Assert.Contains("id-token: write", release, StringComparison.Ordinal);
        Assert.Contains("node-version: 22.14.0", release, StringComparison.Ordinal);
        Assert.Contains("npm@11.5.1", release, StringComparison.Ordinal);
        Assert.Contains("OIDC trusted publishing", release, StringComparison.Ordinal);
        Assert.Contains("npm publish", release, StringComparison.Ordinal);
        Assert.Contains("if: ${{ github.event_name == 'release' }}", release, StringComparison.Ordinal);
        Assert.Contains("NUGET_API_KEY", release, StringComparison.Ordinal);
        Assert.Contains("--require-real-binaries", release, StringComparison.Ordinal);
        Assert.Contains("--version", release, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorDocs_KeepEnglishJapaneseInstallAndCoexistenceParity()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "13-npm-distribution.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "13-npm-distribution.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("npm install -g intent-system", document, StringComparison.Ordinal);
            Assert.Contains("npx intent-system", document, StringComparison.Ordinal);
            Assert.Contains("intent-cli", document, StringComparison.Ordinal);
            Assert.Contains("PATH", document, StringComparison.Ordinal);
            Assert.Contains("postinstall", document, StringComparison.Ordinal);
            Assert.Contains("JTechJapan.IntentSystem.Cli", document, StringComparison.Ordinal);
            Assert.Contains("--version", document, StringComparison.Ordinal);
        }

        Assert.Contains("checksum", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA-256", japanese, StringComparison.Ordinal);
        Assert.Contains("never publishes", english, StringComparison.Ordinal);
        Assert.Contains("公開は行わず", japanese, StringComparison.Ordinal);
    }

    private static JsonDocument ReadJson(string path)
    {
        Assert.True(File.Exists(path), $"expected G702 contract file: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool HasScript(JsonElement package, string scriptName) =>
        package.TryGetProperty("scripts", out var scripts) && scripts.TryGetProperty(scriptName, out _);
}
