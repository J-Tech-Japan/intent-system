namespace IntentSystem.Cli.Commands;

internal static class RunRootResultArtifactPathResolver
{
    public static string Resolve(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = context.Config.Project.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/run.result.json";
    }
}
