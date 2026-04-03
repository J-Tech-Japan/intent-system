using IntentSystem.Workflow.Models;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Workflow;

public static class WorkflowArtifactWriter
{
    public static void Write(WorkflowDefinition definition, string repoRoot, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = WorkflowArtifactPathResolver.Resolve(definition.ExecutionUnit);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!overwrite && File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Workflow artifact already exists at {absolutePath}.");
        }

        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Workflow artifact path '{absolutePath}' did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, WorkflowDefinitionSerializer.Serialize(definition));
    }
}
