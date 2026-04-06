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

        try
        {
            var (request, artifactPath) = ExecuteCore(context.RepoRoot, domain);
            IntakeFoldinRenderer.WriteSummary(writer, request, artifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static (IntakeFoldinRequest Request, string ArtifactPath) ExecuteCore(string repoRoot, string domain)
    {
        var compilePath = Path.Combine(
            repoRoot,
            IntakeCompileArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(compilePath))
        {
            throw new InvalidOperationException($"Intake compile artifact was not found at {compilePath}");
        }

        var compileRequest = IntakeCompileArtifactMarkdown.Deserialize(File.ReadAllText(compilePath));
        if (!string.Equals(compileRequest.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake compile artifact domain '{compileRequest.Domain}' does not match requested domain '{domain}'.");
        }

        var request = new IntakeFoldinRequest
        {
            Domain = compileRequest.Domain,
            AnsweredQuestionIds = compileRequest.AnsweredQuestionIds,
            RecommendedUpdates = compileRequest.RecommendedUpdates,
            ReturnToIntentPaths = compileRequest.ReturnToIntentPaths,
            SourceConceptRefs = compileRequest.SourceConceptRefs
        };
        var markdown = IntakeFoldinRenderer.RenderMarkdown(request);
        var artifactPath = IntakeFoldinArtifactWriter.Write(markdown, domain, repoRoot);
        return (request, artifactPath);
    }
}
