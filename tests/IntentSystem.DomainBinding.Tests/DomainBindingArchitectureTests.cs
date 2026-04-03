using IntentSystem.DomainBinding.Serialization;

namespace IntentSystem.DomainBinding.Tests;

public sealed class DomainBindingArchitectureTests
{
    [Fact]
    public void SerializationInfrastructure_DoesNotExposeDomainBindingJsonOptionsAsPublicApi()
    {
        var optionsType = typeof(DomainBindingSerializer).Assembly
            .GetType("IntentSystem.DomainBinding.Serialization.DomainBindingJsonOptions");

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
