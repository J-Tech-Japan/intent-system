using IntentSystem.WorkerAdapter;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class WorkflowRunCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Workflow run command requires an execution unit.");
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

        var workflowDefinitionRef = $".intent-cli/workflows/{executionUnit}.yaml";
        var workflowDefinitionPath = Path.Combine(
            context.RepoRoot,
            workflowDefinitionRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(workflowDefinitionPath))
        {
            writer.WriteLine($"Workflow definition artifact was not found at {workflowDefinitionPath}");
            return 1;
        }

        try
        {
            var definition = WorkflowDefinitionSerializer.Deserialize(File.ReadAllText(workflowDefinitionPath));
            if (!string.Equals(definition.ExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Workflow definition execution unit '{definition.ExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            var request = WorkerAdapterRequestFactory.Create(
                executionUnit,
                workflowDefinitionRef,
                context.RepoRoot);
            var result = WorkerAdapterRunArtifactFactory.CreateInitialResult(request, definition);
            WorkerAdapterRunArtifactWriter.Write(result, executionUnit, context.RepoRoot, overwrite: true);

            writer.WriteLine($"Workflow run artifact generated for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
