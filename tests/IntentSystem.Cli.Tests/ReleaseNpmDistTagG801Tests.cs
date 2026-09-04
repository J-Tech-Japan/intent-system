using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G801: npm publish must use a dist-tag derived from the resolved release
/// version. These tests read the workflow itself so the release-only shell
/// helper and every package call site stay covered without publishing anything.
/// </summary>
public sealed class ReleaseNpmDistTagG801Tests
{
    [Fact]
    public void Ac1_DerivesLatestForStableAndNonDefaultTagForPrerelease()
    {
        var result = RunTagHelper(
            "0.32.0",
            "0.32.0-preview.1");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(
            "0.32.0 -> latest\n0.32.0-preview.1 -> preview\n",
            result.StandardOutput.ReplaceLineEndings("\n"));
        Console.WriteLine("G801 AC1 resolved dist-tags:\n" + result.StandardOutput);
    }

    [Fact]
    public void Ac2_AllFourNpmPackagesUseTheDerivedTag()
    {
        var workflow = ReadWorkflow();
        var publishStep = ExtractPublishStep(workflow);

        Assert.Contains(
            "npm publish \"${package_path}\" --access public --tag \"${NPM_DIST_TAG}\"",
            publishStep,
            StringComparison.Ordinal);

        var packages = new[]
        {
            "@j-tech-japan/intent-cli-darwin-arm64",
            "@j-tech-japan/intent-cli-linux-x64",
            "@j-tech-japan/intent-cli-win32-x64",
            "intent-system",
        };
        foreach (var package in packages)
        {
            Assert.Contains($"\"{package}\"", publishStep, StringComparison.Ordinal);
        }

        var callCount = Regex.Matches(
            publishStep,
            @"(?m)^\s*publish_package \\\s*$").Count;
        Assert.Equal(4, callCount);
        var callNames = Regex.Matches(
                publishStep,
                @"(?ms)^\s*publish_package \\\s*\n\s+[^\r\n]+\n\s+""(?<name>[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.Equal(packages, callNames);
        Console.WriteLine(
            $"G801 AC2 publish call-sites: package_count={packages.Length}; calls={callCount}; tag=\"${{NPM_DIST_TAG}}\"; packages={string.Join(",", callNames)}");
    }

    [Fact]
    public void Ac3_ResolvedVersionStillStripsOnlyLeadingV()
    {
        var workflow = ReadWorkflow();
        var stripCount = Regex.Matches(workflow, Regex.Escape("VERSION=\"${RAW#v}\"")).Count;

        Assert.Equal(3, stripCount);
        var result = RunBash(
            "for raw in v0.32.0-preview.1 v0.32.0; do VERSION=\"${raw#v}\"; printf '%s -> %s\\n' \"$raw\" \"$VERSION\"; done");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(
            "v0.32.0-preview.1 -> 0.32.0-preview.1\nv0.32.0 -> 0.32.0\n",
            result.StandardOutput.ReplaceLineEndings("\n"));
        Console.WriteLine($"G801 AC3 version derivation occurrences={stripCount}:\n{result.StandardOutput}");
    }

    [Fact]
    public void Ac4_DetectsEverySupportedPrereleaseFormIndividually()
    {
        var result = RunTagHelper(
            "0.32.0-preview.1",
            "0.32.0-rc.2",
            "0.32.0-beta.3",
            "0.32.0-alpha.4",
            "0.32.0");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(
            "0.32.0-preview.1 -> preview\n"
            + "0.32.0-rc.2 -> rc\n"
            + "0.32.0-beta.3 -> beta\n"
            + "0.32.0-alpha.4 -> alpha\n"
            + "0.32.0 -> latest\n",
            result.StandardOutput.ReplaceLineEndings("\n"));
        Console.WriteLine("G801 AC4 semver detection cases:\n" + result.StandardOutput);
    }

    [Fact]
    public void Ac5_NuGetVersionAndPublishHandlingRemainPresentAndUntagged()
    {
        var workflow = ReadWorkflow();
        var nugetStart = workflow.IndexOf("  nupkg:\n", StringComparison.Ordinal);
        var nugetEnd = workflow.IndexOf("\n  binaries:\n", nugetStart, StringComparison.Ordinal);
        Assert.True(nugetStart >= 0 && nugetEnd > nugetStart, "Could not isolate the NuGet job.");
        var nuget = workflow[nugetStart..nugetEnd];

        Assert.Contains("VERSION=\"${RAW#v}\"", nuget, StringComparison.Ordinal);
        Assert.Contains("dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj", nuget, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget push", nuget, StringComparison.Ordinal);
        Assert.DoesNotContain("npm publish", nuget, StringComparison.Ordinal);
        Console.WriteLine("G801 AC5 NuGet job: version_strip=true; pack=true; nuget_publish=true; npm_tag_changes=none");
    }

    private static CommandResult RunTagHelper(params string[] versions)
    {
        var helper = ExtractTagHelper(ReadWorkflow());
        var loop = "for version in "
            + string.Join(" ", versions.Select(version => $"'{version}'"))
            + "; do printf '%s -> %s\\n' \"$version\" \"$(derive_npm_dist_tag \"$version\")\"; done";
        return RunBash(helper + "\n" + loop);
    }

    private static string ExtractTagHelper(string workflow)
    {
        var match = Regex.Match(
            workflow,
            @"(?ms)^\s*derive_npm_dist_tag\(\) \{(?<body>.*?)^\s*\}\s*$");
        Assert.True(match.Success, "Could not locate derive_npm_dist_tag in release.yml.");
        var body = match.Groups["body"].Value;
        var lines = body.Split('\n').Select(line => line.StartsWith("          ", StringComparison.Ordinal)
            ? line[10..]
            : line);
        return "derive_npm_dist_tag() {\n" + string.Join('\n', lines) + "}\n";
    }

    private static string ExtractPublishStep(string workflow)
    {
        var start = workflow.IndexOf("      - name: Publish npm packages through OIDC trusted publishing", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate npm publish step.");
        return workflow[start..];
    }

    private static string ReadWorkflow() => File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "release.yml"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".github", "workflows", "release.yml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate .github/workflows/release.yml");
    }

    private static CommandResult RunBash(string script)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start bash.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new CommandResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Output => StandardOutput + StandardError;
    }
}
