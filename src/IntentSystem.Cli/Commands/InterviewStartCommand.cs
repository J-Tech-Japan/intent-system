using IntentSystem.ConceptIntake.Interview;
namespace IntentSystem.Cli.Commands;

internal static class InterviewStartCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Interview start command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var items = InterviewArtifactYaml.Discover(context.RepoRoot, domain);
            var nextQuestion = InterviewQueue.GetNextPendingForDomain(items.Select(item => item.Item).ToArray(), domain);
            InterviewStartRenderer.Write(writer, domain, nextQuestion);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
