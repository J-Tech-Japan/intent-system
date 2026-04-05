using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ClarifyListCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 0)
        {
            writer.WriteLine("Clarify list command does not accept arguments.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        try
        {
            var clarifications = DiscoverOpenClarifications(context.RepoRoot);
            var queueItemsByExecutionUnit = queueState.Items.ToDictionary(
                item => item.ExecutionUnit,
                StringComparer.Ordinal);

            ClarifyListRenderer.Write(writer, clarifications, queueItemsByExecutionUnit);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IReadOnlyList<ClarificationItem> DiscoverOpenClarifications(string repoRoot)
    {
        var clarificationsRoot = Path.Combine(
            repoRoot,
            ".intent-cli",
            "clarifications");

        if (!Directory.Exists(clarificationsRoot))
        {
            return [];
        }

        var clarifications = new List<ClarificationItem>();
        foreach (var artifactPath in Directory.EnumerateFiles(
                     clarificationsRoot,
                     "request.json",
                     SearchOption.AllDirectories))
        {
            var clarification = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
            if (clarification.Status == ClarificationStatus.Open)
            {
                clarifications.Add(clarification);
            }
        }

        return clarifications
            .OrderBy(item => item.ExecutionUnit, StringComparer.Ordinal)
            .ThenBy(item => item.QuestionId, StringComparer.Ordinal)
            .ToArray();
    }
}
