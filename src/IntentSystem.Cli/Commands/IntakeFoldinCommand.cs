namespace IntentSystem.Cli.Commands;

internal static class IntakeFoldinCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake foldin command requires a domain.");
            return 1;
        }

        var domain = args[0];
        var compilePath = Path.Combine(
            context.RepoRoot,
            IntakeCompileArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(compilePath))
        {
            writer.WriteLine($"Intake compile artifact was not found at {compilePath}");
            return 1;
        }

        try
        {
            var request = IntakeCompileArtifactMarkdown.Deserialize(File.ReadAllText(compilePath));
            if (!string.Equals(request.Domain, domain, StringComparison.Ordinal))
            {
                writer.WriteLine(
                    $"Intake compile artifact domain '{request.Domain}' does not match requested domain '{domain}'.");
                return 1;
            }

            var markdown = IntakeFoldinRenderer.RenderMarkdown(request);
            var artifactPath = IntakeFoldinArtifactWriter.Write(markdown, domain, context.RepoRoot);
            IntakeFoldinRenderer.WriteSummary(writer, request, artifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
