using IntentSystem.WorkerAdapter.Models;
using IntentSystem.WorkerAdapter.Serialization;

namespace IntentSystem.WorkerAdapter.Tests;

public sealed class WorkerAdapterRunArtifactWriterTests
{
    [Fact]
    public void Resolve_GivenExecutionUnit_UsesRunJsonBaselinePath()
    {
        var path = WorkerAdapterRunArtifactPathResolver.Resolve("C2");

        Assert.Equal(".intent-cli/workflows/C2.run.json", path);
    }

    [Fact]
    public void Write_GivenResult_WritesSerializedArtifactUnderRepoRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var result = CreateResult();

        WorkerAdapterRunArtifactWriter.Write(result, "C2", repoRoot, overwrite: false);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "workflows", "C2.run.json");
        Assert.True(File.Exists(artifactPath));
        Assert.Equal(
            WorkerAdapterSerializer.SerializeResult(result),
            File.ReadAllText(artifactPath));
    }

    [Fact]
    public void Write_GivenExistingArtifactAndOverwriteFalse_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            "existing run artifact");

        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkerAdapterRunArtifactWriter.Write(CreateResult(), "C2", repoRoot, overwrite: false));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing run artifact", File.ReadAllText(artifactPath));
    }

    private static WorkerAdapterResult CreateResult()
    {
        return new WorkerAdapterResult
        {
            RunStatus = WorkerAdapterRunStatus.Running,
            StepStatuses =
            [
                new WorkerAdapterStepStatus
                {
                    Step = Workflow.Models.WorkflowStepKind.Implement,
                    Status = WorkerAdapterStepState.Running
                }
            ],
            ReviewResult = new WorkerReviewResult
            {
                Disposition = WorkerReviewDisposition.Pending
            },
            ReviewCommentRefs = [],
            ClarificationRequests = [],
            ResultSummary = "Workflow run artifact initialized for C2.",
            RunLogRefs = [".intent-cli/workflows/C2.run.json"]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-worker-adapter-writer-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
