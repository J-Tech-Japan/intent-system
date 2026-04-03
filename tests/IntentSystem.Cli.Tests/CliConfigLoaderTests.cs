using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

public sealed class CliConfigLoaderTests
{
    [Fact]
    public void Load_GivenProjectSection_RestoresRuntimeConfig()
    {
        var toml = """
        [project]
        domain = "intent-system"
        workflow_engine = "intent-cli"
        artifact_root = ".intent-cli"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("intent-system", config.Project.Domain);
        Assert.Equal("intent-cli", config.Project.WorkflowEngine);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
    }

    [Fact]
    public void LoadFromFile_GivenConfigTomlPath_RestoresProjectSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var configPath = tempDirectory.CreateFile(
            ".intent-cli/config.toml",
            """
            [project]
            domain = "intent-system"
            workflow_engine = "intent-cli"
            artifact_root = ".intent-cli"
            """);

        var config = CliConfigLoader.LoadFromFile(configPath);

        Assert.Equal("intent-system", config.Project.Domain);
        Assert.Equal("intent-cli", config.Project.WorkflowEngine);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
    }

    [Fact]
    public void Load_GivenMissingProjectSection_ThrowsInvalidOperationException()
    {
        var toml = """
        [queue]
        file = ".intent-cli/queue-state.json"
        """;

        Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));
    }

    [Fact]
    public void Load_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var toml = """
        [project]
        domain = "intent-system"
        artifact_root = ".intent-cli"
        """;

        Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

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
