using IntentSystem.Workflow.Models;
using IntentSystem.Workflow.Serialization;

namespace IntentSystem.Workflow.Tests;

public sealed class WorkflowArtifactWriterTests
{
    [Fact]
    public void Resolve_GivenExecutionUnit_UsesWorkflowYamlBaselinePath()
    {
        var path = WorkflowArtifactPathResolver.Resolve("C2");

        Assert.Equal(".intent-cli/workflows/C2.yaml", path);
    }

    [Fact]
    public void Write_GivenDefinition_WritesSerializedArtifactUnderRepoRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var definition = CreateDefinition();

        WorkflowArtifactWriter.Write(definition, repoRoot, overwrite: false);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "workflows", "C2.yaml");
        Assert.True(File.Exists(artifactPath));
        Assert.Equal(
            WorkflowDefinitionSerializer.Serialize(definition),
            File.ReadAllText(artifactPath));
    }

    [Fact]
    public void Write_GivenExistingArtifactAndOverwriteFalse_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            "existing artifact");

        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkflowArtifactWriter.Write(CreateDefinition(), repoRoot, overwrite: false));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing artifact", File.ReadAllText(artifactPath));
    }

    private static WorkflowDefinition CreateDefinition()
    {
        var roles = new WorkerRoles
        {
            Worker = "coder",
            Reviewer = "reviewer"
        };

        return new WorkflowDefinition
        {
            ExecutionUnit = "C2",
            PacketPaths = new WorkflowPacketPaths
            {
                Implementation = ".intent-cli/issues/C2/implementation.md",
                ReviewContext = ".intent-cli/issues/C2/review-context.md",
                Yaml = ".intent-cli/issues/C2/packet.yaml"
            },
            WorkerRoles = roles,
            DependencySnapshot = ["A1"],
            EntryConditions = ["A1 completed"],
            Steps = MvpWorkflowTemplate.CreateSteps(roles),
            SuccessSignal = "workflow render writes workflow artifact",
            ReviewMode = "deterministic-review",
            CompletionAction = "wait-for-deterministic-review"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-workflow-writer-tests-").FullName;

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
