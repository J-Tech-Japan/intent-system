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
        var packetYamlPath = ResolvePacketYamlPath(context.RepoRoot, executionUnit);
        if (!File.Exists(packetYamlPath))
        {
            writer.WriteLine($"Projection packet YAML was not found at {packetYamlPath}");
            return 1;
        }

        try
        {
            var packetContract = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetYamlPath));
            EnsureExecutionUnitMatches(executionUnit, packetContract.ImplementationIssuePacket.SourceExecutionUnit);
            var packet = PacketGenerator.Generate(
                packetContract.ImplementationIssuePacket,
                packetContract.ReviewContextPacket);

            ProjectionArtifactWriter.Write(
                packet,
                context.RepoRoot,
                overwrite: mode == ProjectionCommandMode.Regenerate,
                allowExistingPacketYaml: mode == ProjectionCommandMode.Generate);

            writer.WriteLine($"Projection artifacts generated for {executionUnit}.");
            return 0;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ResolvePacketYamlPath(string repoRoot, string executionUnit)
    {
        var paths = PacketPathResolver.Resolve(executionUnit);
        return Path.Combine(repoRoot, paths.Yaml.Replace('/', Path.DirectorySeparatorChar));
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
