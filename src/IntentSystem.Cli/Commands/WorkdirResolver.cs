namespace IntentSystem.Cli.Commands;

internal static class WorkdirResolver
{
    public static string Resolve(CliContext context, string? workdir)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(workdir))
        {
            return context.RepoRoot;
        }

        return Path.IsPathRooted(workdir)
            ? workdir
            : Path.GetFullPath(Path.Combine(context.RepoRoot, workdir));
    }
}
