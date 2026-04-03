using IntentSystem.WorkerAdapter.Models;
using IntentSystem.WorkerAdapter.Serialization;

namespace IntentSystem.WorkerAdapter;

public static class WorkerAdapterRunArtifactWriter
{
    public static void Write(WorkerAdapterResult result, string executionUnit, string repoRoot, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var relativePath = WorkerAdapterRunArtifactPathResolver.Resolve(executionUnit);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!overwrite && File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Workflow run artifact already exists at {absolutePath}.");
        }

        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Workflow run artifact path '{absolutePath}' did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, WorkerAdapterSerializer.SerializeResult(result));
    }
}
