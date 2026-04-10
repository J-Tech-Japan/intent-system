using System.Xml.Linq;

namespace IntentSystem.Cli.Tests;

public sealed class PackagedInvocationSmokeTests
{
    [Fact]
    public void CliProject_DeclaresDotNetToolPackagingMetadata()
    {
        var document = XDocument.Load(Path.Combine(GetSolutionRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj"));
        var propertyGroup = document.Root?
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal));

        Assert.NotNull(propertyGroup);
        Assert.Equal("true", GetPropertyValue(propertyGroup!, "PackAsTool"));
        Assert.Equal("intent-cli", GetPropertyValue(propertyGroup, "ToolCommandName"));
        Assert.Equal("intent-cli", GetPropertyValue(propertyGroup, "PackageId"));
        Assert.Equal("0.1.0", GetPropertyValue(propertyGroup, "Version"));
        Assert.Equal("README.md", GetPropertyValue(propertyGroup, "PackageReadmeFile"));
    }

    [Fact]
    public void Readme_DocumentsToolExecAndDnxPackagedInvocationPaths()
    {
        var readme = File.ReadAllText(Path.Combine(GetSolutionRoot(), "README.md"));

        Assert.Contains("dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -o .artifacts/packages", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet tool exec --yes --source .artifacts/packages --version 0.1.0 intent-cli project status", readme, StringComparison.Ordinal);
        Assert.Contains("dnx --yes --source .artifacts/packages --version 0.1.0 intent-cli project status", readme, StringComparison.Ordinal);
    }

    private static string? GetPropertyValue(XElement propertyGroup, string propertyName)
    {
        return propertyGroup.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.Ordinal))?
            .Value;
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
