using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G665 keeps the v0.18.0 preparation bilingual, bounded to G664 in the
/// three-commit post-v0.17.0 range, and explicitly prepare-only.
/// </summary>
public sealed class ReleaseNotesV0180DocsTests
{
    private static readonly string[] RangeCommits = ["c2746f26", "229e5522", "40081137"];

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCoverExactlyG664AndAccountForTheThreeCommitRange(string language)
    {
        var notes = Read(language);
        var units = Regex.Matches(notes, @"(?m)^- (G\d+) —")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(["G664"], units);
        Assert.Single(units);
        Assert.Contains("#1435", notes, StringComparison.Ordinal);
        Assert.Contains("merge commit `40081137`", notes, StringComparison.Ordinal);
        Assert.Contains("git log v0.17.0..main", notes, StringComparison.Ordinal);
        foreach (var commit in RangeCommits) Assert.Contains(commit, notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "three-commit range" : "三 commit", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "not a release execution unit" : "release execution unit ではありません", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void PreviewStatementPrecedesTheFeatureAndLinksThePromise(string language)
    {
        var notes = Read(language);
        var preview = notes.IndexOf("preview-through-1.x", StringComparison.Ordinal);
        var feature = notes.IndexOf(
            language == "en" ? "## The application conversation is the front door" : "## application conversation は front door",
            StringComparison.Ordinal);

        Assert.True(preview >= 0);
        Assert.True(feature > preview);
        Assert.Contains("[1.0 compatibility promise](1.0-compatibility-promise.md)", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesStateExactTriggersSixOrderedStepsAndExplicitHandoff(string language)
    {
        var notes = Read(language);
        Assert.Contains("Start this work in a herdr-only team.", notes, StringComparison.Ordinal);
        Assert.Contains("herdr-only で起動して。", notes, StringComparison.Ordinal);

        var previous = -1;
        for (var step = 1; step <= 6; step++)
        {
            var index = notes.IndexOf($"\n{step}. ", StringComparison.Ordinal);
            Assert.True(index > previous, $"step {step} must follow step {step - 1}");
            previous = index;
        }

        Assert.Contains("HANDOFF", notes, StringComparison.Ordinal);
        Assert.Contains("operator's front door", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en"
                ? "not a design, orchestration, implementation, review, or supervision"
                : "design、orchestration、implementation、review、supervision の loop seat ではありません",
            notes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void HumanQuestionsNeverBecomeDefaults(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[] { language == "en" ? "human" : "人間", "CLI", "model", "application", "agent kind", "inbound app monitor" })
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "there are no defaults" : "default は置きません", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "never assumes" : "推測しません", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCarryJoinPartialAndAdvisorLifecycle(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");
        foreach (var term in new[]
                 {
                     "join-and-delegate", "idempotent", "topology-recorded-seats-missing",
                     "topology-recorded-supervision-and-handoff-missing", "bootstrap-resume",
                 })
        {
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(language == "en" ? "Absent topology is silent" : "topology がなければ", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "completed cycle clears" : "completed cycle は推奨を解除", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesCarryNoExecutionNoIntegrationAndLinkedCompositionBoundaries(string language)
    {
        var notes = Read(language);
        Assert.Contains("executes nothing", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("application-side integration code", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G637", notes, StringComparison.Ordinal);
        Assert.Contains("G654", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "linked and composed" : "link して compose", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "never invokes herdr" : "herdr の呼び出し", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("register", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unregister", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NotesStateMergedTreeRealHostRenderingVerification(string language)
    {
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");

        foreach (var term in new[] { "merged head", "40081137", "real host data", "Markdown", "JSON", "--team", language == "en" ? "eight" : "八回" })
            Assert.Contains(term, compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "not a diff-only reading" : "diff の読解だけでなく", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-keyword claim", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("string field name", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CountGuardMatchesExactlyOneUnitAndRejectsMismatch(string language)
    {
        var notes = Read(language);
        Assert.Empty(CountMismatches(notes, language));

        var wrong = language == "en"
            ? notes.Replace("exactly one merged unit", "exactly two merged units", StringComparison.Ordinal)
            : notes.Replace("正確に一件の merged unit", "正確に二件の merged unit", StringComparison.Ordinal);

        Assert.NotEqual(notes, wrong);
        Assert.NotEmpty(CountMismatches(wrong, language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void VersionReadinessMinorRationaleAndPrepareOnlyBoundariesStayAligned(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var notes = Read(language);
        var compact = Regex.Replace(notes, @"\s+", " ");
        var reference = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
        var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng", "version.json"))).RootElement;

        Assert.Equal("0.17.0", policy.GetProperty("stableVersion").GetString());
        Assert.Equal("0.18.0", policy.GetProperty("nextVersion").GetString());
        Assert.Contains("guide bootstrap", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bootstrap-resume", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "absent at v0.17.0" : "v0.17.0 には存在しません", compact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepare-only", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "UNRELEASED" : "未リリース", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package publish", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-release roll", notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v0180-preapproved-001", notes, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.18.0.md", reference, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.17.0.md", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-v0.17.1.md", reference, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> CountMismatches(string notes, string language)
    {
        var listedCount = Regex.Matches(notes, @"(?m)^- (G\d+) —").Count;
        var statedCounts = language == "en"
            ? Regex.Matches(notes, @"\b(?<count>one|two|three|four|five|six|seven|eight|nine|ten)\s+merged units?\b", RegexOptions.IgnoreCase)
                .Select(match => EnglishCount(match.Groups["count"].Value))
            : Regex.Matches(notes, @"正確に(?<count>[一二三四五六七八九十百〇零]+)件の merged unit")
                .Select(match => JapaneseCount(match.Groups["count"].Value));

        return statedCounts.Where(count => count != listedCount).Select(count => $"stated {count}; listed {listedCount}").ToArray();
    }

    private static int EnglishCount(string text) => text.ToLowerInvariant() switch
    {
        "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
        "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "unsupported count"),
    };

    private static int JapaneseCount(string text)
    {
        var values = new Dictionary<char, int>
        {
            ['〇'] = 0, ['零'] = 0, ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4,
            ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9, ['十'] = 10,
            ['百'] = 100,
        };
        var total = 0;
        var pending = 0;
        foreach (var character in text)
        {
            var value = values[character];
            if (value is 10 or 100)
            {
                total += (pending == 0 ? 1 : pending) * value;
                pending = 0;
            }
            else
            {
                pending = value;
            }
        }
        return total + pending;
    }

    private static string Read(string language) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.18.0.md"));
}
