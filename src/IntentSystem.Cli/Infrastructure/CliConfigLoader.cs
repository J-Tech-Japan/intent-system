using IntentSystem.Cli.Models;
using Tomlyn;
using Tomlyn.Model;

namespace IntentSystem.Cli.Infrastructure;

internal static class CliConfigLoader
{
    public static CliConfig Load(string toml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toml);

        var model = TomlSerializer.Deserialize<TomlTable>(toml);
        if (model is not TomlTable rootTable)
        {
            throw new InvalidOperationException("CLI config payload did not deserialize to a TOML table.");
        }

        if (!rootTable.TryGetValue(CliRuntimeContracts.ProjectSectionName, out var projectSection)
            || projectSection is not TomlTable projectTable)
        {
            throw new InvalidOperationException("CLI config must contain a [project] section.");
        }

        return new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = GetRequiredString(projectTable, CliRuntimeContracts.DomainKey),
                WorkflowEngine = GetRequiredString(projectTable, CliRuntimeContracts.WorkflowEngineKey),
                ArtifactRoot = GetRequiredString(projectTable, CliRuntimeContracts.ArtifactRootKey)
            }
        };
    }

    public static CliConfig LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Load(File.ReadAllText(filePath));
    }

    private static string GetRequiredString(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"CLI config [project] section must contain '{key}'.");
        }

        if (value is not string textValue || string.IsNullOrWhiteSpace(textValue))
        {
            throw new InvalidOperationException(
                $"CLI config [project] value '{key}' must be a non-empty string.");
        }

        return textValue;
    }
}
