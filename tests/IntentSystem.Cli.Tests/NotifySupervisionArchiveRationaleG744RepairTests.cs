using System.Text.RegularExpressions;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class NotifySupervisionArchiveRationaleG744RepairTests
{
    [Fact]
    public void ArchiveRationale_StatesMeasuredInputsAndSevenDayRelationshipInBothMirrors()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine("docs", "en", "12-agent-message-orchestration.md")] =
                "The observed live file reached 111.5 MB while the incident accumulated approximately 96,000 records over approximately 14 days; therefore the default seven-day live window retains roughly half of that observed volume live and remains comfortably below GitHub's 100 MB tracking limit.",
            [Path.Combine("docs", "ja", "12-agent-message-orchestration.md")] =
                "観測された live file は 111.5 MB に達し、この incident は約 14 日間で約 96,000 records を蓄積しました。そのため、同じ rate で既定の 7 日 live window を保つと、その観測 volume の 約半分が live に残り、GitHub の 100 MB tracking limit を十分下回ります。",
        };

        foreach (var (relativePath, rationale) in expected)
        {
            var content = Normalize(File.ReadAllText(Path.Combine(root, relativePath)));
            Assert.Contains(rationale, content, StringComparison.Ordinal);
        }

        var roleGuidance = Normalize(SupervisionGuideText.ArchiveRule);
        Assert.Contains(
            "The default is 7 days: the observed live file reached 111.5 MB while the incident accumulated approximately 96,000 records over approximately 14 days; therefore the default seven-day live window retains roughly half that observed volume live and stays comfortably below GitHub's 100 MB tracking limit",
            roleGuidance,
            StringComparison.Ordinal);
    }

    private static string Normalize(string content) =>
        Regex.Replace(content, @"\s+", " ").Trim();
}
