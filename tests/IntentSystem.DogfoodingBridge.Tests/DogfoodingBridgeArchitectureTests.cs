using IntentSystem.DogfoodingBridge.Serialization;

namespace IntentSystem.DogfoodingBridge.Tests;

public sealed class DogfoodingBridgeArchitectureTests
{
    [Fact]
    public void SerializationInfrastructure_DoesNotExposeDogfoodingBridgeJsonOptionsAsPublicApi()
    {
        var optionsType = typeof(DogfoodingBridgeSerializer).Assembly
            .GetType("IntentSystem.DogfoodingBridge.Serialization.DogfoodingBridgeJsonOptions");

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
        return new DirectoryInfo(projectDirectory).FullName;
    }
}
