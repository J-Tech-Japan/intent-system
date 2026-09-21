using System.Text.RegularExpressions;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IssueBodySizeLimitsSourceGuardTests
{
    [Fact]
    public void ProductionSource_DeclaresEachBodyLimitSpellingExactlyOnce()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var sourceFiles = Directory.GetFiles(
            Path.Combine(root, "src"),
            "*.cs",
            SearchOption.AllDirectories);
        var hardMatches = FindMatches(sourceFiles, @"(?:65536|65_536)");
        var warningMatches = FindMatches(sourceFiles, @"(?:58000|58_000)");

        var hardMatch = Assert.Single(hardMatches);
        var warningMatch = Assert.Single(warningMatches);
        Assert.Equal(
            Path.Combine(root, "src", "IntentSystem.Cli", "Commands", "IssueBodySizeLimits.cs"),
            hardMatch.FilePath);
        Assert.Equal(
            Path.Combine(root, "src", "IntentSystem.Cli", "Commands", "IssueBodySizeLimits.cs"),
            warningMatch.FilePath);
    }

    [Fact]
    public void BodyFeedingReads_UseTheSharedStrictReader_AndOnlyNonBodyReadsRemainLenient()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var commandRoot = Path.Combine(root, "src", "IntentSystem.Cli", "Commands");
        var sources = new Dictionary<string, string>
        {
            ["IssueCreateCommand.cs"] = File.ReadAllText(Path.Combine(commandRoot, "IssueCreateCommand.cs")),
            ["QueueDispatchCommand.cs"] = File.ReadAllText(Path.Combine(commandRoot, "QueueDispatchCommand.cs")),
            ["BugImplementationIssueCommand.cs"] = File.ReadAllText(Path.Combine(commandRoot, "BugImplementationIssueCommand.cs"))
        };
        var allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["IssueCreateCommand.cs"] = ["absoluteArtifactPath", "packetPath"],
            ["QueueDispatchCommand.cs"] = ["packetPath"],
            ["BugImplementationIssueCommand.cs"] = []
        };

        foreach (var (fileName, source) in sources)
        {
            foreach (Match match in Regex.Matches(source, @"File\.ReadAllText\((?<argument>[^)]*)\)"))
            {
                var argument = match.Groups["argument"].Value.Trim();
                var line = source[..match.Index].Count(character => character == '\n') + 1;
                Assert.True(
                    allowed[fileName].Contains(argument),
                    $"{fileName}:{line} has an unapproved File.ReadAllText({argument}).");
            }
        }

        Assert.Contains("StrictUtf8FileReader.ReadText(issueBodyPath)", sources["IssueCreateCommand.cs"], StringComparison.Ordinal);
        Assert.Contains("StrictUtf8FileReader.ReadText(githubBodyPath)", sources["QueueDispatchCommand.cs"], StringComparison.Ordinal);

        var bugSource = sources["BugImplementationIssueCommand.cs"];
        Assert.Equal(6, Regex.Matches(bugSource, @"StrictUtf8FileReader\.ReadText\(").Count);
        Assert.Contains("StrictUtf8FileReader.ReadText(implementationRepairPath)", bugSource, StringComparison.Ordinal);
        Assert.Contains("StrictUtf8FileReader.ReadText(packetPath)", bugSource, StringComparison.Ordinal);
        Assert.Contains("StrictUtf8FileReader.ReadText(artifactPath)", bugSource, StringComparison.Ordinal);
        Assert.Contains("File.ReadAllBytes", File.ReadAllText(Path.Combine(commandRoot, "StrictUtf8FileReader.cs")), StringComparison.Ordinal);
        Assert.Contains("new UTF8Encoding(false, throwOnInvalidBytes: true)", File.ReadAllText(Path.Combine(commandRoot, "StrictUtf8FileReader.cs")), StringComparison.Ordinal);
    }

    private static List<SourceMatch> FindMatches(IEnumerable<string> sourceFiles, string pattern)
    {
        var matches = new List<SourceMatch>();
        foreach (var filePath in sourceFiles)
        {
            var source = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(source, pattern))
            {
                matches.Add(new SourceMatch(filePath, match.Value));
            }
        }

        return matches;
    }

    private sealed record SourceMatch(string FilePath, string Value);
}
