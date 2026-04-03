using IntentSystem.Projection.Serialization;
using IntentSystem.Workflow;

namespace IntentSystem.Cli.Commands;

internal static class WorkflowRenderCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Workflow render command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = args[0];
        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        var packetYamlPath = Path.Combine(
            context.RepoRoot,
            queueItem.PacketPaths.Yaml.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packetYamlPath))
        {
            writer.WriteLine($"Workflow render packet YAML was not found at {packetYamlPath}");
            return 1;
        }

        try
        {
            var packetContract = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetYamlPath));
            var definition = WorkflowDefinitionMapper.Map(queueItem, packetContract);
            WorkflowArtifactWriter.Write(definition, context.RepoRoot, overwrite: true);
            writer.WriteLine($"Workflow definition rendered for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
