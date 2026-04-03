using IntentSystem.WorkerAdapter;
using IntentSystem.WorkerAdapter.Serialization;
using IntentSystem.Workflow;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class WorkflowStatusCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Workflow status command requires an execution unit.");
            return 1;
        }

        var executionUnit = args[0];
        var workflowDefinitionRef = WorkflowArtifactPathResolver.Resolve(executionUnit);
        var workflowDefinitionPath = Path.Combine(
            context.RepoRoot,
            workflowDefinitionRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(workflowDefinitionPath))
        {
            writer.WriteLine($"Workflow definition artifact was not found at {workflowDefinitionPath}");
            return 1;
        }

        var runArtifactRef = WorkerAdapterRunArtifactPathResolver.Resolve(executionUnit);
        var runArtifactPath = Path.Combine(
            context.RepoRoot,
            runArtifactRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(runArtifactPath))
        {
            writer.WriteLine($"Workflow run artifact was not found at {runArtifactPath}");
            return 1;
        }

        try
        {
            var definition = WorkflowDefinitionSerializer.Deserialize(File.ReadAllText(workflowDefinitionPath));
            if (!string.Equals(definition.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Workflow definition execution unit '{definition.ExecutionUnit}' must match requested execution unit '{executionUnit}'.");
            }

            var result = WorkerAdapterSerializer.DeserializeResult(File.ReadAllText(runArtifactPath));
            WorkflowStatusRenderer.Write(writer, definition, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
