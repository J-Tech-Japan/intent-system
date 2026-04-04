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

        if (TryCreateFromRootKeys(rootTable, out var rootConfig))
        {
            return rootConfig;
        }

        if (TryCreateFromProjectSection(rootTable, out var projectConfig))
        {
            return projectConfig;
        }

        throw new InvalidOperationException(
            $"CLI config must contain root keys '{CliRuntimeContracts.DefaultDomainKey}', " +
            $"'{CliRuntimeContracts.WorkflowEngineKey}', and '{CliRuntimeContracts.ArtifactRootKey}'.");
    }

    public static CliConfig LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Load(File.ReadAllText(filePath));
    }

    private static bool TryCreateFromRootKeys(TomlTable rootTable, out CliConfig config)
    {
        config = default!;

        if (!TryGetRequiredString(rootTable, CliRuntimeContracts.DefaultDomainKey, out var domain)
            || !TryGetRequiredString(rootTable, CliRuntimeContracts.WorkflowEngineKey, out var workflowEngine)
            || !TryGetRequiredString(rootTable, CliRuntimeContracts.ArtifactRootKey, out var artifactRoot))
        {
            return false;
        }

        var worktreeRoot = TryGetOptionalString(rootTable, CliRuntimeContracts.WorktreeRootKey)
            ?? CliRuntimeContracts.DefaultWorktreeRoot;

        config = CreateConfig(domain, workflowEngine, artifactRoot, worktreeRoot);
        return true;
    }

    private static bool TryCreateFromProjectSection(TomlTable rootTable, out CliConfig config)
    {
        config = default!;

        if (!rootTable.TryGetValue(CliRuntimeContracts.ProjectSectionName, out var projectSection)
            || projectSection is not TomlTable projectTable)
        {
            return false;
        }

        if (!TryGetRequiredString(projectTable, CliRuntimeContracts.DomainKey, out var domain)
            || !TryGetRequiredString(projectTable, CliRuntimeContracts.WorkflowEngineKey, out var workflowEngine)
            || !TryGetRequiredString(projectTable, CliRuntimeContracts.ArtifactRootKey, out var artifactRoot))
        {
            throw new InvalidOperationException(
                $"CLI config [project] section must contain '{CliRuntimeContracts.DomainKey}', " +
                $"'{CliRuntimeContracts.WorkflowEngineKey}', and '{CliRuntimeContracts.ArtifactRootKey}'.");
        }

        var worktreeRoot = TryGetOptionalString(projectTable, CliRuntimeContracts.WorktreeRootKey)
            ?? CliRuntimeContracts.DefaultWorktreeRoot;

        config = CreateConfig(domain, workflowEngine, artifactRoot, worktreeRoot);
        return true;
    }

    private static CliConfig CreateConfig(
        string domain,
        string workflowEngine,
        string artifactRoot,
        string worktreeRoot)
    {
        return new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = domain,
                WorkflowEngine = workflowEngine,
                ArtifactRoot = artifactRoot,
                WorktreeRoot = worktreeRoot
            }
        };
    }

    private static bool TryGetRequiredString(TomlTable table, string key, out string value)
    {
        value = string.Empty;

        if (!table.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        if (rawValue is not string textValue || string.IsNullOrWhiteSpace(textValue))
        {
            throw new InvalidOperationException(
                $"CLI config value '{key}' must be a non-empty string.");
        }

        value = textValue;
        return true;
    }

    private static string? TryGetOptionalString(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!table.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is not string textValue || string.IsNullOrWhiteSpace(textValue))
        {
            throw new InvalidOperationException(
                $"CLI config value '{key}' must be a non-empty string.");
        }

        return textValue;
    }
}
