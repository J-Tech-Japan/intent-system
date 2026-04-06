namespace IntentSystem.Cli.Commands;

internal static class IntakeAdvanceCommand
{
    private static readonly string[] DeferredStages =
    [
        "foldin",
        "patch",
        "apply",
        "execution",
        "execution-apply"
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake advance command requires a domain.");
            return 1;
        }

        var domain = args[0].Trim();

        try
        {
            EnsureConceptArtifactExists(context.RepoRoot, domain);

            var compileResult = IntakeCompileCommand.ExecuteCore(context.RepoRoot, domain);
            if (!compileResult.IsReady)
            {
                IntakeAdvanceRenderer.WriteSummary(writer, new IntakeAdvanceResult
                {
                    Domain = domain,
                    ReadinessStatus = "not-ready",
                    UpdatedSourceFilePaths = [],
                    UpdatedExecutionFilePaths = [],
                    RegeneratedArtifactPaths = [],
                    SkippedStages = DeferredStages
                });
                return 0;
            }

            var (_, foldinPath) = IntakeFoldinCommand.ExecuteCore(context.RepoRoot, domain);
            var (_, patchPath) = IntakePatchCommand.ExecuteCore(context.RepoRoot, domain);
            var applyResult = IntakeApplyCommand.ExecuteCore(context.RepoRoot, domain);
            var (_, executionPath) = IntakeExecutionCommand.ExecuteCore(context.RepoRoot, domain);
            var executionApplyResult = IntakeExecutionApplyCommand.ExecuteCore(context.RepoRoot, domain);

            var regeneratedArtifactPaths = new[]
            {
                compileResult.ArtifactPath,
                foldinPath,
                patchPath,
                executionPath
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ToRelativePath(context.RepoRoot, path!))
            .ToArray();

            IntakeAdvanceRenderer.WriteSummary(writer, new IntakeAdvanceResult
            {
                Domain = domain,
                ReadinessStatus = "ready",
                UpdatedSourceFilePaths = applyResult.ChangedFilePaths,
                UpdatedExecutionFilePaths = executionApplyResult.ChangedFilePaths,
                RegeneratedArtifactPaths = regeneratedArtifactPaths,
                SkippedStages = []
            });
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void EnsureConceptArtifactExists(string repoRoot, string domain)
    {
        var conceptPath = Path.Combine(
            repoRoot,
            IntakeConceptArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(conceptPath))
        {
            throw new InvalidOperationException($"Intake concept artifact was not found at {conceptPath}");
        }

        var packet = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(conceptPath));
        if (!string.Equals(packet.DomainSlug, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake concept artifact domain '{packet.DomainSlug}' does not match requested domain '{domain}'.");
        }
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
