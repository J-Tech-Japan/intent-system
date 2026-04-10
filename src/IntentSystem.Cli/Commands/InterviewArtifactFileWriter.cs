using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class InterviewArtifactFileWriter
{
    public static IReadOnlyList<string> Write(
        string repoRoot,
        InterviewQueueItem item,
        IReadOnlyList<string> recommendedUpdates,
        IReadOnlyList<string> gaps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(recommendedUpdates);
        ArgumentNullException.ThrowIfNull(gaps);

        var interviewsRoot = Path.Combine(
            repoRoot,
            ".intent-cli",
            "interviews",
            item.DomainSlug.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(interviewsRoot);

        var yamlPath = Path.Combine(interviewsRoot, $"{item.QuestionId}.yaml");
        var markdownPath = Path.Combine(interviewsRoot, $"{item.QuestionId}.md");

        File.WriteAllText(yamlPath, InterviewArtifactYaml.Serialize(item));
        File.WriteAllText(
            markdownPath,
            RenderMarkdown(item, recommendedUpdates, gaps));

        return
        [
            ToRelativePath(repoRoot, yamlPath),
            ToRelativePath(repoRoot, markdownPath)
        ];
    }

    public static string RenderMarkdown(
        InterviewQueueItem item,
        IReadOnlyList<string> recommendedUpdates,
        IReadOnlyList<string> gaps)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(recommendedUpdates);
        ArgumentNullException.ThrowIfNull(gaps);

        var lines = new List<string>
        {
            "# Interview Question",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{item.DomainSlug}`",
            string.Empty,
            $"question_id: {item.QuestionId}",
            $"question_text: {item.QuestionText}",
            $"reason: {item.Reason}",
            $"blocking_or_nonblocking: {item.BlockingOrNonblocking}",
            string.Empty,
            "return_to_intent_paths:"
        };

        AppendBullets(lines, item.ReturnToIntentPaths);
        lines.Add(string.Empty);
        lines.Add("recommended_updates:");
        AppendBullets(lines, recommendedUpdates);
        lines.Add(string.Empty);
        lines.Add("gaps:");
        AppendBullets(lines, gaps);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static void AppendBullets(List<string> lines, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        lines.AddRange(values.Select(value => $"- {value}"));
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
