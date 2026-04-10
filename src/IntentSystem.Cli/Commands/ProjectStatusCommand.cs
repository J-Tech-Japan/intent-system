namespace IntentSystem.Cli.Commands;

internal static class ProjectStatusCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine($"Domain: {context.Config.Project.Domain}");
        writer.WriteLine($"Repo root: {context.RepoRoot}");
        writer.WriteLine($"Intent CLI root: {context.GetIntentCliDirectoryPath()}");
        writer.WriteLine($"Artifact root: {context.ResolveArtifactRootPath()}");
        writer.WriteLine($"Config path: {context.GetConfigPath()}");

        return 0;
    }
}
