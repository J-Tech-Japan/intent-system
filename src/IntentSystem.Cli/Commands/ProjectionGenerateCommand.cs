using System.Text.Json;
using IntentSystem.Projection;
using IntentSystem.Projection.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ProjectionGenerateCommand
{
    private enum ProjectionCommandMode
    {
        Generate,
        Regenerate
    }

    public static int Generate(CliContext context, string[] args, TextWriter writer)
    {
        return Execute(context, args, writer, ProjectionCommandMode.Generate);
    }

    public static int Regenerate(CliContext context, string[] args, TextWriter writer)
    {
        return Execute(context, args, writer, ProjectionCommandMode.Regenerate);
    }

    private static int Execute(
        CliContext context,
        string[] args,
        TextWriter writer,
        ProjectionCommandMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Projection command requires an execution unit.");
            return 1;
        }

        var executionUnit = args[0];
        var sourcePath = ProjectionSourcePathResolver.Resolve(context.RepoRoot, executionUnit);
        if (!File.Exists(sourcePath))
        {
            writer.WriteLine($"Projection source bundle was not found at {sourcePath}");
            return 1;
        }

        try
        {
            var bundle = ProjectionSourceSerializer.Deserialize(File.ReadAllText(sourcePath));
            EnsureExecutionUnitMatches(executionUnit, bundle.Row.SourceExecutionUnit);
            var packet = PacketGenerator.Generate(bundle.Row, bundle.Context);

            ProjectionArtifactWriter.Write(
                packet,
                context.RepoRoot,
                overwrite: mode == ProjectionCommandMode.Regenerate);

            writer.WriteLine($"Projection artifacts generated for {executionUnit}.");
            return 0;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or JsonException)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void EnsureExecutionUnitMatches(string requestedExecutionUnit, string bundleExecutionUnit)
    {
        var requestedPaths = PacketPathResolver.Resolve(requestedExecutionUnit);
        var bundlePaths = PacketPathResolver.Resolve(bundleExecutionUnit);

        if (requestedPaths != bundlePaths)
        {
            throw new InvalidOperationException(
                $"Projection source bundle execution unit '{bundleExecutionUnit}' must match requested execution unit '{requestedExecutionUnit}'.");
        }
    }
}
