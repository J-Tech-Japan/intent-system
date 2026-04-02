using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor.Tests;

public sealed class SupervisorArchitectureTests
{
    [Fact]
    public void SerializationInfrastructure_DoesNotExposeSupervisorJsonOptionsAsPublicApi()
    {
        var optionsType = typeof(QueueStateSerializer).Assembly
            .GetType("IntentSystem.Supervisor.Serialization.SupervisorJsonOptions");

        Assert.NotNull(optionsType);
        Assert.False(optionsType!.IsPublic);
    }

    [Fact]
    public void TestSources_DoNotUseGivenWhenThenComments()
    {
        var testSourceDirectory = GetTestSourceDirectory();
        var sourceFiles = Directory.GetFiles(testSourceDirectory, "*.cs", SearchOption.TopDirectoryOnly);
        var bannedMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "// Given",
            "// When",
            "// Then"
        };

        var violations = sourceFiles
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { file, line, lineNumber = index + 1 }))
            .Where(entry => bannedMarkers.Contains(entry.line.Trim()))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.lineNumber}")
            .ToArray();

        Assert.Empty(violations);
    }

    private static string GetTestSourceDirectory()
    {
        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var directoryInfo = new DirectoryInfo(projectDirectory);
        var sourceDirectory = directoryInfo.FullName;

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Test source directory not found: {sourceDirectory}");
        }

        return sourceDirectory;
    }
}
