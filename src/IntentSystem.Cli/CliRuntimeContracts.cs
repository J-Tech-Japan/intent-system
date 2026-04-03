namespace IntentSystem.Cli;

internal static class CliRuntimeContracts
{
    public const string IntentCliDirectoryName = ".intent-cli";
    public const string ConfigFileName = "config.toml";
    public const string QueueStateFileName = "queue-state.json";
    public const string ProjectSectionName = "project";
    public const string DefaultDomainKey = "default_domain";
    public const string DomainKey = "domain";
    public const string WorkflowEngineKey = "workflow_engine";
    public const string ArtifactRootKey = "artifact_root";

    public static string GetIntentCliDirectoryPath(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        return Path.Combine(repoRoot, IntentCliDirectoryName);
    }

    public static string GetConfigPath(string repoRoot)
    {
        return Path.Combine(GetIntentCliDirectoryPath(repoRoot), ConfigFileName);
    }

    public static string GetQueueStatePath(string repoRoot)
    {
        return Path.Combine(GetIntentCliDirectoryPath(repoRoot), QueueStateFileName);
    }
}
